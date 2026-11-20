using System.Collections.Concurrent;
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

/// <summary>
/// Drives Manage › Autoscan › Settings (official Silo-Server/silo-server 66b836f82) through the production registration,
/// AdminShellPage and nested Frame against a synthetic server: loading, load failures, empty list, populated table,
/// polling save (refused, unknown outcome, applied with a reschedule warning), every dialog, and refused, failed and
/// unknown-outcome connection writes. Every credential is an obviously synthetic value.
/// </summary>
internal static class AdminAutoscanSettingsNativeFixture
{
    private sealed class Server : HttpMessageHandler
    {
        internal bool Enabled = true, BlockDelete, LoseDelete, LoseWrite, FailTest, EchoKey, FailServers, NoServers, RefuseWrite, LoseSettings, FailSettingsRead, FailConnectionsRead, EmptyConnections;
        internal int Interval = 300, Debounce = 30, Writes, Tests;
        internal string Reschedule = "applied";
        internal readonly ConcurrentQueue<string> Requests = new();
        internal string? LastWrite, LastTest;
        internal readonly Dictionary<string, Dictionary<string, object?>> Connections = new()
        {
            ["own"] = Row("own", "My Sonarr", "sonarr", "http://localhost:8989", null, true),
            ["linked"] = Row("linked", "Movies", "radarr", null, "movies", false),
            ["missing-key"] = Row("missing-key", "Needs a key", "sonarr", "http://localhost:8989", null, false),
            ["odd-kind"] = Row("odd-kind", "Odd kind", "lidarr", "sonarr.internal:8989/path", null, true)
        };
        private static Dictionary<string, object?> Row(string id, string name, string kind, string? url, string? integration, bool key) => new()
            { ["id"] = id, ["name"] = name, ["kind"] = kind, ["base_url"] = url, ["request_integration_id"] = integration, ["has_api_key"] = key };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestPath = request.RequestUri!.AbsolutePath; Requests.Enqueue(request.Method + " " + requestPath);
            if (request.Method == HttpMethod.Get)
            {
                if (requestPath == "/api/v2/admin/autoscan/settings")
                    return FailSettingsRead ? Json(new { code = "dependency_unavailable" }, HttpStatusCode.ServiceUnavailable) : Json(new { enabled = Enabled, default_poll_interval_seconds = Interval, debounce_seconds = Debounce });
                if (requestPath == "/api/v2/admin/autoscan/connections")
                    return FailConnectionsRead ? Json(new { code = "dependency_unavailable" }, HttpStatusCode.ServiceUnavailable)
                        : Json(new { items = EmptyConnections ? Array.Empty<object>() : Connections.Values.ToArray(), page = new { has_more = false } });
                if (requestPath == "/api/v2/admin/request-integrations") return FailServers ? Json(new { code = "permission_denied" }, HttpStatusCode.Forbidden)
                    : Json(new { items = NoServers ? Array.Empty<object>() : new object[] { new { id = "movies" } }, page = new { has_more = false } });
                if (requestPath == "/api/v2/admin/request-integrations/movies")
                {
                    var response = Json(new { id = "movies", name = "Movies", kind = "sonarr", plugin_config = new { service_kind = "radarr" }, api_key_ref = "synthetic-never-display" });
                    response.Headers.ETag = new("\"requests-one\""); return response;
                }
                return Json(new { });
            }
            if (requestPath == "/api/v2/admin/autoscan/connections/test")
            {
                Tests++; LastTest = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (!FailTest) return Json(new { ok = true, version = "1.2" });
                using var attempt = JsonDocument.Parse(LastTest);
                var echoed = EchoKey && attempt.RootElement.TryGetProperty("api_key_ref", out var typed) ? " The key " + typed.GetString() + " was refused." : "";
                return Json(new { ok = false, error = "Connection could not be verified. Check its configuration and access." + echoed });
            }
            Writes++; LastWrite = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Check(!request.Headers.IfMatch.Any(), "Autoscan commands never invent If-Match");
            if (request.Method == HttpMethod.Delete)
            {
                var target = requestPath.Split('/').Last();
                if (BlockDelete) return Problem("Connection is referenced by a scan source.", HttpStatusCode.UnprocessableEntity);
                Connections.Remove(target);
                return LoseDelete ? Json(new { code = "dependency_unavailable" }, HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.NoContent);
            }
            using var data = JsonDocument.Parse(LastWrite!); var input = data.RootElement;
            if (requestPath == "/api/v2/admin/autoscan/settings")
            {
                if (RefuseWrite) { RefuseWrite = false; return Problem("Invalid autoscan interval or debounce.", HttpStatusCode.UnprocessableEntity); }
                Enabled = input.GetProperty("enabled").GetBoolean(); Interval = input.GetProperty("default_poll_interval_seconds").GetInt32(); Debounce = input.GetProperty("debounce_seconds").GetInt32();
                if (LoseSettings) { LoseSettings = false; return Json(new { code = "dependency_unavailable" }, HttpStatusCode.ServiceUnavailable); }
                return Json(new { settings = new { enabled = Enabled, default_poll_interval_seconds = Interval, debounce_seconds = Debounce }, reschedule_state = Reschedule });
            }
            if (RefuseWrite)
            {
                RefuseWrite = false;
                return Problem("Rejected base URL" + (input.TryGetProperty("api_key_ref", out var typed) ? " (key " + typed.GetString() + ")" : "") + ".", HttpStatusCode.UnprocessableEntity);
            }
            var id = request.Method == HttpMethod.Post ? "created-" + Writes : requestPath.Split('/').Last();
            var hasSavedKey = Connections.TryGetValue(id, out var existing) && existing["has_api_key"] is true;
            var row = Row(id, input.GetProperty("name").GetString()!, input.GetProperty("kind").GetString()!,
                input.TryGetProperty("base_url", out var url) ? url.GetString() : null,
                input.TryGetProperty("request_integration_id", out var linked) ? linked.GetString() : null,
                input.TryGetProperty("api_key_ref", out _) || hasSavedKey);
            Connections[id] = row;
            if (LoseWrite) { LoseWrite = false; return Json(new { code = "dependency_unavailable" }, HttpStatusCode.ServiceUnavailable); }
            return Json(row, request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK);
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Problem(string detail, HttpStatusCode status) =>
            Json(new { type = "https://fixture.invalid/docs/api/v2/problems/validation_failed", title = "Validation failed", detail }, status);
    }

