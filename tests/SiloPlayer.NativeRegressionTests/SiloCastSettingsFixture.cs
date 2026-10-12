using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Views;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class SiloCastSettingsFixture
{
    internal static async Task RunAsync(SettingsPage page)
    {
        typeof(SettingsPage).GetMethod("ShowSettingsDetail", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, [page.FindName("PlaybackTab")]);
        var toggle = (ToggleSwitch)page.FindName("SiloCastToggle");
        var status = (TextBlock)page.FindName("SiloCastStatusText");
        var disconnect = (Button)page.FindName("SiloCastDisconnectButton");
        if (toggle.IsOn || status.Text != "Off" || disconnect.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("SiloCast settings are not off by default.");
        DependencyObject card = toggle;
        while (card is not Border) card = VisualTreeHelper.GetParent(card);
        var border = (Border)card;
        foreach (var width in new[] { 600d, 900d })
        {
            page.Width = width;
            page.Measure(new Windows.Foundation.Size(width, 650));
            page.Arrange(new Windows.Foundation.Rect(0, 0, width, 650));
            page.UpdateLayout();
            await Task.Delay(100);
            Program.Log($"SiloCast layout {width}: page={page.ActualWidth}, toggle={toggle.ActualWidth}, status={status.ActualWidth}, card={border.ActualWidth}, panel={((FrameworkElement)page.FindName("PlaybackPanel")).Visibility}");
            if (toggle.ActualWidth < 30 || status.ActualWidth <= 0 || border.ActualWidth > width)
                throw new InvalidOperationException("SiloCast controls were clipped at settings width " + width);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(border);
            var pixels = await bitmap.GetPixelsAsync();
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
            await encoder.FlushAsync();
            stream.Seek(0);
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[(int)stream.Size];
            reader.ReadBytes(bytes);
            File.WriteAllBytes(Path.Combine(Program.ResultDirectory, $"silocast-settings-{width}.png"), bytes);
        }
        page.Width = 900;
        Program.Log("PASS: SiloCast controls are off by default and fit narrow/desktop settings layouts; PNG previews captured.");
    }
}
