using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Admin › Playback › History and Skip Markers (G10 read-only audit; official
/// Silo e61187daaf72e8accad486eb2f632c753ae211d5). Controllable HTTP fakes and a
/// manual clock prove request bounds, URL/filter resets, local pagination without
/// extra reads, refresh coalescing and cancellation, stale-authority rejection,
/// finalized fields, unknown values and error/retry. Synthetic data only.
/// </summary>
public sealed class AdminPlaybackAuditTests
{
    private static readonly AppLocale English = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));

    // ── Fixtures ──────────────────────────────────────────────

    /// <summary>Official contract fixture list_admin_playback_history_ok.json (documentation-range address, synthetic names).</summary>
    private const string OfficialHistoryFixture = """
        {"items":[{"session_id":"sess-9007199254740993","user_id":"9007199254740993","username":"laura","profile_id":"p-owner","profile_name":"Laura",
        "media_item_id":"movie-1","media_file_id":"42","media_title":"Synthetic Movie","media_type":"movie","play_method":"direct_play",
        "started_at":"2026-01-02T02:04:05.678Z","ended_at":"2026-01-02T03:04:05.678Z","watched_seconds":3600,"duration_seconds":5400.5,"completed":true,
        "played_seconds":120,"series_title":"","season_number":null,"episode_number":null,"measured":true,"start_position_seconds":3480,"paused_seconds":15,
        "measured_since":"2026-01-02T03:01:05.678Z","measured_until":"2026-01-02T03:03:05.678Z","client_ip":"2001:db8::7","device_id":"iphone-1",
        "device_platform":"iOS","client_name":null,"client_version":null,"video_decision":"direct","audio_decision":null,"end_reason":"stopped","lifecycle_seq":null}],
        "page":{"next_cursor":"eyJ2IjoxfQ.sig","has_more":true}}
        """;

    /// <summary>Official contract fixture admin_marker_history_all.json.</summary>
    private const string OfficialMarkerFixture = """
        {"history":[{"id":"9007199254740993","media_file_id":"42","segment":"intro","action":"clear","before":{"start_seconds":1.5,"end_seconds":10},
        "user_id":"7","api_key_id":"9007199254740995","created_at":"2026-01-02T02:04:05.000Z"}]}
        """;

    private static string Entry(int index, bool completed = true, string? userId = "7", string profile = "p1", string? played = "60") => $$"""
        {"session_id":"s-{{index:D4}}","user_id":"{{userId}}","username":"user{{userId}}","profile_id":"{{profile}}","profile_name":"Profile {{profile}}",
        "media_item_id":"item-{{index}}","media_file_id":"{{index + 1}}","media_title":"Title {{index}}","media_type":"movie","play_method":"direct",
        "started_at":"2026-11-01T10:00:00Z","ended_at":"2026-11-01T11:00:00Z","watched_seconds":3000,"duration_seconds":3600,
        "completed":{{(completed ? "true" : "false")}},"played_seconds":{{played ?? "null"}},"series_title":"","season_number":null,"episode_number":null}
        """;

    private static string HistoryPage(int count, bool hasMore = false, Func<int, bool>? completed = null, string? userId = "7", string profile = "p1") =>
        "{\"items\":[" + string.Join(",", Enumerable.Range(0, count).Select(index => Entry(index, completed?.Invoke(index) ?? true, userId, profile))) + "],\"page\":{\"has_more\":"
        + (hasMore ? "true,\"next_cursor\":\"next-1\"" : "false") + "}}";

    private const string EmptyPage = "{\"items\":[],\"page\":{\"has_more\":false}}";

    /// <summary>A scripted server: per-path responders, optional gates that hold a response until released.</summary>
    private sealed class Server
    {
        private readonly object _gate = new();
        private readonly List<(string Prefix, TaskCompletionSource<HttpResponseMessage> Release)> _held = [];
        public Func<string, HttpResponseMessage?> Respond { get; set; } = _ => null;
        public HashSet<string> HoldPrefixes { get; } = [];
        public List<string> Requests { get; } = [];

        public void Attach(AdminMonitorKit kit) => kit.Http.Respond = async (request, ct) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            TaskCompletionSource<HttpResponseMessage>? hold = null;
            lock (_gate)
            {
                Requests.Add(path);
                var prefix = HoldPrefixes.FirstOrDefault(candidate => path.StartsWith(candidate, StringComparison.Ordinal));
                if (prefix != null) { hold = new(TaskCreationOptions.RunContinuationsAsynchronously); _held.Add((path, hold)); }
            }
            if (hold != null)
            {
                await using var registration = ct.Register(() => hold.TrySetCanceled(ct));
                return await hold.Task;
            }
            return Respond(path) ?? AdminMonitorKit.Json(path.StartsWith("/api/v2/admin/users") ? EmptyPage : "{}");
        };

        public int Count(string prefix) { lock (_gate) return Requests.Count(path => path.StartsWith(prefix, StringComparison.Ordinal)); }
        public int HeldCount { get { lock (_gate) return _held.Count; } }

        /// <summary>Releases the oldest held request whose path starts with <paramref name="prefix"/>.</summary>
        public string Release(string prefix, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            (string Path, TaskCompletionSource<HttpResponseMessage> Release) entry;
            lock (_gate)
            {
                entry = _held.First(held => held.Prefix.StartsWith(prefix, StringComparison.Ordinal));
                _held.Remove(entry);
            }
            entry.Release.TrySetResult(AdminMonitorKit.Json(json, status));
            return entry.Path;
        }
    }

    /// <summary>Deterministic clock: timers and <c>Task.Delay(..., provider)</c> fire only on <see cref="Advance"/>.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_gate) return _ticks; }
        public int ActiveTimers { get { lock (_gate) return _timers.Count(timer => timer.Due != null); } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate) _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _now += by; _ticks += by.Ticks;
                due = _timers.Where(timer => timer.Due is { } at && at <= _ticks).ToList();
                foreach (var timer in due) timer.Due = timer.Period is { } period && period > TimeSpan.Zero ? _ticks + period.Ticks : null;
            }
            foreach (var timer in due) timer.Fire();
        }

        private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state) : ITimer
        {
            public long? Due { get; set; }
            public TimeSpan? Period { get; private set; }
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._ticks + dueTime.Ticks;
                    Period = period == Timeout.InfiniteTimeSpan ? null : period;
                }
                return true;
            }
            public void Dispose() { lock (owner._gate) { Due = null; owner._timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 3000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 10) await Task.Delay(10);
        Assert.True(condition(), "condition was not reached");
    }

    private sealed class Harness : IDisposable
    {
        public AdminMonitorKit Kit { get; } = AdminMonitorKit.Create();
        public Server Server { get; } = new();
        public ManualTime Time { get; } = new();
        public AdminMonitorAuthority Authority { get; }
        public AdminPlaybackHistoryController History { get; }
        public AdminMarkerHistoryController Markers { get; }

        public Harness()
        {
            Server.Attach(Kit);
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            History = new AdminPlaybackHistoryController(new AdminPlaybackHistoryApi(Kit.Client), Authority, Time);
            Markers = new AdminMarkerHistoryController(new AdminMarkerHistoryApi(Kit.Client), Authority);
        }

        public void Dispose()
        {
            History.Dispose();
            Markers.Dispose();
            Authority.Dispose();
            Kit.Dispose();
        }
    }

    // ── Request shape and bounds ──────────────────────────────

    [Fact]
    public void HistoryRequestCarriesTheWebUiLimitAndOnlyValidFilters()
    {
        Assert.Equal("/api/v2/admin/playback-history?limit=100", AdminPlaybackHistoryApi.HistoryPath(new()));
        Assert.Equal("/api/v2/admin/playback-history?limit=100&user_id=7&profile_id=p%201&media_item_id=item%2F9&completed=false",
            AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter("7", "p 1", AdminPlaybackHistoryCompletion.Partial, "  item/9 ")));
        // Invalid accounts are omitted; positive Int64 IDs remain exact, including beyond JavaScript precision.
        Assert.Equal("/api/v2/admin/playback-history?limit=100", AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter("abc", "p1")));
        Assert.Equal("/api/v2/admin/playback-history?limit=100", AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter("0")));
        Assert.Equal("/api/v2/admin/playback-history?limit=100&user_id=9007199254740993", AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter("9007199254740993")));
        Assert.Equal("/api/v2/admin/playback-history?limit=100", AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(null, "p1")));
        Assert.Equal("/api/v2/admin/playback-history?limit=100&user_id=7&completed=true",
            AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter("007", null, AdminPlaybackHistoryCompletion.Completed)));
        // The v2 ceiling bounds any caller.
        Assert.StartsWith("/api/v2/admin/playback-history?limit=200", AdminPlaybackHistoryApi.HistoryPath(new(), 5000));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(MediaItemId: new string('x', 1025))));
        Assert.Equal("/api/v2/admin/markers/history?limit=100", AdminMarkerHistoryApi.HistoryPath(500));
        Assert.Equal("/api/v2/admin/markers/history?limit=1", AdminMarkerHistoryApi.HistoryPath(0));
    }

    [Theory]
    [InlineData("abc", null)]
    [InlineData("0", null)]
    [InlineData("9223372036854775808", null)]
    [InlineData("007", "7")]
    [InlineData("9007199254740993", "9007199254740993")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    public void AccountFilterNormalizesToTheExactPositiveInt64(string input, string? expected)
    {
        var normalized = new AdminPlaybackHistoryFilter(input, "p1").Normalized();
        Assert.Equal(expected, normalized.UserId);
        Assert.Equal(expected != null ? "p1" : null, normalized.ProfileId);
        Assert.Equal(expected, normalized.ToQuery().GetValueOrDefault("user_id"));
    }

    [Fact]
    public void UrlStateParsesLikeTheWebUiAndRoundTrips()
    {
        var all = AdminPlaybackHistoryFilter.FromQuery(key => key switch { "user_id" => "all", "profile_id" => "all", "completed" => "all", _ => null });
        Assert.Equal(new AdminPlaybackHistoryFilter(), all);
        var orphanProfile = AdminPlaybackHistoryFilter.FromQuery(key => key == "profile_id" ? "p1" : null);
        Assert.Null(orphanProfile.ProfileId);
        var full = AdminPlaybackHistoryFilter.FromQuery(key => key switch
        {
            "user_id" => "7", "profile_id" => "p1", "completed" => "true", "media_item_id" => "  item-1  ", _ => null,
        });
        Assert.Equal(new AdminPlaybackHistoryFilter("7", "p1", AdminPlaybackHistoryCompletion.Completed, "item-1"), full);
        Assert.Equal(new Dictionary<string, string> { ["user_id"] = "7", ["profile_id"] = "p1", ["completed"] = "true", ["media_item_id"] = "item-1" }, full.ToQuery());
        Assert.Empty(new AdminPlaybackHistoryFilter().ToQuery());
    }

    [Fact]
    public void OfficialHistoryFixtureProjectsFinalizedFieldsAndCarriesExtendedOnes()
    {
        var page = JsonSerializer.Deserialize<AdminPlaybackHistoryWirePage>(OfficialHistoryFixture, SnakeCase)!;
        var window = AdminPlaybackHistoryApi.ValidateWindow(page, 100);
        var row = Assert.Single(window.Rows);
        Assert.True(window.ServerHasMore);
        Assert.Equal("sess-9007199254740993", row.SessionId);
        Assert.Equal("9007199254740993", row.UserId); // opaque string; never narrowed to a double
        Assert.Equal("42", row.MediaFileId);
        Assert.True(row.Completed);
        Assert.Equal(120, row.PlayedSeconds);
        Assert.Equal(5400.5, row.DurationSeconds);
        Assert.True(row.Measured);
        Assert.Equal("2001:db8::7", row.ClientIp);
        Assert.Equal("iOS", row.DevicePlatform);
        Assert.Equal("stopped", row.EndReason);
        Assert.Null(row.ClientName);
        Assert.Null(row.LifecycleSeq);
    }

    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    [Theory]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"c\"}}")]                       // has_more with no rows
    [InlineData("{\"items\":[ENTRY],\"page\":{\"has_more\":true}}")]                                          // has_more with no cursor
    [InlineData("{\"items\":[ENTRY],\"page\":{\"has_more\":false,\"next_cursor\":\"c\"}}")]                  // cursor without has_more
    [InlineData("{\"items\":[ENTRY]}")]                                                                      // no page info
    [InlineData("{\"page\":{\"has_more\":false}}")]
    [InlineData("{\"items\":[],\"page\":{}}")] // missing pagination flag
    [InlineData("{\"items\":[MISSING_COMPLETED],\"page\":{\"has_more\":false}}")]                             // required field absent
    [InlineData("{\"items\":[BAD_USER],\"page\":{\"has_more\":false}}")]                                      // non-numeric account id
    public void InconsistentHistoryPagesAreRefusedWhole(string template)
    {
        var json = template.Replace("ENTRY", Entry(1)).Replace("MISSING_COMPLETED", Entry(1).Replace("\"completed\":true,", ""))
            .Replace("BAD_USER", Entry(1, userId: "u-7"));
        var page = JsonSerializer.Deserialize<AdminPlaybackHistoryWirePage>(json, SnakeCase);
        using var _ = AppStrings.UseForCurrentFlow(English);
        var error = Assert.Throws<InvalidDataException>(() => AdminPlaybackHistoryApi.ValidateWindow(page, 100));
        Assert.Equal("Invalid playback history page. Reload the page.", error.Message);
    }

    [Fact]
    public void OversizedHistoryPageIsRefused()
    {
        var page = JsonSerializer.Deserialize<AdminPlaybackHistoryWirePage>(HistoryPage(3), SnakeCase);
        Assert.Throws<InvalidDataException>(() => AdminPlaybackHistoryApi.ValidateWindow(page, 2));
    }

    [Fact]
    public async Task HistoryWindowIsOneRequestEvenWhenTheServerHasMore()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json(HistoryPage(100, hasMore: true)) : null;
        using var lease = h.Authority.Capture();
        var window = await new AdminPlaybackHistoryApi(h.Kit.Client).ReadWindowAsync(lease, new AdminPlaybackHistoryFilter());
        Assert.Equal(100, window.Rows.Count);
        Assert.True(window.ServerHasMore);
        Assert.Equal(1, h.Server.Count("/api/v2/admin/playback-history"));
        Assert.DoesNotContain(h.Server.Requests, path => path.Contains("cursor=", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Server.Requests, path => path.StartsWith("/api/v2/admin/sessions", StringComparison.Ordinal)); // never live streams
    }

    [Fact]
    public async Task UserPickerStopsAtItsPageCapAndSaysItIsTruncated()
    {
        using var h = new Harness();
        var page = 0;
        h.Server.Respond = path =>
        {
            if (!path.StartsWith("/api/v2/admin/users")) return null;
            var start = Interlocked.Increment(ref page) * 1000;
            var items = string.Join(",", Enumerable.Range(start, 3).Select(id => $"{{\"id\":\"{id}\",\"username\":\"u{id}\"}}"));
            return AdminMonitorKit.Json($"{{\"items\":[{items}],\"page\":{{\"has_more\":true,\"next_cursor\":\"c{start}\"}}}}");
        };
        using var lease = h.Authority.Capture();
        var users = await new AdminPlaybackHistoryApi(h.Kit.Client).ListUsersAsync(lease);
        Assert.True(users.Truncated);
        Assert.Equal(AdminPlaybackHistoryApi.UserMaxPages, h.Server.Count("/api/v2/admin/users"));
        Assert.Equal(AdminPlaybackHistoryApi.UserMaxPages * 3, users.Items.Count);
        Assert.All(h.Server.Requests, path => Assert.Contains("limit=200", path));
    }

    [Fact]
    public async Task ProfileListingWithMoreIsRefusedAndOversizedListsAreBounded()
    {
        using var h = new Harness();
        var api = new AdminPlaybackHistoryApi(h.Kit.Client);
        using var lease = h.Authority.Capture();
        h.Server.Respond = _ => AdminMonitorKit.Json("{\"items\":[{\"id\":\"p1\",\"name\":\"One\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"x\"}}");
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListProfilesAsync(lease, "7"));
        var many = string.Join(",", Enumerable.Range(0, 150).Select(index => $"{{\"id\":\"p{index}\",\"name\":\"P{index}\"}}"));
        h.Server.Respond = _ => AdminMonitorKit.Json($"{{\"items\":[{many}],\"page\":{{\"has_more\":false}}}}");
        var profiles = await api.ListProfilesAsync(lease, "7");
        Assert.Equal(AdminPlaybackHistoryApi.ProfileMaxItems, profiles.Items.Count);
        Assert.True(profiles.Truncated);
        Assert.Contains("/api/v2/admin/users/7/profiles", h.Server.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => api.ListProfilesAsync(lease, "../7"));
    }

    // ── Controller: local pagination, filters, refresh ───────

    [Fact]
    public async Task LocalPaginationNeverReadsTheServerAgain()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history")
            ? AdminMonitorKit.Json(HistoryPage(100, hasMore: true, completed: index => index % 4 != 0)) : null;
        Assert.True(h.History.Open(new AdminPlaybackHistoryFilter()));
        await Until(() => h.History.State.Window != null);
        var state = h.History.State;
        Assert.Equal(100, state.Total);
        Assert.Equal(75, state.CompletedCount);
        Assert.Equal(25, state.PartialCount);
        Assert.Equal(25, state.PageRows.Count);
        Assert.Equal((1, 25), (state.RangeFrom, state.RangeTo));
        Assert.False(state.CanPrevious);
        Assert.True(state.Window!.ServerHasMore);

        h.History.NextPage(); h.History.NextPage(); h.History.NextPage();
        state = h.History.State;
        Assert.Equal(3, state.PageIndex);
        Assert.Equal((76, 100), (state.RangeFrom, state.RangeTo));
        Assert.False(state.CanNext);
        h.History.NextPage();
        Assert.Equal(3, h.History.State.PageIndex);
        h.History.SetPageSize(50);
        Assert.Equal((0, 50), (h.History.State.PageIndex, h.History.State.PageRows.Count));
        h.History.SetPageSize(100);
        Assert.False(h.History.State.CanNext);
        Assert.Throws<ArgumentOutOfRangeException>(() => h.History.SetPageSize(30));
        Assert.Equal(1, h.Server.Count("/api/v2/admin/playback-history"));
        Assert.Equal(1, h.History.HistoryReadsStarted);
    }

    [Fact]
    public async Task FilterChangeCancelsTheOldReadAndDiscardsItsLateAnswer()
    {
        using var h = new Harness();
        h.Server.HoldPrefixes.Add("/api/v2/admin/playback-history");
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.Server.HeldCount == 1);
        h.History.SetPageSize(50);
        h.History.SelectCompletion(AdminPlaybackHistoryCompletion.Completed);
        Assert.True(h.History.State.Loading);
        Assert.Equal(0, h.History.State.PageIndex);
        await Until(() => h.Server.HeldCount >= 2 || h.Server.Count("/api/v2/admin/playback-history") == 2);
        // The first request's answer arrives late (or was cancelled): it must never show.
        try { h.Server.Release("/api/v2/admin/playback-history?limit=100", HistoryPage(5, completed: _ => false)); } catch (InvalidOperationException) { }
        h.Server.Release("/api/v2/admin/playback-history?limit=100&completed=true", HistoryPage(2));
        await Until(() => h.History.State.Window != null);
        Assert.Equal(2, h.History.State.Total);
        Assert.All(h.History.State.Rows, row => Assert.True(row.Completed));
        Assert.Equal(AdminPlaybackHistoryCompletion.Completed, h.History.State.Filter.Completion);
    }

    [Fact]
    public async Task OverlappingRefreshesJoinOneReadPerChannel()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json(HistoryPage(3)) : null;
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.History.State.Window != null);
        h.Server.HoldPrefixes.Add("/api/v2/admin/playback-history");
        var first = h.History.RefreshAsync();
        var second = h.History.RefreshAsync();
        var manual = h.History.RefreshAsync(manual: true);
        var manualAgain = h.History.RefreshAsync(manual: true);
        await Until(() => h.Server.HeldCount == 1);
        Assert.Same(first, second);
        Assert.Same(manual, manualAgain);
        Assert.True(h.History.State.Fetching);
        Assert.False(h.History.State.Loading); // the previous rows stay while refreshing
        Assert.Equal(3, h.History.State.Total);
        Assert.True(h.History.State.ManualRefreshPending);
        h.Server.Release("/api/v2/admin/playback-history", HistoryPage(4));
        await first;
        Assert.Equal(4, h.History.State.Total);
        Assert.Equal(2, h.Server.Count("/api/v2/admin/playback-history"));
        // The manual spinner stays at least one second (manual clock), then clears.
        Assert.True(h.History.State.ManualRefreshPending);
        await Until(() => h.Time.ActiveTimers > 0);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await manual;
        Assert.False(h.History.State.ManualRefreshPending);
    }

    [Fact]
    public async Task PollingRunsOnlyWhileVisibleAndNeverWhenHidden()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json(HistoryPage(1)) : null;
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.History.State.Window != null);
        Assert.False(h.History.IsPolling);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(1, h.Server.Count("/api/v2/admin/playback-history"));

        h.History.SetVisible(true);
        Assert.True(h.History.IsPolling);
        h.Time.Advance(AdminPlaybackHistoryController.RefreshInterval);
        await Until(() => h.Server.Count("/api/v2/admin/playback-history") == 2);
        await Until(() => !h.History.State.Fetching);

        h.History.SetVisible(false);
        Assert.False(h.History.IsPolling);
        h.Time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        Assert.Equal(2, h.Server.Count("/api/v2/admin/playback-history"));

        // Returning with stale data refreshes once (WebUI refetch on focus), then polls again.
        h.History.SetVisible(true);
        await Until(() => h.Server.Count("/api/v2/admin/playback-history") == 3);
        Assert.True(h.History.IsPolling);

        h.History.Suspend();
        Assert.False(h.History.IsPolling);
        Assert.False(h.History.State.IsOpen);
        Assert.Empty(h.History.State.Rows);
        h.Time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        Assert.Equal(3, h.Server.Count("/api/v2/admin/playback-history"));
    }

    [Fact]
    public async Task ResetClearsEveryUrlParameterAndRestartsAtTheFirstPage()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json(HistoryPage(60)) : null;
        h.History.Open(new AdminPlaybackHistoryFilter("7", null, AdminPlaybackHistoryCompletion.Partial, "item-1"));
        await Until(() => h.History.State.Window != null);
        h.History.NextPage();
        var reset = h.History.Reset();
        Assert.Equal(new AdminPlaybackHistoryFilter(), reset);
        Assert.Equal(0, h.History.State.PageIndex);
        Assert.Null(h.History.State.Profiles);
        await Until(() => h.Server.Requests.Contains("/api/v2/admin/playback-history?limit=100"));
        // Choosing an account always drops the profile.
        await Until(() => h.History.State.Window != null);
        var chosen = h.History.SelectUser("8");
        Assert.Null(chosen.ProfileId);
        Assert.True(h.History.State.ProfilePickerEnabled);
        h.History.SelectUser(null);
        Assert.False(h.History.State.ProfilePickerEnabled);
    }

    [Fact]
    public async Task TruncatedProfilePickerDoesNotDiscardAnUnlistedSelection()
    {
        using var h = new Harness();
        var profiles = Enumerable.Range(1, 101).Select(id => new { id = "p" + id, name = "Profile " + id });
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/users/7/profiles")
            ? AdminMonitorKit.Json(JsonSerializer.Serialize(new { items = profiles, page = new { has_more = false } }))
            : path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json(HistoryPage(1)) : null;
        h.History.Open(new AdminPlaybackHistoryFilter("7", "p101"));
        await Until(() => h.History.State.Profiles != null);
        Assert.True(h.History.State.Profiles!.Truncated);
        Assert.Equal("p101", h.History.State.Filter.ProfileId);
    }

    [Fact]
    public async Task UnknownProfileIsReplacedAwayAndHistoryReadWithoutIt()
    {
        using var h = new Harness();
        h.Server.Respond = path => path switch
        {
            _ when path.StartsWith("/api/v2/admin/users/7/profiles") => AdminMonitorKit.Json("{\"items\":[{\"id\":\"p1\",\"name\":\"One\"},{\"id\":\"p2\",\"name\":\"Two\"}],\"page\":{\"has_more\":false}}"),
            _ when path.StartsWith("/api/v2/admin/playback-history") => AdminMonitorKit.Json(HistoryPage(1)),
            _ => null,
        };
        AdminPlaybackHistoryFilter? replaced = null;
        h.History.FilterReplaced += filter => replaced = filter;
        h.History.Open(new AdminPlaybackHistoryFilter("7", "gone"));
        await Until(() => replaced != null);
        Assert.Equal(new AdminPlaybackHistoryFilter("7"), replaced);
        await Until(() => h.Server.Requests.Contains("/api/v2/admin/playback-history?limit=100&user_id=7"));
        Assert.Contains("/api/v2/admin/playback-history?limit=100&user_id=7&profile_id=gone", h.Server.Requests);
        Assert.Equal(new[] { "p1", "p2" }, h.History.State.Profiles!.Items.Select(profile => profile.Id));
    }

    [Fact]
    public async Task StaleAccountAnswerIsRejectedAndEverythingClears()
    {
        using var h = new Harness();
        h.Server.HoldPrefixes.Add("/api/v2/admin/playback-history");
        var retired = 0;
        h.History.AuthorityRetired += () => Interlocked.Increment(ref retired);
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.Server.HeldCount == 1);
        // The account loses admin while the read is in flight.
        h.Kit.SetRole("user");
        Assert.Equal(1, retired);
        try { h.Server.Release("/api/v2/admin/playback-history", HistoryPage(5)); } catch (InvalidOperationException) { }
        await Task.Delay(50);
        var state = h.History.State;
        Assert.False(state.IsOpen);
        Assert.Empty(state.Rows);
        Assert.Null(state.Users);
        Assert.Null(state.Error);
        Assert.False(h.History.Reopen()); // no longer acting admin
    }

    [Fact]
    public async Task ProfileSwitchRetiresAndReopensUnderTheNewAuthority()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json(HistoryPage(2)) : null;
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.History.State.Window != null);
        h.Server.HoldPrefixes.Add("/api/v2/admin/playback-history");
        _ = h.History.RefreshAsync();
        await Until(() => h.Server.HeldCount == 1);
        h.Kit.Auth.SelectProfile("p2", null, new Profile { Id = "p2", Name = "Second primary", IsPrimary = true });
        Assert.False(h.History.State.IsOpen);
        try { h.Server.Release("/api/v2/admin/playback-history", HistoryPage(9)); } catch (InvalidOperationException) { }
        h.Server.HoldPrefixes.Clear();
        Assert.True(h.History.Reopen());
        await Until(() => h.History.State.Window != null);
        Assert.Equal(2, h.History.State.Total); // the old authority's nine rows never appear
    }

    [Fact]
    public async Task ServerErrorShowsAndManualRefreshRetries()
    {
        using var h = new Harness();
        var fail = true;
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history")
            ? fail ? AdminMonitorKit.Json("{\"type\":\"internal_error\",\"title\":\"Unable to list playback history.\",\"status\":500}", HttpStatusCode.InternalServerError)
                   : AdminMonitorKit.Json(HistoryPage(1))
            : null;
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.History.State.Error != null);
        Assert.Equal(AdminFailureKind.Server, h.History.State.ErrorKind);
        Assert.False(h.History.State.Loading);
        Assert.Equal(1, h.Server.Count("/api/v2/admin/playback-history")); // no automatic retry (WebUI retry: false)
        fail = false;
        var retry = h.History.RefreshAsync(manual: true);
        await Until(() => h.History.State.Window != null && h.History.State.Error == null);
        Assert.Equal(1, h.History.State.Total);
        await Until(() => h.Time.ActiveTimers > 0);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await retry;
    }

    [Fact]
    public async Task InvalidPageShowsTheOfficialMessageAndTransportShowsTheLoadError()
    {
        using var h = new Harness();
        using var _ = AppStrings.UseForCurrentFlow(English);
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json("{\"items\":[],\"page\":{\"has_more\":true}}") : null;
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.History.State.Error != null);
        Assert.Equal("Invalid playback history page. Reload the page.", h.History.State.Error);
        Assert.Equal(AdminFailureKind.InvalidResponse, h.History.State.ErrorKind);
        Assert.Equal("Failed to load playback history", AdminPlaybackHistoryController.ErrorText(new HttpRequestException("socket detail"), AdminFailureKind.Transport));
    }

    [Fact]
    public async Task EmptyHistoryIsNotLoadingAndHasNoPages()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/playback-history") ? AdminMonitorKit.Json(EmptyPage) : null;
        h.History.Open(new AdminPlaybackHistoryFilter());
        await Until(() => h.History.State.Window != null);
        var state = h.History.State;
        Assert.False(state.Loading);
        Assert.Equal(0, state.Total);
        Assert.False(state.CanNext);
        Assert.Null(state.Error);
    }

    // ── Presentation ─────────────────────────────────────────

    private static AdminPlaybackHistoryRow Row(string mediaTitle = "Pilot", string series = "", int? season = null, int? episode = null,
        double? played = null, double watched = 600, string started = "2026-11-01T10:00:00Z", string ended = "2026-11-01T10:05:00Z",
        string type = "episode", string itemId = "item-1", string username = "", string profileName = "") =>
        new("abcdef0123456789", "7", username, "p1", profileName, itemId, "42", mediaTitle, type, "transcode", started, ended, watched, 3600, false, played,
            series, season, episode);

    [Fact]
    public void RowTextFollowsTheWebUi()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal(("Show", "S02E04 · Pilot"), AdminPlaybackHistoryPresentation.RowTitle(Row(series: "Show", season: 2, episode: 4)));
        Assert.Equal(("Show", "S01E01"), AdminPlaybackHistoryPresentation.RowTitle(Row("Show", "Show", 1, 1))); // a pilot named after its show
        Assert.Equal(("item-1", ""), AdminPlaybackHistoryPresentation.RowTitle(Row("", type: "movie")));
        Assert.Equal(("File #42", ""), AdminPlaybackHistoryPresentation.RowTitle(Row("", itemId: "")));
        Assert.Equal("Show · S02E04 · Pilot", AdminPlaybackHistoryPresentation.RowLabel(Row(series: "Show", season: 2, episode: 4)));
        Assert.Equal("User #7", AdminPlaybackHistoryPresentation.Username(Row()));
        Assert.Equal("p1", AdminPlaybackHistoryPresentation.ProfileLabel(Row()));
        Assert.Equal("Transcode", AdminPlaybackHistoryPresentation.Method(Row()));
        Assert.Equal("Partial", AdminPlaybackHistoryPresentation.Status(Row()));
        Assert.Equal("episode · session abcdef01", AdminPlaybackHistoryPresentation.Meta(Row()));
        Assert.Equal("unknown", AdminPlaybackHistoryPresentation.MediaTypeLabel(""));
        Assert.Equal("audiobook", AdminPlaybackHistoryPresentation.MediaTypeLabel("audiobook")); // others print as sent
    }

    [Fact]
    public void WatchTimeIsMeasuredOrAnEstimateCappedAtTheSessionLength()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal((90d, false), AdminPlaybackHistoryPresentation.WatchTime(Row(played: 90)));
        Assert.Equal((300d, true), AdminPlaybackHistoryPresentation.WatchTime(Row(watched: 600))); // 5-minute session caps the 10-minute end position
        Assert.Equal((600d, true), AdminPlaybackHistoryPresentation.WatchTime(Row(watched: 600, ended: "not-a-time")));
        Assert.Equal((0d, true), AdminPlaybackHistoryPresentation.WatchTime(Row(started: "2026-11-01T11:00:00Z", ended: "2026-11-01T10:00:00Z")));
        Assert.Equal("≈ 5m 0s", AdminPlaybackHistoryPresentation.WatchTimeText(Row()));
        Assert.Equal("1m 30s", AdminPlaybackHistoryPresentation.WatchTimeText(Row(played: 90)));
        Assert.Equal("0m", AdminPlaybackHistoryPresentation.FormatDuration(null));
        Assert.Equal("0m", AdminPlaybackHistoryPresentation.FormatDuration(0));
        Assert.Equal("0m", AdminPlaybackHistoryPresentation.FormatDuration(double.NaN));
        Assert.Equal("59s", AdminPlaybackHistoryPresentation.FormatDuration(59.9));
        Assert.Equal("2h 3m", AdminPlaybackHistoryPresentation.FormatDuration(7384));
    }

    [Fact]
    public void TimestampsAreRelativeWhenParsableAndShownAsSentOtherwise()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("started 2h ago", AdminPlaybackHistoryPresentation.Started(Row(), now));
        Assert.Equal("started 59m ago", AdminPlaybackHistoryPresentation.Started(Row(started: "2026-11-01T11:00:30Z"), now)); // floor rounding
        Assert.Equal("started just now", AdminPlaybackHistoryPresentation.Started(Row(started: "2026-11-01T11:59:30Z"), now));
        Assert.Equal("started yesterday-ish", AdminPlaybackHistoryPresentation.Started(Row(started: "yesterday-ish"), now));
        Assert.Null(AdminPlaybackAuditText.Relative("", now));
    }

    [Fact]
    public void RowLinksTargetTheOfficialRoutes()
    {
        var logs = AdminPlaybackHistoryPresentation.LogsRequest(Row(), ffmpeg: true);
        Assert.Equal("/admin/logs?component=ffmpeg&focus=playback&playback_session_id=abcdef0123456789", logs.Href);
        Assert.Equal("/admin/history?profile_id=p1&user_id=7", AdminPlaybackHistoryPresentation.ProfileRequest(Row()).Href);
    }

    // ── Skip Markers ─────────────────────────────────────────

    [Fact]
    public void OfficialMarkerFixtureValidatesAndRendersReadOnlyAuditText()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        var body = JsonSerializer.Deserialize<AdminMarkerHistoryWire>(OfficialMarkerFixture, SnakeCase);
        var row = Assert.Single(AdminMarkerHistoryApi.Validate(body, 50));
        Assert.Equal("9007199254740993", row.Id);
        Assert.Null(row.After);
        Assert.Equal("File 42", AdminMarkerHistoryPresentation.Title(row));
        Assert.Equal("Media file 42", AdminMarkerHistoryPresentation.FileLine(row));
        Assert.Equal("Intro", AdminMarkerHistoryPresentation.SegmentLabel(row.Segment));
        Assert.Equal("Cleared", AdminMarkerHistoryPresentation.ActionLabel(row));
        Assert.Equal("0:02-0:10 -> none", AdminMarkerHistoryPresentation.RangeChange(row)); // 1.5 rounds half up
        Assert.Equal("Unknown user", AdminMarkerHistoryPresentation.Actor(row));
        Assert.Null(AdminMarkerHistoryPresentation.ImpersonatedVia(row));
        Assert.Equal("No request id", AdminMarkerHistoryPresentation.Request(row));
        Assert.Equal("", AdminMarkerHistoryPresentation.Client(row));
    }

    [Fact]
    public void MarkerKindsKeepTheirLabelsAndUnknownKindsShowAsSent()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Recap", AdminMarkerHistoryPresentation.SegmentLabel("recap"));
        Assert.Equal("Credits / Outro", AdminMarkerHistoryPresentation.SegmentLabel("credits"));
        Assert.Equal("Preview", AdminMarkerHistoryPresentation.SegmentLabel("preview"));
        Assert.Equal("commercial", AdminMarkerHistoryPresentation.SegmentLabel("commercial"));
        Assert.Equal("—", AdminMarkerHistoryPresentation.SegmentLabel(""));
        Assert.Equal("1:01:01", AdminMarkerHistoryPresentation.Clock(3661));
        Assert.Equal("0:00", AdminMarkerHistoryPresentation.Clock(-4));
        var edit = new AdminMarkerEditRow("1", "5", "item", "episode", "", "/media/show/S01E01.mkv", "credits", "set",
            new AdminMarkerSegment(null, 20, null, null, null, null, null), new AdminMarkerSegment(70, 95.4, "manual", null, null, null, null),
            "3", "", "9", "owner", null, "req-1", null, "ExampleAgent/1", "2026-11-01T10:00:00Z");
        Assert.Equal("S01E01.mkv (episode)", AdminMarkerHistoryPresentation.ItemLabel(edit));
        Assert.Equal("none -> 1:10-1:35", AdminMarkerHistoryPresentation.RangeChange(edit));
        Assert.Equal("Set", AdminMarkerHistoryPresentation.ActionLabel(edit));
        Assert.Equal("", AdminMarkerHistoryPresentation.Actor(edit)); // ?? semantics: an empty name is not "Unknown user"
        Assert.Equal("via owner", AdminMarkerHistoryPresentation.ImpersonatedVia(edit));
        Assert.Equal("ExampleAgent/1", AdminMarkerHistoryPresentation.Client(edit));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"history\":null}")]
    [InlineData("{\"history\":[{\"media_file_id\":\"1\",\"created_at\":\"2026-01-01T00:00:00Z\"}]}")]
    public void InvalidMarkerHistoryIsRefused(string json)
    {
        Assert.Throws<InvalidDataException>(() => AdminMarkerHistoryApi.Validate(JsonSerializer.Deserialize<AdminMarkerHistoryWire>(json, SnakeCase), 50));
    }

    [Fact]
    public void MarkerHistoryLargerThanRequestedIsRefused()
    {
        var rows = string.Join(",", Enumerable.Range(1, 26).Select(id => $"{{\"id\":\"{id}\",\"media_file_id\":\"1\",\"segment\":\"intro\",\"action\":\"set\",\"created_at\":\"2026-01-01T00:00:00Z\"}}"));
        Assert.Throws<InvalidDataException>(() => AdminMarkerHistoryApi.Validate(JsonSerializer.Deserialize<AdminMarkerHistoryWire>($"{{\"history\":[{rows}]}}", SnakeCase), 25));
    }

    private static string Markers(int count) => "{\"history\":[" + string.Join(",", Enumerable.Range(1, count).Select(id =>
        $"{{\"id\":\"{id}\",\"media_file_id\":\"{id}\",\"segment\":\"intro\",\"action\":\"set\",\"after\":{{\"start_seconds\":0,\"end_seconds\":30}},\"created_at\":\"2026-11-01T10:00:00Z\"}}")) + "]}";

    [Fact]
    public async Task MarkerCountDefaultsToFiftyAndAChangeDiscardsTheOldRead()
    {
        using var h = new Harness();
        h.Server.HoldPrefixes.Add("/api/v2/admin/markers/history");
        Assert.True(h.Markers.Open());
        await Until(() => h.Server.HeldCount == 1);
        Assert.Contains("/api/v2/admin/markers/history?limit=50", h.Server.Requests);
        Assert.True(h.Markers.State.Loading);
        h.Markers.SetLimit(25);
        await Until(() => h.Server.Count("/api/v2/admin/markers/history") == 2);
        try { h.Server.Release("/api/v2/admin/markers/history?limit=50", Markers(50)); } catch (InvalidOperationException) { }
        h.Server.Release("/api/v2/admin/markers/history?limit=25", Markers(3));
        await Until(() => h.Markers.State.Rows != null);
        Assert.Equal(3, h.Markers.State.Rows!.Count);
        Assert.Equal(25, h.Markers.State.Limit);
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Markers.SetLimit(500));
        Assert.DoesNotContain(h.Server.Requests, path => !path.StartsWith("/api/v2/admin/markers/history", StringComparison.Ordinal)); // read-only, one endpoint
    }

    [Fact]
    public async Task MarkerRefreshJoinsTheReadInFlightAndNeverPolls()
    {
        using var h = new Harness();
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/markers/history") ? AdminMonitorKit.Json(Markers(2)) : null;
        h.Markers.Open();
        await Until(() => h.Markers.State.Rows != null);
        h.Server.HoldPrefixes.Add("/api/v2/admin/markers/history");
        var first = h.Markers.RefreshAsync();
        var second = h.Markers.RefreshAsync();
        Assert.Same(first, second);
        await Until(() => h.Server.HeldCount == 1);
        Assert.True(h.Markers.State.Fetching);
        Assert.Equal(2, h.Markers.State.Rows!.Count);
        h.Server.Release("/api/v2/admin/markers/history", Markers(4));
        await first;
        Assert.Equal(4, h.Markers.State.Rows!.Count);
        await Task.Delay(100);
        Assert.Equal(2, h.Server.Count("/api/v2/admin/markers/history"));
        Assert.Equal(2, h.Markers.ReadsStarted);
    }

    [Fact]
    public async Task MarkerErrorEmptyAndRetirementStates()
    {
        using var h = new Harness();
        var status = HttpStatusCode.InternalServerError;
        h.Server.Respond = path => path.StartsWith("/api/v2/admin/markers/history")
            ? status == HttpStatusCode.OK ? AdminMonitorKit.Json("{\"history\":[]}") : AdminMonitorKit.Json("{\"title\":\"x\"}", status)
            : null;
        h.Markers.Open();
        await Until(() => h.Markers.State.Failed);
        Assert.False(h.Markers.State.Loading);
        status = HttpStatusCode.OK;
        await h.Markers.RefreshAsync();
        Assert.False(h.Markers.State.Failed);
        Assert.Empty(h.Markers.State.Rows!);
        h.Kit.SetRole("user");
        Assert.False(h.Markers.State.IsOpen);
        Assert.Null(h.Markers.State.Rows);
        Assert.False(h.Markers.Reopen());
    }

    // ── Text and route registration ──────────────────────────

    [Fact]
    public void DomainTextResolvesEveryKeyAndRoutesHaveCatalogText()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        foreach (var key in AdminPlaybackAuditText.CatalogKeys)
            Assert.NotEqual(key, AdminPlaybackAuditText.T(key, [("id", "1"), .. AdminPlaybackAuditText.Count(2)]));
        foreach (var key in AdminPlaybackAuditText.OfficialKeys)
            Assert.DoesNotContain("web.", AdminPlaybackAuditText.T(key, ("id", "1"), ("name", "n"), ("title", "t"), ("type", "y"), ("count", 2), ("count_text", "2"),
                ("from", 1), ("to", 2), ("total", 3), ("duration", "1m"), ("time", "now"), ("media_type", "movie"), ("session", "s"), ("before", "a"),
                ("after", "b"), ("hours", 1), ("minutes", 2), ("seconds", 3), ("season", "01"), ("episode", "02")), StringComparison.Ordinal);
        Assert.Equal("1 row", AdminPlaybackAuditText.T("web.admin.history.pagination.pageSize", AdminPlaybackAuditText.Count(1)));
        Assert.Equal("100 rows", AdminPlaybackAuditText.T("web.admin.markers.history.limitOption", AdminPlaybackAuditText.Count(100)));
        Assert.Equal("Showing 26-50 of 100", AdminPlaybackAuditText.T("web.admin.history.pagination.range", ("from", 26), ("to", 50), ("total", 100)));
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.PlaybackHistory));
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.SkipMarkers));
        Assert.True(AdminRouteCatalog.IsValid(new AdminRouteRequest(AdminRoute.PlaybackHistory, new Dictionary<string, string> { ["user_id"] = "7" })));
        Assert.False(AdminRouteCatalog.IsValid(new AdminRouteRequest(AdminRoute.SkipMarkers) { Segments = ["1"] }));
        Assert.True(AdminRouteCatalog.TryResolve("/admin/history?media_item_id=item-1", out var resolved));
        Assert.Equal(AdminRoute.PlaybackHistory, resolved.Request!.Route);
        Assert.Equal("item-1", resolved.Request.Get("media_item_id"));
    }
}
