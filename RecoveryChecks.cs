using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;

namespace CloudMusicFloat {
 public partial class Overlay {
  static Song RecoverySong(string source,string id,double duration){return new Song{Source=source,Id=id,Title="测试歌曲",Artist="测试歌手",Album="测试专辑",Duration=duration};}
  async Task VerifyRecovery(Dictionary<string,object> checks,string dir){
   string search=Json.Write(new{code=0,req_1=new{code=0,data=new{body=new{song=new{list=new[]{new{mid="recovery",title="测试歌曲",interval=249,singer=new[]{new{name="测试歌手"}}}}}}}}});
   string emptySearch=Json.Write(new{code=0,req_1=new{code=0,data=new{body=new{song=new{list=new object[0]}}}}});
   string lyric="{\"code\":0,\"lyric\":\"[00:01.00]恢复后的歌词\"}";
   Func<AssetStore> cache=()=>new AssetStore(Path.Combine(dir,Guid.NewGuid().ToString("N"),"cache"));
   SwitchPlayer("QQMusic");int requests=0;
   lyricService=new LyricService(cache(),(url,body)=>{requests++;return Task.FromResult(url.Contains("musicu.fcg")?search:lyric);});
   var stale=RecoverySong("QQMusic","duration",500);
   await AcceptSongSnapshot(stale,null,sourceRevision,"QQMusic");await SettleUi(()=>!lyricBusy);
   checks["qqStaleDurationInitiallyRejected"]=lyrics.Count==0&&lyricState.Contains("未匹配")&&lyricRetry.Next!=DateTime.MaxValue;
   await AcceptSongSnapshot(RecoverySong("QQMusic","duration",249),null,sourceRevision,"QQMusic");await SettleUi(()=>!lyricBusy);
   checks["qqDurationCorrectionRecoversWithoutManualRetry"]=lyrics.Count==1&&lyrics[0].Text=="恢复后的歌词"&&song.Duration==249;
   checks["songSnapshotIsNotMutated"]=stale.Duration==500;
   int before=requests;
   await AcceptSongSnapshot(RecoverySong("QQMusic","duration",249.2),null,sourceRevision,"QQMusic");
   checks["sameSongPollDoesNotRepeatRequests"]=requests==before&&lyrics.Count==1;
   await AcceptSongSnapshot(RecoverySong("QQMusic","duration",251),null,sourceRevision,"QQMusic");
   checks["smallTimelineJitterDoesNotReloadLyrics"]=requests==before&&lyrics.Count==1;

   SwitchPlayer("NetEase");SwitchPlayer("QQMusic");int searches=0;
   lyricService=new LyricService(cache(),(url,body)=>Task.FromResult(url.Contains("musicu.fcg")?(++searches<=2?emptySearch:search):lyric));
   await AcceptSongSnapshot(RecoverySong("QQMusic","transient-search",249),null,sourceRevision,"QQMusic");await SettleUi(()=>!lyricBusy);
   checks["qqEmptySearchSchedulesBoundedRecheck"]=lyrics.Count==0&&lyricRetry.Next!=DateTime.MaxValue;
   RetryCurrentLyricsIfDue(lyricRetry.Next.AddMilliseconds(1));await SettleUi(()=>!lyricBusy);
   checks["qqSearchAutomaticallyRecovers"]=lyrics.Count==1&&searches==3&&lyricRetry.Next==DateTime.MaxValue;

   SwitchPlayer("NetEase");SwitchPlayer("QQMusic");int lyricCalls=0;
   lyricService=new LyricService(cache(),(url,body)=>Task.FromResult(url.Contains("musicu.fcg")?search:(++lyricCalls==1?"{\"code\":0,\"lyric\":\"\"}":lyric)));
   await AcceptSongSnapshot(RecoverySong("QQMusic","transient-empty",249),null,sourceRevision,"QQMusic");await SettleUi(()=>!lyricBusy);
   RetryCurrentLyricsIfDue(lyricRetry.Next.AddMilliseconds(1));await SettleUi(()=>!lyricBusy);
   checks["qqEmptyLyricsAutomaticallyRecover"]=lyrics.Count==1&&lyricCalls==2;

   SwitchPlayer("NetEase");SwitchPlayer("QQMusic");int delayedCalls=0;var delayed=new TaskCompletionSource<string>();
   lyricService=new LyricService(cache(),(url,body)=>url.Contains("musicu.fcg")?(++delayedCalls==1?delayed.Task:Task.FromResult(search)):Task.FromResult(lyric));
   await AcceptSongSnapshot(RecoverySong("QQMusic","inflight",500),null,sourceRevision,"QQMusic");
   checks["qqOldMetadataRequestIsInFlight"]=lyricBusy;
   await AcceptSongSnapshot(RecoverySong("QQMusic","inflight",249),null,sourceRevision,"QQMusic");await SettleUi(()=>!lyricBusy);
   delayed.SetResult(emptySearch);await Task.Delay(30);
   checks["lateSameKeyFailureCannotOverwriteRecovery"]=lyrics.Count==1&&lyrics[0].Text=="恢复后的歌词"&&!lyricBusy&&lyricRetry.Next==DateTime.MaxValue;

   SwitchPlayer("NetEase");int netEaseCalls=0;
   lyricService=new LyricService(cache(),(url,body)=>Task.FromResult(++netEaseCalls==1?"{\"code\":200,\"lrc\":{\"lyric\":\"\"}}":"{\"code\":200,\"lrc\":{\"lyric\":\"[00:01.00]网易云已恢复\"}}"));
   await AcceptSongSnapshot(RecoverySong("NetEase","transient-empty",200),null,sourceRevision,"NetEase");await SettleUi(()=>!lyricBusy);
   checks["neteaseEmptyLyricsScheduleRecheck"]=lyrics.Count==0&&lyricRetry.Next!=DateTime.MaxValue;
   RetryCurrentLyricsIfDue(lyricRetry.Next.AddMilliseconds(1));await SettleUi(()=>!lyricBusy);
   checks["neteaseEmptyLyricsAutomaticallyRecover"]=lyrics.Count==1&&lyrics[0].Text=="网易云已恢复"&&netEaseCalls==2;

   SwitchPlayer("QQMusic");SwitchPlayer("NetEase");netEaseCalls=0;
   lyricService=new LyricService(cache(),(url,body)=>Task.FromResult(++netEaseCalls==1?"{\"code\":200,\"lrc\":{\"lyric\":\"\"}}":"{\"code\":200,\"lrc\":{\"lyric\":\"[00:01.00]资料补全后恢复\"}}"));
   await AcceptSongSnapshot(RecoverySong("NetEase","metadata",0),null,sourceRevision,"NetEase");await SettleUi(()=>!lyricBusy);
   await AcceptSongSnapshot(RecoverySong("NetEase","metadata",200),null,sourceRevision,"NetEase");await SettleUi(()=>!lyricBusy);
   checks["neteaseMetadataCorrectionRecovers"]=lyrics.Count==1&&lyrics[0].Text=="资料补全后恢复"&&netEaseCalls==2;

   SwitchPlayer("QQMusic");searches=0;
   lyricService=new LyricService(cache(),(url,body)=>{searches++;return Task.FromResult(emptySearch);});
   await AcceptSongSnapshot(RecoverySong("QQMusic","permanent",249),null,sourceRevision,"QQMusic");await SettleUi(()=>!lyricBusy);
   for(int i=0;i<3;i++){RetryCurrentLyricsIfDue(lyricRetry.Next.AddMilliseconds(1));await SettleUi(()=>!lyricBusy);}
   checks["unmatchedSongStopsAfterThreeRechecks"]=lyricRetry.Next==DateTime.MaxValue&&lyrics.Count==0&&searches==8;
   before=searches;RetryCurrentLyricsIfDue(DateTime.UtcNow.AddDays(1));
   checks["boundedRecheckDoesNotLoopForever"]=searches==before;
   SwitchPlayer("NetEase");
  }
  void VerifyCursorProtection(Dictionary<string,object> checks){
   var visible=new CursorObservation{Valid=true,ExternalForeground=true,Flags=1,Cursor=(IntPtr)123};
   var hiddenCount=visible;hiddenCount.Flags=0;
   var hiddenShape=visible;hiddenShape.Cursor=IntPtr.Zero;
   var suppressed=visible;suppressed.Flags=2;
   var own=hiddenCount;own.ExternalForeground=false;
   checks["hiddenCursorCountProtects"]=CursorGuard.ShouldBlock(hiddenCount,false);
   checks["nullCursorShapeProtects"]=CursorGuard.ShouldBlock(hiddenShape,false);
   checks["suppressedCursorProtects"]=CursorGuard.ShouldBlock(suppressed,false);
   checks["visibleCursorAllowsInteraction"]=!CursorGuard.ShouldBlock(visible,true);
   checks["ownForegroundAllowsInteraction"]=!CursorGuard.ShouldBlock(own,true);
   checks["cursorReadFailurePreservesProtection"]=CursorGuard.ShouldBlock(new CursorObservation(),true)&&!CursorGuard.ShouldBlock(new CursorObservation(),false);
   var capturedVisible=visible;capturedVisible.ExternalCapture=true;
   checks["foregroundCaptureProtectsEvenIfGlobalCursorShows"]=CursorGuard.ShouldBlock(capturedVisible,false);
   capturedVisible.ExternalCapture=false;
   checks["releasedCaptureAndVisibleCursorAllowInteraction"]=!CursorGuard.ShouldBlock(capturedVisible,true);
   capturedVisible.ExternalCapture=true;capturedVisible.ExternalForeground=false;
   checks["ownCaptureDoesNotLockOverlay"]=!CursorGuard.ShouldBlock(capturedVisible,true);
   var observation=visible;long initial;int blockedEvents=0;IntPtr handle=new WindowInteropHelper(this).Handle;
   using(var guard=new CursorGuard(this,()=>blockedEvents++,()=>observation)){
    initial=guard.ExtendedStyle;observation=hiddenCount;bool handled=false;
    IntPtr hit=guard.Hook(handle,0x0084,IntPtr.Zero,IntPtr.Zero,ref handled);
    checks["hiddenCursorSynchronouslySkipsHitTest"]=guard.Blocked&&handled&&hit==(IntPtr)(-1);
    checks["hiddenCursorEnablesLayeredPassthrough"]=((guard.ExtendedStyle&0x20)!=0)&&((guard.ExtendedStyle&0x80000)!=0)&&((guard.ExtendedStyle&0x08000000)!=0);
    handled=false;IntPtr cursor=guard.Hook(handle,0x0020,handle,IntPtr.Zero,ref handled);
    checks["hiddenCursorStopsNativeCursorReplacement"]=handled&&cursor==(IntPtr)1;
    handled=false;IntPtr activate=guard.Hook(handle,0x0021,IntPtr.Zero,IntPtr.Zero,ref handled);
    checks["hiddenCursorCannotActivateOverlay"]=handled&&activate==(IntPtr)4;
    var query=new QueryCursorEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.QueryCursorEvent,Cursor=Cursors.Hand};
    playButton.RaiseEvent(query);
    checks["hiddenCursorOverridesWpfHandQuery"]=query.Handled&&query.Cursor==Cursors.None;
    observation=visible;guard.Refresh();
    checks["revealedCursorRestoresInteractionAndStyles"]=!guard.Blocked&&guard.ExtendedStyle==initial&&blockedEvents==1;
    handled=false;guard.Hook(handle,0x0084,IntPtr.Zero,IntPtr.Zero,ref handled);
    checks["revealedCursorUsesNormalHitTest"]=!handled;
    observation=hiddenShape;guard.Refresh();
    checks["cursorProtectionCanBeReentered"]=guard.Blocked&&blockedEvents==2;
   }
   using(var restored=new CursorGuard(this,null,()=>visible))checks["cursorGuardDisposalRestoresOwnedStyles"]=restored.ExtendedStyle==initial;
   Func<System.Windows.Window> windowFactory=()=>new System.Windows.Window{Width=240,Height=100,Left=-12000,Top=-12000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=System.Windows.WindowStyle.None,AllowsTransparency=true,Background=System.Windows.Media.Brushes.Transparent};
   var early=windowFactory();new WindowInteropHelper(early).EnsureHandle();
   using(var lifetime=new CursorGuardLifetime(early,null,()=>visible)){
    checks["guardAttachesAfterSourceInitializedAlreadyFired"]=lifetime.Guard!=null&&lifetime.Guard.IsAttached;
    var first=lifetime.Guard;lifetime.Ensure();lifetime.Ensure();early.Show();
    checks["guardLifecycleIsIdempotent"]=lifetime.InitializationCount==1&&Object.ReferenceEquals(first,lifetime.Guard);
    early.Close();checks["guardStopsWhenWindowCloses"]=!first.IsAttached;
   }
   var late=windowFactory();using(var lifetime=new CursorGuardLifetime(late,null,()=>visible)){
    checks["guardWaitsForNativeWindow"]=lifetime.Guard==null;
    late.Show();checks["guardStartsWhenNativeWindowAppears"]=lifetime.Guard!=null&&lifetime.Guard.IsAttached&&lifetime.InitializationCount==1;late.Close();
   }
  }
 }
}
