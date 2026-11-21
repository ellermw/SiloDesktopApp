using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Libraries › Subtitle Files in the real Admin shell against a state-aware
/// synthetic server (no live server, account or user data; PNG state captures,
/// no file picker). Proves: the registration hook shows the leaf and opens
/// the page with URL filters; filter clicks replace the URL in place and
/// restart paging without a cursor; Next/Previous and page size; filtered
/// empty state and reset; the editor sheet's dirty guard, guarded save with
/// the exact validator, 412 conflict with in-place reload, and the one-shot
/// delete confirmation; authority retirement clears the page; no transition.
/// Program.cs dispatch (SILO_NATIVE_TEST_ONLY=admin-subtitles) is wired by the lead.
/// </summary>
internal static class AdminSubtitlesNativeFixture
{
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 5000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState { get; set; } = WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels) => new Release();
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            snapshot = JsonDocument.Parse("[]").RootElement.Clone();
            return channel == "scans";
        }
        private sealed class Release : IDisposable { public void Dispose() { } }
        internal void Silence() { SnapshotReceived?.Invoke("", default); EventReceived?.Invoke("", "", default); StateChanged?.Invoke(CurrentState); }
    }

    private sealed record StoredRow(string Id, string Provider, string Language, string Release, string? Uploader, bool Hearing);

    /// <summary>A synthetic subtitle store: filtering, offset cursors, strong validators, 412 and one-shot deletes.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly object _gate = new();
        public ConcurrentQueue<string> Requests { get; } = new();
        public List<StoredRow> Rows { get; } = [];
        public Dictionary<string, int> Versions { get; } = [];
        public bool ConflictNextPatch { get; set; }
        public string? LastIfMatch { get; private set; }
        public string? LastPatchBody { get; private set; }

        public Server()
        {
            var providers = new[] { "upload", "opensubtitles", "subdl", "subsource" };
            for (var index = 1; index <= 240; index++)
                Rows.Add(new StoredRow((1000 - index).ToString(), providers[index % 4], index % 3 == 0 ? "fr" : "en", "Synthetic.Release." + index, index % 4 == 0 ? "uploader" : null, index % 5 == 0));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue($"{request.Method} {request.RequestUri.PathAndQuery}");
            if (path == "/api/v2/admin/subtitles" && request.Method == HttpMethod.Get) return List(request.RequestUri);
            if (path.StartsWith("/api/v2/admin/subtitles/", StringComparison.Ordinal))
            {
                var id = path["/api/v2/admin/subtitles/".Length..];
                StoredRow? row;
                lock (_gate) row = Rows.FirstOrDefault(candidate => candidate.Id == id);
                if (row == null) return Problem(HttpStatusCode.NotFound, "not_found", "Stored subtitle not found.");
                var tag = "\"v" + Version(id) + "\"";
                if (request.Method == HttpMethod.Get) return Tagged(Metadata(row), tag);
                LastIfMatch = request.Headers.IfMatch.FirstOrDefault()?.ToString();
                if (request.Method == HttpMethod.Patch)
                {
                    LastPatchBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                    if (ConflictNextPatch) { ConflictNextPatch = false; lock (_gate) Versions[id] = Version(id) + 1; return Problem(HttpStatusCode.PreconditionFailed, "precondition_failed", "Subtitle metadata changed; review the current value before saving."); }
                    if (LastIfMatch != tag) return Problem(HttpStatusCode.PreconditionFailed, "precondition_failed", "Subtitle metadata changed; review the current value before saving.");
                    using var body = JsonDocument.Parse(LastPatchBody);
                    var updated = row with
                    {
                        Release = body.RootElement.TryGetProperty("release_name", out var release) ? release.GetString()! : row.Release,
                        Language = body.RootElement.TryGetProperty("language", out var language) ? language.GetString()! : row.Language,
                        Hearing = body.RootElement.TryGetProperty("hearing_impaired", out var hearing) ? hearing.GetBoolean() : row.Hearing,
                    };
                    lock (_gate) { Rows[Rows.IndexOf(row)] = updated; Versions[id] = Version(id) + 1; }
                    return Tagged(Metadata(updated), "\"v" + Version(id) + "\"");
                }
                if (request.Method == HttpMethod.Delete)
                {
                    if (LastIfMatch != tag) return Problem(HttpStatusCode.PreconditionFailed, "precondition_failed", "Subtitle changed; review it before deleting.");
                    lock (_gate) Rows.Remove(row);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
            }
            var json = path switch
            {
                "/api/v2/admin/users" => "{\"items\":[{\"id\":\"1\",\"username\":\"synthetic-admin\"},{\"id\":\"2\",\"username\":\"uploader\"}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/sessions" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/tasks" => "{\"items\":[]}",
                "/api/v2/libraries" => "{\"items\":[]}",
                "/api/v2/admin/downloads/preparations" => "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":0},\"items\":[]}",
                "/api/v2/admin/system/build" => "{\"display\":\"v1.2.3\",\"revision\":\"abc\",\"dirty\":false,\"build_number\":4321,\"available\":true}",
                "/api/v2/admin/server/status" => "{\"restart_required\":false,\"restart_mark_count\":0,\"restart_requested\":false}",
                _ => "{}",
            };
            return Json(json);
        }

        private int Version(string id) { lock (_gate) return Versions.TryGetValue(id, out var version) ? version : 1; }

        private HttpResponseMessage List(Uri uri)
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : "", StringComparer.Ordinal);
            List<StoredRow> matching;
            lock (_gate)
                matching = Rows.Where(row =>
                    (!query.TryGetValue("provider", out var provider) || row.Provider == provider) &&
                    (!query.TryGetValue("language", out var language) || row.Language == language) &&
                    (!query.TryGetValue("user_id", out var user) || (user == "2" && row.Uploader != null)) &&
                    (!query.TryGetValue("q", out var search) || row.Release.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
            var limit = int.Parse(query["limit"]);
            var offset = query.TryGetValue("cursor", out var cursor) ? int.Parse(cursor[1..]) : 0;
            var page = matching.Skip(offset).Take(limit).ToList();
            var more = offset + limit < matching.Count;
            var items = string.Join(',', page.Select(row => ListRow(row)));
            var pageInfo = more ? "{\"has_more\":true,\"next_cursor\":\"c" + (offset + limit) + "\"}" : "{\"has_more\":false}";
            return Json("{\"items\":[" + items + "],\"page\":" + pageInfo + ",\"total\":" + matching.Count + ",\"uploads\":" + matching.Count(row => row.Provider == "upload") + ",\"provider_downloads\":" + matching.Count(row => row.Provider != "upload") + "}");
        }

        private static string ListRow(StoredRow row) =>
            "{\"id\":\"" + row.Id + "\",\"media_file_id\":\"42\",\"media_content_id\":\"content-" + row.Id + "\",\"provider\":\"" + row.Provider + "\",\"language\":\"" + row.Language +
            "\",\"format\":\"srt\",\"release_name\":\"" + row.Release + "\",\"score\":0,\"hearing_impaired\":" + (row.Hearing ? "true" : "false") +
            ",\"created_at\":\"2026-11-14T10:00:00.000Z\",\"uploader_username\":\"" + (row.Uploader ?? "") + "\",\"media_title\":\"Synthetic Movie " + row.Id +
            "\",\"media_type\":\"" + (int.Parse(row.Id) % 2 == 0 ? "episode" : "movie") + "\",\"file_path\":\"/synthetic/subtitles/" + row.Id + ".srt\"}";

        private static string Metadata(StoredRow row) =>
            "{\"id\":\"" + row.Id + "\",\"media_file_id\":\"42\",\"provider\":\"" + row.Provider + "\",\"language\":\"" + row.Language + "\",\"format\":\"srt\",\"release_name\":\"" +
            row.Release + "\",\"score\":0,\"hearing_impaired\":" + (row.Hearing ? "true" : "false") + ",\"created_at\":\"2026-11-14T10:00:00.000Z\"}";

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Tagged(string json, string tag)
        {
            var response = Json(json); response.Headers.ETag = new EntityTagHeaderValue(tag); return response;
        }

        private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) =>
            Json("{\"type\":\"https://siloserver.org/docs/api/v2/problems/" + type + "\",\"title\":\"" + type + "\",\"status\":" + (int)status + ",\"detail\":\"" + detail + "\"}", status);
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
        var api = new AdminMonitorApi(client);
        var realtime = new Realtime();
        using var store = new ServerActivityStore(api, authority, realtime);
        var navigation = new NavigationService();
        var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddAdminSubtitleFiles();
        await using var provider = services.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        field.SetValue(null, provider);
        try
        {
            Resize(window, 1440, 900);
            var outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;

            // Entry with URL filters through the registration hook.
            Check(AdminShellPage.CanOpen(new AdminRouteRequest(AdminRoute.SubtitleFiles)), "the registration hook makes Subtitle Files openable");
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.SubtitleFiles, new Dictionary<string, string> { ["provider"] = "upload" })), "Subtitle Files enters Admin");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)Field(shell, "_frame")!;
            await Until(() => inner.Content is AdminSubtitlesPage { Controller.State.Loaded: true });
            var page = inner.Content as AdminSubtitlesPage;
            Check(page?.Controller?.State is { Loaded: true, Error: null }, "the first page settles without an error");
            await Until(() => Find<FrameworkElement>(shell, "SubtitlesTable") != null);
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "the page swaps in place without a transition");
            Check(server.Requests.Any(request => request.StartsWith("GET /api/v2/admin/subtitles?provider=upload&limit=25", StringComparison.Ordinal)), "URL provider filter and default page size are sent");
            var sidebar = (Grid)Field(shell, "_desktopSidebarHost")!;
            Check(Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Subtitle Files"), "the sidebar shows the registered Subtitle Files leaf");
            Check(Find<FrameworkElement>(shell, "SubtitlesTable") != null, "the subtitle table renders");
            Check(Texts(shell).Contains("Subtitles") && Texts(shell).Contains("TOTAL STORED"), "header and stats render with WebUI text");
            Check(Find<HyperlinkButton>(shell, "SubtitlesMedia_996") != null, "rows with a content id link to the item");
            Check(Find<Button>(shell, "SubtitlesEdit_996") is { } editButton && AutomationProperties.GetName(editButton) == "Edit subtitle 996", "row actions carry WebUI accessible names");

            Check(((Grid)Field(page!, "_stats")!).ColumnDefinitions.Count == 4, "stats use the WebUI viewport breakpoint at 1440px");
            await CaptureAsync(outer, "subtitles-list-1440x900.png");

            var hiddenActions = (StackPanel)Find<Button>(shell, "SubtitlesEdit_996")!.Parent;
            Check(hiddenActions.Opacity == 0, "desktop row actions are hidden until pointer or keyboard focus enters the row");
            Find<Button>(shell, "SubtitlesEdit_996")!.Focus(FocusState.Keyboard);
            await Until(() => hiddenActions.Opacity == 1);
            Check(hiddenActions.Opacity == 1, "keyboard focus reveals row actions");
            var retainedEdit = Find<Button>(shell, "SubtitlesEdit_996")!;
            var render = typeof(AdminSubtitlesPage).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var beforeDownload = page!.Controller!.State;
            render.Invoke(page, [beforeDownload with { DownloadingId = "996" }]);
            Check(ReferenceEquals(retainedEdit, Find<Button>(shell, "SubtitlesEdit_996")), "download status keeps existing row controls mounted");
            Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(shell.XamlRoot), retainedEdit), "download status preserves keyboard focus");
            Check(Find<Button>(shell, "SubtitlesDownload_996") is { IsEnabled: false }, "download status disables the retained download button");
            render.Invoke(page, [beforeDownload]);
            Check(Find<Button>(shell, "SubtitlesDownload_996") is { IsEnabled: true }, "download completion enables the same download button");

            // Provider filter: in-place URL replacement, first page, no history.
            await InvokeAsync(Find<Button>(shell, "SubtitlesProvider_subdl"));
            await Until(() => shell.CurrentRequest.Get("provider") == "subdl" && page!.Controller!.State is { Loading: false, Query.Provider: "subdl" } && Find<Button>(shell, "SubtitlesNext") is { IsEnabled: true });
            Check(ReferenceEquals(inner.Content, page) && !inner.CanGoBack, "provider filter replaces the URL without remounting or adding history");
            Check(server.Requests.Last(request => request.StartsWith("GET /api/v2/admin/subtitles?", StringComparison.Ordinal)) == "GET /api/v2/admin/subtitles?provider=subdl&limit=25", "provider change sends the first page without a cursor");
            Check(page!.Controller!.State.Total == 60 && page.Controller.State.CanNext, "subdl matches 60 rows over more than one page");

            // Next / Previous / page size.
            await InvokeAsync(Find<Button>(shell, "SubtitlesNext"));
            await Until(() => page.Controller.State is { Loading: false, PageIndex: 1 } && Find<TextBlock>(shell, "SubtitlesPageNumber")?.Text == "Page 2" && Find<Button>(shell, "SubtitlesPrevious") is { IsEnabled: true });
            Check(server.Requests.Contains("GET /api/v2/admin/subtitles?provider=subdl&limit=25&cursor=c25"), "Next requests the server cursor under the same filters");
            Check(Find<TextBlock>(shell, "SubtitlesPageNumber")?.Text == "Page 2", "page number reads Page 2");
            await InvokeAsync(Find<Button>(shell, "SubtitlesPrevious"));
            await Until(() => page.Controller.State is { Loading: false, PageIndex: 0 } && Find<TextBlock>(shell, "SubtitlesPageNumber")?.Text == "Page 1" && Find<Button>(shell, "SubtitlesPrevious") is { IsEnabled: false });
            Check(page.Controller.State.PageIndex == 0 && !page.Controller.State.CanPrevious, "Previous returns to the first page");
            var pageSize = Find<ComboBox>(shell, "SubtitlesPageSize")!;
            pageSize.SelectedIndex = 1;
            await Until(() => page.Controller.State is { Loading: false, Query.PageSize: 50 });
            Check(server.Requests.Any(request => request == "GET /api/v2/admin/subtitles?provider=subdl&limit=50"), "page size 50 restarts with limit=50");

            // Search with no matches → filtered empty state → reset clears the URL.
            var search = Find<TextBox>(shell, "SubtitlesSearch")!;
            search.Text = "no-such-release";
            await Until(() => page.Controller.State is { Loading: false, Query.Search: "no-such-release" } && Find<TextBlock>(shell, "SubtitlesEmptyTitle") != null, 6000);
            Check(Find<TextBlock>(shell, "SubtitlesEmptyTitle")?.Text == "No subtitles match these filters", "a filtered empty state uses the WebUI copy");
            Check(shell.CurrentRequest.Get("q") == "no-such-release", "release-name search is mirrored into the URL");
            await InvokeAsync(Find<Button>(shell, "SubtitlesEmptyReset"));
            await Until(() => page.Controller.State is { Loading: false, Query.Provider: null, Query.Search: null } && page.Controller.State.Items.Count > 0 && Find<Button>(shell, "SubtitlesEdit_" + page.Controller.State.Items[0].Id) != null);
            Check(shell.CurrentRequest.Query == null && search.Text.Length == 0, "Reset filters clears every URL parameter and the search box");

            // Editor: dirty guard, guarded save with the exact validator.
            var first = page.Controller.State.Items[0];
            await InvokeAsync(Find<Button>(shell, "SubtitlesEdit_" + first.Id));
            await Until(() => page.Editor?.State.Current != null && Find<Button>(shell, "SubtitlesEditSave") is { Visibility: Visibility.Visible });
            Check(page.Editor?.State.Current?.ETag == "\"v1\"", "the editor reads the record with its strong validator");
            var release = Find<TextBox>(shell, "SubtitlesEditRelease")!;
            var save = Find<Button>(shell, "SubtitlesEditSave")!;
            Check(!save.IsEnabled, "Save is disabled before any change");
            release.Text = "Edited.Release";
            await Until(() => page.Editor?.State.IsDirty == true && save.IsEnabled);
            Check(save.IsEnabled && shell.RequiresLeaveConfirmation, "a dirty draft enables Save and the shell leave guard");
            await CaptureAsync(outer, "subtitles-editor-dirty-1440x900.png");
            await InvokeAsync(save);
            await Until(() => page.Editor == null);
            Check(server.LastIfMatch == "\"v1\"" && server.LastPatchBody == "{\"release_name\":\"Edited.Release\"}", "save sends only the changed field with the exact validator");
            Check(!shell.RequiresLeaveConfirmation, "a confirmed save releases the leave guard");
            await Until(() => page.Controller.State is { Loading: false } && page.Controller.State.Items.Any(item => item.ReleaseName == "Edited.Release"));
            Check(page.Controller.State.Items.Any(item => item.ReleaseName == "Edited.Release"), "the list reloads after a confirmed save");

            // Conflict: 412 keeps the draft, disables Save, Reload re-reads in place.
            await Until(() => Find<Button>(shell, "SubtitlesEdit_" + first.Id) is { IsLoaded: true, IsEnabled: true });
            server.ConflictNextPatch = true;
            await InvokeAsync(Find<Button>(shell, "SubtitlesEdit_" + first.Id));
            await Until(() => page.Editor?.State.Current != null && Find<TextBox>(shell, "SubtitlesEditRelease") is { Visibility: Visibility.Visible });
            Find<ToggleSwitch>(shell, "SubtitlesEditHearing")!.IsOn = !page.Editor!.State.HearingImpaired;
            await Until(() => Find<Button>(shell, "SubtitlesEditSave")?.IsEnabled == true);
            await InvokeAsync(Find<Button>(shell, "SubtitlesEditSave"));
            await Until(() => page.Editor?.State.Failure != null);
            Check(page.Editor?.State is { Failure: not null, CanSave: false, IsDirty: true }, "a 412 keeps the draft and disables Save");
            await CaptureAsync(outer, "subtitles-editor-conflict-1440x900.png");
            Check(Find<TextBlock>(shell, "SubtitlesEditFailure")?.Visibility == Visibility.Visible, "the conflict is announced in the sheet");
            var patches = server.Requests.Count(request => request.StartsWith("PATCH", StringComparison.Ordinal));
            await InvokeAsync(Find<Button>(shell, "SubtitlesEditReload"));
            await Until(() => page.Editor?.State is { Failure: null, Loading: false, CanSave: true });
            Check(page.Editor?.State.Current?.ETag == "\"v3\"" && page.Editor.State.CanSave, "Reload adopts the current validator and keeps the reviewed draft");
            Check(server.Requests.Count(request => request.StartsWith("PATCH", StringComparison.Ordinal)) == patches, "no write is replayed automatically");
            await InvokeAsync(Find<Button>(shell, "SubtitlesEditCancel"));
            await Until(() => page.Editor == null);
            Check(page.Editor == null, "Cancel closes the editor");

            // Delete: confirmation reads first, one DELETE with the validator.
            var target = page.Controller.State.Items[1];
            await InvokeAsync(Find<Button>(shell, "SubtitlesDelete_" + target.Id));
            Button? primary = null;
            await Until(() => (primary = DialogButton(window, "PrimaryButton")) is { IsEnabled: true });
            Check(primary is { IsEnabled: true }, "the delete confirmation enables Delete after reading the record");
            await CaptureAsync(outer, "subtitles-delete-1440x900.png");
            await InvokeAsync(primary);
            await Until(() => DialogButton(window, "PrimaryButton") == null && !page.Controller.State.Items.Any(item => item.Id == target.Id) && !page.Controller.State.Loading);
            Check(server.Requests.Count(request => request == "DELETE /api/v2/admin/subtitles/" + target.Id) == 1, "exactly one DELETE is sent");
            Check(!page.Controller.State.Items.Any(item => item.Id == target.Id), "the deleted row leaves the list");

            foreach (var width in new[] { 1024, 800 })
            {
                Resize(window, width, 900);
                await Task.Delay(200);
                await CaptureAsync(outer, $"subtitles-list-{width}x900.png");
            }

            // Narrow width: the stats grid stacks; the table still renders.
            Resize(window, 560, 900);
            await Until(() => ((Grid)Field(page, "_stats")!).ColumnDefinitions.Count == 1);
            Check(((Grid)Field(page, "_stats")!).ColumnDefinitions.Count == 1, "stats stack into one column below 640px");
            await Until(() => Find<Button>(shell, "SubtitlesEdit_" + first.Id) is { IsLoaded: true });
            Check(((StackPanel)Find<Button>(shell, "SubtitlesEdit_" + first.Id)!.Parent).Opacity == 1, "narrow row actions remain visible");
            Resize(window, 1440, 900);

            // Retirement: a role change clears rows and closes nothing into a stale state.
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            await Until(() => page.Controller == null || page.Controller.State.Owner == null);
            Check(page.Controller == null || page.Controller.State is { Owner: null, Items.Count: 0 }, "authority retirement clears the page data");
            realtime.Silence();
        }
        finally
        {
            window.Content = previousContent;
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException($"Admin Subtitle Files fixture failed: {string.Join("; ", Failures)}");
        Program.Log("PASS: Admin Subtitle Files native fixture completed; synthetic server only, no live server, picker download or visual parity certification.");
    }

    private static void Resize(Window window, int width, int height)
    {
        var scale = window.Content?.XamlRoot?.RasterizationScale ?? 1;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(150);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, $"{name} rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private static async Task InvokeAsync(Button? button)
    {
        if (button == null) { Check(false, "expected button exists"); return; }
        button.StartBringIntoView();
        await Task.Delay(100);
        Check(button.IsLoaded && button.IsEnabled, $"{AutomationProperties.GetAutomationId(button)} is rendered and enabled before invocation");
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
        if (peer == null) throw new InvalidOperationException("The rendered button has no invocation provider.");
        peer.Invoke();
        await Task.Delay(50);
    }

    private static Button? DialogButton(Window window, string name)
    {
        if (window.Content?.XamlRoot is not { } root) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            if (popup.Child is { } child && Descendants(child).OfType<Button>().FirstOrDefault(button => button.Name == name) is { } match) return match;
        return null;
    }

    private static T? Find<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == automationId);

    private static HashSet<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(text => text.Text).ToHashSet(StringComparer.Ordinal);

    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
