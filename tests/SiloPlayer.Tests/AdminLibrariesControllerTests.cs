using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Tests.AdminLibrariesTests;

namespace SiloPlayer.Tests;

/// <summary>
/// Libraries page ownership: reads, realtime scans and jobs, serialized whole-order writes,
/// row commands with truthful pending/unknown outcomes, the editor's create/update/chain
/// transaction, the diagnostics panels' bounded paging and retirement. Synthetic server only.
/// </summary>
public sealed class AdminLibrariesControllerTests
{
    private sealed class Channel : IAdminRealtimeChannel
    {
        public int Subscriptions;
        public int Released;
        public JsonElement? Latest;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) { Subscriptions++; return new Handle(() => Released++); }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = Latest ?? default; return channel == "scans" && Latest != null; }
        public void Snapshot(string channel, string json) => SnapshotReceived?.Invoke(channel, JsonDocument.Parse(json).RootElement.Clone());
        public void Event(string channel, string name, string json) => EventReceived?.Invoke(channel, name, JsonDocument.Parse(json).RootElement.Clone());
        public bool HasListeners => SnapshotReceived != null || EventReceived != null;
        private sealed class Handle(Action release) : IDisposable { private Action? _release = release; public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke(); }
    }

    private sealed class Scheduler : IAdminScheduler
    {
        private readonly List<(DateTimeOffset Due, Action Callback, Token Handle)> _timers = [];
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-11-15T12:00:00Z");
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var token = new Token();
            lock (_timers) _timers.Add((UtcNow + delay, callback, token));
            return token;
        }
        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                (DateTimeOffset Due, Action Callback, Token Handle) next;
                lock (_timers)
                {
                    next = _timers.Where(timer => timer.Due <= UtcNow && !timer.Handle.Cancelled).OrderBy(timer => timer.Due).FirstOrDefault();
                    if (next.Callback == null) return;
                    _timers.Remove(next);
                }
                next.Callback();
            }
        }
        public int Pending(TimeSpan delay) { lock (_timers) return _timers.Count(timer => !timer.Handle.Cancelled && timer.Due - UtcNow <= delay); }
        public sealed class Token : IDisposable { public bool Cancelled; public void Dispose() => Cancelled = true; }
    }

    /// <summary>A state-aware synthetic library server.</summary>
    private sealed class Server
    {
        public List<string> Order = ["1", "2", "3"];
        public Func<HttpRequestMessage, HttpResponseMessage?>? Override;
        public int ReorderCalls;
        public string? LastReorder;
        public TaskCompletionSource? HoldReorder;
        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if (Override?.Invoke(request) is { } custom) return custom;
            var path = request.RequestUri!.AbsolutePath;
            switch (path)
            {
                case "/api/v2/libraries" when request.Method == HttpMethod.Get:
                    return AdminMonitorKit.Json(Libraries(Order.Select(id => Library(id, "Library " + id, warning: id == "3" ? "dead_root" : null)).ToArray()));
                case "/api/v2/libraries/reorder":
                    ReorderCalls++;
                    LastReorder = request.Content!.ReadAsStringAsync().Result;
                    HoldReorder?.Task.Wait(TimeSpan.FromSeconds(5));
                    using (var body = JsonDocument.Parse(LastReorder)) Order = body.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!).ToList();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/api/v2/libraries/capabilities":
                    return AdminMonitorKit.Json("{\"allowed\":true,\"box_sets\":true,\"ignore_unmatched\":true,\"realtime_monitoring\":true,\"revision\":\"r\",\"state\":\"available\",\"trickplay\":true,\"trickplay_supported\":true}");
                case "/api/v2/libraries/realtime-monitoring":
                    return AdminMonitorKit.Json("{\"server_enabled\":true,\"libraries\":[{\"library_id\":\"1\",\"enabled\":true,\"state\":\"limit_reached\",\"backend\":\"inotify\",\"detail\":\"watch limit\",\"directories\":4}]}");
                case "/api/v2/admin/trickplay/libraries":
                    return AdminMonitorKit.Json("{\"items\":[{\"library_id\":\"1\",\"name\":\"L\",\"pending\":1,\"running\":0,\"ready\":1,\"unusable\":0,\"sheet_bytes\":10}]}");
                case "/api/v2/admin/jobs":
                    return AdminMonitorKit.Json("{\"items\":[{\"id\":\"j1\",\"kind\":\"library_refresh\",\"state\":\"running\",\"terminal\":false,\"cancelable\":true,\"created_at\":\"2026-11-15T10:00:00Z\",\"library_id\":\"2\",\"library_ids\":[],\"artifact_size_bytes\":0,\"public_link_supported\":false}],\"page\":{\"has_more\":false}}");
                case "/api/v2/scan":
                    return AdminMonitorKit.Json("{\"library_id\":\"1\",\"mode\":\"library\",\"status\":\"accepted\"}", HttpStatusCode.Accepted);
                case "/api/v2/libraries/1/check-mount":
                    return AdminMonitorKit.Json("{\"library_id\":\"1\",\"library_name\":\"L\",\"healthy\":true,\"status\":\"healthy\",\"summary\":\"All roots reachable\",\"checked_at\":\"2026-11-15T10:00:00Z\",\"roots\":[]}");
            }
            return AdminMonitorKit.Json("{}", HttpStatusCode.NotFound);
        }
    }

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminLibrariesController Controller, Server Server, Channel Channel, Scheduler Scheduler) Create()
    {
        var kit = AdminMonitorKit.Create();
        var server = new Server();
        kit.Http.Respond = (request, _) => Task.Run(() => server.Respond(request));
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var channel = new Channel();
        var scheduler = new Scheduler();
        return (kit, authority, new AdminLibrariesController(new AdminLibrariesApi(kit.Client), authority, channel, scheduler), server, channel, scheduler);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 3000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 10) await Task.Delay(10);
        Assert.True(condition(), "condition was not reached");
    }

    [Fact]
    public async Task OpenReadsTheListStatusAndJobsAndLeasesTheScanAndJobChannels()
    {
        var (kit, authority, controller, _, channel, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var state = controller.State;
            Assert.True(state.Loaded);
            Assert.Equal(["1", "2", "3"], state.Libraries.Select(library => library.Id));
            Assert.True(state.CanReorder);
            Assert.NotNull(state.Capabilities);
            Assert.Equal(1, channel.Subscriptions);
            var rows = AdminLibrariesController.Rows(state);
            Assert.True(AdminLibraryPresentation.MonitoringNeedsAttention(rows[0].Monitoring));
            Assert.NotNull(rows[0].Trickplay);
            Assert.Equal("j1", rows[1].ActiveRefresh?.Id);
            Assert.True(rows[1].RefreshStops);
            Assert.True(rows[2].HasScanWarning && rows[2].CanCleanup);
            // Visible polls only: monitoring every 30 s, seek previews every 60 s.
            Assert.Equal(1, scheduler.Pending(AdminLibrariesController.MonitoringInterval));
            Assert.Equal(2, scheduler.Pending(AdminLibrariesController.TrickplayInterval));
        }
    }

    [Fact]
    public async Task ScansComeFromTheChannelAndAFinishedScanReloadsTheList()
    {
        var (kit, authority, controller, _, channel, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            channel.Latest = JsonDocument.Parse("[{\"id\":\"s1\",\"library_id\":1,\"mode\":\"library\",\"status\":\"running\"},{\"id\":\"s2\",\"library_id\":1,\"mode\":\"file\",\"path\":\"/m/a.mkv\",\"status\":\"accepted\"},{\"id\":\"s3\",\"library_id\":2,\"mode\":\"library\",\"status\":\"completed\"}]").RootElement.Clone();
            await controller.OpenAsync();
            var groups = AdminLibrariesController.ScanGroups(controller.State);
            Assert.Single(groups);
            Assert.Equal((1, 1), (groups[0].Running, groups[0].Queued));
            var row = AdminLibrariesController.Rows(controller.State)[0];
            Assert.True(row.ScanStops);
            Assert.Equal(AdminLibrariesText.T("web.admin.libraries.list.stopScans"), row.ScanLabel);
            var stale = 0;
            controller.DiagnosticsStale += () => stale++;
            var reads = kit.Http.Requests.Count(request => request == "GET /api/v2/libraries");
            channel.Event("scans", "scan.completed", "{\"id\":\"s1\",\"library_id\":1,\"mode\":\"library\",\"status\":\"completed\"}");
            channel.Event("scans", "scan.failed", "{\"id\":\"s2\",\"library_id\":1,\"mode\":\"file\",\"status\":\"failed\"}");
            Assert.Empty(controller.State.Scans);
            Assert.Equal(2, stale);
            scheduler.Advance(AdminLibrariesController.EventCoalescing);
            await Until(() => kit.Http.Requests.Count(request => request == "GET /api/v2/libraries") == reads + 1);
            await Task.Delay(50);
            Assert.Equal(reads + 1, kit.Http.Requests.Count(request => request == "GET /api/v2/libraries"));
        }
    }

    [Fact]
    public async Task ReorderShowsAtOnceRunsAloneAndReloadsBeforeTheNextMove()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.HoldReorder = new TaskCompletionSource();
            var first = controller.MoveAsync("3", -1);
            await Until(() => controller.State.Reordering);
            Assert.Equal(["1", "3", "2"], controller.State.Libraries.Select(library => library.Id));
            Assert.False(controller.State.CanReorder);
            await controller.MoveAsync("1", 1);
            server.HoldReorder.SetResult();
            await first;
            Assert.Equal(1, server.ReorderCalls);
            Assert.Equal("{\"entries\":[{\"id\":\"1\",\"position\":0},{\"id\":\"3\",\"position\":1},{\"id\":\"2\",\"position\":2}]}", server.LastReorder);
            await Until(() => controller.State.CanReorder);
            Assert.Equal(["1", "3", "2"], controller.State.Libraries.Select(library => library.Id));
        }
    }

    [Fact]
    public async Task RefusedReorderRestoresThePreviousOrder()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var notices = new ConcurrentQueue<(string, bool)>();
            controller.Notice += (message, error) => notices.Enqueue((message, error));
            server.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/libraries/reorder"
                ? AdminMonitorKit.Json("{\"type\":\"https://siloserver.org/docs/api/v2/problems/validation_failed\",\"title\":\"Invalid\",\"status\":422,\"detail\":\"bad order\"}", HttpStatusCode.UnprocessableEntity)
                : null;
            await controller.MoveAsync("2", 1);
            Assert.Equal(["1", "2", "3"], controller.State.Libraries.Select(library => library.Id));
            Assert.True(controller.State.CanReorder);
            Assert.Contains(notices, notice => notice.Item2);
        }
    }

    [Fact]
    public async Task LostReorderAnswerIsReportedUnknownAndReloadedNotReplayed()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var notices = new ConcurrentQueue<string>();
            controller.Notice += (message, _) => notices.Enqueue(message);
            var lost = true;
            server.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/libraries/reorder" && lost ? throw new HttpRequestException("connection reset") : null;
            var reads = kit.Http.Requests.Count(request => request == "GET /api/v2/libraries");
            await controller.MoveAsync("2", 1);
            lost = false;
            Assert.Contains(AdminLibrariesText.T("admin.native.libraries.outcomeUnknown"), notices);
            await Until(() => kit.Http.Requests.Count(request => request == "GET /api/v2/libraries") > reads);
            Assert.Equal(1, kit.Http.Requests.Count(request => request.StartsWith("POST /api/v2/libraries/reorder", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task ARowCommandIsSentOnceWhilePendingAndItsReceiptIsNotCompletion()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var gate = new TaskCompletionSource();
            server.Override = request => { if (request.RequestUri!.AbsolutePath == "/api/v2/scan") gate.Task.Wait(TimeSpan.FromSeconds(5)); return null; };
            var first = controller.StartScanAsync("1");
            await Until(() => controller.State.ScanStarting.Contains("1"));
            Assert.True(AdminLibrariesController.Rows(controller.State)[0].ScanDisabled);
            await controller.StartScanAsync("1");
            gate.SetResult();
            await first;
            Assert.Equal(1, kit.Http.Requests.Count(request => request == "POST /api/v2/scan"));
            Assert.DoesNotContain("1", controller.State.ScanStarting);
            Assert.Empty(controller.State.Scans);
        }
    }

    [Fact]
    public async Task MountCheckResultShowsForFiveSeconds()
    {
        var (kit, authority, controller, _, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            await controller.CheckMountAsync("1");
            Assert.Equal("All roots reachable", AdminLibrariesController.Rows(controller.State)[0].MountMessage);
            scheduler.Advance(AdminLibrariesController.MountFeedback);
            Assert.Null(AdminLibrariesController.Rows(controller.State)[0].MountMessage);
        }
    }

    [Fact]
    public async Task RetirementClearsDataReleasesTheChannelAndDropsLateAnswers()
    {
        var (kit, authority, controller, server, channel, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var retired = 0;
            controller.Retired += () => retired++;
            var gate = new TaskCompletionSource();
            server.Override = request => { if (request.RequestUri!.AbsolutePath == "/api/v2/libraries/1/check-mount") gate.Task.Wait(TimeSpan.FromSeconds(5)); return null; };
            var pending = controller.CheckMountAsync("1");
            await Until(() => controller.State.MountChecking.Contains("1"));
            kit.SetRole("user");
            await Until(() => retired == 1);
            gate.SetResult();
            await pending;
            Assert.Null(controller.State.Owner);
            Assert.Empty(controller.State.Libraries);
            Assert.Empty(controller.State.MountResults);
            Assert.Equal(1, channel.Released);
            Assert.False(channel.HasListeners);
            channel.Snapshot("scans", "[{\"id\":\"late\",\"library_id\":1,\"mode\":\"library\",\"status\":\"running\"}]");
            Assert.Empty(controller.State.Scans);
            var before = kit.Http.Requests.Count;
            scheduler.Advance(TimeSpan.FromMinutes(5));
            await Task.Delay(50);
            Assert.Equal(before, kit.Http.Requests.Count);
        }
    }

    // ── Editor ───────────────────────────────────────────────────────────

    private const string Defaults = "{\"levels\":[{\"content_level\":\"movie\",\"entries\":[{\"plugin_installation_id\":\"9\",\"capability_id\":\"meta\",\"provider_slug\":\"tmdb\",\"priority\":0,\"enabled\":true,\"local\":false},{\"plugin_installation_id\":\"8\",\"capability_id\":\"meta\",\"provider_slug\":\"omdb\",\"priority\":1,\"enabled\":true,\"local\":false}]}]}";

    [Fact]
    public async Task NewLibraryValidatesThenCreatesAndWritesAnEditedChain()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            string? created = null;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries/provider-defaults" => AdminMonitorKit.Json(Defaults),
                "/api/v2/libraries" when request.Method == HttpMethod.Post => (created = request.Content!.ReadAsStringAsync().Result) == null ? null : AdminMonitorKit.Json(Library("9", "New"), HttpStatusCode.Created),
                "/api/v2/libraries/9/providers" => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => null,
            };
            using var session = AdminLibraryEditorSession.Begin(controller, null);
            await session.LoadAsync();
            Assert.Equal("movies", session.State.Type);
            Assert.False(session.State.ChainLoading);
            Assert.Equal(["tmdb", "omdb"], session.State.RemoteChains["movie"].Select(item => item.ProviderSlug));
            Assert.Equal(AdminLibraryEditorSection.General, await session.SubmitAsync());
            Assert.NotNull(session.State.ShownNameError);
            session.SetName("New");
            Assert.Equal(AdminLibraryEditorSection.Folders, await session.SubmitAsync());
            session.UpdatePath(0, "/media/new");
            session.MoveProvider("movie", 0, 1);
            Assert.True(session.State.IsDirty);
            Assert.Null(await session.SubmitAsync());
            Assert.True(session.State.Saved);
            using var body = JsonDocument.Parse(created!);
            Assert.False(body.RootElement.TryGetProperty("enabled", out _));
            Assert.False(body.RootElement.GetProperty("intro_detection_enabled").GetBoolean()); // movies default off
            Assert.Contains("PUT /api/v2/libraries/9/providers", kit.Http.Requests);
            Assert.Equal(1, kit.Http.Requests.Count(request => request == "POST /api/v2/libraries"));
        }
    }

    [Fact]
    public async Task ACreatedLibraryWhoseChainFailedIsRetriedAsAnUpdate()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var chainFails = true;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries/provider-defaults" => AdminMonitorKit.Json(Defaults),
                "/api/v2/libraries" when request.Method == HttpMethod.Post => AdminMonitorKit.Json(Library("9", "New"), HttpStatusCode.Created),
                "/api/v2/libraries/9" when request.Method == HttpMethod.Patch => AdminMonitorKit.Json(Library("9", "New")),
                "/api/v2/libraries/9/providers" => chainFails
                    ? AdminMonitorKit.Json("{\"type\":\"https://siloserver.org/docs/api/v2/problems/validation_failed\",\"title\":\"x\",\"status\":422,\"detail\":\"bad chain\"}", HttpStatusCode.UnprocessableEntity)
                    : new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => null,
            };
            using var session = AdminLibraryEditorSession.Begin(controller, null);
            await session.LoadAsync();
            session.SetName("New"); session.UpdatePath(0, "/media/new"); session.ToggleProvider("movie", 1);
            await session.SubmitAsync();
            Assert.False(session.State.Saved);
            Assert.True(session.State.IsEditing);
            Assert.NotNull(session.State.Failure);
            Assert.True(session.State.CanSave);
            chainFails = false;
            await session.SubmitAsync();
            Assert.True(session.State.Saved);
            Assert.Equal(1, kit.Http.Requests.Count(request => request == "POST /api/v2/libraries"));
            Assert.Equal(1, kit.Http.Requests.Count(request => request == "PATCH /api/v2/libraries/9"));
        }
    }

    [Fact]
    public async Task ALostSaveAnswerDisablesSavingAndIsNotReplayed()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries/provider-defaults" or "/api/v2/libraries/1/providers" => AdminMonitorKit.Json(Defaults),
                "/api/v2/libraries/1" when request.Method == HttpMethod.Patch => throw new HttpRequestException("reset"),
                _ => null,
            };
            using var session = AdminLibraryEditorSession.Begin(controller, controller.State.Libraries[0]);
            await session.LoadAsync();
            session.SetName("Renamed");
            await session.SubmitAsync();
            Assert.True(session.State.OutcomeUnknown);
            Assert.False(session.State.CanSave);
            Assert.Equal(AdminLibrariesText.T("admin.native.libraries.outcomeUnknown"), session.State.Failure);
            await session.SubmitAsync();
            Assert.Equal(1, kit.Http.Requests.Count(request => request == "PATCH /api/v2/libraries/1"));
        }
    }

    [Fact]
    public void EditBodiesSendChangeOnlyMembersOnlyWhenTheyChange()
    {
        var library = JsonSerializer.Deserialize<AdminLibrary>(Library("1"), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        var state = new AdminLibraryEditorState
        {
            Library = library, Name = "Movies", Paths = ["/m", " "], Type = "movies", TrickplayEnabled = false, BoxSetsEnabled = false,
        };
        var body = AdminLibraryEditorSession.BuildBody(state);
        Assert.Null(body.TrickplayEnabled);
        Assert.Null(body.BoxSetsEnabled);
        Assert.Equal(["/m"], body.Paths);
        var changed = AdminLibraryEditorSession.BuildBody(state with { TrickplayEnabled = true, BoxSetsEnabled = true, BoxSetMinMovies = 3 });
        Assert.True(changed.TrickplayEnabled);
        Assert.True(changed.BoxSetsEnabled);
        Assert.Equal(3, changed.BoxSetMinMovies);
        var audiobooks = AdminLibraryEditorSession.BuildBody(state with { Type = "audiobooks", TrickplayEnabled = true, TrailerKinds = ["trailer"], ChapterThumbnailsEnabled = true });
        Assert.Null(audiobooks.TrickplayEnabled);
        Assert.Empty(audiobooks.TrailerKinds);
        Assert.False(audiobooks.ChapterThumbnailsEnabled);
    }

    [Fact]
    public async Task ChangingANewLibrarysTypeDropsChainEditsAndReadsThatTypesDefaults()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/libraries/provider-defaults" ? AdminMonitorKit.Json(Defaults) : null;
            using var session = AdminLibraryEditorSession.Begin(controller, null);
            await session.LoadAsync();
            session.ToggleProvider("movie", 0);
            Assert.True(session.State.ChainDirty);
            session.SetType("series");
            Assert.False(session.State.ChainDirty);
            Assert.True(session.State.ChainLoading);
            await Until(() => kit.Http.Requests.Contains("GET /api/v2/libraries/provider-defaults?library_type=series"));
            Assert.Equal(["series", "season", "episode"], session.State.ContentLevels);
        }
    }

    // ── Diagnostics ──────────────────────────────────────────────────────

    private static string UnmatchedPage(int start, int count, string? next, int total = 300) =>
        "{\"items\":[" + string.Join(',', Enumerable.Range(start, count).Select(i => "{\"content_id\":\"c" + i + "\",\"content_type\":\"movie\",\"library_id\":\"1\",\"library_name\":\"M\",\"status\":\"unmatched\",\"title\":\"T" + i + "\",\"year\":2000}")) +
        "],\"page\":" + (next == null ? "{\"has_more\":false}" : "{\"has_more\":true,\"next_cursor\":\"" + next + "\"}") + ",\"total\":" + total + "}";

    [Fact]
    public async Task UnmatchedPagingRetainsCursorsOnlyAndBoundsTheWalkToLast()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/libraries/unmatched-items") return null;
                var query = request.RequestUri.Query;
                var page = query.Contains("cursor=p") ? int.Parse(query[(query.IndexOf("cursor=p", StringComparison.Ordinal) + 8)..].Split('&')[0]) : 0;
                return AdminMonitorKit.Json(UnmatchedPage(page * 10, 10, page < 29 ? "p" + (page + 1) : null));
            };
            using var panel = new AdminUnmatchedPanel(controller);
            await panel.LoadTotalsAsync();
            Assert.Equal(300, panel.State.AwaitingTotal);
            Assert.False(panel.State.Hidden);
            await panel.SetOpenAsync(true);
            Assert.Equal(10, panel.State.Items.Count);
            Assert.True(panel.State.HasNext);
            await panel.NextAsync();
            Assert.Equal(1, panel.State.PageIndex);
            Assert.Equal("c10", panel.State.Items[0].ContentId);
            var before = kit.Http.Requests.Count;
            await panel.LastAsync();
            Assert.True(panel.State.WalkCapped);
            // The walk starts from the furthest known cursor (page 3) and stops after MaxWalk requests.
            Assert.Equal(2 + AdminUnmatchedPanel.MaxWalk, panel.State.PageIndex);
            Assert.True(kit.Http.Requests.Count - before <= AdminUnmatchedPanel.MaxWalk + 1);
            await panel.FirstAsync();
            Assert.Equal(0, panel.State.PageIndex);
            Assert.Equal(10, panel.State.Items.Count);
            Assert.Equal(1, panel.State.RangeStart);
        }
    }

    [Fact]
    public async Task IgnoringAnItemReloadsTheListAndOffersOneUndo()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries/unmatched-items" => AdminMonitorKit.Json(UnmatchedPage(0, 2, null, 2)),
                "/api/v2/libraries/unmatched-items/c1/ignore" => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => null,
            };
            using var panel = new AdminUnmatchedPanel(controller);
            await panel.SetOpenAsync(true);
            var item = panel.State.Items[1];
            await panel.SetIgnoredAsync(item, true);
            Assert.Equal(new AdminUnmatchedChange("c1", "T1", true, false), panel.State.LastChange);
            Assert.Contains("PUT /api/v2/libraries/unmatched-items/c1/ignore", kit.Http.Requests);
            await panel.UndoAsync();
            Assert.Contains("DELETE /api/v2/libraries/unmatched-items/c1/ignore", kit.Http.Requests);
            await panel.UndoAsync();
            Assert.Equal(1, kit.Http.Requests.Count(request => request.StartsWith("DELETE /api/v2/libraries/unmatched-items/c1/ignore", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task AppendListsReadOnOpenRestartOnSearchAndStopAtTheirPageCap()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/libraries/stale-ids") return null;
                var query = request.RequestUri.Query;
                var page = query.Contains("cursor=s") ? int.Parse(query[(query.IndexOf("cursor=s", StringComparison.Ordinal) + 8)..].Split('&')[0]) : 0;
                var items = string.Join(',', Enumerable.Range(page * 50, 50).Select(i => "{\"content_id\":\"c" + i + "\",\"content_type\":\"movie\",\"library_id\":\"1\",\"library_name\":\"M\",\"provider\":\"tmdb\",\"provider_id\":\"" + i + "\",\"title\":\"T\",\"year\":1,\"first_seen_at\":\"2026-11-15T10:00:00Z\",\"last_seen_at\":\"2026-11-15T10:00:00Z\"}"));
                return AdminMonitorKit.Json("{\"items\":[" + items + "],\"page\":{\"has_more\":true,\"next_cursor\":\"s" + (page + 1) + "\"},\"total\":5000}");
            };
            using var list = new AdminAppendList<AdminStaleMediaId>(controller, (lease, _, search, cursor, ct) => controller.Api.ListStaleIdsAsync(lease, search, cursor, ct));
            Assert.Empty(kit.Http.Requests.Where(request => request.Contains("stale-ids")));
            await list.SetOpenAsync(true);
            Assert.Equal(50, list.State.Items.Count);
            for (var index = 0; index < 12; index++) await list.LoadMoreAsync();
            Assert.Equal(AdminAppendList<AdminStaleMediaId>.MaxPages * 50, list.State.Items.Count);
            Assert.True(list.State.Capped);
            Assert.False(list.State.HasMore);
            Assert.Equal(AdminAppendList<AdminStaleMediaId>.MaxPages, kit.Http.Requests.Count(request => request.Contains("stale-ids")));
            await list.SetSearchAsync("heat");
            Assert.Equal(50, list.State.Items.Count);
            Assert.Equal("GET /api/v2/libraries/stale-ids?limit=50&q=heat", kit.Http.Requests.Last());
        }
    }

    [Fact]
    public async Task MatcherDetailPollsOnlyWhileOpenAndExpanded()
    {
        var (kit, authority, controller, server, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries/metadata-match-queue" => AdminMonitorKit.Json("{\"items\":[{\"library_id\":\"1\",\"movie_count\":1,\"series_count\":0,\"raw_file_count\":0,\"pending_count\":1,\"parked_count\":0,\"total_count\":1}]}"),
                "/api/v2/libraries/1/metadata-match-queue" => AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[],\"series\":[],\"raw_files\":[],\"page\":{\"has_more\":false},\"total_count\":1}"),
                _ => null,
            };
            using var panel = new AdminMatcherQueuesPanel(controller);
            await panel.LoadSummaryAsync();
            Assert.Equal(1, panel.State.Total);
            panel.SetOpen(true);
            await panel.ToggleLibraryAsync("1");
            Assert.NotNull(panel.State.Detail);
            var detailReads = kit.Http.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/1/metadata-match-queue", StringComparison.Ordinal));
            scheduler.Advance(AdminMatcherQueuesPanel.PollInterval);
            await Until(() => kit.Http.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/1/metadata-match-queue", StringComparison.Ordinal)) == detailReads + 1);
            panel.SetOpen(false);
            var closed = kit.Http.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/1/metadata-match-queue", StringComparison.Ordinal));
            scheduler.Advance(AdminMatcherQueuesPanel.PollInterval * 3);
            await Task.Delay(50);
            Assert.Equal(closed, kit.Http.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/1/metadata-match-queue", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task FolderBrowserValidatesAbsolutePathsAndSelectsOnlyNewFolders()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            // The synthetic server honours the requested limit, as the real one does (the path probe asks for one entry).
            server.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/admin/filesystem/browse"
                ? AdminMonitorKit.Json(request.RequestUri.Query.Contains("path=%2Fmnt") && request.RequestUri.Query.EndsWith("limit=1", StringComparison.Ordinal)
                    ? "{\"path\":\"/mnt\",\"parent\":\"/\",\"items\":[{\"name\":\"a\",\"path\":\"/mnt/a\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"n\"}}"
                    : request.RequestUri.Query.Contains("path=%2Fmnt")
                    ? "{\"path\":\"/mnt\",\"parent\":\"/\",\"items\":[{\"name\":\"a\",\"path\":\"/mnt/a\"},{\"name\":\"b\",\"path\":\"/mnt/b\"}],\"page\":{\"has_more\":false}}"
                    : "{\"path\":\"/\",\"parent\":\"/\",\"items\":[{\"name\":\"mnt\",\"path\":\"/mnt\"}],\"page\":{\"has_more\":false}}")
                : null;
            using var lease = controller.CaptureAction();
            using var browser = new AdminFolderBrowserSession(controller, lease, ["/mnt/a"]);
            await browser.OpenAsync();
            Assert.False(browser.State.CanGoUp);
            Assert.Null(await browser.SubmitDraftAsync("mnt"));
            Assert.NotNull(browser.State.ValidationError);
            Assert.Equal("/mnt", await browser.SubmitDraftAsync(" /mnt "));
            Assert.True(browser.State.CanGoUp);
            browser.ToggleSelected("/mnt/a");
            browser.ToggleSelected("/mnt/b");
            Assert.Equal(["/mnt/b"], browser.Selection());
            browser.ToggleSelected("/mnt/b");
            Assert.Equal(["/mnt"], browser.Selection());
        }
    }
}
