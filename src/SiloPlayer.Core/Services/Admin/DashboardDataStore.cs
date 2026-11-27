using System.Diagnostics;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A capability this server build does not support; rendered as unavailable, never as zero.</summary>
public sealed class DashboardUnsupportedException(string capability) : Exception("Not supported by this server: " + capability)
{
    public string Capability { get; } = capability;
}

/// <summary>A query's published state. Data survives a failed refetch; Error is set beside it.</summary>
public sealed class DashboardQueryState
{
    public object? Data { get; internal set; }
    public Exception? Error { get; internal set; }
    public bool IsFetching { get; internal set; }
    public DateTimeOffset? UpdatedAt { get; internal set; }
    public bool Invalidated { get; internal set; }
    /// <summary>The native Insights endpoint was found unsupported, independently of the fallback result.</summary>
    public bool InsightsUnsupported { get; internal set; }
    /// <summary>No data and no error yet: the skeleton state.</summary>
    public bool IsLoading => Data == null && Error == null;
    public bool IsSuccess => Data != null;
    public T? As<T>() where T : class => Data as T;
}

public enum DashboardNoticeKind { Success, Error }

/// <summary>
/// All dashboard data for one captured authority lease. Widgets mount the
/// queries they render; mounted queries can poll and can fetch their dependencies on demand. Sampled series are
/// fresh for 55s. Insights reads (the six Insights widgets and both leaderboards)
/// are fresh for, and re-read at, their poll interval (5 minutes for a week or a
/// month, 15 for longer) while visible, share one read per window and are never
/// touched by the page's 60s loop: only a manual Refresh marks them stale and
/// asks the server for fresh aggregates (<c>refresh=true</c>). The page
/// refreshes every 60s only while visible and focused; resources poll every 15s under the same gate,
/// nodes every 30s and autoscan every 15s while visible. Session events
/// coalesce into one REST refresh per 5s window. Every publication re-checks
/// the lease. A departure can retain successful reads for the same authority.
/// </summary>
public sealed class DashboardDataStore : IDisposable
{
    public static readonly TimeSpan PageRefreshInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ManualSpinnerMinimum = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RealtimeWindow = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EgressSampleMaxAge = TimeSpan.FromMinutes(2);
    private static readonly Uri RelativeArtworkBase = new("https://dashboard.invalid");
    private const string LegacyTopActivityPrefix = "top:";

    public const string Capabilities = "capabilities";
    public const string Stats = "stats";
    public const string Sessions = "sessions";
    public const string Libraries = "libraries";
    public const string Users = "users";
    public const string Status = "status";
    public const string Build = "build";
    public const string ResourceCapabilities = "resource-capabilities";
    public const string Resources = "resources";
    public const string Nodes = "nodes";
    public const string Autoscan = "autoscan";
    public const string ScanHistory = "scan-history";
    public const string RecentErrors = "recent-errors";
    public const string Downloads = "downloads";
    public const string AttentionSettings = "attention-settings";
    public const string AttentionSensitive = "attention-sensitive";
    public const string AttentionBranding = "attention-branding";
    public const string AttentionSearch = "attention-search";
    public static string Timeseries(int hours) => "timeseries:" + hours;
    public static string Playback(int hours) => "playback:" + hours;
    public static string ApiClients(int hours) => "api-clients:" + hours;
    public static string ApiClientEndpoints(string sessionId, int hours) => $"api-clients:{hours}:endpoints:{sessionId}";

    /// <summary>Every Insights read starts with this; the page's manual Refresh invalidates it, its 60s loop never does.</summary>
    public const string InsightsPrefix = "insights:";

    /// <summary>
    /// The Activity read of a window, shared by all five Insights chart widgets on it (the key is the mapped Insights range, as the
    /// official cache key is the Insights page's unfiltered query, never the widget's own range name).
    /// </summary>
    public static string InsightsActivity(DashboardWidgetRange range) => InsightsActivity(AdminInsightsRules.WidgetRangeToInsightsRange(range));
    public static string InsightsActivity(AdminInsightsRange range) => InsightsPrefix + "activity:" + range.Wire();
    /// <summary>Top titles by plays, <paramref name="limit"/> rows per kind (the dashboard asks for 8).</summary>
    public static string InsightsTitles(DashboardWidgetRange range, int limit = AdminInsightsLimits.DashboardTitles)
        => InsightsPrefix + "titles:" + AdminInsightsRules.WidgetRangeToInsightsRange(range).Wire() + ":" + limit;
    public static string InsightsLibrary(DashboardWidgetRange range) => InsightsPrefix + "library:" + AdminInsightsRules.WidgetRangeToInsightsRange(range).Wire();
    /// <summary>People with one account row: Top profiles reads only <c>top_profiles</c>.</summary>
    public static string InsightsPeople(DashboardWidgetRange range, int limit = 1)
        => InsightsPrefix + "people:" + AdminInsightsRules.WidgetRangeToInsightsRange(range).Wire() + ":" + limit;

    /// <summary>Queries the 60s/manual refresh marks stale so mounted widgets refetch. Insights reads are deliberately absent.</summary>
    private static readonly string[] WidgetPrefixes =
        ["timeseries:", "playback:", Downloads, "api-clients:", Status, Resources, ResourceCapabilities, Nodes, Autoscan, ScanHistory, RecentErrors];

    private readonly AdminMonitorApi _api;
    private readonly AdminInsightsApi? _insights;
    private readonly AdminInsightsRefreshGate? _refreshGate;
    private readonly string _timeZone;
    private readonly MonitorAuthorityLease _lease;
    private readonly IDashboardActivityFeed? _feed;
    private readonly TimeProvider _time;
    private readonly DashboardQueryCache? _cache;
    private readonly long _cacheVisit;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _realtimePending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DashboardActiveScan> _activeScans = new(StringComparer.Ordinal);
    private readonly List<string> _activeScanOrder = [];
    private IDisposable? _feedReads;
    private IReadOnlyList<DashboardSession>? _observedSessions;
    private Task? _initialSessionsRead;
    private ITimer? _tick;
    private ITimer? _realtimeTimer;
    private bool _realtimeRefreshing;
    private bool _refreshing;
    private readonly HashSet<string> _startingLibraries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cancellingLibraries = new(StringComparer.Ordinal);
    private bool _visible = true;
    private bool _focused = true;
    private bool _pollPaused;
    private bool _disposed;
    private object? _prunedSettings;
    private string? _attentionPendingValue;

