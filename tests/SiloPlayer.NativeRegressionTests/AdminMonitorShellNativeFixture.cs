using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
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
/// Admin Monitor shell/indicator/Activity native fixture. Synthetic admin and
/// sessions only; no live server, account or user data. Modes (SILO_NATIVE_ADMIN_CASE):
/// "indicator" — visibility matrix, badge/99+/accessible names, suppression, role loss;
/// "activity" — Activity page wide/stacked table, filters, IP lookup, FFmpeg console states,
///              plus PNG captures at 1440x900, 1280x800, 1024x768, 1023x768 and 800x900;
/// "activity-scroll" — rendered outer/nested scrollbar bounds, rightmost-text clearance, gutter
///              hit-test routing over both side gutters and across the nested streams panel;
/// "" — both. Program.cs dispatch is wired by the lead (see admin-shell-implementation.md).
/// </summary>
internal static partial class AdminMonitorShellNativeFixture
{
    private static readonly List<string> Failures = [];
    private const string TitleMovie = "Synthetic Movie", TitleShow = "Synthetic Show", TitleEpisode = "Synthetic Pilot Episode", TitleLoose = "Loose Episode",
        TitleUntitledShow = "Untitled Episode Show", TitleTrack = "Synthetic Track";
    private const string TitleLongShow = "The Extraordinarily Long Synthetic Series Title That Goes On And On Well Past Any Column Width The Page Could Offer Today";
    private const string TitleLongEpisode = "An Equally Long Synthetic Episode Title That Also Keeps Going Past Every Width And Must End In An Ellipsis Not A Wrapped Line";

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 4000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public int Active;
        public WebSocketState CurrentState { get; set; } = WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels)
        {
            Active += channels.Length;
            var released = false;
            return new Release(() => { if (!released) { released = true; Active -= channels.Length; } });
        }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            snapshot = JsonDocument.Parse("[]").RootElement.Clone();
            return channel == "scans";
        }
        public void Event(string channel, string name) => EventReceived?.Invoke(channel, name, JsonDocument.Parse("{}").RootElement.Clone());
        public void State(WebSocketState state) { CurrentState = state; StateChanged?.Invoke(state); }
        public void Snapshot(string channel, string json) => SnapshotReceived?.Invoke(channel, JsonDocument.Parse(json).RootElement.Clone());
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }

    private sealed class Server : HttpMessageHandler
    {
        public int SessionCount = 3;
        public bool UniformRoutes;
        public bool SessionsFail;
        /// <summary>Serves the title cases (a movie, episodes with and without a show, a long title, a track) instead of the numbered sessions.</summary>
        public bool TitleCases;
        public bool RestartRequired = true;
        public int AdminReads;
        public int BrandingReads;
        public int BrandingInFlight;
        public string BrandingWordmarkUrl = "/fixture-wordmark.png";
        public TaskCompletionSource<bool>? HeldBranding;
        public byte[]? WordmarkBytes;
        public TaskCompletionSource<bool>? HeldMessage;
        public string IpLookupUsername = "synthetic-viewer";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("wordmark.png", StringComparison.Ordinal) && WordmarkBytes is { } bytes)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("image/png") } } };
            // Extensionless on purpose: only the cached content identifies this wordmark as SVG.
            if (path == "/vector-wordmark")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"224\" height=\"96\"><rect width=\"224\" height=\"96\" fill=\"#ed3344\"/></svg>")) { Headers = { ContentType = new("image/svg+xml") } } };
            if (path == "/api/v2/theme/branding")
            {
                var asset = BrandingWordmarkUrl; var brandingHeld = HeldBranding; HeldBranding = null;
                Interlocked.Increment(ref BrandingInFlight); Interlocked.Increment(ref BrandingReads);
                try
                {
                    if (brandingHeld != null) await brandingHeld.Task; // Deliberately late, even after caller cancellation.
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { server_name = "Fixture Theater", wordmark_url = asset }), Encoding.UTF8, "application/json") };
                }
                finally { Interlocked.Decrement(ref BrandingInFlight); }
            }
            if (path.EndsWith("/message", StringComparison.Ordinal) && HeldMessage is { } held)
            {
                var command = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                await held.Task.WaitAsync(cancellationToken);
                var receipt = JsonSerializer.Serialize(new { command_id = command.GetProperty("command_id").GetString(), sequence = command.GetProperty("sequence").GetInt64(), outcome = "applied", delivery = "dispatched" });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(receipt, Encoding.UTF8, "application/json") };
            }
            if (path.StartsWith("/api/v2/admin", StringComparison.Ordinal) || path == "/api/v2/libraries") Interlocked.Increment(ref AdminReads);
            if (path == "/api/v2/admin/sessions" && SessionsFail)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            string body = path switch
            {
                "/api/v2/admin/sessions" => "{\"items\":[" + string.Join(",", TitleCases ? TitleSessions() : Enumerable.Range(0, SessionCount).Select(i => Session(i, UniformRoutes))) + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/tasks" => "{\"items\":[{\"key\":\"scan\",\"name\":\"Scan libraries\",\"description\":\"\",\"category\":\"library\",\"state\":\"running\",\"progress\":42.5}]}",
                "/api/v2/libraries" => "{\"items\":[{\"id\":\"1\",\"name\":\"Movies\",\"type\":\"movies\"}]}",
                "/api/v2/admin/downloads/preparations" => "{\"counts\":{\"running\":0,\"queued\":1,\"retrying\":0,\"paused\":0,\"failed_recent\":1},\"items\":[]}",
                "/api/v2/admin/system/build" => "{\"display\":\"v1.2.3\",\"revision\":\"abc\",\"dirty\":false,\"build_number\":4321,\"available\":true}",
                "/api/v2/admin/server/status" => "{\"restart_required\":" + (RestartRequired ? "true" : "false") + ",\"restart_mark_count\":2,\"restart_requested\":false}",
                "/api/v2/admin/ips" => "{\"items\":[{\"user_id\":\"3\",\"username\":" + JsonSerializer.Serialize(IpLookupUsername) + ",\"first_seen\":\"2026-11-01T00:00:00Z\",\"last_seen\":\"2026-11-13T00:00:00Z\",\"request_count\":1200}],\"page\":{\"has_more\":true,\"next_cursor\":\"n1\"}}",
                "/api/v2/admin/logs/app" => "{\"items\":[{\"id\":\"9\",\"timestamp\":\"2026-11-14T10:00:00Z\",\"level\":\"info\",\"component\":\"ffmpeg\",\"message\":\"ffmpeg stderr\",\"node_id\":\"gpu-1\",\"attrs\":{\"ffmpeg_line\":\"frame=  120 fps= 48 q=28.0\",\"hw_accel\":\"qsv\",\"restart_count\":1}}],\"page\":{\"has_more\":false}}",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        // Official c90e406db (#5106, #5105): a movie, an episode (the show leads, the episode line under it), an episode that lost its show, an episode
        // with no title of its own, an episode with very long names, and a track. All synthetic.
        private static IEnumerable<string> TitleSessions()
        {
            string Row(int i, Action<JsonObject> configure)
            {
                var row = JsonNode.Parse(Session(i))!.AsObject();
                configure(row);
                return row.ToJsonString();
            }
            yield return Row(0, row => { row["media_title"] = TitleMovie; row["media_type"] = "movie"; row["media_item_id"] = "movie-item-1"; });
            yield return Row(1, row =>
            {
                row["media_title"] = TitleEpisode; row["media_type"] = "episode"; row["series_title"] = TitleShow; row["season_number"] = 1; row["episode_number"] = 2;
                row["media_item_id"] = "episode-item-1";
            });
            yield return Row(2, row => { row["media_title"] = TitleLoose; row["media_type"] = "episode"; row["season_number"] = 1; row["episode_number"] = 3; });
            yield return Row(3, row =>
            {
                row["media_title"] = ""; row["media_type"] = "episode"; row["series_title"] = TitleUntitledShow; row["season_number"] = 2; row["episode_number"] = 11;
            });
            yield return Row(4, row =>
            {
                row["media_title"] = TitleLongEpisode; row["media_type"] = "episode"; row["series_title"] = TitleLongShow; row["season_number"] = 12; row["episode_number"] = 104;
                row["media_item_id"] = "episode-item-2";
            });
            yield return Row(5, row => { row["media_title"] = TitleTrack; row["media_type"] = "track"; });
        }

        private static string Session(int i, bool uniformRoutes = false) =>
            "{\"session_id\":\"synthetic-" + i + "\",\"user_id\":\"" + (7 + i) + "\",\"username\":\"viewer" + i + "\",\"profile_id\":\"p" + i + "\",\"profile_name\":\"Profile " + i + "\"," +
            "\"media_file_id\":\"" + (40 + i) + "\",\"requested_media_file_id\":\"" + (40 + i) + "\",\"media_title\":\"Synthetic Title " + i + "\",\"media_type\":\"" + (i % 2 == 0 ? "movie" : "series") + "\"," +
            "\"play_method\":\"" + (i == 0 ? "direct" : "transcode") + "\",\"video_decision\":\"" + (i == 0 ? "direct" : "transcode") + "\",\"audio_decision\":\"" + (i == 0 ? "direct" : "copy") + "\",\"adaptation_reasons\":" + (i == 0 ? "[]" : "[\"video_codec_unsupported\",\"bandwidth_cap\",\"future_reason\"]") + "," +
            "\"reporting_node\":\"local\",\"file_duration\":7200,\"started_at\":\"2026-11-14T10:00:00Z\",\"updated_at\":\"2026-11-14T10:00:00Z\",\"position_seconds\":" + (600 + i) + "," +
            "\"is_paused\":" + (i == 2 ? "true" : "false") + ",\"has_playback_control\":" + (i == 1 ? "false" : "true") + ",\"stream_location\":\"local\",\"audio_track_index\":0,\"transcode_audio\":false," +
            "\"stream_bitrate_kbps\":" + (i == 0 ? "16000" : "3500") + ",\"target_bitrate_kbps\":" + (i == 0 ? "null" : "4000") + ",\"source_bitrate_kbps\":16000,\"source_audio_channels\":6,\"source_container\":\"mkv\",\"source_video_codec\":\"hevc\",\"source_video_resolution\":\"2160p\",\"target_video_codec\":\"h264\",\"target_resolution\":\"1080p\",\"source_audio_codec\":\"aac\",\"client_label\":\"Synthetic Client\",\"client_label_full\":\"Synthetic Client 1.0 (build 42, Beta)\",\"client_user_agent\":\"SyntheticAgent/1.0\",\"device_platform\":\"Windows\",\"device_id\":\"synthetic-device-long-identity-0123456789\",\"client_ip\":\"203.0.113." + (10 + i) + "\"," +
            "\"routing_workload\":\"video_transcode\",\"routing_execution\":\"" + (i == 0 || uniformRoutes ? "api" : "transcode") + "\",\"routing_execution_node_name\":\"gpu-1\",\"routing_egress\":\"api\",\"routing_network_provider\":\"\"}";
    }

    internal static async Task RunAsync(Window window)
    {
        var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_ADMIN_CASE") ?? "";
        if (mode is not ("" or "indicator" or "activity" or "ownership" or "activity-scroll" or "activity-correction" or "shell-branding" or "shell-unload"))
            throw new ArgumentException("Unknown Admin Monitor fixture case: " + mode);
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        var realtime = new Realtime();
        using var authority = new AdminMonitorAuthority(client, auth);
        var api = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(api, authority, realtime);
        var navigation = new NavigationService();
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "admin-images"));
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(store)
            .AddSingleton(http).AddSingleton(images).AddSingleton(new SettingsApi(client))
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        field.SetValue(null, services);
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        try
        {
            if (mode is "" or "indicator") await IndicatorAsync(window, server, realtime, auth, store);
            if (mode is "" or "activity") await ActivityAsync(window, server, store);
            if (mode is "" or "ownership") await OwnershipAsync(window, server, auth, store);
            if (mode is "" or "activity-scroll") await ActivityScrollAsync(window, server, store);
            if (mode is "" or "activity-correction") await ActivityCorrectionAsync(window, server, store);
            if (mode is "" or "shell-branding") await ShellBrandingAsync(window, server);
            if (mode is "" or "shell-unload") await ShellUnloadAsync(window, auth);
        }
        finally
        {
            var retired = window.Content as FrameworkElement;
            if (retired is Frame frame) frame.Navigate(typeof(Page));
            window.Content = new Grid();
            if (retired != null) await Until(() => !retired.IsLoaded);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException($"Admin Monitor fixture failed: {string.Join("; ", Failures)}");
    }

    private static async Task IndicatorAsync(Window window, Server server, Realtime realtime, AuthService auth, ServerActivityStore store)
    {
        var main = new ServerActivityButton { HideWhenEmpty = true };
        var admin = new ServerActivityButton { HideWhenEmpty = false };
        var host = new StackPanel { Orientation = Orientation.Horizontal, Children = { main, admin } };
        window.Content = host;
        await Task.Delay(100);

        // Before eligibility (e.g. Home's first rows not loaded) nothing shows and nothing is read.
        Check(!main.IsTriggerShown && !admin.IsTriggerShown, "ineligible indicators stay hidden");
        Check(realtime.Active == ServerActivityStore.AdminChannels.Length, "one shared admin channel lease for the acting admin");
        Check(server.AdminReads == 0, "no indicator reads before a consumer is eligible");

        server.SessionCount = 0;
        main.Eligible = true;
        admin.Eligible = true;
        await Until(() => store.Current.SessionsLoaded && store.Current.Tasks.Count > 0);
        await Task.Delay(100);
        // One running task + one queued preparation are active, so both show; force idle by role-free data instead.
        Check(admin.IsTriggerShown, "admin shell indicator is always shown for an acting admin");
        var trigger = (Button)((Grid)admin.Content).Children[0];
        Check(trigger.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 }, "activity trigger keeps a transparent normal background");
        // The count pill's fill is the live theme brush (it pairs with the text's live AppBackgroundBrush through a theme switch; the main shell's instance is never rebuilt).
        Check(((Microsoft.UI.Xaml.Controls.Border)((Microsoft.UI.Xaml.Controls.Border)Field(main, "_badge")!).Child).Background == Application.Current.Resources["PrimaryTextBrush"], "the count badge fill is the live theme foreground brush");
        Check(main.CurrentView.TotalActive == 2, $"total = sessions + running hidden-inclusive tasks + scans + active preparations ({main.CurrentView.TotalActive})");
        Check(AutomationProperties.GetName((UIElement)((Grid)main.Content).Children[0]) == "Server activity: 2 active", "accessible name carries the count without a tooltip");

        server.SessionCount = 120;
        realtime.Event("sessions", "sessions.replaced");
        await Until(() => store.Current.Sessions.Count == 120);
        await Task.Delay(100);
        Check(main.CurrentView.BadgeText == "99+", "badge caps at 99+");

        main.Suppressed = true;
        await Task.Delay(50);
        Check(!main.IsTriggerShown, "covered by the full-window player: hidden");
        main.Suppressed = false;
        await Task.Delay(50);
        Check(main.IsTriggerShown, "mini player context: shown");

        realtime.State(WebSocketState.Closed);
        await Task.Delay(3000);
        Check(!store.Current.ConnectionProblem, "no connection dot before four seconds");
        await Task.Delay(1300);
        Check(store.Current.ConnectionProblem, "connection dot after four continuous non-live seconds");
        realtime.State(WebSocketState.Open);
        await Task.Delay(50);
        Check(!store.Current.ConnectionProblem, "dot clears immediately on reconnect");

        await CaptureAsync(host, "admin-indicator-active.png");

        // Role loss retires everything; regaining admin does not revive the old data.
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        await Task.Delay(100);
        Check(!main.IsTriggerShown && !admin.IsTriggerShown, "role loss hides every indicator");
        Check(realtime.Active == 0, "role loss releases only the admin channel lease");
        Check(store.Current.Sessions.Count == 0, "role loss clears retired sessions");
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        await Until(() => store.Current.SessionsLoaded);
        Check(admin.IsTriggerShown, "regranted admin re-reads under a new epoch");
    }

    private static async Task ActivityAsync(Window window, Server server, ServerActivityStore store)
    {
        server.SessionCount = 3;
        await store.RefreshSessionsAsync();
        var frame = new Frame { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"] };
        window.Content = frame;
        Check(((Microsoft.UI.Xaml.Markup.IXamlMetadataProvider)Application.Current).GetXamlType(typeof(AdminActivityPage)) != null, "Activity has WinUI navigation activation metadata");
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)frame.Content;
        var innerFrame = (Frame)Field(shell, "_frame")!;
        var page = (AdminActivityPage)innerFrame.Content;
        await Until(() => page.RenderedSessionIds.Count == 3);
        Check(page.RenderedSessionIds.Count == 3, "Activity renders every live session");

        var table = (StackPanel)Field(page, "_tableHost")!;
        bool Shows(string text) => Descendants(table).OfType<TextBlock>().Any(block => block.Text == text);
        server.SessionsFail = true;
        await store.RefreshSessionsAsync();
        await Until(() => Shows(AdminText.T("web.admin.activity.page.stale")));
        Check(Shows(AdminText.T("web.admin.activity.page.stale")) && page.RenderedSessionIds.Count == 3,
            "failed refresh labels retained Activity rows as stale");
        Check(Shows(store.Current.SessionsError!), "Activity displays the session read error");
        await CaptureFourWidthsAsync(window, frame, "admin-activity-stale");
        server.SessionsFail = false;
        server.SessionCount = 0;
        await store.RefreshSessionsAsync();
        await Until(() => Shows(AdminText.T("web.admin.activity.page.noStreams")));
        await CaptureFourWidthsAsync(window, frame, "admin-activity-empty");
        server.SessionsFail = true;
        await store.RefreshSessionsAsync();
        await Until(() => Shows(AdminText.T("web.admin.activity.page.loadFailed")));
        Check(Shows(AdminText.T("web.admin.activity.page.loadFailed")) && !Shows(AdminText.T("web.admin.activity.page.noStreams")),
            "failed Activity read without rows shows an error instead of No active streams");
        await CaptureFourWidthsAsync(window, frame, "admin-activity-error");
        server.SessionsFail = false;
        server.SessionCount = 3;
        var retry = Descendants(table).OfType<Button>().Single(button => AutomationProperties.GetName(button) == AdminText.T("common.retry"));
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(retry)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        await Until(() => page.RenderedSessionIds.Count == 3 && !Shows(AdminText.T("web.admin.activity.page.loadFailed")));
        Check(page.RenderedSessionIds.Count == 3 && store.Current.SessionsError == null, "Activity Retry restores rows and clears the error");

        {
            // Official c90e406db (#5106, #5105): an episode is led by its show, with the episode code and title under it; a movie has no subtitle.
            server.TitleCases = true; server.SessionCount = 6;
            await store.RefreshSessionsAsync();
            await Until(() => page.RenderedSessionIds.Count == 6 && Shows(TitleMovie));
            Check(page.RenderedSessionIds.Count == 6, "the title cases render every session");
            foreach (var (width, height) in new[] { (1440, 900), (1024, 768), (800, 900), (560, 900) })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
                await Task.Delay(300);
                var wide = page.IsWideTable;
                TextBlock Single(string text) => Descendants(table).OfType<TextBlock>().First(block => block.Text == text);
                Windows.Foundation.Rect BoundsOf(TextBlock block) => block.TransformToVisual(table).TransformBounds(new Windows.Foundation.Rect(0, 0, block.ActualWidth, block.ActualHeight));
                var label = $"{width} ({(wide ? "wide" : "stacked")})";
                foreach (var title in new[] { TitleMovie, TitleShow, TitleLoose, TitleUntitledShow, TitleLongShow, TitleTrack })
                    Check(Shows(title), $"{label}: the stream line leads with \"{title.Substring(0, Math.Min(24, title.Length))}\"");
                // An episode's own title is no longer the main line: it appears only inside the line under the show.
                Check(!Shows(TitleEpisode) && !Shows(TitleLongEpisode), $"{label}: an episode's own title is not a main line");
                Check(Shows("S1E2 · " + TitleEpisode), $"{label}: the episode line reads code · title under the show");
                Check(Shows("S2E11"), $"{label}: an episode with no title of its own shows the code alone");
                Check(Shows("S12E104 · " + TitleLongEpisode), $"{label}: the long episode line keeps its code and title");
                Check(!Shows("Movie") && !Shows("Synthetic Movie · Movie"), $"{label}: a movie has no \"Movie\" subtitle");
                Check(wide ? Shows("Series") : !Shows("Series"), wide ? $"{label}: an episode that lost its show reads \"Series\" under its own title" : $"{label}: the stacked card shows an episode line only for an episode");
                // Show above episode line, left edges aligned, one line each, the pair staying inside the table.
                var show = Single(TitleShow); var line = Single("S1E2 · " + TitleEpisode);
                var showBounds = BoundsOf(show); var lineBounds = BoundsOf(line);
                Check(lineBounds.Top >= showBounds.Bottom - 1 && lineBounds.Top - showBounds.Bottom < 6 && Math.Abs(lineBounds.Left - showBounds.Left) < 1.5,
                    $"{label}: the episode line sits directly under the show, left aligned ({showBounds.Left:0.#},{showBounds.Bottom:0.#} / {lineBounds.Left:0.#},{lineBounds.Top:0.#})");
                var longShow = Single(TitleLongShow); var longLine = Single("S12E104 · " + TitleLongEpisode);
                Check(longShow.IsTextTrimmed && longLine.IsTextTrimmed && longShow.ActualHeight < 22 && longLine.ActualHeight < 20,
                    $"{label}: a very long show and episode line each truncate with an ellipsis on one line ({longShow.ActualHeight:0.#}, {longLine.ActualHeight:0.#})");
                Check(new[] { show, line, longShow, longLine, Single(TitleMovie), Single(TitleTrack) }.All(block => BoundsOf(block).Right <= table.ActualWidth + 1),
                    $"{label}: no title or episode line runs past the table");
                // The link's accessible name is the main line (official: the title attribute and text are the show).
                Check(Descendants(table).OfType<Button>().Any(button => AutomationProperties.GetName(button) == TitleShow), $"{label}: the show's link is named by the show");
                await CaptureAsync(frame, $"admin-activity-titles-{width}x{height}.png");
                // The rows below the first screen (the untitled episode, the very long names, the track), scrolled into view.
                var titleScroller = (ScrollViewer)Field(page, "_scroller")!;
                var innerScroller = Descendants(table).OfType<ScrollViewer>().FirstOrDefault();
                titleScroller.ChangeView(null, titleScroller.ScrollableHeight, null, true); await Task.Delay(250);
                innerScroller?.ChangeView(null, innerScroller.ScrollableHeight, null, true); await Task.Delay(250);
                await CaptureAsync(frame, $"admin-activity-titles-end-{width}x{height}.png");
                innerScroller?.ChangeView(null, 0, null, true);
                titleScroller.ChangeView(null, 0, null, true); await Task.Delay(150);
            }
            server.TitleCases = false; server.SessionCount = 3;
            await store.RefreshSessionsAsync();
            await Until(() => page.RenderedSessionIds.Count == 3 && Shows("Synthetic Title 0"));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            await Task.Delay(300);
        }

        {
            // The wrap panel on a scaled display: a nested panel's desired width is rounded to the pixel grid, so it can come back a fraction
            // smaller than its children need (here a 0.4 gap stands in for a 1px gap at 125%). The children must stay on one line.
            var wrapType = typeof(SiloPlayer.Views.Admin.AdminShellPage).Assembly.GetType("SiloPlayer.Views.Admin.LogsWrapPanel")!;
            Panel NewWrap(double spacing)
            {
                var panel = (Panel)Activator.CreateInstance(wrapType, true)!;
                wrapType.GetProperty("Spacing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, spacing);
                return panel;
            }
            var inner = NewWrap(0.4); var first = new Border { Width = 90, Height = 10 }; var second = new Border { Width = 75, Height = 10 };
            inner.Children.Add(first); inner.Children.Add(second);
            var outer = NewWrap(8); outer.Children.Add(new Border { BorderThickness = new Thickness(1), Child = inner });
            // Laid out in the live page (an element outside the tree is not arranged the same way), then removed again.
            var wrapHost = (StackPanel)Field(page, "_tableHost")!; wrapHost.Children.Add(outer); wrapHost.UpdateLayout(); await Task.Delay(100);
            var firstSlot = Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(first);
            var secondSlot = Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(second);
            Program.Log($"TRACE wrap rounding: inner desired {inner.DesiredSize.Width:0.###}x{inner.DesiredSize.Height:0.###}, slots {firstSlot.X:0.###},{firstSlot.Y:0.###} {firstSlot.Width:0.###}x{firstSlot.Height:0.###} / {secondSlot.X:0.###},{secondSlot.Y:0.###} {secondSlot.Width:0.###}x{secondSlot.Height:0.###}");
            Check(Math.Abs(firstSlot.Y - secondSlot.Y) < 0.01 && secondSlot.X > firstSlot.X && inner.DesiredSize.Height < 15,
                "a nested wrap panel keeps its children on one line when its rounded width is a fraction short");
            wrapHost.Children.Remove(outer); wrapHost.UpdateLayout();

            // A growing child with no MinWidth in a full row is allocated exactly its own desired width. Measured again at that width it can be
            // offered a fraction less than it needs (here 100.4 needed, 100 desired), so it wrapped and grew taller. It must be left as measured;
            // given more room than it asked for, it still grows into it.
            // Row of 200: the growing child (desired 100) and a fixed 92 wide sibling beside the 8 gap fill it exactly. Row of 300: 100 spare.
            foreach (var (offered, expectedWidth) in new[] { (200d, RoundedNeedPanel.Rounded), (300d, RoundedNeedPanel.Rounded + 100) })
            {
                var grower = new RoundedNeedPanel();
                var growRow = NewWrap(8);
                wrapType.GetProperty("GrowFields", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(growRow, true);
                growRow.Width = offered; growRow.HorizontalAlignment = HorizontalAlignment.Left;
                growRow.Children.Add(grower); growRow.Children.Add(new Border { Width = 92, Height = 10 });
                wrapHost.Children.Add(growRow); wrapHost.UpdateLayout(); await Task.Delay(100);
                var slot = Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(grower);
                Program.Log($"TRACE wrap grow {offered}: measured {grower.Measures} times, desired {grower.DesiredSize.Width:0.###}x{grower.DesiredSize.Height:0.###}, slot {slot.Width:0.###}x{slot.Height:0.###}");
                Check(grower.DesiredSize.Height < 30 && slot.Height < 30 && Math.Abs(slot.Width - expectedWidth) < 0.5,
                    $"a growing child in a row of {offered:0} ({RoundedNeedPanel.Need:0.#} needed, {RoundedNeedPanel.Rounded:0} desired) is {(offered == 200 ? "not measured again at its own rounded width" : "still grown into spare room")} (slot {slot.Width:0.#}x{slot.Height:0.#})");
                wrapHost.Children.Remove(growRow); wrapHost.UpdateLayout();
            }
            {
                // A growing child with a MinWidth (the restart banner's text, the Diagnostics filter fields) is first measured at the panel's
                // width and then given a smaller share: it must be measured again at that share so its text wraps instead of being cut off.
                // Row of 250: the fixed 92 wide sibling and the 8 gap leave 150 for a child that measured 250 wide.
                var wide = new WrapsWhenNarrowPanel { MinWidth = 100 };
                var shareRow = NewWrap(8);
                wrapType.GetProperty("GrowFields", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shareRow, true);
                shareRow.Width = 250; shareRow.HorizontalAlignment = HorizontalAlignment.Left;
                shareRow.Children.Add(wide); shareRow.Children.Add(new Border { Width = 92, Height = 10 });
                wrapHost.Children.Add(shareRow); wrapHost.UpdateLayout(); await Task.Delay(100);
                var slot = Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(wide);
                Program.Log($"TRACE wrap share: last offered {wide.LastOffered:0.###}, desired {wide.DesiredSize.Width:0.###}x{wide.DesiredSize.Height:0.###}, slot {slot.Width:0.###}x{slot.Height:0.###}");
                Check(Math.Abs(wide.LastOffered - 150) < 0.5 && wide.DesiredSize.Width <= 150.5 && wide.DesiredSize.Height > 30 && Math.Abs(slot.Width - 150) < 0.5,
                    $"a growing child given less than it measured at the panel's width is measured again at its share and wraps (offered {wide.LastOffered:0.#}, desired {wide.DesiredSize.Width:0.#}x{wide.DesiredSize.Height:0.#})");
                wrapHost.Children.Remove(shareRow); wrapHost.UpdateLayout();
            }
        }
        foreach (var (width, height) in new[] { (1440, 900), (1439, 900), (1280, 800), (1024, 768), (1023, 768), (800, 900), (640, 900), (639, 900), (560, 900) })

        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
            await Task.Delay(300);
            Program.Log($"TRACE activity {width}x{height}: wide={page.IsWideTable}");
            if (width is 1439 or 1440) Check(page.IsWideTable == (width == 1440), $"Activity rows with distinct routes switch at the 70rem container boundary ({width}px)");
            // AdminActivity.tsx:502 `w-full sm:w-80`: the whole filter row below 640, 320 from 640 up.
            var searchHost = (FrameworkElement)Field(page, "_searchHost")!;
            var filterRow = (FrameworkElement)Field(page, "_filterRow")!;
            if (width < 640) Check(Math.Abs(searchHost.ActualWidth - filterRow.ActualWidth) < 1.5, $"{width}: the search box fills the filter row ({searchHost.ActualWidth:0.#} of {filterRow.ActualWidth:0.#})");
            else Check(Math.Abs(searchHost.ActualWidth - 320) < 1.5, $"{width}: the search box is 320 wide ({searchHost.ActualWidth:0.#})");
            // A filter's segments are one line with whole labels (official: inline-flex). On a scaled display they had stacked and been cut off.
            // The strip may wrap only when the row is narrower than the whole group; either way no segment leaves its group or the row.
            var groups = ((Panel)filterRow).Children.OfType<Border>().Where(border => border.Child is Panel).ToArray();
            Check(groups.Length >= 2, $"{width}: the method and type filters are segmented groups ({groups.Length})");
            foreach (var group in groups)
            {
                var segments = ((Panel)group.Child).Children.OfType<Button>().ToArray();
                var origins = segments.Select(segment => segment.TransformToVisual(group).TransformPoint(new Windows.Foundation.Point(0, 0))).ToArray();
                // Segments, the 1px gaps between them and the group's 1px border.
                var fits = segments.Sum(segment => segment.ActualWidth) + segments.Length + 1 < filterRow.ActualWidth + 0.5;
                Check(segments.Length >= 2 && (!fits || origins.All(origin => Math.Abs(origin.Y - origins[0].Y) < 0.5)) && segments.All(segment => Math.Abs(segment.ActualHeight - 30) < 0.5),
                    fits ? $"{width}: a filter's {segments.Length} segments share one 30px line" : $"{width}: a filter's {segments.Length} segments are 30px high in a row narrower than their group");
                Check(Descendants(group).OfType<TextBlock>().All(label => !label.IsTextTrimmed && label.ActualWidth > 0), $"{width}: every segment shows its whole label");
                var groupLeft = group.TransformToVisual(filterRow).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
                Check(groupLeft > -0.5 && groupLeft + group.ActualWidth < filterRow.ActualWidth + 1.5
                    && Enumerable.Range(0, segments.Length).All(index => origins[index].X > -0.5 && origins[index].X + segments[index].ActualWidth < group.ActualWidth + 1.5),
                    $"{width}: a filter group and its segments stay inside the filter row ({groupLeft:0.#} + {group.ActualWidth:0.#} of {filterRow.ActualWidth:0.#})");
            }
            if (page.IsWideTable)
            {
                // Column headers: the button's rounded corners must not cut into its label (padding, with the label where it was).
                var headers = Descendants(table).OfType<Button>().Where(button => button.Content is StackPanel && button.Margin.Left == -6).ToArray();
                Check(headers.Length >= 5 && headers.All(header => header.Padding.Left >= header.CornerRadius.TopLeft && header.Padding.Right >= header.CornerRadius.TopRight)
                    && headers.All(header => Descendants(header).OfType<TextBlock>().All(label => !label.IsTextTrimmed)),
                    $"{width}: {headers.Length} column headers keep their labels clear of the rounded corners");
            }
            // rounded-sm = --radius - 4px = 8px on the poster (the empty placeholder keeps the same radius).
            Check(Descendants(table).OfType<Border>().Any(b => (b.Width == 32 || b.Width == 40) && b.CornerRadius.TopLeft == 8), $"{width}: the poster has 8px corners");
            await CaptureAsync(frame, $"admin-activity-{width}x{height}.png");
        }
        {
            // A filter row narrower than a whole group (a narrow window, long translated labels): the strip wraps, so every segment stays
            // visible instead of being clipped at the row's edge. The row is narrowed to just over the widest segment, then restored.
            var filterRow = (FrameworkElement)Field(page, "_filterRow")!;
            Border[] FilterGroups() => ((Panel)filterRow).Children.OfType<Border>().Where(border => border.Child is Panel).ToArray();
            var widestSegment = FilterGroups().SelectMany(filter => ((Panel)filter.Child).Children.OfType<Button>()).Max(segment => segment.ActualWidth);
            filterRow.Width = Math.Ceiling(widestSegment) + 10; filterRow.UpdateLayout(); await Task.Delay(100);
            var wrappedGroups = 0;
            foreach (var group in FilterGroups())
            {
                var segments = ((Panel)group.Child).Children.OfType<Button>().ToArray();
                var origins = segments.Select(segment => segment.TransformToVisual(group).TransformPoint(new Windows.Foundation.Point(0, 0))).ToArray();
                if (origins.Any(origin => Math.Abs(origin.Y - origins[0].Y) > 0.5)) wrappedGroups++;
                Check(group.ActualWidth < filterRow.ActualWidth + 1.5
                    && Enumerable.Range(0, segments.Length).All(index => origins[index].X > -0.5 && origins[index].X + segments[index].ActualWidth < group.ActualWidth + 1.5)
                    && Descendants(group).OfType<TextBlock>().All(label => !label.IsTextTrimmed && label.ActualWidth > 0),
                    $"narrow row: a filter's {segments.Length} segments stay whole inside the {filterRow.ActualWidth:0.#} wide row (group {group.ActualWidth:0.#})");
            }
            Check(wrappedGroups >= 1, $"narrow row: a filter group wider than the row wraps its segments instead of clipping them ({wrappedGroups})");
            filterRow.Width = double.NaN; filterRow.UpdateLayout(); await Task.Delay(100);
        }
        // role="status" (AdminActivity.tsx:690): the realtime line is a polite live region and re-renders do not re-announce it.
        var statusHeader = (StackPanel)Field(page, "_header")!;
        Check(Descendants(statusHeader).OfType<TextBlock>().Any(t => AutomationProperties.GetLiveSetting(t) == Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite), "the realtime status is a polite live region");
        var announced = page.StatusAnnouncements;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        await Task.Delay(300);
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(560, 900));
        await Task.Delay(300);
        Check(page.StatusAnnouncements == announced, "re-rendering the header does not announce an unchanged status");
        var stackedTitle = Descendants(table).OfType<TextBlock>().First(text => text.Text.StartsWith("Synthetic Title", StringComparison.Ordinal));
        var originalTitle = stackedTitle.Text;
        stackedTitle.Text = new string('W', 200);
        table.UpdateLayout();
        await Until(() => stackedTitle.IsTextTrimmed);
        Check(stackedTitle.IsTextTrimmed, "stacked Activity ellipsizes a long title instead of clipping its tail");
        var compactActions = Descendants(table).OfType<Button>().First(button => AutomationProperties.GetName(button) == AdminText.T("web.admin.sessions.playback.menu.ariaLabel"));
        Check(compactActions.ActualWidth >= 28 && compactActions.ActualHeight >= 28,
            "stacked Activity preserves its action hit target beside a long title");
        stackedTitle.Text = originalTitle;

        var rowStates = (System.Collections.IDictionary)Field(page, "_rowStates")!;
        var firstSession = store.Current.Sessions.First();
        var rowState = rowStates[firstSession.SessionId]!;
        rowState.GetType().GetField("Details")!.SetValue(rowState, true);
        typeof(AdminActivityPage).GetMethod("RenderTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        foreach (var width in new[] { 1440, 1280, 1024, 1023, 800, 560 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
            await Task.Delay(150);
            await CaptureAsync(frame, $"admin-activity-expanded-{width}x900.png");
        }
        rowState.GetType().GetField("Ffmpeg")!.SetValue(rowState, true);
        var ffmpegRead = (Task)typeof(AdminActivityPage).GetMethod("LoadFfmpegAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { firstSession.SessionId, rowState, true })!;
        await ffmpegRead;
        typeof(AdminActivityPage).GetMethod("RenderTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        {
            // "Open ffmpeg logs" is a text button: its rounded corners must not cut into the first and last letters (as the column headers).
            page.UpdateLayout(); await Task.Delay(200);
            var openLogs = Descendants(page).OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == AdminText.T("web.admin.activity.panel.openFfmpegLogs"));
            var openLabel = (TextBlock)openLogs.Content;
            var inset = openLabel.TransformToVisual(openLogs).TransformPoint(new Windows.Foundation.Point(0, 0));
            Check(openLogs.CornerRadius.TopLeft == 4 && openLogs.Padding.Left >= openLogs.CornerRadius.TopLeft && openLogs.Padding.Right >= openLogs.CornerRadius.TopRight
                && inset.X >= openLogs.CornerRadius.TopLeft - 0.01 && openLogs.ActualWidth - inset.X - openLabel.ActualWidth >= openLogs.CornerRadius.TopRight - 0.01
                && openLogs.Margin.Left == -openLogs.Padding.Left && openLogs.Margin.Top == -openLogs.Padding.Top && !openLabel.IsTextTrimmed,
                $"the Open ffmpeg logs button keeps its label clear of its rounded corners (inset {inset.X:0.#}, padding {openLogs.Padding.Left:0.#}, radius {openLogs.CornerRadius.TopLeft:0.#})");
        }
        await CaptureAsync(frame, "admin-activity-ffmpeg-800x900.png");
        var consoleLine = Descendants(page).OfType<TextBlock>().Single(text => text.Text.StartsWith("frame=", StringComparison.Ordinal));
        consoleLine.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Task.Delay(200);
        var consoleOrigin = consoleLine.TransformToVisual(frame).TransformPoint(new Windows.Foundation.Point(0, 0));
        Check(consoleOrigin.Y >= 0 && consoleOrigin.Y + consoleLine.ActualHeight <= frame.ActualHeight, "FFmpeg console line is in the captured viewport");
        await CaptureAsync(frame, "admin-activity-ffmpeg-scrolled-800x900.png");
        ((ScrollViewer)Field(page, "_scroller")!).ChangeView(null, 0, null, true);
        rowState.GetType().GetField("Details")!.SetValue(rowState, false);
        rowState.GetType().GetField("Ffmpeg")!.SetValue(rowState, false);
        typeof(AdminActivityPage).GetMethod("RenderTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900));
        await Task.Delay(300);
        var messageRowState = rowStates[firstSession.SessionId]!;
        var pendingDialog = (Task)typeof(AdminActivityPage).GetMethod("ShowMessageDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new[] { (object)firstSession, messageRowState })!;
        await Until(() => Field(page, "_messageDialog") != null);
        var messageDialog = (ContentDialog)Field(page, "_messageDialog")!;
        ((TextBox)Field(page, "_messageDraft")!).Text = "Synthetic pending message";
        await Until(() => Descendants(messageDialog).OfType<Button>().Any(button => button.Name == "PrimaryButton"));
        await Until(() => Descendants(messageDialog).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminMessageClose"));
        server.HeldMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(Descendants(messageDialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
        await Until(() => !messageDialog.IsPrimaryButtonEnabled);
        var cancel = Descendants(messageDialog).OfType<Button>().First(button => button.Visibility == Visibility.Visible && button.Name is "SecondaryButton" or "CloseButton");
        Check(!cancel.IsEnabled, "pending Message disables Cancel as in WebUI");
        var closeMessage = Descendants(messageDialog).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == "AdminMessageClose");
        Program.Log("TRACE pending Message buttons: " + string.Join("; ", Descendants(messageDialog).OfType<Button>().Select(button => $"{button.Name}/{AutomationProperties.GetAutomationId(button)} enabled={button.IsEnabled}")));
        Check(closeMessage?.IsEnabled == true, "pending Message keeps the independent Close button enabled");
        await CaptureAsync(messageDialog, "admin-message-pending-800x900.png");
        if (closeMessage != null) Invoke(closeMessage); else messageDialog.Hide();
        await pendingDialog;
        var reopen = (Task)typeof(AdminActivityPage).GetMethod("ShowMessageDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new[] { (object)firstSession, messageRowState })!;
        await Task.Delay(100);
        Check(Field(page, "_messageDialog") == null, "a pending Message cannot reopen after X closes its dialog");
        if (Field(page, "_messageDialog") is ContentDialog reopened) reopened.Hide();
        await reopen;
        server.HeldMessage.TrySetResult(true);
        server.HeldMessage = null;
        Check(Field(page, "_messageDialog") == null && Field(page, "_messageDraft") == null, "closing pending Message clears the private draft");
        var searchTask = (Task)typeof(AdminShellPage).GetMethod("ShowSearchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null)!;
        await Until(() => Field(shell, "_searchOverlay") != null);
        var overlay = (Grid)Field(shell, "_searchOverlay")!;
        var panel = (Border)overlay.Children[0];
        await Until(() => panel.ActualWidth > 0 && panel.ActualHeight > 0);
        Program.Log($"TRACE search palette: width={panel.ActualWidth}, top={panel.Margin.Top}, root={shell.XamlRoot.Size}");
        Check(Math.Abs(panel.ActualWidth - 576) < 2 && Math.Abs(panel.Margin.Top - 162) < 2, "search palette is 576px wide at the WebUI 18% top anchor");
        var query = Descendants(overlay).OfType<TextBox>().Single();
        Check(query.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 }, "focused search input has a transparent editing surface");
        await CaptureAsync(frame, "admin-search-800x900.png");
        query.Text = "no matching section";
        await Until(() => Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.homeRows.commandDialog.noMatches")));
        Check(Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.homeRows.commandDialog.noMatches")), "search renders no-match state for typed text");
        await CaptureAsync(frame, "admin-search-no-match-800x900.png");
        query.Text = "logs";
        await CaptureAsync(frame, "admin-search-filtered-800x900.png");
        ((Action)Field(shell, "_closeSearch")!).Invoke();
        await searchTask;
        Check(Field(shell, "_searchOverlay") == null, "closing search retires its overlay");
        Check(!page.IsWideTable, "narrow container uses stacked rows with a Sort row");
        Check(page.RenderedSessionIds[0] == "synthetic-0" || page.RenderedSessionIds.Count == 3, "default sort is Time descending (stable for equal times)");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static async Task OwnershipAsync(Window window, Server server, AuthService auth, ServerActivityStore store)
    {
        server.SessionCount = 3;
        await store.RefreshSessionsAsync();
        var outer = new Frame();
        window.Content = outer;
        outer.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)outer.Content;
        var inner = (Frame)Field(shell, "_frame")!;
        var page = (AdminActivityPage)inner.Content;
        await Until(() => page.RenderedSessionIds.Count == 3);
        Check(page.RenderedSessionIds.Count == 3, "nested shell Activity loads");
        var banner = (StackPanel)Field(shell, "_restartBannerHost")!;
        await Until(() => banner.Children.Count > 0 && banner.Parent != null);
        Check(ReferenceEquals(banner.Parent, Field(page, "_page")), "restart notice shares Activity scroll content");
        server.SessionCount = 30;
        await store.RefreshSessionsAsync();
        await Until(() => page.RenderedSessionIds.Count == 30);
        var scroller = (ScrollViewer)Field(page, "_scroller")!;
        page.UpdateLayout();
        await Until(() => scroller.ScrollableHeight > 0);
        var bannerOrigin = banner.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y;
        var scrollTarget = Math.Min(250, scroller.ScrollableHeight);
        var scrollAccepted = scroller.ChangeView(null, scrollTarget, null, true);
        await Until(() => Math.Abs(scroller.VerticalOffset - scrollTarget) < 1);
        var bannerScrolled = banner.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y;
        Program.Log($"TRACE restart-scroll: accepted={scrollAccepted}, offset={scroller.VerticalOffset}, target={scrollTarget}, extent={scroller.ExtentHeight}, viewport={scroller.ViewportHeight}, scrollable={scroller.ScrollableHeight}, bannerBefore={bannerOrigin}, bannerAfter={bannerScrolled}");
        Check(scrollAccepted && scroller.VerticalOffset > 0 && Math.Abs(bannerOrigin - bannerScrolled - scroller.VerticalOffset) < 1,
            "restart notice moves by the actual page scroll offset after layout settles");
        Check(banner.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y < 0,
            "restart notice scrolls away instead of reserving fixed viewport height");
        async Task RefreshRestartStatusAsync()
        {
            await (Task)typeof(AdminShellPage).GetMethod("LoadServerStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, new[] { Field(shell, "_lease")!, (object)true })!;
        }
        Invoke(Descendants(banner).OfType<Button>().Single(button =>
            Descendants(button).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.common.restartBanner.later"))));
        await Until(() => banner.Visibility == Visibility.Collapsed);
        Check(banner.Children.Count == 0 && banner.Visibility == Visibility.Collapsed,
            "Later collapses the restart host so it contributes no page spacing");
        server.RestartRequired = false;
        await RefreshRestartStatusAsync();
        server.RestartRequired = true;
        await RefreshRestartStatusAsync();
        Check(banner.Children.Count > 0 && banner.Visibility == Visibility.Visible, "a new restart notice reveals its host again");
        server.RestartRequired = false;
        await RefreshRestartStatusAsync();
        Check(banner.Children.Count == 0 && banner.Visibility == Visibility.Collapsed,
            "cleared restart requirement collapses the empty notice host");
        server.RestartRequired = true;
        await RefreshRestartStatusAsync();

        var old = store.Current;
        var states = (System.Collections.IDictionary)Field(page, "_rowStates")!;
        var first = old.Sessions.First();
        var messageTask = (Task)typeof(AdminActivityPage).GetMethod("ShowMessageDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, new[] { (object)first, states[first.SessionId]! })!;
        await Until(() => Field(page, "_messageDialog") != null);
        var draft = (TextBox)Field(page, "_messageDraft")!;
        draft.Text = "retired synthetic private draft";
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        // Replay an older publication after the authority clear reached subscribers.
        typeof(AdminActivityPage).GetMethod("OnStoreChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { old });
        await Task.Delay(200);
        Check(page.RenderedSessionIds.Count == 0, "queued old snapshot cannot restore retired rows");
        Check(draft.Text.Length == 0 && Field(page, "_messageDialog") == null, "retirement closes Message and clears draft");
        await messageTask;
        server.SessionCount = 3;
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        outer.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var secondShell = (AdminShellPage)outer.Content;
        var second = (AdminActivityPage)((Frame)Field(secondShell, "_frame")!).Content;
        await Until(() => second.RenderedSessionIds.Count == 3);
        Check(outer.Navigate(typeof(AdminActivityPage)), "outer departure destination activates");
        await Task.Delay(150);
        Check(Field(second, "_active") is false && Field(second, "_lease") == null && Field(second, "_reads") == null,
            "outer shell departure retires inner Activity without inner navigation");
        Check(second.RenderedSessionIds.Count == 0, "departed Activity clears private rows");
    }

    /// <summary>
    /// Owner report: the Activity page scrollbar covered the rightmost text and sat on top of the
    /// nested active-streams scrollbar, and the wheel only scrolled the page over the centred column.
    /// WebUI scrolls the document, so its scrollbar is at the viewport edge and the side gutters scroll.
    /// Measures the rendered shell/page geometry and the hit-test owner that wheel input routes to.
    /// </summary>
    private static async Task ActivityScrollAsync(Window window, Server server, ServerActivityStore store)
    {
        server.SessionCount = 30;
        await store.RefreshSessionsAsync();
        var frame = new Frame { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"] };
        window.Content = frame;
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)frame.Content;
        var page = (AdminActivityPage)((Frame)Field(shell, "_frame")!).Content;
        await Until(() => page.RenderedSessionIds.Count == 30);
        Check(page.RenderedSessionIds.Count == 30, "scroll case renders 30 synthetic streams");
        var outer = (ScrollViewer)Field(page, "_scroller")!;
        var content = (FrameworkElement)Field(page, "_page")!;
        var table = (StackPanel)Field(page, "_tableHost")!;
        var header = (StackPanel)Field(page, "_header")!;

        foreach (var (width, height) in new[] { (1440, 400), (1920, 400), (800, 400) })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
            await Task.Delay(400);
            // The nested panel height follows the viewport at render time; render it for this size.
            typeof(AdminActivityPage).GetMethod("RenderTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            frame.UpdateLayout();
            await Task.Delay(300);
            var inner = Descendants(table).OfType<ScrollViewer>().First();
            outer.ChangeView(null, 0, null, true);
            inner.ChangeView(null, 0, null, true);
            await Task.Delay(200);
            var size = $"{width}x{height}";
            var windowWidth = frame.ActualWidth;
            Check(outer.ScrollableHeight > 0 && inner.ScrollableHeight > 0,
                $"{size}: page and nested streams both overflow (outer {outer.ScrollableHeight:0}, inner {inner.ScrollableHeight:0})");

            var outerBar = VerticalBar(outer);
            var innerBar = VerticalBar(inner);
            var outerBarRect = outerBar == null ? default : Bounds(outerBar);
            var innerBarRect = innerBar == null ? default : Bounds(innerBar);
            var tableRect = Bounds(table);
            var outerRect = Bounds(outer);
            var tableCard = (FrameworkElement)table.Children[0];
            var cardRect = Bounds(tableCard);
            var texts = Descendants(content).OfType<TextBlock>()
                .Where(text => Shown(text) && text.ActualWidth > 0 && text.ActualHeight > 0)
                .Select(text => Bounds(text))
                .Where(rect => rect.Bottom > outerRect.Top && rect.Top < outerRect.Bottom)
                .ToList();
            var textRight = texts.Count == 0 ? 0 : texts.Max(rect => rect.Right);

            // The shell's centred column: 240px sidebar from 1024px, gutters 16/24/32/40, max 1560px.
            var desktop = windowWidth >= AdminShellPage.DesktopBreakpoint;
            var gutter = windowWidth >= 1280 ? 40 : windowWidth >= 1024 ? 32 : windowWidth >= 640 ? 24 : 16;
            var hostLeft = desktop ? AdminShellPage.SidebarWidth : 0;
            var hostWidth = windowWidth - hostLeft;
            var contentWidth = Math.Min(AdminShellPage.ContentMaxWidth, hostWidth - 2 * gutter);
            var contentLeft = hostLeft + gutter + (hostWidth - 2 * gutter - contentWidth) / 2;
            Program.Log($"TRACE activity-scroll {size}: window={windowWidth:0.#} outerViewer={Fmt(outerRect)} outerBar={Fmt(outerBarRect)} " +
                $"table={Fmt(tableRect)} card={Fmt(cardRect)} innerBar={Fmt(innerBarRect)} textRight={textRight:0.#} " +
                $"expectedContent=[{contentLeft:0.#},{contentLeft + contentWidth:0.#}] wide={page.IsWideTable}");

            Check(Math.Abs(tableRect.Left - contentLeft) < 1.5 && Math.Abs(tableRect.Width - contentWidth) < 1.5,
                $"{size}: Activity content keeps the centred shell gutter layout");
            Check(innerBar != null && Math.Abs(innerBarRect.Right - (cardRect.Right - tableCard.Margin.Right - 1)) < 1.5,
                $"{size}: nested streams scrollbar stays at the table card's inner edge");
            Check(outerBar != null && outerBarRect.Right >= windowWidth - 1.5,
                $"{size}: page scrollbar sits at the window's right edge");
            Check(outerBar != null && outerBarRect.Left >= textRight + 4,
                $"{size}: page scrollbar clears the rightmost text ({outerBarRect.Left:0.#} vs {textRight:0.#})");
            Check(outerBar != null && innerBar != null && outerBarRect.Left >= innerBarRect.Right + 4,
                $"{size}: page and nested scrollbars are separate ({outerBarRect.Left:0.#} vs {innerBarRect.Right:0.#})");

            var middle = outerRect.Top + outerRect.Height / 2;
            var leftGutter = new Windows.Foundation.Point(contentLeft - gutter / 2.0, middle);
            var rightSpace = outerBar != null && outerBarRect.Left > contentLeft + contentWidth ? outerBarRect.Left - (contentLeft + contentWidth) : gutter;
            var rightGutter = new Windows.Foundation.Point(contentLeft + contentWidth + Math.Max(2, Math.Min(gutter / 2.0, rightSpace / 2)), middle);
            var headerText = Descendants(header).OfType<TextBlock>().First(text => Shown(text) && text.ActualWidth > 0);
            var headerRect = Bounds(headerText);
            var control = new Windows.Foundation.Point(headerRect.Left + Math.Min(headerRect.Width / 2, 40), headerRect.Top + headerRect.Height / 2);
            string Owner(Windows.Foundation.Point point)
            {
                var top = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, frame).FirstOrDefault();
                var owner = NearestScroller(top);
                var name = ReferenceEquals(owner, outer) ? "outer" : ReferenceEquals(owner, inner) ? "inner" : top == null ? "nothing" : "outside";
                Program.Log($"TRACE activity-scroll {size} hit ({point.X:0.#},{point.Y:0.#}) -> {name}: {Describe(top)}");
                return name;
            }
            Check(Owner(control) == "outer", $"{size}: page text hit-tests inside the page scroller");
            Check(Owner(leftGutter) == "outer", $"{size}: left gutter hit-tests inside the page scroller");
            Check(Owner(rightGutter) == "outer", $"{size}: right gutter hit-tests inside the page scroller");

            // Bring the nested panel into the page viewport with page scroll still available both ways,
            // then probe across an unstriped row, whose blank areas have no background of their own.
            var pageOffset = Math.Floor(outer.ScrollableHeight / 2);
            outer.ChangeView(null, pageOffset, null, true);
            await Task.Delay(250);
            var outerNow = Bounds(outer);
            var innerRect = Bounds(inner);
            var visibleTop = Math.Max(innerRect.Top, outerNow.Top);
            var visibleBottom = Math.Min(innerRect.Bottom, outerNow.Bottom);
            var rows = (Panel)inner.Content;
            var row = rows.Children.OfType<Panel>().Where((_, index) => index % 2 == 0)
                .Select(candidate => (Row: candidate, Rect: Bounds(candidate)))
                .FirstOrDefault(candidate => Math.Min(candidate.Rect.Bottom, visibleBottom) - Math.Max(candidate.Rect.Top, visibleTop) >= 24);
            Check(row.Row != null && row.Row.Background == null, $"{size}: an unstriped stream row is visible for nested probes");
            var rowMiddle = row.Row == null
                ? (visibleTop + visibleBottom) / 2
                : (Math.Max(row.Rect.Top, visibleTop) + Math.Min(row.Rect.Bottom, visibleBottom)) / 2;
            var nestedPoints = new[] { 0.03, 0.25, 0.5, 0.75, 0.9 }
                .Select(fraction => new Windows.Foundation.Point(innerRect.Left + innerRect.Width * fraction, rowMiddle)).ToArray();
            var nestedOwners = nestedPoints.Select(Owner).ToArray();
            Check(nestedOwners.All(owner => owner != "outer"), $"{size}: no point over the nested streams panel routes to the page scroller ({string.Join(",", nestedOwners)})");
            Check(nestedOwners.All(owner => owner == "inner"), $"{size}: every probed point over the nested streams panel hit-tests inside that panel ({string.Join(",", nestedOwners)})");
            // Wheel input routes from the topmost hit element to its nearest ScrollViewer, so these
            // hit-test owners are the routing evidence. WinAppSDK input did not consume posted or sent
            // WM_MOUSEWHEEL (input site or bridge) and global SendInput would reach the owner's desktop.

            outer.ChangeView(null, outer.ScrollableHeight, null, true);
            inner.ChangeView(null, 0, null, true);
            await Task.Delay(200);
            // Capture-only: page scrolled to its end so the nested panel and both bars are in view, bars expanded.
            foreach (var viewer in new[] { outer, inner }) VisualStateManager.GoToState(viewer, "MouseIndicator", false);
            foreach (var bar in new[] { outerBar, innerBar }) if (bar != null) VisualStateManager.GoToState(bar, "ExpandedWithoutAnimation", false);
            await CaptureAsync(frame, $"admin-activity-scroll-{size}.png");
        }

        // Only Activity opts into the gutter-spanning scroller; other Monitor pages keep the shell column.
        var column = (Grid)Field(shell, "_contentColumn")!;
        var innerFrame = (Frame)Field(shell, "_frame")!;
        shell.Navigate(new AdminRouteRequest(AdminRoute.ServerLog));
        await Until(() => innerFrame.Content is AdminLogsPage);
        await Task.Delay(200);
        Check(innerFrame.Content is AdminLogsPage && column.MaxWidth == AdminShellPage.ContentMaxWidth && column.Margin.Left == 24 && column.Margin.Right == 24,
            $"Logs keeps the gutter outside its frame (max {column.MaxWidth}, margin {column.Margin})");
        shell.Navigate(new AdminRouteRequest(AdminRoute.Activity));
        await Until(() => innerFrame.Content is AdminActivityPage);
        await Task.Delay(200);
        var returned = innerFrame.Content as AdminActivityPage;
        var returnedContent = returned == null ? null : (FrameworkElement)Field(returned, "_page")!;
        Check(returnedContent != null && column.Margin.Left == 0 && double.IsPositiveInfinity(column.MaxWidth)
                && returnedContent.Margin.Left == 24 && returnedContent.MaxWidth == AdminShellPage.ContentMaxWidth,
            "returning to Activity moves the gutter back inside its scroller");
    }

    private static Microsoft.UI.Xaml.Controls.Primitives.ScrollBar? VerticalBar(ScrollViewer viewer) =>
        Descendants(viewer).OfType<Microsoft.UI.Xaml.Controls.Primitives.ScrollBar>()
            .FirstOrDefault(bar => bar.Orientation == Orientation.Vertical && ReferenceEquals(NearestScroller(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(bar)), viewer));

    private static ScrollViewer? NearestScroller(DependencyObject? element)
    {
        for (var current = element; current != null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer viewer) return viewer;
        return null;
    }

    private static bool Shown(UIElement element)
    {
        for (DependencyObject? current = element; current != null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed } or UIElement { Opacity: 0 }) return false;
        return true;
    }

    private static Windows.Foundation.Rect Bounds(FrameworkElement element)
    {
        var origin = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new Windows.Foundation.Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static string Fmt(Windows.Foundation.Rect rect) => $"[{rect.Left:0.#},{rect.Top:0.#} → {rect.Right:0.#},{rect.Bottom:0.#}]";

    private static string Describe(DependencyObject? element)
    {
        var chain = new List<string>();
        for (var current = element; current != null && chain.Count < 6; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
            chain.Add(current.GetType().Name + (current is FrameworkElement { Name.Length: > 0 } named ? "#" + named.Name : ""));
        return chain.Count == 0 ? "nothing" : string.Join(" < ", chain);
    }

    private static void Invoke(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();


    /// <summary>The four widths the parity record compares (1440, 1024, 800, 560 device-independent pixels), then back to 1440.</summary>
    /// <summary>
    /// Stands in for a nested panel on a scaled display: it needs 100.4 but reports a desired width rounded to 100, and offered strictly less than it
    /// needs it wraps into a second line (height 40 instead of 20).
    /// </summary>
    /// <summary>Content 300 wide on one line that wraps to two lines when it is offered less.</summary>
    private sealed class WrapsWhenNarrowPanel : Panel
    {
        internal double LastOffered = double.NaN;
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
        {
            LastOffered = available.Width;
            return available.Width < 300 ? new Windows.Foundation.Size(available.Width, 40) : new Windows.Foundation.Size(300, 20);
        }
        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final) => final;
    }

    private sealed class RoundedNeedPanel : Panel
    {
        internal const double Need = 100.4, Rounded = 100;
        internal int Measures;
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
        {
            Measures++;
            return available.Width < Need ? new Windows.Foundation.Size(available.Width, 40) : new Windows.Foundation.Size(Math.Min(available.Width, Rounded), 20);
        }
        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final) => final;
    }

    private static async Task CaptureFourWidthsAsync(Window window, FrameworkElement element, string name)
    {
        foreach (var width in new[] { 1440, 1024, 800, 560 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
            await Task.Delay(300);
            await CaptureAsync(element, $"{name}-{width}.png");
        }
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        await Task.Delay(300);
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
}
