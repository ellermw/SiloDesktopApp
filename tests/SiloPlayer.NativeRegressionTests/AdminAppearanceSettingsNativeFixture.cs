using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>Synthetic Silo server for the Appearance fixtures (official contracts at 66b836f82).</summary>
internal sealed class AdminAppearanceFixtureServer : HttpMessageHandler
{
    internal readonly Dictionary<string, string> Values = new()
    {
        ["branding.accent_color"] = "", ["overlays.enabled"] = "true", ["defaults.card_overlays"] = "", ["defaults.ui.title_art"] = "",
        ["s3.public_bucket"] = "synthetic-bucket",
    };
    internal readonly Dictionary<string, string> Refs = new(StringComparer.Ordinal);
    internal readonly List<(string Ref, string Name)> Images = [];
    internal readonly List<string> Writes = [];
    internal int Version = 1;
    internal int Saves;
    internal int EffectiveReads;
    internal string SaveMode = "ok";
    internal string UploadMode = "ok";
    internal bool FailEffective;
    internal bool StorageAvailable = true;
    internal bool UploadsAvailable = true;
    internal int ManifestRevision = 29;
    internal TaskCompletionSource? EffectiveGate;
    internal string[] LastSavedKeys = [];
    internal string? LastIfMatch;
    internal string? LastUploadType;
    internal string? LastImageName;