    internal static async Task RunAsync(Window window)
    {
        using var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "fixture-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var navigation = new NavigationService();
        var registrations = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(navigation)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        registrations.AddAdminAutoscanSettings();
        await using var services = registrations.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = servicesField.GetValue(null); var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null; AdminShellPage? shell = null; AdminAutoscanSettingsPage? page = null;
        try
        {
            servicesField.SetValue(null, services); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.TryOpenHref("/admin/settings/autoscan") is { Kind: AdminRouteResolutionKind.Route }, "production route is registered and reachable");
            await Until(() => outer.Content is AdminShellPage, "shell Frame activation"); shell = (AdminShellPage)outer.Content;
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminAutoscanSettingsPage, "Autoscan settings Frame activation"); page = (AdminAutoscanSettingsPage)inner.Content;
            await Until(() => page.Controller?.State is { Loading: false, ConnectionsLoaded: true, ServersLoaded: true }, "owned bounded reads");
            var controller = page.Controller!;
            await Until(() => Text(page).Contains("Polling defaults") && Text(page).Contains("Reused from Requests"), "state rendered into the page");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "in-place navigation has no slide");
            Check(Descendants(page).OfType<ToggleSwitch>().Count() == 0 && !Text(page).Contains("Run now"), "global enable and Run now stay on Sources");
            Check(Text(page).Contains("How often Autoscan checks polled sources, and the servers its sources connect to."), "official settings page subtitle");
            Check(Text(page).Contains("Polling defaults") && Text(page).Contains("Used by sources that don't set their own."), "official polling group and description");
            Check(Text(page).Contains("Default check interval") && Text(page).Contains("Debounce") && Text(page).Contains("Skips repeat reports of an unchanged file within this window."), "official polling rows");
            Check(Text(page).Contains("Connections") && Text(page).Contains("Reused from Requests") && Text(page).Contains("Own"), "official connection columns and source badges");
            Check(Text(page).Contains("http://localhost:8989") && Text(page).Contains("sonarr.internal:8989/path") && Text(page).Contains("lidarr"), "stored URLs and kinds that fail a client check stay displayed");
            Check(!Text(page).Contains("synthetic-never-display"), "canonical descriptors never expose credentials");
            Check(ButtonOf(page, "AutoscanConnectionAdd") is { IsEnabled: true }, "Add connection sits in the connections group header");
            Check(!Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AutoscanSettingsSave" && button.IsLoaded && Visible(button)), "save bar is hidden while nothing is dirty");

            // Polling defaults: the official Number(value) || fallback coercion, then refused, unknown-outcome and applied saves.
            Input(page, "AutoscanInterval").Text = "abc"; await Until(() => Input(page, "AutoscanInterval").Text == "300", "a non-numeric interval reads 300");
            Input(page, "AutoscanDebounce").Text = ""; await Until(() => Input(page, "AutoscanDebounce").Text == "0" && controller.State.Dirty, "an empty debounce reads 0");
            Input(page, "AutoscanDebounce").Text = "30"; await Until(() => !controller.State.Dirty, "restoring the saved values clears the draft");
            Input(page, "AutoscanInterval").Text = "600"; Input(page, "AutoscanDebounce").Text = "0";
            await Until(() => controller.State.Dirty && Text(page).Contains("1 unsaved change") || Text(page).Contains("2 unsaved changes"), "dirty rows feed the save bar count");
            server.RefuseWrite = true; var writes = server.Writes;
            await Click(page, "AutoscanSettingsSave"); await Until(() => !controller.State.Busy && controller.State.Error != null, "refused polling save");
            await Until(() => Text(page).Contains("Invalid autoscan interval or debounce."), "refusal shows the server's reason");
            Check(server.Writes == writes + 1 && server.Interval == 300 && controller.State is { Dirty: true, NeedsReview: false } && Input(page, "AutoscanInterval").Text == "600", "refused save keeps the draft and allows a corrected retry");
            Check(ButtonOf(page, "AutoscanSettingsSave") is { IsEnabled: true }, "Save stays available after a refusal");
            server.Enabled = false; server.Reschedule = "failed";
            await Click(page, "AutoscanSettingsSave"); await Until(() => !controller.State.Busy && !controller.State.Dirty, "polling acknowledgement");
            Check(!server.Enabled && server.Interval == 600 && server.Debounce == 0, "latest enabled value is preserved while defaults change");
            using (var body = JsonDocument.Parse(server.LastWrite!)) Check(body.RootElement.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(["debounce_seconds", "default_poll_interval_seconds", "enabled"]), "settings body carries exactly the official three fields");
            await Until(() => Text(page).Contains("Settings saved, but poll-task rescheduling failed."), "saved but not rescheduled warning");
            await Capture(page, "admin-autoscan-settings-wide.png");

            Input(page, "AutoscanInterval").Text = "700"; server.LoseSettings = true; writes = server.Writes;
            await Click(page, "AutoscanSettingsSave"); await Until(() => controller.State.NeedsReview && !controller.State.Busy, "unknown polling outcome blocks writes");
            await Until(() => Text(page).Contains("Settings persistence could not be confirmed. Reload settings before submitting again."), "unknown outcome uses the official unconfirmed text");
            Check(server.Writes == writes + 1 && controller.State.OutcomeUnknown && !Input(page, "AutoscanInterval").IsEnabled, "unknown outcome is sent once and the fields lock");
            await Capture(page, "admin-autoscan-settings-unknown.png");
            await Click(page, "AutoscanSettingsReload"); await Until(() => controller.State is { Loading: false, NeedsReview: false, Saved: not null }, "explicit settings reload");
            Check(controller.State.Saved!.DefaultPollIntervalSeconds == 700 && !controller.State.Dirty && server.Writes == writes + 1 && Input(page, "AutoscanInterval").Text == "700", "reload shows the server's canonical value without replaying the write");
            server.Reschedule = "applied";

            // Failed reads stay visible and recoverable.
            server.FailSettingsRead = true; await controller.ReloadAsync(); await Until(() => controller.State is { Loading: false, Saved: null }, "failed settings read");
            await Until(() => Text(page).Contains("Autoscan settings could not be loaded. Reload before editing."), "official settings load error");
            Check(ButtonOf(page, "AutoscanSettingsReload") is { IsEnabled: true } && controller.State.ConnectionsLoaded, "the connections list still loads and Reload is reachable");
            server.FailSettingsRead = false; await Click(page, "AutoscanSettingsReload"); await Until(() => controller.State is { Loading: false, Saved: not null }, "settings recovery");
            server.FailConnectionsRead = true; await controller.ReloadAsync(); await Until(() => controller.State is { Loading: false, ConnectionsLoaded: false }, "failed connections read");
            await Until(() => Text(page).Contains("Connections could not be loaded. Reload before editing."), "connections load error is shown, not an empty list");
            Check(!Text(page).Contains("No connections yet.") && ButtonOf(page, "AutoscanConnectionAdd") is { IsEnabled: false }, "a failed list is not presented as empty and cannot be edited");
            server.FailConnectionsRead = false; server.EmptyConnections = true; await controller.ReloadAsync();
            await Until(() => controller.State is { Loading: false, ConnectionsLoaded: true } && Text(page).Contains("No connections yet."), "official empty connections state");
            server.EmptyConnections = false; await controller.ReloadAsync(); await Until(() => controller.State is { Loading: false, ConnectionsLoaded: true, ServersLoaded: true } && controller.State.Connections.Count == 4, "recovered connections");

            await Click(page, "AutoscanConnectionAdd"); var dialog = await Dialog(page);
            var mode = Select(dialog, "AutoscanConnectionType"); Check(Value(mode) == "reuse", "create defaults to existing Requests server");
            Check(Value(Select(dialog, "AutoscanRequestServer")) == "movies" && Select(dialog, "AutoscanRequestServer").PlaceholderText == "Select a server…", "reuse selector starts on the first Requests server and keeps the official placeholder");
            Check(controller.State.Editor?.IntegrationId == "movies", "request server selection");
            Check(Text(dialog).Contains("Reuse a server you already configured in Requests (Sonarr/Radarr), or enter your own credentials."), "official dialog description");
            Check(Select(dialog, "AutoscanRequestServer").Items.OfType<ComboBoxItem>().Any(item => item.Content as string == "Movies (Radarr)"), "reuse option uses canonical service_kind and the official label");
            await Capture(dialog, "admin-autoscan-connection-reuse.png");
            await Click(dialog, "AutoscanConnectionTest"); await Until(() => controller.State.TestResult != null, "reuse advisory check");
            await Until(() => Text(dialog).Contains("Connected (v1.2)"), "successful advisory check shows the version");
            using (var body = JsonDocument.Parse(server.LastTest!)) Check(body.RootElement.GetProperty("request_integration_id").GetString() == "movies", "new reuse test uses selected integration");
            await Primary(dialog); await Closed(page); Check(server.Connections.Values.Count(row => row["request_integration_id"] as string == "movies") == 2, "reuse creates saved connection");
            using (var body = JsonDocument.Parse(server.LastWrite!)) Check(body.RootElement.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(["kind", "name", "request_integration_id"]) && body.RootElement.GetProperty("kind").GetString() == "radarr", "linked create sends name, kind and the integration only");

            await Click(page, "AutoscanConnectionAdd"); dialog = await Dialog(page); mode = Select(dialog, "AutoscanConnectionType"); Pick(mode, "own");
            await Until(() => controller.State.Editor?.Reuse == false, "manual credential state");
            Input(dialog, "AutoscanConnectionName").Text = "New manual"; Input(dialog, "AutoscanConnectionUrl").Text = "http://manual:8989";
            var key = Secret(dialog); key.Password = "synthetic-new-key";
            await Until(() => controller.State.Editor?.CanSave == true, "new manual requires key");
            server.FailTest = true; server.EchoKey = true;
            await Click(dialog, "AutoscanConnectionTest"); await Until(() => controller.State.TestResult is { Ok: false } && dialog.IsPrimaryButtonEnabled, "failed advisory check never blocks Save");
            await Until(() => Text(dialog).Contains("Connection could not be verified. Check its configuration and access.") && Text(dialog).Contains("<redacted>"), "failed check shows the server reason");
            Check(!Text(dialog).Contains("synthetic-new-key"), "failed check shows the typed key redacted");
            server.FailTest = false; server.EchoKey = false;
            await Click(dialog, "AutoscanConnectionTest"); await Until(() => controller.State.TestResult is { Ok: true }, "manual test");
            using (var body = JsonDocument.Parse(server.LastTest!)) Check(body.RootElement.TryGetProperty("api_key_ref", out _), "new credentials are write-only in test body");
            // A refused save keeps the dialog and the typed key; the corrected retry still sends it.
            server.RefuseWrite = true; var before = server.Writes; await Primary(dialog);
            await Until(() => !controller.State.Busy && controller.State.Error != null, "refused connection save");
            await Until(() => Text(dialog).Contains("Rejected base URL (key <redacted>)."), "refusal shows the server reason with the key redacted");
            Check(server.Writes == before + 1 && !controller.State.NeedsReview && key.Password == "synthetic-new-key" && dialog.IsPrimaryButtonEnabled && !Text(dialog).Contains("synthetic-new-key"), "refused create keeps the dialog, the typed key and a usable Save");
            Check(!server.Connections.Values.Any(row => row["name"] as string == "New manual"), "a refused create saved nothing");
            await Capture(dialog, "admin-autoscan-connection-refused.png");
            Input(dialog, "AutoscanConnectionUrl").Text = "http://manual:8990"; await Until(() => controller.State.Error == null, "editing after a refusal starts a new attempt");
            server.LoseWrite = true; await Primary(dialog);
            await Until(() => controller.State.NeedsReview && !dialog.IsPrimaryButtonEnabled, "unknown create locks mutation");
            using (var body = JsonDocument.Parse(server.LastWrite!)) Check(body.RootElement.GetProperty("api_key_ref").GetString() == "synthetic-new-key" && body.RootElement.GetProperty("base_url").GetString() == "http://manual:8990", "the corrected retry sends the typed key and the corrected URL");
            Check(server.Writes == before + 2 && !dialog.IsPrimaryButtonEnabled, "unknown create is sent once and cannot replay");
            await Until(() => key.Password.Length == 0, "unknown outcome clears visible key");
            Check(Text(dialog).Contains("Connection creation could not be confirmed. Refresh connections before submitting again."), "unknown create uses the official unconfirmed text");
            await Capture(dialog, "admin-autoscan-connection-unknown.png");
            await Click(dialog, "AutoscanConnectionReconcile"); await Closed(page); await Until(() => controller.State is { Loading: false, NeedsReview: false }, "explicit readback reconciliation");
            Check(server.Writes == before + 2 && controller.State.Connections.Any(row => row.Name == "New manual"), "reconciliation reads the saved create without duplicating");

            await Click(page, "AutoscanEdit_own"); dialog = await Dialog(page); key = Secret(dialog);
            Check(key.Password == "" && key.PlaceholderText == "••••••••" && Text(dialog).Contains("(leave blank to keep existing)"), "saved-key editor is write-only with keep hint");
            await Capture(dialog, "admin-autoscan-connection-edit.png");
            Check(!Visible(Select(dialog, "AutoscanConnectionType")), "edit does not change credential ownership mode");
            Input(dialog, "AutoscanConnectionName").Text = "Renamed"; server.FailTest = true;
            await Click(dialog, "AutoscanConnectionTest"); await Until(() => controller.State.TestResult is { Ok: false } && dialog.IsPrimaryButtonEnabled, "advisory failed saved check");
            using (var body = JsonDocument.Parse(server.LastTest!)) Check(body.RootElement.GetProperty("connection_id").GetString() == "own", "blank key tests saved record by id");
            await Until(() => Text(dialog).Contains("Connection could not be verified.") && dialog.IsPrimaryButtonEnabled, "failed check shows the server reason and does not block save");
            server.FailTest = false;
            await Primary(dialog); await Closed(page); using (var body = JsonDocument.Parse(server.LastWrite!)) Check(!body.RootElement.TryGetProperty("api_key_ref", out _), "blank edit omits credential and keeps saved key");

            await Click(page, "AutoscanEdit_missing-key"); dialog = await Dialog(page); Check(!dialog.IsPrimaryButtonEnabled, "record without saved key requires replacement");
            Check(Secret(dialog).PlaceholderText == "Enter API key", "missing saved credential has no keep mask"); dialog.Hide(); await Closed(page);
            await Click(page, "AutoscanEdit_odd-kind"); dialog = await Dialog(page);
            Check(Input(dialog, "AutoscanConnectionUrl").Text == "sonarr.internal:8989/path" && Value(Select(dialog, "AutoscanConnectionKind")) == "lidarr" && dialog.IsPrimaryButtonEnabled, "stored kind and URL that fail a client check stay in the editor and remain savable");
            Input(dialog, "AutoscanConnectionName").Text = "Odd renamed"; await Primary(dialog); await Closed(page);
            using (var body = JsonDocument.Parse(server.LastWrite!)) Check(body.RootElement.GetProperty("kind").GetString() == "lidarr" && body.RootElement.GetProperty("base_url").GetString() == "sonarr.internal:8989/path", "an unrelated edit rewrites neither the stored kind nor the stored URL");
            await Click(page, "AutoscanEdit_linked"); dialog = await Dialog(page);
            Check(controller.State.Editor!.Reuse && Text(dialog).Contains("Request server"), "linked edit exposes request-server selection");
            await Click(dialog, "AutoscanConnectionTest"); await Until(() => !controller.State.Testing, "linked saved check");
            using (var body = JsonDocument.Parse(server.LastTest!)) Check(body.RootElement.GetProperty("connection_id").GetString() == "linked", "linked edit checks saved id, not draft integration");
            dialog.Hide(); await Closed(page);

            // Delete: a definite refusal keeps the row and allows a retry; an unknown outcome locks until Reload.
            server.BlockDelete = true; before = server.Writes;
            await Click(page, "AutoscanDelete_own"); dialog = await Dialog(page);
            Check(Text(dialog).Contains("Reconfigure those sources first.") && Text(dialog).Contains("Delete connection?"), "delete confirmation explains source bindings");
            await Capture(dialog, "admin-autoscan-connection-delete.png");
            await Primary(dialog); await Until(() => !controller.State.Busy && controller.State.Error != null, "refused delete");
            await Until(() => Text(dialog).Contains("Connection is referenced by a scan source."), "refused delete shows the server reason");
            Check(server.Writes == before + 1 && !controller.State.NeedsReview && server.Connections.ContainsKey("own") && dialog.IsPrimaryButtonEnabled, "source-bound delete keeps the row and may be retried");
            server.BlockDelete = false; await Primary(dialog); await Closed(page);
            Check(!server.Connections.ContainsKey("own") && controller.State.Connections.All(row => row.Id != "own"), "explicit confirmed delete removes record after 204");
            server.LoseDelete = true; before = server.Writes;
            await Click(page, "AutoscanDelete_linked"); dialog = await Dialog(page); await Primary(dialog);
            await Until(() => controller.State.NeedsReview && !controller.State.Busy, "unknown delete requires review");
            await Until(() => !dialog.IsPrimaryButtonEnabled && Text(dialog).Contains("Connection deletion could not be confirmed."), "unknown delete locks with the official text");
            Check(server.Writes == before + 1 && controller.State.OutcomeUnknown, "unknown delete is sent once");
            await Click(dialog, "AutoscanDeleteReconcile"); await Closed(page); await Until(() => controller.State is { Loading: false, NeedsReview: false }, "delete readback");
            Check(controller.State.Connections.All(row => row.Id != "linked") && server.Writes == before + 1, "reload shows the canonical list without replaying the delete");
            server.LoseDelete = false;

            server.FailServers = true; await controller.ReloadAsync(); await Until(() => !controller.State.Loading, "request dependency failure");
            await Click(page, "AutoscanConnectionAdd"); dialog = await Dialog(page);
            Pick(Select(dialog, "AutoscanConnectionType"), "reuse");
            await Until(() => Text(dialog).Contains("Request servers could not be loaded."), "reuse dependency error is rendered");
            Check(!controller.State.ServersLoaded && Text(dialog).Contains("Request servers could not be loaded."), "request read error differs from no servers");
            dialog.Hide(); await Closed(page); server.FailServers = false; server.NoServers = true; await controller.ReloadAsync();
            await Click(page, "AutoscanConnectionAdd"); dialog = await Dialog(page);
            Check(controller.State.Editor is { Reuse: false }, "no reusable servers defaults to manual setup");
            Check(Select(dialog, "AutoscanConnectionType").Items.OfType<ComboBoxItem>().Any(item => item.Tag as string == "reuse" && !item.IsEnabled), "reuse option is disabled without Requests servers");
            controller.EditConnection(controller.State.Editor! with { Reuse = true });
            await Until(() => Text(dialog).Contains("No Sonarr or Radarr servers yet. Add one under"), "empty Requests setup instructions");
            Check(Descendants(dialog).OfType<HyperlinkButton>().Any(button => AutomationProperties.GetAutomationId(button) == "AutoscanRequestsSetup"), "empty reuse state offers Settings › Requests setup link");
            dialog.Hide(); await Closed(page);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(720, 900)); await Capture(page, "admin-autoscan-settings-narrow.png");
            await Click(page, "AutoscanConnectionAdd"); dialog = await Dialog(page); Pick(Select(dialog, "AutoscanConnectionType"), "own"); key = Secret(dialog); key.Password = "synthetic-retiring-key";
            var closing = page.DialogClosed;
            auth.SetCurrentUser(new UserInfo { Id = "other", Username = "other", Role = "user" });
            await closing.WaitAsync(TimeSpan.FromSeconds(8)); await Until(() => page.Controller?.State.Owner == null, "authority retirement");
            Check(key.Password == "" && controller.State.Editor == null && controller.State.Connections.Count == 0, "retirement clears dialog, secrets and identity-owned reads");
            Check(server.Requests.All(request => !request.EndsWith("/api/v2/admin/settings") && !request.Contains("/autoscan/trigger")), "settings never writes generic server settings or triggers scans");
        }
        finally
        {
            var pageUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var shellUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (page?.IsLoaded == true) page.Unloaded += (_, _) => pageUnloaded.TrySetResult(); else pageUnloaded.TrySetResult();
            if (shell?.IsLoaded == true) shell.Unloaded += (_, _) => shellUnloaded.TrySetResult(); else shellUnloaded.TrySetResult();
            page?.SuspendAdminPage(); var dialogsClosed = page?.DialogClosed ?? Task.CompletedTask;
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            await Task.WhenAll(pageUnloaded.Task, shellUnloaded.Task, dialogsClosed).WaitAsync(TimeSpan.FromSeconds(8));
            Check(page?.IsLoaded != true && shell?.IsLoaded != true, "real shell and page unload barriers precede service restoration");
            window.Content = previousContent; servicesField.SetValue(null, previousServices);
        }
    }

    private static void Check(bool condition, string label)
    { Program.Log((condition ? "PASS: " : "FAIL: ") + label); if (!condition) throw new InvalidOperationException(label); }
    private static async Task Until(Func<bool> predicate, string label)
    { for (var attempt = 0; attempt < 240 && !predicate(); attempt++) await Task.Delay(25); if (!predicate()) throw new InvalidOperationException("Timed out: " + label); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child; }
    private static bool Visible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }
    private static string Text(DependencyObject root) => string.Join("\n", Descendants(root).OfType<TextBlock>().Where(Visible).Select(text => text.Text));
    private static TextBox Input(DependencyObject root, string id) => Descendants(root).OfType<TextBox>().Single(input => AutomationProperties.GetAutomationId(input) == id);
    private static ComboBox Select(DependencyObject root, string id) => Descendants(root).OfType<ComboBox>().Single(input => AutomationProperties.GetAutomationId(input) == id);
    private static Button ButtonOf(DependencyObject root, string id) => Descendants(root).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id);
    private static PasswordBox Secret(DependencyObject root) => Descendants(root).OfType<PasswordBox>().Single();
    private static string Value(ComboBox select) => (select.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private static void Pick(ComboBox select, string value) => select.SelectedItem = select.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == value);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Click(DependencyObject root, string id)
    {
        await Until(() => Descendants(root).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == id && button.IsEnabled && button.IsLoaded), id);
        Invoke(Descendants(root).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id));
    }
    private static async Task Primary(ContentDialog dialog)
    {
        await Until(() => dialog.IsPrimaryButtonEnabled && Descendants(dialog).OfType<Button>().Any(button => button.Name == "PrimaryButton" && button.IsLoaded), "dialog primary action");
        Invoke(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
    }
    private static async Task<ContentDialog> Dialog(AdminAutoscanSettingsPage page)
    {
        var field = typeof(AdminAutoscanSettingsPage).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Until(() => field.GetValue(page) is ContentDialog { IsLoaded: true }, "connection dialog loaded"); return (ContentDialog)field.GetValue(page)!;
    }
    private static async Task Closed(AdminAutoscanSettingsPage page) { await page.DialogClosed.WaitAsync(TimeSpan.FromSeconds(8)); await Until(() => page.Controller?.State.Editor == null, "editor closed"); }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(100); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