    private sealed class Entry
    {
        public readonly DashboardQueryState State = new();
        public int Mounts;
        public long Generation;
        public Task? InFlight;
        public bool RefreshesStats;
        public DateTimeOffset? LastStarted;
        public DateTimeOffset? UnusedSince;
        public long? InitialElapsedMs;
        public (LegacyTopActivitySnapshot Snapshot, DateTimeOffset UpdatedAt)? PendingLegacy;
    }

    private sealed record LegacyTopActivitySnapshot(DashboardTopActivity Rows, bool InvalidTitles, bool InvalidProfiles)
    {
        public Exception? ErrorFor(string key) =>
            (key.StartsWith("insights:titles:", StringComparison.Ordinal) ? InvalidTitles : InvalidProfiles)
                ? new InvalidDataException("The server sent unusable leaderboard rows.") : null;
    }

    /// <param name="insights">The Insights reads behind the Insights widgets and both leaderboards; without it they read as unsupported.</param>
    /// <param name="refreshGate">The shared Refresh window: reads started inside it send <c>refresh=true</c>.</param>
    /// <param name="timeZone">The IANA zone Insights buckets by; the machine's zone when omitted (the browser's, in the WebUI).</param>
    public DashboardDataStore(AdminMonitorApi api, MonitorAuthorityLease lease, IDashboardActivityFeed? feed = null,
        TimeProvider? time = null, bool retainAcrossVisits = false, AdminInsightsApi? insights = null,
        AdminInsightsRefreshGate? refreshGate = null, string? timeZone = null)
    {
        _api = api;
        _insights = insights;
        _refreshGate = refreshGate;
        _timeZone = string.IsNullOrWhiteSpace(timeZone) ? AdminInsightsRules.DetectTimezone() : timeZone;
        _lease = lease;
        _feed = feed;
        _time = time ?? TimeProvider.System;
        if (retainAcrossVisits)
        {
            _cache = DashboardQueryCache.For(lease, _time);
            var (visit, queries) = _cache.Restore(lease);
            _cacheVisit = visit;
            foreach (var (key, query) in queries)
            {
                var entry = EntryFor(key);
                entry.State.Data = query.Data;
                entry.State.UpdatedAt = query.UpdatedAt;
                entry.State.Invalidated = query.Invalidated;
                entry.State.InsightsUnsupported = query.InsightsUnsupported;
                entry.LastStarted = IsInsights(key) ? query.LastStarted : query.UpdatedAt;
                entry.UnusedSince = query.ExpiresAt - DashboardQueryCache.UnusedLifetime;
            }
            IsWarmVisit = queries.Any(pair => pair.Key != Capabilities && pair.Value.Data != null);
        }
    }

    /// <summary>Raised with the changed query key, or "page"/"scans"/"actions"/"attention".</summary>
    public event Action<string>? Changed;
    public event Action<DashboardNoticeKind, string>? Notice;
    /// <summary>Signature renewal can retry a failed poster without rebuilding its widget.</summary>
    public event Action<IReadOnlyList<DashboardSession>>? SessionPosterUrlsChanged;

    public MonitorAuthorityLease Lease => _lease;
    public DateTimeOffset Now => _time.GetUtcNow();
    public bool IsWarmVisit { get; }
    public bool IsCurrent => !_disposed && _lease.IsCurrent;
    public bool CanPollDashboard => _visible && _focused;
    public bool CanApplyRealtimeUpdates => _visible;
    public bool ManualRefreshPending { get; private set; }
    public DateTimeOffset? LastUpdatedAt { get; private set; }
    public bool ScansHydrated { get; private set; }
    public IReadOnlyList<DashboardActiveScan> ActiveScans => _activeScanOrder.Select(id => _activeScans[id]).ToArray();
    public string? ScanStartingLibraryId => _startingLibraries.FirstOrDefault();
    public string? ScanCancellingLibraryId => _cancellingLibraries.FirstOrDefault();
    public bool IsStartingLibraryScan(string id) => _startingLibraries.Contains(id);
    public bool IsCancellingLibraryScans(string id) => _cancellingLibraries.Contains(id);
    public bool ScanAllPending { get; private set; }
    public bool AttentionSaving { get; private set; }

    /// <summary>Starts realtime, polling and the four page-level queries.</summary>
    public void Start()
    {
        if (!IsCurrent || _tick != null) return;
        _tick = _time.CreateTimer(_ => Post(OnTick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        if (_feed != null)
        {
            _feed.Changed += OnFeedChanged;
            _feedReads = _feed.AcquireReads();
            SyncFeed(initial: true);
            _initialSessionsRead = ObserveSessionsAsync(initialRead: true);
        }
        var capabilities = EntryFor(Capabilities);
        capabilities.UnusedSince = null; // Always observed during a visit, even with no gated widgets.
        if (IsStale(Capabilities, capabilities)) _ = FetchAsync(Capabilities);
    }

    private async Task ObserveSessionsAsync(bool initialRead)
    {
        var started = Stopwatch.GetTimestamp();
        await _feed!.EnsureSessionsAsync();
        if (!IsCurrent) return;
        if (initialRead) EntryFor(Sessions).InitialElapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        SyncFeed(initial: false);
    }

    /// <summary>Measures the initial mounted query set. Endpoint details can contain a login session ID.</summary>
    public async Task<string?> InitialLoadTimingAsync(long pageStarted)
    {
        var initial = _entries.Where(pair => pair.Value.Mounts > 0 || pair.Key == Capabilities)
            .Where(pair => !pair.Key.Contains(":endpoints:", StringComparison.Ordinal)).ToArray();
        await Task.WhenAll(initial.Select(pair => pair.Value.InFlight ?? Task.CompletedTask)
            .Append(_initialSessionsRead ?? Task.CompletedTask));
        if (!IsCurrent) return null;
        var slowest = initial.OrderByDescending(pair => pair.Value.InitialElapsedMs ?? 0)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(3)
            .Select(pair => FormattableString.Invariant($"{pair.Key}={pair.Value.InitialElapsedMs ?? 0}ms"));
        return FormattableString.Invariant(
            $"dashboard ready total_ms={(long)Stopwatch.GetElapsedTime(pageStarted).TotalMilliseconds} warm={IsWarmVisit} queries={string.Join(',', slowest)}");
    }

    public DashboardQueryState Get(string key) => IsCurrent ? EntryFor(key).State : new();

    /// <summary>Marks a query in use; it fetches now if empty or stale and polls while mounted.</summary>
    public IDisposable Mount(string key)
    {
        var entry = EntryFor(key);
        entry.Mounts++;
        entry.UnusedSince = null;
        // A different range may have refreshed these rows while this card was unmounted.
        if (IsCurrent && !entry.State.IsFetching && !entry.State.Invalidated &&
            LegacyDependency(key, entry) is { } legacyKey && _entries.TryGetValue(legacyKey, out var shared) &&
            shared.State.As<LegacyTopActivitySnapshot>() is { } snapshot && shared.State.UpdatedAt is { } updated &&
            (entry.State.UpdatedAt is not { } previous || updated > previous))
            PublishLegacyLeaderboard(key, entry, snapshot, updated);
        // Sessions come from the shared activity store, which reads on demand itself.
        if (IsStale(key, entry) && !(key == Sessions && _feed != null)) _ = FetchAsync(key);
        return new Mounting(this, key);
    }

    private sealed class Mounting(DashboardDataStore store, string key) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            if (store._entries.TryGetValue(key, out var entry) && entry.Mounts > 0 && --entry.Mounts == 0)
                entry.UnusedSince = store.Now;
        }
    }

