using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;
using R = SiloPlayer.Core.Services.Admin.AdminLibrarySettingsRules;

/// <summary>Production Manage > Libraries > Settings registration and page flow on synthetic data.</summary>
internal static class AdminLibrarySettingsNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        for (var index = 0; index < milliseconds / 25 && !condition(); index++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
        Program.Log("STEP: " + label);
    }

    /// <summary>Records a check whose rendering follows the controller state by a queued dispatcher turn, allowing that turn to run.</summary>
    private static async Task Eventually(Func<bool> condition, string label, int milliseconds = 3000)
    {
        for (var index = 0; index < milliseconds / 25 && !condition(); index++) await Task.Delay(25);
        Check(condition(), label);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = new()
        {
            [R.ScopeVersionsToLibrary] = "false", [R.FallbackLanguage] = "en", [R.RealtimeMonitoring] = "false",
            [R.ScannerWorkers] = "4", [R.MatcherWorkers] = "8", [R.ImageWorkers] = "0",
            [R.PersonRefreshPerMinute] = "120", [R.MatcherBatchSize] = "500", [R.UnratedContent] = "hide",
            [R.ExtraRatingSources] = "rt_critic,letterboxd", [R.SubtitleAutoSync] = "true",
            [R.SubtitleSyncExecution] = "transcode_nodes_only", [R.SubtitleSyncNodeCapacity] = "2",
            ["branding.server_name"] = "Synthetic Silo",
        };
        internal readonly List<string> Requests = [];
        internal int Saves, Version = 1;
        internal bool FailRatingSources, RejectPut, UnknownOutcome, Conflict, EffectiveFailure, RatingsUnsupported, OnlyAlwaysShownSources, NodeAvailable;
        internal string[] Managed = [];
        internal TaskCompletionSource? EffectiveGate;
        internal string? LastPut, LastIfMatch;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add(request.Method + " " + path);
            if (path == "/api/v2/admin/settings/effective")
            {
                if (EffectiveGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                if (EffectiveFailure) return Json(new { type = "service_unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
            }
            if (path == "/api/v2/admin/rating-sources" && FailRatingSources)
                return Json(new { type = "service_unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
            object body;
            if (request.Method == HttpMethod.Put)
            {
                Saves++; LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                LastPut = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (UnknownOutcome) throw new HttpRequestException("synthetic transport failure");
                if (Conflict) return Json(new { type = "precondition_failed", title = "Settings changed", status = 412 }, HttpStatusCode.PreconditionFailed);
                if (RejectPut) return Json(new { type = "validation_failed", title = "Rejected", status = 422 }, HttpStatusCode.UnprocessableEntity);
                using var json = JsonDocument.Parse(LastPut);
                var saved = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in json.RootElement.GetProperty("values").EnumerateObject())
                    saved[property.Name] = Values[property.Name] = property.Value.GetString()!;
                Version++;
                body = new { values = saved, restart_required = false };
            }
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = Managed },
                "/api/v2/admin/settings/restart-keys" => new { keys = new[] { R.UnratedContent }, prefixes = Array.Empty<string>() },
                "/api/v2/admin/rating-sources/capabilities" => new { revision = "r1", state = "available", allowed = true, plugin_declared_sources = !RatingsUnsupported },
                "/api/v2/admin/rating-sources" => new
                {
                    items = OnlyAlwaysShownSources
                        ? new object[]
                        {
                            new { source = "imdb", label = "IMDb", name = "IMDb", always_shown = true },
                            new { source = "tmdb", label = "TMDB", name = "TMDB", always_shown = true },
                        }
                        : new object[]
                        {
                            new { source = "imdb", label = "IMDb", name = "IMDb", always_shown = true },
                            new { source = "tmdb", label = "TMDB", name = "TMDB", always_shown = true },
                            new { source = "rt_critic", label = "Rotten Tomatoes critics", name = "RT", always_shown = false, provider = "MDBList" },
                        }
                },
                "/api/v2/capabilities/ratings" => new
                {
                    state = "available",
                    sources = new[] { new { source = "imdb", name = "IMDb" }, new { source = "rt_critic", name = "RT" } },
                },
                "/api/v2/admin/nodes" => new
                {
                    items = NodeAvailable ? new object[] { new { id = "node-1", type = "transcode", enabled = true, healthy = true } } : Array.Empty<object>(),
                    page = new { has_more = false },
                },
                _ => new { },
            };
            var response = Json(body);
            response.Headers.ETag = new($"\"library-settings-v{Version}\"");
            return response;
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    internal static async Task RunAsync(Window window)
    {
        Failures.Clear();
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var catalog = new CatalogApi(client);
        await catalog.GetShownRatingSourcesAsync();
        Check(catalog.CachedShownRatingSources.SetEquals(["imdb", "rt_critic"]), "fixture begins with a populated rating capability cache");
        var navigation = new NavigationService();
        var preferenceDirectory = Path.Combine(Program.ResultDirectory, "library-settings-ui");
        if (Directory.Exists(preferenceDirectory)) Directory.Delete(preferenceDirectory, true);
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(catalog)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService())
            .AddSingleton(new SettingsService(Program.ResultDirectory)).AddSingleton(new LoggingDisclosurePreferences(preferenceDirectory));
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = serviceField.GetValue(null);
        var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null;
        try
        {
            serviceField.SetValue(null, services);
            window.AppWindow.ResizeClient(new(1440, 900));
            outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/settings/library");
            Check(resolved is { Kind: AdminRouteResolutionKind.Route }, "Library settings resolve through the production registration");
            await Until(() => outer.Content is AdminShellPage, "Admin shell");
            var shell = (AdminShellPage)outer.Content!;
            var inner = (Frame)Field(shell, "_frame")!;
            await Until(() => inner.Content is AdminLibrarySettingsPage, "Library settings page");
            var page = (AdminLibrarySettingsPage)inner.Content!;
            await Until(() => page.Controller?.State is { Loaded: true, RatingSourcesLoaded: true, NodesKnown: true }, "Library settings data");
            var controller = page.Controller!;
            await Until(() => Input(page, R.ScannerWorkers).Text == "4", "Library settings rendered");

            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Library settings navigation has no slide transition");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Library & Metadata"), "official page title is rendered");
            foreach (var heading in new[] { "General", "Scanning", "Maturity limits", "Ratings", "Subtitle sync" })
                Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == heading), heading + " group is rendered");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text ==
                "For profiles with a maximum content rating, which is set on each profile. Changes apply after a restart."),
                "a restart-all group retains its official description");
            Check(!Descendants(outer).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.", StringComparison.Ordinal) || text.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "every visible page and shell label resolves through the native catalog");

            Check(Toggle(page, R.ScopeVersionsToLibrary).IsOn, "the all-versions switch is the inverse of the stored false scope key");
            Check(Toggle(page, R.RealtimeMonitoring).Visibility == Visibility.Visible && !Toggle(page, R.RealtimeMonitoring).IsOn,
                "the server-wide realtime switch is shown while its saved value is off");
            Check(Select(page, R.FallbackLanguage).SelectedValue?.ToString() == "en", "fallback metadata language loads the saved language");
            Check(Select(page, R.UnratedContent).SelectedValue?.ToString() == "hide", "unrated maturity policy loads the saved choice");
            Check(Select(page, R.SubtitleSyncExecution).SelectedValue?.ToString() == "transcode_nodes_only", "subtitle execution loads its saved node-only mode");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == "No transcode nodes are connected"),
                "saved node-only subtitle sync warns when the known node list has none");
            await Eventually(() => IsEffectivelyVisible(Input(page, R.SubtitleSyncNodeCapacity)) && Input(page, R.SubtitleSyncNodeCapacity).Text == "2",
                "node capacity stays visible for a node-backed sync mode");
            var executionItems = Select(page, R.SubtitleSyncExecution).Items.OfType<ComboBoxItem>().ToDictionary(item => item.Tag!.ToString()!);
            Check(executionItems["transcode_nodes_only"].IsEnabled && !executionItems["prefer_transcode_nodes"].IsEnabled,
                "known node absence keeps the saved node-only mode reachable and disables the other node-backed mode");

            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == "From MDBList"), "rating sources are grouped by their declaring plugin");
            Check(ToggleById(page, "LibrarySettingsRating_rt_critic").IsOn, "a declared selected rating is on");
            Check(ToggleById(page, "LibrarySettingsRating_letterboxd").IsOn &&
                  Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Not added by an enabled plugin"),
                "an undeclared saved rating remains visible and can be turned off");
            Check(!Descendants(page).OfType<ToggleSwitch>().Any(toggle => AutomationProperties.GetName(toggle) is "IMDb" or "TMDB"),
                "always-shown IMDb and TMDB have no switches");
            server.FailRatingSources = true;
            await controller.ReloadAsync(false);
            await Until(() => controller.State.RatingSourcesError && Descendants(page).OfType<TextBlock>()
                .Any(text => text.Text == "Couldn't load the rating sources." && IsEffectivelyVisible(text)), "rating source warning");
            Check(IsEffectivelyVisible(Descendants(page).OfType<TextBlock>().Single(text => text.Text == "Couldn't load the rating sources.")),
                "a rating-source failure keeps Ratings visible with its official warning");
            server.FailRatingSources = false;
            await controller.ReloadAsync(false);
            await Until(() => controller.State.RatingSourcesLoaded && ToggleById(page, "LibrarySettingsRating_rt_critic").IsOn,
                "rating source recovery");

            var scanningAdvanced = Button(page, "LibrarySettingsScanningAdvanced");
            Check(AutomationProperties.GetName(scanningAdvanced) == "Advanced · 2 settings", "Scanning Advanced names its exact count");
            Invoke(scanningAdvanced); await Until(() => IsEffectivelyVisible(Input(page, R.PersonRefreshPerMinute)), "Scanning Advanced opened");
            Check(Input(page, R.PersonRefreshPerMinute).Text == "120" && Input(page, R.MatcherBatchSize).Text == "500",
                "Scanning Advanced exposes person rate and matcher batch");
            var subtitleAdvanced = Button(page, "LibrarySettingsSubtitleAdvanced");
            Check(AutomationProperties.GetName(subtitleAdvanced) == "Advanced · 2 settings", "Subtitle Advanced counts execution and visible capacity");
            var restoreDefaults = Button(page, "LibrarySettingsRestoreDefaults");
            Check(IsEffectivelyVisible(restoreDefaults) && Descendants(restoreDefaults).OfType<Viewbox>().Any(),
                "Restore defaults is shown for an override with the official rotate icon");

            Input(page, R.ImageWorkers).Text = "257";
            await Until(() => controller.State.Errors.ContainsKey(R.ImageWorkers), "invalid image worker count");
            await Eventually(() => !Button(page, "LibrarySettingsSave").IsEnabled && Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Enter a whole number from 0 to 256." && IsEffectivelyVisible(text)),
                "invalid numeric text is explained inline and blocks Save");
            Input(page, R.ImageWorkers).Text = "0";
            await Until(() => !controller.State.Errors.ContainsKey(R.ImageWorkers), "image worker repaired");
            Invoke(Button(page, "LibrarySettingsRestoreDefaults"));
            await Until(() => controller.State.Values[R.ScannerWorkers] == "8", "worker defaults staged");
            ToggleById(page, "LibrarySettingsRating_rt_critic").IsOn = false;
            await Until(() => !R.ParseRatingSources(controller.State.Values[R.ExtraRatingSources]).Contains("rt_critic"), "rating toggle staged");
            await Click(page, "LibrarySettingsSave");
            await Until(() => server.Saves == 1 && controller.State.DirtyKeys.Count == 0, "Library settings saved");
            await Until(() => catalog.CachedShownRatingSources.Count == 0, "rating capability cache invalidated");
            Check(server.LastIfMatch == "\"library-settings-v1\"", "Save carries the exact strong ETag");
            using (var put = JsonDocument.Parse(server.LastPut!))
            {
                var values = put.RootElement.GetProperty("values").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString());
                Check(values.Keys.Order().SequenceEqual(new[] { R.ExtraRatingSources, R.ScannerWorkers }.Order()),
                    "one write contains only the worker default and rating edits");
            }

            foreach (var (width, height) in new[] { (1440, 900), (800, 700) })
            {
                window.AppWindow.ResizeClient(new(width, height)); await Task.Delay(200); outer.UpdateLayout();
                var scroller = (ScrollViewer)Field(page, "_scroller")!;
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, $"Library settings have no horizontal overflow at {width}x{height}");
                foreach (var control in Descendants(page).OfType<Control>().Where(control => AutomationProperties.GetAutomationId(control).StartsWith("LibrarySettings_", StringComparison.Ordinal) && control.IsLoaded))
                    Check(AutomationProperties.GetName(control).Length > 0, AutomationProperties.GetAutomationId(control) + " has an accessible name");
                await CaptureAsync(outer, $"library-settings-{width}x{height}.png");
            }

            // A definite server rejection keeps the draft, explains itself, and is sent once; the user may then retry.
            bool Shown(string text) => Descendants(page).OfType<TextBlock>().Any(block => block.Text == text && IsEffectivelyVisible(block));
            Input(page, R.MatcherWorkers).Text = "12";
            await Until(() => controller.State.DirtyKeys.Contains(R.MatcherWorkers), "rejected draft");
            server.RejectPut = true; var beforeRejected = server.Saves;
            await Click(page, "LibrarySettingsSave");
            await Until(() => controller.State.Error != null && !controller.State.Saving, "rejected save settled");
            await Task.Delay(300);
            Check(server.Saves == beforeRejected + 1 && !controller.State.Conflict && !controller.State.OutcomeUnknown &&
                  controller.State.DirtyKeys.Contains(R.MatcherWorkers) && Input(page, R.MatcherWorkers).Text == "12" &&
                  Shown("The server rejected one or more settings. Review the values and try again.") && Button(page, "LibrarySettingsSave").IsEnabled,
                "a definite server rejection is sent once, explained, retains the draft and allows a retry");
            await CaptureAsync(outer, "library-settings-rejected.png");
            server.RejectPut = false; await Click(page, "LibrarySettingsSave");
            await Until(() => server.Saves == beforeRejected + 2 && controller.State.DirtyKeys.Count == 0 && controller.State.Error == null, "retry after rejection");
            Check(server.Values[R.MatcherWorkers] == "12", "the retry saved the retained draft");

            // An unknown outcome is never replayed: Save is blocked until an explicit reload that keeps the draft.
            Input(page, R.ScannerWorkers).Text = "9";
            await Until(() => controller.State.DirtyKeys.Contains(R.ScannerWorkers), "unknown outcome draft");
            server.UnknownOutcome = true; var beforeUnknown = server.Saves;
            await Click(page, "LibrarySettingsSave");
            await Until(() => controller.State.OutcomeUnknown, "unknown outcome shown"); await Task.Delay(300);
            Check(server.Saves == beforeUnknown + 1 && !Button(page, "LibrarySettingsSave").IsEnabled && Input(page, R.ScannerWorkers).Text == "9" &&
                  Descendants(page).OfType<TextBlock>().Any(block => block.Text.StartsWith("The save outcome is unknown", StringComparison.Ordinal) && IsEffectivelyVisible(block)),
                "an unknown save outcome keeps the draft, blocks Save and is not replayed");
            await CaptureAsync(outer, "library-settings-unknown-outcome.png");
            server.UnknownOutcome = false; await Click(page, "LibrarySettingsReload");
            await Until(() => !controller.State.OutcomeUnknown && !controller.State.Loading && controller.State.Loaded, "reload after unknown outcome");
            Check(controller.State.DirtyKeys.Contains(R.ScannerWorkers) && Button(page, "LibrarySettingsSave").IsEnabled && server.Saves == beforeUnknown + 1,
                "an explicit reload keeps the draft, re-enables Save and still has not replayed the write");

            // A stale validator disables Save until the explicit reload; Discard then restores the saved value.
            server.Conflict = true; await Click(page, "LibrarySettingsSave");
            await Until(() => controller.State.Conflict, "conflict shown"); await Task.Delay(200);
            Check(!Button(page, "LibrarySettingsSave").IsEnabled && Input(page, R.ScannerWorkers).Text == "9" &&
                  Shown("Settings changed on the server. Reload and review your changes."), "a stale version disables Save, retains the draft and offers Reload");
            await CaptureAsync(outer, "library-settings-conflict.png");
            server.Conflict = false; await Click(page, "LibrarySettingsReload");
            await Until(() => !controller.State.Conflict && !controller.State.Loading, "reload after conflict");
            Check(controller.State.DirtyKeys.Contains(R.ScannerWorkers) && Button(page, "LibrarySettingsSave").IsEnabled, "reload after a conflict preserves the draft");
            await Click(page, "LibrarySettingsDiscard");
            await Until(() => controller.State.DirtyKeys.Count == 0 && Input(page, R.ScannerWorkers).Text == "8", "discard restores the saved value");
            Check(!IsEffectivelyVisible(Button(page, "LibrarySettingsSave")) || !Button(page, "LibrarySettingsSave").IsEnabled, "Discard leaves nothing to save");

            // Environment-managed keys are read-only with the reason shown; the restore action stays available for the others.
            server.Managed = [R.ScannerWorkers]; await controller.ReloadAsync(true);
            await Until(() => controller.State.ManagedByEnv.Contains(R.ScannerWorkers) && !Input(page, R.ScannerWorkers).IsEnabled, "managed worker locked");
            // The official button has no managed-key condition; the controller leaves managed keys untouched.
            Check(Input(page, R.MatcherWorkers).IsEnabled && Button(page, "LibrarySettingsRestoreDefaults").IsEnabled,
                "a managed worker count is read-only, other fields stay editable and Restore defaults stays available for them");
            Check(Descendants(page).OfType<TextBlock>().Any(block => block.Text.Contains("Managed by a server environment variable.", StringComparison.Ordinal) && IsEffectivelyVisible(block)),
                "the managed row says why it is disabled");
            server.Managed = []; await controller.ReloadAsync(false);
            await Until(() => controller.State.ManagedByEnv.Count == 0 && Input(page, R.ScannerWorkers).IsEnabled, "managed lock released");

            // Server capability and node evidence: an older server hides Ratings, an empty list says so, a node removes the warning.
            server.RatingsUnsupported = true; await controller.ReloadAsync(false);
            await Until(() => !controller.State.RatingSourcesSupported && !controller.State.RatingSourcesLoading && !controller.State.Loading, "ratings unsupported");
            Check(!Shown("Ratings") && !Descendants(page).OfType<ToggleSwitch>().Any(toggle => AutomationProperties.GetAutomationId(toggle).StartsWith("LibrarySettingsRating_", StringComparison.Ordinal) && IsEffectivelyVisible(toggle)),
                "a server without plugin rating sources hides the Ratings group");
            server.RatingsUnsupported = false; server.OnlyAlwaysShownSources = true;
            server.Values[R.ExtraRatingSources] = ""; await controller.ReloadAsync(false);
            await Until(() => controller.State.RatingSourcesLoaded && !controller.State.Loading, "empty ratings");
            await Until(() => Shown("No metadata plugin adds other ratings."), "empty ratings message");
            Check(Shown("Ratings"), "an empty rating list keeps the Ratings group with its official empty message");
            await CaptureAsync(outer, "library-settings-ratings-empty.png");
            server.OnlyAlwaysShownSources = false; server.NodeAvailable = true; server.Values[R.ExtraRatingSources] = "rt_critic";
            await controller.ReloadAsync(false);
            await Until(() => controller.State.NodesKnown && controller.State.TranscodeNodeAvailable && !controller.State.Loading && ToggleById(page, "LibrarySettingsRating_rt_critic").IsOn, "transcode node known");
            var nodeItems = Select(page, R.SubtitleSyncExecution).Items.OfType<ComboBoxItem>().ToDictionary(item => item.Tag!.ToString()!);
            Check(!Shown("No transcode nodes are connected") && nodeItems.Values.All(item => item.IsEnabled), "a usable transcode node removes the warning and enables every execution mode");
            Input(page, R.SubtitleSyncNodeCapacity).Text = "3";
            Select(page, R.SubtitleSyncExecution).SelectedValue = "local";
            await Until(() => controller.State.DirtyKeys.Contains(R.SubtitleSyncExecution) && controller.State.DirtyKeys.Contains(R.SubtitleSyncNodeCapacity), "local sync with staged capacity");
            await Eventually(() => IsEffectivelyVisible(Input(page, R.SubtitleSyncNodeCapacity)) && Input(page, R.SubtitleSyncNodeCapacity).Text == "3",
                "an unsaved capacity stays visible after placement moves off the nodes");
            controller.Discard(); await Until(() => controller.State.DirtyKeys.Count == 0, "capacity draft discarded");
            await Until(() => Select(page, R.SubtitleSyncExecution).SelectedValue?.ToString() == "transcode_nodes_only", "execution restored");
            server.NodeAvailable = false; await controller.ReloadAsync(false);
            await Until(() => controller.State.NodesKnown && !controller.State.TranscodeNodeAvailable && !controller.State.Loading, "node absence known again");

            Input(page, R.MatcherWorkers).Text = "14";
            await Until(() => controller.State.DirtyKeys.Contains(R.MatcherWorkers), "departure draft");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "discard dialog");
            var dialog = (ContentDialog)Field(page, "_dialog")!;
            Invoke(Descendants(dialog).OfType<Button>().First(button => AutomationProperties.GetName(button) == "Discard"));
            await Until(() => inner.Content is AdminActivityPage, "discard navigation");
            Check(controller.State.Values.Count == 0 && Input(page, R.MatcherWorkers).Text.Length == 0,
                "departure retires the controller and clears native drafts");
            // Back with a held first read shows the loading skeleton and no editable controls; the read then fails with Retry.
            server.EffectiveGate = new(TaskCreationOptions.RunContinuationsAsynchronously); server.EffectiveFailure = true;
            inner.GoBack(); await Until(() => inner.Content is AdminLibrarySettingsPage { Controller.State.Loading: true }, "Back held read");
            page = (AdminLibrarySettingsPage)inner.Content; controller = page.Controller!;
            await Task.Delay(150);
            var loadingPanel = (FrameworkElement)Field(page, "_loading")!;
            Check(IsEffectivelyVisible(loadingPanel) && AutomationProperties.GetName(loadingPanel) == "Loading settings" &&
                  !Descendants(page).OfType<Control>().Where(control => AutomationProperties.GetAutomationId(control).StartsWith("LibrarySettings_", StringComparison.Ordinal)).Any(IsEffectivelyVisible),
                "the first read shows the loading skeleton and no editable controls");
            await CaptureAsync(outer, "library-settings-loading.png");
            server.EffectiveGate.SetResult(); server.EffectiveGate = null;
            await Until(() => !controller.State.Loading && controller.State.Error != null, "failed first read"); await Task.Delay(150);
            Check(!controller.State.Loaded && Shown("Library and metadata settings could not be loaded. Retry before editing.") &&
                  IsEffectivelyVisible(Button(page, "LibrarySettingsRetry")) && Button(page, "LibrarySettingsRetry").IsEnabled &&
                  !Descendants(page).OfType<Control>().Where(control => AutomationProperties.GetAutomationId(control).StartsWith("LibrarySettings_", StringComparison.Ordinal)).Any(IsEffectivelyVisible),
                "a failed first read shows the official sentence and Retry with no editable defaults");
            await CaptureAsync(outer, "library-settings-load-error.png");
            controller.Set(R.ScannerWorkers, "2"); await controller.SaveAsync();
            Check(controller.State.DirtyKeys.Count == 0, "a failed read stages and saves nothing");
            server.EffectiveFailure = false; await Click(page, "LibrarySettingsRetry");
            await Until(() => controller.State.Loaded && controller.State.Error == null && !controller.State.Loading, "retry recovers");
            await Until(() => Input(page, R.ScannerWorkers).Text.Length > 0, "Back fields rendered");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "former-admin", Role = "user" }); await Task.Delay(200);
            Check(controller.State.Values.Count == 0 && Input(page, R.ScannerWorkers).Text.Length == 0 && !Button(page, "LibrarySettingsSave").IsEnabled,
                "authority retirement clears values, controls and save authority");
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent; await Task.Delay(100); serviceField.SetValue(null, previousServices);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Library settings fixture failures: " + string.Join("; ", Failures));
    }

    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static TextBox Input(DependencyObject page, string key) => Descendants(page).OfType<TextBox>().Single(control => AutomationProperties.GetAutomationId(control) == "LibrarySettings_" + key);
    private static ComboBox Select(DependencyObject page, string key) => Descendants(page).OfType<ComboBox>().Single(control => AutomationProperties.GetAutomationId(control) == "LibrarySettings_" + key);
    private static ToggleSwitch Toggle(DependencyObject page, string key) => ToggleById(page, "LibrarySettings_" + key);
    private static ToggleSwitch ToggleById(DependencyObject page, string id) => Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static bool IsEffectivelyVisible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return element.IsLoaded && element.ActualWidth > 0 && element.ActualHeight > 0;
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Click(DependencyObject page, string id)
    {
        var button = Button(page, id); await Until(() => button.IsEnabled && button.IsLoaded, id + " enabled"); Invoke(button);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
