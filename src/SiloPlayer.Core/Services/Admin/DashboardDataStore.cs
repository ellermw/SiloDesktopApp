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
    /// <summary>No data and no error yet: the skeleton state.</summary>
    public bool IsLoading => Data == null && Error == null;
    public bool IsSuccess => Data != null;
    public T? As<T>() where T : class => Data as T;
}

public enum DashboardNoticeKind { Success, Error }

/// <summary>
/// All dashboard data for one captured authority lease. Widgets mount the
/// queries they render; only mounted queries fetch or poll. Insights are fresh
/// for 55s and leaderboards for 5 minutes; the page refreshes every 60s only
/// while visible and focused; resources poll every 15s under the same gate,
/// nodes every 30s and autoscan every 15s while visible. Session events
/// coalesce into one REST refresh per 5s window. Every publication re-checks
/// the lease, and a retired lease publishes nothing and keeps nothing.
/// </summary>
public sealed class DashboardDataStore : IDisposable
{
    public static readonly TimeSpan PageRefreshInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ManualSpinnerMinimum = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RealtimeWindow = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EgressSampleMaxAge = TimeSpan.FromMinutes(2);

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
    public static string TopActivity(int days) => "top:" + days;
    public static string ApiClients(int hours) => "api-clients:" + hours;
    public static string ApiClientEndpoints(string sessionId, int hours) => $"api-clients:{hours}:endpoints:{sessionId}";

    /// <summary>Queries the 60s/manual refresh marks stale so mounted widgets refetch.</summary>
    private static readonly string[] WidgetPrefixes =
        ["timeseries:", "playback:", "top:", Downloads, "api-clients:", Status, Resources, ResourceCapabilities, Nodes, Autoscan, ScanHistory, RecentErrors];

