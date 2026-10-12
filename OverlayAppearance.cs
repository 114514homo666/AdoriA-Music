using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace CloudMusicFloat {
 public partial class Overlay {
  Border backgroundLayer;
  readonly List<AppearanceSlider> scaleSliders=new List<AppearanceSlider>(),opacitySliders=new List<AppearanceSlider>();
  DispatcherTimer saveTimer;int glassRevision;
  static double FiniteClamp(double value,double minimum,double maximum,double fallback){return Double.IsNaN(value)||Double.IsInfinity(value)?fallback:Math.Max(minimum,Math.Min(maximum,value));}
  bool IsInteractive(DependencyObject node){while(node!=null){if(node is ButtonBase||node is Slider||node is Thumb||node is MenuItem||node is ContextMenu)return true;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}return false;}
  void AddAppearanceSliders(ItemCollection items){
   items.Add(new Separator());
   var size=new AppearanceSlider("窗口缩放",70,180,settings.Scale*100,"70%","180%",v=>{SetScale(v/100,true);ScheduleSave();});scaleSliders.Add(size);items.Add(new MenuItem{Header=size,StaysOpenOnClick=true,Focusable=false});
   var opacity=new AppearanceSlider("背景不透明度",0,100,settings.BackgroundOpacity*100,"0% 全透明","100%",v=>SetOpacity(v/100));opacitySliders.Add(opacity);items.Add(new MenuItem{Header=opacity,StaysOpenOnClick=true,Focusable=false});
   var reset=new MenuItem{Header="恢复大小与背景"};reset.Click+=(s,e)=>{SetScale(1,true);SetOpacity(.35);ScheduleSave();};items.Add(reset);
  }
  void SyncAppearanceSliders(){foreach(var value in scaleSliders)value.Sync(settings.Scale*100);foreach(var value in opacitySliders)value.Sync(settings.BackgroundOpacity*100);}
  void SetScale(double value,bool animated){
   double maximum=1.8;if(!isVerification){var area=SystemParameters.WorkArea;maximum=Math.Max(.7,Math.Min(maximum,Math.Min(area.Width/440,area.Height/(settings.Compact?214:356))));}
   settings.Scale=FiniteClamp(value,.7,maximum,1);double width=440*settings.Scale,height=(settings.Compact?214:356)*settings.Scale;
   Motion.To(this,Window.WidthProperty,width,animated?180:0);Motion.To(this,Window.HeightProperty,height,animated?180:0);SyncAppearanceSliders();
   if(!isVerification){if(animated)Later(210,()=>{ClampToWorkArea();ApplyGlassAndTint(false);});else ClampToWorkArea();}
  }
  void ClampToWorkArea(){if(isVerification)return;var area=SystemParameters.WorkArea;Left=FiniteClamp(Left,area.Left,Math.Max(area.Left,area.Right-Width),area.Left);Top=FiniteClamp(Top,area.Top,Math.Max(area.Top,area.Bottom-Height),area.Top);}
  void ScheduleSave(){if(isVerification||SilentRun)return;if(saveTimer==null){saveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};}saveTimer.Stop();saveTimer.Start();}
  void ApplyGlassAndTint(bool animated){
   int revision=++glassRevision;var palette=Palette.Get(settings.Theme);bool wanted=settings.Glass&&settings.BackgroundOpacity>0;
   glass.Apply(this,wanted,palette.Dark);var top=palette.Top;var bottom=palette.Bottom;
   // The background layer alone controls alpha: 0 is clear and 1 is solid.
   // The blur has no opaque system tint; the foreground is outside this layer.
   top.A=bottom.A=255;
   Palette.To(surfaceTop,top,animated);Palette.To(surfaceBottom,bottom,animated);
   if(animated&&settings.BackgroundOpacity==0){Later(120,()=>{if(revision==glassRevision&&settings.BackgroundOpacity==0)glass.Apply(this,false,palette.Dark);});}
  }
  void ApplyTextContrast(Palette palette){foreach(var text in new[]{title,artist,compactLyric,current,translation,previous,next,time,status,brand,sourceLabel})if(text!=null)text.Effect=new DropShadowEffect{Color=palette.Dark?Colors.Black:Colors.White,BlurRadius=3,ShadowDepth=0,Opacity=.7};}
  void AddResizeGrip(Grid layers){
   var grip=new Thumb{Width=22,Height=22,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,4,4),Cursor=Cursors.SizeNWSE,ToolTip="拖动缩放 · Ctrl+滚轮也可调整"};
   grip.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Path Data='M8,17 L17,8 M13,17 L17,13' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.3' Opacity='.65'/></Grid></ControlTemplate>");grip.Foreground=muted;System.Windows.Automation.AutomationProperties.SetName(grip,"拖动缩放悬浮窗");
   double startX=0,startWidth=0;grip.DragStarted+=(s,e)=>{startX=Mouse.GetPosition(this).X;startWidth=Width;};grip.DragDelta+=(s,e)=>SetScale((startWidth+Mouse.GetPosition(this).X-startX)/440,false);grip.DragCompleted+=(s,e)=>{ApplyGlassAndTint(false);Save();};layers.Children.Add(grip);
  }
  async Task VerifyAppearance(Dictionary<string,object> checks,string dir){
   settings.Compact=false;ApplyCompact(false);SetScale(1,false);await Task.Delay(250);
   foreach(double scale in new[]{.7,1.25,1.8}){SetScale(scale,true);await SettleUi(()=>Math.Abs(Width-440*scale)<.1&&Math.Abs(Height-356*scale)<.1);UpdateLayout();var first=artist.TranslatePoint(new Point(0,0),this);var second=title.TranslatePoint(new Point(0,0),this);double artworkWidth=artwork.TranslatePoint(new Point(72,0),this).X-artwork.TranslatePoint(new Point(0,0),this).X;checks["scale"+Math.Round(scale*100)]=Math.Abs(Width-440*scale)<.1&&Math.Abs(Height-356*scale)<.1&&first.Y<second.Y&&Math.Abs(artworkWidth-72*scale)<.2;Snapshot(Path.Combine(dir,"scale-"+Math.Round(scale*100)+".png"));}
   SetScale(.7,true);await Task.Delay(30);SetScale(1.4,true);await Task.Delay(30);SetScale(1.1,true);await SettleUi(()=>Math.Abs(Width-484)<.1);checks["rapidScaleKeepsLatest"]=Math.Abs(Width-484)<.1&&Math.Abs(Height-391.6)<.1;
   settings.Compact=true;ApplyCompact(true);await SettleUi(()=>Math.Abs(Height-235.4)<.1);checks["compactUsesCurrentScale"]=Math.Abs(Height-235.4)<.1&&compactLyricSlot.Visibility==Visibility.Visible;settings.Compact=false;ApplyCompact(true);SetScale(1.3,true);await SettleUi(()=>Math.Abs(Height-462.8)<.1&&Math.Abs(card.Height-356)<.1);checks["scaleDuringModeChange"]=Math.Abs(Width-572)<.1&&Math.Abs(Height-462.8)<.1&&Math.Abs(card.Height-356)<.1;
   SetScale(1,false);SetScale(Double.NaN,false);checks["invalidScaleIsSafe"]=settings.Scale==1;SetScale(9,false);checks["scaleMaximum"]=settings.Scale==1.8;SetScale(-1,false);checks["scaleMinimum"]=settings.Scale==.7;SetScale(1,false);
   Motion.Text(current,"透明背景，清晰歌词");await Task.Delay(350);double textOpacity=current.Opacity,artistOpacity=artist.Opacity;Color textColor=((SolidColorBrush)current.Foreground).Color;
   foreach(double opacity in new[]{0,.5,1}){SetOpacity(opacity);await SettleUi(()=>Math.Abs(backgroundLayer.Opacity-opacity)<.001);checks["backgroundOpacity"+Math.Round(opacity*100)]=Math.Abs(backgroundLayer.Opacity-opacity)<.001&&card.Opacity==1&&Opacity==1&&current.Opacity==textOpacity&&artist.Opacity==artistOpacity&&((SolidColorBrush)current.Foreground).Color==textColor;Snapshot(Path.Combine(dir,"opacity-"+Math.Round(opacity*100)+".png"));}
   SetOpacity(0);await Task.Delay(140);checks["zeroBackgroundDisablesBlur"]=!glass.Active&&backgroundLayer.Opacity==0&&current.Opacity==1;SetOpacity(.5);await Task.Delay(140);checks["glassBackgroundIndependentlyTransparent"]=Math.Abs(backgroundLayer.Opacity-.5)<.001&&current.Opacity==1&&AllowsTransparency;
   SetOpacity(.2);SetOpacity(.8);SetOpacity(.35);await SettleUi(()=>Math.Abs(backgroundLayer.Opacity-.35)<.001);checks["rapidOpacityKeepsLatest"]=Math.Abs(backgroundLayer.Opacity-.35)<.001&&current.Opacity==1;
   checks["sliderRangesAndValues"]=scaleSliders.All(s=>s.Slider.Minimum==70&&s.Slider.Maximum==180&&s.ValueLabel.Text=="100%")&&opacitySliders.All(s=>s.Slider.Minimum==0&&s.Slider.Maximum==100&&s.ValueLabel.Text=="35%");
   scaleSliders[0].Slider.Value=123;opacitySliders[0].Slider.Value=42;await SettleUi(()=>Math.Abs(Width-541.2)<.1&&Math.Abs(backgroundLayer.Opacity-.42)<.001);checks["slidersChangeActualWindow"]=Math.Abs(settings.Scale-1.23)<.001&&Math.Abs(settings.BackgroundOpacity-.42)<.001&&scaleSliders.All(s=>s.ValueLabel.Text=="123%")&&opacitySliders.All(s=>s.ValueLabel.Text=="42%");
   var restored=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>(Json.Write(settings));checks["scaleAndOpacityPersist"]=Math.Abs(restored.Scale-1.23)<.001&&Math.Abs(restored.BackgroundOpacity-.42)<.001;
   var old=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>("{\"Opacity\":0.65}");checks["oldSettingsDefaultScale"]=old.Scale==1&&old.Opacity==.65&&old.BackgroundOpacity==.35;
   SetScale(1,false);SetOpacity(.35);await Task.Delay(200);
  }
 }
}
