using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.WatchProviders;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class Oct24UiControlsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://oct24-ui.fixture.invalid");
        var authApi = new AuthApi(client); var settings = new SettingsApi(client); var catalog = new CatalogApi(client);
        using var auth = new AuthService(client, authApi);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "admin" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "oct24-ui-settings"));
        var theme = new ThemeService(local, settings); var accessibility = new AccessibilityService(local, theme);
        var history = new HistoryImportApi(client); var providers = new WatchProvidersApi(client);
        var vm = new SettingsViewModel(settings, catalog, authApi, history, providers, auth, theme, local, accessibility);
        var collections = new CollectionsApi(client); var collectionsVm = new CollectionsViewModel(collections, catalog, authApi, client);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authApi).AddSingleton(settings)
            .AddSingleton(catalog).AddSingleton(history).AddSingleton(providers).AddSingleton(local).AddSingleton(theme).AddSingleton(accessibility)
            .AddSingleton(vm).AddSingleton(collections).AddSingleton(collectionsVm).AddSingleton(new PeopleApi(client))
            .AddSingleton(new NavigationService()).AddSingleton(new RequestsApi(client)).AddSingleton(new RecommendationsApi(client))
            .AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(new CardOverlayService(settings)).AddSingleton(new UICustomizationService(settings))
            .AddSingleton(new ToastService()).BuildServiceProvider();
        slot.SetValue(null, new Services(services, previous));
        var attached = new List<FrameworkElement>();
        try
        {
            var only = Environment.GetEnvironmentVariable("SILO_OCT24_UI_CASE");
            if (string.IsNullOrWhiteSpace(only)) only = null;
            if (only is not (null or "connect-apps" or "home-preview")) throw new ArgumentException("Unknown Oct24 UI native case.");
            if (only == null)
            {
                await QueryEditor(parent, attached);
                await Metadata(parent, wire);
                await History(parent);
                await CollectionGrid(parent, collectionsVm, catalog, wire, attached);
            }
            if (only is null or "home-preview") await HomeResetPreview(parent, attached);
            if (only is null or "connect-apps") await ConnectApps(parent, wire, attached);
            if (wire.UnexpectedWrites != 0) throw new InvalidOperationException("UI fixture attempted an unexpected mutation.");
            Program.Log(only == null ? "PASS Oct24 actual native rules, metadata save, history presentation, complete collection grid, Home reset preview, Connect Apps/provider controls." : "PASS Oct24 native UI case: " + only);
        }
        finally
        {
            wire.Release?.TrySetResult();
            foreach (var element in attached) parent.Children.Remove(element);
            slot.SetValue(null, previous);
        }
    }

    private static async Task QueryEditor(StackPanel parent, List<FrameworkElement> attached)
    {
        foreach (var capable in new[] { false, true })
        {
            var editor = new QueryRulesEditor { Width = 780, CapabilitiesLoader = _ => Task.FromResult(new QueryEditorCapabilities(true, new HashSet<string>(), capable)) };
            editor.ConfigureCatalogScope("episode");
            var query = new QueryDefinition { MediaScope = "episode", Groups = [new() { Rules = [new() { Field = "title", Op = "contains", Value = "Pilot" },
                new() { Field = "future_rule", Op = "future_op", Value = false }] }] };
            var original = JsonSerializer.Serialize(query);
            editor.Load(query); parent.Children.Add(editor); attached.Add(editor); await Task.Delay(100); editor.UpdateLayout();
            var choices = All<ComboBox>(editor).Where(c => AutomationProperties.GetName(c) == "Rule field").ToArray();
            Check(choices.Length == 1, "An unknown saved rule was offered as an editable rule.");
            var values = choices[0].Items.OfType<ComboBoxItem>().ToArray();
            Check(values.Any(i => Equals(i.Tag, "show_title")) == capable, "Show title capability gate differs in actual episode rule field menu.");
            Check(values.Any(i => Equals(i.Tag, "title") && Equals(i.Content, "Episode title")), "Episode Title label missing.");
            Check(JsonSerializer.Serialize(query) == original, "Loading capability choices rewrote a saved unknown rule.");
            await MediaParityNativeFixture.CaptureAsync(editor, $"oct24-query-episode-{capable}.png");
            parent.Children.Remove(editor); attached.Remove(editor);
        }
    }

    private static async Task Metadata(StackPanel parent, Wire wire)
    {
        var dialog = new EditMetadataDialog(new MediaItemDetail { ContentId = "metadata", Type = "series", Title = "Original", ShowStatus = "Continuing" }) { XamlRoot = parent.XamlRoot };
        Check(!((Dictionary<string, ComboBox>)Read(dialog, "_choiceInputs")!).ContainsKey("status"), "Metadata dialog still edits the match status using lifecycle values.");
        var showing = dialog.ShowAsync();
        try
        {
            await Until(() => All<Button>(dialog).Any(b => Equals(b.Content, "Save Changes")));
            ((Dictionary<string, TextBox>)Read(dialog, "_textInputs")!)["title"].Text = "Corrected";
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)dialog.Content, "oct24-metadata-title.png");
            Click(All<Button>(dialog).First(b => Equals(b.Content, "Save Changes")));
            await Until(() => wire.MetadataPatch.HasValue);
            var patch = wire.MetadataPatch!.Value;
            Check(patch.GetProperty("title").GetString() == "Corrected" && !patch.TryGetProperty("status", out _), "Actual metadata save submitted lifecycle status.");
            Check(patch.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "locked_fields", "title" }), "Metadata save sent unchanged fields.");
        }
        finally { dialog.Hide(); await showing; }
    }

    private static async Task History(StackPanel parent)
    {
        var dialog = new PlayHistoryDialog("episode") { XamlRoot = parent.XamlRoot };
        var showing = dialog.ShowAsync();
        try
        {
            await Until(() => ((StackPanel)Read(dialog, "_rows")!).Children.Count == 2);
            var texts = All<TextBlock>((DependencyObject)dialog.Content).Select(t => t.Text).ToArray();
            Check(texts.Any(t => t.Contains("Show\nS02E04 · Pilot")), "Actual play history omitted series and episode identity.");
            Check(texts.Any(t => t.Contains("0 seconds") && !t.Contains("estimated")), "Measured zero watch time became ending position.");
            Check(texts.Any(t => t.Contains("60 seconds (estimated)")), "Legacy resumed history did not cap ending position to session duration.");
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)dialog.Content, "oct24-history.png");
            var scroll = (ScrollViewer)dialog.Content;
            foreach (var info in All<TextBlock>(scroll).Where(t => t.Text.Contains("seconds")))
            {
                var point = info.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());
                Check(point.X >= 0 && point.X + info.ActualWidth <= scroll.ActualWidth + 1,
                    $"Watch-time column is clipped by the actual history dialog: x={point.X}, width={info.ActualWidth}, viewport={scroll.ActualWidth}.");
            }
        }
        finally { dialog.Hide(); await showing; }
    }

    private static async Task CollectionGrid(StackPanel parent, CollectionsViewModel vm, CatalogApi catalog, Wire wire, List<FrameworkElement> attached)
    {
        var page = new CollectionsPage { Width = 1200, Height = 900 }; parent.Children.Add(page); attached.Add(page);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        await Until(() => Cards(page) == 35);
        Click(All<Button>(page).First(b => AutomationProperties.GetName(b) == "Load more server collections"));
        await Until(() => Cards(page) == 40);
        Click(All<Button>(page).First(b => AutomationProperties.GetName(b) == "Load more server collections"));
        await Until(() => Cards(page) == 45);
        wire.ExtraCollections = 1; catalog.InvalidateLibraryCache();
        await Until(() => Cards(page) == 47);
        await MediaParityNativeFixture.CaptureAsync((FrameworkElement)page.FindName("ServerCollectionsSection"), "oct24-collections-full-grid.png");
        parent.Children.Remove(page); attached.Remove(page);
        await Task.Delay(60); var reads = wire.AggregateReads;
        wire.ExtraCollections = 2; catalog.InvalidateLibraryCache(); await Task.Delay(100);
        Check(wire.AggregateReads == reads, "An unloaded collection page continued refreshing after library invalidation.");
        static int Cards(DependencyObject page) => All<Button>(page).Count(b => AutomationProperties.GetName(b).StartsWith("Open collection ", StringComparison.Ordinal));
    }

    private static async Task ConnectApps(StackPanel parent, Wire wire, List<FrameworkElement> attached)
    {
        var page = new SettingsPage();
        var host = (StackPanel)page.FindName("ConnectAppsContentHost");
        ((StackPanel)page.FindName("ConnectAppsPanel")).Children.Remove(host); parent.Children.Add(host); attached.Add(host); host.Width = 850;
        foreach (var mode in new[] { "oauth", "credentials", "network", "disabled", "recovery", "password", "oauth-no-address" })
        {
            wire.SignInMode = mode;
            await (Task)Call(page, "LoadConnectAppsAsync")!;
            Click(All<Button>(host).Single(b => Equals(b.Content, "Silo app or website")));
            var texts = string.Join("\n", All<TextBlock>(host).Select(t => t.Text));
            Check(mode is "password" or "credentials" ? texts.Contains("your password") : !texts.Contains("your password"), "Connect Apps advertised an unavailable password field.");
            if (mode == "credentials") Check(texts.Contains("directory-user"), "Directory sign-in lost its actual linked username.");
            if (mode == "oauth") Check(texts.Contains("/activate") && texts.Contains("Choose Fixture provider"), "OAuth instructions omitted provider button/TV approval.");
            if (mode == "oauth-no-address") Check(texts.Contains("public address"), "Missing OAuth native address was not explained.");
            if (mode == "network") Check(texts.Contains("Continue as"), "Network login instructions missing.");
            if (mode == "recovery") Check(texts.Contains("/login?local=1"), "Recovery login route missing when password form is disabled.");
            await MediaParityNativeFixture.CaptureAsync(host, "oct24-connect-apps-" + mode + ".png");
        }
        var provider = new WatchProviderCardViewModel(new() { Key = "fixture", DisplayName = "Fixture provider" })
            { Connection = new() { Connected = true, Capabilities = new() { ExportUnwatched = true }, ExportUnwatchedEnabled = true }, SettingsExpanded = true };
        var card = (Border)Call(page, "BuildWatchProviderCard", provider)!;
        Check(!All<ToggleSwitch>(card).Any(toggle => Equals(toggle.Header, "Send unwatched changes")), "Obsolete destructive provider history control remains visible.");
        parent.Children.Add(card); attached.Add(card); await MediaParityNativeFixture.CaptureAsync(card, "oct24-provider-history-controls.png");
        // A late discovery reply cannot repaint this detached owner for another profile.
        wire.HoldProviders = true; wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = (Task)Call(page, "LoadConnectAppsAsync")!;
        await Until(() => wire.ProviderStarted);
        AppClient().SetProfile("retired"); wire.Release.SetResult(); await pending;
        Check(host.Children.Count == 0, "Late Connect Apps result displayed credentials from a retired profile.");
        static SiloApiClient AppClient() => SiloPlayer.App.Services.GetRequiredService<SiloApiClient>();
    }

    private static async Task HomeResetPreview(StackPanel parent, List<FrameworkElement> attached)
    {
        var page = new SettingsPage { Width = 1000, Height = 800 }; parent.Children.Add(page); attached.Add(page); await Task.Delay(60);
        Call(page, "HomeImport_Click", new Button(), new RoutedEventArgs());
        await Until(() => Read(page, "_layoutTransferPreview") is ContentDialog);
        var input = (ContentDialog)Read(page, "_layoutTransferPreview")!;
        try
        {
            await Until(() => All<Button>(input).Any(b => b.Name == "PrimaryButton"));
            All<TextBox>(input).Single(t => t.AcceptsReturn).Text = HomeLayoutTransfer.Serialize(new HomeLayoutFile { ServerId = "same", Pages = [new()] });
            Click(All<Button>(input).Single(b => b.Name == "PrimaryButton"));
            await Until(() => Read(page, "_layoutTransferPreview") is ContentDialog next && !ReferenceEquals(next, input));
            var preview = (ContentDialog)Read(page, "_layoutTransferPreview")!;
            await Until(() => All<Button>(preview).Any(b => b.Name == "CloseButton"));
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)preview.Content, "oct24-home-reset-preview.png");
            Check(All<TextBlock>(preview).Any(t => t.Text.Contains("Home: restore server rows (clear saved overrides)")), "Empty same-server Home preview does not explain that it restores server rows.");
            Check(preview.IsPrimaryButtonEnabled, "An explicit same-server default reset is not applicable.");
            Click(All<Button>(preview).Single(b => b.Name == "CloseButton"));
            await Until(() => !(bool)Read(page, "_layoutTransferBusy")!);
        }
        finally
        {
            if (Read(page, "_layoutTransferPreview") is ContentDialog dialog) dialog.Hide();
            parent.Children.Remove(page); attached.Remove(page); await Task.Delay(60);
        }
    }

    private static object? Read(object owner, string field) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static object? Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> predicate) { for (var i = 0; i < 150; i++) { if (predicate()) return; await Task.Delay(20); } throw new TimeoutException("Oct24 native control action did not complete."); }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class Services(IServiceProvider primary, IServiceProvider fallback) : IServiceProvider
    { public object? GetService(Type type) => primary.GetService(type) ?? fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        public JsonElement? MetadataPatch; public int UnexpectedWrites; public string SignInMode = "oauth";
        public bool HoldProviders, ProviderStarted; public TaskCompletionSource? Release;
        public int ExtraCollections, AggregateReads;
        private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        private static object[] Entries(string prefix, int count) => Enumerable.Range(0, count).Select(i => (object)new { id = i < 5 ? "shared-" + i : prefix + i, title = "collection " + (i < 5 ? "shared-" + i : prefix + i), item_count = i }).ToArray();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Patch && path.EndsWith("/metadata")) { MetadataPatch = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone(); return Ok(new { }); }
            if (request.Method != HttpMethod.Get) { UnexpectedWrites++; return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"message\":\"Unexpected fixture mutation\"}") }; }
            if (path == "/api/v2/auth/providers")
            {
                if (HoldProviders) { ProviderStarted = true; await Release!.Task; }
                return Ok(new { password_login = SignInMode != "recovery", items = new[] { new { id = "provider", mode = SignInMode.StartsWith("oauth") ? "oauth" : SignInMode, display_name = "Fixture provider", native_start_path = SignInMode == "oauth-no-address" ? "" : "/oauth/start", installation_id = "5" } } });
            }
            if (path == "/api/v2/account/identities") return Ok(new { items = new[] { new { provider_id = SignInMode is "disabled" or "recovery" ? "" : "provider", provider_name = "Fixture provider", username = "directory-user" } } });
            if (path == "/api/v2/compat/connect-info") return Ok(new { account = new { password_login_available = SignInMode is "password" or "recovery" }, jellyfin = new { enabled = true, public_url = "https://compat.fixture.invalid" } });
            if (path == "/api/v2/profiles") return Ok(new { items = new[] { new { id = "fixture", name = "Primary", is_primary = true } } });
            if (path == "/api/v2/system/identity") return Ok(new { server_id = "same" });
            if (path == "/api/v2/collections/server") { AggregateReads++; return Ok(new { libraries = new[] { new { library_id = 1, library_name = "One", total_count = 25 + ExtraCollections, collections = Entries("one-", 20) }, new { library_id = 2, library_name = "Two", total_count = 25 + ExtraCollections, collections = Entries("two-", 20) } } }); }
            if (path.StartsWith("/api/v2/library/")) return Ok(new { collections = Entries(path.Contains("/1/") ? "one-" : "two-", 25 + ExtraCollections) });
            if (path == "/api/v2/admin/playback-history") return Ok(new { items = new[] {
                new { media_title = "Pilot", series_title = "Show", season_number = 2, episode_number = 4, played_seconds = (int?)0, watched_seconds = 2700, started_at = "2026-10-24T10:00:00Z", ended_at = "2026-10-24T10:01:00Z" },
                new { media_title = "Pilot", series_title = "Show", season_number = 2, episode_number = 4, played_seconds = (int?)null, watched_seconds = 2700, started_at = "2026-10-24T10:00:00Z", ended_at = "2026-10-24T10:01:00Z" } }, page = new { has_more = false } });
            return Ok(new { items = Array.Empty<object>(), groups = Array.Empty<object>(), categories = Array.Empty<object>(), settings = Array.Empty<object>(), import_sources = Array.Empty<object>(), page = new { has_more = false } });
        }
    }
}