    /// <summary>Visibility and focus of the hosting window. Resuming refreshes aged data.</summary>
    public void SetActivity(bool visible, bool focused)
    {
        var couldPoll = CanPollDashboard;
        _visible = visible;
        _focused = focused;
        if (!CanPollDashboard)
        {
            _pollPaused = true;
            return;
        }
        if (!couldPoll && IsCurrent)
        {
            foreach (var (key, entry) in _entries.ToArray())
            {
                if (entry.Mounts == 0 || !IsStale(key, entry)) continue;
                if (key == Sessions && _feed != null) _ = ObserveSessionsAsync(initialRead: false);
                else _ = FetchAsync(key);
            }
            EvaluatePageLoop();
        }
    }

    public bool HasDashboardData => new[] { Stats, Sessions, Libraries, Users }.All(key => Get(key).Data != null);

    private bool HasStaleDashboardData => new[] { Stats, Sessions, Libraries, Users }.Any(key => IsStale(key, EntryFor(key)));

    /// <summary>Marks a query stale; a mounted query refetches now.</summary>
    public void Invalidate(string keyOrPrefix, bool refetch = true)
    {
        foreach (var (key, entry) in _entries.ToArray())
        {
            if (key != keyOrPrefix && !(keyOrPrefix.EndsWith(':') && key.StartsWith(keyOrPrefix, StringComparison.Ordinal))) continue;
            entry.State.Invalidated = true;
            if (refetch && entry.Mounts > 0) _ = FetchAsync(key, force: true);
        }
    }

    /// <summary>
    /// Page refresh (manual or the 60s loop): marks widget queries stale so mounted
    /// ones refetch, reads stats with refresh=true, then sessions, libraries and users.
    /// A manual refresh keeps its spinner at least one second. Only the Refresh button passes
    /// <paramref name="freshInsights"/> (official <c>freshInsights</c>): it opens the shared Refresh window and marks every Insights read
    /// stale, so mounted ones re-read with <c>refresh=true</c>. The 60s loop and the page-return catch-up leave Insights to its own pacing.
    /// </summary>
    public async Task RefreshDashboardAsync(bool manual, bool freshInsights = false)
    {
        if (!IsCurrent || _refreshing) return;
        _refreshing = true;
        var started = Now;
        if (manual)
        {
            ManualRefreshPending = true;
            Raise("page");
        }
        try
        {
            foreach (var key in new[] { Stats, Sessions, Libraries, Users }) Invalidate(key, refetch: false);
            foreach (var prefix in WidgetPrefixes) Invalidate(prefix);
            if (freshInsights)
            {
                _refreshGate?.Mark();
                Invalidate(LegacyTopActivityPrefix, refetch: false);
                Invalidate(InsightsPrefix);
            }
            await FetchAsync(Stats, force: true, refreshStats: true);
            if (!IsCurrent) return;
            await Task.WhenAll(FetchAsync(Sessions, force: true), FetchAsync(Libraries, force: true), FetchAsync(Users, force: true));
            if (!IsCurrent) return;
            LastUpdatedAt = Now;
            Raise("page");
        }
        finally
        {
            if (manual)
            {
                var remaining = ManualSpinnerMinimum - (Now - started);
                if (remaining > TimeSpan.Zero)
                {
                    try { await Task.Delay(remaining, _time, _lease.Token); } catch (OperationCanceledException) { }
                }
                ManualRefreshPending = false;
                Raise("page");
            }
            _refreshing = false;
        }
    }

    private void OnTick()
    {
        if (!IsCurrent) return;
        var now = Now;
        foreach (var (key, entry) in _entries.ToArray())
        {
            if (entry.Mounts == 0 || entry.InFlight != null) continue;
            var interval = PollInterval(key);
            if (interval is not { } period) continue;
            if (entry.LastStarted is { } last && now - last < period) continue;
            _ = FetchAsync(key, force: true);
        }
        EvaluatePageLoop();
    }

    private void EvaluatePageLoop()
    {
        if (!CanPollDashboard || _refreshing || !IsCurrent) return;
        if (LastUpdatedAt == null && HasDashboardData)
        {
            LastUpdatedAt = new[] { Stats, Sessions, Libraries, Users }.Max(key => Get(key).UpdatedAt);
            Raise("page");
        }
        var resumed = _pollPaused;
        _pollPaused = false;
        if (resumed && HasDashboardData && (HasStaleDashboardData || LastUpdatedAt == null || Now - LastUpdatedAt >= PageRefreshInterval))
        {
            _ = RefreshDashboardAsync(manual: true);
            return;
        }
        if (LastUpdatedAt is { } updated && Now - updated >= PageRefreshInterval) _ = RefreshDashboardAsync(manual: false);
    }