    private readonly AdminMonitorApi _api;
    private readonly MonitorAuthorityLease _lease;
    private readonly IDashboardActivityFeed? _feed;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _realtimePending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DashboardActiveScan> _activeScans = new(StringComparer.Ordinal);
    private readonly List<string> _activeScanOrder = [];
    private IDisposable? _feedReads;
    private string? _sessionSignature;
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
        public DateTimeOffset? LastStarted;
    }

    public DashboardDataStore(AdminMonitorApi api, MonitorAuthorityLease lease, IDashboardActivityFeed? feed = null, TimeProvider? time = null)
    {
        _api = api;
        _lease = lease;
        _feed = feed;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised with the changed query key, or "page"/"scans"/"actions"/"attention".</summary>
    public event Action<string>? Changed;
    public event Action<DashboardNoticeKind, string>? Notice;

    public MonitorAuthorityLease Lease => _lease;
    public DateTimeOffset Now => _time.GetUtcNow();
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
        }
        _ = FetchAsync(Capabilities);
    }

    public DashboardQueryState Get(string key) => EntryFor(key).State;

    /// <summary>Marks a query in use; it fetches now if empty or stale and polls while mounted.</summary>
    public IDisposable Mount(string key)
    {
        var entry = EntryFor(key);
        entry.Mounts++;
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
            if (store._entries.TryGetValue(key, out var entry)) entry.Mounts = Math.Max(0, entry.Mounts - 1);
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
                if (entry.Mounts > 0 && IsStale(key, entry)) _ = FetchAsync(key);
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
    /// A manual refresh keeps its spinner at least one second.
    /// </summary>
    public async Task RefreshDashboardAsync(bool manual)
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
        _ => null,
    };

    private TimeSpan StaleTime(string key) => key switch
    {
        Build or Capabilities => TimeSpan.MaxValue,
        Stats or Sessions or Libraries or Users or Nodes or AttentionSettings or AttentionSensitive => TimeSpan.FromSeconds(30),
        Status or Resources or AttentionSearch or Autoscan or ScanHistory => TimeSpan.FromSeconds(15),
        ResourceCapabilities => TimeSpan.FromSeconds(60),
        RecentErrors => TimeSpan.FromSeconds(5),
        AttentionBranding => TimeSpan.FromMinutes(5),
        _ when key.StartsWith("top:", StringComparison.Ordinal) => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromSeconds(55),
    };

    private bool IsStale(string key, Entry entry)
    {
        if (entry.State.Data == null || entry.State.Invalidated || entry.State.UpdatedAt is not { } updated) return true;
        var stale = StaleTime(key);
        return stale != TimeSpan.MaxValue && Now - updated >= stale;
    }

    private Entry EntryFor(string key)
    {
        if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry();
        return entry;
    }

    /// <summary>Fetches one query under the lease. Older generations never publish over newer ones.</summary>
    public Task FetchAsync(string key, bool force = false, bool refreshStats = false)
    {
        var entry = EntryFor(key);
        if (!IsCurrent) return Task.CompletedTask;
        if (entry.InFlight != null) return entry.InFlight;
        var generation = ++entry.Generation;
        entry.LastStarted = Now;
        entry.State.IsFetching = true;
        var task = RunFetchAsync(key, entry, generation, refreshStats);
        // A read that completed synchronously already cleared itself; never park a finished task.
        entry.InFlight = task;
        if (task.IsCompleted && entry.Generation == generation) entry.InFlight = null;
        return task;
    }

    private async Task RunFetchAsync(string key, Entry entry, long generation, bool refreshStats)
    {
        object? data = null;
        Exception? error = null;
        try
        {
            var gate = CapabilityFor(key);
            if (gate != null)
            {
                await EnsureCapabilitiesAsync();
                if (Get(Capabilities).Data is DashboardCapabilities caps && !Supports(caps, gate))
                    throw new DashboardUnsupportedException(gate);
            }
            data = await LoadAsync(key, refreshStats);
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
            if (entry.Generation == generation) entry.InFlight = null;
        }
        if (!IsCurrent || entry.Generation != generation) return;
        entry.State.IsFetching = false;
        if (error != null)
        {
            entry.State.Error = error;
        }
        else
        {
            entry.State.Data = data;
            entry.State.Error = null;
            entry.State.UpdatedAt = Now;
            entry.State.Invalidated = false;
        }
        Raise(key);
        if (key is AttentionSettings or Status) Raise("attention");
    }

    private async Task EnsureCapabilitiesAsync()
    {
        var entry = EntryFor(Capabilities);
        if (entry.State.Data != null || entry.State.Error != null) return;
        try { await (entry.InFlight ?? FetchAsync(Capabilities)); } catch (Exception) { }
    }

    private static string? CapabilityFor(string key) => key switch
    {
        _ when key.StartsWith("timeseries:", StringComparison.Ordinal) => "timeseries",
        _ when key.StartsWith("playback:", StringComparison.Ordinal) => "playback_activity",
        _ when key.StartsWith("top:", StringComparison.Ordinal) => "top_activity",
        _ when key.StartsWith("api-clients:", StringComparison.Ordinal) => "api_clients",
        Downloads => "downloads_stats",
        RecentErrors => "log_level_list",
        _ => null,
    };

    private static bool Supports(DashboardCapabilities caps, string capability) => capability switch
    {
        "timeseries" => caps.Timeseries,
        "playback_activity" => caps.PlaybackActivity,
        "top_activity" => caps.TopActivity,
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
                if (!current.SessionsLoaded) throw new InvalidOperationException(current.SessionsError ?? "Sessions unavailable.");
                _sessionSignature = SessionSignature(current.Sessions);
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
            "top" => await _api.GetDashboardTopActivityAsync(lease, int.Parse(parts[1]), ct),
            "api-clients" when parts.Length == 2 => await _api.GetDashboardApiClientsAsync(lease, int.Parse(parts[1]), 10, ct),
            "api-clients" when parts.Length >= 4 && parts[2] == "endpoints" =>
                await _api.GetDashboardApiClientEndpointsAsync(lease, string.Join(':', parts[3..]), int.Parse(parts[1]), ct),
            _ => throw new ArgumentException("Unknown dashboard query " + key, nameof(key)),
        };
    }

    // ---- Realtime ------------------------------------------------------

    private void OnFeedChanged() => Post(() => SyncFeed(initial: false));

    private static string SessionSignature(IReadOnlyList<DashboardSession> sessions)
        => string.Join('|', sessions.Select(s => s.SessionId + ':' + s.IsPaused + ':' + s.EffectivePlayMethod + ':' + s.ProfileId));

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
        if (current.SessionsLoaded)
        {
            var signature = SessionSignature(current.Sessions);
            var changed = signature != _sessionSignature || entry.State.Data == null;
            entry.State.Data = current.Sessions;
            entry.State.Error = current.SessionsError != null ? new InvalidOperationException(current.SessionsError) : null;
            if (changed)
            {
                var hadData = _sessionSignature != null;
                _sessionSignature = signature;
                entry.State.UpdatedAt = Now;
                entry.State.Invalidated = false;
                if (hadData && !initial) ScheduleRealtime(Stats);
            }
            Raise(Sessions);
        }
        else if (current.SessionsError != null)
        {
            entry.State.Error = new InvalidOperationException(current.SessionsError);
            Raise(Sessions);
        }
        if (current.ScansLoaded)
        {
            var before = _activeScanOrder.ToHashSet(StringComparer.Ordinal);
            _activeScans.Clear();
            _activeScanOrder.Clear();
            foreach (var scan in current.Scans.Where(scan => scan.IsActive))
                if (_activeScans.TryAdd(scan.Id, scan)) _activeScanOrder.Add(scan.Id);
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
        _disposed = true;
        _tick?.Dispose();
        _realtimeTimer?.Dispose();
        _feedReads?.Dispose();
        if (_feed != null) _feed.Changed -= OnFeedChanged;
        _entries.Clear();
        _activeScans.Clear();
        _activeScanOrder.Clear();
        _realtimePending.Clear();
        Changed = null;
        Notice = null;
    }
}
