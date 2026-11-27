using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

/// <summary>
/// Official #5204 on the Settings view model: a refused save shows the last stored value again at once; a save whose outcome is unknown
/// shows what the server holds once the saves have settled; another profile's page is never repainted and a failed read changes nothing.
/// Run by SILO_NATIVE_TEST_ONLY=shared-appearance.
/// </summary>
internal static class SettingSaveReconcileNativeFixture
{
    private enum Put { Ok, Refuse, UnknownApplied, UnknownNotApplied }

    internal static async Task RunAsync()
    {
        var store = new Store();
        using var http = new HttpClient(store);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://reconcile-fixture.invalid");
        client.SetProfile("profile-a");
        var api = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "reconcile-settings"));
        var theme = new ThemeService(settings, api);
        var accessibility = new AccessibilityService(settings, theme);
        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client),
            new AuthService(client, new AuthApi(client)), theme, settings, accessibility);

        // A refused change (422): the stored value shows again at once and the server is not asked what it holds.
        store.Values["ui.text_scale"] = "default"; store.Mode = Put.Refuse;
        await vm.SetAccessibilityAsync(textScale: "large");
        Check(vm.TextScale == "default" && settings.Load().UiTextScale == "default", "a refused text size shows the stored value again, shown and kept locally");
        Check(!string.IsNullOrEmpty(vm.ErrorMessage), "a refused text size still says it was not saved");
        Check(store.Reads == 0, "a refused save makes no re-read");

        // The same for a refused date format (a different key, the local cache and the shown value).
        store.Values["ui.date_format"] = "auto";
        await vm.SetDateFormatAsync("YYYY-MM-DD");
        Check(vm.DateFormat == "auto" && settings.Load().UiDateFormat == "auto", "a refused date format shows the stored value again");

        // Outcome unknown (503), and the server stored it: the re-read shows the stored value.
        store.Mode = Put.UnknownApplied; store.Reads = 0;
        await vm.SetAccessibilityAsync(textWeight: "strong");
        Check(store.Reads >= 1 && vm.TextWeight == "strong" && settings.Load().UiTextWeight == "strong", "an unknown outcome that was stored shows the stored value after the re-read");

        // Outcome unknown and nothing stored: the re-read shows the old value, even though it equals the value before the change.
        store.Mode = Put.UnknownNotApplied; store.Reads = 0;
        await vm.SetAccessibilityAsync(highContrast: true);
        Check(store.Reads >= 1 && !vm.HighContrast && !settings.Load().UiHighContrast, "an unknown outcome that was not stored shows the old value after the re-read");

        // The re-read failed too: nothing is repainted from it.
        store.Mode = Put.UnknownNotApplied; store.ReadFails = true;
        await vm.SetAccessibilityAsync(textScale: "x-large");
        Check(vm.TextScale == "x-large", "a failed re-read leaves the shown value alone");
        store.ReadFails = false;

        // Another profile owns the page when a refusal comes back: its settings are left alone and nothing is read.
        store.Mode = Put.Refuse; store.Reads = 0; store.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.TextScale = "default";
        var save = vm.SetAccessibilityAsync(textScale: "large");
        await Task.Delay(100);
        client.SetProfile("profile-b");
        vm.TextScale = "x-large";
        store.Gate.SetResult();
        await save;
        store.Gate = null;
        Check(vm.TextScale == "x-large" && store.Reads == 0, "a refusal that returns after a profile change does not repaint the other profile");
        Program.Log("Settings save reconcile: refused, unknown outcome stored or not, failed re-read and profile change passed.");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Settings save reconcile failed: " + message);
        Program.Log("PASS: " + message);
    }

    private sealed class Store : HttpMessageHandler
    {
        public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        public Put Mode;
        public int Reads;
        public bool ReadFails;
        public TaskCompletionSource? Gate;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/settings/contract/capabilities") return Json(new { api_version = 1, manifest_revision = 15 });
            if (request.Method == HttpMethod.Put && path.StartsWith("/api/v2/settings/values/", StringComparison.Ordinal))
            {
                var key = Uri.UnescapeDataString(path["/api/v2/settings/values/".Length..]);
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.GetProperty("value");
                if (Gate != null) await Gate.Task;
                var text = body.ValueKind == JsonValueKind.String ? body.GetString()! : body.GetRawText();
                if (Mode is Put.Ok or Put.UnknownApplied) Values[key] = text;
                return Mode switch
                {
                    Put.Ok => Json(new { key, value = body }),
                    Put.Refuse => Json(new { title = "Unprocessable", status = 422, detail = "That value is not allowed." }, HttpStatusCode.UnprocessableEntity),
                    _ => Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable),
                };
            }
            if (request.Method == HttpMethod.Get && path == "/api/v2/settings/values/effective")
            {
                Reads++;
                if (ReadFails) return Json(new { title = "Unavailable", status = 500 }, HttpStatusCode.InternalServerError);
                return Json(new { items = Values.Where(pair => pair.Key.StartsWith("ui.text_", StringComparison.Ordinal) || pair.Key == "ui.high_contrast").Select(pair => Entry(pair.Key, pair.Value)).ToArray(), revision = 1 });
            }
            if (request.Method == HttpMethod.Get && path.StartsWith("/api/v2/settings/values/", StringComparison.Ordinal))
            {
                Reads++;
                var key = Uri.UnescapeDataString(path["/api/v2/settings/values/".Length..]);
                if (ReadFails) return Json(new { title = "Unavailable", status = 500 }, HttpStatusCode.InternalServerError);
                return Json(Entry(key, Values.GetValueOrDefault(key, key == "ui.high_contrast" ? "false" : "auto")));
            }
            return Json(new { });
        }

        private static object Entry(string key, string value) => new { key, value = key == "ui.high_contrast" ? JsonSerializer.Deserialize<JsonElement>(value) : JsonSerializer.SerializeToElement(value), source = "profile" };

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}