    private TimeSpan? PollInterval(string key) => key switch
    {
        Resources when CanPollDashboard => TimeSpan.FromSeconds(15),
        Nodes when CanApplyRealtimeUpdates => TimeSpan.FromSeconds(30),
        Autoscan or ScanHistory when CanApplyRealtimeUpdates => TimeSpan.FromSeconds(15),
        // Paused while the window is hidden, as the interval of an official hidden tab is.
        _ when IsInsights(key) && CanApplyRealtimeUpdates => InsightsInterval(key),
        _ => null,
    };

    private static bool IsInsights(string key) => key.StartsWith(InsightsPrefix, StringComparison.Ordinal);

    /// <summary>A week or month re-reads every 5 minutes, a quarter, year or all time every 15 (<c>dashboardInsightsInterval</c>).</summary>
    private static TimeSpan InsightsInterval(string key)
    {
        var parts = key.Split(':');
        return AdminInsightsRanges.TryParse(parts.Length > 2 ? parts[2] : null, out var range)
            ? AdminInsightsIntervals.DashboardPoll(range)
            : AdminInsightsIntervals.DashboardLongRangePoll;
    }

    private TimeSpan StaleTime(string key) => key switch
    {
        Build or Capabilities => TimeSpan.MaxValue,
        Stats or Sessions or Libraries or Users or Nodes or AttentionSettings or AttentionSensitive => TimeSpan.FromSeconds(30),
        Status or Resources or AttentionSearch or Autoscan or ScanHistory => TimeSpan.FromSeconds(15),
        ResourceCapabilities => TimeSpan.FromSeconds(60),
        RecentErrors => TimeSpan.FromSeconds(5),
        AttentionBranding => TimeSpan.FromMinutes(5),
        // Fresh for exactly the interval it is re-read at, so a widget mounting mid-cycle serves from the shared read.
        _ when IsInsights(key) => InsightsInterval(key),
        _ => TimeSpan.FromSeconds(55),
    };

    private bool IsStale(string key, Entry entry)
    {
        if (entry.State.Data == null || entry.State.Invalidated || entry.State.UpdatedAt is not { } updated) return true;
        var stale = StaleTime(key);
        // A sibling can freshen the displayed rows without probing this card's native endpoint.
        if (entry.State.InsightsUnsupported)
            return entry.LastStarted is not { } probe || Now - probe >= stale;
        return stale != TimeSpan.MaxValue && Now - updated >= stale;
    }

    private Entry EntryFor(string key)
    {
        if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry { UnusedSince = Now };
        return entry;
    }

    /// <summary>Fetches one query under the lease. Older generations never publish over newer ones.</summary>
    public Task FetchAsync(string key, bool force = false, bool refreshStats = false)
    {
        if (!IsCurrent) return Task.CompletedTask;
        var entry = EntryFor(key);
        // An explicit refresh must reach refresh=true even when an ordinary
        // read is outstanding. The generation below prevents the older result
        // from replacing it. Duplicate explicit refreshes still join. An Insights
        // read refreshes while the shared Refresh window is open, whoever started it.
        var refresh = (key == Stats && refreshStats) || (IsInsights(key) && _refreshGate?.ShouldRefresh == true);
        if (entry.InFlight != null && !(refresh && !entry.RefreshesStats)) return entry.InFlight;
        var generation = ++entry.Generation;
        entry.RefreshesStats = refresh;
        entry.LastStarted = Now;
        entry.PendingLegacy = null;
        entry.State.IsFetching = true;
        var task = RunFetchAsync(key, entry, generation, refresh);
        // A read that completed synchronously already cleared itself; never park a finished task.
        if (entry.Generation == generation) entry.InFlight = task.IsCompleted ? null : task;
        return task;
    }

    private async Task RunFetchAsync(string key, Entry entry, long generation, bool refreshStats)
    {
        var started = Stopwatch.GetTimestamp();
        object? data = null;
        Exception? error = null;
        bool? insightsUnsupported = null;
        (LegacyTopActivitySnapshot Snapshot, DateTimeOffset UpdatedAt)? legacy = null;
        try
        {
            var gate = CapabilityFor(key);
            if (gate != null)
            {
                // Older servers can omit the capability route or explicitly disable optional
                // endpoints. Preserve that distinction and only defer the cold, unknown case.
                var capabilities = EntryFor(Capabilities);
                if (capabilities.State.Data == null && capabilities.State.Error == null)
                    await (capabilities.InFlight ?? FetchAsync(Capabilities));
                _lease.ThrowIfRetired();
                if (capabilities.State.Data is DashboardCapabilities caps && !Supports(caps, gate))
                    throw new DashboardUnsupportedException(gate);
            }
            try
            {
                data = await LoadAsync(key, refreshStats);
                if (IsInsights(key)) insightsUnsupported = false;
            }
            catch (AdminInsightsUnsupportedException)
            {
                insightsUnsupported = true;
                var legacyKey = LegacyTopActivityKeyFor(key) ?? throw new DashboardUnsupportedException("insights");
                legacy = await LoadLegacyLeaderboardAsync(legacyKey);
            }
        }
        catch (Exception) when (!IsCurrent)
        {
            return;
        }
        catch (OperationCanceledException) when (_lease.Token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            if (generation == 1) entry.InitialElapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (entry.Generation == generation) entry.InFlight = null;
        }
        if (!IsCurrent || entry.Generation != generation) return;
        entry.State.IsFetching = false;
        var pendingLegacy = entry.PendingLegacy;
        entry.PendingLegacy = null;
        if (legacy is { } fallback)
            PublishLegacyLeaderboard(key, entry, fallback.Snapshot, fallback.UpdatedAt);
        else if (pendingLegacy is { } missed && insightsUnsupported == null &&
            error is (ApiException { StatusCode: 408 or 429 or >= 500 } or HttpRequestException or TimeoutException
                or TaskCanceledException { InnerException: TimeoutException }))
            PublishLegacyLeaderboard(key, entry, missed.Snapshot, missed.UpdatedAt);
        else
        {
            if (IsInsights(key) && error is DashboardUnsupportedException { Capability: "insights" }) insightsUnsupported = true;
            PublishQueryResult(key, entry, data, error, insightsUnsupported: insightsUnsupported);
        }
        if (error == null && data is LegacyTopActivitySnapshot top && entry.State.UpdatedAt is { } updated)
        {
            // Defer updates during native probes so success stays authoritative, but transient failures can use these rows.
            foreach (var (dependentKey, dependent) in _entries.ToArray())
            {
                if (!IsCurrent) break;
                if (LegacyDependency(dependentKey, dependent) != key) continue;
                if (dependent.State.IsFetching)
                    dependent.PendingLegacy = (top, updated);
                else if (dependent.Mounts > 0)
                    PublishLegacyLeaderboard(dependentKey, dependent, top, updated);
            }
        }
    }

