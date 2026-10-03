using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using System.Runtime.InteropServices.WindowsRuntime;

internal static class MediaVolumePaintNativeFixture
{
    internal static async Task RunVisibleAsync(StackPanel parent)
    {
        foreach (var mini in new[] { false, true })
        {
            FrameworkElement control = mini ? new MiniPlayerBar { Width = 1280 } : new AudiobookNowListening { Width = 1280, Height = 720 };
            parent.Children.Add(control);
            try
            {
                if (mini)
                {
                    ((FrameworkElement)control.FindName("VideoBar")).Visibility = Visibility.Collapsed;
                    ((FrameworkElement)control.FindName("AudiobookBar")).Visibility = Visibility.Visible;
                }
                control.UpdateLayout(); await Task.Delay(100);
                var slider = (Slider)control.FindName(mini ? "AudiobookVolumeSlider" : "VolumeSlider");
                var host = (Grid)VisualTreeHelper.GetParent(slider);
                var rail = host.Children.OfType<ProgressBar>().Single(bar => (string?)bar.Tag == "native-volume-paint");
                var provider = (Microsoft.UI.Xaml.Automation.Provider.IRangeValueProvider)new Microsoft.UI.Xaml.Automation.Peers.SliderAutomationPeer(slider).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.RangeValue);
                var events = 0;
                slider.ValueChanged += (_, _) => events++;
                foreach (var value in new[] { 0d, 50d, 100d })
                {
                    var before = slider.Value;
                    var beforeEvents = events;
                    provider.SetValue(value);
                    host.UpdateLayout(); await Task.Delay(100);
                    var fill = Descendants(rail).OfType<Border>().SingleOrDefault(part => part.Name == "NativeVolumeIndicator");
                    if (provider.IsReadOnly || provider.Minimum != 0 || provider.Maximum != 100 || slider.Value != value || rail.Value != value || (before != value && events <= beforeEvents))
                        throw new InvalidOperationException($"Volume {mini}/{value}: actual native range/event was not retained.");
                    if (fill == null || fill.ActualHeight != 2 || Math.Abs(fill.ActualWidth - 96 * value / 100) > .5)
                        throw new InvalidOperationException($"Volume {mini}/{value}: bounded two-pixel native value fill is absent or incorrect ({fill?.ActualWidth}x{fill?.ActualHeight}).");
                    var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(rail);
                    if (bitmap.PixelWidth < 94 || bitmap.PixelHeight < 2)
                        throw new InvalidOperationException($"Volume {mini}/{value}: the actual bounded rail bitmap is empty or undersized.");
                    var pixels = (await bitmap.GetPixelsAsync()).ToArray(); var white = 0;
                    for (var index = 0; index < pixels.Length; index += 4)
                        if (pixels[index] > 190 && pixels[index + 1] > 190 && pixels[index + 2] > 190 && pixels[index + 3] > 190) white++;
                    Program.Log($"TRACE: visible volume {mini}/{value}: fill={fill.ActualWidth}x{fill.ActualHeight}, bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}, whitePixels={white}, events={events}.");
                    if (value == 0 ? white != 0 : white < bitmap.PixelWidth * bitmap.PixelHeight * value / 100 * .5)
                        throw new InvalidOperationException($"Volume {mini}/{value}: actual bounded XAML bitmap does not paint its native value.");
                    await MediaParityNativeFixture.CaptureAsync(host, $"media-volume-visible-{mini}-{value}.png");
                }
            }
            finally { parent.Children.Remove(control); await Task.Delay(100); }
        }
        Program.Log("PASS: MEDIA_VOLUME_VISIBLE_COMPLETED actual expanded/mini native input and 0/50/100 fill pixels.");
    }
    internal static async Task RunAsync(StackPanel parent)
    {
        foreach (var mini in new[] { false, true })
        {
            FrameworkElement control = mini ? new MiniPlayerBar { Width = 1280 } : new AudiobookNowListening { Width = 1280, Height = 720 };
            parent.Children.Add(control);
            try
            {
                if (mini)
                {
                    ((FrameworkElement)control.FindName("VideoBar")).Visibility = Visibility.Collapsed;
                    ((FrameworkElement)control.FindName("AudiobookBar")).Visibility = Visibility.Visible;
                }
                control.UpdateLayout(); await Task.Delay(100);
                var slider = (Slider)control.FindName(mini ? "AudiobookVolumeSlider" : "VolumeSlider");
                var host = (Grid)VisualTreeHelper.GetParent(slider);
                var rail = host.Children.OfType<ProgressBar>().Single(bar => (string?)bar.Tag == "native-volume-paint");
                slider.Value = slider.Maximum;
                await InspectAsync(host, rail, mini, "default");
                // Diagnostic only: same real control, value and geometry with
                // the already-used ordinary-XAML track/indicator template.
                rail.Style = (Style)Application.Current.Resources["RequestDownloadProgressBarStyle"];
                rail.Height = 2; rail.MinHeight = 0;
                rail.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                rail.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(51, 255, 255, 255));
                await InspectAsync(host, rail, mini, "xaml-template");
            }
            finally { parent.Children.Remove(control); await Task.Delay(100); }
        }
        Program.Log("PASS: MEDIA_VOLUME_PAINT_DIAGNOSTIC_COMPLETED observed default versus existing XAML template; this does not alone certify native-screen paint.");
    }
    private static async Task InspectAsync(Grid host, ProgressBar rail, bool mini, string name)
    {
        host.UpdateLayout(); await Task.Delay(100);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(host);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray(); var white = 0;
        for (var index = 0; index < pixels.Length; index += 4)
            if (pixels[index] > 190 && pixels[index + 1] > 190 && pixels[index + 2] > 190 && pixels[index + 3] > 190) white++;
        Program.Log($"TRACE: volume {mini}/{name}: host={host.ActualWidth}x{host.ActualHeight}, rail={rail.ActualWidth}x{rail.ActualHeight}, value={rail.Value}, whitePixels={white}, parts=" +
            string.Join(";", Descendants(rail).OfType<FrameworkElement>().Select(part => $"{part.GetType().Name}:{part.Name}={part.ActualWidth}x{part.ActualHeight}")));
        await MediaParityNativeFixture.CaptureAsync(host, $"media-volume-paint-{mini}-{name}.png");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = VisualTreeHelper.GetChild(root, index); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
}
