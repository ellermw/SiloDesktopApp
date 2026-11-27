using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// One immutable view of Playback History. The server page and detail read share the same admin authority.
/// </summary>
public sealed record AdminPlaybackHistoryState
{
    public static AdminPlaybackHistoryState Closed { get; } = new();

    public bool IsOpen { get; init; }
    public AdminPlaybackHistoryFilter Filter { get; init; } = new();
    public bool SupportsRichFilters { get; init; }
    public bool SupportsSeriesFilter { get; init; }
    public bool SupportsSessionDetail { get; init; }
    /// <summary>Filters that can be presented as supported without changing the requested URL scope.</summary>
    public AdminPlaybackHistoryFilter SupportedFilter => Filter.HasRichFilters
        ? Filter.SupportedBy(SupportsRichFilters, SupportsSeriesFilter) : Filter;
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
    public IReadOnlyList<AdminLibrary> Libraries { get; init; } = [];
    public AdminPlaybackHistoryRow? Detail { get; init; }
    public bool DetailLoading { get; init; }
    public bool DetailUnavailable { get; init; }
    public string? DetailError { get; init; }
    public bool ManualRefreshPending { get; init; }
    /// <summary>Grows on every change; a renderer drops a snapshot older than the one it drew.</summary>
    public long Revision { get; init; }

    public IReadOnlyList<AdminPlaybackHistoryRow> Rows => Window?.Rows ?? [];
    public int Total => Rows.Count;
    public bool CanPrevious => PageIndex > 0;
    public bool CanNext => Window is { ServerHasMore: true, NextCursor: not null };
    public int CompletedCount => Rows.Count(row => row.Completed);
    public int PartialCount => Rows.Count(row => !row.Completed);
    public bool ProfilePickerEnabled => Filter.HasUser;

    public string? MissingProfileLabel
    {
        get
        {
            if (Filter.ProfileId is not { } id || Profiles?.Items.Any(profile => profile.Id == id) == true) return null;
            var label = Rows.FirstOrDefault(row => row.ProfileId == id)?.ProfileName;
            return string.IsNullOrEmpty(label) ? id : label;
        }
    }

    public string? AdjacentSession(int offset)
    {
        if (Filter.SessionId is not { } session) return null;
        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].SessionId != session) continue;
            var target = index + offset;
            return target >= 0 && target < Rows.Count ? Rows[target].SessionId : null;
        }
        return null;
    }
}

