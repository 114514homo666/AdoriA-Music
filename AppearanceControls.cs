using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace CloudMusicFloat
{
    public class AppearanceSlider : StackPanel
    {
        private const string TemplateXaml =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>" +
            "<Grid Background='Transparent' Height='26'>" +
            "<Border Height='4' CornerRadius='2' Margin='7,0' VerticalAlignment='Center' Background='{DynamicResource MenuEdge}' Opacity='.32'/>" +
            "<Track x:Name='PART_Track' VerticalAlignment='Center' IsDirectionReversed='False' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' Background='{DynamicResource MenuText}' Opacity='.8'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton><Track.Thumb>" +
            "<Thumb Width='14' Height='20' Cursor='Hand'><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
            "<Grid Background='Transparent'><Ellipse x:Name='Dot' Width='13' Height='13' Fill='{DynamicResource MenuText}' Stroke='{DynamicResource MenuEdge}' StrokeThickness='1'/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Dot' Property='Width' Value='15'/><Setter TargetName='Dot' Property='Height' Value='15'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='Dot' Property='Opacity' Value='.7'/></Trigger></ControlTemplate.Triggers>" +
            "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track>" +
            "</Grid></ControlTemplate>";

        private bool _sync;

        public Slider Slider { get; private set; }
        public TextBlock ValueLabel { get; private set; }

        public AppearanceSlider(string label, double minimum, double maximum, double value,
            string leftCaption, string rightCaption, Action<double> changed)
        {
            Width = 210; Margin = new Thickness(0,3,0,5); System.Windows.Documents.TextElement.SetFontFamily(this,new System.Windows.Media.FontFamily("Microsoft YaHei UI"));

            var title = new TextBlock { Text = label, FontSize=12 };
            title.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
            ValueLabel = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
            ValueLabel.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
            var header = new DockPanel();
            DockPanel.SetDock(ValueLabel, Dock.Right);
            header.Children.Add(ValueLabel);
            header.Children.Add(title);

            Slider = new Slider
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = value,
                TickFrequency = 1,
                SmallChange = 1,
                LargeChange = 10,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                Height=28, Margin=new Thickness(0,4,0,0),
                Template = (ControlTemplate)XamlReader.Parse(TemplateXaml)
            };
            System.Windows.Automation.AutomationProperties.SetName(Slider,label);
            Slider.ValueChanged += (s, e) =>
            {
                UpdateLabel(e.NewValue);
                if (!_sync && changed != null) changed(e.NewValue);
            };

            var caps = new Grid { Margin=new Thickness(0,2,0,0), Opacity=.68 };
            caps.ColumnDefinitions.Add(new ColumnDefinition());
            caps.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new TextBlock { Text = leftCaption, FontSize=10, HorizontalAlignment = HorizontalAlignment.Left };
            var right = new TextBlock { Text = rightCaption, FontSize=10, HorizontalAlignment = HorizontalAlignment.Right };
            left.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
            right.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
            Grid.SetColumn(right, 1);
            caps.Children.Add(left);
            caps.Children.Add(right);

            Children.Add(header);
            Children.Add(Slider);
            Children.Add(caps);
            Sync(value);
        }

        public void Sync(double value)
        {
            _sync = true;
            try { Slider.Value = Math.Max(Slider.Minimum,Math.Min(Slider.Maximum,value)); UpdateLabel(Slider.Value); }
            finally { _sync = false; }
        }

        private void UpdateLabel(double value)
        {
            ValueLabel.Text = ((int)Math.Round(value)).ToString() + "%";
        }
    }
}
