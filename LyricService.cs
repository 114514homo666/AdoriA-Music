using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudMusicFloat {
 // A disk error must never discard successfully downloaded lyrics.
 public class AssetStore {
  public readonly string CacheDirectory,LegacyDirectory,LogFile;static readonly object logGate=new object();
  public AssetStore(string cacheDirectory,string legacyDirectory=null){CacheDirectory=cacheDirectory;LegacyDirectory=legacyDirectory;LogFile=Path.Combine(Path.GetDirectoryName(cacheDirectory),"diagnostic.log");}
  public static AssetStore ForApplication(){return new AssetStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AdoriA-Music","cache"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cache"));}
  public string Read(string name){foreach(string dir in new[]{CacheDirectory,LegacyDirectory}){if(String.IsNullOrEmpty(dir))continue;try{string file=Path.Combine(dir,name);if(File.Exists(file))return File.ReadAllText(file,Encoding.UTF8);}catch(Exception ex){Log("缓存读取 "+name+"："+ex.Message);}}return null;}
  public bool Write(string name,string text){string temporary=null;try{Directory.CreateDirectory(CacheDirectory);string target=Path.Combine(CacheDirectory,name);temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,text,Encoding.UTF8);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);return true;}catch(Exception ex){Log("缓存写入 "+name+"："+ex.Message);return false;}finally{if(temporary!=null)try{File.Delete(temporary);}catch{}}}
  public void Log(string text){try{lock(logGate){Directory.CreateDirectory(Path.GetDirectoryName(LogFile));if(File.Exists(LogFile)&&new FileInfo(LogFile).Length>1024*1024)File.WriteAllText(LogFile,"");File.AppendAllText(LogFile,DateTime.Now.ToString("s")+" "+text+Environment.NewLine,Encoding.UTF8);}}catch{}}
 }
 public class LyricFailure:Exception {public readonly bool Retryable,Recheck;public LyricFailure(string message,bool retryable,bool recheck=false):base(message){Retryable=retryable;Recheck=recheck;}}
 public class LyricResult {public List<LyricLine> Lines=new List<LyricLine>();public string Message="",Details="";public bool Retryable,Recheck;}
 public class LyricRetry {
  public DateTime Next=DateTime.MaxValue;int attempts,rechecks;
  public void Reset(){attempts=rechecks=0;Next=DateTime.MaxValue;}
  public void Complete(bool retryable,DateTime now,bool recheck=false){
   if(retryable){int[] seconds={5,15,45,120};Next=now.AddSeconds(seconds[Math.Min(attempts++,seconds.Length-1)]);return;}
   if(recheck){int[] seconds={2,5,12};Next=rechecks<seconds.Length?now.AddSeconds(seconds[rechecks++]):DateTime.MaxValue;return;}
   Reset();
  }
  public bool Due(DateTime now){return now>=Next;}
 }
 public static class LyricHttp {
  public static Task<string> Request(string url,string body){return Request(url,body,CancellationToken.None);}
  public static async Task<string> Request(string url,string body,CancellationToken cancellationToken){
   cancellationToken.ThrowIfCancellationRequested();
   var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="Mozilla/5.0 AdoriA-Music/1.2.1";request.Referer=new Uri(url).Host.EndsWith("qq.com",StringComparison.OrdinalIgnoreCase)?"https://y.qq.com/":"https://music.163.com/";
   request.Timeout=12000;request.ReadWriteTimeout=12000;request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;request.ServicePoint.Expect100Continue=false;
   using(var timeout=new CancellationTokenSource(12000))using(var combined=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token,cancellationToken))using(combined.Token.Register(()=>request.Abort()))try{
    if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(body);request.Method="POST";request.ContentType=body.TrimStart().StartsWith("{")?"application/json; charset=utf-8":"application/x-www-form-urlencoded; charset=utf-8";request.ContentLength=bytes.Length;using(var stream=await request.GetRequestStreamAsync())await stream.WriteAsync(bytes,0,bytes.Length);}
    using(var response=await request.GetResponseAsync())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream,Encoding.UTF8))return await reader.ReadToEndAsync();
   }catch(WebException){cancellationToken.ThrowIfCancellationRequested();throw;}
  }
  public static Dictionary<string,object> ReadJson(string response){
   string text=(response??"").Trim().TrimStart('\ufeff');if(text.StartsWith("{"))return Json.Read(text);
   int start=text.IndexOf('('),end=text.LastIndexOf(')');if(start>0&&end>start&&System.Text.RegularExpressions.Regex.IsMatch(text.Substring(0,start),@"^[\w.$]+$"))return Json.Read(text.Substring(start+1,end-start-1));
   throw new LyricFailure("歌词服务返回了无效数据",true);
  }
 }
 public class LyricService {
  readonly AssetStore store;readonly Func<string,string,Task<string>> request;
  public LyricService(AssetStore assets,Func<string,string,Task<string>> transport=null){store=assets;request=transport;}
  public async Task<LyricResult> Load(Song song,bool refresh=false,CancellationToken cancellationToken=default(CancellationToken)){
   var result=new LyricResult();try{
    cancellationToken.ThrowIfCancellationRequested();Func<string,string,Task<string>> fetch=request??((url,body)=>LyricHttp.Request(url,body,cancellationToken));
    string custom=store.Read(song.CacheKey+".lrc");if(custom!=null){var imported=Lyrics.Parse(custom);if(imported.Count>0){result.Lines=imported;return result;}}
    if(song.Source=="QQMusic")result.Lines=await QQMusicAssets.LoadLyrics(song,store,fetch,refresh);
    else{
     string name=song.CacheKey+".lyrics.json",json=refresh?null:store.Read(name);
     if(json!=null)try{var cached=Lyrics.FromJson(json);if(cached.Count>0){result.Lines=cached;return result;}}catch(Exception ex){store.Log("忽略损坏歌词缓存："+ex.Message);}
     json=await fetch("https://music.163.com/api/song/lyric?id="+Uri.EscapeDataString(song.Id)+"&lv=-1&kv=-1&tv=-1",null);
     if(Json.Num(LyricHttp.ReadJson(json),"code")!=200)throw new LyricFailure("网易云歌词服务暂不可用",true);
     result.Lines=Lyrics.FromJson(json);if(result.Lines.Count>0)store.Write(name,json);
    }
    if(result.Lines.Count==0){result.Message="该歌曲暂无同步歌词";result.Recheck=true;}
   }catch(OperationCanceledException){throw;}catch(Exception ex){
    var failure=ex as LyricFailure;result.Retryable=failure==null||failure.Retryable;result.Recheck=failure!=null&&failure.Recheck;var network=ex as WebException;
    result.Message=failure!=null?failure.Message:network!=null?"歌词网络连接失败":"歌词获取失败";
    if(network!=null){if(network.Status==WebExceptionStatus.Timeout||network.Status==WebExceptionStatus.RequestCanceled)result.Message="歌词请求超时";else if(network.Status==WebExceptionStatus.TrustFailure||network.Status==WebExceptionStatus.SecureChannelFailure)result.Message="歌词安全连接失败";}
    if(network!=null&&network.Response is HttpWebResponse){int code=(int)((HttpWebResponse)network.Response).StatusCode;result.Message="歌词请求失败（HTTP "+code+"）";if(code>=400&&code<500&&code!=408&&code!=429)result.Retryable=false;network.Response.Close();}
    result.Details=song.Source+" · "+song.Title+" / "+song.Artist+" · "+ex.ToString();store.Log(result.Details);
   }return result;
  }
 }
}
