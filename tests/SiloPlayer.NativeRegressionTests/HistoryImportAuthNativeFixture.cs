using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class HistoryImportAuthNativeFixture
{
    internal static async Task RunAsync()
    {
        var failures = new List<string>();
        foreach (var flow in new[] { "emby", "pin", "check" })
        foreach (var change in new[] { "read", "account", "profile", "server" })
        foreach (var reject in new[] { false, true })
        {
            var name = $"{flow}-{change}-{(reject ? "failure" : "success")}";
            try { await CaseAsync(flow, change, reject); Program.Log($"PASS: HISTORY_IMPORT_AUTH {name}"); }
            catch (Exception ex) { failures.Add(name); Program.Log($"FAIL: HISTORY_IMPORT_AUTH {name}: {ex.Message}"); }
        }
        if (failures.Count > 0) throw new InvalidOperationException("History import auth cases failed: " + string.Join(", ", failures));
    }

    private static async Task CaseAsync(string flow, string change, bool reject)
    {
        using var handler = new Handler(flow, reject);
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://history-import-fixture.invalid"); client.SetProfile("fixture-one");
        var api = new SettingsApi(client);
        using var auth = new AuthService(client, new AuthApi(client));
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "history-import-settings", Guid.NewGuid().ToString("N")));
        var theme = new ThemeService(settings, api);
        var launches = 0;
        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client),
            new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme), _ => launches++);
        var operation = flow == "emby" ? vm.EmbyConnectLoginCommand.ExecuteAsync(null) : vm.PlexAuthStartCommand.ExecuteAsync(null);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Check(flow == "emby" ? vm.EmbyConnectLoginPending : vm.PlexAuthPending, "Pending state cleared before response.");
        switch (change)
        {
            case "read": client.InvalidateAccessContext(); break;
            case "account": client.BeginAuthenticationSession("synthetic-new-account"); break;
            case "profile": client.SetProfile("fixture-two"); break;
            case "server": client.SetBaseUrl("https://other-history-import-fixture.invalid"); break;
        }
        // A new owner's visible errors must not be overwritten by an obsolete response.
        if (change != "read") { vm.ErrorMessage = "new-owner-error"; vm.PlexAuthError = "new-owner-error"; }
        handler.Release.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(8));
        Check(!vm.EmbyConnectLoginPending && !vm.PlexAuthPending, "Pending state was not released.");
        if (change != "read")
        {
            Check(vm.EmbyConnectSessionId == null && vm.EmbyConnectServers.Count == 0 && vm.PlexOAuthServers.Count == 0,
                "Obsolete identity populated authorization results.");
            Check(vm.ErrorMessage == "new-owner-error" && vm.PlexAuthError == "new-owner-error", "Obsolete error replaced new owner's error.");
            Check(flow != "pin" || (launches == 0 && handler.Checks == 0 && vm.PlexSessionId == null), "Obsolete PIN launched browser or polled.");
            Check(flow != "check" || handler.Checks == 1, "Obsolete check issued another poll.");
        }
        else if (reject)
        {
            var error = flow == "emby" ? vm.ErrorMessage : vm.PlexAuthError;
            Check(!string.IsNullOrEmpty(error), "Read invalidation suppressed the current owner's failure.");
        }
        else if (flow == "emby")
        {
            Check(vm.EmbyConnectSessionId == "fixture-emby-session" && vm.EmbyConnectServers.Count == 1,
                "Read invalidation discarded current Emby authorization.");
        }
        else
        {
            Check(vm.PlexSessionId == "fixture-plex-session" && vm.PlexOAuthServers.Count == 1 && vm.PlexAuthStatus == "Connected",
                "Read invalidation aborted current Plex authorization.");
            Check(launches == 1 && handler.Checks == 1, "Plex approval launch/poll count is incorrect.");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Handler(string flow, bool reject) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Checks;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var stage = path.EndsWith("/login") ? "emby" : path.EndsWith("/pin") ? "pin" : path.EndsWith("/check") ? "check" : throw new InvalidOperationException("Unexpected fixture route.");
            if (stage == "check") Checks++;
            if (stage == flow) { Started.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (stage == flow && reject)
                return Json(HttpStatusCode.BadGateway, "{\"title\":\"Synthetic login rejection\"}");
            return Json(HttpStatusCode.OK, stage switch
            {
                "emby" => "{\"connect_session_id\":\"fixture-emby-session\",\"servers\":[{\"server_id\":\"fixture-emby\",\"name\":\"Fixture\"}]}",
                "pin" => "{\"session_id\":\"fixture-plex-session\",\"pin_code\":\"FIXTURE\",\"auth_url\":\"https://approval-fixture.invalid\"}",
                _ => "{\"authenticated\":true,\"servers\":[{\"client_identifier\":\"fixture-plex\",\"name\":\"Fixture\"}]}"
            });
        }
        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