    internal static string ImageRef(int index) => index.ToString("x2").PadLeft(64, 'c') + ".webp";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method != HttpMethod.Get) Writes.Add(request.Method.Method + " " + path);
        if (request.Method == HttpMethod.Put && path == "/api/v2/admin/settings")
        {
            Saves++;
            LastIfMatch = request.Headers.IfMatch.ToString();
            switch (SaveMode)
            {
                case "conflict": return Problem("precondition_failed", "The resource changed.", HttpStatusCode.PreconditionFailed);
                case "refuse": return Problem("validation_failed", "The request did not pass validation; see errors.", HttpStatusCode.UnprocessableEntity);
                case "unknown": return Problem("internal_error", "An unexpected error occurred.", HttpStatusCode.InternalServerError);
            }
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            var values = document.RootElement.GetProperty("values");
            LastSavedKeys = values.EnumerateObject().Select(property => property.Name).ToArray();
            foreach (var property in values.EnumerateObject()) Values[property.Name] = property.Value.GetString()!;
            Version++;
            return Json(new { values = Values, restart_required = false });
        }
        if (path.StartsWith("/api/v2/admin/branding/assets/", StringComparison.Ordinal))
        {
            var kind = path[("/api/v2/admin/branding/assets/".Length)..];
            if (request.Method == HttpMethod.Delete) { Refs.Remove(kind); Version++; return new(HttpStatusCode.NoContent); }
            var form = (MultipartFormDataContent)request.Content!;
            LastUploadType = form.Single().Headers.ContentType?.MediaType;
            switch (UploadMode)
            {
                case "refuse":
                    UploadMode = "ok";
                    return Json(new { type = "https://siloserver.org/docs/api/v2/problems/validation_failed", title = "The request did not pass validation; see errors.", status = 422,
                        errors = new[] { new { location = "body.file", code = "invalid", detail = "Unsupported image type; use PNG, JPEG, WebP (or PNG/ICO/SVG for favicon)" } } }, HttpStatusCode.UnprocessableEntity, "application/problem+json");
                case "unknown":
                    UploadMode = "ok";
                    return Problem("internal_error", "An unexpected error occurred.", HttpStatusCode.BadGateway);
            }
            var extension = LastUploadType switch { "image/svg+xml" => ".svg", "image/x-icon" => ".ico", "image/png" when kind == "favicon" => ".png", _ => ".webp" };
            var reference = kind[..Math.Min(4, kind.Length)].PadRight(16, '0') + extension;
            Refs[kind] = reference;
            Version++;
            return Json(new { kind, @ref = reference, url = "/api/v2/branding/assets/" + kind + "?v=" + Uri.EscapeDataString(reference) });
        }
        if (path == "/api/v2/admin/overlay-images" && request.Method == HttpMethod.Post)
        {
            var form = (MultipartFormDataContent)request.Content!;
            LastImageName = await form.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "name").ReadAsStringAsync(cancellation);
            var reference = ImageRef(Images.Count + 1);
            Images.Add((reference, LastImageName));
            var created = Json(new { @ref = reference, name = LastImageName, url = "/api/v2/overlay-images/" + reference, width = 300, height = 120 }, HttpStatusCode.Created);
            created.Headers.Location = new Uri("/api/v2/admin/overlay-images/" + reference, UriKind.Relative);
            return created;
        }
        if (path.StartsWith("/api/v2/admin/overlay-images/", StringComparison.Ordinal) && request.Method == HttpMethod.Delete)
        {
            Images.RemoveAll(image => path.EndsWith(image.Ref, StringComparison.Ordinal));
            return new(HttpStatusCode.NoContent);
        }
        if (path == "/api/v2/admin/settings/effective")
        {
            EffectiveReads++;
            if (EffectiveGate is { } gate) await gate.Task.WaitAsync(cancellation);
            if (FailEffective) return Problem("dependency_unavailable", "Settings are unavailable.", HttpStatusCode.ServiceUnavailable);
            return Json(Values);
        }
        object body = path switch
        {
            "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = Array.Empty<string>() },
            "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
            "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
            "/api/v2/admin/system/build" => new { available = true, display = "synthetic", build_number = 1 },
            "/api/v2/theme/branding" => new
            {
                server_name = "Synthetic", login_subtitle = "", storage_available = StorageAvailable,
                wordmark_url = Url("wordmark"), mark_url = Url("mark"), favicon_url = Url("favicon"), login_bg_url = Url("login_bg"),
            },
            "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = ManifestRevision, supports_overlay_images = true, supports_batched_effective = true, supports_idempotent_writes = true, client_families = new[] { "desktop" } },
            "/api/v2/capabilities/ratings" => new { state = "available", sources = new[] { new { source = "imdb", name = "IMDb" }, new { source = "tmdb", name = "TMDB" }, new { source = "rt_critic", name = "RT" } } },
            "/api/v2/settings/overlay-config" => new
            {
                enabled = Values["overlays.enabled"] != "false", image_uploads_available = UploadsAvailable,
                images = Images.Select(image => new { @ref = image.Ref, name = image.Name, url = "/api/v2/overlay-images/" + image.Ref, width = 300, height = 120 }).ToArray(),
            },
            _ when path.StartsWith("/api/v2/overlay-images/", StringComparison.Ordinal) => null!,
            _ => new { items = Array.Empty<object>(), revision = 1 },
        };
        return body == null ? new(HttpStatusCode.NotFound) : Json(body);
    }

    private string? Url(string kind) => Refs.TryGetValue(kind, out var reference) ? "/api/v2/branding/assets/" + kind + "?v=" + Uri.EscapeDataString(reference) : null;

    private HttpResponseMessage Problem(string type, string title, HttpStatusCode status) =>
        Json(new { type = "https://siloserver.org/docs/api/v2/problems/" + type, title, status = (int)status }, status, "application/problem+json");

    private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json")
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, mediaType) };
        response.Headers.ETag = new($"\"appearance-{Version}\"");
        return response;
    }
}

