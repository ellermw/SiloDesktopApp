using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>Production Configure > Search registration and Frame entry using synthetic server data only.</summary>
internal static class AdminSearchSettingsNativeFixture
{
    private const string Provider = "catalog.search.provider";
    private const string Url = "catalog.search.meilisearch.url";
    private const string ApiKey = "catalog.search.meilisearch.api_key";
    private const string Semantic = "catalog.search.meilisearch.semantic_enabled";
    private const string Timeout = "catalog.search.meilisearch.timeout_ms";
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000, Func<string>? diagnostics = null)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label + (diagnostics == null ? "" : "; " + diagnostics()));
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = new()
        {
            ["branding.server_name"] = "Synthetic Silo",
            [Provider] = "meilisearch", [Url] = "http://search.example.test:7700", ["catalog.search.meilisearch.matching_strategy"] = "",
            [Semantic] = "false", ["catalog.search.meilisearch.index"] = "", [Timeout] = "", ["catalog.search.meilisearch.sync_batch_size"] = "",
            ["catalog.search.meilisearch.semantic_ratio"] = "",
        };
        internal int Saves, StatusReads, Checks, Rebuilds, Version = 1;
        internal bool KeyConfigured = true, ManagedUrl = true, ManagedApiKey, RebuildRequired, Degraded, StatusFailure;
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal string? LastIfMatch, LastPut, LastCheck;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (request.Method == HttpMethod.Put)
            {
                Saves++; LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                LastPut = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var data = JsonDocument.Parse(LastPut);
                var updated = new Dictionary<string, string>();
                foreach (var entry in data.RootElement.GetProperty("values").EnumerateObject())
                {
                    if (entry.Name == ApiKey) KeyConfigured = entry.Value.GetString()!.Length > 0;
                    else updated[entry.Name] = Values[entry.Name] = entry.Value.GetString()!;
                }
                Version++;
                body = new { values = updated, restart_required = false };
            }
            else if (request.Method == HttpMethod.Post && path == "/api/v2/admin/settings/check/meilisearch")
            {
                Checks++; LastCheck = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(new { success = true, message = "Meilisearch is reachable." });
            }
            else if (request.Method == HttpMethod.Post && path == "/api/v2/admin/tasks/rebuild_catalog_search_index/run")
            {
                Rebuilds++;
                return Json(new { key = "rebuild_catalog_search_index", name = "Rebuild", state = "running", progress = 0 });
            }
            else if (path == "/api/v2/admin/catalog/search/status")
            {
                StatusReads++;
                if (StatusFailure) return Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StatusJson(), Encoding.UTF8, "application/json") };
            }
            else if (path.EndsWith("/sensitive-status", StringComparison.Ordinal))
            {
                var managed = (ManagedUrl ? new[] { Url } : []).Concat(ManagedApiKey ? new[] { ApiKey } : []).ToArray();
                body = managed.Length > 0 && NamedVariables
                    ? new { configured = KeyConfigured ? new[] { ApiKey } : [], managed_by_env = managed,
                        managed_by_env_vars = managed.ToDictionary(key => key, key => key == Url ? "SILO_MEILISEARCH_URL" : "SILO_MEILISEARCH_API_KEY_FILE") }
                    : (object)new { configured = KeyConfigured ? new[] { ApiKey } : [], managed_by_env = managed };
            }
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/restart-keys" => new { keys = new[] { "catalog.search.meilisearch.timeout_ms" }, prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            return Json(body);
        }
        private string StatusJson() => $$"""
            {"configured_provider":"meilisearch","active_provider":"meilisearch","degraded":{{(Degraded ? "true" : "false")}},{{(Degraded ? "\"degraded_reason\":\"Synthetic Meilisearch is unreachable.\"," : "")}}
             "meilisearch":{"configured":true,"healthy":true,"circuit_state":"closed","timeout_ms":800,"matching_strategy":"last","index_types":["movie","show"],
               "semantic_enabled":false,"binary_quantized":false,"semantic_ratio":0.5,"embedder":"default"},
             "index":{"active_index_uid":"silo_media_items_v3","schema_version":3,"expected_schema_version":3,"rebuild_required":{{(RebuildRequired ? "true" : "false")}},
               "document_count":1234,"vector_document_count":0,"pending_events":7,"dead_lettered_events":0,"last_sync_at":"2026-01-02T03:04:05Z","last_processed_event_id":"9"},
             "tasks":[],"semantic":{"ready":false,"vector_coverage_ratio":0,"capability":{"ok":true,"embedder":"default"} } }
            """;
        private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"search-fixture-v{Version}\""); return response;
        }
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton(new LoggingDisclosurePreferences());
        // Invoke the same registration entry point as App, rather than registering a test-only page.
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/settings/search");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("Search is not available through the production Configure registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            if (outer.Content is not AdminShellPage shell) throw new InvalidOperationException("Search did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content?.GetType().Name != "AdminSearchSettingsPage"; i++) await Task.Delay(25);
            if (inner.Content is not AdminSearchSettingsPage page) throw new InvalidOperationException("Search did not activate its production WinUI Page.");
            await Until(() => page.Controller?.State.Loaded == true && Select(page, Provider).SelectedItem != null, "Search fields rendered");
            var controller = page.Controller!;
            Check(true, "Search activates through production registration, shell and Frame");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal) || t.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "rendered Search and navigation labels have catalog text");
            var shellText = Descendants(shell).OfType<TextBlock>().Where(t => !IsInside(t, page)).Select(t => t.Text).ToArray();
            Check(shellText.Contains("Server") && shellText.Contains("Search"), "sidebar shows the Server topic with its Search leaf");
            Check(AdminNavigation.SearchGroups("reindex", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(g => g.Pages).Single().Route == AdminRoute.SearchSettings,
                "registered Search is searchable through its official keywords");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Search navigation keeps page transitions suppressed");
            Check(AutomationProperties.GetName(Descendants(page).OfType<ScrollViewer>().First()) == "Search settings", "the page region is named Search settings");

            // Meilisearch is selected: its rows are shown, the engine row carries the one-line status summary.
            await Until(() => server.StatusReads >= 1 && controller.State.Status != null, "status read for the selected Meilisearch");
            await Until(() => Shown(Select(page, Provider), page) && Shown(Input(page, Url), page) && Shown(Secret(page), page) && Shown(Button(page, "SearchCheckConnection"), page) &&
                Shown(Select(page, "catalog.search.meilisearch.matching_strategy"), page) && Shown(Toggle(page), page), "the Meilisearch rows are rendered");
            Check(Shown(Select(page, Provider), page) && Shown(Input(page, Url), page) && Shown(Secret(page), page) && Shown(Button(page, "SearchCheckConnection"), page) &&
                Shown(Select(page, "catalog.search.meilisearch.matching_strategy"), page) && Shown(Toggle(page), page), "Meilisearch rows are shown while it is selected");
            Check(!Shown(Input(page, "catalog.search.meilisearch.timeout_ms"), page), "Advanced rows stay hidden while its disclosure is closed");
            await Until(() => Text(page, "SearchStatusSummary").Text.StartsWith("Last sync ", StringComparison.Ordinal) && Text(page, "SearchStatusSummary").Text.EndsWith("items indexed", StringComparison.Ordinal), "status summary under the engine row");
            Check(Text(page, "SearchStatusSummary").Visibility == Visibility.Visible, "the engine row shows the index summary");
            Check((Select(page, Provider).SelectedItem as ComboBoxItem)?.Content as string == "Meilisearch" &&
                Select(page, Provider).Items.Cast<ComboBoxItem>().Select(i => i.Content as string).SequenceEqual(["Built-in (Postgres)", "Meilisearch"]),
                "engine select offers the official options in order");
            var matching = Select(page, "catalog.search.meilisearch.matching_strategy");
            Check((matching.SelectedItem as ComboBoxItem)?.Content as string == "Drop trailing words until something matches" && controller.State.DirtyKeys.Count == 0,
                "a blank saved matching strategy displays the official default without staging an edit");
            Check(EnvironmentNoteProbe.Verify(page, Input(page, Url), EnvironmentNoteProbe.Named("SILO_MEILISEARCH_URL")) is null, "an environment-managed URL is disabled, shows the official note naming its variable and carries it in its help text");
            Check(EnvironmentNoteProbe.VerifyEditable(Input(page, "catalog.search.meilisearch.index")) is null, "an editable Search row carries no environment note");
            server.NamedVariables = false; await controller.ReloadAsync(true);
            await Until(() => controller.State.EnvManaged.VariableName(Url) == null, "the older server's answer is applied");
            Check(EnvironmentNoteProbe.Verify(page, Input(page, Url), EnvironmentNoteProbe.Named("SILO_MEILISEARCH_URL")) is null, "an older server that names no variable keeps the variable this page has always shown for the URL");
            server.NamedVariables = true; await controller.ReloadAsync(true);
            await Until(() => controller.State.EnvManaged.VariableName(Url) == "SILO_MEILISEARCH_URL", "the named answer is applied again");
            // A managed API key: the page's hint stays (official SecretField), the note names the variable the server sent, and a server that names none gets
            // the unnamed note, not a guessed variable (the key may come from a file).
            server.ManagedApiKey = true; await controller.ReloadAsync(true);
            await Until(() => controller.State.EnvManaged.IsManaged(ApiKey), "the managed API key is applied");
            Check(EnvironmentNoteProbe.Verify(page, Secret(page), EnvironmentNoteProbe.Named("SILO_MEILISEARCH_API_KEY_FILE")) is null, "a managed API key is disabled and shows the note naming the variable the server sent");
            Check(Description(page, ApiKey).StartsWith("Master key, or one that can read and write the index.", StringComparison.Ordinal) && !Description(page, ApiKey).Contains("saved", StringComparison.OrdinalIgnoreCase), "a managed API key keeps its hint and makes no claim about a saved value");
            server.NamedVariables = false; await controller.ReloadAsync(true);
            await Until(() => EnvironmentNoteProbe.Verify(page, Secret(page), EnvironmentNoteProbe.Unnamed) is null, "a managed API key the server names no variable for shows the unnamed note");
            server.NamedVariables = true; server.ManagedApiKey = false; await controller.ReloadAsync(true);
            await Until(() => !controller.State.EnvManaged.IsManaged(ApiKey), "the API key is editable again");
            var secret = Secret(page);
            var keyAction = Button(page, "SearchApiKeyAction");
            Check(secret.PlaceholderText == "••••••••••••" && secret.Password == "" && keyAction.Visibility == Visibility.Visible &&
                AutomationProperties.GetName(keyAction) == "Clear saved value", "a saved API key shows only the mask and an explicit clear action");
            Check(keyAction.TransformToVisual(page).TransformPoint(new(0, 0)).X < secret.TransformToVisual(page).TransformPoint(new(0, 0)).X, "the key action sits before the input");

            // API key: clear / keep / replace / erase.
            Invoke(keyAction); await Until(() => controller.State.DirtyKeys.Contains(ApiKey), "clear staged");
            await Task.Delay(100);
            Check(AutomationProperties.GetName(keyAction) == "Keep saved value" && secret.PlaceholderText == "Will be cleared on save", "a staged clear offers Keep saved value");
            Invoke(keyAction); await Until(() => controller.State.DirtyKeys.Count == 0, "keep restored");
            secret.Password = "synthetic-new-key"; await Until(() => controller.State.DirtyKeys.Contains(ApiKey), "replacement staged");
            secret.Password = ""; await Until(() => controller.State.DirtyKeys.Count == 0, "erasing keeps the saved key");
            Check(controller.State.ApiKeyConfigured, "erasing a replacement keeps the saved key");

            // Semantic warning.
            var toggle = Toggle(page);
            Check(!Descendants(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Enabling this changes the index format", StringComparison.Ordinal) && Shown(t, page)), "no format warning before enabling");
            toggle.IsOn = true; await Until(() => controller.State.DirtyKeys.Contains(Semantic), "semantic staged");
            await Task.Delay(100);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Enabling this changes the index format", StringComparison.Ordinal) && Shown(t, page)), "enabling meaning-based search shows the index-format warning");
            toggle.IsOn = false; await Until(() => controller.State.DirtyKeys.Count == 0, "semantic withdrawn");

            // Connection check against the staged values; any edit hides the result.
            var checkResult = Text(page, "SearchCheckResult");
            await Click(page, "SearchCheckConnection"); await Until(() => server.Checks == 1 && controller.State.ConnectionResult != null, "check result");
            await Task.Delay(100);
            Check(checkResult.Text == "Meilisearch is reachable." && checkResult.Foreground is SolidColorBrush { Color.G: 0xA3 }, "a successful check shows the green server message");
            using (var body = JsonDocument.Parse(server.LastCheck!))
                Check(body.RootElement.GetProperty("values").EnumerateObject().Count() == 8 && body.RootElement.GetProperty("dirty_keys").GetArrayLength() == 0, "the check sends every Meilisearch value and no unedited key as dirty");
            Select(page, "catalog.search.meilisearch.matching_strategy").Focus(FocusState.Keyboard);
            await SelectItem(matching, 1); await Until(() => controller.State.DirtyKeys.Contains("catalog.search.meilisearch.matching_strategy"), "matching staged");
            await Task.Delay(100);
            Check(checkResult.Text.Length == 0, "editing a checked value hides the check result");
            await SelectItem(matching, 0);
            await Until(() => controller.State.Values["catalog.search.meilisearch.matching_strategy"] == "last", "explicit matching default staged");
            Check(controller.State.DirtyKeys.SequenceEqual(["catalog.search.meilisearch.matching_strategy"]),
                "choosing an explicit default remains an edit over a blank saved value, matching the official form");
            await Click(page, "SearchDiscard");
            await Until(() => controller.State.DirtyKeys.Count == 0 && controller.State.Values["catalog.search.meilisearch.matching_strategy"] == "", "blank matching baseline restored");

            // Advanced: the four tuning rows and the status panel.
            var advanced = Button(page, "SearchAdvanced");
            Check(AutomationProperties.GetName(advanced) == "Advanced · 4 settings", "the disclosure names its four settings");
            Invoke(advanced); await Until(() => Shown(Input(page, Timeout), page), "Advanced opened");
            await Until(() => Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Answering searches"), "status rows built");
            Check(Input(page, Timeout).Text == "800" && Input(page, "catalog.search.meilisearch.index").Text == "silo_media_items" &&
                Input(page, "catalog.search.meilisearch.sync_batch_size").Text == "500" && Input(page, "catalog.search.meilisearch.semantic_ratio").Text == "0.50",
                "blank tuning values display the official defaults");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "SEARCH STATUS") && Descendants(page).Any(e => AutomationProperties.GetName(e) == "Documents: 1234"), "Search status lists the index state");
            Check(Descendants(page).Any(e => AutomationProperties.GetName(e) == "Answering searches: Meilisearch meilisearch"), "the status row names the answering engine and configured provider");
            var rebuild = Button(page, "SearchRebuildIndex");
            var maintenance = Button(page, "SearchAutomaticMaintenance");
            // Scheduled Tasks is registered by the production Configure registration, so both official task links are live.
            Check(rebuild.IsEnabled && maintenance.IsEnabled, "both maintenance links are enabled because Scheduled Tasks is registered");
            Check(ToolTipService.GetToolTip(rebuild) == null && ToolTipService.GetToolTip(maintenance) == null &&
                AutomationProperties.GetHelpText(rebuild).Length == 0 && AutomationProperties.GetHelpText(maintenance).Length == 0,
                "registered task links carry no unavailable-dependency tooltip or help text");
            Check(AutomationProperties.GetName(rebuild) == "Rebuild index" && AutomationProperties.GetName(maintenance) == "Automatic maintenance",
                "the two task links are named as the official status panel names them");
            Check(server.Rebuilds == 0 && Field(page, "_dialog") == null, "task links expose no task command or confirmation dialog");

            // Status polling: 2 seconds, only while a rebuild is required and the page is visible.
            server.RebuildRequired = true; controller.RefreshStatus();
            await Until(() => controller.State.Status?.Index.RebuildRequired == true && controller.IsPolling, "polling starts for a required rebuild");
            await Until(() => Descendants(page).Any(e => AutomationProperties.GetName(e) is { } n && n.StartsWith("Active index:", StringComparison.Ordinal) && n.EndsWith("rebuild required", StringComparison.Ordinal)), "status row shows rebuild required");
            var reads = server.StatusReads;
            await Until(() => server.StatusReads > reads, "a poll read arrives", 4500);
            page.SetHostActivity(visible: false, focused: true); await Task.Delay(100);
            Check(!controller.IsPolling, "a hidden window drops the poll timer");
            reads = server.StatusReads; await Task.Delay(2600);
            Check(server.StatusReads == reads, "no status read while the window is hidden");
            page.SetHostActivity(visible: true, focused: true);
            await Until(() => server.StatusReads > reads, "polling resumes when visible", 4500);
            server.RebuildRequired = false;
            await Until(() => controller.State.Status?.Index.RebuildRequired == false && !controller.IsPolling, "polling stops when the rebuild completes", 4500);
            reads = server.StatusReads; await Task.Delay(2600);
            Check(server.StatusReads == reads, "no further polling once no rebuild is required");

            // Degraded notice, then a failed status keeps the last rows and offers Retry.
            server.Degraded = true; controller.RefreshStatus();
            await Until(() => controller.State.Status?.Degraded == true, "degraded status"); await Task.Delay(150);
            Check(Text(page, "SearchDegraded").Text.StartsWith("Additional provider diagnostics are withheld.", StringComparison.Ordinal) && Shown(Text(page, "SearchDegraded"), page), "a degraded status shows a safe diagnostic beside the engine row");
            Check(Text(page, "SearchStatusSummary").Visibility == Visibility.Collapsed, "the index summary yields to the degraded notice");
            server.Degraded = false; server.StatusFailure = true; controller.RefreshStatus();
            await Until(() => controller.State.StatusFailure != null, "status failure"); await Task.Delay(150);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Couldn't load search status", StringComparison.Ordinal)) && Descendants(page).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "SearchRetryStatus"),
                "a failed status read says so and offers Retry, never an endless skeleton");
            Check(rebuild.IsEnabled && maintenance.IsEnabled, "a failed status read leaves both task links available, as the official panel shows them whether or not status resolved");
            server.StatusFailure = false; Invoke(Button(page, "SearchRetryStatus"));
            await Until(() => controller.State.StatusFailure == null && Descendants(page).Any(e => AutomationProperties.GetName(e) == "Documents: 1234"), "retry restores the status rows");

            // Save only the dirty tuning key under the strong ETag; the restart chip marks the server-flagged field.
            var timeout = Input(page, Timeout);
            timeout.Text = "1200"; await Until(() => controller.State.DirtyKeys.Contains(Timeout), "timeout staged");
            await Click(page, "SearchSave"); await Until(() => server.Saves == 1 && !controller.State.Saving, "save settled"); await Task.Delay(150);
            Check(server.LastIfMatch == "\"search-fixture-v1\"" && server.LastPut == """{"values":{"catalog.search.meilisearch.timeout_ms":"1200"}}""", "Save sends one guarded write with only the dirty key");
            Check(controller.State.DirtyKeys.Count == 0 && timeout.Text == "1200", "the acknowledged value is the new saved value");
            timeout.Focus(FocusState.Keyboard); await Until(() => timeout.FocusState != FocusState.Unfocused, "the tuning box has focus");
            timeout.Text = ""; await Until(() => controller.State.DirtyKeys.Contains(Timeout), "clearing the saved value is an edit");
            await Task.Delay(100);
            Check(timeout.Text == "", "a box being typed in keeps its cleared text while it is focused or just edited");
            await Click(page, "SearchDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard");
            await Task.Delay(100);
            Check(timeout.Text == "1200", "Discard restores the saved value into the box");

            // Switching to Postgres hides the Meilisearch rows; a staged Meilisearch edit keeps them reachable.
            await SelectItem(Select(page, Provider), 0); await Until(() => controller.State.DirtyKeys.Contains(Provider), "postgres staged");
            await Task.Delay(100);
            Check(!Shown(Input(page, Url), page) && !Shown(Secret(page), page), "choosing Postgres hides the Meilisearch rows");
            await SelectItem(Select(page, Provider), 1); await Until(() => controller.State.DirtyKeys.Count == 0, "provider restored");
            timeout.Text = "900"; await Until(() => controller.State.DirtyKeys.Contains(Timeout), "tuning staged");
            await SelectItem(Select(page, Provider), 0); await Task.Delay(150);
            Check(Shown(timeout, page) && controller.State.DirtyKeys.Contains(Timeout), "a staged Advanced edit stays visible after choosing Postgres");
            await Click(page, "SearchDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard staged edits");

            // Dirty departure dialog.
            timeout.Text = "700"; await Until(() => controller.State.DirtyKeys.Count > 0, "dialog draft");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "dirty leave dialog");
            var dialog = (ContentDialog)Field(page, "_dialog")!;
            Invoke(Descendants(dialog).OfType<Button>().First(b => AutomationProperties.GetName(b) == "Cancel"));
            await Until(() => Field(page, "_dialog") == null && Field(page, "_leave") is AdminLeaveGuard { IsConfirming: false }, "Cancel navigation settled");
            Check(ReferenceEquals(inner.Content, page) && controller.State.DirtyKeys.Count > 0, "Cancel retains the Search page and its draft");
            await Click(page, "SearchDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "draft discarded");

            // Departure retires the controller and its polling; Back starts clean.
            server.RebuildRequired = true; controller.RefreshStatus(); await Until(() => controller.IsPolling, "polling before departure");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => inner.Content is AdminActivityPage, "departure");
            reads = server.StatusReads; await Task.Delay(2600);
            Check(controller.State.Values.Count == 0 && !controller.IsPolling && server.StatusReads == reads, "departure retires the controller, clears values and stops polling");
            server.RebuildRequired = false;
            inner.GoBack(); await Until(() => inner.Content is AdminSearchSettingsPage { Controller.State.Loaded: true }, "Back restored production Search", diagnostics: () =>
                $"page={inner.Content?.GetType().Name}; loaded={(inner.Content as FrameworkElement)?.IsLoaded}; back={inner.BackStack.Count}; forward={inner.ForwardStack.Count}; controller={(inner.Content as AdminSearchSettingsPage)?.Controller != null}; loading={(inner.Content as AdminSearchSettingsPage)?.Controller?.State.Loading}; failed={(inner.Content as AdminSearchSettingsPage)?.Controller?.State.Error != null}");
            page = (AdminSearchSettingsPage)inner.Content; controller = page.Controller!;

            // Reproduce activation's closed local disclosure with persisted Advanced=true, but
            // status already complete before the dispatcher can drain any queued snapshots.
            await Until(() => controller.State.Status != null && Field(controller, "_statusFlight") == null, "Back status completed");
            Check(services.GetRequiredService<LoggingDisclosurePreferences>().Read("search"), "Advanced open preference survives departure");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(AdminSearchSettingsPage).GetMethod("ClearStatusUi", flags)!.Invoke(page, null);
            typeof(AdminSearchSettingsPage).GetField("_advancedOpen", flags)!.SetValue(page, false);
            typeof(AdminSearchSettingsPage).GetField("_override", flags)!.SetValue(page, null);
            typeof(AdminSearchSettingsPage).GetMethod("RenderDisclosure", flags)!.Invoke(page, null);
            controller.SetAdvancedOpen(false);
            reads = server.StatusReads;
            typeof(AdminSearchSettingsPage).GetMethod("QueueRender", flags)!.Invoke(page, null);
            controller.Discard(); // supersedes the queued snapshot without changing fresh status
            await Until(() => AutomationProperties.GetItemStatus(Button(page, "SearchAdvanced")) == "expanded" &&
                Descendants(page).Any(e => AutomationProperties.GetName(e) == "Documents: 1234"), "persisted Advanced renders already completed status");
            Check(server.StatusReads == reads && !controller.IsPolling && Shown(Input(page, Timeout), page),
                "restoring Advanced populates fresh status without an interaction, extra read or polling");

            // The official task links open the task's detail page (they never run it from here); Back returns to a clean Search page.
            foreach (var (linkId, taskKey) in new[] { ("SearchRebuildIndex", "rebuild_catalog_search_index"), ("SearchAutomaticMaintenance", "sync_catalog_search_index") })
            {
                var taskLink = Button(page, linkId);
                await Until(() => taskLink.IsLoaded && taskLink.IsEnabled && Shown(taskLink, page), linkId + " ready");
                Invoke(taskLink);
                await Until(() => inner.Content is AdminScheduledTasksPage && shell.CurrentRequest.Route == AdminRoute.ScheduledTasks, linkId + " opens Scheduled Tasks");
                Check(shell.CurrentRequest.Segments.SequenceEqual([taskKey]), linkId + " opens the " + taskKey + " task detail");
                Check(server.Rebuilds == 0, linkId + " does not run the task");
                inner.GoBack();
                await Until(() => inner.Content is AdminSearchSettingsPage { Controller.State.Loaded: true }, "Back restored Search after " + linkId);
                page = (AdminSearchSettingsPage)inner.Content; controller = page.Controller!;
            }

            // PasswordChanged is deferred: retirement must clear typing even before the controller sees it.
            secret = Secret(page);
            await Until(() => Shown(Input(page, Timeout), page) || Shown(Secret(page), page), "Back fields rendered");
            secret.Password = "synthetic-retirement-race";
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic-admin" }); await Task.Delay(200);
            Check(controller.State.Values.Count == 0 && Input(page, Url).Text.Length == 0, "authority retirement clears values");
            Check(secret.Password.Length == 0, "authority retirement clears key typing awaiting PasswordChanged");
        }
        finally
        {
            try
            {
                var unloading = outer == null ? Array.Empty<Task>() : Descendants(outer).OfType<FrameworkElement>()
                    .Where(element => element.IsLoaded && (element is Page || ReferenceEquals(element, outer)))
                    .Select(Unloaded).ToArray();
                if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
                window.Content = previousContent;
                await Task.WhenAll(unloading).WaitAsync(TimeSpan.FromSeconds(6));
                Check(true, "Search Page and Frame Unloaded complete before fixture services retire");
            }
            finally
            {
                window.Content = previousContent;
                field.SetValue(null, previous);
            }
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Search fixture failures: " + string.Join("; ", Failures));
    }

    private static Task Unloaded(FrameworkElement element)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        element.Unloaded += (_, _) => completion.TrySetResult();
        return completion.Task;
    }

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    /// <summary>Visible and every ancestor up to the page visible (a collapsed parent hides its children without changing theirs).</summary>
    private static bool Shown(FrameworkElement element, DependencyObject page)
    {
        for (DependencyObject? current = element; current != null && !ReferenceEquals(current, page); current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return element.IsLoaded;
    }
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static TextBox Input(DependencyObject page, string key) => Descendants(page).OfType<TextBox>().Single(c => AutomationProperties.GetAutomationId(c) == "Search_" + key);
    private static ComboBox Select(DependencyObject page, string key) => Descendants(page).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == "Search_" + key);
    private static PasswordBox Secret(DependencyObject page) => Descendants(page).OfType<PasswordBox>().Single();
    private static ToggleSwitch Toggle(DependencyObject page) => Descendants(page).OfType<ToggleSwitch>().Single(c => AutomationProperties.GetAutomationId(c) == "Search_" + Semantic);
    private static TextBlock Text(DependencyObject page, string id) => Descendants(page).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == id);
    private static string Description(DependencyObject page, string key) => AutomationProperties.GetHelpText(Descendants(page).OfType<Control>().Single(c => AutomationProperties.GetAutomationId(c) == "Search_" + key));
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    /// <summary>Selects through the open dropdown's realized item, as a user choice does.</summary>
    private static async Task SelectItem(ComboBox select, int index)
    {
        select.IsDropDownOpen = true; await Task.Delay(250);
        var item = (ComboBoxItem)select.Items[index];
        var provider = FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
        if (provider != null) provider.Select(); else select.SelectedItem = item;
        select.IsDropDownOpen = false; await Task.Delay(150);
    }
    private static async Task Click(DependencyObject page, string id)
    {
        var button = Button(page, id);
        await Until(() => button.IsEnabled && button.IsLoaded, id + " enabled");
        Invoke(button);
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
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