/// <summary>
/// Playback History state owner. Filter and page changes retire the previous request.
/// A cursor belongs to the current filter and page size. Polling runs on the visible first page.
/// </summary>
public sealed class AdminPlaybackHistoryController : IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ManualSpinnerMinimum = TimeSpan.FromSeconds(1);
    public const int DefaultPageSize = 25;
    public static IReadOnlyList<int> PageSizeOptions { get; } = [25, 50, 100];

    private readonly AdminPlaybackHistoryApi _api;
    private readonly AdminLibrariesApi? _librariesApi;
    private readonly List<string> _cursors = [];
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private AdminPlaybackHistoryState _state = AdminPlaybackHistoryState.Closed;
    private AdminPlaybackHistoryFilter _filter = new();
    private int _pageSize = DefaultPageSize;
    private MonitorAuthorityLease? _lease;
    private Task? _capabilitiesRead;
    private Read? _history;
    private Read? _users;
    private Read? _profiles;
    private Read? _detail;
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

    public AdminPlaybackHistoryController(AdminPlaybackHistoryApi api, AdminMonitorAuthority authority, TimeProvider? time = null, AdminLibrariesApi? librariesApi = null)
    {
        _api = api;
        _librariesApi = librariesApi;
        _time = time ?? TimeProvider.System;
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnAuthorityRetired;
    }

    /// <summary>Raised after every state change, on any thread. Marshal, then read <see cref="State"/>.</summary>
    public event Action? Changed;

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
            if (_disposed || !_lifetime.IsCurrent(lease)) return false;
            CancelAllLocked();
            _lease = lease;
            _filter = filter.Normalized();
            _state = new AdminPlaybackHistoryState { IsOpen = true, Filter = _filter, PageSize = _pageSize, Revision = _state.Revision + 1 };
            _capabilitiesRead = ReadCapabilitiesAsync(lease);
            StartUsersLocked(lease);
            if (_filter.RequestUserId != null) StartProfilesLocked(lease, _filter.RequestUserId);
            StartHistoryLocked(lease);
            UpdateDetailLocked();
            StartPollLocked();
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
        var reads = new[] { _history, _users, _profiles, _detail };
        _history = _users = _profiles = _detail = null;
        _cursors.Clear();
        _lastRead = DateTimeOffset.MinValue;
        foreach (var read in reads) CancelLocked(read);
        _poll?.Dispose(); _poll = null;
        _manual = null;
        _capabilitiesRead = null;
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

    public AdminPlaybackHistoryFilter ApplyFilter(AdminPlaybackHistoryFilter filter)
    {
        filter = filter.Normalized();
        lock (_gate)
        {
            var previous = _filter;
            if (filter == previous) return filter;
            _filter = filter;
            _state = _state with { Filter = filter, Revision = _state.Revision + 1 };
            if (_state.IsOpen && _lease is { } lease)
            {
                if (filter.RequestUserId != previous.RequestUserId)
                {
                    _profileSeq++;
                    var oldProfiles = _profiles;
                    _profiles = null;
                    CancelLocked(oldProfiles);
                    _state = _state with { Profiles = null, ProfilesLoading = false };
                    if (filter.RequestUserId is { } user) StartProfilesLocked(lease, user);
                }
                if ((filter with { SessionId = null }) != (previous with { SessionId = null }))
                {
                    _cursors.Clear();
                    ChangePageLocked(lease);
                }
                else UpdateDetailLocked();
            }
        }
        Raise();
        return filter;
    }

    public void SetPageSize(int size)
    {
        if (!PageSizeOptions.Contains(size)) throw new ArgumentOutOfRangeException(nameof(size));
        lock (_gate)
        {
            if (_pageSize == size) return;
            _pageSize = size;
            _state = _state with { PageSize = size, Revision = _state.Revision + 1 };
            _cursors.Clear();
            if (_state.IsOpen && _lease is { } lease) ChangePageLocked(lease);
        }
        Raise();
    }

    public void NextPage() => MovePage(1);
    public void PreviousPage() => MovePage(-1);
    public void FirstPage()
    {
        lock (_gate)
        {
            if (_cursors.Count == 0 || _lease is not { IsCurrent: true } lease) return;
            _cursors.Clear();
            ChangePageLocked(lease);
        }
        Raise();
    }

    private void MovePage(int offset)
    {
        lock (_gate)
        {
            if (!_state.IsOpen || _lease is not { IsCurrent: true } lease) return;
            if (offset > 0)
            {
                if (!_state.CanNext || _state.Window?.NextCursor is not { } next) return;
                _cursors.Add(next);
            }
            else
            {
                var target = Math.Max(0, _cursors.Count + offset);
                if (target == _cursors.Count) return;
                _cursors.RemoveRange(target, _cursors.Count - target);
            }
            ChangePageLocked(lease);
        }
        Raise();
    }

    private void ChangePageLocked(MonitorAuthorityLease lease)
    {
        _historySeq++;
        var oldHistory = _history;
        _history = null;
        CancelLocked(oldHistory);
        _state = _state with
        {
            PageIndex = _cursors.Count, Window = null, Error = null, ErrorKind = null,
            Revision = _state.Revision + 1,
        };
        StartHistoryLocked(lease);
        UpdateDetailLocked();
        StartPollLocked();
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
            if (manual)
            {
                _state = _state with { ManualRefreshPending = true, Revision = _state.Revision + 1 };
                _manual = ManualAsync(read, _time.GetTimestamp(), ++_manualId);
                read = _manual;
            }
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
        bool refresh;
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
            refresh = _state.IsOpen && _state.PageIndex == 0 && _hiddenSinceRead && _time.GetUtcNow() - _lastRead >= RefreshInterval
                && _lease is { IsCurrent: true };
            _hiddenSinceRead = false;
            if (refresh) _ = StartHistoryLocked(_lease!);
        }
        if (refresh) Raise();
    }

    public bool IsPolling { get { lock (_gate) return _poll != null; } }

    private void StartPollLocked()
    {
        _poll?.Dispose();
        _poll = _visible && _state.IsOpen && _state.PageIndex == 0
            ? _time.CreateTimer(_ => OnPoll(), null, RefreshInterval, RefreshInterval) : null;
    }

    private void OnPoll()
    {
        lock (_gate)
        {
            if (_visible && _state.IsOpen && _state.PageIndex == 0 && _lease is { IsCurrent: true } lease)
                _ = StartHistoryLocked(lease);
            else return;
        }
        Raise();
    }

    // ── Reads ────────────────────────────────────────────────

    private async Task ReadCapabilitiesAsync(MonitorAuthorityLease lease)
    {
        await Task.Yield();
        AdminPlaybackHistoryCapabilities? capabilities = null;
        try { capabilities = await _api.GetCapabilitiesAsync(lease).ConfigureAwait(false); }
        catch (Exception) { /* Basic history does not depend on this optional read. */ }
        Task? historyRead = null;
        lock (_gate)
        {
            if (!ReferenceEquals(lease, _lease) || !_lifetime.IsCurrent(lease)) return;
            _capabilitiesRead = null;
            _state = _state with
            {
                SupportsRichFilters = capabilities?.RichFilters == true,
                SupportsSeriesFilter = capabilities?.SeriesFilter == true,
                SupportsSessionDetail = capabilities?.SessionDetail == true,
                Revision = _state.Revision + 1,
            };
            if (_state.SupportsRichFilters && _librariesApi != null) _ = ReadLibrariesAsync(lease);
            if (_filter.HasRichFilters) historyRead = StartHistoryLocked(lease);
            UpdateDetailLocked();
        }
        Raise();
        if (historyRead != null) await historyRead.ConfigureAwait(false);
    }

    /// <summary>Starts the current filter's read or returns the one in flight. Caller holds the gate.</summary>
    private Task StartHistoryLocked(MonitorAuthorityLease lease)
    {
        var filter = _filter;
        if (filter.HasRichFilters && _capabilitiesRead is { } probe)
        {
            _state = _state with { Loading = true, Fetching = true, Revision = _state.Revision + 1 };
            return probe;
        }
        // Never fall back to a broader list for a scoped link. Reset (or removing the scope) can load basic history.
        if (filter != _state.SupportedFilter)
        {
            var messageKey = filter.RequestSeriesId != null && !_state.SupportsSeriesFilter ? "seriesFilterUnavailable" : "richFiltersUnavailable";
            _state = _state with
            {
                Loading = false, Fetching = false, Error = AdminPlaybackAuditText.T("admin.native.playbackAudit." + messageKey),
                Revision = _state.Revision + 1,
            };
            return Task.CompletedTask;
        }
        string key;
        var limit = _pageSize;
        var cursor = _cursors.Count > 0 ? _cursors[^1] : null;
        try { key = AdminPlaybackHistoryApi.HistoryPath(filter, limit, cursor); }
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
        read.Task = RunHistoryAsync(read, lease, filter, limit, cursor);
        return read.Task;
    }

    private async Task RunHistoryAsync(Read read, MonitorAuthorityLease lease, AdminPlaybackHistoryFilter filter, int limit, string? cursor)
    {
        await Task.Yield();
        AdminPlaybackHistoryWindow? window = null;
        Exception? failure = null;
        try { window = await _api.ReadPageAsync(lease, filter, limit, cursor, read.Cts.Token).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            read.Done = true;
            read.Cts.Dispose();
            if (read.Seq != _historySeq || !ReferenceEquals(lease, _lease) || !_lifetime.IsCurrent(lease)) return;
            if (window != null)
            {
                _lastRead = _time.GetUtcNow();
                _state = _state with
                {
                    Window = window, Loading = false, Fetching = false, Error = null, ErrorKind = null,
                    Revision = _state.Revision + 1,
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
            UpdateDetailLocked();
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
        lock (_gate)
        {
            read.Done = true;
            read.Cts.Dispose();
            if (read.Seq != _profileSeq || !ReferenceEquals(lease, _lease) || !_lifetime.IsCurrent(lease) || _filter.RequestUserId != userId) return;
            _state = _state with { Profiles = profiles, ProfilesLoading = false, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    public void RetryDetail()
    {
        lock (_gate)
        {
            if (_state.DetailError == null || _lease is not { IsCurrent: true }) return;
            _detail = null;
            UpdateDetailLocked();
            _state = _state with { Revision = _state.Revision + 1 };
        }
        Raise();
    }

    private void UpdateDetailLocked()
    {
        var id = _filter.SessionId;
        var row = id == null ? null : _state.Rows.FirstOrDefault(item => item.SessionId == id);
        if (id != null && row == null && _detail?.Key == id) return;

        var previous = _detail;
        _detail = null;
        CancelLocked(previous);
        var offPage = id != null && row == null;
        var loading = offPage && (_state.SupportsSessionDetail || _capabilitiesRead != null || _state.Loading);
        _state = _state with { Detail = row, DetailLoading = loading, DetailUnavailable = offPage && !loading, DetailError = null };
        // On a supporting server the link can resolve independently of the page. A matching row always takes precedence.
        if (id == null || row != null || !_state.SupportsSessionDetail || _lease is not { } lease) return;
        var read = new Read(0, id, CancellationTokenSource.CreateLinkedTokenSource(lease.Token));
        _detail = read;
        read.Task = RunDetailAsync(read, lease);
    }

    private async Task RunDetailAsync(Read read, MonitorAuthorityLease lease)
    {
        await Task.Yield();
        AdminPlaybackHistoryRow? row = null;
        string? error = null;
        try { row = await _api.GetEntryAsync(lease, read.Key, read.Cts.Token).ConfigureAwait(false); }
        catch (Exception failure)
        {
            var kind = AdminFailures.Classify(failure, lease);
            error = kind switch
            {
                AdminFailureKind.InvalidResponse => failure.Message,
                _ when failure is ApiException && kind is not AdminFailureKind.Transport and not AdminFailureKind.Unknown => failure.Message,
                _ => AdminPlaybackAuditText.T("web.admin.history.detail.loadFailed"),
            };
        }
        lock (_gate)
        {
            read.Done = true;
            read.Cts.Dispose();
            if (!ReferenceEquals(_detail, read) || !_lifetime.IsCurrent(lease)) return;
            _state = _state with { Detail = row, DetailLoading = false, DetailError = error, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    private async Task ReadLibrariesAsync(MonitorAuthorityLease lease)
    {
        await Task.Yield();
        IReadOnlyList<AdminLibrary> libraries;
        try { libraries = (await _librariesApi!.ListAsync(lease).ConfigureAwait(false)).Items; }
        catch (Exception) { return; } // Optional picker data.
        lock (_gate)
        {
            if (!ReferenceEquals(_lease, lease) || !_lifetime.IsCurrent(lease)) return;
            _state = _state with { Libraries = libraries, Revision = _state.Revision + 1 };
        }
        Raise();
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
        AuthorityRetired = null;
    }
}
