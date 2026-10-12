using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Data;

namespace CloudMusicFloat {
 public class Palette {
  public string Key,Name;public bool Dark;public Color Top,Bottom,Text,Muted,Accent,Border,Track,Artwork;
  static Color C(byte r,byte g,byte b){return Color.FromRgb(r,g,b);}
  public static readonly Palette[] All={
   new Palette{Key="White",Name="白色",Dark=false,Top=C(250,252,255),Bottom=C(231,238,247),Text=C(24,34,52),Muted=C(75,90,111),Accent=C(35,91,184),Border=C(175,192,214),Track=C(190,204,223),Artwork=C(212,224,239)},
   new Palette{Key="Blue",Name="蓝色",Dark=true,Top=C(29,63,111),Bottom=C(16,35,70),Text=C(246,249,255),Muted=C(190,207,232),Accent=C(145,211,255),Border=C(113,167,226),Track=C(62,96,143),Artwork=C(43,77,124)},
   new Palette{Key="Gray",Name="灰色",Dark=true,Top=C(69,75,85),Bottom=C(39,44,53),Text=C(248,250,253),Muted=C(200,208,219),Accent=C(181,210,247),Border=C(145,157,176),Track=C(102,113,132),Artwork=C(84,95,112)},
   new Palette{Key="Black",Name="黑色",Dark=true,Top=C(26,29,37),Bottom=C(12,15,22),Text=C(244,246,255),Muted=C(169,180,201),Accent=C(151,184,255),Border=C(96,116,156),Track=C(47,56,78),Artwork=C(38,48,65)}
  };
  public static Palette Get(string key){foreach(var palette in All)if(palette.Key==key)return palette;return All[3];}
  public static void To(SolidColorBrush brush,Color color,bool animated){Animate(brush,SolidColorBrush.ColorProperty,color,animated);}
  public static void To(GradientStop stop,Color color,bool animated){Animate(stop,GradientStop.ColorProperty,color,animated);}
  static void Animate(System.Windows.Media.Animation.Animatable target,DependencyProperty property,Color color,bool animated){
   if(!animated){target.BeginAnimation(property,null);target.SetValue(property,color);return;}
   Color from=(Color)target.GetValue(property);target.SetValue(property,color);target.BeginAnimation(property,new ColorAnimation(from,color,TimeSpan.FromMilliseconds(280)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
  }
 }
 // Clear blur on this application's own layered HWND. DWM Desktop Acrylic's
 // built-in opaque tint cannot be faded independently of the foreground.
 // Accent policy is a compatibility API: failure leaves ordinary transparency.
 public class GlassEffect {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct VersionInfo{public int Size,Major,Minor,Build,Platform;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string ServicePack;}
  [StructLayout(LayoutKind.Sequential)]struct AccentPolicy{public int State,Flags,Color,Animation;}
  [StructLayout(LayoutKind.Sequential)]struct CompositionData{public int Attribute;public IntPtr Data;public int Size;}
  [DllImport("ntdll.dll",CharSet=CharSet.Unicode)]static extern int RtlGetVersion(ref VersionInfo version);
  [DllImport("user32.dll")]static extern int SetWindowCompositionAttribute(IntPtr window,ref CompositionData data);
  [DllImport("user32.dll")]static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
  [DllImport("gdi32.dll")]static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
  [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
  public static readonly bool Supported=IsSupported();
  public bool Active{get;private set;}public int LastResult{get;private set;}
  static bool IsSupported(){try{var info=new VersionInfo{Size=Marshal.SizeOf(typeof(VersionInfo))};return RtlGetVersion(ref info)==0&&info.Major>=10&&info.Build>=17763;}catch{return false;}}
  public bool Apply(Window window,bool enabled,bool dark){
   Active=false;if(!Supported)return false;IntPtr handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return false;
   IntPtr memory=IntPtr.Zero;try{var policy=new AccentPolicy{State=enabled?3:0};memory=Marshal.AllocHGlobal(Marshal.SizeOf(policy));Marshal.StructureToPtr(policy,memory,false);var data=new CompositionData{Attribute=19,Data=memory,Size=Marshal.SizeOf(policy)};LastResult=SetWindowCompositionAttribute(handle,ref data);Active=enabled&&LastResult!=0;}
   catch{LastResult=0;}finally{if(memory!=IntPtr.Zero)Marshal.FreeHGlobal(memory);}return Active;
  }
  public void Resize(Window window,double radius){IntPtr handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;try{var source=PresentationSource.FromVisual(window);if(source==null||source.CompositionTarget==null)return;var dpi=source.CompositionTarget.TransformToDevice;int width=(int)Math.Ceiling(window.ActualWidth*dpi.M11),height=(int)Math.Ceiling(window.ActualHeight*dpi.M22),round=(int)Math.Round(radius*2*dpi.M11);IntPtr region=CreateRoundRectRgn(0,0,width+1,height+1,round,round);if(region!=IntPtr.Zero&&SetWindowRgn(handle,region,true)==0)DeleteObject(region);}catch{}}
 }
 public static class MenuAppearance {
  static Brush BoundBrush(Brush source){var mutable=source as SolidColorBrush;if(mutable==null)return source;var proxy=new SolidColorBrush();BindingOperations.SetBinding(proxy,SolidColorBrush.ColorProperty,new Binding("Color"){Source=mutable,Mode=BindingMode.OneWay});return proxy;}
  public static void ConfigureTooltips(FrameworkElement target,SolidColorBrush surface,Brush text,Brush border){target.Resources["TooltipSurface"]=BoundBrush(surface);target.Resources["TooltipText"]=BoundBrush(text);target.Resources["TooltipEdge"]=BoundBrush(border);target.Resources[typeof(ToolTip)]=(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'><Setter Property='Background' Value='{DynamicResource TooltipSurface}'/><Setter Property='Foreground' Value='{DynamicResource TooltipText}'/><Setter Property='BorderBrush' Value='{DynamicResource TooltipEdge}'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='11'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'><Border CornerRadius='8' Padding='10,6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>");}
  public static void Configure(ContextMenu menu,SolidColorBrush surface,SolidColorBrush hover,Brush text,Brush border){
   ConfigureTooltips(menu,surface,text,border);
   // Resource lookup may freeze brushes. Bound proxies keep the animated palette mutable.
   menu.Resources["MenuSurface"]=BoundBrush(surface);menu.Resources["MenuHover"]=BoundBrush(hover);menu.Resources["MenuText"]=BoundBrush(text);menu.Resources["MenuEdge"]=BoundBrush(border);
   menu.Background=surface;menu.Foreground=text;menu.BorderBrush=border;menu.FontFamily=new FontFamily("Microsoft YaHei UI");menu.FontSize=12;menu.BorderThickness=new Thickness(1);menu.Padding=new Thickness(6);
   menu.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='12' Padding='6' Margin='8'><Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='3' Opacity='.24'/></Border.Effect><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>");
   var itemStyle=(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>
    <Setter Property='Foreground' Value='{DynamicResource MenuText}'/><Setter Property='FontSize' Value='12'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'><Grid>
     <Border x:Name='Root' CornerRadius='7' Background='Transparent' Margin='1'><Grid>
      <Border x:Name='Hover' CornerRadius='7' Background='{DynamicResource MenuHover}' Opacity='0'/>
      <Grid Margin='10,7'><Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition Width='*'/><ColumnDefinition Width='16'/></Grid.ColumnDefinitions>
       <TextBlock x:Name='Check' Text='✓' Foreground='{TemplateBinding Foreground}' VerticalAlignment='Center' Visibility='Hidden' FontSize='12'/>
       <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center' Margin='0,0,10,0'/>
       <TextBlock x:Name='Arrow' Grid.Column='2' Text='›' Foreground='{TemplateBinding Foreground}' VerticalAlignment='Center' HorizontalAlignment='Right' Visibility='Hidden' FontSize='17'/>
      </Grid>
     </Grid></Border>
     <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' VerticalOffset='-8' PlacementTarget='{Binding ElementName=Root}' IsOpen='{Binding IsSubmenuOpen,RelativeSource={RelativeSource TemplatedParent}}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
      <Border Background='{DynamicResource MenuSurface}' BorderBrush='{DynamicResource MenuEdge}' BorderThickness='1' CornerRadius='12' Padding='6' Margin='8'><Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='3' Opacity='.24'/></Border.Effect><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/></ScrollViewer></Border>
     </Popup>
    </Grid><ControlTemplate.Triggers>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger>
     <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Root' Property='Opacity' Value='.4'/></Trigger>
     <Trigger Property='IsHighlighted' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Hover' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.12'/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Hover' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.16'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
   menu.Resources[typeof(MenuItem)]=itemStyle;
   menu.Resources[typeof(Separator)]=(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Separator'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'><Border Height='1' Margin='10,5' Background='{DynamicResource MenuEdge}' Opacity='.4'/></ControlTemplate></Setter.Value></Setter></Style>");
  }
 }
}
