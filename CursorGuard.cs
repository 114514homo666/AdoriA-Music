using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CloudMusicFloat {
 // Observe the system cursor; never change ShowCursor's per-thread counter.
 // Layered + WS_EX_TRANSPARENT lets input reach windows in OTHER processes.
 internal struct CursorObservation {
  public bool Valid,ExternalForeground,ExternalCapture;
  public uint Flags;
  public IntPtr Cursor;
 }
 internal sealed class CursorGuard : IDisposable {
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct CursorInfo{public int Size;public uint Flags;public IntPtr Cursor;public Point Position;}
  [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct GuiInfo{public int Size;public uint Flags;public IntPtr Active,Focus,Capture,Menu,Move,Caret;public Rect CaretRect;}
  [DllImport("user32.dll")]static extern bool GetCursorInfo(ref CursorInfo info);
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  const long Transparent=0x20,NoActivate=0x08000000;
  readonly Window window;readonly HwndSource source;readonly IntPtr handle;
  readonly Func<CursorObservation> observe;readonly Action onBlocked;readonly Action<string> log;
  readonly DispatcherTimer timer;readonly uint ownPid;
  long addedStyle;bool disposed,changingStyle;
  public bool Blocked{get;private set;}
  public bool IsAttached{get{return !disposed&&source!=null&&!source.IsDisposed&&handle==new WindowInteropHelper(window).Handle;}}
  public long ExtendedStyle{get{return GetWindowLongPtr(handle,-20).ToInt64();}}
  public CursorGuard(Window window,Action onBlocked=null,Func<CursorObservation> observation=null,Action<string> log=null){
   this.window=window;this.onBlocked=onBlocked;this.log=log;
   handle=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(handle);
   ownPid=(uint)Process.GetCurrentProcess().Id;observe=observation??ReadObservation;
   if(source!=null)source.AddHook(Hook);
   window.AddHandler(Mouse.QueryCursorEvent,new QueryCursorEventHandler(QueryCursor),true);
   timer=new DispatcherTimer(DispatcherPriority.Input,window.Dispatcher){Interval=TimeSpan.FromMilliseconds(33)};
   timer.Tick+=(s,e)=>Refresh();Refresh();timer.Start();
  }
  CursorObservation ReadObservation(){
   IntPtr foreground=GetForegroundWindow();uint pid;
   if(foreground==IntPtr.Zero)return new CursorObservation();
   uint thread=GetWindowThreadProcessId(foreground,out pid);if(thread==0)return new CursorObservation();
   if(pid==ownPid)return new CursorObservation{Valid=true,ExternalForeground=false};
   var gui=new GuiInfo{Size=Marshal.SizeOf(typeof(GuiInfo))};uint capturePid=0;
   bool captured=GetGUIThreadInfo(thread,ref gui)&&gui.Capture!=IntPtr.Zero&&GetWindowThreadProcessId(gui.Capture,out capturePid)!=0&&capturePid==pid;
   var info=new CursorInfo{Size=Marshal.SizeOf(typeof(CursorInfo))};
   if(!GetCursorInfo(ref info)&&!captured)return new CursorObservation();
   return new CursorObservation{Valid=true,ExternalForeground=true,ExternalCapture=captured,Flags=info.Flags,Cursor=info.Cursor};
  }
  public static bool ShouldBlock(CursorObservation observation,bool previous){
   if(!observation.Valid)return previous;
   return observation.ExternalForeground&&(observation.ExternalCapture||(observation.Flags&1)==0||(observation.Flags&2)!=0||observation.Cursor==IntPtr.Zero);
  }
  public void Refresh(){
   if(disposed||changingStyle)return;
   var observation=observe();bool protect=ShouldBlock(observation,Blocked);
   long before=ExtendedStyle;
   if(protect){
    long missing=(Transparent|NoActivate)&~before;addedStyle|=missing;
    SetStyle(before|Transparent|NoActivate);
   }else if(addedStyle!=0){SetStyle(before&~addedStyle);addedStyle=0;}
   bool changed=Blocked!=protect;Blocked=protect;
   if(changed&&log!=null)log("光标保护="+protect+"；前台捕获="+observation.ExternalCapture+"；可见标志="+observation.Flags+"；窗口="+handle.ToInt64()+"；扩展样式="+ExtendedStyle.ToString("X"));
   if(changed&&protect&&onBlocked!=null)onBlocked();
  }
  void SetStyle(long value){
   if(value==ExtendedStyle)return;
   changingStyle=true;
   try{SetWindowLongPtr(handle,-20,(IntPtr)value);SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020);}finally{changingStyle=false;}
  }
  void QueryCursor(object sender,QueryCursorEventArgs e){
   Refresh();if(!Blocked)return;
   // WPF can also query from WM_MOUSEMOVE without WM_SETCURSOR. None keeps
   // an already-hidden cursor null; it never forces a visible cursor shape.
   e.Cursor=Cursors.None;e.Handled=true;
  }
  internal IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled){
   if(disposed||changingStyle)return IntPtr.Zero;
   if(message!=0x0084&&message!=0x0020&&message!=0x0021&&(message<0x0200||message>0x020e))return IntPtr.Zero;
   Refresh();if(!Blocked)return IntPtr.Zero;
   handled=true;
   if(message==0x0084)return (IntPtr)(-1);
   if(message==0x0020)return (IntPtr)1;
   if(message==0x0021)return (IntPtr)4;
   return IntPtr.Zero;
  }
  public void Dispose(){
   if(disposed)return;timer.Stop();window.RemoveHandler(Mouse.QueryCursorEvent,new QueryCursorEventHandler(QueryCursor));
   if(source!=null&&!source.IsDisposed)source.RemoveHook(Hook);
   if(addedStyle!=0){if(source!=null&&!source.IsDisposed)SetStyle(ExtendedStyle&~addedStyle);addedStyle=0;}
   Blocked=false;disposed=true;
  }
 }
 // A Window can already own its HWND before callers subscribe to SourceInitialized.
 // Attach immediately when possible, and cover both source creation and Loaded.
 internal sealed class CursorGuardLifetime : IDisposable {
  readonly Window window;readonly Action onBlocked;readonly Func<CursorObservation> observation;readonly Action<string> log;
  bool disposed;public CursorGuard Guard{get;private set;}public int InitializationCount{get;private set;}
  public CursorGuardLifetime(Window window,Action onBlocked=null,Func<CursorObservation> observation=null,Action<string> log=null){
   this.window=window;this.onBlocked=onBlocked;this.observation=observation;this.log=log;
   window.SourceInitialized+=OnReady;window.Loaded+=OnLoaded;window.Closed+=OnClosed;Ensure();
  }
  void OnReady(object sender,EventArgs args){Ensure();}
  void OnLoaded(object sender,RoutedEventArgs args){Ensure();}
  void OnClosed(object sender,EventArgs args){Dispose();}
  public void Ensure(){
   if(disposed||new WindowInteropHelper(window).Handle==IntPtr.Zero)return;
   if(Guard!=null&&Guard.IsAttached)return;
   if(Guard!=null)Guard.Dispose();Guard=new CursorGuard(window,onBlocked,observation,log);InitializationCount++;
   if(log!=null)log("光标保护已挂接 v"+typeof(CursorGuard).Assembly.GetName().Version+"；原生消息="+Guard.IsAttached+"；次数="+InitializationCount);
  }
  public void Dispose(){
   if(disposed)return;disposed=true;window.SourceInitialized-=OnReady;window.Loaded-=OnLoaded;window.Closed-=OnClosed;
   if(Guard!=null)Guard.Dispose();
  }
 }
}