    private void PublishLegacyLeaderboard(string key, Entry entry, LegacyTopActivitySnapshot snapshot, DateTimeOffset updatedAt) =>
        PublishQueryResult(key, entry, snapshot.Rows, snapshot.ErrorFor(key), updatedAt, insightsUnsupported: true);

    private void PublishQueryResult(string key, Entry entry, object? data, Exception? error,
        DateTimeOffset? updatedAt = null, bool? insightsUnsupported = null)
    {
        var changed = !SameError(entry.State.Error, error);
        if (insightsUnsupported is { } unsupported && entry.State.InsightsUnsupported != unsupported)
        {
            changed = true;
            entry.State.InsightsUnsupported = unsupported;
        }
        if (error == null)
        {
            if (key == Sessions)
            {
                var before = entry.State.As<IReadOnlyList<DashboardSession>>();
                var sessions = (IReadOnlyList<DashboardSession>)data!;
                changed |= !SameSessions(before, sessions);
                entry.State.Data = sessions;
                if (!changed) PublishRenewedPosters(before, sessions);
            }
            else if (!SameData(entry.State.Data, data))
            {
                changed = true;
                entry.State.Data = data;
            }
            entry.State.UpdatedAt = updatedAt ?? Now;
            entry.State.Invalidated = false;
        }
        entry.State.Error = error;
        if (changed) Raise(key);
        // Pruning waits for both reads to settle, even if a refetch returns the same data.
        if (key is AttentionSettings or Status) Raise("attention");
    }

    private static bool SameError(Exception? before, Exception? after)
        => before?.GetType() == after?.GetType() && before?.Message == after?.Message;

