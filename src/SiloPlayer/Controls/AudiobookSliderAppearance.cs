using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace SiloPlayer.Controls;

internal static class AudiobookSliderAppearance
{
    // Keep WinUI's native range/drag/keyboard/UIA implementation while giving
    // player rails their white, thin surface styling instead of settings styling.
    internal static void Apply(Slider slider, bool volume = false)
    {
        var trackHeight = volume ? 2d : 3d;
        var white = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        var track = new SolidColorBrush(Windows.UI.Color.FromArgb(volume ? (byte)51 : (byte)38, 255, 255, 255));
        var transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        slider.Background = track; slider.Foreground = white;
        slider.Resources["SliderTrackThemeHeight"] = trackHeight;
        slider.Resources["SliderHorizontalThumbWidth"] = volume ? 12d : 14d;
        slider.Resources["SliderHorizontalThumbHeight"] = volume ? 12d : 14d;
        slider.Resources["SliderInnerThumbWidth"] = volume ? 10d : 12d;
        slider.Resources["SliderInnerThumbHeight"] = volume ? 10d : 12d;
        slider.Resources["SliderOuterThumbBackground"] = white;
        slider.Resources["SliderThumbBorderBrush"] = transparent;
        slider.Resources["SliderTrackFill"] = track;
        foreach (var key in new[] { "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed", "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed" }) slider.Resources[key] = white;
        var hovering = false;
        void Paint()
        {
            var visible = hovering || slider.FocusState != FocusState.Unfocused;
            foreach (var thumb in Descendants(slider).OfType<Thumb>()) thumb.Opacity = visible ? 1 : 0;
            foreach (var rect in Descendants(slider).OfType<Rectangle>().Where(rect => rect.Name is "HorizontalTrackRect" or "HorizontalDecreaseRect")) rect.Height = !volume && hovering ? 5 : trackHeight;
        }
        slider.Loaded += (_, _) => Paint();
        slider.PointerEntered += (_, _) => { hovering = true; Paint(); };
        slider.PointerExited += (_, _) => { hovering = false; Paint(); };
        slider.GotFocus += (_, _) => Paint(); slider.LostFocus += (_, _) => Paint();
        if (volume)
        {
            var hosted = false;
            slider.Loaded += (_, _) =>
            {
                // WinUI reserves half a thumb at each track endpoint, even
                // when the thumb is hidden. Paint the full-width WebUI rail
                // from the actual native value while retaining its input/UIA.
                if (hosted || VisualTreeHelper.GetParent(slider) is not Panel parent) return;
                var index = parent.Children.IndexOf(slider);
                if (index < 0) return;
                hosted = true;
                parent.Children.RemoveAt(index);
                var host = new Grid { Width = slider.Width, Height = slider.Height, VerticalAlignment = slider.VerticalAlignment, Visibility = slider.Visibility };
                slider.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => host.Visibility = slider.Visibility);
                var rail = new ProgressBar { Minimum = slider.Minimum, Maximum = slider.Maximum, Value = slider.Value,
                    Height = 2, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center, Foreground = white, Background = track, IsHitTestVisible = false, Tag = "native-volume-paint" };
                // The platform ProgressBar template has zero-height internal
                // parts at two pixels. Bound ordinary XAML paint to the real
                // slider range instead of relying on its minimum-size template.
                rail.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
                    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ProgressBar">
                        <Grid Height="2">
                            <Border Background="{TemplateBinding Background}" CornerRadius="1"/>
                            <Border x:Name="NativeVolumeIndicator" Background="{TemplateBinding Foreground}" CornerRadius="1" HorizontalAlignment="Left" Width="0"/>
                        </Grid>
                    </ControlTemplate>
                    """);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(rail, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                slider.Opacity = 0;
                host.Children.Add(rail); host.Children.Add(slider); parent.Children.Insert(index, host);
                void PaintVolume()
                {
                    rail.Value = slider.Value;
                    var fill = Descendants(rail).OfType<Border>().FirstOrDefault(part => part.Name == "NativeVolumeIndicator");
                    if (fill != null) fill.Width = rail.ActualWidth * Math.Clamp((slider.Value - slider.Minimum) / Math.Max(1, slider.Maximum - slider.Minimum), 0, 1);
                }
                rail.Loaded += (_, _) => PaintVolume();
                rail.SizeChanged += (_, _) => PaintVolume();
                slider.ValueChanged += (_, _) => PaintVolume();
                slider.GotFocus += (_, _) => { host.BorderBrush = white; host.BorderThickness = new Thickness(1); };
                slider.LostFocus += (_, _) => host.BorderThickness = new Thickness(0);
            };
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }
}