/// <summary>Hosts the production Appearance page in the real Admin shell and Frame.</summary>
internal sealed class AdminAppearanceFixtureHost : IAsyncDisposable
{
    private readonly Window _window;
    private readonly object? _previousServices;
    private readonly object? _previousPicker;
    private readonly UIElement? _previousContent;
    private readonly FieldInfo _servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly FieldInfo _pickerField = typeof(AdminAppearanceSettingsPage).GetField("ImagePickerOverride", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly HttpClient _http;
    internal AdminAppearanceFixtureServer Server { get; } = new();
    internal SiloApiClient Client { get; }
    internal AuthService Auth { get; }
    internal AdminMonitorAuthority Authority { get; }
    internal ServiceProvider Services { get; }
    internal Frame Outer { get; } = new();
    internal AdminShellPage? Shell { get; private set; }
    internal AdminAppearanceSettingsPage? Page { get; private set; }
    internal Func<string, AdminAppearancePickedImage?> Pick { get; set; } = _ => null;
    internal int Opens;

    internal AdminAppearanceFixtureHost(Window window)
    {
        _window = window;
        _http = new HttpClient(Server);
        Client = new SiloApiClient(_http);
        Client.SetBaseUrl("https://fixture.invalid");
        Auth = new AuthService(Client, new AuthApi(Client));
        Auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "Synthetic admin", Role = "admin" });
        Auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        Authority = new AdminMonitorAuthority(Client, Auth);
        var navigation = new NavigationService { Frame = Outer };
        var settingsApi = new SettingsApi(Client);
        var settings = new SettingsService(Program.ResultDirectory);
        var collection = new ServiceCollection().AddSingleton(Client).AddSingleton(Auth).AddSingleton(Authority)
            .AddSingleton(new AdminMonitorApi(Client)).AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(settings)
            .AddSingleton(settingsApi).AddSingleton(new CatalogApi(Client)).AddSingleton(new CardOverlayService(settingsApi)).AddSingleton(new ThemeService(settings, settingsApi));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!
            .GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        Services = collection.BuildServiceProvider();
        _previousServices = _servicesField.GetValue(null);
        _previousPicker = _pickerField.GetValue(null);
        _previousContent = window.Content;
        _servicesField.SetValue(null, Services);
        Func<string, CancellationToken, Task<AdminAppearancePickedImage?>> picker = (purpose, _) => Task.FromResult(Pick(purpose));
        _pickerField.SetValue(null, picker);
        window.Content = Outer;
    }

    internal async Task<AdminAppearanceSettingsPage> OpenAsync(bool waitLoaded = true)
    {
        AdminAppearanceFixture.Check(AdminShellPage.TryOpenHref("/admin/settings/appearance")?.Kind == AdminRouteResolutionKind.Route,
            "Appearance opens through the real Admin route and production registration");
        await AdminAppearanceFixture.Until(() => Outer.Content is AdminShellPage, "Admin shell");
        Shell = (AdminShellPage)Outer.Content!;
        var frame = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Shell)!;
        await AdminAppearanceFixture.Until(() => frame.Content is AdminAppearanceSettingsPage { IsLoaded: true }, "Appearance page mounted");
        Page = (AdminAppearanceSettingsPage)frame.Content;
        if (waitLoaded) await AdminAppearanceFixture.Until(() => Page.Controller?.State is { Loaded: true, Loading: false }, "Appearance loaded");
        return Page;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var pageUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var shellUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Page is { IsLoaded: true }) Page.Unloaded += (_, _) => pageUnloaded.TrySetResult(); else pageUnloaded.TrySetResult();
            if (Shell is { IsLoaded: true }) Shell.Unloaded += (_, _) => shellUnloaded.TrySetResult(); else shellUnloaded.TrySetResult();
            Outer.BackStack.Clear();
            Outer.ForwardStack.Clear();
            Outer.Content = null;
            await Task.WhenAll(pageUnloaded.Task, shellUnloaded.Task).WaitAsync(TimeSpan.FromSeconds(6));
            AdminAppearanceFixture.Check(Page?.Controller == null, "page and shell unload barriers retire the controller");
        }
        finally
        {
            _pickerField.SetValue(null, _previousPicker);
            _window.Content = _previousContent;
            _servicesField.SetValue(null, _previousServices);
            await Services.DisposeAsync();
            Authority.Dispose();
            Auth.Dispose();
            _http.Dispose();
            Server.Dispose();
        }
    }
}

