using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Services;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

/// <summary>Actual shared OSC popup, without account access, physical input or playback mutation.</summary>
internal static class PlaybackPopupLayoutNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(Window owner)
    {
        var type = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackChoiceMenu")!;
        var choice = type.Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackMenuChoice")!;
        var anchor = type.Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackMenuAnchor")!;
        var ctor = type.GetConstructors(Private).Single();
        var closeType = ctor.GetParameters()[6].ParameterType;
        var close = Expression.Lambda(closeType, Expression.Empty(),
            Expression.Parameter(closeType.GenericTypeArguments[0])).Compile();
        var failures = new List<string>();
        if (owner.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        var work = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        foreach (var (width, height, count, footerVisible, textScale, atEdge) in new[]
        {
            (1280, 720, 1, false, 1d, false), (1280, 720, 2, false, 1d, false),
            (1280, 720, 7, false, 1d, false), (800, 600, 2, false, 1d, false),
            (460, 300, 24, true, 1d, false), (800, 600, 2, true, 1.25, false),
            (1280, 720, 24, true, 1.25, false), (800, 600, 2, false, 1d, true),
        })
        {
            var label = $"{width}x{height}-{count}-footer{footerVisible}-text{textScale}" + (atEdge ? "-screen-edge" : "");
            owner.AppWindow.MoveAndResize(new RectInt32(atEdge ? work.X + work.Width - width + 100 : -20000,
                atEdge ? work.Y + work.Height - height + 100 : -20000, width, height));
            using var video = new MpvVideoWindow(); video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
            var choices = Array.CreateInstance(choice, count);
            for (var i = 0; i < count; i++) choices.SetValue(Activator.CreateInstance(choice,
                [$"{i}", i == 0 ? "Auto" : $"Subtitle track {i}", i == 0 ? "Server recommended quality" : "English · External subtitle",
                    Array.Empty<string>(), i == 0, true, "", false, false]), i);
            var footer = new StackPanel { Spacing = 4, Padding = new Thickness(12, 4, 12, 12),
                Visibility = footerVisible ? Visibility.Visible : Visibility.Collapsed };
            footer.Children.Add(new Button { Content = "Add subtitles", HorizontalAlignment = HorizontalAlignment.Stretch });
            footer.Children.Add(new Button { Content = "Subtitle appearance", HorizontalAlignment = HorizontalAlignment.Stretch });
            void Typography(DependencyObject root)
            {
                foreach (var text in Descendants<TextBlock>(root)) text.FontSize = textScale * (text.Text is "Quality" or "Subtitles" ? 18 : 14);
            }
            var menu = ctor.Invoke([video, Activator.CreateInstance(anchor, [.8d, .92d, .95d, .98d]),
                footerVisible ? "Subtitles" : "Quality", choices, (Func<bool>)(() => true), (Action<string>)(_ => { }), close,
                (Action<DependencyObject>)Typography, footer, null, false]);
            try
            {
                await (Task)type.GetMethod("ShowAsync", Private)!.Invoke(menu, null)!;
                await Task.Delay(200);
                var root = (Grid)type.GetField("_root", Private)!.GetValue(menu)!;
                var list = (ListView)type.GetField("_list", Private)!.GetValue(menu)!;
                var hwnd = (IntPtr)type.GetProperty("Hwnd", Private)!.GetValue(menu)!;
                GetWindowRect(hwnd, out var bounds); GetClientRect(hwnd, out var client); GetWindowRect(video.Hwnd, out var surface);
                var scale = root.XamlRoot.RasterizationScale;
                var frame = GetWindowLongPtrW(hwnd, -16).ToInt64() & 0x00C40000;
                var extended = GetWindowLongPtrW(hwnd, -20).ToInt64() & 0x00020301;
                if (frame != 0 || extended != 0 || bounds.Right - bounds.Left != client.Right || bounds.Bottom - bounds.Top != client.Bottom)
                    failures.Add($"{label}: unwanted native frame style={frame:X}, ex={extended:X}, outer={bounds.Right-bounds.Left}x{bounds.Bottom-bounds.Top}, client={client.Right}x{client.Bottom}");
                if (bounds.Left < surface.Left || bounds.Right > surface.Right || bounds.Top < surface.Top || bounds.Bottom > surface.Bottom)
                    failures.Add($"{label}: popup extends beyond playback bounds");
                if (atEdge && (bounds.Left < work.X || bounds.Right > work.X + work.Width || bounds.Top < work.Y || bounds.Bottom > work.Y + work.Height))
                    failures.Add($"{label}: popup extends beyond the screen work area");
                if (count <= 2)
                {
                    var rows = list.Items.Cast<ListViewItem>().Sum(row => row.ActualHeight);
                    var heading = ((FrameworkElement)root.Children[0]).ActualHeight;
                    var used = rows + heading + (footerVisible ? footer.ActualHeight : 0) + 24;
                    if (root.ActualHeight > used + 32)
                        failures.Add($"{label}: excessive blank space root={root.ActualHeight:0.0}, content={used:0.0}, scale={scale}");
                }
                type.GetMethod("Input", Private)!.Invoke(menu, ["end"]);
                await Task.Delay(80);
                // ListView's trailing container padding can extend past its
                // viewport; assert the actual label/check content is visible.
                if (list.SelectedItem is ListViewItem { Content: FrameworkElement selected })
                {
                    foreach (var text in Descendants<TextBlock>(selected).Where(text => text.Text.Length > 0))
                    {
                        var rowBounds = text.TransformToVisual(list).TransformBounds(new Windows.Foundation.Rect(0, 0, text.ActualWidth, text.ActualHeight));
                        if (rowBounds.Top < -1 || rowBounds.Bottom > list.ActualHeight + 1)
                            failures.Add($"{label}: final label/check is clipped (top={rowBounds.Top:0.0}, bottom={rowBounds.Bottom:0.0}, viewport={list.ActualHeight:0.0})");
                    }
                }
                if (footerVisible)
                {
                    var footerBounds = footer.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, footer.ActualWidth, footer.ActualHeight));
                    if (footerBounds.Bottom > root.ActualHeight + 1 || list.ActualHeight < 30)
                        failures.Add($"{label}: subtitle footer clips the list or leaves the popup");
                }
                await Capture(root, label);
                Program.Log($"Popup {label}: outer={bounds.Right-bounds.Left}x{bounds.Bottom-bounds.Top}, XAML={root.ActualWidth:0}x{root.ActualHeight:0}, list={list.ActualHeight:0}, footer={footer.ActualHeight:0}, frame={frame:X}/{extended:X}.");
            }
            finally
            {
                type.GetMethod("Input", Private)!.Invoke(menu, ["cancel"]);
                if (atEdge) owner.AppWindow.Move(new PointInt32(-20000, -20000));
            }
        }
        foreach (var failure in failures) Program.Log("FAIL: " + failure);
        if (failures.Count != 0) throw new InvalidOperationException($"{failures.Count} shared popup layout/frame failures.");
        Program.Log("PASS: actual OSC shared Quality/Subtitle popup sizes to content, stays in playback bounds, has no native frame and keeps final rows/footer reachable at compact and enlarged text sizes.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Capture(FrameworkElement root, string name)
    {
        var target = new RenderTargetBitmap(); await target.RenderAsync(root);
        var pixels = await target.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory);
        var file = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        var bytes = new byte[pixels.Length]; using (var reader = DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
}
