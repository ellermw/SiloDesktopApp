using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Storage;

internal static class ProfileTransferFinalNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ServicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo PickerProperty = typeof(SettingsPage).GetProperty("ProfileTransferPicker", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly List<string> Findings = [];
    private static readonly byte[] Document = Encoding.UTF8.GetBytes("""{"format":"silo-profile-export","version":1,"data":{},"items":[]}""");

    internal static async Task RunAsync(StackPanel parent)
    {
        Program.Log($"BINARY app={typeof(SettingsPage).Assembly.Location}; core={typeof(ProfileTransferApi).Assembly.Location}; fixture={typeof(ProfileTransferFinalNativeFixture).Assembly.Location}");
        await using var ownedShell = EpisodeQuickPlayNativeFixture.CreateOwnedDispatcherShell();
        var outer = SiloPlayer.App.Services;
        var oldPicker = PickerProperty.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://profile-transfer-final.invalid");
        var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SetCurrentUser(new() { Id = "synthetic-account", Role = "user" });
        void ResetOwner() => auth.SelectProfile("synthetic-profile", profile: new() { Id = "synthetic-profile", Name = "Synthetic", HasPin = true, IsPrimary = true });
        ResetOwner();
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "isolated-settings"));
        var preference = settings.Load(); preference.SiloCastEnabled = false; settings.Save(preference);
        var settingsApi = new SettingsApi(client); var catalog = new CatalogApi(client);
        var theme = new ThemeService(settings, settingsApi);
        var customization = new UICustomizationService(settingsApi);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(settings)
            .AddSingleton(new AuthApi(client)).AddSingleton(settingsApi).AddSingleton(catalog)
            .AddSingleton(new ToastService()).AddSingleton(customization).AddSingleton(new CardOverlayService(settingsApi))
            .AddSingleton(outer.GetRequiredService<SiloPlayer.Services.SiloCast.SiloCastReceiverService>())
            .AddTransient(_ => new SettingsViewModel(settingsApi, catalog, new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme)))
            .BuildServiceProvider();
        ServicesField.SetValue(null, services);
        try
        {
            foreach (var scenario in new[] { "export-import", "pin", "pin-import", "non-pin-403", "oversize", "growth", "navigation", "navigation-picker", "navigation-read", "authority-response", "authority-import", "authority-dialog", "authority-verification", "cache-refresh", "near-limit" })
            {
                ResetOwner(); wire.Reset();
                var picker = new Picker(); PickerProperty.SetValue(null, picker);
                var page = new SettingsPage { Width = 950, Height = 750 }; parent.Children.Add(page);
                try
                {
                    await Task.Delay(80);
                    Open(page); await Ready(page);
                    var host = (StackPanel)page.FindName("YourDataContentHost");
                    Button Button(string label) => Descendants<Button>(host).Single(button => button.Content as string == label);
                    if (scenario == "export-import")
                    {
                        Invoke(Button("Export profile data")); await Until(() => picker.Saves == 1 && Busy(page) == 0);
                        var savedBytes = await File.ReadAllBytesAsync(picker.TargetPath!); Require(savedBytes.Length > 0, "Export did not save real bytes");
                        Invoke(Button("Choose file")); await Until(() => wire.Imports == 1 && Busy(page) == 0);
                        Require(Descendants<TextBlock>(host).Any(text => text.Text == "Import complete"), "Import summary missing");
                        Require(Descendants<TextBlock>(host).Any(text => text.Text.Contains("synthetic note")), "Import notes missing");
                    }
                    else if (scenario == "pin" || scenario == "pin-import")
                    {
                        var importing = scenario == "pin-import";
                        wire.RequirePin = true; Invoke(Button(importing ? "Choose file" : "Export profile data"));
                        await Until(() => Dialog(page) != null); var dialog = Dialog(page)!; await Task.Delay(180);
                        Descendants<PasswordBox>(dialog).Single().Password = "0000"; ClickDialog(dialog, "Verify");
                        await Until(() => wire.Verifications == 1 && dialog.IsPrimaryButtonEnabled);
                        Require(Dialog(page) == dialog && (importing ? wire.Imports : wire.Exports) == 1 && client.ProfileToken == null, "Wrong PIN adopted or closed dialog");
                        Descendants<PasswordBox>(dialog).Single().Password = "1234"; ClickDialog(dialog, "Verify");
                        await Until(() => (importing ? wire.Imports == 2 : picker.Saves == 1) && Busy(page) == 0);
                        Require((importing ? wire.Imports : wire.Exports) == 2 && client.ProfileToken == "synthetic-proof" && (importing || wire.LastExportProof), "PIN retry lost proof");
                        if(!importing) { Invoke(Button("Choose file")); await Until(() => wire.Imports == 1 && Busy(page) == 0); }
                    }
                    else if (scenario == "non-pin-403")
                    {
                        wire.Forbidden = true; Invoke(Button("Export profile data")); await Until(() => Busy(page) == 0);
                        Require(Dialog(page) == null && wire.Verifications == 0 && picker.Saves == 0, "Unrelated 403 prompted for PIN");
                    }
                    else if (scenario == "oversize")
                    {
                        picker.Source.Size = wire.Limit + 1L; Invoke(Button("Choose file")); await Until(() => picker.Opens == 1 && Busy(page) == 0);
                        Require(picker.Source.Reads == 0 && wire.Imports == 0, "Oversize file buffered or uploaded");
                    }
                    else if (scenario == "growth")
                    {
                        var path = Path.Combine(Path.GetTempPath(), "silo-growth-test.json"); await File.WriteAllBytesAsync(path, new byte[wire.Limit + 1]);
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        picker.OpenSource = (SettingsPage.IProfileTransferFileSource)Activator.CreateInstance(typeof(SettingsPage).GetNestedType("NativeFileSource", BindingFlags.NonPublic)!, file, 1L)!;
                        Invoke(Button("Choose file")); await Until(() => picker.Opens == 1 && Busy(page) == 0);
                        Require(wire.Imports == 0 && Descendants<TextBlock>(host).Any(text => text.Text.Contains("too large")), "Real bounded reader did not reject growth");
                    }
                    else if (scenario == "navigation")
                    {
                        wire.ExportGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Invoke(Button("Export profile data")); await Until(() => wire.Exports == 1);
                        Call(page, "ShowSettingsOverview"); Open(page); await Ready(page);
                        var successor = Descendants<Button>(host).Single(button => button.Content as string == "Export profile data");
                        Invoke(successor); await Until(() => wire.Exports == 2);
                        wire.ExportGate.SetResult(); await Until(() => Busy(page) == 0);
                        Require(picker.Saves == 1, "Retired export saved or successor blocked");
                        Call(page, "ShowSettingsDetail", page.FindName("PlaybackTab"));
                        Require(Dialog(page) == null, "Detail departure retained dialog");
                    }
                    else if (scenario == "navigation-picker" || scenario == "navigation-read")
                    {
                        if(scenario == "navigation-picker") picker.OpenGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        else picker.Source.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Invoke(Button("Choose file")); await Until(() => scenario == "navigation-picker" ? picker.Opens == 1 : picker.Source.Reads == 1);
                        Call(page, "ShowSettingsOverview"); Open(page); await Ready(page);
                        wire.ExportGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Invoke(Button("Export profile data")); await Until(() => wire.Exports == 1);
                        picker.OpenGate?.SetResult(); picker.Source.ReadGate?.SetResult(); await Task.Delay(150);
                        Require(Busy(page) != 0 && wire.Imports == 0, "Old file operation released successor busy state or uploaded");
                        if(scenario == "navigation-picker") Require(picker.Source.Reads == 0, "Retired picker buffered bytes");
                        wire.ExportGate.SetResult(); await Until(() => Busy(page) == 0);
                    }
                    else if (scenario == "authority-import")
                    {
                        wire.ImportGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Invoke(Button("Choose file")); await Until(() => wire.Imports == 1);
                        client.InvalidateAccessContext(); wire.ImportGate.SetResult(); await Task.Delay(180);
                        Require(!Descendants<TextBlock>(host).Any(text => text.Text == "Import complete"), "Stale import published success");
                    }
                    else if (scenario == "authority-response")
                    {
                        wire.ExportGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Invoke(Button("Export profile data")); await Until(() => wire.Exports == 1);
                        client.InvalidateAccessContext(); wire.ExportGate.SetResult(); await Task.Delay(180);
                        Require(picker.Saves == 0, "Stale authority exported to picker");
                    }
                    else if (scenario == "authority-dialog")
                    {
                        wire.RequirePin = true; Invoke(Button("Export profile data")); await Until(() => Dialog(page) != null);
                        var dialog = Dialog(page)!; await Task.Delay(180);
                        Require(Dialog(page) != null, "Dialog disappeared before authority change");
                        client.InvalidateAccessContext(); await Task.Delay(180);
                        if (Dialog(page) != null) Finding("R2: authority retirement leaves actual PIN dialog reference active");
                        await Until(() => Busy(page) == 0);
                        Require(Dialog(page) == null, "Dialog reference not retired after authority change");
                        Descendants<PasswordBox>(dialog).Single().Password = "1234"; ClickDialog(dialog, "Verify"); await Task.Delay(100);
                        Require(wire.Verifications == 0, "Retired dialog transmitted PIN"); dialog.Hide();
                    }
                    else if (scenario == "authority-verification")
                    {
                        wire.RequirePin = true; wire.PinGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Invoke(Button("Export profile data")); await Until(() => Dialog(page) != null); var dialog = Dialog(page)!; await Task.Delay(180);
                        Descendants<PasswordBox>(dialog).Single().Password = "1234"; ClickDialog(dialog, "Verify"); await Until(() => wire.Verifications == 1);
                        client.InvalidateAccessContext(); wire.PinGate.SetResult(); await Task.Delay(180);
                        Require(client.ProfileToken == null && wire.Exports == 1 && picker.Saves == 0, "Stale PIN verification adopted authority or retried"); dialog.Hide();
                    }
                    else if (scenario == "cache-refresh")
                    {
                        await customization.EnsureLoadedAsync(); Require(customization.CardPresentation.PosterSize == "compact", "Initial customization did not load");
                        var observedSizes = new List<string>();
                        EventHandler changed = (_, _) => observedSizes.Add(customization.CardPresentation.PosterSize);
                        customization.Changed += changed;
                        try
                        {
                            Invoke(Button("Choose file")); await Until(() => wire.Imports == 1 && Busy(page) == 0);
                            Require(customization.CardPresentation.PosterSize == "standard", "Successful import left loaded production customization stale");
                            Require(observedSizes.Count > 0 && observedSizes.All(size => size == "standard"), "Consumers were notified with pre-import customization");
                            Program.Log("CACHE: import refreshed customization before notifying existing consumers; no explicit fixture reload.");
                        }
                        finally { customization.Changed -= changed; }
                    }
                    else if (scenario == "near-limit")
                    {
                        var payload = new StringBuilder("{\"format\":\"silo-profile-export\",\"version\":1,\"server_id\":\"synthetic\",\"exported_at\":\"2026-11-11T00:00:00Z\",\"profile_name\":\"Synthetic\",\"data\":{},\"items\":[");
                        for(var index = 0; index < 60_000; index++)
                        {
                            if(index > 0) payload.Append(',');
                            payload.Append("{\"content_id\":\"00000000-0000-0000-0000-").Append(index.ToString("D12"))
                                .Append("\",\"kind\":\"movie\",\"title\":\"").Append(new string('\u00e9', 200)).Append("\",\"year\":2026}");
                        }
                        payload.Append("]}"); wire.ExportDocument = Encoding.UTF8.GetBytes(payload.ToString()); wire.Limit = 67_108_864;
                        Call(page, "ShowSettingsOverview"); Open(page); await Ready(page);
                        Invoke(Button("Export profile data")); await Until(() => picker.Saves == 1 && Busy(page) == 0);
                        var savedSize = new FileInfo(picker.TargetPath!).Length;
                        Program.Log($"SIZE raw={wire.ExportDocument.Length}; saved={savedSize}; officialLimit={wire.Limit}; items=60000");
                        picker.Source.Size = savedSize; Invoke(Button("Choose file")); await Until(() => picker.Opens == 1 && Busy(page) == 0);
                        if(savedSize > wire.Limit && wire.Imports == 0 && picker.Source.Reads == 0)
                            Finding("NEW P2: actual UI export expands a below-limit Unicode title document beyond the advertised re-import limit; UI refuses its own output");
                        File.Delete(picker.TargetPath);
                    }
                    Program.Log($"PASS: Your Data {scenario}");
                }
                catch(Exception exception) { Finding($"Acceptance {scenario}: {exception}"); }
                finally
                {
                    Dialog(page)?.Hide(); Call(page, "DeactivateYourData"); parent.Children.Remove(page); await Task.Delay(80);
                }
            }
        }
        finally { PickerProperty.SetValue(null, oldPicker); ServicesField.SetValue(null, outer); }
        if(Findings.Count != 0) throw new InvalidOperationException($"Profile transfer acceptance has {Findings.Count} preserved findings. See FINDING records.");
    }

    private static void Finding(string message) { Findings.Add(message); Program.Log("FINDING: " + message); }
    private static void Require(bool condition, string message) { if(!condition) throw new InvalidOperationException(message); }
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static void Open(SettingsPage page) => Call(page, "ShowSettingsDetail", page.FindName("YourDataTab"));
    private static int Busy(SettingsPage page) => (int)typeof(SettingsPage).GetField("_yourDataBusyOwner", Private)!.GetValue(page)!;
    private static ContentDialog? Dialog(SettingsPage page) => (ContentDialog?)typeof(SettingsPage).GetField("_yourDataPinDialog", Private)!.GetValue(page);
    private static Task Ready(SettingsPage page) => Until(() => !((ProgressRing)page.FindName("YourDataLoadingRing")).IsActive && Descendants<Button>((StackPanel)page.FindName("YourDataContentHost")).Count() == 2);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void ClickDialog(ContentDialog dialog, string label) => Invoke(Descendants<Button>(dialog).First(button => button.Content as string == label));
    private static async Task Until(Func<bool> predicate) { for(var attempt = 0; attempt < 300; attempt++) { if(predicate()) return; await Task.Delay(20); } throw new TimeoutException("Native condition did not complete"); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if(root is T match) yield return match;
        for(var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private sealed class Source : SettingsPage.IProfileTransferFileSource
    {
        internal long Size = Document.Length; internal int Reads; internal TaskCompletionSource? ReadGate; public long FileSize => Size;
        public async Task<byte[]> ReadBoundedAsync(long maxBytes, CancellationToken ct) { Reads++; if(ReadGate != null) await ReadGate.Task; return Document; }
    }
    private sealed class Picker : SettingsPage.IProfileTransferFilePicker
    {
        internal int Saves, Opens; internal Source Source = new(); internal SettingsPage.IProfileTransferFileSource? OpenSource; internal StorageFile? Target; internal string? TargetPath; internal TaskCompletionSource? OpenGate;
        public async Task<StorageFile?> PickSaveFileAsync(nint handle, string name)
        {
            Require(handle != 0 && name == "silo-profile.json", "Picker owner/name missing"); Saves++;
            var path = Path.Combine(Path.GetTempPath(), "silo-export-" + Guid.NewGuid().ToString("N") + ".json"); await File.WriteAllBytesAsync(path, []);
            TargetPath = path; Target = await StorageFile.GetFileFromPathAsync(path);
            return Target;
        }
        public async Task<SettingsPage.IProfileTransferFileSource?> PickOpenFileAsync(nint handle) { Require(handle != 0, "Import picker owner missing"); Opens++; if(OpenGate != null) await OpenGate.Task; return OpenSource ?? Source; }
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal int Exports, Imports, Verifications, Requests, Limit = 1024;
        internal bool RequirePin, Forbidden, LastExportProof;
        internal string PosterSize = "compact";
        internal byte[] ExportDocument = Document;
        internal TaskCompletionSource? ExportGate, ImportGate, PinGate;
        internal void Reset() { Exports = Imports = Verifications = Requests = 0; Limit = 1024; RequirePin = Forbidden = LastExportProof = false; ExportGate = ImportGate = PinGate = null; ExportDocument = Document; PosterSize = "compact"; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri!.Host == "profile-transfer-final.invalid", "Unexpected network target"); Requests++;
            var path = request.RequestUri.AbsolutePath;
            if(path == "/api/v2/capabilities/profile-export") return Json(JsonSerializer.Serialize(new { state = "available", format = "silo-profile-export", version = 1, max_import_bytes = Limit }));
            if(path == "/api/v2/profiles") return Json("""{"items":[{"id":"synthetic-profile","name":"Synthetic","has_pin":true,"is_primary":true}]}""");
            if(path == "/api/v2/settings/contract/capabilities") return Json("""{"api_version":1,"manifest_revision":22,"client_families":["desktop","web"],"supports_batched_effective":true,"supports_idempotent_writes":true}""");
            if(path == "/api/v2/settings/values/effective") return Json(JsonSerializer.Serialize(new { items = new[] { new { key = "ui.card_presentation", value = new { poster_size = PosterSize, caption = "title_metadata" }, source = "profile_client" } } }));
            if(path.EndsWith("/verify-pin"))
            {
                Verifications++; if(PinGate != null) await PinGate.Task;
                var body = await request.Content!.ReadAsStringAsync(ct);
                return Json(body.Contains("1234") ? """{"valid":true,"profile_token":"synthetic-proof"}""" : """{"valid":false}""");
            }
            if(path == "/api/v2/profile/export" || path == "/api/v2/profile/import")
            {
                var export = path.EndsWith("export"); if(export) Exports++; else Imports++;
                if(export && ExportGate != null) await ExportGate.Task;
                if(!export && ImportGate != null) await ImportGate.Task;
                if(Forbidden) return Json("""{"type":"https://silo.example/docs/api/v2/problems/forbidden","status":403,"detail":"Forbidden"}""", HttpStatusCode.Forbidden);
                var proof = request.Headers.TryGetValues("X-Profile-Token", out var values) && values.Single() == "synthetic-proof";
                if(RequirePin && !proof) return Json("""{"type":"https://silo.example/docs/api/v2/problems/profile_verification_required","status":403,"detail":"PIN required"}""", HttpStatusCode.Forbidden);
                if(export) { LastExportProof = proof; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(ExportDocument) }; }
                PosterSize = "standard";
                return Json("""{"same_server":true,"data":[{"type":"watch_history","added":1,"updated":2,"unchanged":3,"left_out":4,"notes":["synthetic note"]}],"unmatched":["synthetic unmatched"]}""");
            }
            return Json("{}");
        }
        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