/// <summary>Shared fixture helpers.</summary>
internal static class AdminAppearanceFixture
{
    internal static string BuiltIn => (string)typeof(CardOverlayService).GetProperty("BuiltInEditorDefaults", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    internal static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aSWQAAAAASUVORK5CYII=");

    internal static AdminAppearancePickedImage File(string name, string mediaType, long? length = null, Action? opened = null) =>
        new(name, mediaType, length ?? Png.Length, _ => { opened?.Invoke(); return Task.FromResult<Stream>(new MemoryStream(Png)); });

    internal static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) throw new InvalidOperationException(label);
    }

    internal static async Task Until(Func<bool> condition, string label)
    {
        for (var attempt = 0; attempt < 320 && !condition(); attempt++) await Task.Delay(25);
        if (!condition()) { Program.Log("FAIL: timed out: " + label); throw new InvalidOperationException("Timed out: " + label); }
        Program.Log("PASS: " + label);
    }

    internal static string Id(DependencyObject element) => AutomationProperties.GetAutomationId(element);
    internal static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().Single(element => Id(element) == id);
    internal static bool Has(DependencyObject root, string id) => Descendants(root).Any(element => Id(element) == id);
    internal static bool Shown(DependencyObject root, string id) => Descendants(root).OfType<FrameworkElement>().Any(element => Id(element) == id && Visible(element));

    internal static bool Visible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return element.IsLoaded;
    }

    internal static void Invoke(ButtonBase button)
    {
        if (button is ToggleButton toggle) ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
        else ((IInvokeProvider)new ButtonAutomationPeer((Button)button).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    internal static async Task Click(DependencyObject root, string id)
    {
        await Until(() => Has(root, id) && Find<ButtonBase>(root, id) is { IsLoaded: true, IsEnabled: true }, id + " enabled");
        Invoke(Find<ButtonBase>(root, id));
    }

    internal static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);

    internal static Button DialogButton(ContentDialog dialog, string label) =>
        Descendants(dialog).OfType<Button>().First(button => button.Content?.ToString() == label || AutomationProperties.GetName(button) == label);

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    internal static IEnumerable<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Where(text => text.IsLoaded).Select(text => text.Text);
}

