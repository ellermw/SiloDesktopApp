using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// One immutable view of Admin › Playback › History. <see cref="Window"/> is the
/// bounded first page for <see cref="Filter"/> (null while that filter loads);
/// pagination is local over that window. Counts are visible-window counts.
/// </summary>
public sealed record AdminPlaybackHistoryState
{
    public static AdminPlaybackHistoryState Closed { get; } = new();

    public bool IsOpen { get; init; }
    public AdminPlaybackHistoryFilter Filter { get; init; } = new();
    public AdminPlaybackHistoryWindow? Window { get; init; }
    /// <summary>The first read for this filter is in flight (skeleton).</summary>
    public bool Loading { get; init; }
    /// <summary>Any history read is in flight ("Refreshing...").</summary>
    public bool Fetching { get; init; }
    public string? Error { get; init; }
    public AdminFailureKind? ErrorKind { get; init; }
    public int PageIndex { get; init; }
    public int PageSize { get; init; } = AdminPlaybackHistoryController.DefaultPageSize;
    public AdminHistoryUserList? Users { get; init; }
    public AdminHistoryProfileList? Profiles { get; init; }
    public bool ProfilesLoading { get; init; }
    public bool ManualRefreshPending { get; init; }
    /// <summary>Grows on every change; a renderer drops a snapshot older than the one it drew.</summary>
    public long Revision { get; init; }

    public IReadOnlyList<AdminPlaybackHistoryRow> Rows => Window?.Rows ?? [];
    public int Total => Rows.Count;
    public IReadOnlyList<AdminPlaybackHistoryRow> PageRows => Rows.Skip(PageIndex * PageSize).Take(PageSize).ToArray();
    public int RangeFrom => PageIndex * PageSize + 1;
    public int RangeTo => Math.Min((PageIndex + 1) * PageSize, Total);
    public bool CanPrevious => PageIndex > 0;
    public bool CanNext => (PageIndex + 1) * PageSize < Total;
    public int CompletedCount => Rows.Count(row => row.Completed);
    public int PartialCount => Rows.Count(row => !row.Completed);
    public bool ProfilePickerEnabled => Filter.HasUser;

    /// <summary>WebUI <c>activeMediaItemLabel</c>: the first row's title, else the raw item id.</summary>
    public string? ActiveMediaItemLabel => Filter.RequestMediaItemId is not { } item ? null
        : Rows.Count > 0 ? AdminPlaybackHistoryPresentation.RowLabel(Rows[0]) : item;
}

