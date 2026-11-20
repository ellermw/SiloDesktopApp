using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views.Admin;

internal static partial class AdminRequestsManagementNativeFixture
{
    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<string> Calls = [];
        internal string? LastValidator, LastWrite;
        internal bool Conflict, Watchlist = true, Routing = true, Discover = true;
        internal bool Corrections, MissingSeriesFallback, Downloading;
        internal bool StoredPluginSecret, FractionalNumbers, PreviewOverrides, RulesForReorder, LoseReorder, FailConfigurationReads, QueuedProgress, ProgressOnTarget;
        internal bool ConditionalPluginSecret, OverlapPreviews, HoldPreviewOptions, LoseCreate, CreateWithoutIdentity, FailEarlierPreview;
        internal bool FailQueueReads, RejectLayoutWrites, PagedQueue, RetiredServerType;
        internal readonly TaskCompletionSource<bool> PreviewReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> ReleasePreviewRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _previewWaitingOptions;
        internal int FailedConfigurationReads;
        internal int QueueReads;
        internal string? LastProbe, LastCreatedRoute;
        private JsonObject _settings = JsonNode.Parse("{\"requests_enabled\":true,\"global_max_requests\":5,\"global_window_days\":7,\"global_auto_approval_enabled\":false,\"force_dual_quality\":false,\"watchlist_requests\":true}")!.AsObject();
        private JsonObject _routing = JsonNode.Parse("{\"mode\":\"standard\",\"standard\":[{\"media_type\":\"movie\",\"hd_integration_id\":\"integration-1\"}]}")!.AsObject();
        private JsonObject _layout = JsonNode.Parse("{\"customized\":false,\"rows\":[{\"id\":\"trending_movies\",\"type\":\"tmdb_chart\",\"title\":\"Trending Movies\",\"enabled\":true,\"limit\":20,\"chart\":{\"preset\":\"trending\",\"media_type\":\"movie\",\"time_window\":\"week\"}}],\"max_rows\":30}")!.AsObject();
        private const string Integration = "{\"id\":\"integration-1\",\"name\":\"Synthetic Radarr\",\"enabled\":true,\"base_url\":\"http://router.test:7878\",\"capability_id\":\"arr\",\"installation_id\":\"7\",\"has_api_key\":true,\"supported_media_types\":[\"movie\"],\"plugin_config\":{\"service_kind\":\"radarr\",\"quality_profile_id\":1}}";
        private const string Fallback = "{\"id\":\"fallback-movie\",\"media_type\":\"movie\",\"name\":\"Everything else\",\"enabled\":true,\"is_fallback\":true,\"conditions\":{},\"hd\":{\"integration_id\":\"integration-1\"},\"uhd\":{},\"skip_uhd\":false}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add(request.Method + " " + path);
            if (Corrections)
            {
                if (path == "/api/v2/admin/requests") QueueReads++;
                if (path == "/api/v2/admin/requests" && FailQueueReads) return Json("{}", HttpStatusCode.ServiceUnavailable);
                if (path == "/api/v2/admin/requests" && PagedQueue)
                {
                    const string row = "{\"id\":\"7\",\"media_type\":\"movie\",\"tmdb_id\":123,\"title\":\"Synthetic Request\",\"status\":\"pending\",\"outcome\":\"active\",\"targets\":[]}";
                    return Json(request.RequestUri!.Query.Contains("cursor=synthetic-next", StringComparison.Ordinal)
                        ? "{\"items\":[" + row.Replace("\"id\":\"7\"", "\"id\":\"8\"") + "],\"page\":{\"has_more\":false}}"
                        : "{\"items\":[" + row + "],\"page\":{\"has_more\":true,\"next_cursor\":\"synthetic-next\"}}");
                }
                if (request.Method == HttpMethod.Post && path == "/api/v2/admin/request-integrations" && LoseCreate)
                {
                    LoseCreate = false; FailConfigurationReads = true;
                    return Json("{}", HttpStatusCode.ServiceUnavailable);
                }
                if (request.Method == HttpMethod.Post && path == "/api/v2/admin/request-integrations" && CreateWithoutIdentity)
                    return Json("{\"name\":\"Synthetic added server\"}", HttpStatusCode.Created);
                if (request.Method == HttpMethod.Post && path == "/api/v2/admin/request-routes/order")
                {
                    if (LoseReorder) { LoseReorder = false; FailConfigurationReads = true; return Json("{}", HttpStatusCode.ServiceUnavailable); }
                    return Json("{}");
                }
                if (request.Method == HttpMethod.Get && path == "/api/v2/admin/request-integrations" && FailConfigurationReads)
                { FailedConfigurationReads++; return Json("{}", HttpStatusCode.ServiceUnavailable); }
                if (request.Method == HttpMethod.Get && path == "/api/v2/admin/request-integrations/integration-1" && (StoredPluginSecret || FractionalNumbers))
                {
                    var integration = JsonNode.Parse(Integration)!.AsObject(); var config = integration["plugin_config"]!.AsObject();
                    if (StoredPluginSecret) config["credential"] = "synthetic-saved-plugin-credential";
                    if (ConditionalPluginSecret) config["use_credential"] = true;
                    if (FractionalNumbers) { config["timeout"] = 0.5; config["ratio"] = 0.5; }
                    return Json(integration.ToJsonString());
                }
                if (path == "/api/v2/admin/request-routes/titles" && OverlapPreviews)
                    return Json("{\"items\":[{\"tmdb_id\":123,\"media_type\":\"movie\",\"title\":\"Earlier title\",\"year\":2026},{\"tmdb_id\":456,\"media_type\":\"movie\",\"title\":\"Latest title\",\"year\":2026}],\"page\":{\"has_more\":false}}");
                if (path == "/api/v2/admin/request-routes/preview" && OverlapPreviews)
                {
                    var input = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                    if (input["tmdb_id"]?.GetValue<int>() == 123)
                    {
                        if (HoldPreviewOptions) _previewWaitingOptions = true;
                        else { PreviewReadStarted.TrySetResult(true); await ReleasePreviewRead.Task.WaitAsync(ct); }
                        if (FailEarlierPreview) return Json("{}", HttpStatusCode.ServiceUnavailable);
                        return Json("{\"facts\":{},\"tiers\":[{\"quality\":\"hd\",\"integration_id\":\"integration-1\",\"integration_name\":\"Earlier selection\",\"route_name\":\"Everything else\",\"overrides\":{\"quality_profile_id\":1}}],\"rules\":[]}");
                    }
                    return Json("{\"facts\":{},\"tiers\":[{\"quality\":\"hd\",\"integration_name\":\"Latest selection\",\"route_name\":\"Everything else\"}],\"rules\":[]}");
                }
                if (_previewWaitingOptions && path == "/api/v2/admin/request-integrations/integration-1/options")
                {
                    _previewWaitingOptions = false; PreviewReadStarted.TrySetResult(true); await ReleasePreviewRead.Task.WaitAsync(ct);
                }
                if (path == "/api/v2/admin/request-routes/preview" && PreviewOverrides)
                    return Json("{\"facts\":{},\"tiers\":[{\"quality\":\"hd\",\"integration_id\":\"integration-1\",\"integration_name\":\"Synthetic Radarr\",\"route_name\":\"Everything else\",\"overrides\":{\"root_folder\":\"/media/movies\",\"quality_profile_id\":1,\"series_type\":\"anime\"}}],\"rules\":[]}");
                if (path == "/api/v2/admin/request-routes" && RulesForReorder)
                    return Json("{\"items\":[{\"id\":\"rule-a\"},{\"id\":\"rule-b\"}," + Fallback + "],\"page\":{\"has_more\":false}}");
                if (RulesForReorder && path is "/api/v2/admin/request-routes/rule-a" or "/api/v2/admin/request-routes/rule-b")
                    return Json(Fallback.Replace("fallback-movie", path.EndsWith("rule-a") ? "rule-a" : "rule-b").Replace("Everything else", "Synthetic rule").Replace("\"is_fallback\":true", "\"is_fallback\":false"));
                if (path == "/api/v2/admin/requests" && QueuedProgress)
                {
                    var progress = "{\"progress\":25,\"size_bytes\":100}";
                    return Json("{\"items\":[{\"id\":\"7\",\"media_type\":\"movie\",\"tmdb_id\":123,\"title\":\"Synthetic Download\",\"status\":\"queued\",\"outcome\":\"active\",\"targets\":" + (ProgressOnTarget ? "[{\"status\":\"queued\",\"download\":" + progress + "}]" : "[],\"download\":" + progress) + "}],\"page\":{\"has_more\":false}}");
                }
                if (request.Method == HttpMethod.Post && path.EndsWith("/options")) LastProbe = await request.Content!.ReadAsStringAsync(ct);
                if (request.Method == HttpMethod.Post && path == "/api/v2/admin/request-routes")
                {
                    LastCreatedRoute = await request.Content!.ReadAsStringAsync(ct);
                    var created = JsonNode.Parse(LastCreatedRoute)!.AsObject(); created["id"] = "created-rule";
                    return Json(created.ToJsonString());
                }
                if (path == "/api/v2/admin/request-integrations" && request.Method == HttpMethod.Get) return Json("{\"items\":[{\"id\":\"integration-1\"},{\"id\":\"integration-2\"}],\"page\":{\"has_more\":false}}");
                if (path == "/api/v2/admin/request-integrations/integration-2")
                {
                    if (request.Method == HttpMethod.Put) LastWrite = await request.Content!.ReadAsStringAsync(ct);
                    // A retired type names an installation the plugin list no longer offers.
                    return Json(Integration.Replace("integration-1", "integration-2").Replace("Synthetic Radarr", "Synthetic Sonarr").Replace("radarr", "sonarr").Replace("movie", "series").Replace("\"installation_id\":\"7\"", "\"installation_id\":\"" + (RetiredServerType ? "9" : "7") + "\""));
                }
                if (path == "/api/v2/admin/plugins/installations")
                {
                    if (!ConditionalPluginSecret) return Json(CorrectionPlugin);
                    var plugin = JsonNode.Parse(CorrectionPlugin)!;
                    var fields = plugin["items"]![0]!["capabilities"]![0]!["config_schema"]![0]!["admin_form"]!["fields"]!.AsArray();
                    fields.Insert(0, new JsonObject { ["key"] = "use_credential", ["label"] = "Use credential", ["control"] = "SWITCH" });
                    fields.Single(field => field?["key"]?.ToString() == "credential")!["show_when"] = new JsonArray(new JsonObject { ["field"] = "use_credential", ["equals"] = new JsonArray(true) });
                    return Json(plugin.ToJsonString());
                }
                if (path == "/api/v2/admin/request-routes/fallback-series") return Json(Fallback.Replace("fallback-movie", "fallback-series").Replace("movie", "series").Replace("integration-1", MissingSeriesFallback ? "" : "integration-2"));
                if (path == "/api/v2/admin/request-integrations/integration-2/options") return Json("{\"options\":{\"service_kind\":[{\"value\":\"sonarr\",\"label\":\"Sonarr\"}],\"series_type\":[{\"value\":\"standard\",\"label\":\"Standard\"},{\"value\":\"anime\",\"label\":\"Anime\"}]}}");
                if (path == "/api/v2/admin/request-routes") return Json("{\"items\":[" + Fallback + "," + Fallback.Replace("fallback-movie", "fallback-series").Replace("movie", "series").Replace("integration-1", MissingSeriesFallback ? "" : "integration-2") + "],\"page\":{\"has_more\":false}}");
                if (path == "/api/v2/admin/requests" && Downloading) return Json("{\"items\":[{\"id\":\"7\",\"media_type\":\"movie\",\"tmdb_id\":123,\"title\":\"Synthetic Download\",\"status\":\"downloading\",\"outcome\":\"active\",\"download\":{\"progress\":25},\"targets\":[]}],\"page\":{\"has_more\":false}}");
            }
            if (request.Method == HttpMethod.Put)
            {
                LastValidator = request.Headers.IfMatch.SingleOrDefault()?.ToString(); LastWrite = await request.Content!.ReadAsStringAsync(ct);
                if (Conflict) return Json("{\"type\":\"https://siloserver.org/problems/precondition_failed\",\"title\":\"Changed\",\"status\":412}", HttpStatusCode.PreconditionFailed);
                if (RejectLayoutWrites && path.EndsWith("request-discover/layout")) return Json("{\"type\":\"https://siloserver.org/problems/validation_failed\",\"title\":\"Invalid\",\"status\":422}", HttpStatusCode.UnprocessableEntity);
                var written = JsonNode.Parse(LastWrite)!.AsObject();
                if (path.EndsWith("request-settings")) _settings = written;
                if (path.EndsWith("request-routing")) _routing["mode"] = written["mode"]!.DeepClone();
                if (path.EndsWith("request-discover/layout")) { _layout["rows"] = written["rows"]!.DeepClone(); _layout["customized"] = true; }
            }
            if (request.Method == HttpMethod.Delete) return Json("", HttpStatusCode.NoContent);
            var json = path switch
            {
                "/api/v2/admin/requests/capabilities" => new JsonObject { ["available"] = true, ["guarded_configuration"] = true, ["discover_layout"] = Discover, ["discover_list_order"] = true, ["routing"] = Routing, ["routing_series_status"] = true }.ToJsonString(),
                "/api/v2/admin/requests/counts" => "{\"needs_approval\":1,\"in_progress\":0,\"failed\":0,\"done\":0}",
                "/api/v2/admin/requests" => "{\"items\":[{\"id\":\"7\",\"media_type\":\"movie\",\"tmdb_id\":123,\"title\":\"Synthetic Request\",\"status\":\"pending\",\"outcome\":\"active\",\"requested_by_user_id\":\"8\",\"targets\":[]}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/requests/7/events" => "{\"items\":[{\"id\":\"9\",\"type\":\"created\",\"created_at\":\"2026-11-17T12:00:00Z\"}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/request-discover/layout" => _layout.ToJsonString(),
                "/api/v2/admin/request-settings" => Settings(),
                "/api/v2/admin/request-routing" => _routing.ToJsonString(),
                "/api/v2/admin/request-integrations" => "{\"items\":[{\"id\":\"integration-1\"}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/request-integrations/integration-1" => Integration,
                "/api/v2/admin/request-integrations/integration-1/options" => "{\"options\":{\"service_kind\":[{\"value\":\"radarr\",\"label\":\"Radarr\"}],\"quality_profile_id\":[{\"value\":\"1\",\"label\":\"Synthetic HD\"}],\"root_folder\":[{\"value\":\"/media/movies\",\"label\":\"Movies\"}]}}",
                "/api/v2/admin/request-routes" => "{\"items\":[" + Fallback + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/request-routes/fallback-movie" => Fallback,
                "/api/v2/admin/request-routes/titles" => "{\"items\":[{\"tmdb_id\":123,\"media_type\":\"movie\",\"title\":\"Synthetic Title\",\"year\":2026}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/request-routes/preview" => "{\"facts\":{},\"tiers\":[{\"quality\":\"hd\",\"integration_name\":\"Synthetic Radarr\",\"route_name\":\"Everything else\"}],\"rules\":[]}",
                "/api/v2/admin/plugins/installations" => "{\"items\":[{\"id\":\"7\",\"plugin_id\":\"synthetic-arr\",\"capabilities\":[{\"id\":\"arr\",\"type\":\"request_router.v1\",\"display_name\":\"Arr\",\"config_schema\":[{\"admin_form\":{\"fields\":[{\"key\":\"service_kind\",\"label\":\"Service\",\"control\":\"SELECT\",\"options\":[{\"value\":\"radarr\",\"label\":\"Radarr\"}]},{\"key\":\"quality_profile_id\",\"label\":\"Quality profile\",\"control\":\"SELECT\",\"dynamic_options\":true}]}}]}]}],\"page\":{\"has_more\":false}}",
                _ => "{\"items\":[],\"page\":{\"has_more\":false}}",
            };
            return Json(json);
        }
        private string Settings() { var copy = (JsonObject)_settings.DeepClone(); if (!Watchlist) copy.Remove("watchlist_requests"); return copy.ToJsonString(); }
        internal void AddDiscoverRow(string row) => _layout["rows"]!.AsArray().Add(JsonNode.Parse(row));
        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("ETag", "\"synthetic-1\"");
            return response;
        }
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", IsPrimary = true, Name = "Primary" });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        var registration = typeof(AdminShellPage).Assembly.GetType("SiloPlayer.Views.Admin.AdminRequestsRegistration");
        if (registration == null) throw new InvalidOperationException("Admin Requests registration is missing.");
        registration.GetMethod("AddAdminRequests")!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldServices = field.GetValue(null); var oldContent = window.Content;
        var frame = new Frame();
        field.SetValue(null, services);
        try
        {
            window.Content = frame; navigation.Frame = frame;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.RequestQueue)), "registered Requests Queue enters the real Admin shell");
            await Until(() => Find(window.Content, "AdminRequestDetails-7") != null);
            var shell = (AdminShellPage)frame.Content;
            Check(Find(window.Content, "AdminRequestApprove-7") != null, "pending request exposes Approve");
            await CaptureAsync(frame, "admin-requests-queue-wide.png");
            DependencyObject? actionAncestor = Find(frame, "AdminRequestApprove-7");
            while (actionAncestor != null && actionAncestor is not ScrollViewer) actionAncestor = VisualTreeHelper.GetParent(actionAncestor);
            Check(actionAncestor is ScrollViewer { ScrollableWidth: <= 1 }, "wide queue actions remain visible without horizontal scrolling");
            var decline = (Button)Find(window.Content, "AdminRequestDecline-7")!;
            Click(decline);
            await Until(() => Dialog(shell) is { IsLoaded: true });
            var dialog = Dialog(shell)!;
            Check(!server.Calls.Any(call => call.Contains("/decline")), "decline requires explicit confirmation");
            var reason = (TextBox)Find(dialog, "AdminRequestReason")!;
            reason.Text = "Synthetic reason";
            await CaptureAsync(dialog, "admin-requests-decline-dialog.png");
            dialog.Hide();
            await ClosedAsync(shell, dialog);
            Check(!server.Calls.Any(call => call.Contains("/decline")), "cancelled confirmation sends nothing");
            await Until(() => decline.IsEnabled && Find(window.Content, "AdminRequestDetails-7") is { IsLoaded: true });
            Click(Find(window.Content, "AdminRequestDetails-7")!);
            await Until(() => Dialog(shell) is { IsLoaded: true });
            dialog = Dialog(shell)!;
            Check(Walk(dialog).OfType<TextBlock>().Any(text => text.Text == "History"), "request history is reachable through the row details dialog");
            dialog.Hide(); await ClosedAsync(shell, dialog);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(760, 900)); await Task.Delay(150);
            await CaptureAsync(frame, "admin-requests-queue-compact.png");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            shell.Navigate(new AdminRouteRequest(AdminRoute.RequestDiscover));
            await Until(() => Find(window.Content, "AdminDiscoverAdd") != null);
            Check(Find(window.Content, "AdminDiscoverRestore") != null, "Discover offers restore defaults");
            await CaptureAsync(frame, "admin-requests-discover-wide.png");
            Click(Find(window.Content, "AdminDiscoverEdit-trending_movies")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); dialog = Dialog(shell)!;
            Check(Find(dialog, "AdminDiscoverChart") == null, "built-in row keeps its official chart");
            ((TextBox)Find(dialog, "AdminDiscoverName")!).Text = "Synthetic Trending";
            await CaptureAsync(dialog, "admin-requests-discover-editor.png");
            ClickPrimary(dialog); await ClosedAsync(shell, dialog);
            await Until(() => Walk(frame).OfType<ToggleSwitch>().Any(toggle => toggle.Header?.ToString() == "Synthetic Trending"));
            Check(server.LastValidator == "\"synthetic-1\"", "Discover replacement uses its canonical strong validator");
            Check(!server.LastWrite!.Contains("title_key") && !server.LastWrite.Contains("source"), "Discover excludes read-only row metadata");
            shell.Navigate(new AdminRouteRequest(AdminRoute.RequestSettings));
            await Until(() => Find(window.Content, "AdminRequestsEnabled") != null);
            Check(Find(window.Content, "AdminRequestsWatchlist") != null, "advertised watchlist setting is reachable");
            await Until(() => Find(window.Content, "AdminRequestServerEdit-integration-1") != null && Find(window.Content, "AdminRequestRoutingMode") != null);
            Check(Find(window.Content, "AdminRequestServerAdd") != null, "server connection editor is reachable");
            Check(Find(window.Content, "AdminRequestRoutingMode") != null, "routing mode is reachable");
            await CaptureAsync(frame, "admin-requests-settings-wide.png");
            var enabled = (ToggleSwitch)Find(window.Content, "AdminRequestsEnabled")!; enabled.IsOn = false;
            Check(((IAdminNavigationGuard)CurrentPage(shell)).RequiresLeaveConfirmation, "General edits report unsaved changes through the real shell guard");
            server.Conflict = true;
            var savesBefore = server.Calls.Count(call => call == "PUT /api/v2/admin/request-settings");
            Click(Find(window.Content, "AdminRequestsGeneralSave")!);
            await Until(() => server.Calls.Count(call => call == "PUT /api/v2/admin/request-settings") > savesBefore);
            await Until(() => Find(window.Content, "AdminRequestsNotice") is TextBlock text && text.Text.Contains("another admin"));
            Click(Find(window.Content, "AdminRequestsGeneralSave")!); await Task.Delay(100);
            Check(server.Calls.Count(call => call == "PUT /api/v2/admin/request-settings") == savesBefore + 1, "412 locks General save without rebasing or replay");
            Check(!enabled.IsOn && ((IAdminNavigationGuard)CurrentPage(shell)).RequiresLeaveConfirmation, "conflict retains the General draft");
            server.Conflict = false;
            Click(Find(window.Content, "AdminRequestServerEdit-integration-1")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); dialog = Dialog(shell)!;
            var password = (PasswordBox)Find(dialog, "AdminRequestServerApiKey")!;
            Check(password.Password.Length == 0, "saved API key is never read into the connection editor");
            password.Password = "synthetic-replacement";
            Click(Find(dialog, "AdminRequestServerTest")!);
            await Until(() => Walk(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Connected")));
            Check(server.Calls.Count(call => call == "POST /api/v2/admin/request-integrations/integration-1/options") == 1, "connection test is one explicit probe");
            await CaptureAsync(dialog, "admin-requests-server-editor.png");
            dialog.Hide(); await ClosedAsync(shell, dialog);
            Check(password.Password.Length == 0, "closing the server editor clears the unsaved secret");
            Check(((IAdminNavigationGuard)CurrentPage(shell)).RequiresLeaveConfirmation, "server editor does not discard the staged General draft");
            var mode = (ComboBox)Find(window.Content, "AdminRequestRoutingMode")!;
            mode.SelectedItem = mode.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "advanced"));
            await Until(() => Find(window.Content, "AdminRequestRuleEdit-fallback-movie") != null);
            Click(Find(window.Content, "AdminRequestRuleEdit-fallback-movie")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); dialog = Dialog(shell)!;
            Check(Find(dialog, "AdminRequestDestination-hd") != null && Find(dialog, "AdminRequestDestination-uhd") != null, "Everything else exposes both quality destinations");
            Check(Find(dialog, "AdminRequestRuleName") == null, "fallback cannot gain rule conditions or a custom name");
            dialog.Hide(); await ClosedAsync(shell, dialog);
            var search = (TextBox)Find(window.Content, "AdminRequestPreviewSearch")!; search.Text = "Synthetic";
            Click(Find(window.Content, "AdminRequestPreviewSearchGo")!);
            await Until(() => Find(window.Content, "AdminRequestPreviewTitle-123") != null);
            Click(Find(window.Content, "AdminRequestPreviewTitle-123")!);
            await Until(() => server.Calls.Contains("POST /api/v2/admin/request-routes/preview"));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(760, 900)); await Task.Delay(150);
            await CaptureAsync(frame, "admin-requests-settings-compact.png");
            Click(Find(window.Content, "AdminRequestServerEdit-integration-1")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); dialog = Dialog(shell)!;
            password = (PasswordBox)Find(dialog, "AdminRequestServerApiKey")!; password.Password = "synthetic-retired";
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "user" });
            await Until(() => Find(window.Content, "AdminRequestsEnabled") == null);
            await Until(() => !dialog.IsLoaded);
            Check(password.Password.Length == 0, "authority retirement clears a mounted secret");
            Check(!Walk(window.Content).OfType<PasswordBox>().Any(), "authority retirement clears credential controls");
            server.Routing = false; server.Discover = false; server.Watchlist = false;
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
            frame.Navigate(typeof(Page)); await Until(() => !shell.IsLoaded);
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.RequestSettings)), "a fresh authority can reopen Requests settings");
            await Until(() => Find(frame, "AdminRequestServerEdit-integration-1") != null);
            Check(Find(frame, "AdminRequestsWatchlist") == null, "older server without watchlist requests hides the optional setting");
            Check(Find(frame, "AdminRequestRoutingMode") == null, "server without routing hides the complete routing group");
            shell = (AdminShellPage)frame.Content;
            var layoutsBefore = server.Calls.Count(call => call.EndsWith("request-discover/layout"));
            shell.Navigate(new AdminRouteRequest(AdminRoute.RequestDiscover));
            await Until(() => Find(frame, "AdminRequestsNotice") is TextBlock message && message.Text.Contains("Discover", StringComparison.OrdinalIgnoreCase));
            Check(Find(frame, "AdminDiscoverAdd") == null && server.Calls.Count(call => call.EndsWith("request-discover/layout")) == layoutsBefore, "Discover capability absence performs no layout reads and offers no writes");
        }
        finally
        {
            var page = frame.Content is AdminShellPage mounted ? Field<Frame>(mounted, "_frame")?.Content as Page : null;
            var pendingDialog = page == null ? null : Field<Task>(page, "_dialogClosed");
            if (page is IAdminPageSuspension suspension) suspension.SuspendAdminPage();
            frame.Navigate(typeof(Page));
            window.Content = oldContent;
            if (pendingDialog != null) await pendingDialog;
            if (page != null) await Until(() => !page.IsLoaded);
            await Until(() => !frame.IsLoaded);
            field.SetValue(null, oldServices);
        }
    }

    private static void Check(bool value, string label) { Program.Log((value ? "PASS: " : "FAIL: ") + label); if (!value) throw new InvalidOperationException(label); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 240 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Admin Requests fixture readiness."); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static FrameworkElement? Find(DependencyObject root, string id) => Walk(root).OfType<FrameworkElement>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);
    private static void Click(FrameworkElement button) => ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static T? Field<T>(object owner, string name) where T : class => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner) as T;
    private static Page CurrentPage(AdminShellPage shell) => (Page)Field<Frame>(shell, "_frame")!.Content;
    private static ContentDialog? Dialog(AdminShellPage shell) => Field<ContentDialog>(CurrentPage(shell), "_dialog");
    private static async Task ClosedAsync(AdminShellPage shell, ContentDialog dialog) { var pending = Field<Task>(CurrentPage(shell), "_dialogClosed"); if (pending != null) await pending; await Until(() => !dialog.IsLoaded); }
    private static void ClickPrimary(ContentDialog dialog) => Click(Walk(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(150); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