/// <summary>
/// Admin Configure &gt; Appearance through the real shell: loading, a failed read with Reload,
/// every branding slot (upload, SVG favicon, refusal, oversize, unknown outcome, replace,
/// remove), accent, the save model (refusal, conflict, unknown), restore defaults and the
/// leave guard. Synthetic data only.
/// </summary>
internal static class AdminAppearanceSettingsNativeFixture
{
    internal static async Task RunAsync(Window window)
    {
        await using var host = new AdminAppearanceFixtureHost(window);
        var server = host.Server;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000));

        // Loading, then a failed first read with a reachable Reload.
        server.EffectiveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.FailEffective = true;
        var page = await host.OpenAsync(waitLoaded: false);
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Shown(page, "AppearanceLoading"), "loading text while the first read is open");
        await AdminAppearanceFixture.Until(() => !AdminAppearanceFixture.Shown(page, "AppearanceTitleArt"), "no setting renders before the snapshot");
        server.EffectiveGate.SetResult();
        server.EffectiveGate = null;
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Shown(page, "AppearanceReload"), "failed read shows Reload");
        AdminAppearanceFixture.Check(page.Controller!.State.Error != null && !page.Controller.State.Loaded, "failed read is visible and nothing is editable");
        server.FailEffective = false;
        await AdminAppearanceFixture.Click(page, "AppearanceReload");
        await AdminAppearanceFixture.Until(() => page.Controller!.State is { Loaded: true, Loading: false, Error: null }, "Reload recovers the page");
        var controller = page.Controller!;
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Shown(page, "AppearanceTitleArt"), "Appearance controls rendered");
        await Task.Delay(150);
        AdminAppearanceFixture.Check(controller.State.DirtyKeys.Count == 0 && server.Saves == 0 && server.Writes.Count == 0, "opening the page stages and sends nothing");
        await AdminAppearanceFixture.Until(() => !AdminAppearanceFixture.Texts(page).Any(text => text.StartsWith("web.", StringComparison.Ordinal) || text.StartsWith("settingsManifest.", StringComparison.Ordinal)), "official text keys resolve");
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Texts(page).Contains("Logos and icons") && AdminAppearanceFixture.Texts(page).Contains("Card overlays")
            && AdminAppearanceFixture.Texts(page).Contains("Badge images") && AdminAppearanceFixture.Texts(page).Contains("Title pages"), "official groups render");
        await AdminAppearanceFixture.Until(() => !AdminAppearanceFixture.Find<Button>(page, "AppearanceRestoreOverlays").IsEnabled, "Restore defaults is disabled while the stored defaults are built in");

        // Every branding slot: upload, replace label, remove; the SVG favicon is accepted and never rendered.
        foreach (var (kind, type) in new[] { ("wordmark", "image/png"), ("mark", "image/jpeg"), ("favicon", "image/svg+xml"), ("login_bg", "image/webp") })
        {
            host.Pick = _ => AdminAppearanceFixture.File("synthetic", type);
            await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Find<Button>(page, "AppearanceUpload" + kind).Content?.ToString() == "Upload", kind + ": empty slot offers Upload");
            await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Shown(page, "AppearanceCaption" + kind), kind + ": empty slot shows its default caption");
            var writes = server.Writes.Count;
            await AdminAppearanceFixture.Click(page, "AppearanceUpload" + kind);
            await AdminAppearanceFixture.Until(() => server.Refs.ContainsKey(kind) && controller.State.AssetBusy == null && controller.State.Branding?.AssetUrl(kind) != null, kind + " upload acknowledged and re-read");
            AdminAppearanceFixture.Check(server.Writes.Count == writes + 1 && server.LastUploadType == type, kind + ": one multipart write with the declared type");
            await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Find<Button>(page, "AppearanceUpload" + kind).Content?.ToString() == "Replace"
                && AdminAppearanceFixture.Shown(page, "AppearanceRemove" + kind), kind + ": Replace and Remove after upload");
        }
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Find<Image>(page, "AppearancePreviewfavicon").Source == null, "an SVG favicon is never handed to an image decoder");
        AdminAppearanceFixture.Check(controller.State.DirtyKeys.Count == 0, "image writes leave the settings draft untouched");

        // Refused, oversized and unknown uploads.
        host.Pick = _ => AdminAppearanceFixture.File("synthetic", "image/png");
        server.UploadMode = "refuse";
        await AdminAppearanceFixture.Click(page, "AppearanceUploadmark");
        await AdminAppearanceFixture.Until(() => controller.State.AssetError != null && controller.State.AssetBusy == null, "refused upload");
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Find<TextBlock>(page, "AppearanceError").Text.Contains("Unsupported image type", StringComparison.Ordinal), "refusal shows the server's field error");
        await AdminAppearanceFixture.Until(() => !controller.State.AssetsUnknown && AdminAppearanceFixture.Find<Button>(page, "AppearanceUploadmark").IsEnabled, "a refusal leaves image writes available for a retry");
        var opened = false;
        var before = server.Writes.Count;
        host.Pick = _ => AdminAppearanceFixture.File("huge", "image/png", 9L * 1024 * 1024, () => opened = true);
        await AdminAppearanceFixture.Click(page, "AppearanceUploadwordmark");
        await AdminAppearanceFixture.Until(() => controller.State.AssetError?.Contains("maximum size", StringComparison.Ordinal) == true, "oversized wordmark refused");
        AdminAppearanceFixture.Check(!opened && server.Writes.Count == before, "an oversized file is refused before a byte is read or sent");
        host.Pick = _ => AdminAppearanceFixture.File("synthetic", "image/png");
        server.UploadMode = "unknown";
        await AdminAppearanceFixture.Click(page, "AppearanceUploadmark");
        await AdminAppearanceFixture.Until(() => controller.State.AssetsUnknown && controller.State.AssetBusy == null, "unknown upload outcome");
        before = server.Writes.Count;
        await AdminAppearanceFixture.Until(() => !AdminAppearanceFixture.Find<Button>(page, "AppearanceUploadmark").IsEnabled && !AdminAppearanceFixture.Find<Button>(page, "AppearanceRemovemark").IsEnabled
            && !AdminAppearanceFixture.Find<Button>(page, "AppearanceUploadwordmark").IsEnabled, "an unknown outcome blocks every image write");
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Shown(page, "AppearanceReload"), "an unknown image outcome offers Reload");
        await AdminAppearanceFixture.Click(page, "AppearanceReload");
        await AdminAppearanceFixture.Until(() => !controller.State.AssetsUnknown && !controller.State.Loading, "reload clears the unknown image outcome");
        AdminAppearanceFixture.Check(server.Writes.Count == before, "the unknown upload is never replayed");
        foreach (var kind in new[] { "wordmark", "mark", "favicon", "login_bg" })
        {
            await AdminAppearanceFixture.Click(page, "AppearanceRemove" + kind);
            await AdminAppearanceFixture.Until(() => !server.Refs.ContainsKey(kind) && controller.State.AssetBusy == null && controller.State.Branding?.AssetUrl(kind) == null, kind + " removed");
        }
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Find<Button>(page, "AppearanceUploadwordmark").Content?.ToString() == "Upload", "a removed slot offers Upload again");

        // Accent color: exactly one control selected.
        await AdminAppearanceFixture.Click(page, "AppearanceAccent10b981");
        await AdminAppearanceFixture.Until(() => controller.State.Values["branding.accent_color"] == "#10b981", "accent preset staged");
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Find<ToggleButton>(page, "AppearanceAccent10b981").IsChecked == true
            && AdminAppearanceFixture.Find<ToggleButton>(page, "AppearanceAccentDefault").IsChecked == false, "preset is the one selected accent control");
        await AdminAppearanceFixture.Click(page, "AppearanceAccentDefault");
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Count == 0, "choosing the stored default again clears the draft");

        // Save: one batch with only the changed key, then refusal, conflict and unknown outcomes.
        var titleArt = AdminAppearanceFixture.Find<ToggleSwitch>(page, "AppearanceTitleArt");
        titleArt.IsOn = false;
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Contains("defaults.ui.title_art"), "title-art draft");
        await AdminAppearanceFixture.Click(page, "AppearanceSave");
        await AdminAppearanceFixture.Until(() => server.Saves == 1 && !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "settings save");
        AdminAppearanceFixture.Check(server.LastSavedKeys.SequenceEqual(["defaults.ui.title_art"]) && server.Values["defaults.ui.title_art"] == "false", "Save sends only the changed title-art default");
        AdminAppearanceFixture.Check(server.LastIfMatch?.StartsWith("\"appearance-", StringComparison.Ordinal) == true, "Save pins the displayed strong ETag");

        server.SaveMode = "refuse";
        titleArt.IsOn = true;
        await AdminAppearanceFixture.Click(page, "AppearanceSave");
        await AdminAppearanceFixture.Until(() => server.Saves == 2 && !controller.State.Saving && controller.State.Error != null, "refused save");
        await AdminAppearanceFixture.Until(() => titleArt.IsOn && controller.State.DirtyKeys.Contains("defaults.ui.title_art") && AdminAppearanceFixture.Find<Button>(page, "AppearanceSave").IsEnabled,
            "a refused save keeps the typed value and allows a retry");
        server.SaveMode = "conflict";
        await AdminAppearanceFixture.Click(page, "AppearanceSave");
        await AdminAppearanceFixture.Until(() => controller.State.Conflict && !controller.State.Saving, "conflict result");
        await AdminAppearanceFixture.Until(() => !AdminAppearanceFixture.Find<Button>(page, "AppearanceSave").IsEnabled && AdminAppearanceFixture.Shown(page, "AppearanceReload"), "a conflict blocks Save and offers Reload");
        server.SaveMode = "ok";
        await AdminAppearanceFixture.Click(page, "AppearanceReload");
        await AdminAppearanceFixture.Until(() => !controller.State.Loading && !controller.State.Conflict, "reload after conflict");
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Contains("defaults.ui.title_art") && titleArt.IsOn, "reload keeps the unacknowledged draft");
        server.SaveMode = "unknown";
        var saves = server.Saves;
        await AdminAppearanceFixture.Click(page, "AppearanceSave");
        await AdminAppearanceFixture.Until(() => controller.State.OutcomeUnknown && !controller.State.Saving, "unknown save outcome");
        await AdminAppearanceFixture.Until(() => !AdminAppearanceFixture.Find<Button>(page, "AppearanceSave").IsEnabled && server.Saves == saves + 1, "an unknown save is sent once and blocks another");
        server.SaveMode = "ok";
        await AdminAppearanceFixture.Click(page, "AppearanceReload");
        await AdminAppearanceFixture.Until(() => !controller.State.Loading && !controller.State.OutcomeUnknown, "reload after unknown save");
        await AdminAppearanceFixture.Click(page, "AppearanceDiscard");
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Count == 0 && !titleArt.IsOn, "Discard restores the saved value");

        // Restore defaults stages the built-in document behind a confirmation.
        var year = AdminAppearanceFixture.Find<ToggleSwitch>(page, "AppearanceOverlayToggle-year");
        year.IsOn = true;
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Contains("defaults.card_overlays") && AdminAppearanceFixture.Find<Button>(page, "AppearanceRestoreOverlays").IsEnabled, "edited overlays enable Restore defaults");
        await AdminAppearanceFixture.Click(page, "AppearanceRestoreOverlays");
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "restore dialog");
        var dialog = (ContentDialog)AdminAppearanceFixture.Field(page, "_dialog")!;
        AdminAppearanceFixture.Check(dialog.Title?.ToString() == "Restore badge defaults", "restore dialog uses the official title");
        AdminAppearanceFixture.Invoke(AdminAppearanceFixture.DialogButton(dialog, "Restore"));
        await AdminAppearanceFixture.Until(() => controller.State.Values["defaults.card_overlays"] == AdminAppearanceFixture.BuiltIn, "restore stages the built-in document");
        await AdminAppearanceFixture.Until(() => !year.IsOn && !AdminAppearanceFixture.Find<Button>(page, "AppearanceRestoreOverlays").IsEnabled, "restored controls follow the built-ins");
        AdminAppearanceFixture.Check(server.Saves == saves + 1, "restoring writes nothing until Save");
        await AdminAppearanceFixture.Click(page, "AppearanceSave");
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Count == 0 && !controller.State.Saving, "restored defaults saved");
        AdminAppearanceFixture.Check(server.Values["defaults.card_overlays"] == AdminAppearanceFixture.BuiltIn, "Save writes the official built-in document");

        // Leave guard: Cancel keeps the page and draft, Discard leaves.
        titleArt.IsOn = true;
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Count == 1, "draft before leaving");
        var leaving = page.CanLeaveAsync();
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "leave dialog");
        AdminAppearanceFixture.Invoke(AdminAppearanceFixture.DialogButton((ContentDialog)AdminAppearanceFixture.Field(page, "_dialog")!, "Cancel"));
        AdminAppearanceFixture.Check(!await leaving && controller.State.DirtyKeys.Count == 1, "Cancel keeps the page and its draft");
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Field(page, "_dialog") == null, "leave dialog closed");
        leaving = page.CanLeaveAsync();
        await AdminAppearanceFixture.Until(() => AdminAppearanceFixture.Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "second leave dialog");
        AdminAppearanceFixture.Invoke(AdminAppearanceFixture.DialogButton((ContentDialog)AdminAppearanceFixture.Field(page, "_dialog")!, "Discard"));
        AdminAppearanceFixture.Check(await leaving && controller.State.DirtyKeys.Count == 0, "Discard clears the draft and allows leaving");

        foreach (var width in new[] { 1440, 800, 480 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 1000));
            await Task.Delay(150);
            await MediaParityNativeFixture.CaptureAsync(host.Outer, $"appearance-loaded-{width}.png");
            await AdminAppearanceFixture.Until(() => !AdminAppearanceFixture.Texts(page).Any(text => text.StartsWith("web.", StringComparison.Ordinal)), $"{width}: official text keys resolve");
        }
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000));
        titleArt.IsOn = !titleArt.IsOn;
        await AdminAppearanceFixture.Until(() => controller.State.DirtyKeys.Count == 1, "draft before retirement");
        host.Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "Synthetic admin", Role = "user" });
        await AdminAppearanceFixture.Until(() => controller.State.Owner == null, "authority retirement");
        AdminAppearanceFixture.Check(controller.State.Values.Count == 0 && controller.State.DirtyKeys.Count == 0, "authority retirement clears the draft and values");
    }
}
