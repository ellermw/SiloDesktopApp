using System.Collections.ObjectModel;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

/// <summary>
/// Interface language through actual controls: the Settings language picker
/// (unknown stored tag, save to Arabic, Match device), the shell's direction and
/// language, a retained SectionRow's server-keyed title, image mirroring, the
/// display culture, a realtime-style change and a retired owner's late read.
/// </summary>
internal static class AppLanguageNativeFixture
{
    private const string Host = "app-language.invalid";

    internal static async Task RunAsync(Panel parent)
    {
        var locales = LocaleService.Shared;
        var before = locales.Current;
        var wire = new Wire { Stored = "he" };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://" + Host);
        client.SetProfile("profile-a");
        var preference = new AppLanguagePreference(new SettingsApi(client));
        var localSettings = new SettingsService(Path.Combine(Program.ResultDirectory, "app-language"));
        var coordinator = new AppLocaleCoordinator(preference, locales, localSettings, () => ["en-US"], "en-US",
            AppLocalization.PostTo(parent.DispatcherQueue));
        coordinator.ApplyBeforeSettings();
        Require(locales.Current.Language == "en", "device en-US did not seed English");

        var shell = new Grid { Width = 900, Height = 700, Background = new SolidColorBrush(Colors.Black) };
        var directionRoot = new StackPanel { Spacing = 16 };
        shell.Children.Add(directionRoot);
        var group = new AppLanguageSettingsGroup(preference, coordinator);
        var keyed = new HomeSectionWithItems
        {
            Id = "recent-movies", SectionType = "recently_added", Title = "Recently Added in Movies",
            TitleKey = "home.section.recentlyAddedIn", TitleArgs = new() { ["library"] = "Movies" },
            KeyedTitle = "Recently Added in Movies", Items = new ObservableCollection<MediaItem>(),
        };
        var row = new SectionRow { Section = keyed };
        var pictureHost = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8 };
        var picture = new Image { Width = 100, Height = 40, Stretch = Stretch.Fill, Source = await TwoColorBitmapAsync() };
        // Positive control: an explicitly flipped copy proves the sampler detects mirroring.
        var flipped = new Image { Width = 100, Height = 40, Stretch = Stretch.Fill, Source = picture.Source,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5), RenderTransform = new ScaleTransform { ScaleX = -1 } };
        pictureHost.Children.Add(picture);
        pictureHost.Children.Add(flipped);
        directionRoot.Children.Add(group);
        directionRoot.Children.Add(row);
        directionRoot.Children.Add(pictureHost);
        using var shellRegistration = AppLocalization.AttachShell(shell, directionRoot);
        parent.Children.Add(shell);
        try
        {
            await Settle(shell);

            // Unknown stored tag: offered by name, selected, never replaced by Match device.
            await group.EnsureLoadedAsync();
            await Settle(shell);
            Require(group.Visibility == Visibility.Visible, "picker hidden on a server that supports ui.app_language");
            Require(group.OfferedTags.SequenceEqual(new string?[] { null, "he", "en", "es", "de", "fr", "pt-BR", "ja", "zh-Hans", "ar" }),
                "picker offers " + string.Join(",", group.OfferedTags.Select(tag => tag ?? "device")));
            Require(group.Picker.SelectedIndex == 1 && ItemText(group.Picker, 1) == "Hebrew", "unknown stored tag not selected by name: " + ItemText(group.Picker, 1));
            Require(group.UnavailableNote.Visibility == Visibility.Visible, "unknown-tag note hidden");
            Require(group.TitleText.Text == "Language" && group.LabelText.Text == "App language", "English picker labels: " + group.LabelText.Text);
            Require(await coordinator.RefreshAsync(), "effective read for the current owner not applied");
            Require(locales.Current.Language == "en" && locales.Current.Resolution.StoredTagUnsupported, "unknown tag did not fall through to the device language");
            Require(directionRoot.FlowDirection == FlowDirection.LeftToRight, "English shell mirrored");
            Program.Log("PASS: app-language unknown stored tag stays selected by name and resolves to the device language.");

            // Choose Arabic in the actual ComboBox.
            wire.Requests.Clear();
            group.Picker.SelectedIndex = group.OfferedTags.ToList().IndexOf("ar");
            await WaitFor(() => locales.Current.Language == "ar", "Arabic was not applied after the picker save");
            await Settle(shell);
            Require(wire.Writes.SequenceEqual(new[]
            {
                "PUT /api/v2/settings/values/ui.app_language?scope=profile&profile_id=profile-a {\"value\":\"ar\"}",
                "DELETE /api/v2/settings/values/ui.app_language?scope=profile_device&profile_id=profile-a",
            }), "picker writes: " + string.Join(" | ", wire.Writes));
            Require(directionRoot.FlowDirection == FlowDirection.RightToLeft && shell.Language == "ar", "shell direction/language not applied");
            Require(group.LabelText.Text == locales.Current.Get("settings.control.appLanguage") && group.LabelText.Text != "App language",
                "picker label not refreshed in Arabic: " + group.LabelText.Text);
            Require(!group.OfferedTags.Contains("he") && group.UnavailableNote.Visibility == Visibility.Collapsed, "replaced unknown tag still offered");
            var title = (TextBlock)row.FindName("SectionTitle");
            Require(title.Text == "أحدث الإضافات في ⁨Movies⁩", "retained SectionRow title: " + title.Text);
            Require(keyed.Title == "Recently Added in Movies", "raw section title changed");
            var titleOrigin = group.TitleText.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point(0, 0));
            var titleRight = group.TitleText.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point(group.TitleText.ActualWidth, 0));
            Require(Math.Max(titleOrigin.X, titleRight.X) > shell.ActualWidth - 40, $"RTL heading not at the right edge: {titleOrigin.X:0}..{titleRight.X:0} of {shell.ActualWidth:0}");
            var pictureLeft = await LeftEdgeColorAsync(picture, shell);
            var flippedLeft = await LeftEdgeColorAsync(flipped, shell);
            Program.Log($"RTL image left-edge colours: artwork={pictureLeft}, flipped control={flippedLeft}.");
            Require(flippedLeft == "blue", "mirroring detector failed its flipped control: " + flippedLeft);
            Require(pictureLeft == "red", "artwork image mirrored or not rendered in the right-to-left shell: " + pictureLeft);
            Require(localSettings.Load().LastAppLanguageOverride == "ar", "applied language not cached for the next launch");
            Program.Log("PASS: app-language picker saves Arabic, clears the device value, mirrors the shell and refreshes retained text.");

            // Display culture follows the stored language when the device speaks another.
            wire.Stored = "ja";
            Require(await coordinator.RefreshAsync(), "realtime-style refresh not applied");
            await WaitFor(() => group.Picker.SelectedIndex == group.OfferedTags.ToList().IndexOf("ja"), "picker did not show the changed stored value");
            await Settle(shell);
            var date = DateTimeDisplay.FormatDate(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero), medium: true);
            Require(locales.Current.FormatCulture.Name == "ja" && date.Contains('月'), "Japanese display date: " + date);
            Require(directionRoot.FlowDirection == FlowDirection.LeftToRight && title.Text == locales.Current.Get("home.section.recentlyAddedIn", new Dictionary<string, object?> { ["library"] = "Movies" }),
                "Japanese title/direction: " + title.Text);
            Program.Log($"PASS: app-language realtime change applies Japanese text, direction and display date ({date}).");

            // A late read for a retired owner is dropped.
            var hold = new TaskCompletionSource();
            wire.Stored = "fr";
            wire.Hold = hold.Task;
            var late = coordinator.RefreshAsync();
            client.SetProfile("profile-b");
            coordinator.Retire();
            hold.SetResult();
            Require(!await late && locales.Current.Language == "ja", "retired owner's late read applied " + locales.Current.Language);
            wire.Hold = null;
            Program.Log("PASS: app-language late read for a retired profile is never applied.");

            // Match device clears the profile value and the cache.
            client.SetProfile("profile-a");
            await group.EnsureLoadedAsync();
            wire.Requests.Clear();
            group.Picker.SelectedIndex = 0;
            await WaitFor(() => locales.Current.Language == "en" && wire.Writes.Count == 2, "Match device not applied");
            Require(wire.Writes[0].EndsWith("{\"value\":null}", StringComparison.Ordinal), "Match device write: " + wire.Writes[0]);
            Require(localSettings.Load().LastAppLanguageOverride == null && directionRoot.FlowDirection == FlowDirection.LeftToRight, "Match device cache/direction");
            Program.Log("PASS: app-language Match device writes null and follows the device again.");
        }
        finally
        {
            parent.Children.Remove(shell);
            locales.Apply(locales.Create(before.Resolution));
        }
    }

    private static string ItemText(ComboBox picker, int index)
        => picker.Items[index] is ComboBoxItem { Content: var content } ? content as string ?? (content as TextBlock)?.Text ?? "" : "";

    private static async Task<WriteableBitmap> TwoColorBitmapAsync()
    {
        // Left half red, right half blue.
        var bitmap = new WriteableBitmap(2, 1);
        using (var stream = bitmap.PixelBuffer.AsStream())
            await stream.WriteAsync(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 });
        bitmap.Invalidate();
        return bitmap;
    }

    // Renders the left-to-right fixture root, so the right-to-left root's mirror is
    // included, and samples the image's on-screen left eighth: red unless mirrored.
    private static async Task<string> LeftEdgeColorAsync(FrameworkElement image, FrameworkElement root)
    {
        var render = new RenderTargetBitmap();
        await render.RenderAsync(root);
        var pixels = (await render.GetPixelsAsync()).ToArray();
        if (render.PixelWidth == 0 || pixels.Length == 0) throw new InvalidOperationException("fixture root did not render");
        var scale = render.PixelWidth / root.ActualWidth;
        var transform = image.TransformToVisual(root);
        var a = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
        var b = transform.TransformPoint(new Windows.Foundation.Point(image.ActualWidth, image.ActualHeight));
        var x = (int)((Math.Min(a.X, b.X) + Math.Abs(b.X - a.X) / 8) * scale);
        var y = (int)((Math.Min(a.Y, b.Y) + Math.Abs(b.Y - a.Y) / 2) * scale);
        var offset = (y * render.PixelWidth + x) * 4;
        var (blue, red) = (pixels[offset], pixels[offset + 2]);
        return red > 160 && blue < 96 ? "red" : blue > 160 && red < 96 ? "blue" : $"other(r={red},b={blue})";
    }

    private static async Task Settle(FrameworkElement element)
    {
        for (var i = 0; i < 4; i++) { element.UpdateLayout(); await Task.Delay(50); }
    }

    private static async Task WaitFor(Func<bool> condition, string message)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Require(condition(), message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("app-language: " + message);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public string? Stored { get; set; }
        public Task? Hold { get; set; }
        public List<string> Requests { get; } = [];
        public List<string> Writes => Requests.Where(request => !request.StartsWith("GET", StringComparison.Ordinal)).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != Host) throw new InvalidOperationException("External fixture request refused.");
            var body = request.Content == null ? "" : " " + await request.Content.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add($"{request.Method} {uri.PathAndQuery}{body}");
            string Value() => Stored == null ? "null" : "\"" + Stored + "\"";
            if (uri.AbsolutePath == "/api/v2/settings/contract/capabilities")
                return Json("""{"api_version":1,"manifest_revision":28,"supports_batched_effective":true}""");
            if (uri.AbsolutePath == "/api/v2/settings/values/effective")
            {
                var value = Value();
                if (Hold is { } hold) await hold.WaitAsync(ct);
                return Json($$"""{"items":[{"key":"ui.app_language","value":{{value}},"source":"profile"}],"revision":1}""");
            }
            if (uri.AbsolutePath == "/api/v2/settings/values/ui.app_language")
            {
                if (request.Method == HttpMethod.Get)
                    return Stored == null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                        : Json($$"""{"key":"ui.app_language","value":{{Value()}},"stored_value":{{Value()}},"source":"profile"}""");
                if (request.Method == HttpMethod.Put)
                {
                    using var document = System.Text.Json.JsonDocument.Parse(body);
                    var value = document.RootElement.GetProperty("value");
                    Stored = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                    return Json($$"""{"key":"ui.app_language","value":{{Value()}},"source":"profile"}""");
                }
                if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            throw new InvalidOperationException("Unexpected fixture route " + uri.PathAndQuery);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
