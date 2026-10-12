using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Automation;
using Microsoft.Win32;
using System.Windows.Interop;
using System.Windows.Shell;

[assembly:System.Reflection.AssemblyVersion("1.5.1.0")]
[assembly:System.Reflection.AssemblyFileVersion("1.5.1.0")]
namespace CloudMusicFloat {
 public class Song {
  public string Id="", Title="", Artist="", Album="", Cover="", Source="NetEase"; public double Duration; public byte[] CoverBytes;
  public string CacheKey {get{return Source+"-"+Id;}}
  public static Song Parse(string json) {
   var d=Json.Read(json);var album=Json.Obj(d,"album");var artists=Json.Arr(d,"artists");
   return new Song {Id=Json.Str(d,"id"),Title=Json.Str(d,"name"),Artist=String.Join(" / ",artists.Select(x=>Json.Str(x as Dictionary<string,object>,"name"))),Album=Json.Str(album,"name"),Cover=Json.Str(album,"picUrl"),Duration=Json.Num(d,"duration")/1000.0};
  }
 }
 public static class Json {
  public static Dictionary<string,object> Read(string s){return new JavaScriptSerializer{MaxJsonLength=8000000}.Deserialize<Dictionary<string,object>>(s);}
  public static string Write(object o){return new JavaScriptSerializer().Serialize(o);}
  public static string Str(Dictionary<string,object>d,string k){return d!=null&&d.ContainsKey(k)&&d[k]!=null?Convert.ToString(d[k]):"";}
  public static double Num(Dictionary<string,object>d,string k){double v;if(d==null||!d.ContainsKey(k)||d[k]==null)return 0;if(d[k] is string)return Double.TryParse((string)d[k],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)?v:0;try{return Convert.ToDouble(d[k],System.Globalization.CultureInfo.InvariantCulture);}catch{return 0;}}
  public static Dictionary<string,object> Obj(Dictionary<string,object>d,string k){return d!=null&&d.ContainsKey(k)?d[k] as Dictionary<string,object>:null;}
  public static object[] Arr(Dictionary<string,object>d,string k){if(d==null||!d.ContainsKey(k))return new object[0];var a=d[k] as IEnumerable;return a==null?new object[0]:a.Cast<object>().ToArray();}
 }
 // Open SQLite in READONLY mode. Never write the player's database.
 public static class Db {
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] name,out IntPtr db,int flags,IntPtr vfs);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int n,out IntPtr stmt,IntPtr tail);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr stmt);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr stmt,int col);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_bytes(IntPtr stmt,int col);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr stmt);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close(IntPtr db);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_busy_timeout(IntPtr db,int ms);
  public static string Scalar(string file,string query) {
   IntPtr db=IntPtr.Zero,stmt=IntPtr.Zero;
   try{if(sqlite3_open_v2(Encoding.UTF8.GetBytes(file+"\0"),out db,1,IntPtr.Zero)!=0)throw new IOException("播放数据库暂不可读");
    sqlite3_busy_timeout(db,100);if(sqlite3_prepare_v2(db,Encoding.UTF8.GetBytes(query+"\0"),-1,out stmt,IntPtr.Zero)!=0)return null;
    if(sqlite3_step(stmt)!=100)return null;int n=sqlite3_column_bytes(stmt,0);var b=new byte[n];if(n>0)Marshal.Copy(sqlite3_column_text(stmt,0),b,0,n);return Encoding.UTF8.GetString(b);
   }finally{if(stmt!=IntPtr.Zero)sqlite3_finalize(stmt);if(db!=IntPtr.Zero)sqlite3_close(db);}
  }
 }
 // This reader opens ONLY cloudmusic.exe, reads ONLY cloudmusic.dll and an
 // identified playback double. No process writes, injection, hooks or elevation.
 public class PlaybackReader : IDisposable {
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll")]static extern bool ReadProcessMemory(IntPtr h,IntPtr p,byte[] b,IntPtr n,out IntPtr read);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
  IntPtr handle=IntPtr.Zero,address=IntPtr.Zero;int pid;DateTime retry=DateTime.MinValue;
  public string Error=""; public int Pid {get{return pid;}}
  public double Read(double duration) {
   if(handle!=IntPtr.Zero){try{if(Process.GetProcessById(pid).HasExited)Dispose();}catch{Dispose();}}
   if(handle==IntPtr.Zero&&DateTime.UtcNow>=retry){retry=DateTime.UtcNow.AddSeconds(5);Connect(duration);}
   if(handle==IntPtr.Zero)return Double.NaN;
   byte[] b=new byte[8];IntPtr n;if(!ReadProcessMemory(handle,address,b,(IntPtr)8,out n)||n.ToInt64()!=8){Dispose();return Double.NaN;}
   double v=BitConverter.ToDouble(b,0);
   if(Double.IsNaN(v)||Double.IsInfinity(v)||v < -1 || v>86400){Dispose();Error="当前版本的进度数据不可用";return Double.NaN;}
   if(v<0)return 0;return v;
  }
  void Connect(double duration) {
   Error="等待网易云音乐";
   foreach(var p in Process.GetProcessesByName("cloudmusic")) {
    IntPtr h=OpenProcess(0x410,false,p.Id);if(h==IntPtr.Zero)continue;
    try{foreach(ProcessModule m in p.Modules){if(!m.ModuleName.Equals("cloudmusic.dll",StringComparison.OrdinalIgnoreCase))continue;
     if(m.ModuleMemorySize>120000000)continue;
     byte[] b=new byte[m.ModuleMemorySize];IntPtr n;ReadProcessMemory(h,m.BaseAddress,b,(IntPtr)b.Length,out n);int len=(int)n.ToInt64();
     for(int i=0;i<len-16;i++) {
      if(b[i]!=0xf2||b[i+1]!=0x0f||b[i+2]!=0x11||b[i+3]!=0x3d||b[i+8]!=0xf2||b[i+9]!=0x0f||b[i+10]!=0x11||b[i+11]!=0x35)continue;
      long a=m.BaseAddress.ToInt64()+i+8+BitConverter.ToInt32(b,i+4);byte[] val=new byte[8];
      if(!ReadProcessMemory(h,(IntPtr)a,val,(IntPtr)8,out n)||n.ToInt64()!=8)continue;
      double v=BitConverter.ToDouble(val,0);
      // -1 is the unused renderer process. Select the initialized player.
      if(Double.IsNaN(v)||Double.IsInfinity(v)||v<0||v>86400||(duration>0&&v>duration+2))continue;
      handle=h;address=(IntPtr)a;pid=p.Id;h=IntPtr.Zero;Error="";return;
     }
    }}catch(Exception e){Error=e.Message;}finally{if(h!=IntPtr.Zero)CloseHandle(h);}
   }
   if(Process.GetProcessesByName("cloudmusic").Length>0)Error="当前网易云版本暂不支持进度读取";
  }
  public void Dispose(){if(handle!=IntPtr.Zero)CloseHandle(handle);handle=IntPtr.Zero;address=IntPtr.Zero;pid=0;}
 }
 public class LyricLine {public double Time;public string Text="",Translation="";}
 public static class Lyrics {
  public static List<LyricLine> Parse(string text) {
   var lines=new List<LyricLine>();double offset=0;
   var off=System.Text.RegularExpressions.Regex.Match(text??"",@"\[offset:([+-]?\d+)\]",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
   if(off.Success)offset=Double.Parse(off.Groups[1].Value)/1000.0;
   foreach(string row in (text??"").Split('\n')) {
    var times=System.Text.RegularExpressions.Regex.Matches(row,@"\[(\d+):(\d+(?:\.\d+)?)\]");
    string words=System.Text.RegularExpressions.Regex.Replace(row,@"\[[^\]]*\]","").Trim();
    foreach(System.Text.RegularExpressions.Match t in times) {
     double seconds=Double.Parse(t.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture);
     lines.Add(new LyricLine{Time=Math.Max(0,Int32.Parse(t.Groups[1].Value)*60+seconds-offset),Text=words});
    }
   }
   return lines.OrderBy(l=>l.Time).ToList();
  }
  public static List<LyricLine> FromJson(string json) {
   var d=Json.Read(json);var lines=Parse(Json.Str(Json.Obj(d,"lrc"),"lyric"));var translated=Parse(Json.Str(Json.Obj(d,"tlyric"),"lyric"));
   foreach(var line in lines){var tr=translated.FirstOrDefault(t=>Math.Abs(t.Time-line.Time)<.08);if(tr!=null)line.Translation=tr.Text;}
   return lines;
  }
 }
 public class Settings {public double Left=Double.NaN,Top=Double.NaN,Opacity=.96,BackgroundOpacity=.35,Offset=0,Scale=1,WindowWidth=0,WindowHeight=0;public bool Compact=false,Topmost=true,Glass=true;public string Player="NetEase",Theme="Black";}
 public partial class Overlay : Window {
  readonly string root=AppDomain.CurrentDomain.BaseDirectory;string dataRoot,dbPath,currentId="";Song song;Settings settings;
  PlaybackReader reader=new PlaybackReader();List<LyricLine> lyrics=new List<LyricLine>();
  SystemMediaReader mediaReader=new SystemMediaReader();MediaSnapshot mediaSnapshot;DateTime mediaReadAt;int sourceRevision;bool controlling;
  public bool SilentRun;
  readonly AssetStore assets=AssetStore.ForApplication();readonly LyricRetry lyricRetry=new LyricRetry();bool lyricBusy;int lyricRequestRevision;string lyricDetails="";
  CancellationTokenSource lyricCancellation;
  LyricService lyricService;
  readonly GlassEffect glass=new GlassEffect();ContextMenu appearanceMenu;SolidColorBrush edge=new SolidColorBrush(),trackBrush=new SolidColorBrush(),artworkBrush=new SolidColorBrush();GradientStop surfaceTop=new GradientStop(),surfaceBottom=new GradientStop();
  SolidColorBrush menuSurface=new SolidColorBrush(),menuHover=new SolidColorBrush();
  TextBlock sourceLabel;Button sourceButton;ContextMenu sourceMenu;MenuItem netEaseItem,qqItem;
  TextBlock title,artist,compactLyric,previous,current,next,translation,time,status,brand,compactLabel;Image cover;Border artwork,progressFill,lyricsViewport,compactLyricSlot;Grid progressTrack;StackPanel lyricsPanel;Button compactButton,pinButton,playButton;
  System.Windows.Shapes.Path pinIcon,playIcon,pauseIcon;RotateTransform pinRotation;TranslateTransform lyricShift;Border card;int coverRevision;bool? playbackState;double progressTarget=-1;bool closingAnimated,isVerification;
  DispatcherTimer clock;bool busy=false;double position=Double.NaN,lastPosition=Double.NaN;DateTime lastMove=DateTime.MinValue;string lyricState="等待播放歌曲";int ticks=0,lyricIndex=-999;
  Brush white=new SolidColorBrush(Color.FromRgb(244,246,255)),muted=new SolidColorBrush(Color.FromRgb(145,156,184)),accent=new SolidColorBrush(Color.FromRgb(138,174,255));
  delegate bool EnumWindow(IntPtr window,IntPtr param);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr param);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [StructLayout(LayoutKind.Sequential)]struct MediaKey {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
  [StructLayout(LayoutKind.Explicit,Size=40)]struct MediaInput {[FieldOffset(0)]public uint Type;[FieldOffset(8)]public MediaKey Key;}
  [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,MediaInput[] inputs,int size);
  static IntPtr PlayerWindow(){var ids=new HashSet<int>(Process.GetProcessesByName("cloudmusic").Select(p=>p.Id));IntPtr best=IntPtr.Zero;EnumWindows((w,p)=>{uint pid;GetWindowThreadProcessId(w,out pid);if(ids.Contains((int)pid)){var b=new StringBuilder(1024);GetWindowText(w,b,b.Capacity);string s=b.ToString();if(s.Contains(" - ")){best=w;return false;}if(s.Contains("网易云音乐"))best=w;}return true;},IntPtr.Zero);return best;}
  static string PlayerCaption(){var ids=new HashSet<int>(Process.GetProcessesByName("cloudmusic").Select(p=>p.Id));string title="";EnumWindows((w,p)=>{uint pid;GetWindowThreadProcessId(w,out pid);if(ids.Contains((int)pid)){var b=new StringBuilder(1024);GetWindowText(w,b,b.Capacity);string s=b.ToString();if(s.Contains(" - ")){title=s;return false;}}return true;},IntPtr.Zero);return title;}
  Song ReadCurrentSong(){
   string value=Db.Scalar(dbPath,"SELECT jsonStr FROM historyTracks ORDER BY playtime DESC LIMIT 1");if(String.IsNullOrEmpty(value))return null;var last=Song.Parse(value);string caption=PlayerCaption();
   if(String.IsNullOrEmpty(caption)||caption.StartsWith(last.Title+" - ",StringComparison.OrdinalIgnoreCase))return last;
   // The title keeps changing when a minimized client delays history writes.
   // Match only the local playing queue, never an unrelated history entry.
   try{string queue=File.ReadAllText(Path.Combine(dataRoot,"webdata","file","playingList"));foreach(var item in Json.Arr(Json.Read(queue),"list")){var track=Json.Obj(item as Dictionary<string,object>,"track");if(track==null)continue;string name=Json.Str(track,"name");if(!String.IsNullOrEmpty(name)&&caption.StartsWith(name+" - ",StringComparison.OrdinalIgnoreCase))return Song.Parse(Json.Write(track));}}catch{}
   return last;
  }
  public Overlay(bool verification=false,string selectedPlayer=null) {
   lyricService=new LyricService(assets);
   isVerification=verification;Title="AdoriA MUSIC · 悬浮窗"; Width=440;Height=356;MinWidth=240;MinHeight=64;UseLayoutRounding=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=true;
   bool hadSettings=File.Exists(Path.Combine(root,"settings.json"));try{settings=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"settings.json")));}catch{settings=new Settings();}if(settings==null)settings=new Settings();
   if(settings.Player!="QQMusic")settings.Player="NetEase";if(!hadSettings&&Process.GetProcessesByName("QQMusic").Length>0&&Process.GetProcessesByName("cloudmusic").Length==0)settings.Player="QQMusic";
   if(selectedPlayer=="QQMusic"||selectedPlayer=="NetEase")settings.Player=selectedPlayer;
   settings.Theme=Palette.Get(settings.Theme).Key;settings.BackgroundOpacity=FiniteClamp(settings.BackgroundOpacity,0,1,.35);settings.Scale=FiniteClamp(settings.Scale,.7,1.8,1);Opacity=1;Topmost=settings.Topmost;
   var area=SystemParameters.WorkArea;Left=Double.IsNaN(settings.Left)?area.Right-Width-28:Math.Max(area.Left,Math.Min(area.Right-Width,settings.Left));Top=Double.IsNaN(settings.Top)?area.Top+90:Math.Max(area.Top,Math.Min(area.Bottom-Height,settings.Top));
   if(isVerification){settings.Compact=false;settings.Scale=1;settings.Topmost=false;settings.Player="NetEase";ShowActivated=false;ShowInTaskbar=false;Left=Top=-10000;}Build();ApplyTheme(false);RestoreDimensions();ApplyCompact(false);if(!isVerification)FindData();
   SourceInitialized+=(s,e)=>{ApplyTheme(false);QueueRoundedClip();var source=(HwndSource)PresentationSource.FromVisual(this);if(source!=null)source.AddHook((IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)=>{if(message==0x031e||message==0x0320)Dispatcher.BeginInvoke(new Action(()=>{ApplyTheme(false);QueueRoundedClip();}));if(message==0x0005||message==0x02e0||message==0x0047)QueueRoundedClip();return IntPtr.Zero;});};
   ContentRendered+=(s,e)=>QueueRoundedClip();
   StateChanged+=(s,e)=>QueueRoundedClip();IsVisibleChanged+=(s,e)=>QueueRoundedClip();Closed+=(s,e)=>glass.Dispose();
   MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed&&!IsInteractive(e.OriginalSource as DependencyObject)){try{DragMove();Save();}catch{}}};

   SizeChanged+=(s,e)=>ApplyResponsiveLayout();
   Closing+=(s,e)=>{CancelLyrics();if(!closingAnimated&&!isVerification){e.Cancel=true;closingAnimated=true;sourceRevision++;clock.Stop();Motion.Fade(card,0,150);var scale=(ScaleTransform)card.RenderTransform;Motion.To(scale,ScaleTransform.ScaleXProperty,.97,150);Motion.To(scale,ScaleTransform.ScaleYProperty,.97,150);Later(170,()=>Close());return;}Save();reader.Dispose();Task.Run(()=>mediaReader.Dispose());};
   KeyDown+=(s,e)=>{if(e.Key!=Key.Escape)return;bool menuOpen=false;foreach(var menu in new[]{appearanceMenu,sourceMenu,settingsMenu})if(menu!=null&&menu.IsOpen){menu.IsOpen=false;menuOpen=true;}e.Handled=true;if(!menuOpen)Close();};
   clock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};clock.Tick+=Tick;if(!isVerification)clock.Start();
   Loaded+=async(s,e)=>{card.Opacity=0;var scale=(ScaleTransform)card.RenderTransform;scale.ScaleX=scale.ScaleY=.97;Motion.Fade(card,1,230);Motion.To(scale,ScaleTransform.ScaleXProperty,1,230);Motion.To(scale,ScaleTransform.ScaleYProperty,1,230);UpdateRoundedClip();if(!isVerification)await RefreshSong();};
  }
  TextBlock Text(string value,double size,Brush color){return new TextBlock{Text=value,FontSize=size,Foreground=color,FontFamily=new FontFamily("Microsoft YaHei UI"),TextTrimming=TextTrimming.CharacterEllipsis};}
  void Later(int ms,Action action){var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(s,e)=>{timer.Stop();action();};timer.Start();}
  Button Button(object label,string tip,Action action){
   var background=new SolidColorBrush(Colors.Transparent);var scale=new ScaleTransform(1,1);
   var b=new Button{Content=label,Width=27,Height=25,Margin=new Thickness(3,0,0,0),Background=background,Foreground=muted,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,FontSize=16,RenderTransform=scale,RenderTransformOrigin=new Point(.5,.5)};
   b.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border CornerRadius='7' Background='{TemplateBinding Background}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
   AutomationProperties.SetName(b,tip);
   b.MouseEnter+=(s,e)=>{background.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(Color.FromArgb(40,145,171,240),TimeSpan.FromMilliseconds(150)));Motion.To(scale,ScaleTransform.ScaleXProperty,1.07,150);Motion.To(scale,ScaleTransform.ScaleYProperty,1.07,150);};
   b.MouseLeave+=(s,e)=>{background.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(Colors.Transparent,TimeSpan.FromMilliseconds(180)));Motion.To(scale,ScaleTransform.ScaleXProperty,1,180);Motion.To(scale,ScaleTransform.ScaleYProperty,1,180);};
   b.PreviewMouseLeftButtonDown+=(s,e)=>{Motion.To(scale,ScaleTransform.ScaleXProperty,.92,90);Motion.To(scale,ScaleTransform.ScaleYProperty,.92,90);};
   b.PreviewMouseLeftButtonUp+=(s,e)=>{Motion.To(scale,ScaleTransform.ScaleXProperty,b.IsMouseOver?1.07:1,150);Motion.To(scale,ScaleTransform.ScaleYProperty,b.IsMouseOver?1.07:1,150);};
   b.Click+=(s,e)=>{e.Handled=true;action();};return b;
  }
  void Build() {
   surfaceTop.Offset=0;surfaceBottom.Offset=1;var surface=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(.35,1)};surface.GradientStops.Add(surfaceTop);surface.GradientStops.Add(surfaceBottom);
   var outer=new Border{CornerRadius=new CornerRadius(18),Background=Brushes.Transparent,RenderTransform=new ScaleTransform(1,1),RenderTransformOrigin=new Point(.5,.5)};card=outer;
   Content=outer;var layers=new Grid();outer.Child=layers;
   backgroundLayer=new Border{CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(0),Background=surface,Opacity=settings.BackgroundOpacity,IsHitTestVisible=false};layers.Children.Add(backgroundLayer);
   layout=new Grid{Margin=new Thickness(24)};layout.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});layers.Children.Add(layout);layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   topBar=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,15)};var top=topBar;Grid.SetRow(top,0);Grid.SetColumnSpan(top,3);layout.Children.Add(top);
   controlStrip=new StackPanel{Orientation=Orientation.Horizontal};var controls=controlStrip;DockPanel.SetDock(controls,Dock.Right);top.Children.Add(controls);
   sourceMenu=new ContextMenu();netEaseItem=new MenuItem{Header="网易云音乐",IsCheckable=true};qqItem=new MenuItem{Header="QQ 音乐",IsCheckable=true};netEaseItem.Click+=(s,e)=>SwitchPlayer("NetEase");qqItem.Click+=(s,e)=>SwitchPlayer("QQMusic");sourceMenu.Items.Add(netEaseItem);sourceMenu.Items.Add(qqItem);sourceMenu.Opened+=(s,e)=>{netEaseItem.IsChecked=settings.Player=="NetEase";qqItem.IsChecked=settings.Player=="QQMusic";};
   sourceLabel=Text(PlayerName+" ▾",10,accent);sourceButton=Button(sourceLabel,"切换音乐播放器",()=>{sourceMenu.PlacementTarget=sourceButton;sourceMenu.IsOpen=true;});sourceButton.Width=76;sourceButton.Margin=new Thickness(0,0,7,0);controls.Children.Add(sourceButton);
   appearanceMenu=new ContextMenu();PopulateAppearanceMenu(appearanceMenu.Items);var paletteIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M8,1 A7,7 0 1 0 8,15 L10,15 Q12,15 11,13 L10,11 Q10,10 12,10 L13,10 Q16,10 15,7 A7,7 0 0 0 8,1 Z M4,5 A.7,.7 0 1 0 5.4,5 A.7,.7 0 1 0 4,5 M8,3 A.7,.7 0 1 0 9.4,3 A.7,.7 0 1 0 8,3"),Stroke=accent,StrokeThickness=1.1,Fill=Brushes.Transparent,Width=16,Height=16};Button appearanceButton=null;appearanceButton=Button(paletteIcon,"颜色与毛玻璃",()=>{appearanceMenu.PlacementTarget=appearanceButton;appearanceMenu.IsOpen=true;});controls.Children.Add(appearanceButton);
   var gear=new System.Windows.Shapes.Path{Data=Geometry.Parse("M6,1 L10,1 L10.5,3 L12,4 L14,4 L15,7 L13.5,8.5 L13,10 L14,12 L11,14 L9,13 L7,13 L5,14 L2,12 L3,10 L2.5,8.5 L1,7 L2,4 L4,4 L5.5,3 Z M5,8 A3,3 0 1 0 11,8 A3,3 0 1 0 5,8"),Stroke=accent,StrokeThickness=1.1,Fill=Brushes.Transparent,Width=16,Height=16};settingsButton=Button(gear,"设置",()=>OpenSettings());controls.Children.Add(settingsButton);
   pinRotation=new RotateTransform(Topmost?0:-35,8,8);pinIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M5,2 L11,2 L10,7 L13,10 L3,10 L6,7 Z M8,10 L8,15"),Width=16,Height=16,Stroke=new SolidColorBrush(Topmost?((SolidColorBrush)accent).Color:((SolidColorBrush)muted).Color),StrokeThickness=1.35,StrokeLineJoin=PenLineJoin.Round,Fill=Brushes.Transparent,RenderTransform=pinRotation};
   pinButton=Button(pinIcon,"切换始终置顶",()=>{Topmost=!Topmost;settings.Topmost=Topmost;Motion.To(pinRotation,RotateTransform.AngleProperty,Topmost?0:-35,220);((SolidColorBrush)pinIcon.Stroke).BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(((SolidColorBrush)(Topmost?accent:muted)).Color,TimeSpan.FromMilliseconds(220)));Save();});controls.Children.Add(pinButton);
   compactLabel=Text("−",16,muted);compactButton=Button(compactLabel,"精简 / 完整模式",()=>{settings.Compact=!settings.Compact;ApplyCompact(true);Save();});controls.Children.Add(compactButton);controls.Children.Add(Button("×","关闭悬浮窗",()=>Close()));
   brand=Text("♪   AdoriA MUSIC",10,accent);brand.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(brand);
   songInfo=new Grid{Margin=new Thickness(0,0,0,10)};var info=songInfo;Grid.SetColumnSpan(info,3);info.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(80)});info.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(info,1);layout.Children.Add(info);
   artwork=new Border{Width=72,Height=72,CornerRadius=new CornerRadius(14),Background=artworkBrush,HorizontalAlignment=HorizontalAlignment.Left,ClipToBounds=true};
   cover=new Image{Stretch=Stretch.UniformToFill};cover.Clip=new RectangleGeometry(new Rect(0,0,72,72),14,14);artwork.Child=cover;info.Children.Add(artwork);
   metadataPanel=new StackPanel{Margin=new Thickness(12,3,0,0),VerticalAlignment=VerticalAlignment.Center};var meta=metadataPanel;Grid.SetColumn(meta,1);info.Children.Add(meta);
   artist=Text("打开"+PlayerName+"，播放一首歌",11,muted);title=Text("等待"+PlayerName,17,white);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,4,0,0);compactLyric=Text("",11,accent);compactLyricSlot=new Border{Height=16,Margin=new Thickness(0,5,0,0),Visibility=Visibility.Hidden,Child=compactLyric};meta.Children.Add(artist);meta.Children.Add(title);meta.Children.Add(compactLyricSlot);
   lyricsViewport=new Border{ClipToBounds=true};Grid.SetRow(lyricsViewport,2);Grid.SetColumnSpan(lyricsViewport,3);layout.Children.Add(lyricsViewport);lyricShift=new TranslateTransform();lyricsPanel=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,0,10),RenderTransform=lyricShift};lyricsViewport.Child=lyricsPanel;
   previous=Text("",12,muted);previous.Opacity=.55;current=Text("让音乐在桌面停留",19,white);current.FontWeight=FontWeights.SemiBold;current.TextWrapping=TextWrapping.Wrap;current.MaxHeight=58;current.Margin=new Thickness(0,9,0,0);
   translation=Text("",11,muted);translation.Margin=new Thickness(0,5,0,0);next=Text("",12,muted);next.Opacity=.65;next.Margin=new Thickness(0,9,0,0);lyricsPanel.Children.Add(previous);lyricsPanel.Children.Add(current);lyricsPanel.Children.Add(translation);lyricsPanel.Children.Add(next);
   footer=new StackPanel();var bottom=footer;Grid.SetRow(bottom,3);Grid.SetColumnSpan(bottom,3);layout.Children.Add(bottom);
   progressTrack=new Grid{Height=3,Background=trackBrush,Margin=new Thickness(0,0,0,9)};progressFill=new Border{Height=3,Width=0,CornerRadius=new CornerRadius(2),Background=accent,HorizontalAlignment=HorizontalAlignment.Left};progressTrack.Children.Add(progressFill);bottom.Children.Add(progressTrack);
   playbackRow=new DockPanel{LastChildFill=true,Height=22};var timeRow=playbackRow;time=Text("--:-- / --:--",10,muted);time.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(time,Dock.Right);timeRow.Children.Add(time);
   var playGlyph=new Grid{Width=16,Height=16};playIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M5,3 L13,8 L5,13 Z"),Fill=accent,Opacity=1};pauseIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M4,3 L7,3 L7,13 L4,13 Z M10,3 L13,3 L13,13 L10,13 Z"),Fill=accent,Opacity=0};playGlyph.Children.Add(playIcon);playGlyph.Children.Add(pauseIcon);
   playButton=Button(playGlyph,"播放",TogglePlayback);playButton.Width=25;playButton.Height=22;playButton.Margin=new Thickness(-5,0,5,0);playButton.IsEnabled=false;DockPanel.SetDock(playButton,Dock.Left);timeRow.Children.Add(playButton);status=Text("正在连接…",10,muted);status.VerticalAlignment=VerticalAlignment.Center;timeRow.Children.Add(status);bottom.Children.Add(timeRow);
   var menu=new ContextMenu();
   AddMenu(menu,"使用网易云音乐",()=>SwitchPlayer("NetEase"));AddMenu(menu,"使用 QQ 音乐",()=>SwitchPlayer("QQMusic"));menu.Items.Add(new Separator());
   AddMenu(menu,"恢复默认窗口大小",()=>{settings.Compact=false;SetDimensions(440,356,true);ApplyCompact(true);});
   pinSetting=new MenuItem{Header="始终置顶",IsCheckable=true};pinSetting.Click+=(s,e)=>{Topmost=!Topmost;settings.Topmost=Topmost;ApplyTheme(true);Save();};menu.Items.Add(pinSetting);
   compactSetting=new MenuItem{Header="精简显示",IsCheckable=true};compactSetting.Click+=(s,e)=>{settings.Compact=!settings.Compact;ApplyCompact(true);Save();};menu.Items.Add(compactSetting);menu.Opened+=(s,e)=>{pinSetting.IsChecked=Topmost;compactSetting.IsChecked=settings.Compact;};menu.Items.Add(new Separator());
   AddMenu(menu,"歌词提前 0.5 秒",()=>{settings.Offset+=.5;lyricIndex=-999;Save();});AddMenu(menu,"歌词延后 0.5 秒",()=>{settings.Offset-=.5;lyricIndex=-999;Save();});AddMenu(menu,"重置歌词偏移",()=>{settings.Offset=0;lyricIndex=-999;Save();});
   AddMenu(menu,"重新获取歌词",()=>{if(song!=null)LoadCurrentLyrics(song,sourceRevision,true);});AddMenu(menu,"复制歌词诊断信息",()=>{try{Clipboard.SetText("AdoriA MUSIC v1.5.1\n"+(song==null?PlayerName:song.Source+" · "+song.Title+" / "+song.Artist)+"\n"+lyricState+"\n"+lyricDetails);}catch{}});
   AddMenu(menu,"导入当前歌曲的 LRC 歌词…",ImportLrc);AddMenu(menu,"重新连接",()=>SwitchPlayer(settings.Player,true));menu.Items.Add(new Separator());AddMenu(menu,"退出",()=>Close());settingsMenu=menu;ContextMenu=null;
   foreach(var popup in new[]{sourceMenu,appearanceMenu,menu})MenuAppearance.Configure(popup,menuSurface,menuHover,white,edge);
   ToolTipService.SetIsEnabled(this,false);foreach(var popup in new[]{sourceMenu,appearanceMenu,menu})ToolTipService.SetIsEnabled(popup,false);
   AddResizeGrips(layers);progressTrack.SizeChanged+=(s,e)=>SetProgress(progressFraction);ApplyResponsiveLayout();
  }
  void AddMenu(ContextMenu m,string label,Action action){var item=new MenuItem{Header=label};item.Click+=(s,e)=>action();m.Items.Add(item);}
  void SetOpacity(double x){settings.BackgroundOpacity=FiniteClamp(x,0,1,.35);Motion.Fade(backgroundLayer,settings.BackgroundOpacity,100);ApplyGlassAndTint(true);SyncAppearanceSliders();ScheduleSave();}
  void PopulateAppearanceMenu(ItemCollection items){foreach(var palette in Palette.All){var chosen=palette;var item=new MenuItem{Header=chosen.Name,Tag=chosen.Key,IsCheckable=true};item.Click+=(s,e)=>{settings.Theme=chosen.Key;ApplyTheme(true);Save();};items.Add(item);}
   items.Add(new Separator());var glassItem=new MenuItem{Header="毛玻璃效果",Tag="Glass",IsCheckable=true};glassItem.Click+=(s,e)=>{settings.Glass=!settings.Glass;ApplyTheme(true);Save();};items.Add(glassItem);AddAppearanceSliders(items);
  }
  void UpdateAppearanceChecks(ItemCollection items){foreach(var item in items){var entry=item as MenuItem;if(entry==null)continue;string tag=entry.Tag as string;if(tag=="Glass"){entry.IsChecked=settings.Glass;}else if(tag!=null)entry.IsChecked=tag==settings.Theme;else if(entry.HasItems)UpdateAppearanceChecks(entry.Items);}}
  void ApplyTheme(bool animated){var palette=Palette.Get(settings.Theme);ApplyGlassAndTint(animated);
   Palette.To((SolidColorBrush)white,palette.Text,animated);Palette.To((SolidColorBrush)muted,palette.Muted,animated);Palette.To((SolidColorBrush)accent,palette.Accent,animated);Palette.To(edge,palette.Border,animated);Palette.To(trackBrush,palette.Track,animated);Palette.To(artworkBrush,palette.Artwork,animated);
   if(pinIcon!=null)Palette.To((SolidColorBrush)pinIcon.Stroke,Topmost?palette.Accent:palette.Muted,animated);
   Palette.To(menuSurface,palette.Top,animated);var hover=palette.Accent;hover.A=36;Palette.To(menuHover,hover,animated);
   Background=Brushes.Transparent;ApplyTextContrast(palette);SyncAppearanceSliders();
   if(appearanceMenu!=null)UpdateAppearanceChecks(appearanceMenu.Items);
  }
  string PlayerName {get{return settings.Player=="QQMusic"?"QQ 音乐":"网易云音乐";}}
  void SwitchPlayer(string player,bool reconnect=false){
   if(player!="QQMusic")player="NetEase";if(!reconnect&&settings.Player==player)return;
   sourceRevision++;CancelLyrics();lyricRetry.Reset();lyricDetails="";settings.Player=player;busy=false;currentId="";song=null;mediaSnapshot=null;position=lastPosition=Double.NaN;lastMove=DateTime.MinValue;reader.Dispose();lyrics.Clear();lyricIndex=-999;lyricState="等待播放歌曲";
   Motion.Text(sourceLabel,PlayerName+" ▾");Motion.Text(title,"等待"+PlayerName);Motion.Text(artist,"打开"+PlayerName+"并播放一首歌曲");SetCover(null);SetLyricTexts("","等待播放歌曲","","");time.Text="--:-- / --:--";SetProgress(0);SetPlayback(null);Motion.Text(status,"正在连接…");Save();
   if(!isVerification){FindData();Later(1,async()=>await RefreshSong());}
  }
  void ApplyCompact(bool animated=false){
   if(animated){Motion.Text(compactLabel,settings.Compact?"+":"−");Motion.To(lyricShift,TranslateTransform.YProperty,0,180);}else compactLabel.Text=settings.Compact?"+":"−";
   ApplyResponsiveLayout();if(animated){lyricsPanel.Opacity=.65;Motion.Fade(lyricsPanel,1,180);}
  }
  public static bool SendPlaybackCommand(){if(PlayerWindow()==IntPtr.Zero)return false;var keys=new[]{new MediaInput{Type=1,Key=new MediaKey{Key=0xb3}},new MediaInput{Type=1,Key=new MediaKey{Key=0xb3,Flags=2}}};return SendInput(2,keys,Marshal.SizeOf(typeof(MediaInput)))==2;}
  async void TogglePlayback(){if(isVerification){SetPlayback(!(playbackState??false));return;}if(controlling)return;controlling=true;string selected=settings.Player;int revision=sourceRevision;
   try{bool sent=await Task.Run(()=>{bool ok=mediaReader.Toggle(selected=="QQMusic"?"QQMusic":"cloudmusic");if(!ok&&selected=="NetEase"&&Process.GetProcessesByName("QQMusic").Length==0)ok=SendPlaybackCommand();return ok;});if(revision==sourceRevision){if(!sent)Motion.Text(status,"请在"+PlayerName+"中播放 / 暂停");else await RefreshSong();}}
   catch(Exception ex){if(revision==sourceRevision)Motion.Text(status,"播放控制暂不可用");Log(ex.Message);}finally{controlling=false;}
  }
  void SetPlayback(bool? playing){if(playbackState==playing&&playButton.IsEnabled==(playing!=null))return;playbackState=playing;playButton.IsEnabled=playing!=null;Motion.Fade(playButton,playing==null?.4:1,180);Motion.Fade(playIcon,playing==true?0:1,180);Motion.Fade(pauseIcon,playing==true?1:0,180);string label=playing==true?"暂停":"播放";AutomationProperties.SetName(playButton,label);}
  void SetProgress(double value){progressFraction=Math.Max(0,Math.Min(1,value));double target=progressTrack.ActualWidth*Math.Max(0,Math.Min(1,value));if(Math.Abs(target-progressTarget)<.01)return;progressTarget=target;Motion.To(progressFill,FrameworkElement.WidthProperty,target,220);}
  void SetCover(ImageSource value){int revision=++coverRevision;Motion.Fade(cover,0,100);Later(110,()=>{if(revision!=coverRevision)return;cover.Source=value;Motion.Fade(cover,1,220);});}
  void Save(){if(isVerification||SilentRun)return;try{settings.Left=Left;settings.Top=Top;settings.WindowWidth=Width;settings.WindowHeight=Height;File.WriteAllText(Path.Combine(root,"settings.json"),Json.Write(settings),Encoding.UTF8);}catch{}}
  void FindData() {
   var candidates=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NetEase","CloudMusic")};
   string packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
   if(Directory.Exists(packages)){foreach(var d in Directory.GetDirectories(packages,"1F8B0F94.122165AE053F_*"))candidates.Add(Path.Combine(d,"LocalCache","Local","NetEase","CloudMusic"));}
   dataRoot=candidates.FirstOrDefault(d=>File.Exists(Path.Combine(d,"Library","webdb.dat")));dbPath=dataRoot==null?null:Path.Combine(dataRoot,"Library","webdb.dat");
  }
  async void Tick(object sender,EventArgs e) {
   ticks++;if(ticks%4==0&&!busy)await RefreshSong();
   if(song==null)return;
   if(!lyricBusy&&lyrics.Count==0&&lyricRetry.Due(DateTime.UtcNow))LoadCurrentLyrics(song,sourceRevision);
   if(settings.Player=="QQMusic"){
    if(mediaSnapshot==null)return;position=mediaSnapshot.Position+(mediaSnapshot.Playing?Math.Max(0,(DateTime.UtcNow-mediaReadAt).TotalSeconds):0);if(song.Duration>0)position=Math.Min(song.Duration,position);
    SetPlayback(mediaSnapshot.Playing);time.Text=Format(position)+" / "+Format(song.Duration);SetProgress(position/Math.Max(1,song.Duration));UpdateLyric(position+settings.Offset);return;
   }
   position=reader.Read(song.Duration);
   if(!Double.IsNaN(position)){
    if(!Double.IsNaN(lastPosition)&&Math.Abs(position-lastPosition)>.005)lastMove=DateTime.UtcNow;
    lastPosition=position;bool playing=(DateTime.UtcNow-lastMove).TotalSeconds<1.1;
    SetPlayback(playing);Motion.Text(status,"");playButton.ToolTip=(playing?"暂停":"播放")+(Math.Abs(settings.Offset)>.01?" · 歌词偏移 "+settings.Offset.ToString("+0.0;-0.0")+"s":"");
    time.Text=Format(position)+" / "+Format(song.Duration);SetProgress(position/Math.Max(1,song.Duration));UpdateLyric(position+settings.Offset);
   }else{SetPlayback(null);Motion.Text(status,reader.Error);time.Text="--:-- / "+Format(song.Duration);SetProgress(0);}
  }
  async Task RefreshSong() {
   if(busy||closingAnimated)return;busy=true;int revision=sourceRevision;string selected=settings.Player;
   try{
    MediaSnapshot snapshot=null;
    var fresh=await Task.Run(()=>{
     if(selected=="QQMusic"){snapshot=mediaReader.Read("QQMusic");if(snapshot==null||String.IsNullOrWhiteSpace(snapshot.Title))return (Song)null;return new Song{Source="QQMusic",Id=QQMusicAssets.Identity(snapshot.Title,snapshot.Artist,snapshot.Album),Title=snapshot.Title,Artist=snapshot.Artist,Album=snapshot.Album,Duration=snapshot.Duration,CoverBytes=snapshot.Cover};}
     if(Process.GetProcessesByName("cloudmusic").Length==0)return (Song)null;
     if(dbPath==null)FindData();if(dbPath==null)return (Song)null;
     return ReadCurrentSong();
    });
    if(revision!=sourceRevision||closingAnimated)return;
    if(snapshot!=null){mediaSnapshot=snapshot;mediaReadAt=DateTime.UtcNow;}
    if(fresh==null){if(song!=null){CancelLyrics();lyricRetry.Reset();}song=null;currentId="";Motion.Text(title,"等待"+PlayerName);Motion.Text(artist,"打开"+PlayerName+"并播放一首歌曲");SetCover(null);lyrics.Clear();SetLyricTexts("","等待播放歌曲","","");time.Text="--:-- / --:--";SetPlayback(null);Motion.Text(status,selected=="QQMusic"?mediaReader.Error:"未连接");SetProgress(0);reader.Dispose();return;}
    Motion.Text(status,"");if(fresh.CacheKey==currentId){if(fresh.Duration>0)song.Duration=fresh.Duration;if(fresh.Source=="QQMusic"&&cover.Source==null&&fresh.CoverBytes!=null){var picture=await LoadCover(fresh);if(revision==sourceRevision&&currentId==fresh.CacheKey)SetCover(picture);}return;}
    CancelLyrics();lyricRetry.Reset();lyricDetails="";song=fresh;currentId=fresh.CacheKey;lyrics.Clear();lyricIndex=-999;lastPosition=Double.NaN;lastMove=DateTime.MinValue;position=snapshot==null?Double.NaN:snapshot.Position;
    Motion.Text(title,song.Title);Motion.Text(artist,song.Artist);SetCover(null);SetLyricTexts("","正在获取歌词…","","");lyricState="正在获取歌词…";
    LoadSongAssets(fresh,revision);
   }catch(Exception ex){if(revision==sourceRevision)Motion.Text(status,"暂时无法读取播放信息");Log(ex.Message);}finally{if(revision==sourceRevision)busy=false;}
  }
  async void LoadSongAssets(Song requested,int revision){
   LoadCurrentLyrics(requested,revision);
   try{var picture=await LoadCover(requested);if(revision!=sourceRevision||currentId!=requested.CacheKey||closingAnimated)return;SetCover(picture);}
   catch(Exception ex){Log(ex.Message);}
  }
  void CancelLyrics(){lyricRequestRevision++;lyricBusy=false;if(lyricCancellation!=null){lyricCancellation.Cancel();lyricCancellation.Dispose();lyricCancellation=null;}}
  async void LoadCurrentLyrics(Song requested,int revision,bool refresh=false){
   if(lyricBusy&&!refresh)return;if(refresh)CancelLyrics();lyricBusy=true;int requestRevision=++lyricRequestRevision;lyricCancellation=new CancellationTokenSource();var cancellation=lyricCancellation;
   if(refresh){lyricRetry.Reset();lyricState="正在重新获取歌词…";lyricIndex=-999;UpdateLyric(Double.IsNaN(position)?0:position+settings.Offset);}
   try{var result=await lyricService.Load(requested,refresh,cancellation.Token);if(revision!=sourceRevision||currentId!=requested.CacheKey||requestRevision!=lyricRequestRevision||closingAnimated)return;
    if(result.Lines.Count>0||lyrics.Count==0)lyrics=result.Lines;lyricDetails=result.Details;
    lyricState=lyrics.Count>0?"♪":result.Message+(result.Retryable?" · 将自动重试":" · 可在设置中重试");
    lyricRetry.Complete(lyrics.Count==0&&result.Retryable,DateTime.UtcNow);lyricIndex=-999;UpdateLyric(Double.IsNaN(position)?0:position+settings.Offset);
   }catch(OperationCanceledException){}catch(Exception ex){Log("歌词加载："+ex);if(requestRevision==lyricRequestRevision)lyricRetry.Complete(true,DateTime.UtcNow);}
   finally{if(requestRevision==lyricRequestRevision){lyricBusy=false;lyricCancellation=null;cancellation.Dispose();}}
  }
  static string Format(double x){if(Double.IsNaN(x)||x<0)return "--:--";int s=(int)x;return (s/60).ToString("00")+":"+(s%60).ToString("00");}
  async Task<BitmapImage> LoadCover(Song requested) {
   try{byte[] b=requested.CoverBytes;if(b==null){string url=requested.Cover;if(String.IsNullOrWhiteSpace(url))return null;url=url.Replace("http://","https://");var u=new Uri(url);if(!u.Host.EndsWith(".music.126.net",StringComparison.OrdinalIgnoreCase)&&!u.Host.Equals("music.126.net",StringComparison.OrdinalIgnoreCase))return null;
    string dir=assets.CacheDirectory;string file=Path.Combine(dir,requested.CacheKey+".jpg");try{if(File.Exists(file))b=File.ReadAllBytes(file);}catch{}if(b==null){b=await Download(url+"?param=240y240");try{Directory.CreateDirectory(dir);File.WriteAllBytes(file,b);}catch(Exception ex){Log("封面缓存："+ex.Message);}}}
    return await Task.Run(()=>{var image=new BitmapImage();image.BeginInit();image.StreamSource=new MemoryStream(b);image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=240;image.EndInit();image.Freeze();return image;});
   }catch(Exception ex){Log("封面："+ex.Message);return null;}
  }
  static async Task<byte[]> Download(string url){var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="CloudMusicFloat/1.0";request.Referer="https://music.163.com/";request.Timeout=8000;request.ReadWriteTimeout=8000;using(var timeout=new CancellationTokenSource(8000))using(timeout.Token.Register(()=>request.Abort()))using(var response=await request.GetResponseAsync())using(var input=response.GetResponseStream())using(var output=new MemoryStream()){await input.CopyToAsync(output);return output.ToArray();}}
  void UpdateLyric(double value) {
   int index=-1;for(int i=0;i<lyrics.Count;i++){if(lyrics[i].Time<=value)index=i;else break;}
   if(index==lyricIndex)return;lyricIndex=index;
   if(lyrics.Count==0){SetLyricTexts("",lyricState,"","");return;}
   if(index<0){SetLyricTexts("","♪ 前奏",lyrics[0].Text,"");return;}
   SetLyricTexts(index>0?lyrics[index-1].Text:"",String.IsNullOrWhiteSpace(lyrics[index].Text)?"♪":lyrics[index].Text,index+1<lyrics.Count?lyrics[index+1].Text:"",lyrics[index].Translation);
  }
  void SetLyricTexts(string before,string active,string after,string translated){Motion.Text(previous,before);Motion.Text(current,active);Motion.Text(next,after);Motion.Text(translation,translated);Motion.Text(compactLyric,active);}
  void ImportLrc(){if(song==null)return;var dialog=new OpenFileDialog{Filter="LRC 歌词|*.lrc|文本文件|*.txt",Title="导入《"+song.Title+"》的歌词"};if(dialog.ShowDialog(this)!=true)return;
   try{string text=File.ReadAllText(dialog.FileName);var parsed=Lyrics.Parse(text);if(parsed.Count==0){MessageBox.Show(this,"未找到时间标签，例如 [00:12.50]。","歌词格式");return;}bool saved=assets.Write(song.CacheKey+".lrc",text);CancelLyrics();lyricRetry.Reset();lyrics=parsed;lyricState="♪";lyricDetails=saved?"":"歌词已载入，缓存写入失败，重启后需重新导入。";lyricIndex=-999;UpdateLyric(Double.IsNaN(position)?0:position+settings.Offset);if(!saved)Motion.Text(status,lyricDetails);}catch(Exception ex){MessageBox.Show(this,ex.Message,"导入失败");}
  }
  void Log(string text){assets.Log(text);}
  void Snapshot(string file){UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(file))enc.Save(f);}
  async Task SettleUi(Func<bool> ready){for(int i=0;i<80&&!ready();i++)await Task.Delay(25);}
  public void AppearanceDemo(double width=440,double height=356){Title="AdoriA MUSIC · 外观预览";ToolTip=null;SetDimensions(width,height,false);settings.Theme="Blue";settings.Glass=true;settings.BackgroundOpacity=.35;Motion.Fade(backgroundLayer,.35,0);ApplyTheme(false);Motion.Text(artist,"AdoriA MUSIC");Motion.Text(title,"让音乐，自由变换");SetLyricTexts("拉动任意四角，自由改变形状","让音乐，随你的桌面变换","小窗保留歌词，展开显示更多","");time.Text="01:28 / 04:08";Motion.Text(status,"");SetProgress(.355);SetPlayback(false);
   var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(119,168,249),Color.FromRgb(65,88,175),45),null,new RectangleGeometry(new Rect(0,0,72,72),0,0)));group.Children.Add(new GeometryDrawing(Brushes.White,null,Geometry.Parse("M33,17 L52,13 L52,44 C52,52 39,55 39,48 C39,43 45,41 48,42 L48,22 L33,26 L33,48 C33,56 20,59 20,52 C20,47 26,45 29,46 L29,21 Z")));cover.Source=new DrawingImage(group);}
  public async Task VerifyUi(){
   string dir=Path.Combine(root,"ui-verification");Directory.CreateDirectory(dir);var checks=new Dictionary<string,object>();
   try{
    Motion.Text(artist,"AdoriA");Motion.Text(title,"让旋律慢慢靠近");SetLyricTexts("窗外的风轻轻经过","让旋律，慢慢靠近","下一刻，继续聆听","Let the melody come closer");Motion.Text(status,"");time.Text="01:24 / 03:48";SetPlayback(false);SetProgress(.37);
    string picture=Path.Combine(root,"..","outputs","CloudMusicFloat","cache","2746522670.jpg");if(File.Exists(picture)){var image=new BitmapImage(new Uri(Path.GetFullPath(picture)));SetCover(image);}
    await Task.Delay(600);Snapshot(Path.Combine(dir,"full.png"));
    checks["artistAboveTitle"]=artist.TranslatePoint(new Point(0,0),card).Y<title.TranslatePoint(new Point(0,0),card).Y;checks["defaultSubtitleBlank"]=compactLyricSlot.Visibility==Visibility.Collapsed;checks["brand"]=brand.Text.Contains("AdoriA MUSIC");
    settings.Compact=true;ApplyCompact(true);await Task.Delay(250);checks["compactHidesSecondaryContent"]=songInfo.Visibility==Visibility.Collapsed&&previous.Visibility==Visibility.Collapsed&&lyricsViewport.Visibility==Visibility.Visible;settings.Compact=false;ApplyCompact(true);await Task.Delay(250);checks["modePreservesCustomDimensions"]=Width==440&&Height==356;
    Motion.Text(current,"第一行");await Task.Delay(45);Motion.Text(current,"第二行");await Task.Delay(45);Motion.Text(current,"最终歌词");await Task.Delay(350);checks["rapidTextKeepsLatest"]=current.Text=="最终歌词"&&Math.Abs(current.Opacity-1)<.01;
    SetPlayback(true);await SettleUi(()=>pauseIcon.Opacity>.99&&playIcon.Opacity<.01);checks["playingShowsPause"]=pauseIcon.Opacity>.99&&playIcon.Opacity<.01&&String.Equals(AutomationProperties.GetName(playButton),"暂停");TogglePlayback();await SettleUi(()=>playIcon.Opacity>.99&&pauseIcon.Opacity<.01);checks["pausedShowsPlay"]=playIcon.Opacity>.99&&pauseIcon.Opacity<.01;
    SwitchPlayer("QQMusic");await Task.Delay(320);checks["qqSourceSelectable"]=sourceLabel.Text.Contains("QQ 音乐")&&title.Text.Contains("QQ 音乐")&&song==null&&mediaSnapshot==null&&playButton.IsEnabled==false;
    Snapshot(Path.Combine(dir,"qq-waiting.png"));SwitchPlayer("NetEase");await Task.Delay(35);SwitchPlayer("QQMusic");await Task.Delay(35);SwitchPlayer("NetEase");await Task.Delay(350);checks["rapidSourceSwitchKeepsLatest"]=settings.Player=="NetEase"&&sourceLabel.Text.Contains("网易云音乐")&&title.Text.Contains("网易云音乐")&&currentId==""&&lyrics.Count==0;
    var example=new Song{Title="龙卷风",Artist="G.E.M.邓紫棋",Album="龙卷风",Duration=248.888};var wrong=new Dictionary<string,object>{{"title","龙卷风"},{"interval",249},{"singer",new object[]{new Dictionary<string,object>{{"name","其他歌手"}}}}};var correct=new Dictionary<string,object>{{"title","龙卷风"},{"interval",249},{"singer",new object[]{new Dictionary<string,object>{{"name","G.E.M.邓紫棋"}}}}};checks["qqLyricsRejectWrongArtist"]=QQMusicAssets.PickMatch(example,new object[]{wrong})==null&&Object.ReferenceEquals(QQMusicAssets.PickMatch(example,new object[]{wrong,correct}),correct);
    checks["qqLyricsDecodeBase64AndTranslation"]=QQMusicAssets.Parse(Json.Write(new Dictionary<string,object>{{"lyric",Convert.ToBase64String(Encoding.UTF8.GetBytes("[00:01.00]Hello"))},{"trans","[00:01.00]你好"}})).First().Translation=="你好";
    var delayed=new TaskCompletionSource<string>();int downloads=0;string fixture=Json.Write(new{code=0,req_1=new{code=0,data=new{body=new{song=new{list=new[]{new{mid="fixture",title="测试歌曲",interval=30,singer=new[]{new{name="测试歌手"}}}}}}}}});
    lyricService=new LyricService(new AssetStore(Path.Combine(dir,Guid.NewGuid().ToString("N"),"cache")),(url,body)=>url.Contains("musicu.fcg")?Task.FromResult(fixture):++downloads==1?delayed.Task:Task.FromResult("{\"code\":0,\"lyric\":\"[00:01.00]新歌词\"}"));
    song=new Song{Source="QQMusic",Id="old",Title="测试歌曲",Artist="测试歌手",Duration=30};currentId=song.CacheKey;LoadCurrentLyrics(song,sourceRevision);SwitchPlayer("QQMusic");song=new Song{Source="QQMusic",Id="new",Title="测试歌曲",Artist="测试歌手",Duration=30};currentId=song.CacheKey;LoadCurrentLyrics(song,sourceRevision);delayed.SetResult("{\"code\":0,\"lyric\":\"[00:01.00]旧歌词\"}");await Task.Delay(30);
    checks["lateLyricsCannotReplaceNewSong"]=lyrics.Count==1&&lyrics[0].Text=="新歌词"&&!lyricBusy;
    lyrics.Clear();lyricService=new LyricService(new AssetStore(Path.Combine(dir,"failure","cache")),(url,body)=>{throw new WebException("test timeout",WebExceptionStatus.Timeout);});LoadCurrentLyrics(song,sourceRevision,true);await Task.Delay(30);checks["sameSongSchedulesRetry"]=!lyricBusy&&lyrics.Count==0&&lyricState.Contains("自动重试")&&(lyricRetry.Next-DateTime.UtcNow).TotalSeconds>4;
    SwitchPlayer("NetEase");checks["sourceSwitchClearsRetry"]=!lyricBusy&&!lyricRetry.Due(DateTime.UtcNow.AddDays(1));await Task.Delay(350);
    settings.Glass=false;settings.Theme="Black";ApplyTheme(false);var starting=((SolidColorBrush)white).Color;settings.Theme="White";ApplyTheme(true);await Task.Delay(90);checks["themeHasIntermediateColor"]=((SolidColorBrush)white).Color!=starting&&((SolidColorBrush)white).Color!=Palette.Get("White").Text;await Task.Delay(300);
    foreach(var palette in Palette.All){settings.Theme=palette.Key;ApplyTheme(true);await SettleUi(()=>((SolidColorBrush)white).Color==palette.Text&&((SolidColorBrush)accent).Color==palette.Accent&&surfaceTop.Color==palette.Top);checks["theme"+palette.Key]=((SolidColorBrush)white).Color==palette.Text&&((SolidColorBrush)accent).Color==palette.Accent&&surfaceTop.Color==palette.Top;Snapshot(Path.Combine(dir,"theme-"+palette.Key+".png"));}
    settings.Theme="White";ApplyTheme(true);await Task.Delay(35);settings.Theme="Blue";ApplyTheme(true);await Task.Delay(35);settings.Theme="Black";ApplyTheme(true);await Task.Delay(340);checks["rapidThemeSwitchKeepsLatest"]=((SolidColorBrush)white).Color==Palette.Get("Black").Text&&settings.Theme=="Black";
    checks["glassDisabledIsSolid"]=!glass.Active&&surfaceTop.Color.A==255;settings.Glass=true;settings.BackgroundOpacity=.35;ApplyTheme(false);checks["glassNativeOrSafeFallback"]=glass.Active?glass.LastResult!=0&&surfaceTop.Color.A==255:surfaceTop.Color.A==255;
    var restored=new JavaScriptSerializer().Deserialize<Settings>(Json.Write(settings));checks["appearanceSettingsRoundTrip"]=restored.Theme=="Black"&&restored.Glass;var legacy=new JavaScriptSerializer().Deserialize<Settings>("{\"Player\":\"QQMusic\"}");checks["oldSettingsHaveAppearanceDefaults"]=legacy.Theme=="Black"&&legacy.Glass&&legacy.Player=="QQMusic";
    int popupIndex=0;foreach(var popup in new[]{sourceMenu,appearanceMenu,settingsMenu}){popup.PlacementTarget=this;popup.IsOpen=true;popup.UpdateLayout();await Task.Delay(100);checks["customMenuOpens"+(++popupIndex)]=popup.IsOpen&&popup.Template!=null&&popup.Items.OfType<MenuItem>().All(item=>item.Template!=null);popup.IsOpen=false;}checks["rightClickMenuRemoved"]=ContextMenu==null;checks["dedicatedSettingsButton"]=settingsButton!=null&&controlStrip.Children.Contains(settingsButton);
    settings.Theme="White";ApplyTheme(false);appearanceMenu.IsOpen=true;appearanceMenu.UpdateLayout();await Task.Delay(100);checks["menuFollowsTheme"]=((SolidColorBrush)appearanceMenu.Background).Color==Palette.Get("White").Top&&((SolidColorBrush)appearanceMenu.Foreground).Color==Palette.Get("White").Text;appearanceMenu.IsOpen=false;settings.Theme="Black";ApplyTheme(false);
    await VerifyAppearance(checks,dir);Snapshot(Path.Combine(dir,"final.png"));checks["passed"]=checks.Values.All(v=>v is bool&&(bool)v);
   }catch(Exception ex){checks["error"]=ex.ToString();checks["passed"]=false;}
   File.WriteAllText(Path.Combine(dir,"result.json"),Json.Write(checks),Encoding.UTF8);Close();
  }
  public async Task Capture(string filename="preview.png") {for(int i=0;i<120&&busy;i++)await Task.Delay(100);if(!busy)await RefreshSong();for(int i=0;i<100&&lyricState=="正在获取歌词…";i++)await Task.Delay(100);await Task.Delay(1300);Snapshot(Path.Combine(root,filename));}
 }
 static class Program {
  [STAThread]static void Main(string[] args){Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline),new FrameworkPropertyMetadata((int?)120));ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   if(args.Contains("--appearance-demo")||args.Contains("--appearance-lab")){RunAppearanceDemo(args.Contains("--appearance-lab"),args);return;}
   if(args.Contains("--probe-media")){ProbeMedia();return;}
   if(args.Contains("--verify-live-control")){VerifyMediaControl(args.Contains("NetEase")?"cloudmusic":"QQMusic");return;}
   if(args.Contains("--verify-ui")){var testApp=new Application();var testWindow=new Overlay(true);testWindow.Loaded+=async(s,e)=>await testWindow.VerifyUi();testApp.Run(testWindow);return;}
   if(args.Contains("--probe")){Probe();return;}bool created;using(var mutex=new Mutex(true,"Local\\CloudMusicFloat_2026",out created)){if(!created)return;var app=new Application();var window=new Overlay(false,args.Contains("QQMusic")?"QQMusic":args.Contains("NetEase")?"NetEase":null);if(args.Contains("--background-capture")){window.SilentRun=true;window.Topmost=false;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=window.Top=-10000;window.Loaded+=async(s,e)=>{await window.Capture(args.Contains("NetEase")?"网易云模式预览.png":"QQ音乐模式预览.png");window.Close();};}else if(args.Contains("--capture"))window.Loaded+=async(s,e)=>await window.Capture();app.Run(window);}
  }
  static void RunAppearanceDemo(bool lab,string[] args){var demoApp=new Application();demoApp.DispatcherUnhandledException+=(s,e)=>{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-error.txt"),e.Exception.ToString());e.Handled=true;demoApp.Shutdown(1);};var area=SystemParameters.WorkArea;var demo=new Overlay(true);demo.Left=area.Left+400;demo.Top=area.Top+130;demo.ShowInTaskbar=true;demo.ShowActivated=true;double w=440,h=356;string size=args.FirstOrDefault(a=>a.StartsWith("--demo-size="));if(size!=null){var parts=size.Substring(12).Split('x');double parsedW,parsedH;if(parts.Length==2&&Double.TryParse(parts[0],out parsedW)&&Double.TryParse(parts[1],out parsedH)){w=parsedW;h=parsedH;}}demo.Loaded+=async(s,e)=>{demo.AppearanceDemo(w,h);if(args.Contains("--measure-frames")){await Task.Delay(500);await demo.MeasureAnimationFrames();}};if(!lab){demoApp.Run(demo);return;}
   var host=new Window{Title="AdoriA MUSIC · 玻璃效果验证背景",Width=Math.Max(1000,w+440),Height=Math.Max(720,h+180),Left=area.Left+60,Top=area.Top+60,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.White};var canvas=new Canvas();var colors=new[]{Color.FromRgb(235,150,95),Color.FromRgb(64,136,189),Color.FromRgb(107,176,151),Color.FromRgb(185,143,192)};for(int y=0;y<Math.Ceiling(host.Height/240);y++)for(int x=0;x<Math.Ceiling(host.Width/250);x++){var cell=new Border{Width=250,Height=240,Background=new SolidColorBrush(colors[(x+y)%4])};Canvas.SetLeft(cell,x*250);Canvas.SetTop(cell,y*240);canvas.Children.Add(cell);}if(args.Contains("--corner-background")){canvas.Children.Clear();for(int y=0;y<Math.Ceiling(host.Height/100);y++)for(int x=0;x<Math.Ceiling(host.Width/100);x++){var tile=new Border{Width=100,Height=100,Background=(x+y)%2==0?Brushes.White:Brushes.Black};Canvas.SetLeft(tile,x*100);Canvas.SetTop(tile,y*100);canvas.Children.Add(tile);}}host.Content=canvas;host.Loaded+=(s,e)=>{demo.Owner=host;demo.Show();demo.Activate();};demo.Closed+=(s,e)=>host.Close();demoApp.Run(host);
  }
  static void ProbeMedia(){var results=new Dictionary<string,object>();using(var r=new SystemMediaReader())try{
   var snapshot=r.Read("QQMusic");results["error"]=r.Error;results["snapshot"]=snapshot;if(snapshot!=null&&snapshot.Cover!=null){File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"qq-cover-probe.jpg"),snapshot.Cover);snapshot.Cover=null;}
   if(snapshot!=null&&!String.IsNullOrWhiteSpace(snapshot.Title)){var sample=new Song{Source="QQMusic",Id=QQMusicAssets.Identity(snapshot.Title,snapshot.Artist,snapshot.Album),Title=snapshot.Title,Artist=snapshot.Artist,Album=snapshot.Album,Duration=snapshot.Duration};string cache=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cache");Directory.CreateDirectory(cache);var lines=QQMusicAssets.LoadLyrics(sample,cache).GetAwaiter().GetResult();results["lyricLines"]=lines.Count;results["lyricSample"]=lines.Take(3).ToArray();}
  }catch(Exception ex){results["exception"]=ex.ToString();}File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"media-probe.json"),Json.Write(results),Encoding.UTF8);}
  static void VerifyMediaControl(string hint){var results=new Dictionary<string,object>();using(var r=new SystemMediaReader()){
   MediaSnapshot initial=r.Read(hint);bool sent=false;try{if(initial==null||String.IsNullOrEmpty(initial.Title))throw new Exception(r.Error);results["title"]=initial.Title;results["initialPlaying"]=initial.Playing;sent=r.Toggle(hint);results["commandSent"]=sent;bool changed=false;for(int i=0;sent&&i<20;i++){Thread.Sleep(200);var now=r.Read(hint);if(now.Title==initial.Title&&now.Playing!=initial.Playing){changed=true;break;}}results["stateChanged"]=changed;}
   catch(Exception ex){results["error"]=ex.Message;}finally{bool restored=false;if(initial!=null&&sent){var now=r.Read(hint);if(now.Title==initial.Title){if(now.Playing!=initial.Playing)r.Toggle(hint);for(int i=0;i<20;i++){Thread.Sleep(200);now=r.Read(hint);if(now.Title==initial.Title&&now.Playing==initial.Playing){restored=true;break;}}}}results["restored"]=restored;results["passed"]=results.ContainsKey("stateChanged")&&(bool)results["stateChanged"]&&restored;File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"media-control.json"),Json.Write(results),Encoding.UTF8);}
  }}
  static void Probe(){var root=AppDomain.CurrentDomain.BaseDirectory;var results=new Dictionary<string,object>();
   try{string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);var paths=new List<string>{Path.Combine(local,"NetEase","CloudMusic","Library","webdb.dat")};foreach(var d in Directory.GetDirectories(Path.Combine(local,"Packages"),"1F8B0F94.122165AE053F_*"))paths.Add(Path.Combine(d,"LocalCache","Local","NetEase","CloudMusic","Library","webdb.dat"));
    string file=paths.FirstOrDefault(File.Exists);results["databaseFound"]=file!=null;if(file!=null){var song=Song.Parse(Db.Scalar(file,"SELECT jsonStr FROM historyTracks ORDER BY playtime DESC LIMIT 1"));results["title"]=song.Title;results["artist"]=song.Artist;results["duration"]=song.Duration;using(var r=new PlaybackReader()){double pos=r.Read(song.Duration);results["position"]=Double.IsNaN(pos)?(object)null:pos;results["readerError"]=r.Error;results["pid"]=r.Pid;}}
    results["lrcParserPass"]=Lyrics.Parse("[00:01.50][00:04.00]test\n[00:05.00]next").Count==3;
   }catch(Exception ex){results["error"]=ex.ToString();}File.WriteAllText(Path.Combine(root,"probe-result.json"),Json.Write(results),Encoding.UTF8);
  }
 }
}






