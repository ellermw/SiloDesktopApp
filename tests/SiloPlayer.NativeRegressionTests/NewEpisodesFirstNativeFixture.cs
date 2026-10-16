using System.Net;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class NewEpisodesFirstNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://new-episodes.invalid"); client.SetProfile("viewer");
        var api = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "settings"));
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("viewer");
        var theme = new ThemeService(settings, api);
        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client),
            new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme));
        using var imageService = new ImageService(Path.Combine(Program.ResultDirectory, "images"));
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(api).AddSingleton(settings).AddSingleton(http).AddSingleton(imageService)
            .AddSingleton(new CatalogApi(client)).AddSingleton(new MediaMaintenanceApi(client))
            .AddSingleton(auth).AddSingleton(vm).AddSingleton(theme).AddSingleton(new ToastService())
            .AddSingleton(new CardOverlayService(api)).AddSingleton(new UICustomizationService(api)).BuildServiceProvider();
        field.SetValue(null, services);
        SettingsPage? page = null; SectionRow? row = null;
        var home = new HomeViewModel(new HomeApi(client), auth, api);
        var notifications = 0; var observer = new object();
        WeakReferenceMessenger.Default.Register<HomePreferenceChanged>(observer, (_, _) => notifications++);
        try
        {
            page = new SettingsPage { Width = 900, Height = 1000 }; parent.Children.Add(page);
            await Until(() => page.IsLoaded);
            if (page.FindName("NewEpisodesFirstToggle") is not ToggleSwitch)
                throw new InvalidOperationException("Revision18 Home settings must expose the actual Show new episodes first toggle.");
            var toggle = (ToggleSwitch)page.FindName("NewEpisodesFirstToggle");
            Invoke(page, "ShowSettingsDetail", page.FindName("HomeScreenTab"));
            wire.Revision = 17; await vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
            await Until(() => ((FrameworkElement)page.FindName("NewEpisodesFirstGroup")).Visibility == Visibility.Collapsed);
            Require(!vm.NewEpisodesFirstSupported && ((FrameworkElement)page.FindName("NewEpisodesFirstGroup")).Visibility == Visibility.Collapsed && wire.EffectiveReads == 0,
                $"revision17 hides control and never queries the new key (supported={vm.NewEpisodesFirstSupported}, visible={((FrameworkElement)page.FindName("NewEpisodesFirstGroup")).Visibility}, reads={wire.EffectiveReads}, error={vm.NewEpisodesPreferenceError})");
            wire.Revision = 18; await vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
            await Until(() => toggle.IsEnabled);
            Require(!toggle.IsOn && vm.NewEpisodesPreferenceSource == "default", "revision18 resolves false/default");
            Require(AutomationProperties.GetName(toggle) == "Show new episodes first", "actual toggle has accessible current label");
            Require(((TextBlock)page.FindName("NewEpisodesFirstDescription")).Text == "Move a show you're caught up on to the front of Continue Watching and Next Up when a new episode or season arrives.", "current reference description");

            await home.LoadCommand.ExecuteAsync(null);
            await Until(() => home.Sections.Count == 1 && home.Sections[0].Items.Count == 2);
            var mounted = home.Sections[0]; var items = mounted.Items; var first = items[0];
            row = new SectionRow { Width = 900, Section = mounted }; parent.Children.Insert(0, row);
            await Until(() => Descendants(row).OfType<LandscapeCard>().Count() == 2 && Descendants(row).OfType<Image>().Any(image => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0));
            var card = Descendants(row).OfType<LandscapeCard>().First(card => ReferenceEquals(card.MediaItem, first));
            var image = (Image)card.FindName("BackdropImage"); var artwork = image.Source;
            home.SetActive(true); var reads = wire.SectionReads; var layouts = wire.LayoutReads;
            wire.HoldWrite = true; Toggle(toggle); await Until(() => wire.WriteEntered);
            Require(vm.IsNewEpisodesPreferencePending && !toggle.IsEnabled && wire.Writes == 1, "one native toggle dispatches one write and disables while pending");
            wire.WriteRelease.TrySetResult();
            await Until(() => !vm.IsNewEpisodesPreferencePending && wire.SectionReads == reads + 1);
            await Task.Delay(500);
            Require(wire.Writes == 1 && notifications == 1 && wire.LayoutReads == layouts && wire.SectionReads == reads + 1, "accepted write publishes exactly one in-place Home refresh without layout reload");
            Require(vm.ShowNewEpisodesFirst && vm.NewEpisodesPreferenceSource == "profile", "successful save reloads effective value and winning profile source");
            Require(ReferenceEquals(home.Sections[0], mounted) && ReferenceEquals(items, mounted.Items) && ReferenceEquals(items[1], first) && items[0].ContentId == "new",
                "server reorder preserves mounted row, collection and item identity");
            Require(Descendants(row).OfType<LandscapeCard>().Any(current => ReferenceEquals(current, card)) && ReferenceEquals(image.Source, artwork) && artwork is BitmapImage decoded && decoded.PixelWidth > 0,
                "mounted card and decoded artwork survive Home reorder");
            await MediaParityNativeFixture.CaptureAsync(row, "home-new-episodes-first.png");
            home.SetActive(false); parent.Children.Remove(row); row = null;

            wire.HoldWrite = false; wire.FailWrite = true; Toggle(toggle);
            await Until(() => vm.NewEpisodesPreferenceError != null && !vm.IsNewEpisodesPreferencePending);
            Require(toggle.IsOn && toggle.IsEnabled && vm.NewEpisodesPreferenceError!.StartsWith("Failed to save"), "failed write rolls back and remains retryable");
            wire.FailWrite = false; var before = wire.Writes; Click((Button)page.FindName("NewEpisodesFirstRetry"));
            await Until(() => vm.NewEpisodesPreferenceError == null && !vm.IsNewEpisodesPreferencePending);
            Require(wire.Writes == before + 1 && !toggle.IsOn, "visible Retry repeats only failed mutation");

            wire.FailReadAfterWrite = true; before = wire.Writes; var eventsBefore = notifications; Toggle(toggle);
            await Until(() => vm.NewEpisodesPreferenceError != null && !vm.IsNewEpisodesPreferencePending);
            Require(!toggle.IsEnabled && vm.NewEpisodesPreferenceError!.StartsWith("Saved, but") && notifications == eventsBefore + 1, "accepted write/read failure is distinct and publishes once");
            wire.FailReadAfterWrite = false; wire.FailRead = false; Click((Button)page.FindName("NewEpisodesFirstRetry"));
            await Until(() => vm.NewEpisodesPreferenceError == null && toggle.IsEnabled);
            Require(wire.Writes == before + 1 && notifications == eventsBefore + 1 && toggle.IsOn, "resolution Retry issues no second mutation or refresh");

            foreach (var stage in new[] { "capabilities", "effective", "put" })
            {
                wire.Timeout = stage;
                if (stage == "put") Toggle(toggle); else await vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
                await Until(() => vm.NewEpisodesPreferenceError != null && !vm.IsNewEpisodesPreferencePending);
                Require(vm.NewEpisodesPreferenceError != null && ((Button)page.FindName("NewEpisodesFirstRetry")).Visibility == Visibility.Visible,
                    "current-owner " + stage + " timeout shows visible error and Retry");
                wire.Timeout = ""; Click((Button)page.FindName("NewEpisodesFirstRetry"));
                await Until(() => vm.NewEpisodesPreferenceError == null && toggle.IsEnabled);
            }

            // Old PUT ignores cancellation and finishes after a replacement
            // profile has begun its own held effective read.
            wire.ResetWriteHold(); wire.HoldWrite = true; Toggle(toggle); await Until(() => wire.WriteEntered);
            eventsBefore = notifications; auth.SelectProfile("replacement"); wire.HoldRead = true;
            var replacement = vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
            await Until(() => wire.ReadEntered); wire.WriteRelease.TrySetResult(); await Task.Delay(120);
            Require(vm.IsNewEpisodesPreferencePending && notifications == eventsBefore, "stale save cannot clear replacement pending state or refresh it");
            wire.ReadRelease.TrySetResult(); await replacement; wire.HoldRead = false; wire.HoldWrite = false;
            Require(!toggle.IsOn && vm.NewEpisodesPreferenceError == null, "replacement profile keeps its own effective value");

            // A scoped message waiting in the debounce window cannot load a
            // newly selected profile, even when it has the same loaded row IDs.
            auth.SelectProfile("viewer"); await home.LoadCommand.ExecuteAsync(null); await Until(() => home.Sections.FirstOrDefault()?.Items.Count == 2);
            home.SetActive(true); reads = wire.SectionReads; layouts = wire.LayoutReads;
            WeakReferenceMessenger.Default.Send(new HomePreferenceChanged(api.CaptureContext())); auth.SelectProfile("replacement");
            await Task.Delay(550);
            Require(wire.SectionReads == reads && wire.LayoutReads == layouts, "retired scoped debounce does not fetch replacement Home");
            home.SetActive(false);
            await vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
            wire.HoldRead = true; wire.ReadEntered = false; wire.ReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null); await Until(() => wire.ReadEntered);
            vm.CloseNewEpisodesPreference(); wire.ReadRelease.TrySetResult(); await canceled;
            Require(vm.NewEpisodesPreferenceError == null && !vm.IsNewEpisodesPreferencePending, "actual canceled lifecycle remains quiet");
            wire.HoldRead = false; await vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);

            foreach (var width in new[] { 400, 600, 1080, 1920, 3440, 3840 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 1080)); page.Width = width; page.Height = 1080;
                Invoke(page, "ShowSettingsDetail", page.FindName("HomeScreenTab")); await Task.Delay(120); page.UpdateLayout();
                var group = (FrameworkElement)page.FindName("HomePreferencesGroup"); var description = (TextBlock)page.FindName("NewEpisodesFirstDescription");
                Require(group.ActualWidth > 0 && description.ActualWidth > 0 && toggle.ActualWidth > 0, "real Home controls render at " + width);
                var bounds = toggle.TransformToVisual(group).TransformBounds(new Windows.Foundation.Rect(0, 0, toggle.ActualWidth, toggle.ActualHeight));
                Require(bounds.Left >= -1 && bounds.Right <= group.ActualWidth + 1, "toggle stays within actual group at " + width);
                await MediaParityNativeFixture.CaptureAsync(page, "new-episodes-first-" + width + ".png");
            }
            Program.Log("PASS: actual Home toggle capability17/18, exact copy/accessibility, profile scope, pending/rollback/Retry, accepted-write/read-only Retry, current timeouts, stale lifecycle/debounce, one in-place Home refresh/card/artwork identity and six root widths.");
        }
        finally
        {
            wire.WriteRelease.TrySetResult(); wire.ReadRelease.TrySetResult(); vm.CloseNewEpisodesPreference();
            home.SetActive(false); WeakReferenceMessenger.Default.UnregisterAll(home); WeakReferenceMessenger.Default.UnregisterAll(observer);
            if (row != null) parent.Children.Remove(row); if (page != null) parent.Children.Remove(page);
            await Task.Delay(150); field.SetValue(null, original);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(ToggleSwitch toggle) => ((IToggleProvider)new ToggleSwitchAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 250; i++) { if (ready()) return; await Task.Delay(20); } throw new InvalidOperationException("New episodes native state timed out."); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }

    private sealed class Wire : HttpMessageHandler
    {
        public int Revision = 18, Writes, EffectiveReads, SectionReads, LayoutReads;
        public bool HoldWrite, WriteEntered, HoldRead, ReadEntered, FailWrite, FailRead, FailReadAfterWrite;
        public string Timeout = "";
        public Dictionary<string, bool> Values = new();
        public TaskCompletionSource WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously), ReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ResetWriteHold() { WriteEntered = false; WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "new-episodes.invalid") throw new InvalidOperationException("Unexpected external fixture request.");
            var path = request.RequestUri.AbsolutePath;
            var profile = request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : "viewer";
            var value = Values.GetValueOrDefault(profile);
            if (path == "/art.png") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(Program.AppDirectory, "Assets", "silo-icon-1024.png"), ct)) };
            if (path.EndsWith("/contract/capabilities"))
            {
                if (Timeout == "capabilities") throw new TaskCanceledException("Synthetic current capability timeout.");
                return Json(new { api_version = 1, manifest_revision = Revision });
            }
            if (path.EndsWith("/values/effective"))
            {
                if (!request.RequestUri.Query.Contains("keys=ui.next_up_new_releases_first")) return Json(new { items = Array.Empty<object>() });
                EffectiveReads++; if (HoldRead) { ReadEntered = true; await ReadRelease.Task; }
                if (Timeout == "effective") throw new TaskCanceledException("Synthetic current effective timeout.");
                if (FailRead) return new(HttpStatusCode.BadRequest);
                return Json(new { items = new[] { new { key = "ui.next_up_new_releases_first", value, source = Values.ContainsKey(profile) ? "profile" : "default" } } });
            }
            if (request.Method == HttpMethod.Put && path.EndsWith("/ui.next_up_new_releases_first"))
            {
                Writes++; Require(request.RequestUri.Query.Contains("scope=profile&") && request.RequestUri.Query.Contains("profile_id=" + profile) && !request.RequestUri.Query.Contains("device"), "actual toggle writes captured profile only");
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var chosen = body.RootElement.GetProperty("value").GetBoolean();
                if (HoldWrite) { WriteEntered = true; await WriteRelease.Task; }
                if (Timeout == "put") throw new TaskCanceledException("Synthetic current write timeout.");
                if (FailWrite) return new(HttpStatusCode.BadRequest);
                Values[profile] = chosen; if (FailReadAfterWrite) FailRead = true;
                return Json(new { });
            }
            if (path.EndsWith("/home/layout"))
            {
                LayoutReads++; return Json(new { sections = new[] { new { id = "continue", title = "Continue Watching", section_type = "continue_watching", item_limit = 20 } } });
            }
            if (path.Contains("/home/sections/"))
            {
                SectionReads++;
                object Item(string id) => new { content_id = id, type = "episode", title = id == "new" ? "New season premiere" : "Watching now", series_title = id == "new" ? "Caught up show" : "Current show", item_source = "continue_watching", backdrop_url = "https://new-episodes.invalid/art.png" };
                return Json(new { id = "continue", title = "Continue Watching", section_type = "continue_watching", items = value ? new[] { Item("new"), Item("watching") } : new[] { Item("watching"), Item("new") } });
            }
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Unexpected fixture write: " + path);
            return Json(new { items = Array.Empty<object>() });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
