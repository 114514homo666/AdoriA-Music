using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.IO;

namespace CloudMusicFloat {
 public partial class Overlay {
  Grid layout,songInfo;DockPanel topBar,playbackRow;StackPanel controlStrip,metadataPanel,footer;
  Button settingsButton;ContextMenu settingsMenu;MenuItem pinSetting,compactSetting;
  readonly List<Thumb> resizeGrips=new List<Thumb>();
  bool applyingLayout,shapePending;double progressFraction;RectangleGeometry roundedClip;
  void RestoreDimensions(){double legacy=FiniteClamp(settings.Scale,.7,1.8,1);SetDimensions(isVerification?440:settings.WindowWidth>0?settings.WindowWidth:440*legacy,isVerification?356:settings.WindowHeight>0?settings.WindowHeight:(settings.Compact?214:356)*legacy,false);}
  void SetDimensions(double width,double height,bool animated){var area=SystemParameters.WorkArea;double maxW=isVerification?2400:Math.Max(240,area.Width),maxH=isVerification?1600:Math.Max(64,area.Height);Motion.To(this,Window.WidthProperty,FiniteClamp(width,240,maxW,440),animated?180:0);Motion.To(this,Window.HeightProperty,FiniteClamp(height,64,maxH,356),animated?180:0);if(!isVerification){if(animated)Later(200,()=>{ClampToWorkArea();Save();});else ClampToWorkArea();}ApplyResponsiveLayout();}
  void OpenSettings(){settingsMenu.PlacementTarget=settingsButton;settingsMenu.IsOpen=true;}
  static void MoveTo(UIElement element,Panel target){var parent=VisualTreeHelper.GetParent(element) as Panel;if(parent==target)return;if(parent!=null)parent.Children.Remove(element);target.Children.Add(element);}
  static void Show(UIElement element,bool visible){element.Visibility=visible?Visibility.Visible:Visibility.Collapsed;}
  void ApplyResponsiveLayout(){
   if(layout==null||applyingLayout)return;applyingLayout=true;
   try{
    double w=Width,h=Height;bool strip=h<110,portrait=!settings.Compact&&h>=400&&h>=w*1.25&&w<380,details=!settings.Compact&&h>=170,coverVisible=details&&(portrait||w>=340&&h>=230),artistVisible=details&&(portrait||w>=300&&h>=250),context=!settings.Compact&&h>=300;
    layout.Margin=strip?new Thickness(12,8,12,8):new Thickness(w<340?16:24,h<180?12:20,w<340?16:24,h<180?12:20);
    Show(topBar,!strip);Show(songInfo,details);Show(artwork,coverVisible);Show(artist,artistVisible);Show(previous,context);Show(next,context);Show(translation,!settings.Compact&&h>=260);Show(footer,!strip);Show(progressTrack,!strip&&h>=180&&(portrait||w>=280));Show(time,!strip&&h>=150);Show(status,!strip&&h>=180&&w>=340);
    Show(sourceButton,!strip&&w>=430);Show(pinButton,!strip&&w>=360);Show(compactButton,!strip&&w>=400);Show(brand,!strip&&w>=320);
    compactLyricSlot.Visibility=Visibility.Collapsed;lyricsViewport.Visibility=Visibility.Visible;
    if(portrait){if(songInfo.RowDefinitions.Count!=2){songInfo.RowDefinitions.Clear();songInfo.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});songInfo.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});}Grid.SetRow(artwork,0);Grid.SetColumn(artwork,0);Grid.SetColumnSpan(artwork,2);Grid.SetRow(metadataPanel,1);Grid.SetColumn(metadataPanel,0);Grid.SetColumnSpan(metadataPanel,2);artwork.HorizontalAlignment=HorizontalAlignment.Center;metadataPanel.Margin=new Thickness(0,12,0,0);}
    else{songInfo.RowDefinitions.Clear();Grid.SetRow(artwork,0);Grid.SetColumn(artwork,0);Grid.SetColumnSpan(artwork,1);Grid.SetRow(metadataPanel,0);Grid.SetColumn(metadataPanel,1);Grid.SetColumnSpan(metadataPanel,1);artwork.HorizontalAlignment=HorizontalAlignment.Left;metadataPanel.Margin=new Thickness(coverVisible?12:0,0,0,0);}
    double coverSize=portrait?Math.Max(88,Math.Min(Math.Min(200,w-48),(h-360)*.55)):72;artwork.Width=artwork.Height=coverSize;var coverClip=cover.Clip as RectangleGeometry;if(coverClip!=null)coverClip.Rect=new Rect(0,0,coverSize,coverSize);songInfo.ColumnDefinitions[0].Width=portrait?new GridLength(1,GridUnitType.Star):new GridLength(coverVisible?80:0);songInfo.ColumnDefinitions[1].Width=portrait?new GridLength(0):new GridLength(1,GridUnitType.Star);songInfo.Margin=new Thickness(0,0,0,details?8:0);title.Margin=new Thickness(0,artistVisible?4:0,0,0);topBar.Margin=new Thickness(0,0,0,h>=210?14:4);
    foreach(var text in new[]{artist,title,previous,current,next,translation})text.TextAlignment=portrait?TextAlignment.Center:TextAlignment.Left;artist.TextWrapping=title.TextWrapping=previous.TextWrapping=next.TextWrapping=portrait?TextWrapping.Wrap:TextWrapping.NoWrap;artist.MaxHeight=portrait?34:Double.PositiveInfinity;title.MaxHeight=portrait?52:Double.PositiveInfinity;previous.MaxHeight=next.MaxHeight=portrait?38:Double.PositiveInfinity;
    if(strip){MoveTo(playButton,layout);MoveTo(controlStrip,layout);Grid.SetRow(playButton,2);Grid.SetColumn(playButton,0);Grid.SetColumnSpan(playButton,1);Grid.SetRow(controlStrip,2);Grid.SetColumn(controlStrip,2);Grid.SetColumnSpan(controlStrip,1);Grid.SetColumn(lyricsViewport,1);Grid.SetColumnSpan(lyricsViewport,1);playButton.Margin=new Thickness(0,0,6,0);controlStrip.Margin=new Thickness(6,0,0,0);controlStrip.VerticalAlignment=VerticalAlignment.Center;}
    else{MoveTo(playButton,playbackRow);DockPanel.SetDock(playButton,Dock.Left);playbackRow.Children.Remove(playButton);playbackRow.Children.Insert(1,playButton);MoveTo(controlStrip,topBar);DockPanel.SetDock(controlStrip,Dock.Right);topBar.Children.Remove(controlStrip);topBar.Children.Insert(0,controlStrip);Grid.SetColumn(lyricsViewport,0);Grid.SetColumnSpan(lyricsViewport,3);playButton.Margin=new Thickness(-5,0,5,0);controlStrip.Margin=new Thickness(0);}
    foreach(var child in controlStrip.Children){var button=child as Button;if(button==null)continue;button.Width=button==sourceButton?76:strip?22:25;button.Height=strip?24:25;button.Margin=new Thickness(strip?1:3,0,0,0);}
    lyricsPanel.Margin=new Thickness(0);current.Margin=new Thickness(0,context?8:0,0,0);current.FontSize=strip?(h<82?15:17):w>=600?22:w<300?17:19;current.TextWrapping=h<82?TextWrapping.NoWrap:TextWrapping.Wrap;current.MaxHeight=strip?Math.Max(24,h-16):portrait?Math.Max(34,h-coverSize-260):Math.Max(36,h-(details?125:75));title.FontSize=w<300?15:17;lyricsPanel.VerticalAlignment=VerticalAlignment.Center;
    double radius=8;card.CornerRadius=backgroundLayer.CornerRadius=new CornerRadius(radius);UpdateRoundedClip();
   }finally{applyingLayout=false;}
  }
  void UpdateRoundedClip(){if(card==null)return;double radius=card.CornerRadius.TopLeft;double w=Math.Max(0,Width),h=Math.Max(0,Height);if(roundedClip==null){roundedClip=new RectangleGeometry();card.Clip=roundedClip;}roundedClip.Rect=new Rect(0,0,w,h);roundedClip.RadiusX=roundedClip.RadiusY=radius;glass.Resize(this,radius);}
  void QueueRoundedClip(){if(shapePending||Dispatcher.HasShutdownStarted)return;shapePending=true;Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{shapePending=false;UpdateRoundedClip();}));}
  void AddResizeGrips(Grid layers){
   AddGrip(layers,"左上角",HorizontalAlignment.Left,VerticalAlignment.Top,true,true,true,true,Cursors.SizeNWSE,18,18);
   AddGrip(layers,"右上角",HorizontalAlignment.Right,VerticalAlignment.Top,false,true,true,true,Cursors.SizeNESW,18,18);
   AddGrip(layers,"左下角",HorizontalAlignment.Left,VerticalAlignment.Bottom,true,false,true,true,Cursors.SizeNESW,18,18);
   AddGrip(layers,"右下角",HorizontalAlignment.Right,VerticalAlignment.Bottom,false,false,true,true,Cursors.SizeNWSE,18,18);
   AddGrip(layers,"左边",HorizontalAlignment.Left,VerticalAlignment.Stretch,true,false,true,false,Cursors.SizeWE,6,Double.NaN);
   AddGrip(layers,"右边",HorizontalAlignment.Right,VerticalAlignment.Stretch,false,false,true,false,Cursors.SizeWE,6,Double.NaN);
   AddGrip(layers,"上边",HorizontalAlignment.Stretch,VerticalAlignment.Top,false,true,false,true,Cursors.SizeNS,Double.NaN,6);
   AddGrip(layers,"下边",HorizontalAlignment.Stretch,VerticalAlignment.Bottom,false,false,false,true,Cursors.SizeNS,Double.NaN,6);
  }
  void AddGrip(Grid layers,string name,HorizontalAlignment horizontal,VerticalAlignment vertical,bool west,bool north,bool changeWidth,bool changeHeight,Cursor cursor,double width,double height){
   var grip=new Thumb{HorizontalAlignment=horizontal,VerticalAlignment=vertical,Width=width,Height=height,Cursor=cursor,Focusable=false};
   // Barely visible alpha keeps the edge targetable even at 0% background; the
   // rounded root and HWND both clip it so no rectangular corner pixels escape.
   grip.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='#01000000'/></ControlTemplate>");
   if(Double.IsNaN(height))grip.Margin=new Thickness(0,18,0,18);if(Double.IsNaN(width))grip.Margin=new Thickness(18,0,18,0);
   System.Windows.Automation.AutomationProperties.SetName(grip,name+"拉伸");double startX=0,startY=0,startLeft=0,startTop=0,startWidth=0,startHeight=0;
   grip.DragStarted+=(s,e)=>{var p=Mouse.GetPosition(this);startX=Left+p.X;startY=Top+p.Y;startLeft=Left;startTop=Top;startWidth=Width;startHeight=Height;};
   grip.DragDelta+=(s,e)=>{var p=Mouse.GetPosition(this);var area=SystemParameters.WorkArea;var b=ResizeGeometry.FromCorner(startLeft,startTop,startWidth,startHeight,changeWidth?Left+p.X-startX:0,changeHeight?Top+p.Y-startY:0,west,north,240,64,Math.Max(240,area.Width),Math.Max(64,area.Height));Motion.To(this,Window.WidthProperty,b.Width,0);Motion.To(this,Window.HeightProperty,b.Height,0);Left=b.Left;Top=b.Top;};
   grip.DragCompleted+=(s,e)=>{ClampToWorkArea();ApplyResponsiveLayout();UpdateRoundedClip();Save();};resizeGrips.Add(grip);layers.Children.Add(grip);
  }
  public async Task MeasureAnimationFrames(){
   var samples=new List<double>();var updates=new List<double>();double lastValue=Double.NaN;TimeSpan previousTime=TimeSpan.MinValue,lastUpdate=TimeSpan.MinValue;EventHandler measure=(s,e)=>{var frame=e as RenderingEventArgs;if(frame==null||frame.RenderingTime==previousTime)return;if(previousTime!=TimeSpan.MinValue)samples.Add((frame.RenderingTime-previousTime).TotalMilliseconds);previousTime=frame.RenderingTime;double value=lyricShift.Y;if(Double.IsNaN(lastValue)||Math.Abs(value-lastValue)>.0000001){if(lastUpdate!=TimeSpan.MinValue)updates.Add((frame.RenderingTime-lastUpdate).TotalMilliseconds);lastUpdate=frame.RenderingTime;lastValue=value;}};
   CompositionTarget.Rendering+=measure;Motion.To(lyricShift,TranslateTransform.YProperty,4,2000);await Task.Delay(2100);CompositionTarget.Rendering-=measure;Motion.To(lyricShift,TranslateTransform.YProperty,0,100);samples.Sort();updates.Sort();double median=samples.Count==0?0:samples[samples.Count/2],updateMedian=updates.Count==0?0:updates[updates.Count/2];File.WriteAllText(Path.Combine(root,"animation-sample.json"),Json.Write(new{targetFrameRate=120,renderCallbacks=samples.Count,medianIntervalMs=median,estimatedCallbackFps=median>0?1000/median:0,animationUpdates=updates.Count,medianAnimationIntervalMs=updateMedian,estimatedAnimationUpdatesPerSecond=updateMedian>0?1000/updateMedian:0,renderTier=RenderCapability.Tier>>16,note="Observed WPF callbacks and changing animation values, not a display-present FPS guarantee"}));
  }
 }
}