    private static bool SameData(object? before, object? after)
        => ReferenceEquals(before, after) || (before != null && after != null &&
            JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before), JsonSerializer.SerializeToElement(after)));

    private static string? CapabilityFor(string key) => key switch
    {
        _ when key.StartsWith("timeseries:", StringComparison.Ordinal) => "timeseries",
        _ when key.StartsWith("playback:", StringComparison.Ordinal) => "playback_activity",
        _ when key.StartsWith("api-clients:", StringComparison.Ordinal) => "api_clients",
        Downloads => "downloads_stats",
        RecentErrors => "log_level_list",
        _ => null,
    };

    private static bool Supports(DashboardCapabilities caps, string capability) => capability switch
    {
        "timeseries" => caps.Timeseries,
        "playback_activity" => caps.PlaybackActivity,
        "api_clients" => caps.ApiClients,
        "downloads_stats" => caps.DownloadsStats,
        "log_level_list" => caps.LogLevelList,
        _ => true,
    };

    private async Task<object> LoadAsync(string key, bool refreshStats)
    {
        var lease = _lease;
        var ct = lease.Token;
        switch (key)
        {
            case Capabilities: return await _api.GetDashboardCapabilitiesAsync(lease, ct);
            case Stats: return await _api.GetDashboardStatsAsync(lease, refreshStats, ct);
            case Sessions:
            {
                if (_feed == null) throw new InvalidOperationException("No shared activity feed.");
                await _feed.RefreshSessionsAsync();
                lease.ThrowIfRetired();
                var current = _feed.Current;
                if (current.SessionsError != null || !current.SessionsLoaded) throw new InvalidOperationException(current.SessionsError ?? "Sessions unavailable.");
                return current.Sessions;
            }
            case Libraries:
                return (await _api.ListDashboardLibrariesAsync(lease, ct)).OrderBy(library => library.SortOrder).ToArray();
            case Users: return await _api.ListDashboardUsersAsync(lease, ct);
            case Status: return await _api.GetDashboardServerStatusAsync(lease, ct);
            case Build: return await _api.GetDashboardBuildInfoAsync(lease, ct);
            case ResourceCapabilities: return await _api.GetDashboardResourceCapabilitiesAsync(lease, ct);
            case Resources: return await _api.GetDashboardResourcesAsync(lease, ct);
            case Nodes: return await _api.ListDashboardNodesAsync(lease, ct);
            case Autoscan: return await _api.GetDashboardAutoscanStatusAsync(lease, ct);
            case ScanHistory: return await _api.ListDashboardScanHistoryAsync(lease, 8, ct);
            case RecentErrors: return await _api.ListDashboardRecentErrorsAsync(lease, 8, ct);
            case Downloads: return await _api.GetDashboardDownloadsStatsAsync(lease, 10, ct);
            case AttentionSettings: return await _api.GetDashboardAttentionSettingsAsync(lease, ct);
            case AttentionSensitive: return await _api.GetDashboardSensitiveStatusAsync(lease, ct);
            case AttentionBranding: return await _api.GetDashboardBrandingAsync(lease, ct);
            case AttentionSearch: return await _api.GetDashboardSearchStatusAsync(lease, ct);
        }
        var parts = key.Split(':');
        return parts[0] switch
        {
            "timeseries" => await _api.GetDashboardTimeseriesAsync(lease, int.Parse(parts[1]), ct),
            "playback" => await _api.GetDashboardPlaybackActivityAsync(lease, int.Parse(parts[1]), ct),
            "top" => await ReadLegacyTopActivityAsync(int.Parse(parts[1]), ct),
            "insights" => await LoadInsightsAsync(parts, refreshStats, ct),
            "api-clients" when parts.Length == 2 => await _api.GetDashboardApiClientsAsync(lease, int.Parse(parts[1]), 10, ct),
            "api-clients" when parts.Length >= 4 && parts[2] == "endpoints" =>
                await _api.GetDashboardApiClientEndpointsAsync(lease, string.Join(':', parts[3..]), int.Parse(parts[1]), ct),
            _ => throw new ArgumentException("Unknown dashboard query " + key, nameof(key)),
        };
    }

    /// <summary>
    /// One Insights read of the dashboard's server-wide window (<c>insights:&lt;kind&gt;:&lt;range&gt;[:&lt;limit&gt;]</c>).
    /// Unsupported leaderboards can fall back to legacy stats; the Insights charts stay unavailable.
    /// </summary>
    private async Task<object> LoadInsightsAsync(string[] parts, bool refresh, CancellationToken ct)
    {
        if (_insights == null) throw new DashboardUnsupportedException("insights");
        if (parts.Length < 3 || !AdminInsightsRanges.TryParse(parts[2], out var range) || range == AdminInsightsRange.Custom)
            throw new ArgumentException("Unknown dashboard query " + string.Join(':', parts), nameof(parts));
        var window = new AdminInsightsWindow { Range = range, Tz = _timeZone };
        var limit = parts.Length > 3 && int.TryParse(parts[3], out var parsed) ? parsed : 1;
        return parts[1] switch
        {
            "activity" => await _insights.GetActivityAsync(_lease, window, refresh, ct),
            "titles" => await _insights.GetTitlesAsync(_lease, window, AdminInsightsTitleSort.Plays, limit, refresh, ct),
            "library" => await _insights.GetLibraryAsync(_lease, window, refresh, ct),
            "people" => await _insights.GetPeopleAsync(_lease, window, new AdminInsightsPeopleQuery(Limit: limit), refresh, ct),
            _ => throw new ArgumentException("Unknown dashboard query " + string.Join(':', parts), nameof(parts)),
        };
    }

    private static string? LegacyTopActivityKeyFor(string key)
    {
        var parts = key.Split(':');
        return parts.Length >= 3 && parts[0] == "insights" && parts[1] is ("titles" or "people") &&
            AdminInsightsRanges.TryParse(parts[2], out var range) && range != AdminInsightsRange.Custom
            ? LegacyTopActivityPrefix + (range == AdminInsightsRange.Days7 ? 7 : AdminMonitorApi.DashboardMaxTopDays)
            : null;
    }

    private string? LegacyDependency(string key, Entry entry) =>
        _insights != null && entry.State.InsightsUnsupported && entry.State.Data is (null or DashboardTopActivity)
            ? LegacyTopActivityKeyFor(key) : null;

    private async Task<(LegacyTopActivitySnapshot Snapshot, DateTimeOffset UpdatedAt)> LoadLegacyLeaderboardAsync(string key)
    {
        if (!IsCurrent) throw new OperationCanceledException(_lease.Token);
        // The old endpoint returns both leaderboards; read it on demand, not from the page's 60s loop.
        var entry = EntryFor(key);
        entry.UnusedSince = Now;
        if (entry.InFlight != null || IsStale(key, entry)) await FetchAsync(key);
        if (!IsCurrent) throw new OperationCanceledException(_lease.Token);
        if (entry.State.Error is { } error) throw error;
        if (entry.State.As<LegacyTopActivitySnapshot>() is { } snapshot && entry.State.UpdatedAt is { } updated)
            return (snapshot, updated);
        throw new DashboardUnsupportedException("insights");
    }

    private async Task<LegacyTopActivitySnapshot> ReadLegacyTopActivityAsync(int days, CancellationToken ct)
    {
        try
        {
            var (top, invalidTitles, invalidProfiles) = await _api.ReadDashboardTopActivityAsync(_lease, days, ct);
            invalidTitles |= top.Titles?.Any(title => title == null ||
                string.IsNullOrWhiteSpace(title.MediaItemId) && string.IsNullOrWhiteSpace(title.Title)) == true;
            invalidProfiles |= top.Profiles?.Any(profile => profile == null || string.IsNullOrWhiteSpace(profile.UserId)) == true;
            if (invalidTitles) top.Titles = null;
            if (invalidProfiles) top.Profiles = null;
            return new(top, invalidTitles, invalidProfiles);
        }
        catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501 || ex.ProblemType == "capability_unsupported")
        {
            throw new DashboardUnsupportedException("insights");
        }
    }

    // ---- Realtime ------------------------------------------------------

    private void OnFeedChanged() => Post(() => SyncFeed(initial: false));

    private static bool SameSessions(IReadOnlyList<DashboardSession>? before, IReadOnlyList<DashboardSession> after)
        => ReferenceEquals(before, after) || (before != null && before.Count == after.Count &&
            before.Zip(after).All(pair => pair.First == pair.Second ||
                (pair.First with { PosterUrl = null }) == (pair.Second with { PosterUrl = null }) &&
                StablePoster(pair.First.PosterUrl) == StablePoster(pair.Second.PosterUrl)));

    private static string? StablePoster(string? url)
        => Uri.TryCreate(RelativeArtworkBase, url, out var resolved)
            ? ImageService.NormalizeImageUrlForCache(resolved.AbsoluteUri)
            : url;

    private static (string, bool, string?, string) StatsSession(DashboardSession session)
        => (session.SessionId, session.IsPaused, session.EffectivePlayMethod, session.ProfileId);

    private void PublishRenewedPosters(IReadOnlyList<DashboardSession>? before, IReadOnlyList<DashboardSession> after)
    {
        if (before == null || ReferenceEquals(before, after) || SessionPosterUrlsChanged is not { } changed || !IsCurrent) return;
        var renewed = before.Zip(after).Where(pair => pair.First.PosterUrl != pair.Second.PosterUrl)
            .Select(pair => pair.Second).ToArray();
        if (renewed.Length > 0) changed(renewed);
    }

    /// <summary>
    /// Publishes the shared sessions and active scans under this lease. A sessions
    /// change also refreshes stats (coalesced, as the WebUI does); a scan leaving
    /// the active set refreshes libraries.
    /// </summary>
    private void SyncFeed(bool initial)
    {
        if (!IsCurrent || _feed == null) return;
        var current = _feed.Current;
        var entry = EntryFor(Sessions);
        if (current.SessionsLoaded && current.SessionsError == null && !ReferenceEquals(_observedSessions, current.Sessions))
        {
            var before = entry.State.As<IReadOnlyList<DashboardSession>>();
            var changed = entry.State.Error != null || !SameSessions(before, current.Sessions);
            var statsChanged = !initial && before != null &&
                !before.Select(StatsSession).SequenceEqual(current.Sessions.Select(StatsSession));
            _observedSessions = current.Sessions;
            // Keep a renewed signed URL available for the next bind even when the art is unchanged.
            entry.State.Data = current.Sessions;
            entry.State.Error = null;
            if (!initial || entry.State.UpdatedAt == null)
                entry.State.UpdatedAt = Now;
            entry.State.Invalidated = false;
            if (statsChanged) ScheduleRealtime(Stats);
            if (changed) Raise(Sessions);
            else PublishRenewedPosters(before, current.Sessions);
        }
        else if (current.SessionsError != null && entry.State.Error?.Message != current.SessionsError)
        {
            _observedSessions = null;
            entry.State.Error = new InvalidOperationException(current.SessionsError);
            Raise(Sessions);
        }
        if (current.ScansLoaded)
        {
            var scans = current.Scans.Where(scan => scan.IsActive).DistinctBy(scan => scan.Id).ToArray();
            if (ScansHydrated && ActiveScans.SequenceEqual(scans)) return;
            var before = _activeScanOrder.ToArray();
            _activeScans.Clear();
            _activeScanOrder.Clear();
            foreach (var scan in scans)
            {
                _activeScans.Add(scan.Id, scan);
                _activeScanOrder.Add(scan.Id);
            }
            var finished = before.Any(id => !_activeScans.ContainsKey(id));
            ScansHydrated = true;
            Raise("scans");
            if (finished && !initial) Invalidate(Libraries);
        }
    }

    /// <summary>Coalesces event-driven REST refreshes: one read now, then at most one per 5s window.</summary>
    private void ScheduleRealtime(params string[] keys)
    {
        foreach (var key in keys) _realtimePending.Add(key);
        if (!_realtimeRefreshing && _realtimeTimer == null) _ = FlushRealtimeAsync();
    }

    private async Task FlushRealtimeAsync()
    {
        if (!IsCurrent || _realtimePending.Count == 0) return;
        var keys = _realtimePending.ToArray();
        _realtimePending.Clear();
        _realtimeRefreshing = true;
        var reads = new List<Task>();
        foreach (var key in keys)
        {
            var entry = EntryFor(key);
            if (!CanApplyRealtimeUpdates) { entry.State.Invalidated = true; continue; }
            if (entry.InFlight != null) _realtimePending.Add(key);
            entry.State.Invalidated = true;
            if (entry.Mounts > 0 || key is Sessions or Stats) reads.Add(FetchAsync(key, force: entry.InFlight == null));
        }
        try { await Task.WhenAll(reads); } catch (Exception) { }
        _realtimeRefreshing = false;
        if (!IsCurrent) return;
        _realtimeTimer = _time.CreateTimer(_ => Post(() =>
        {
            _realtimeTimer?.Dispose();
            _realtimeTimer = null;
            _ = FlushRealtimeAsync();
        }), null, RealtimeWindow, Timeout.InfiniteTimeSpan);
    }

    // ---- Actions -------------------------------------------------------

    public async Task StartLibraryScanAsync(string libraryId)
    {
        if (!IsCurrent || !_startingLibraries.Add(libraryId)) return;
        Raise("actions");
        try
        {
            await _api.StartDashboardLibraryScanAsync(_lease, libraryId, _lease.Token);
            if (IsCurrent) Notify(DashboardNoticeKind.Success, DashboardText.T("web.common.scans.fullIngestStarted"));
        }
        catch (Exception ex) when (IsCurrent)
        {
            Notify(DashboardNoticeKind.Error, ErrorText(ex, "web.common.scans.scanFailed"));
        }
        catch (Exception) { }
        finally
        {
            if (!_disposed) { _startingLibraries.Remove(libraryId); Raise("actions"); }
        }
    }

    public async Task CancelLibraryScansAsync(string libraryId)
    {
        if (!IsCurrent || !_cancellingLibraries.Add(libraryId)) return;
        Raise("actions");
        try
        {
            await _api.CancelDashboardLibraryScansAsync(_lease, libraryId, _lease.Token);
            if (IsCurrent) Notify(DashboardNoticeKind.Success, DashboardText.T("web.common.scans.cancelRequested"));
        }
        catch (Exception ex) when (IsCurrent)
        {
            Notify(DashboardNoticeKind.Error, ErrorText(ex, "web.common.scans.cancelFailed"));
        }
        catch (Exception) { }
        finally
        {
            if (!_disposed) { _cancellingLibraries.Remove(libraryId); Raise("actions"); }
        }
    }

    public async Task ScanAllLibrariesAsync()
    {
        if (!IsCurrent || ScanAllPending) return;
        ScanAllPending = true;
        Raise("actions");
        try
        {
            await _api.ScanAllDashboardLibrariesAsync(_lease, _lease.Token);
            if (IsCurrent) Notify(DashboardNoticeKind.Success, DashboardText.T("web.admin.libraries.toast.scanAllStarted"));
        }
        catch (Exception ex) when (IsCurrent)
        {
            Notify(DashboardNoticeKind.Error, ErrorText(ex, "web.admin.libraries.toast.scanFailed"));
        }
        catch (Exception) { }
        finally
        {
            if (!_disposed) { ScanAllPending = false; Raise("actions"); }
        }
    }

    /// <summary>Signs out one login session (after the caller's confirmation). True only on confirmed success.</summary>
    public async Task<bool> RevokeLoginSessionAsync(string userId, string sessionId)
    {
        if (!IsCurrent) return false;
        try
        {
            await _api.RevokeDashboardLoginSessionAsync(_lease, userId, sessionId, _lease.Token);
        }
        catch (Exception)
        {
            return false;
        }
        if (!IsCurrent) return false;
        Invalidate("api-clients:");
        return true;
    }

    private static string ErrorText(Exception ex, string fallbackKey)
        => ex is ApiException api && !string.IsNullOrWhiteSpace(api.Message) ? api.Message : DashboardText.T(fallbackKey);

    // ---- Settings attention -------------------------------------------

    /// <summary>Mounts every input the attention notice reads; Meilisearch status only when selected.</summary>
    public IReadOnlyList<IDisposable> MountAttention()
    {
        var mounts = new List<IDisposable> { Mount(AttentionSettings), Mount(AttentionSensitive), Mount(AttentionBranding), Mount(Status) };
        return mounts;
    }

    public (IReadOnlyList<DashboardHealthItem> Visible, int HiddenCount, bool Loaded) Attention()
    {
        var settings = Get(AttentionSettings).As<DashboardAttentionSettings>();
        if (settings == null) return ([], 0, false);
        var provider = settings.Values.GetValueOrDefault("catalog.search.provider")?.Trim() is { Length: > 0 } p ? p : "postgres";
        if (provider == "meilisearch" && EntryFor(AttentionSearch).Mounts == 0) Mount(AttentionSearch);
        var branding = Get(AttentionBranding).As<DashboardBranding>();
        var status = Get(Status).As<DashboardServerStatus>();
        var items = DashboardPresentation.BuildSettingsHealth(settings.Values, Get(AttentionSensitive).As<DashboardSensitiveStatus>()?.Configured,
                branding?.StorageAvailable == true, status, provider == "meilisearch" ? Get(AttentionSearch).As<DashboardSearchStatus>() : null)
            .Where(item => item.ActionLabel != null).ToList();
        var loaded = branding != null && status != null;
        if (branding == null) items = items.Where(item => item.Id != "storage").ToList();
        var dismissed = DashboardPresentation.ParseHiddenAttention(_attentionPendingValue
            ?? settings.Values.GetValueOrDefault(AdminMonitorApi.DashboardHiddenAttentionKey));
        var visible = items.Where(item => !DashboardPresentation.IsAttentionDismissible(item) || !dismissed.Contains(item.Id)).ToArray();
        PruneAttention(settings, items, loaded);
        return (visible, items.Count - visible.Length, loaded);
    }

    public void DismissAttention(DashboardHealthItem item)
    {
        if (AttentionSaving || !DashboardPresentation.IsAttentionDismissible(item)) return;
        var settings = Get(AttentionSettings).As<DashboardAttentionSettings>();
        if (settings == null) return;
        var dismissed = DashboardPresentation.ParseHiddenAttention(settings.Values.GetValueOrDefault(AdminMonitorApi.DashboardHiddenAttentionKey)).ToList();
        if (dismissed.Contains(item.Id)) return;
        dismissed.Add(item.Id);
        _ = SaveAttentionAsync(settings, DashboardPresentation.EncodeHiddenAttention(dismissed));
    }

    public void RestoreAllAttention()
    {
        if (AttentionSaving) return;
        var settings = Get(AttentionSettings).As<DashboardAttentionSettings>();
        if (settings == null) return;
        if (DashboardPresentation.ParseHiddenAttention(settings.Values.GetValueOrDefault(AdminMonitorApi.DashboardHiddenAttentionKey)).Count == 0) return;
        _ = SaveAttentionAsync(settings, "");
    }

    /// <summary>A dismissal is forgotten once its item resolves; never while inputs are refreshing.</summary>
    private void PruneAttention(DashboardAttentionSettings settings, IReadOnlyList<DashboardHealthItem> items, bool loaded)
    {
        if (!loaded || AttentionSaving || Get(AttentionSettings).IsFetching || Get(Status).IsFetching || ReferenceEquals(_prunedSettings, settings)) return;
        var ids = DashboardPresentation.ParseHiddenAttention(settings.Values.GetValueOrDefault(AdminMonitorApi.DashboardHiddenAttentionKey));
        var current = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var kept = ids.Where(current.Contains).ToArray();
        if (kept.Length == ids.Count) return;
        _prunedSettings = settings;
        _ = SaveAttentionAsync(settings, DashboardPresentation.EncodeHiddenAttention(kept));
    }

    private async Task SaveAttentionAsync(DashboardAttentionSettings baseline, string value)
    {
        if (!IsCurrent || AttentionSaving) return;
        AttentionSaving = true;
        _attentionPendingValue = value;
        Raise("attention");
        try
        {
            await _api.SetDashboardHiddenAttentionAsync(_lease, value, baseline.ETag, _lease.Token);
            if (!IsCurrent) return;
            Invalidate(AttentionSettings);
            Invalidate(Status);
            Invalidate(AttentionSensitive);
            await (EntryFor(AttentionSettings).InFlight ?? Task.CompletedTask);
        }
        catch (Exception ex) when (IsCurrent)
        {
            Notify(DashboardNoticeKind.Error, ex.Message);
        }
        catch (Exception) { }
        finally
        {
            if (!_disposed)
            {
                AttentionSaving = false;
                _attentionPendingValue = null;
                Raise("attention");
            }
        }
    }

    // ---- Plumbing ------------------------------------------------------

    private void Notify(DashboardNoticeKind kind, string message)
    {
        if (IsCurrent) Notice?.Invoke(kind, message);
    }

    private void Raise(string key)
    {
        if (IsCurrent) Changed?.Invoke(key);
    }

    private void Post(Action action)
    {
        if (_context != null) _context.Post(_ => { if (IsCurrent) action(); }, null);
        else if (IsCurrent) action();
    }

    /// <summary>Retires: timers stop, the channel lease is released and every row is dropped.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        if (_cache != null && IsCurrent)
        {
            var now = Now;
            _cache.Retain(_lease, _cacheVisit, _entries
                .Where(pair => (pair.Value.State.Data != null && pair.Value.State.UpdatedAt.HasValue) ||
                    LegacyDependency(pair.Key, pair.Value) != null)
                .Select(pair => KeyValuePair.Create(pair.Key, new DashboardQueryCache.Snapshot(pair.Value.State.Data,
                    // Retain capability-only entries, but make them retry even if a shared snapshot is cached.
                    pair.Value.State.UpdatedAt, pair.Value.State.Invalidated || pair.Value.State.Data == null,
                    (pair.Value.UnusedSince ?? now) + DashboardQueryCache.UnusedLifetime,
                    // An unfinished native probe cannot publish here and must be due on return.
                    pair.Value.State.IsFetching ? null : pair.Value.LastStarted, pair.Value.State.InsightsUnsupported))));
        }
        _disposed = true;
        _tick?.Dispose();
        _realtimeTimer?.Dispose();
        _feedReads?.Dispose();
        if (_feed != null) _feed.Changed -= OnFeedChanged;
        _entries.Clear();
        _observedSessions = null;
        _initialSessionsRead = null;
        _activeScans.Clear();
        _activeScanOrder.Clear();
        _realtimePending.Clear();
        Changed = null;
        Notice = null;
        SessionPosterUrlsChanged = null;
    }
}
