using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudMusicFloat;

class LyricsChecks {
 static Dictionary<string,object> checks=new Dictionary<string,object>();static string root;
 static Song Song(){return new Song{Source="QQMusic",Id="test",Title="龙卷风",Artist="G.E.M.邓紫棋",Album="龙卷风",Duration=248.888};}
 static Dictionary<string,object> Candidate(string title="龙卷风",string artist="G.E.M.邓紫棋"){return new Dictionary<string,object>{{"mid","001test"},{"title",title},{"interval",249},{"singer",new object[]{new Dictionary<string,object>{{"name",artist}}}},{"album",new Dictionary<string,object>{{"title","龙卷风"}}}};}
 static string Search(params object[] candidates){return Json.Write(new{code=0,req_1=new{code=0,data=new{body=new{song=new{list=candidates}}}}});}
 static string LyricsJson(){return Json.Write(new{code=0,lyric="[00:01.00]Hello\n[00:02.00]World",trans="[00:01.00]你好"});}
 static void Check(string name,bool pass){checks[name]=pass;if(!pass)throw new Exception("FAIL "+name);}
 static AssetStore Store(string name){return new AssetStore(Path.Combine(root,name,"cache"));}
 static void Main(string[] args){root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);try{if(args.Contains("--live"))Live().GetAwaiter().GetResult();else Run().GetAwaiter().GetResult();checks["passed"]=true;}catch(Exception ex){checks["exception"]=ex.ToString();checks["passed"]=false;Environment.ExitCode=1;}string json=Json.Write(checks);File.WriteAllText(Path.Combine(root,"result.json"),json,Encoding.UTF8);Console.WriteLine(json);}
 static async Task Run(){
  var sample=Song();int requests=0;Func<string,string,Task<string>> good=(u,b)=>{requests++;return Task.FromResult(u.Contains("musicu.fcg")?Search(Candidate()):LyricsJson());};
  var cold=Store("cold");var service=new LyricService(cold,good);var result=await service.Load(sample);Check("coldFetch",result.Lines.Count==2&&requests==2&&result.Lines[0].Translation=="你好");
  requests=0;result=await service.Load(sample);Check("warmCache",result.Lines.Count==2&&requests==0);
  requests=0;result=await service.Load(sample,true);Check("manualRefreshBypassesNetworkCache",result.Lines.Count==2&&requests==2);
  File.WriteAllText(Path.Combine(cold.CacheDirectory,sample.CacheKey+".qq-lyrics.json"),"{broken");requests=0;result=await service.Load(sample);Check("corruptCacheFetchesAgain",result.Lines.Count==2&&requests==1);
  var blocked=Store("blocked");Directory.CreateDirectory(Path.GetDirectoryName(blocked.CacheDirectory));File.WriteAllText(blocked.CacheDirectory,"occupy directory path");requests=0;result=await new LyricService(blocked,good).Load(sample);Check("unwritableCacheKeepsLyrics",result.Lines.Count==2&&!result.Retryable&&requests==2);
  var legacy=Store("legacy");Directory.CreateDirectory(legacy.CacheDirectory);File.WriteAllText(Path.Combine(legacy.CacheDirectory,sample.CacheKey+".lrc"),"[00:01.00]用户歌词");requests=0;result=await new LyricService(new AssetStore(Path.Combine(root,"new-cache"),legacy.CacheDirectory),good).Load(sample,true);Check("legacyImportedLrcPreserved",result.Lines.First().Text=="用户歌词"&&requests==0);
  requests=0;var fallback=Store("query");result=await new LyricService(fallback,(u,b)=>{requests++;return Task.FromResult(u.Contains("musicu.fcg")?(requests==1?Search():Search(Candidate())):LyricsJson());}).Load(sample);Check("titleQueryFallback",result.Lines.Count==2&&requests==3);
  requests=0;result=await new LyricService(Store("post"),(u,b)=>{requests++;if(!u.Contains("musicu.fcg")&&b==null)throw new WebException("timeout",WebExceptionStatus.Timeout);return Task.FromResult(u.Contains("musicu.fcg")?Search(Candidate()):"MusicJsonCallback("+LyricsJson()+");");}).Load(sample);Check("postFallbackAndJsonp",result.Lines.Count==2&&requests==3);
  result=await new LyricService(Store("wrong"),(u,b)=>Task.FromResult(Search(Candidate("龙卷风","其他歌手")))).Load(sample);Check("wrongArtistNeverLoadsLyrics",result.Lines.Count==0&&!result.Retryable&&result.Message.Contains("未匹配"));
  requests=0;var transientService=new LyricService(Store("transient"),(u,b)=>{if(++requests==1)throw new WebException("timeout",WebExceptionStatus.Timeout);return Task.FromResult(u.Contains("musicu.fcg")?Search(Candidate()):LyricsJson());});result=await transientService.Load(sample);Check("transientFailureExplained",result.Retryable&&result.Message.Contains("超时"));result=await transientService.Load(sample);Check("sameSongRecovers",result.Lines.Count==2);
  result=await new LyricService(Store("empty"),(u,b)=>Task.FromResult(u.Contains("musicu.fcg")?Search(Candidate()):"{\"code\":0,\"lyric\":\"\"}")).Load(sample);Check("noLyricsIsNotNetworkFailure",!result.Retryable&&result.Message.Contains("暂无同步歌词"));Check("emptyResponseNotCached",!File.Exists(Path.Combine(Store("empty").CacheDirectory,sample.CacheKey+".qq-lyrics.json")));
  result=await new LyricService(Store("error"),(u,b)=>Task.FromResult("{\"code\":-1}")).Load(sample);Check("searchErrorNotSilenced",result.Retryable&&result.Message.Contains("搜索"));
  sample.Duration=0;Check("unknownDurationAllowed",QQMusicAssets.PickMatch(sample,new object[]{Candidate()})!=null);sample.Duration=999;Check("wrongDurationRejected",QQMusicAssets.PickMatch(sample,new object[]{Candidate()})==null);sample=Song();Check("versionMismatchRejected",QQMusicAssets.PickMatch(sample,new object[]{Candidate("龙卷风 (Live)")})==null);
  sample.Title="Thunderstruck";sample.Artist="AC/DC";sample.Duration=0;Check("artistWithSlash",QQMusicAssets.PickMatch(sample,new object[]{Candidate("Thunderstruck","AC/DC")})!=null);sample.Artist="G.E.M.邓紫棋 / 周杰伦";sample.Title="龙卷风";Check("missingCoartistRejected",QQMusicAssets.PickMatch(sample,new object[]{Candidate()})==null);sample.Artist="G.E.M.";Check("partialArtistRejected",QQMusicAssets.PickMatch(sample,new object[]{Candidate()})==null);
  var culture=Thread.CurrentThread.CurrentCulture;try{Thread.CurrentThread.CurrentCulture=new CultureInfo("fr-FR");Check("invariantJsonNumbers",Math.Abs(Json.Num(new Dictionary<string,object>{{"n","248.888"}},"n")-248.888)<.0001&&Math.Abs(Json.Num(new Dictionary<string,object>{{"n",248.888}},"n")-248.888)<.0001);}finally{Thread.CurrentThread.CurrentCulture=culture;}
  var retry=new LyricRetry();DateTime now=DateTime.UtcNow;foreach(int delay in new[]{5,15,45,120,120}){retry.Complete(true,now);Check("retryDelay"+delay+"-"+checks.Count,(retry.Next-now).TotalSeconds==delay&&!retry.Due(now)&&retry.Due(now.AddSeconds(delay)));}retry.Complete(false,now);Check("successStopsRetry",!retry.Due(now.AddYears(1)));
  using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();bool stopped=false;requests=0;try{await new LyricService(Store("cancel"),good).Load(Song(),false,cancellation.Token);}catch(OperationCanceledException){stopped=true;}Check("preCancelledRequestStops",stopped&&requests==0);}
  var netEase=new Song{Source="NetEase",Id="test",Title="test"};result=await new LyricService(blocked,(u,b)=>Task.FromResult("{\"code\":200,\"lrc\":{\"lyric\":\"[00:01.00]网易云歌词\"}}")).Load(netEase);Check("neteaseCacheFailureKeepsLyrics",result.Lines.Count==1&&result.Lines[0].Text=="网易云歌词");
  var bounded=new LyricRetry();foreach(int delay in new[]{2,5,12}){bounded.Complete(false,now,true);Check("boundedRecheckDelay"+delay,(bounded.Next-now).TotalSeconds==delay);}bounded.Complete(false,now,true);bounded.Complete(false,now,true);Check("boundedRechecksStayStopped",bounded.Next==DateTime.MaxValue);bounded.Reset();bounded.Complete(false,now,true);Check("newSongRestoresRecheckBudget",bounded.Next==now.AddSeconds(2));bounded.Complete(false,now);Check("successfulLyricsStopRechecks",bounded.Next==DateTime.MaxValue);
  var durationCache=Store("duration-cache");var correct=Song();durationCache.Write(correct.CacheKey+".qq-match.json",Json.Write(new{mid="wrong-version",title=correct.Title,artist=correct.Artist,duration=500}));durationCache.Write(correct.CacheKey+".qq-lyrics.json",Json.Write(new{code=0,lyric="[00:01.00]wrong cached lyrics"}));requests=0;
  result=await new LyricService(durationCache,good).Load(correct);Check("staleDurationCacheIsRevalidated",result.Lines.Count==2&&result.Lines[0].Text=="Hello"&&requests==2);
  var frozen=correct.Snapshot();correct.Duration=999;Check("lyricRequestHasIndependentSongSnapshot",Math.Abs(frozen.Duration-248.888)<.001);
  await HttpCancellation();
 }
 static async Task HttpCancellation(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();try{int port=((IPEndPoint)listener.LocalEndpoint).Port;using(var cancellation=new CancellationTokenSource()){var watch=System.Diagnostics.Stopwatch.StartNew();var request=LyricHttp.Request("http://127.0.0.1:"+port+"/test",null,cancellation.Token);using(var client=await listener.AcceptTcpClientAsync()){cancellation.Cancel();bool cancelled=false;try{await request;}catch(OperationCanceledException){cancelled=true;}Check("inflightHttpCancellation",cancelled&&watch.Elapsed.TotalSeconds<3);}}}finally{listener.Stop();}}
 static async Task Live(){
  ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;Song sample=null;using(var reader=new SystemMediaReader()){var metadata=reader.Read("QQMusic");if(metadata!=null&&!String.IsNullOrWhiteSpace(metadata.Title))sample=new Song{Source="QQMusic",Id=QQMusicAssets.Identity(metadata.Title,metadata.Artist,metadata.Album),Title=metadata.Title,Artist=metadata.Artist,Album=metadata.Album,Duration=metadata.Duration};}
  if(sample==null)sample=Song();checks["title"]=sample.Title;checks["artist"]=sample.Artist;int requests=0;
  var service=new LyricService(Store("live"),async(u,b)=>{requests++;return await LyricHttp.Request(u,b);});var result=await service.Load(sample,true);checks["lyricLines"]=result.Lines.Count;checks["requests"]=requests;checks["message"]=result.Message;checks["details"]=result.Details;Check("liveColdFetch",result.Lines.Count>0&&requests>=2);
 }
}
