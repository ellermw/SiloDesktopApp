using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;

/// <summary>
/// Official small changes, batch 2 (#5081 focus ring on red buttons, #4646 Enter in the Match Item dialog, #5070 alert dialogs that stay
/// reachable on a short screen). Real shared styles, the real Match Item dialog and the real confirm helpers in a hidden native window
/// with synthetic data. Nothing is written outside <see cref="Program.ResultDirectory"/>.
/// Program.cs dispatch: SILO_NATIVE_TEST_ONLY=official-small-changes-2.
/// </summary>
internal static class OfficialSmallChanges2NativeFixture
{
    private static readonly List<string> Failures = [];
    private static ResourceDictionary Res => Application.Current.Resources;
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static void Info(string message) => Program.Log("INFO: " + message);
    private static async Task Until(Func<bool> ready, string label, int milliseconds = 6000)
    {
        for (var attempt = 0; attempt < milliseconds / 20 && !ready(); attempt++) await Task.Delay(20);
        if (!ready()) throw new TimeoutException("Timed out: " + label);
    }
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    internal static async Task RunAsync(string only = "")
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new MatchWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://small-changes-2.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        slot.SetValue(null, new Services(original, client, auth));
        var root = new Grid { Background = (Brush)Res["AppBackgroundBrush"] };
        var window = new Window { Content = root };
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1000, 800)); window.AppWindow.Show(false); await Task.Delay(200);
            if (only is "" or "focus") await FocusRingsAsync(window, root);
            if (only is "" or "match") await MatchEnterAsync(window, root, wire, auth);
            if (only is "" or "confirm") await ShortConfirmAsync(window, root, client, auth);
            if (Failures.Count > 0) throw new InvalidOperationException("Official small changes 2 checks failed: " + string.Join("; ", Failures));
            Program.Log("PASS: focus rings, Match Item Enter and short-window confirm dialogs.");
        }
        finally { wire.HintGate?.TrySetResult(); wire.SearchGate?.TrySetResult(); slot.SetValue(null, original); window.Close(); }
    }

    // ───────────────────────────── Item 1: #5081 focus ring on the destructive button ─────────────────────────────

    private static async Task FocusRingsAsync(Window window, Grid root)
    {
        root.Children.Clear();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28, Padding = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Button Make(string content, string style)
        {
            var button = new Button { Content = content, Style = (Style)Res[style] };
            AutomationProperties.SetAutomationId(button, content);
            strip.Children.Add(button); return button;
        }
        var buttons = new[] { Make("Primary", "AccentButtonStyle"), Make("Outline", "OutlineButtonStyle"), Make("Secondary", "SecondaryButtonStyle"),
            Make("Ghost", "GhostButtonStyle"), Make("Destructive", "DestructiveButtonStyle") };
        var tabStop = new Button { Content = "Elsewhere" }; strip.Children.Add(tabStop);
        root.Children.Add(strip); strip.UpdateLayout(); await Task.Delay(200);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var sampled = new Dictionary<string, (Color Ring, bool Found, double Left)>();
        // The hidden window's presented frame can show the focus visual of the previous focus step (a frame behind). So every capture is
        // read for which button carries the ring, instead of assuming it is the one just focused, and one more step moves focus on.
        foreach (var button in buttons.Append(tabStop))
        {
            Check(button.Focus(FocusState.Keyboard), $"{button.Content}: takes keyboard focus");
            await Task.Delay(350); strip.UpdateLayout();
            Check(button.FocusState == FocusState.Keyboard, $"{button.Content}: its focus is keyboard focus (focus-visible)");
            Check(button.UseSystemFocusVisuals && button.IsTabStop, $"{button.Content}: asks for the system focus visual and is a tab stop");
            var primary = button.FocusVisualPrimaryBrush as SolidColorBrush; var secondary = button.FocusVisualSecondaryBrush as SolidColorBrush;
            Info($"{button.Content}: FocusVisualPrimaryBrush={Hex(primary)} (opacity {primary?.Opacity:0.##}) thickness={button.FocusVisualPrimaryThickness.Left} secondary={Hex(secondary)} margin={button.FocusVisualMargin.Left}");
            foreach (var attempt in new[] { 0, 1 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1001 + attempt, 800)); await Task.Delay(250);
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1000, 800)); await Task.Delay(350);
                var pixels = PrintWindow(hwnd, out var width, out var height);
                var scale = width / Math.Max(1d, root.ActualWidth);
                foreach (var candidate in buttons)
                {
                    var name = (string)candidate.Content; if (sampled.ContainsKey(name)) continue;
                    var bounds = candidate.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, candidate.ActualWidth, candidate.ActualHeight));
                    var (ring, found, left) = Ring(pixels, width, height, bounds, scale, root);
                    if (!found) continue;
                    sampled[name] = (ring, true, left); shots[name] = (width, height, pixels);
                    Info($"{name}: ring seen in the capture taken with focus on {button.Content}: colour={Hex(ring)}, {left:0.0} px outside the button edge");
                    await SaveAsync(pixels, width, height, $"small2-focus-{name.ToLowerInvariant()}.png");
                }
                if (sampled.Count == buttons.Length) break;
            }
        }
        foreach (var name in buttons.Select(item => (string)item.Content)) Check(sampled.ContainsKey(name), $"{name}: a keyboard-focused {name} button shows a focus ring in the window capture");
        // Side by side: the focused destructive button next to the focused ordinary (Outline) one, as the official change compares them.
        var d = shots.GetValueOrDefault("Destructive"); var o = shots.GetValueOrDefault("Outline");
        if (d.W == o.W && d.H == o.H && d.W > 0)
        {
            var crop = Math.Min(d.H, 220);
            var combined = new byte[d.W * 2 * crop * 4];
            for (var y = 0; y < crop; y++)
            {
                Buffer.BlockCopy(o.Pixels, y * d.W * 4, combined, (y * d.W * 2) * 4, d.W * 4);
                Buffer.BlockCopy(d.Pixels, y * d.W * 4, combined, (y * d.W * 2 + d.W) * 4, d.W * 4);
            }
            await SaveAsync(combined, d.W * 2, crop, "small2-focus-outline-vs-destructive.png");
        }
        var accent = buttons[0]; var destructive = buttons[4]; var outline = buttons[1];
        foreach (var other in new[] { outline, buttons[2], buttons[3], destructive })
        {
            Check(Hex(other.FocusVisualPrimaryBrush as SolidColorBrush) == Hex(accent.FocusVisualPrimaryBrush as SolidColorBrush)
                && other.FocusVisualPrimaryThickness.Left == accent.FocusVisualPrimaryThickness.Left
                && Hex(other.FocusVisualSecondaryBrush as SolidColorBrush) == Hex(accent.FocusVisualSecondaryBrush as SolidColorBrush),
                $"{other.Content}: focus visual brush, thickness and secondary brush are the primary button's");
        }
        if (sampled.Count == buttons.Length)
        {
            var reference = sampled["Outline"];
            foreach (var name in new[] { "Primary", "Secondary", "Ghost", "Destructive" })
                Check(Near(sampled[name].Ring, reference.Ring, 12) && Math.Abs(sampled[name].Left - reference.Left) <= 1.5,
                    $"{name}: the rendered ring ({Hex(sampled[name].Ring)}, {sampled[name].Left:0.0} px out) matches the Outline button's ({Hex(reference.Ring)}, {reference.Left:0.0} px out)");
            Check(!IsRed(sampled["Destructive"].Ring), "the rendered ring around the destructive button is not red");
            Check(Contrast(sampled["Destructive"].Ring, ((SolidColorBrush)Res["AppBackgroundBrush"]).Color) >= 3, $"the rendered destructive ring has {Contrast(sampled["Destructive"].Ring, ((SolidColorBrush)Res["AppBackgroundBrush"]).Color):0.00}:1 against the page (official: 4.54:1)");
        }
        else Info("the rendered focus ring could not be sampled in this host; only the properties above were compared");
        root.Children.Clear();
    }

    private static bool IsRed(Color c) => c.R > 180 && c.G < 90 && c.B < 90;
    private static string Hex(SolidColorBrush? b) => b is null ? "(none)" : Hex(b.Color);
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static bool Near(Color a, Color b, int tolerance) => Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
    private static double Luminance(Color c)
    {
        static double Channel(byte v) { var s = v / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Scans left from a button's edge across its row of the window capture for the focus ring: the most vivid pixel that is not the page background.</summary>
    private static (Color Color, bool Found, double Left) Ring(byte[] pixels, int width, int height, Windows.Foundation.Rect bounds, double scale, Grid root)
    {
        if (pixels.Length < width * height * 4 || width == 0) return (default, false, 0);
        var background = ((SolidColorBrush)Res["AppBackgroundBrush"]).Color;
        var y = (int)((bounds.Top + bounds.Height / 2) * scale); var edge = (int)(bounds.Left * scale);
        Color best = default; var bestScore = 0; var bestDistance = 0d;
        for (var step = 1; step <= (int)(10 * scale); step++)
        {
            var x = edge - step; if (x < 0 || y < 0 || y >= height) break;
            var offset = (y * width + x) * 4;
            var pixel = Color.FromArgb(255, pixels[offset + 2], pixels[offset + 1], pixels[offset]);
            var score = Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B);
            if (score > bestScore) { bestScore = score; best = pixel; bestDistance = step / scale; }
        }
        return (best, bestScore > 90, bestDistance);
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, byte[]? bits, ref BitmapInfo info, uint usage);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ColorsUsed, ColorsImportant;
    }

    /// <summary>The window's own client pixels (PW_CLIENTONLY | PW_RENDERFULLCONTENT), including what the framework draws outside element bounds such as focus visuals.</summary>
    private static byte[] PrintWindow(IntPtr hwnd, out int width, out int height)
    {
        width = height = 0;
        if (!GetClientRect(hwnd, out var rect)) return [];
        width = rect.Right - rect.Left; height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return [];
        var screen = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(screen); var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memory, 3)) return new byte[width * height * 4];
            var info = new BitmapInfo { Size = Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var bits = new byte[width * height * 4];
            SelectObject(memory, previous);
            GetDIBits(memory, bitmap, 0, (uint)height, bits, ref info, 0);
            for (var i = 3; i < bits.Length; i += 4) bits[i] = 255;
            return bits;
        }
        finally { DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }

    private static async Task SaveAsync(byte[] pixels, int width, int height, string name)
    {
        if (pixels.Length < width * height * 4 || width == 0) return;
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
    }

    // ───────────────────────────── Item 2: #4646 Enter in the Match Item dialog ─────────────────────────────

    private static MatchItemDialog Match(FrameworkElement owner, string type) => new("fixture", "Fixture", 2026, type, null, null, null) { XamlRoot = owner.XamlRoot };
    private static Button SearchOf(MatchItemDialog dialog) => (Button)dialog.FindName("SearchButton");
    private static TextBox BoxOf(MatchItemDialog dialog, string name) => (TextBox)dialog.FindName(name);
    private static string Field(string text) => text;

    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    private static readonly HashSet<TextBox> Observed = [];
    private static readonly Dictionary<TextBox, int> EnterDelivered = [];
    private static int _realKeys, _fallbackKeys;

    /// <summary>
    /// Presses Enter in a field: a real key message posted to this process's focused window when the host delivers it, otherwise the
    /// dialog's own Enter entry point (the handler's body), which the log says.
    /// </summary>
    private static async Task PressEnterAsync(MatchItemDialog dialog, TextBox box)
    {
        if (Observed.Add(box)) { EnterDelivered[box] = 0; box.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) EnterDelivered[box]++; }), true); }
        box.Focus(FocusState.Keyboard); await Task.Delay(60);
        var window = GetFocus(); GetWindowThreadProcessId(window, out var process);
        var before = EnterDelivered[box];
        if (window != IntPtr.Zero && process == (uint)Environment.ProcessId)
        {
            PostMessage(window, 0x0100, 0x0D, 1); PostMessage(window, 0x0101, 0x0D, unchecked((nint)0xC0000001u));
            for (var i = 0; i < 25 && EnterDelivered[box] == before; i++) await Task.Delay(20);
            if (EnterDelivered[box] > before) { _realKeys++; return; }
        }
        _fallbackKeys++;
        var enter = typeof(MatchItemDialog).GetMethod("SearchFromEnter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MatchItemDialog.SearchFromEnter is missing");
        enter.Invoke(dialog, null);
    }

    private static async Task MatchEnterAsync(Window window, Grid owner, MatchWire wire, AuthService auth)
    {
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Permissions = [AuthorizationPolicy.MetadataCuration] });

        // Enter while the dialog is still loading its hints: the Search button is disabled, so Enter does what the button does: nothing.
        wire.Reset(); wire.HintGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = Match(owner, "movie"); var showing = dialog.ShowAsync();
        try
        {
            await Until(() => wire.HintReads == 1, "the hints request is in flight");
            Check(!SearchOf(dialog).IsEnabled, "Match: the Search button is disabled while the hints load");
            await PressEnterAsync(dialog, BoxOf(dialog, "TitleBox")); await Task.Delay(300);
            Check(wire.Count("search") == 0, "Match: Enter while the hints load sends no search (official: the same disabled conditions as the button)");
            wire.HintGate.SetResult(); wire.HintGate = null;
            await Until(() => SearchOf(dialog).IsEnabled, "Search enabled once the hints arrive");
            await Task.Delay(400);
            Check(wire.Count("search") == 0, "Match: that Enter is not queued to start a search when the hints arrive");
            Check(showing.Status == Windows.Foundation.AsyncStatus.Started, "Match: Enter did not close the dialog");

            // Enter in every field of a video dialog runs the search with what is in the fields.
            var cases = new (string Box, string Text, string Json)[]
            {
                ("TitleBox", "Typed title", "title"), ("YearBox", "1999", "year"), ("ImdbIdBox", "tt0133093", "imdb_id"),
                ("TmdbIdBox", "603", "tmdb_id"), ("TvdbIdBox", "2", "tvdb_id"),
            };
            foreach (var (boxName, text, json) in cases)
            {
                var box = BoxOf(dialog, boxName); box.Text = text; var count = wire.Count("search");
                await PressEnterAsync(dialog, box);
                await Until(() => wire.Count("search") == count + 1, $"Enter in {boxName} sends one search");
                await Until(() => SearchOf(dialog).IsEnabled, $"Search settles after Enter in {boxName}");
                var body = wire.Last("search");
                var sent = body.TryGetProperty(json, out var value) ? (value.ValueKind == JsonValueKind.Number ? value.GetInt32().ToString() : value.GetString()) : null;
                Check(sent == text, $"Match: Enter in {boxName} searches with the typed value ({json}={sent})");
                Check(showing.Status == Windows.Foundation.AsyncStatus.Started && wire.Count("apply") == 0, $"Match: Enter in {boxName} neither closes the dialog nor applies anything");
            }

            // A running search is not started twice.
            wire.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var before = wire.Count("search");
            await PressEnterAsync(dialog, BoxOf(dialog, "TitleBox"));
            await Until(() => wire.Count("search") == before + 1, "the first Enter starts the search");
            Check(!SearchOf(dialog).IsEnabled, "Match: the Search button is disabled while the search runs");
            await PressEnterAsync(dialog, BoxOf(dialog, "TitleBox")); await PressEnterAsync(dialog, BoxOf(dialog, "YearBox"));
            await Task.Delay(300);
            Check(wire.Count("search") == before + 1, "Match: further Enter presses during a running search start no second one");
            wire.SearchGate.SetResult(); wire.SearchGate = null;
            await Until(() => SearchOf(dialog).IsEnabled && All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Fixture candidate"), "the search finishes with its candidate");
            // With a candidate chosen (Apply Match shown) Enter still only searches.
            Invoke(All<Button>(dialog).First(button => AutomationProperties.GetName(button) == "Fixture candidate")); await Task.Delay(120);
            var applyButton = (Button)dialog.FindName("ApplyMatchButton");
            Check(applyButton.Visibility == Visibility.Visible && applyButton.IsEnabled, "Match: a chosen candidate shows an enabled Apply Match");
            before = wire.Count("search");
            await PressEnterAsync(dialog, BoxOf(dialog, "TitleBox"));
            await Until(() => wire.Count("search") == before + 1, "Enter searches again with a candidate chosen");
            await Until(() => SearchOf(dialog).IsEnabled, "search settles");
            Check(wire.Count("apply") == 0 && showing.Status == Windows.Foundation.AsyncStatus.Started, "Match: Enter never presses Apply Match and never closes the dialog");
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)dialog.Content, "small2-match-video-after-enter.png");
        }
        finally { wire.HintGate?.TrySetResult(); wire.SearchGate?.TrySetResult(); dialog.Hide(); await showing; }

        // The generic dialog (provider identifiers): title, year and every provider row.
        wire.Reset();
        dialog = Match(owner, "audiobook"); showing = dialog.ShowAsync();
        try
        {
            await Until(() => SearchOf(dialog).IsEnabled, "generic Match ready");
            var provider = All<TextBox>(dialog).First(box => AutomationProperties.GetName(box) == "Provider");
            var providerId = All<TextBox>(dialog).First(box => AutomationProperties.GetName(box) == "Provider ID");
            provider.Text = "isbn"; providerId.Text = "9780140449136";
            foreach (var (box, label) in new[] { (BoxOf(dialog, "GenericTitleBox"), "title"), (BoxOf(dialog, "GenericYearBox"), "year"), (provider, "provider"), (providerId, "provider id") })
            {
                var count = wire.Count("search");
                await PressEnterAsync(dialog, box);
                await Until(() => wire.Count("search") == count + 1, $"Enter in the generic {label} field sends one search");
                await Until(() => SearchOf(dialog).IsEnabled, $"generic Search settles after Enter in {label}");
                var ids = wire.Last("search").TryGetProperty("provider_ids", out var found) && found.ValueKind == JsonValueKind.Object ? found.GetProperty("isbn").GetString() : null;
                Check(ids == "9780140449136", $"Match: Enter in the generic {label} field searches with the provider identifier ({ids})");
            }
            // Add / remove provider ID buttons are not submit buttons: pressing them searches nothing.
            var count2 = wire.Count("search");
            Invoke((Button)dialog.FindName("AddProviderIdButton")); await Task.Delay(150);
            Check(wire.Count("search") == count2 && All<TextBox>(dialog).Count(box => AutomationProperties.GetName(box) == "Provider") == 2, "Match: Add provider ID adds a row and searches nothing");
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)dialog.Content, "small2-match-generic-after-enter.png");
        }
        finally { dialog.Hide(); await showing; }
        Info($"Match Enter keys: {_realKeys} delivered as real key messages, {_fallbackKeys} through the dialog's Enter entry point (the handler's body)");
    }

    // ───────────────────────────── Item 3: #5070 alert dialogs keep their actions reachable ─────────────────────────────

    private static IEnumerable<FrameworkElement> OpenDialogRoots(XamlRoot root) =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(popup => popup.Child).OfType<FrameworkElement>();

    private static Button? Command(XamlRoot root, string name) =>
        OpenDialogRoots(root).SelectMany(All<Button>).FirstOrDefault(button => button.Name == name);

    /// <summary>Whether a dialog command button lies inside the window and can take keyboard focus.</summary>
    private static void CheckReachable(string label, XamlRoot root, string name, bool mustBeVisible = true)
    {
        var button = Command(root, name);
        if (button is null) { Check(!mustBeVisible, $"{label}: {name} exists"); return; }
        if (button.Visibility != Visibility.Visible) { Check(!mustBeVisible, $"{label}: {name} is visible"); return; }
        var size = root.Size;
        var bounds = button.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
        var inside = bounds.Top >= -0.5 && bounds.Bottom <= size.Height + 0.5 && bounds.Left >= -0.5 && bounds.Right <= size.Width + 0.5 && button.ActualHeight > 0;
        Check(inside, $"{label}: {name} is inside the {size.Width:0}x{size.Height:0} window (top {bounds.Top:0}, bottom {bounds.Bottom:0}, left {bounds.Left:0}, right {bounds.Right:0})");
        Check(button.IsEnabled && button.Focus(FocusState.Keyboard), $"{label}: {name} takes keyboard focus");
    }

    private static async Task ShortConfirmAsync(Window window, Grid owner, SiloApiClient client, AuthService auth)
    {
        auth.SetCurrentUser(new() { Id = "fixture", Role = "admin" });
        var longName = string.Join(" ", Enumerable.Repeat("An extremely long collection name", 8));
        string LongBody() => string.Join("\n", Enumerable.Range(1, 70).Select(i => $"{i}. This body is long on purpose so the dialog is taller than a short window."));
        foreach (var (width, height) in new[] { (800, 500), (560, 420) })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height)); await Task.Delay(250); owner.UpdateLayout();
            var tag = $"{width}x{height}";

            // The shared dialog every plain confirm uses: long title, long body, destructive primary and a close button.
            var dialog = new ContentDialog
            {
                XamlRoot = owner.XamlRoot, Title = longName, Content = new TextBlock { Text = LongBody(), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Delete", PrimaryButtonStyle = (Style)Res["DestructiveButtonStyle"], CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
            };
            var shown = dialog.ShowAsync();
            try
            {
                await Task.Delay(400); dialog.UpdateLayout();
                CheckReachable($"shared confirm {tag}", owner.XamlRoot, "PrimaryButton"); CheckReachable($"shared confirm {tag}", owner.XamlRoot, "CloseButton");
                var scroller = OpenDialogRoots(owner.XamlRoot).SelectMany(All<ScrollViewer>).FirstOrDefault(item => item.Name == "ContentScrollViewer");
                Check(scroller is { ScrollableHeight: > 0 }, $"shared confirm {tag}: the long body scrolls ({scroller?.ScrollableHeight:0} px to scroll) while the actions stay put");
                if (scroller is not null)
                {
                    scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(150);
                    Check(scroller.VerticalOffset >= scroller.ScrollableHeight - 1, $"shared confirm {tag}: the end of the body can be scrolled to");
                    Check(scroller.IsTabStop && scroller.Focus(FocusState.Keyboard), $"shared confirm {tag}: the scrolling body is a keyboard stop (arrow and page keys scroll it)");
                    CheckReachable($"shared confirm {tag} scrolled to the end", owner.XamlRoot, "PrimaryButton");
                    scroller.ChangeView(null, 0, null, true); await Task.Delay(100);
                }
                await MediaParityNativeFixture.CaptureAsync(owner, $"small2-confirm-shared-{tag}.png");
                await SaveWindowAsync(window, $"small2-confirm-shared-{tag}-window.png");
            }
            finally { dialog.Hide(); await shown; }

            // The real collection delete confirmation, then its error line grown to many lines.
            var deleting = (Task<bool>)typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Helpers.CollectionDeleteDialog")!.GetMethod("ShowAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [owner.XamlRoot, longName, true,
                (Func<Task<string?>>)(() => Task.FromResult<string?>(string.Join(" ", Enumerable.Repeat("The collection could not be deleted because the server answered with a long explanation.", 14))))])!;
            try
            {
                await Task.Delay(400);
                CheckReachable($"collection delete {tag}", owner.XamlRoot, "PrimaryButton"); CheckReachable($"collection delete {tag}", owner.XamlRoot, "CloseButton");
                Invoke(Command(owner.XamlRoot, "PrimaryButton")!); await Task.Delay(500);
                CheckReachable($"collection delete {tag} with its long error", owner.XamlRoot, "PrimaryButton"); CheckReachable($"collection delete {tag} with its long error", owner.XamlRoot, "CloseButton");
                await SaveWindowAsync(window, $"small2-confirm-collection-delete-{tag}-window.png");
            }
            finally { if (Command(owner.XamlRoot, "CloseButton") is { } close) Invoke(close); await deleting; }

            // The settings pages' discard confirmation owns its two actions inside its content.
            using var authority = new AdminMonitorAuthority(client, auth); using var lease = authority.Capture();
            ContentDialog? current = null;
            var discarding = (Task<bool?>)typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Controls.AdminSettingsUi")!.GetMethod("ConfirmDiscardAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [owner, lease, (Func<ContentDialog?>)(() => current), (Action<ContentDialog?>)(value => current = value)])!;
            try
            {
                await Task.Delay(400);
                var discard = OpenDialogRoots(owner.XamlRoot).SelectMany(All<Button>).FirstOrDefault(button => AutomationProperties.GetName(button) == "Discard");
                var cancel = OpenDialogRoots(owner.XamlRoot).SelectMany(All<Button>).FirstOrDefault(button => AutomationProperties.GetName(button) == "Cancel");
                Check(discard is not null && cancel is not null, $"discard confirm {tag}: Discard and Cancel exist");
                Check(OpenDialogRoots(owner.XamlRoot).SelectMany(All<ScrollViewer>).FirstOrDefault(item => item.Name == "ContentScrollViewer") is { IsTabStop: false }, $"discard confirm {tag}: a body that fits adds no keyboard stop");
                foreach (var button in new[] { discard, cancel }.OfType<Button>())
                {
                    var bounds = button.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Check(bounds.Bottom <= owner.XamlRoot.Size.Height + 0.5 && bounds.Top >= -0.5 && bounds.Right <= owner.XamlRoot.Size.Width + 0.5 && bounds.Left >= -0.5, $"discard confirm {tag}: {AutomationProperties.GetName(button)} is inside the window (bottom {bounds.Bottom:0})");
                    Check(button.Focus(FocusState.Keyboard), $"discard confirm {tag}: {AutomationProperties.GetName(button)} takes keyboard focus");
                }
                await SaveWindowAsync(window, $"small2-confirm-discard-{tag}-window.png");
            }
            finally { current?.Hide(); await discarding; }
        }
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1000, 800)); await Task.Delay(200);
    }

    private static async Task SaveWindowAsync(Window window, string name)
    {
        var size = window.AppWindow.ClientSize;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(size.Width + 1, size.Height)); await Task.Delay(250);
        window.AppWindow.ResizeClient(size); await Task.Delay(450);
        var pixels = PrintWindow(WinRT.Interop.WindowNative.GetWindowHandle(window), out var width, out var height);
        await SaveAsync(pixels, width, height, name);
    }

    // ───────────────────────────── Synthetic server ─────────────────────────────

    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth
            : type == typeof(MediaMaintenanceApi) ? new MediaMaintenanceApi(client) : original.GetService(type);
    }

    private sealed class MatchWire : HttpMessageHandler
    {
        private readonly List<(string Path, JsonElement Body)> _posts = [];
        public TaskCompletionSource? HintGate, SearchGate; public int HintReads;
        public void Reset() { _posts.Clear(); HintGate = SearchGate = null; HintReads = 0; }
        public int Count(string suffix) => _posts.Count(call => call.Path.EndsWith("/" + suffix));
        public JsonElement Last(string suffix) => _posts.Last(call => call.Path.EndsWith("/" + suffix)).Body;
        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/match/hints"))
            {
                HintReads++; if (HintGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                return Json("{\"title\":\"Fixture\",\"year\":2026,\"provider_ids\":{}}");
            }
            if (request.Method == HttpMethod.Post && (path.EndsWith("/match/search") || path.EndsWith("/match/apply")))
            {
                var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
                _posts.Add((path.EndsWith("/search") ? "/search" : "/apply", body));
                if (path.EndsWith("/search"))
                {
                    if (SearchGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                    return Json("{\"candidates\":[{\"title\":\"Fixture candidate\",\"year\":2026,\"provider_ids\":{\"tmdb\":\"42\"}}]}");
                }
                return Json("{\"content_id\":\"fixture\"}");
            }
            return Json("{}");
        }
    }
}