/// <summary>
/// Admin › Playback › History state owner (WebUI <c>AdminPlaybackHistory.tsx</c>
/// + <c>useAdminPlaybackHistory</c>, official Silo e61187da). Reads run under one
/// <see cref="AdminAuthorityLifetime"/> owner; any authority change (account,
/// profile, server, permission) retires them and clears every row and picker.
/// <list type="bullet">
/// <item>One history read per filter at a time: a manual, focus or periodic
/// refresh joins the read in flight; a filter change cancels the old read and
/// its late answer is discarded.</item>
/// <item>The WebUI's 15 s poll runs only while the page is open and visible
/// (<see cref="SetVisible"/>); a hidden page never polls.</item>
/// <item>One bounded page (<see cref="AdminPlaybackHistoryApi.PageLimit"/>),
/// locally paged 25/50/100; no history cursor walk.</item>
/// </list>
/// Thread-safe; <see cref="Changed"/> may be raised on any thread.
/// </summary>
public sealed class AdminPlaybackHistoryController : IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ManualSpinnerMinimum = TimeSpan.FromSeconds(1);
    public const int DefaultPageSize = 25;
    public static IReadOnlyList<int> PageSizeOptions { get; } = [25, 50, 100];

    private readonly AdminPlaybackHistoryApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private AdminPlaybackHistoryState _state = AdminPlaybackHistoryState.Closed;
    private AdminPlaybackHistoryFilter _filter = new();
    private int _pageSize = DefaultPageSize;
    private MonitorAuthorityLease? _lease;
    private Read? _history;
    private Read? _users;
    private Read? _profiles;
    private long _historySeq, _profileSeq, _userSeq;
    private Task? _manual;
    private long _manualId;
    private ITimer? _poll;
    private bool _visible;
    private bool _hiddenSinceRead;
    private DateTimeOffset _lastRead = DateTimeOffset.MinValue;
    private bool _disposed;

    private sealed record Read(long Seq, string Key, CancellationTokenSource Cts)
    {
        public Task Task { get; set; } = Task.CompletedTask;
        public bool Done { get; set; }
    }

    public AdminPlaybackHistoryController(AdminPlaybackHistoryApi api, AdminMonitorAuthority authority, TimeProvider? time = null)
    {
        _api = api;
        _time = time ?? TimeProvider.System;
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnAuthorityRetired;
    }

    /// <summary>Raised after every state change, on any thread. Marshal, then read <see cref="State"/>.</summary>
    public event Action? Changed;

    /// <summary>The controller dropped URL state itself (an unknown profile); replace the location with it.</summary>
    public event Action<AdminPlaybackHistoryFilter>? FilterReplaced;

    /// <summary>Authority changed while open: everything was cleared. Marshal and call <see cref="Reopen"/>.</summary>
    public event Action? AuthorityRetired;

    public AdminPlaybackHistoryState State { get { lock (_gate) return _state; } }

    /// <summary>Number of history requests started (test seam; counts reads, not refresh requests).</summary>
    public int HistoryReadsStarted { get; private set; }

    /// <summary>Captures the page owner and loads <paramref name="filter"/>, the account picker and its profiles. False when not acting as admin.</summary>
    public bool Open(AdminPlaybackHistoryFilter filter)
    {
        MonitorAuthorityLease lease;
        try { lease = _lifetime.Open(); }
        catch (OperationCanceledException) { return false; }
        catch (ObjectDisposedException) { return false; }
        lock (_gate)
        {
            CancelAllLocked();
            _lease = lease;
            _filter = filter.Normalized();
            _state = new AdminPlaybackHistoryState { IsOpen = true, Filter = _filter, PageSize = _pageSize, Revision = _state.Revision + 1 };
            StartUsersLocked(lease);
            if (_filter.RequestUserId != null) StartProfilesLocked(lease, _filter.RequestUserId);
            StartHistoryLocked(lease);
            if (_visible) StartPollLocked();
        }
        Raise();
        return true;
    }

    /// <summary>After an authority change: reopen with the same URL state if still acting as admin.</summary>
    public bool Reopen()
    {
        AdminPlaybackHistoryFilter filter;
        lock (_gate) { if (_disposed) return false; filter = _filter; }
        return Open(filter);
    }

    /// <summary>Explicit page activation after <see cref="Suspend"/>, before <see cref="Open"/>.</summary>
    public void Resume()
    {
        lock (_gate) if (_disposed) return;
        _lifetime.Resume();
    }

    /// <summary>Page departure (navigation or outer Admin exit): retire every read, stop polling, clear private data.</summary>
    public void Suspend()
    {
        _lifetime.Retire();
        lock (_gate)
        {
            CancelAllLocked();
            _state = AdminPlaybackHistoryState.Closed with { Filter = _filter, PageSize = _pageSize, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    private void OnAuthorityRetired()
    {
        lock (_gate)
        {
            if (_disposed) return;
            CancelAllLocked();
            _state = AdminPlaybackHistoryState.Closed with { Filter = _filter, PageSize = _pageSize, Revision = _state.Revision + 1 };
        }
        Raise();
        AuthorityRetired?.Invoke();
    }

    private void CancelAllLocked()
    {
        // Sequences advance first: a cancelled read may complete inline on this thread.
        _historySeq++; _profileSeq++; _userSeq++;
        var reads = new[] { _history, _users, _profiles };
        _history = _users = _profiles = null;
        foreach (var read in reads) CancelLocked(read);
        _poll?.Dispose(); _poll = null;
        _manual = null;
        _lease = null;
    }

    private static void CancelLocked(Read? read)
    {
        if (read == null) return;
        try { read.Cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    // ── URL state (WebUI updateFilter/resetFilters) ──────────

    /// <summary>Chooses an account (null = all users); the profile is always dropped.</summary>
    public AdminPlaybackHistoryFilter SelectUser(string? userId) =>
        ApplyFilter(CurrentFilter() with { UserId = string.IsNullOrEmpty(userId) ? null : userId, ProfileId = null });

    /// <summary>Chooses a profile (null = all profiles). Ignored without an account.</summary>
    public AdminPlaybackHistoryFilter SelectProfile(string? profileId) =>
        ApplyFilter(CurrentFilter() with { ProfileId = string.IsNullOrEmpty(profileId) ? null : profileId });

    public AdminPlaybackHistoryFilter SelectCompletion(AdminPlaybackHistoryCompletion completion) =>
        ApplyFilter(CurrentFilter() with { Completion = completion });

    /// <summary>Reset: every URL parameter is removed, including the item filter.</summary>
    public AdminPlaybackHistoryFilter Reset() => ApplyFilter(new AdminPlaybackHistoryFilter());

    private AdminPlaybackHistoryFilter CurrentFilter() { lock (_gate) return _filter; }

    /// <summary>
    /// Shows <paramref name="filter"/>: the first local page, a fresh read (the
    /// previous filter's read is cancelled and its answer discarded) and, when
    /// the account changed, that account's profiles. Returns the canonical filter.
    /// </summary>
    public AdminPlaybackHistoryFilter ApplyFilter(AdminPlaybackHistoryFilter filter)
    {
        filter = filter.Normalized();
        lock (_gate) ApplyFilterLocked(filter);
        Raise();
        return filter;
    }

    /// <summary>An unchanged filter only returns to the first local page (WebUI setPage(0)); its data and read in flight are kept.</summary>
    private void ApplyFilterLocked(AdminPlaybackHistoryFilter filter)
    {
        var previous = _filter;
        _filter = filter;
        if (filter == previous || !_state.IsOpen || _lease is not { } lease)
        {
            _state = _state with { Filter = filter, PageIndex = 0, Revision = _state.Revision + 1 };
            return;
        }
        _state = _state with { Filter = filter, PageIndex = 0, Window = null, Error = null, ErrorKind = null, Revision = _state.Revision + 1 };
        if (filter.RequestUserId != previous.RequestUserId)
        {
            _profileSeq++; var oldProfiles = _profiles; _profiles = null; CancelLocked(oldProfiles);
            _state = _state with { Profiles = null, ProfilesLoading = false };
            if (filter.RequestUserId != null) StartProfilesLocked(lease, filter.RequestUserId);
        }
        _historySeq++; var oldHistory = _history; _history = null; CancelLocked(oldHistory);
        StartHistoryLocked(lease);
    }

    // ── Local pagination ─────────────────────────────────────

    public void SetPageSize(int size)
    {
        if (!PageSizeOptions.Contains(size)) throw new ArgumentOutOfRangeException(nameof(size));
        lock (_gate)
        {
            _pageSize = size;
            _state = _state with { PageSize = size, PageIndex = 0, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    public void NextPage() => MovePage(+1);
    public void PreviousPage() => MovePage(-1);

    private void MovePage(int delta)
    {
        lock (_gate)
        {
            var target = _state.PageIndex + delta;
            if (target < 0 || (delta > 0 && !_state.CanNext)) return;
            _state = _state with { PageIndex = target, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    // ── Refresh ──────────────────────────────────────────────

    /// <summary>
    /// Refreshes the current filter. A read already in flight is joined, never
    /// duplicated. A manual refresh keeps its pending state for at least
    /// <see cref="ManualSpinnerMinimum"/>; a second manual click while pending is ignored.
    /// </summary>
    public Task RefreshAsync(bool manual = false)
    {
        Task read;
        lock (_gate)
        {
            if (_disposed || !_state.IsOpen || _lease is not { } lease || !lease.IsCurrent) return Task.CompletedTask;
            if (manual && _manual != null) return _manual;
            read = StartHistoryLocked(lease);
            if (!manual) return read;
            _state = _state with { ManualRefreshPending = true, Revision = _state.Revision + 1 };
            _manual = ManualAsync(read, _time.GetTimestamp(), ++_manualId);
            read = _manual;
        }
        Raise();
        return read;
    }

    private async Task ManualAsync(Task read, long started, long id)
    {
        await Task.Yield();
        try { await read.ConfigureAwait(false); }
        catch { /* the read published its own outcome */ }
        var remaining = ManualSpinnerMinimum - _time.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining, _time).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        lock (_gate)
        {
            if (_manual == null || _manualId != id || !_state.IsOpen) return;
            _manual = null;
            _state = _state with { ManualRefreshPending = false, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    /// <summary>
    /// The page is visible (open, Admin shown, window not hidden). Visible: poll
    /// every <see cref="RefreshInterval"/> and, returning from hidden with stale
    /// data, refresh once (WebUI refetch on focus). Hidden: no timer at all.
    /// </summary>
    public void SetVisible(bool visible)
    {
        var refresh = false;
        lock (_gate)
        {
            if (_disposed || _visible == visible) return;
            _visible = visible;
            if (!visible)
            {
                _poll?.Dispose(); _poll = null;
                _hiddenSinceRead = true;
                return;
            }
            if (_state.IsOpen) StartPollLocked();
            refresh = _state.IsOpen && _hiddenSinceRead && _time.GetUtcNow() - _lastRead >= RefreshInterval;
            _hiddenSinceRead = false;
        }
        if (refresh) _ = RefreshAsync();
    }

    public bool IsPolling { get { lock (_gate) return _poll != null; } }

    private void StartPollLocked()
    {
        _poll?.Dispose();
        _poll = _time.CreateTimer(_ => OnPoll(), null, RefreshInterval, RefreshInterval);
    }

    private void OnPoll()
    {
        lock (_gate) if (!_visible || !_state.IsOpen) return;
        _ = RefreshAsync();
    }

    // ── Reads ────────────────────────────────────────────────

    private static string HistoryKey(AdminPlaybackHistoryFilter filter) => AdminPlaybackHistoryApi.HistoryPath(filter);

    /// <summary>Starts the current filter's read or returns the one in flight. Caller holds the gate.</summary>
    private Task StartHistoryLocked(MonitorAuthorityLease lease)
    {
        var filter = _filter;
        string key;
        try { key = HistoryKey(filter); }
        catch (ArgumentException)
        {
            _state = _state with { Loading = false, Fetching = false, Error = AdminPlaybackAuditText.T("web.admin.history.loadError"), ErrorKind = AdminFailureKind.Validation, Revision = _state.Revision + 1 };
            return Task.CompletedTask;
        }
        if (_history is { Done: false } inFlight && inFlight.Key == key) return inFlight.Task;
        var seq = ++_historySeq;
        var previous = _history;
        _history = null;
        CancelLocked(previous);
        var read = new Read(seq, key, CancellationTokenSource.CreateLinkedTokenSource(lease.Token));
        _history = read;
        HistoryReadsStarted++;
        _state = _state with { Fetching = true, Loading = _state.Window == null, Revision = _state.Revision + 1 };
        read.Task = RunHistoryAsync(read, lease, filter);
        return read.Task;
    }

    private async Task RunHistoryAsync(Read read, MonitorAuthorityLease lease, AdminPlaybackHistoryFilter filter)
    {
        await Task.Yield();
        AdminPlaybackHistoryWindow? window = null;
        Exception? failure = null;
        try { window = await _api.ReadWindowAsync(lease, filter, AdminPlaybackHistoryApi.PageLimit, read.Cts.Token).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            read.Done = true;
            read.Cts.Dispose();
            if (read.Seq != _historySeq || !ReferenceEquals(lease, _lease) || !_lifetime.IsCurrent(lease)) return;
            if (window != null)
            {
                var pages = Math.Max(1, (window.Rows.Count + _state.PageSize - 1) / _state.PageSize);
                _lastRead = _time.GetUtcNow();
                _state = _state with
                {
                    Window = window, Loading = false, Fetching = false, Error = null, ErrorKind = null,
                    PageIndex = Math.Min(_state.PageIndex, pages - 1), Revision = _state.Revision + 1,
                };
            }
            else
            {
                var kind = AdminFailures.Classify(failure!, lease);
                if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled)
                    _state = _state with { Loading = false, Fetching = false, Revision = _state.Revision + 1 };
                else
                    _state = _state with { Loading = false, Fetching = false, Error = ErrorText(failure!, kind), ErrorKind = kind, Revision = _state.Revision + 1 };
            }
        }
        Raise();
    }

    /// <summary>WebUI shows <c>error.message</c>, else the load-error text; transport detail is not shown.</summary>
    public static string ErrorText(Exception error, AdminFailureKind kind) => error switch
    {
        InvalidDataException invalid when invalid.Message.Length > 0 => invalid.Message,
        ApiException api when kind is not AdminFailureKind.Transport and not AdminFailureKind.Unknown && !string.IsNullOrWhiteSpace(api.Message) => api.Message,
        _ => AdminPlaybackAuditText.T("web.admin.history.loadError"),
    };

    private void StartUsersLocked(MonitorAuthorityLease lease)
    {
        var seq = ++_userSeq;
        var previous = _users;
        _users = null;
        CancelLocked(previous);
        var read = new Read(seq, "users", CancellationTokenSource.CreateLinkedTokenSource(lease.Token));
        _users = read;
        read.Task = RunUsersAsync(read, lease);
    }

    private async Task RunUsersAsync(Read read, MonitorAuthorityLease lease)
    {
        await Task.Yield();
        AdminHistoryUserList? users = null;
        try { users = await _api.ListUsersAsync(lease, read.Cts.Token).ConfigureAwait(false); }
        catch (Exception) { /* WebUI shows only "All users" when the account list cannot be read */ }
        lock (_gate)
        {
            read.Done = true;
            read.Cts.Dispose();
            if (users == null || read.Seq != _userSeq || !ReferenceEquals(lease, _lease) || !_lifetime.IsCurrent(lease)) return;
            _state = _state with { Users = users, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    private void StartProfilesLocked(MonitorAuthorityLease lease, string userId)
    {
        var seq = ++_profileSeq;
        var previous = _profiles;
        _profiles = null;
        CancelLocked(previous);
        var read = new Read(seq, userId, CancellationTokenSource.CreateLinkedTokenSource(lease.Token));
        _profiles = read;
        _state = _state with { Profiles = null, ProfilesLoading = true, Revision = _state.Revision + 1 };
        read.Task = RunProfilesAsync(read, lease, userId);
    }

    private async Task RunProfilesAsync(Read read, MonitorAuthorityLease lease, string userId)
    {
        await Task.Yield();
        AdminHistoryProfileList? profiles = null;
        try { profiles = await _api.ListProfilesAsync(lease, userId, read.Cts.Token).ConfigureAwait(false); }
        catch (Exception) { /* the profile picker stays at "All profiles", as in the WebUI */ }
        AdminPlaybackHistoryFilter? replaced = null;
        lock (_gate)
        {
            read.Done = true;
            read.Cts.Dispose();
            if (read.Seq != _profileSeq || !ReferenceEquals(lease, _lease) || !_lifetime.IsCurrent(lease) || _filter.RequestUserId != userId) return;
            _state = _state with { Profiles = profiles, ProfilesLoading = false, Revision = _state.Revision + 1 };
            // WebUI effect: a chosen profile that this account does not have is removed (replace).
            if (profiles is { Truncated: false, Items.Count: > 0 } && _filter.ProfileId is { } chosen && profiles.Items.All(profile => profile.Id != chosen))
            {
                replaced = (_filter with { ProfileId = null }).Normalized();
                ApplyFilterLocked(replaced);
            }
        }
        Raise();
        if (replaced != null) FilterReplaced?.Invoke(replaced);
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelAllLocked();
            _state = AdminPlaybackHistoryState.Closed;
        }
        _lifetime.Retired -= OnAuthorityRetired;
        _lifetime.Dispose();
        Changed = null;
        FilterReplaced = null;
        AuthorityRetired = null;
    }
}

/// <summary>Row text for Playback History (WebUI <c>historyRowTitle</c>, <c>watchTime</c>, <c>formatDuration</c>, <c>mediaTypeLabel</c>).</summary>
public static class AdminPlaybackHistoryPresentation
{
    private static string T(string key, params (string, object?)[] values) => AdminPlaybackAuditText.T(key, values);

    /// <summary>An episode leads with its series title; "S02E04 · Episode title" is the detail line.</summary>
    public static (string Title, string Detail) RowTitle(AdminPlaybackHistoryRow row)
    {
        if (row.SeriesTitle.Length > 0)
        {
            var episodeTitle = row.MediaTitle == row.SeriesTitle ? "" : row.MediaTitle;
            var detail = string.Join(" · ", new[] { EpisodeCode(row.SeasonNumber, row.EpisodeNumber), episodeTitle }.Where(part => part.Length > 0));
            return (row.SeriesTitle, detail);
        }
        var title = row.MediaTitle.Length > 0 ? row.MediaTitle : row.MediaItemId.Length > 0 ? row.MediaItemId : T("web.common.activity.title.file", ("id", row.MediaFileId));
        return (title, "");
    }

    public static string RowLabel(AdminPlaybackHistoryRow row)
    {
        var (title, detail) = RowTitle(row);
        return detail.Length > 0 ? $"{title} · {detail}" : title;
    }

    public static string EpisodeCode(int? season, int? episode) =>
        season is not { } s || episode is not { } e ? "" : T("web.admin.users.detail.common.episodeCode",
            ("season", s.ToString("00", CultureInfo.InvariantCulture)), ("episode", e.ToString("00", CultureInfo.InvariantCulture)));

    /// <summary>Measured watch time, or the server estimate (end position capped at the session length).</summary>
    public static (double Seconds, bool Estimated) WatchTime(AdminPlaybackHistoryRow row)
    {
        if (row.PlayedSeconds is { } played) return (played, false);
        var capped = row.WatchedSeconds;
        if (AdminPlaybackAuditText.TryParseInstant(row.EndedAt, out var ended) && AdminPlaybackAuditText.TryParseInstant(row.StartedAt, out var started))
            capped = Math.Min(row.WatchedSeconds, Math.Max((ended - started).TotalSeconds, 0));
        return (Math.Max(capped, 0), true);
    }

    public static string WatchTimeText(AdminPlaybackHistoryRow row)
    {
        var (seconds, estimated) = WatchTime(row);
        return estimated ? T("web.admin.history.row.estimatedDuration", ("duration", FormatDuration(seconds))) : FormatDuration(seconds);
    }

    public static string FormatDuration(double? seconds)
    {
        if (seconds is not { } value || value == 0 || double.IsNaN(value)) return T("web.admin.history.duration.none");
        var rounded = (long)Math.Max(0, Math.Floor(value));
        var hours = rounded / 3600;
        var minutes = rounded % 3600 / 60;
        var secs = rounded % 60;
        if (hours > 0) return T("web.admin.history.duration.hoursMinutes", ("hours", hours), ("minutes", minutes));
        if (minutes > 0) return T("web.admin.history.duration.minutesSeconds", ("minutes", minutes), ("seconds", secs));
        return T("web.admin.history.duration.seconds", ("seconds", secs));
    }

    /// <summary>Known media types are labelled; others print as sent; empty is "unknown".</summary>
    public static string MediaTypeLabel(string mediaType) => mediaType switch
    {
        "" => T("web.admin.history.mediaType.unknown"),
        "movie" or "episode" or "series" => T("web.admin.history.mediaType." + mediaType),
        _ => mediaType,
    };

    public static string Meta(AdminPlaybackHistoryRow row) =>
        T("web.admin.history.row.meta", ("media_type", MediaTypeLabel(row.MediaType)), ("session", row.SessionId.Length > 8 ? row.SessionId[..8] : row.SessionId));

    public static string Username(AdminPlaybackHistoryRow row) =>
        row.Username.Length > 0 ? row.Username : T("web.admin.activity.userFallback", ("id", row.UserId));

    public static string ProfileLabel(AdminPlaybackHistoryRow row) => row.ProfileName.Length > 0 ? row.ProfileName : row.ProfileId;

    public static string Method(AdminPlaybackHistoryRow row) => AdminActivityPresentation.FormatDecisionLabel(row.PlayMethod);

    public static string Status(AdminPlaybackHistoryRow row) => T(row.Completed ? "web.admin.history.row.completed" : "web.admin.history.row.partial");

    public static string Started(AdminPlaybackHistoryRow row, DateTimeOffset now) =>
        T("web.admin.history.row.started", ("time", AdminPlaybackAuditText.Relative(row.StartedAt, now) ?? row.StartedAt));

    /// <summary>Logs links (Monitor › Server Log), focused on this playback session.</summary>
    public static AdminRouteRequest LogsRequest(AdminPlaybackHistoryRow row, bool ffmpeg)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal) { ["playback_session_id"] = row.SessionId, ["focus"] = "playback" };
        if (ffmpeg) query["component"] = "ffmpeg";
        return new AdminRouteRequest(AdminRoute.ServerLog, query);
    }

    /// <summary>The profile link: this page, filtered to the row's account and profile.</summary>
    public static AdminRouteRequest ProfileRequest(AdminPlaybackHistoryRow row) =>
        new(AdminRoute.PlaybackHistory, new AdminPlaybackHistoryFilter(row.UserId, row.ProfileId).ToQuery());
}
