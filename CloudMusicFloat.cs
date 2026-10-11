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

namespace CloudMusicFloat {
 public class Song {
  public string Id="", Title="", Artist="", Album="", Cover=""; public double Duration;
  public static Song Parse(string json) {
   var d=Json.Read(json);var album=Json.Obj(d,"album");var artists=Json.Arr(d,"artists");
   return new Song {Id=Json.Str(d,"id"),Title=Json.Str(d,"name"),Artist=String.Join(" / ",artists.Select(x=>Json.Str(x as Dictionary<string,object>,"name"))),Album=Json.Str(album,"name"),Cover=Json.Str(album,"picUrl"),Duration=Json.Num(d,"duration")/1000.0};
  }
 }
 public static class Json {
  public static Dictionary<string,object> Read(string s){return new JavaScriptSerializer{MaxJsonLength=8000000}.Deserialize<Dictionary<string,object>>(s);}
  public static string Write(object o){return new JavaScriptSerializer().Serialize(o);}
  public static string Str(Dictionary<string,object>d,string k){return d!=null&&d.ContainsKey(k)&&d[k]!=null?Convert.ToString(d[k]):"";}
  public static double Num(Dictionary<string,object>d,string k){double v;return Double.TryParse(Str(d,k),out v)?v:0;}
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
 public class Settings {public double Left=Double.NaN,Top=Double.NaN,Opacity=.96,Offset=0;public bool Compact=false,Topmost=true;}
 public class Overlay : Window {
  readonly string root=AppDomain.CurrentDomain.BaseDirectory;string dataRoot,dbPath,currentId="";Song song;Settings settings;
  PlaybackReader reader=new PlaybackReader();List<LyricLine> lyrics=new List<LyricLine>();
  TextBlock title,artist,compactLyric,previous,current,next,translation,time,status,brand,compactLabel;Image cover;Border artwork,progressFill,lyricsViewport,compactLyricSlot;Grid progressTrack;StackPanel lyricsPanel;Button compactButton,pinButton,playButton;
  System.Windows.Shapes.Path pinIcon,playIcon,pauseIcon;RotateTransform pinRotation;TranslateTransform lyricShift;Border card;int modeRevision,coverRevision;bool? playbackState;double progressTarget=-1;bool closingAnimated,isVerification;
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
  public Overlay(bool verification=false) {
   isVerification=verification;Title="AdoriA MUSIC · 悬浮窗"; Width=440;Height=356;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=true;
   try{settings=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"settings.json")));}catch{settings=new Settings();}
   settings.Opacity=Math.Max(.45,Math.Min(1,settings.Opacity));Opacity=settings.Opacity;Topmost=settings.Topmost;
   var area=SystemParameters.WorkArea;Left=Double.IsNaN(settings.Left)?area.Right-Width-28:Math.Max(area.Left,Math.Min(area.Right-Width,settings.Left));Top=Double.IsNaN(settings.Top)?area.Top+90:Math.Max(area.Top,Math.Min(area.Bottom-Height,settings.Top));
   if(isVerification){settings.Compact=false;settings.Topmost=false;}Build();ApplyCompact(false);if(!isVerification)FindData();
   MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed){try{DragMove();}catch{}}};
   Closing+=(s,e)=>{if(!closingAnimated&&!isVerification){e.Cancel=true;closingAnimated=true;clock.Stop();Motion.Fade(card,0,150);var scale=(ScaleTransform)card.RenderTransform;Motion.To(scale,ScaleTransform.ScaleXProperty,.97,150);Motion.To(scale,ScaleTransform.ScaleYProperty,.97,150);Later(170,()=>Close());return;}Save();reader.Dispose();};
   KeyDown+=(s,e)=>{if(e.Key==Key.Escape)Close();};
   clock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};clock.Tick+=Tick;if(!isVerification)clock.Start();
   Loaded+=async(s,e)=>{card.Opacity=0;var scale=(ScaleTransform)card.RenderTransform;scale.ScaleX=scale.ScaleY=.97;Motion.Fade(card,1,230);Motion.To(scale,ScaleTransform.ScaleXProperty,1,230);Motion.To(scale,ScaleTransform.ScaleYProperty,1,230);if(!isVerification)await RefreshSong();};
  }
  TextBlock Text(string value,double size,Brush color){return new TextBlock{Text=value,FontSize=size,Foreground=color,FontFamily=new FontFamily("Microsoft YaHei UI"),TextTrimming=TextTrimming.CharacterEllipsis};}
  void Later(int ms,Action action){var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(s,e)=>{timer.Stop();action();};timer.Start();}
  Button Button(object label,string tip,Action action){
   var background=new SolidColorBrush(Colors.Transparent);var scale=new ScaleTransform(1,1);
   var b=new Button{Content=label,ToolTip=tip,Width=27,Height=25,Margin=new Thickness(3,0,0,0),Background=background,Foreground=muted,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,FontSize=16,RenderTransform=scale,RenderTransformOrigin=new Point(.5,.5)};
   b.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border CornerRadius='7' Background='{TemplateBinding Background}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
   AutomationProperties.SetName(b,tip);
   b.MouseEnter+=(s,e)=>{background.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(Color.FromArgb(40,145,171,240),TimeSpan.FromMilliseconds(150)));Motion.To(scale,ScaleTransform.ScaleXProperty,1.07,150);Motion.To(scale,ScaleTransform.ScaleYProperty,1.07,150);};
   b.MouseLeave+=(s,e)=>{background.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(Colors.Transparent,TimeSpan.FromMilliseconds(180)));Motion.To(scale,ScaleTransform.ScaleXProperty,1,180);Motion.To(scale,ScaleTransform.ScaleYProperty,1,180);};
   b.PreviewMouseLeftButtonDown+=(s,e)=>{Motion.To(scale,ScaleTransform.ScaleXProperty,.92,90);Motion.To(scale,ScaleTransform.ScaleYProperty,.92,90);};
   b.PreviewMouseLeftButtonUp+=(s,e)=>{Motion.To(scale,ScaleTransform.ScaleXProperty,b.IsMouseOver?1.07:1,150);Motion.To(scale,ScaleTransform.ScaleYProperty,b.IsMouseOver?1.07:1,150);};
   b.Click+=(s,e)=>{e.Handled=true;action();};return b;
  }
  void Build() {
   var outer=new Border{Margin=new Thickness(8),CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(75,135,159,220)),Background=new LinearGradientBrush(Color.FromArgb(248,23,30,48),Color.FromArgb(248,13,17,29),70),Padding=new Thickness(22,16,22,18),RenderTransform=new ScaleTransform(1,1),RenderTransformOrigin=new Point(.5,.5)};card=outer;
   outer.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=16,ShadowDepth=3,Opacity=.35,Color=Colors.Black};Content=outer;
   var layout=new Grid();outer.Child=layout;layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var top=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,15)};Grid.SetRow(top,0);layout.Children.Add(top);
   var controls=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Right);top.Children.Add(controls);
   pinRotation=new RotateTransform(Topmost?0:-35,8,8);pinIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M5,2 L11,2 L10,7 L13,10 L3,10 L6,7 Z M8,10 L8,15"),Width=16,Height=16,Stroke=new SolidColorBrush(Topmost?((SolidColorBrush)accent).Color:((SolidColorBrush)muted).Color),StrokeThickness=1.35,StrokeLineJoin=PenLineJoin.Round,Fill=Brushes.Transparent,RenderTransform=pinRotation};
   pinButton=Button(pinIcon,"切换始终置顶",()=>{Topmost=!Topmost;settings.Topmost=Topmost;Motion.To(pinRotation,RotateTransform.AngleProperty,Topmost?0:-35,220);((SolidColorBrush)pinIcon.Stroke).BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(((SolidColorBrush)(Topmost?accent:muted)).Color,TimeSpan.FromMilliseconds(220)));Save();});controls.Children.Add(pinButton);
   compactLabel=Text("−",16,muted);compactButton=Button(compactLabel,"精简 / 完整模式",()=>{settings.Compact=!settings.Compact;ApplyCompact(true);Save();});controls.Children.Add(compactButton);controls.Children.Add(Button("×","关闭悬浮窗",()=>Close()));
   brand=Text("♪   AdoriA MUSIC",10,accent);brand.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(brand);
   var info=new Grid{Margin=new Thickness(0,0,0,10)};info.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(80)});info.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(info,1);layout.Children.Add(info);
   artwork=new Border{Width=72,Height=72,CornerRadius=new CornerRadius(14),Background=new SolidColorBrush(Color.FromRgb(38,48,73)),HorizontalAlignment=HorizontalAlignment.Left,ClipToBounds=true};
   cover=new Image{Stretch=Stretch.UniformToFill};cover.Clip=new RectangleGeometry(new Rect(0,0,72,72),14,14);artwork.Child=cover;info.Children.Add(artwork);
   var meta=new StackPanel{Margin=new Thickness(12,3,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(meta,1);info.Children.Add(meta);
   artist=Text("打开网易云，播放一首歌",11,muted);title=Text("等待网易云音乐",17,white);title.FontWeight=FontWeights.SemiBold;title.ToolTip="歌曲名";title.Margin=new Thickness(0,4,0,0);compactLyric=Text("",11,accent);compactLyricSlot=new Border{Height=16,Margin=new Thickness(0,5,0,0),Visibility=Visibility.Hidden,Child=compactLyric};meta.Children.Add(artist);meta.Children.Add(title);meta.Children.Add(compactLyricSlot);
   lyricsViewport=new Border{ClipToBounds=true};Grid.SetRow(lyricsViewport,2);layout.Children.Add(lyricsViewport);lyricShift=new TranslateTransform();lyricsPanel=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,0,10),RenderTransform=lyricShift};lyricsViewport.Child=lyricsPanel;
   previous=Text("",12,muted);previous.Opacity=.55;current=Text("让音乐在桌面停留",19,white);current.FontWeight=FontWeights.SemiBold;current.TextWrapping=TextWrapping.Wrap;current.MaxHeight=58;current.Margin=new Thickness(0,9,0,0);
   translation=Text("",11,muted);translation.Margin=new Thickness(0,5,0,0);next=Text("",12,muted);next.Opacity=.65;next.Margin=new Thickness(0,9,0,0);lyricsPanel.Children.Add(previous);lyricsPanel.Children.Add(current);lyricsPanel.Children.Add(translation);lyricsPanel.Children.Add(next);
   var bottom=new StackPanel();Grid.SetRow(bottom,3);layout.Children.Add(bottom);
   progressTrack=new Grid{Height=3,Background=new SolidColorBrush(Color.FromRgb(47,56,78)),Margin=new Thickness(0,0,0,9)};progressFill=new Border{Height=3,Width=0,CornerRadius=new CornerRadius(2),Background=accent,HorizontalAlignment=HorizontalAlignment.Left};progressTrack.Children.Add(progressFill);bottom.Children.Add(progressTrack);
   var timeRow=new DockPanel{LastChildFill=true,Height=22};time=Text("--:-- / --:--",10,muted);time.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(time,Dock.Right);timeRow.Children.Add(time);
   var playGlyph=new Grid{Width=16,Height=16};playIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M5,3 L13,8 L5,13 Z"),Fill=accent,Opacity=1};pauseIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M4,3 L7,3 L7,13 L4,13 Z M10,3 L13,3 L13,13 L10,13 Z"),Fill=accent,Opacity=0};playGlyph.Children.Add(playIcon);playGlyph.Children.Add(pauseIcon);
   playButton=Button(playGlyph,"播放",TogglePlayback);playButton.Width=25;playButton.Height=22;playButton.Margin=new Thickness(-5,0,5,0);playButton.IsEnabled=false;DockPanel.SetDock(playButton,Dock.Left);timeRow.Children.Add(playButton);status=Text("正在连接…",10,muted);status.VerticalAlignment=VerticalAlignment.Center;timeRow.Children.Add(status);bottom.Children.Add(timeRow);
   var menu=new ContextMenu();
   AddMenu(menu,"透明度 100%",()=>SetOpacity(1));AddMenu(menu,"透明度 85%",()=>SetOpacity(.85));AddMenu(menu,"透明度 65%",()=>SetOpacity(.65));menu.Items.Add(new Separator());
   AddMenu(menu,"歌词提前 0.5 秒",()=>{settings.Offset+=.5;lyricIndex=-999;Save();});AddMenu(menu,"歌词延后 0.5 秒",()=>{settings.Offset-=.5;lyricIndex=-999;Save();});AddMenu(menu,"重置歌词偏移",()=>{settings.Offset=0;lyricIndex=-999;Save();});
   AddMenu(menu,"导入当前歌曲的 LRC 歌词…",ImportLrc);AddMenu(menu,"重新连接",()=>{reader.Dispose();currentId="";FindData();});menu.Items.Add(new Separator());AddMenu(menu,"退出",()=>Close());ContextMenu=menu;
   ToolTip="按住空白处拖动 · 右键打开设置 · Esc 退出";
  }
  void AddMenu(ContextMenu m,string label,Action action){var item=new MenuItem{Header=label};item.Click+=(s,e)=>action();m.Items.Add(item);}
  void SetOpacity(double x){Motion.To(this,Window.OpacityProperty,x,240);settings.Opacity=x;Save();}
  void ApplyCompact(bool animated=false){
   int revision=++modeRevision;bool compact=settings.Compact;if(animated)Motion.Text(compactLabel,compact?"+":"−");else compactLabel.Text=compact?"+":"−";
   if(!animated){Height=compact?214:356;lyricsViewport.Visibility=compact?Visibility.Collapsed:Visibility.Visible;lyricsPanel.Opacity=compact?0:1;compactLyricSlot.Visibility=compact?Visibility.Visible:Visibility.Hidden;compactLyricSlot.Opacity=compact?1:0;return;}
   Motion.To(this,Window.HeightProperty,compact?214:356,300);
   if(compact){Motion.Fade(lyricsPanel,0,120);Motion.To(lyricShift,TranslateTransform.YProperty,-6,150);compactLyricSlot.Visibility=Visibility.Visible;Motion.Fade(compactLyricSlot,1,240);Later(310,()=>{if(revision==modeRevision&&settings.Compact)lyricsViewport.Visibility=Visibility.Collapsed;});}
   else{lyricsViewport.Visibility=Visibility.Visible;Motion.To(lyricShift,TranslateTransform.YProperty,0,280);Motion.Fade(lyricsPanel,1,280);Motion.Fade(compactLyricSlot,0,140);Later(160,()=>{if(revision==modeRevision&&!settings.Compact)compactLyricSlot.Visibility=Visibility.Hidden;});}
  }
  public static bool SendPlaybackCommand(){if(PlayerWindow()==IntPtr.Zero)return false;var keys=new[]{new MediaInput{Type=1,Key=new MediaKey{Key=0xb3}},new MediaInput{Type=1,Key=new MediaKey{Key=0xb3,Flags=2}}};return SendInput(2,keys,Marshal.SizeOf(typeof(MediaInput)))==2;}
  void TogglePlayback(){if(isVerification){SetPlayback(!(playbackState??false));return;}if(!SendPlaybackCommand())Motion.Text(status,"播放控制暂不可用");}
  void SetPlayback(bool? playing){if(playbackState==playing&&playButton.IsEnabled==(playing!=null))return;playbackState=playing;playButton.IsEnabled=playing!=null;Motion.Fade(playButton,playing==null?.4:1,180);Motion.Fade(playIcon,playing==true?0:1,180);Motion.Fade(pauseIcon,playing==true?1:0,180);string label=playing==true?"暂停":"播放";playButton.ToolTip=label;AutomationProperties.SetName(playButton,label);}
  void SetProgress(double value){double target=progressTrack.ActualWidth*Math.Max(0,Math.Min(1,value));if(Math.Abs(target-progressTarget)<.01)return;progressTarget=target;Motion.To(progressFill,FrameworkElement.WidthProperty,target,220);}
  void SetCover(ImageSource value){int revision=++coverRevision;Motion.Fade(cover,0,100);Later(110,()=>{if(revision!=coverRevision)return;cover.Source=value;Motion.Fade(cover,1,220);});}
  void Save(){if(isVerification)return;try{settings.Left=Left;settings.Top=Top;File.WriteAllText(Path.Combine(root,"settings.json"),Json.Write(settings),Encoding.UTF8);}catch{}}
  void FindData() {
   var candidates=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NetEase","CloudMusic")};
   string packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
   if(Directory.Exists(packages)){foreach(var d in Directory.GetDirectories(packages,"1F8B0F94.122165AE053F_*"))candidates.Add(Path.Combine(d,"LocalCache","Local","NetEase","CloudMusic"));}
   dataRoot=candidates.FirstOrDefault(d=>File.Exists(Path.Combine(d,"Library","webdb.dat")));dbPath=dataRoot==null?null:Path.Combine(dataRoot,"Library","webdb.dat");
  }
  async void Tick(object sender,EventArgs e) {
   ticks++;if(ticks%4==0&&!busy)await RefreshSong();
   if(song==null)return;
   position=reader.Read(song.Duration);
   if(!Double.IsNaN(position)){
    if(!Double.IsNaN(lastPosition)&&Math.Abs(position-lastPosition)>.005)lastMove=DateTime.UtcNow;
    lastPosition=position;bool playing=(DateTime.UtcNow-lastMove).TotalSeconds<1.1;
    SetPlayback(playing);Motion.Text(status,"");playButton.ToolTip=(playing?"暂停":"播放")+(Math.Abs(settings.Offset)>.01?" · 歌词偏移 "+settings.Offset.ToString("+0.0;-0.0")+"s":"");
    time.Text=Format(position)+" / "+Format(song.Duration);SetProgress(position/Math.Max(1,song.Duration));UpdateLyric(position+settings.Offset);
   }else{SetPlayback(null);Motion.Text(status,reader.Error);time.Text="--:-- / "+Format(song.Duration);SetProgress(0);}
  }
  async Task RefreshSong() {
   busy=true;
   try{
    var fresh=await Task.Run(()=>{
     if(Process.GetProcessesByName("cloudmusic").Length==0)return (Song)null;
     if(dbPath==null)FindData();if(dbPath==null)return (Song)null;
     return ReadCurrentSong();
    });
    if(fresh==null){if(song!=null||currentId!=""){song=null;currentId="";Motion.Text(title,"等待网易云音乐");Motion.Text(artist,"打开网易云并播放一首歌曲");SetCover(null);lyrics.Clear();SetLyricTexts("","等待播放歌曲","","");}time.Text="--:-- / --:--";SetPlayback(null);Motion.Text(status,"未连接");SetProgress(0);reader.Dispose();return;}
    if(fresh.Id==currentId)return;
    song=fresh;currentId=fresh.Id;string requested=currentId;lyrics.Clear();lyricIndex=-999;lastPosition=Double.NaN;lastMove=DateTime.MinValue;
    Motion.Text(title,song.Title);title.ToolTip=song.Title;Motion.Text(artist,song.Artist);artist.ToolTip=song.Artist;SetCover(null);SetLyricTexts("","正在获取歌词…","","");lyricState="正在获取歌词…";
    var coverTask=LoadCover(song.Cover);var lyricTask=LoadLyrics(song.Id);
    await Task.WhenAll(coverTask,lyricTask);
    if(currentId!=requested)return;
    SetCover(coverTask.Result);lyrics=lyricTask.Result;lyricState=lyrics.Count>0?"♪":"暂无歌词 · 可右键导入 LRC";lyricIndex=-999;UpdateLyric(Double.IsNaN(position)?0:position+settings.Offset);
   }catch(Exception ex){Motion.Text(status,"暂时无法读取播放信息");Log(ex.Message);}finally{busy=false;}
  }
  static string Format(double x){if(Double.IsNaN(x)||x<0)return "--:--";int s=(int)x;return (s/60).ToString("00")+":"+(s%60).ToString("00");}
  async Task<BitmapImage> LoadCover(string url) {
   try{if(String.IsNullOrWhiteSpace(url))return null;url=url.Replace("http://","https://");var u=new Uri(url);if(!u.Host.EndsWith("music.126.net",StringComparison.OrdinalIgnoreCase))return null;
    string dir=Path.Combine(root,"cache");Directory.CreateDirectory(dir);string file=Path.Combine(dir,currentId+".jpg");byte[] b=File.Exists(file)?File.ReadAllBytes(file):await Download(url+"?param=240y240");if(!File.Exists(file))File.WriteAllBytes(file,b);
    return await Task.Run(()=>{var image=new BitmapImage();image.BeginInit();image.StreamSource=new MemoryStream(b);image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=240;image.EndInit();image.Freeze();return image;});
   }catch(Exception ex){Log("封面："+ex.Message);return null;}
  }
  async Task<List<LyricLine>> LoadLyrics(string id) {
   try{string dir=Path.Combine(root,"cache");Directory.CreateDirectory(dir);string custom=Path.Combine(dir,id+".lrc");if(File.Exists(custom))return Lyrics.Parse(File.ReadAllText(custom));
    string file=Path.Combine(dir,id+".lyrics.json");string json;if(File.Exists(file))json=File.ReadAllText(file);else{json=Encoding.UTF8.GetString(await Download("https://music.163.com/api/song/lyric?id="+Uri.EscapeDataString(id)+"&lv=-1&kv=-1&tv=-1"));var parsed=Json.Read(json);if(Json.Num(parsed,"code")!=200)throw new IOException("歌词服务暂不可用");File.WriteAllText(file,json,Encoding.UTF8);}
    return Lyrics.FromJson(json);
   }catch(Exception ex){Log("歌词："+ex.Message);return new List<LyricLine>();}
  }
  static async Task<byte[]> Download(string url){var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="CloudMusicFloat/1.0";request.Referer="https://music.163.com/";request.Timeout=8000;request.ReadWriteTimeout=8000;using(var timeout=new CancellationTokenSource(8000))using(timeout.Token.Register(()=>request.Abort()))using(var response=await request.GetResponseAsync())using(var input=response.GetResponseStream())using(var output=new MemoryStream()){await input.CopyToAsync(output);return output.ToArray();}}
  void UpdateLyric(double value) {
   int index=-1;for(int i=0;i<lyrics.Count;i++){if(lyrics[i].Time<=value)index=i;else break;}
   if(index==lyricIndex)return;lyricIndex=index;
   if(lyrics.Count==0){SetLyricTexts("",lyricState,"","");return;}
   if(index<0){SetLyricTexts("","♪ 前奏",lyrics[0].Text,"");return;}
   SetLyricTexts(index>0?lyrics[index-1].Text:"",String.IsNullOrWhiteSpace(lyrics[index].Text)?"♪":lyrics[index].Text,index+1<lyrics.Count?lyrics[index+1].Text:"",lyrics[index].Translation);
  }
  void SetLyricTexts(string before,string active,string after,string translated){Motion.Text(previous,before);Motion.Text(current,active);Motion.Text(next,after);Motion.Text(translation,translated);Motion.Text(compactLyric,active);compactLyric.ToolTip=active;}
  void ImportLrc(){if(song==null)return;var dialog=new OpenFileDialog{Filter="LRC 歌词|*.lrc|文本文件|*.txt",Title="导入《"+song.Title+"》的歌词"};if(dialog.ShowDialog(this)!=true)return;
   try{string text=File.ReadAllText(dialog.FileName);var parsed=Lyrics.Parse(text);if(parsed.Count==0){MessageBox.Show(this,"未找到时间标签，例如 [00:12.50]。","歌词格式");return;}Directory.CreateDirectory(Path.Combine(root,"cache"));File.WriteAllText(Path.Combine(root,"cache",song.Id+".lrc"),text,Encoding.UTF8);lyrics=parsed;lyricIndex=-999;}catch(Exception ex){MessageBox.Show(this,ex.Message,"导入失败");}
  }
  void Log(string text){try{File.AppendAllText(Path.Combine(root,"diagnostic.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine,Encoding.UTF8);}catch{}}
  void Snapshot(string file){UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(file))enc.Save(f);}
  public async Task VerifyUi(){
   string dir=Path.Combine(root,"ui-verification");Directory.CreateDirectory(dir);var checks=new Dictionary<string,object>();
   try{
    Motion.Text(artist,"AdoriA");Motion.Text(title,"让旋律慢慢靠近");SetLyricTexts("窗外的风轻轻经过","让旋律，慢慢靠近","下一刻，继续聆听","Let the melody come closer");Motion.Text(status,"");time.Text="01:24 / 03:48";SetPlayback(false);SetProgress(.37);
    string picture=Path.Combine(root,"..","outputs","CloudMusicFloat","cache","2746522670.jpg");if(File.Exists(picture)){var image=new BitmapImage(new Uri(Path.GetFullPath(picture)));SetCover(image);}
    await Task.Delay(600);Snapshot(Path.Combine(dir,"full.png"));
    checks["artistAboveTitle"]=artist.TranslatePoint(new Point(0,0),card).Y<title.TranslatePoint(new Point(0,0),card).Y;checks["defaultSubtitleBlank"]=compactLyricSlot.Visibility==Visibility.Hidden;checks["brand"]=brand.Text.Contains("AdoriA MUSIC");
    settings.Compact=true;ApplyCompact(true);await Task.Delay(110);checks["modeHasIntermediateHeight"]=Height>214&&Height<356;Snapshot(Path.Combine(dir,"transition.png"));await Task.Delay(280);Snapshot(Path.Combine(dir,"compact.png"));checks["compactLyricVisible"]=compactLyricSlot.Visibility==Visibility.Visible&&compactLyric.Text==current.Text;checks["compactHeight"]=Math.Abs(Height-214)<.1;
    settings.Compact=false;ApplyCompact(true);await Task.Delay(40);settings.Compact=true;ApplyCompact(true);await Task.Delay(40);settings.Compact=false;ApplyCompact(true);await Task.Delay(400);checks["rapidToggleKeepsLatestMode"]=Math.Abs(Height-356)<.1&&lyricsViewport.Visibility==Visibility.Visible&&compactLyricSlot.Visibility==Visibility.Hidden;
    Motion.Text(current,"第一行");await Task.Delay(45);Motion.Text(current,"第二行");await Task.Delay(45);Motion.Text(current,"最终歌词");await Task.Delay(350);checks["rapidTextKeepsLatest"]=current.Text=="最终歌词"&&Math.Abs(current.Opacity-1)<.01;
    SetPlayback(true);await Task.Delay(220);checks["playingShowsPause"]=pauseIcon.Opacity>.99&&playIcon.Opacity<.01&&String.Equals(playButton.ToolTip,"暂停");TogglePlayback();await Task.Delay(220);checks["pausedShowsPlay"]=playIcon.Opacity>.99&&pauseIcon.Opacity<.01;
    Snapshot(Path.Combine(dir,"final.png"));checks["passed"]=checks.Values.All(v=>v is bool&&(bool)v);
   }catch(Exception ex){checks["error"]=ex.ToString();checks["passed"]=false;}
   File.WriteAllText(Path.Combine(dir,"result.json"),Json.Write(checks),Encoding.UTF8);Close();
  }
  public async Task Capture() {for(int i=0;i<120&&busy;i++)await Task.Delay(100);if(!busy)await RefreshSong();await Task.Delay(1300);UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(root,"preview.png")))enc.Save(f);}
 }
 static class Program {
  [STAThread]static void Main(string[] args){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   if(args.Contains("--verify-live-control")){VerifyLiveControl();return;}
   if(args.Contains("--verify-ui")){var testApp=new Application();var testWindow=new Overlay(true);testWindow.Loaded+=async(s,e)=>await testWindow.VerifyUi();testApp.Run(testWindow);return;}
   if(args.Contains("--probe")){Probe();return;}bool created;using(var mutex=new Mutex(true,"Local\\CloudMusicFloat_2026",out created)){if(!created)return;var app=new Application();var window=new Overlay();if(args.Contains("--capture"))window.Loaded+=async(s,e)=>await window.Capture();app.Run(window);}
  }
  static void Probe(){var root=AppDomain.CurrentDomain.BaseDirectory;var results=new Dictionary<string,object>();
   try{string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);var paths=new List<string>{Path.Combine(local,"NetEase","CloudMusic","Library","webdb.dat")};foreach(var d in Directory.GetDirectories(Path.Combine(local,"Packages"),"1F8B0F94.122165AE053F_*"))paths.Add(Path.Combine(d,"LocalCache","Local","NetEase","CloudMusic","Library","webdb.dat"));
    string file=paths.FirstOrDefault(File.Exists);results["databaseFound"]=file!=null;if(file!=null){var song=Song.Parse(Db.Scalar(file,"SELECT jsonStr FROM historyTracks ORDER BY playtime DESC LIMIT 1"));results["title"]=song.Title;results["artist"]=song.Artist;results["duration"]=song.Duration;using(var r=new PlaybackReader()){double pos=r.Read(song.Duration);results["position"]=Double.IsNaN(pos)?(object)null:pos;results["readerError"]=r.Error;results["pid"]=r.Pid;}}
    results["lrcParserPass"]=Lyrics.Parse("[00:01.50][00:04.00]test\n[00:05.00]next").Count==3;
   }catch(Exception ex){results["error"]=ex.ToString();}File.WriteAllText(Path.Combine(root,"probe-result.json"),Json.Write(results),Encoding.UTF8);
  }
  static void VerifyLiveControl(){var results=new Dictionary<string,object>();bool restore=false;using(var r=new PlaybackReader())try{
   double first=r.Read(86400);Thread.Sleep(1400);double before=r.Read(86400);if(Double.IsNaN(first)||Double.IsNaN(before))throw new Exception("进度不可读，未执行控制测试");bool wasPlaying=before-first>.1;results["wasPlaying"]=wasPlaying;results["before"]=before;
   restore=Overlay.SendPlaybackCommand();results["commandSent"]=restore;if(!restore)throw new Exception("媒体控制消息未发送成功");Thread.Sleep(1800);double after=r.Read(86400);results["after"]=after;results["stateChanged"]=wasPlaying?Math.Abs(after-before)<.5:after-before>.5;
    bool sent=Overlay.SendPlaybackCommand();restore=!sent;Thread.Sleep(1200);double a=r.Read(86400);Thread.Sleep(1400);double b=r.Read(86400);results["restoreA"]=a;results["restoreB"]=b;results["restored"]=sent&&(wasPlaying?b-a>.1:Math.Abs(b-a)<.1);results["passed"]=(bool)results["stateChanged"]&&(bool)results["restored"];
  }catch(Exception e){results["error"]=e.Message;results["passed"]=false;}finally{if(restore)Overlay.SendPlaybackCommand();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"control-result.json"),Json.Write(results),Encoding.UTF8);}}
 }
}
