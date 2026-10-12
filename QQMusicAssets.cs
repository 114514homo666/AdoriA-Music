using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CloudMusicFloat {
 public static class QQMusicAssets {
  public static string Identity(string title,string artist,string album){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(title+"\n"+artist+"\n"+album))).Replace("-","").Substring(0,24);}
  static string Normalize(string text){return Regex.Replace(WebUtility.HtmlDecode(text??"").Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"[^\p{L}\p{N}]","");}
  public static Dictionary<string,object> PickMatch(Song song,object[] candidates){
   string wanted=Normalize(song.Title),wholeArtist=Normalize(song.Artist);if(wanted.Length==0)return null;var wantedArtists=Regex.Split(song.Artist??"",@"\s*[/、;&,，]\s*").Select(Normalize).Where(a=>a.Length>0).ToArray();Dictionary<string,object> best=null;double bestScore=Double.MinValue;
   foreach(var value in candidates){var item=value as Dictionary<string,object>;if(item==null||Normalize(Json.Str(item,"title"))!=wanted)continue;
    var singers=Json.Arr(item,"singer").Select(s=>Normalize(Json.Str(s as Dictionary<string,object>,"name"))).Where(s=>s.Length>0).ToArray();
    if(wantedArtists.Length==0||!(singers.Contains(wholeArtist)||wantedArtists.All(a=>singers.Contains(a))))continue;
    double duration=Json.Num(item,"interval"),difference=(duration>0&&song.Duration>0)?Math.Abs(duration-song.Duration):0;if(difference>6)continue;
    double score=100-difference;if(Normalize(Json.Str(Json.Obj(item,"album"),"title"))==Normalize(song.Album))score+=20;
    if(score>bestScore){bestScore=score;best=item;}
   }return best;
  }
  public static Task<List<LyricLine>> LoadLyrics(Song song,string cache){return LoadLyrics(song,new AssetStore(cache),LyricHttp.Request,false);}
  public static async Task<List<LyricLine>> LoadLyrics(Song song,AssetStore store,Func<string,string,Task<string>> request,bool refresh){
   var transport=request;request=async(url,body)=>{try{return await transport(url,body);}catch(OperationCanceledException){throw;}catch(Exception ex){store.Log("QQ 请求失败 "+url+"："+ex.GetType().Name+" / "+ex.Message);throw;}};
   string lyricName=song.CacheKey+".qq-lyrics.json",cached=refresh?null:store.Read(lyricName);
   if(cached!=null)try{var lines=Parse(cached);if(lines.Count>0)return lines;}catch(Exception ex){store.Log("忽略损坏 QQ 歌词缓存："+ex.Message);}
   if(String.IsNullOrWhiteSpace(song.Artist))throw new LyricFailure("QQ 音乐未提供歌手信息",true);
   string matchName=song.CacheKey+".qq-match.json",mid="",match=refresh?null:store.Read(matchName);
   if(match!=null)try{var value=Json.Read(match);if(Json.Str(value,"title")==song.Title&&Json.Str(value,"artist")==song.Artist)mid=Json.Str(value,"mid");}catch{}
   if(String.IsNullOrEmpty(mid)){
    foreach(string keyword in new[]{song.Title+" "+song.Artist,song.Title}){
     var parameters=new Dictionary<string,object>{{"num_per_page",20},{"page_num",1},{"query",keyword},{"search_type",0}};
     var search=new Dictionary<string,object>{{"req_1",new Dictionary<string,object>{{"method","DoSearchForQQMusicDesktop"},{"module","music.search.SearchCgiService"},{"param",parameters}}}};
     var value=LyricHttp.ReadJson(await request("https://u.y.qq.com/cgi-bin/musicu.fcg",Json.Write(search)));var part=Json.Obj(value,"req_1");
     if(Json.Num(value,"code")!=0||part==null||Json.Num(part,"code")!=0)throw new LyricFailure("QQ 歌曲搜索暂不可用",true);
     var body=Json.Obj(Json.Obj(part,"data"),"body");var selected=PickMatch(song,Json.Arr(Json.Obj(body,"song"),"list"));
     if(selected==null){store.Log("QQ 未匹配 "+song.Title+" / "+song.Artist+" / "+song.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture)+" 秒；候选："+Json.Write(Json.Arr(Json.Obj(body,"song"),"list").Select(v=>{var item=v as Dictionary<string,object>;return new{title=Json.Str(item,"title"),singers=Json.Arr(item,"singer").Select(a=>Json.Str(a as Dictionary<string,object>,"name")).ToArray(),seconds=Json.Num(item,"interval")};}).ToArray()));continue;}mid=Json.Str(selected,"mid");if(mid.Length>0)break;
    }
    if(String.IsNullOrEmpty(mid))throw new LyricFailure("未匹配到这首 QQ 歌曲",false);
    store.Write(matchName,Json.Write(new Dictionary<string,object>{{"mid",mid},{"title",song.Title},{"artist",song.Artist}}));
   }
   string endpoint="https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg";
   string parametersText="songmid="+Uri.EscapeDataString(mid)+"&format=json&nobase64=1&g_tk=5381&loginUin=0&hostUin=0&inCharset=utf8&outCharset=utf-8&notice=0&platform=yqq&needNewCode=0";
   Exception first=null;
   for(int attempt=0;attempt<2;attempt++)try{
    string response=await request(attempt==0?endpoint+"?"+parametersText:endpoint,attempt==0?null:parametersText);
    var value=LyricHttp.ReadJson(response);if(Json.Num(value,"code")!=0)throw new LyricFailure("QQ 歌词服务暂不可用（"+Json.Str(value,"code")+"）",true);
    if(!value.ContainsKey("lyric"))throw new LyricFailure("QQ 歌词服务返回了无效数据",true);
    var lines=Parse(response);if(lines.Count>0)store.Write(lyricName,response);return lines;
   }catch(OperationCanceledException){throw;}catch(Exception ex){if(attempt==1){store.Log("QQ 歌词首次请求："+first);throw;}var network=ex as WebException;if(network!=null&&network.Response!=null)network.Response.Close();first=ex;}
   return new List<LyricLine>();
  }
  public static List<LyricLine> Parse(string json){var value=LyricHttp.ReadJson(json);var lines=Lyrics.Parse(Decode(Json.Str(value,"lyric")));var translated=Lyrics.Parse(Decode(Json.Str(value,"trans")));foreach(var line in lines){var match=translated.FirstOrDefault(t=>Math.Abs(t.Time-line.Time)<.08);if(match!=null)line.Translation=match.Text;}return lines;}
  static string Decode(string value){value=WebUtility.HtmlDecode(value??"");if(value.Contains("["))return value;try{return WebUtility.HtmlDecode(Encoding.UTF8.GetString(Convert.FromBase64String(value)));}catch{return value;}}
 }
}
