using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One immutable view of Admin › Playback › Skip Markers (marker edit history).</summary>
public sealed record AdminMarkerHistoryState
{
    public static AdminMarkerHistoryState Closed { get; } = new();

    public bool IsOpen { get; init; }
    public int Limit { get; init; } = AdminMarkerHistoryApi.DefaultLimit;
    /// <summary>The rows for <see cref="Limit"/>; null until that count has loaded.</summary>
    public IReadOnlyList<AdminMarkerEditRow>? Rows { get; init; }
    public bool Loading { get; init; }
    public bool Fetching { get; init; }
    public bool Failed { get; init; }
    public AdminFailureKind? ErrorKind { get; init; }
    public long Revision { get; init; }
}

/// <summary>
/// Admin › Playback › Skip Markers state owner (WebUI <c>AdminMarkerHistory.tsx</c>,
/// <c>useAllMarkerEditHistory</c>, official Silo e61187da). Read-only: a bounded
/// count selector (25/50/100, default 50, server cap 100) and Refresh. No polling
/// (the WebUI hook has none). One read at a time: Refresh while fetching is
/// ignored (the WebUI disables it); a count change cancels the previous read and
/// discards its answer. Authority changes retire and clear every row.
/// </summary>
public sealed class AdminMarkerHistoryController : IDisposable
{
    private readonly AdminMarkerHistoryApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminMarkerHistoryState _state = AdminMarkerHistoryState.Closed;
    private int _limit = AdminMarkerHistoryApi.DefaultLimit;
    private MonitorAuthorityLease? _lease;
    private CancellationTokenSource? _read;
    private Task _readTask = Task.CompletedTask;
    private long _seq;
    private bool _disposed;

    public AdminMarkerHistoryController(AdminMarkerHistoryApi api, AdminMonitorAuthority authority)
    {
        _api = api;
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnAuthorityRetired;
    }

    public event Action? Changed;

    /// <summary>Authority changed while open; marshal and call <see cref="Reopen"/>.</summary>
    public event Action? AuthorityRetired;

    public AdminMarkerHistoryState State { get { lock (_gate) return _state; } }

    /// <summary>Requests started (test seam).</summary>
    public int ReadsStarted { get; private set; }

    public bool Open()
    {
        MonitorAuthorityLease lease;
        try { lease = _lifetime.Open(); }
        catch (OperationCanceledException) { return false; }
        catch (ObjectDisposedException) { return false; }
        lock (_gate)
        {
            CancelLocked();
            _lease = lease;
            _state = new AdminMarkerHistoryState { IsOpen = true, Limit = _limit, Revision = _state.Revision + 1 };
            StartLocked(lease);
        }
        Raise();
        return true;
    }

    public bool Reopen()
    {
        lock (_gate) if (_disposed) return false;
        return Open();
    }

    public void Resume()
    {
        lock (_gate) if (_disposed) return;
        _lifetime.Resume();
    }

    public void Suspend()
    {
        _lifetime.Retire();
        lock (_gate)
        {
            CancelLocked();
            _state = AdminMarkerHistoryState.Closed with { Limit = _limit, Revision = _state.Revision + 1 };
        }
        Raise();
    }

    private void OnAuthorityRetired()
    {
        lock (_gate)
        {
            if (_disposed) return;
            CancelLocked();
            _state = AdminMarkerHistoryState.Closed with { Limit = _limit, Revision = _state.Revision + 1 };
        }
        Raise();
        AuthorityRetired?.Invoke();
    }

    /// <summary>Chooses how many edits to show; an unchanged count does nothing.</summary>
    public void SetLimit(int limit)
    {
        if (!AdminMarkerHistoryApi.LimitOptions.Contains(limit)) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_gate)
        {
            if (limit == _limit) return;
            _limit = limit;
            if (!_state.IsOpen || _lease is not { } lease) { _state = _state with { Limit = limit, Revision = _state.Revision + 1 }; }
            else
            {
                CancelReadLocked();
                _state = _state with { Limit = limit, Rows = null, Failed = false, ErrorKind = null, Revision = _state.Revision + 1 };
                StartLocked(lease);
            }
        }
        Raise();
    }

    /// <summary>Refetches the current count; joins (never duplicates) a read in flight.</summary>
    public Task RefreshAsync()
    {
        Task task;
        lock (_gate)
        {
            if (_disposed || !_state.IsOpen || _lease is not { IsCurrent: true } lease) return Task.CompletedTask;
            if (_state.Fetching) return _readTask;
            task = StartLocked(lease);
        }
        Raise();
        return task;
    }

    private Task StartLocked(MonitorAuthorityLease lease)
    {
        var seq = ++_seq;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
        _read = cts;
        ReadsStarted++;
        _state = _state with { Fetching = true, Loading = _state.Rows == null, Revision = _state.Revision + 1 };
        _readTask = RunAsync(seq, cts, lease, _limit);
        return _readTask;
    }

    private async Task RunAsync(long seq, CancellationTokenSource cts, MonitorAuthorityLease lease, int limit)
    {
        await Task.Yield();
        IReadOnlyList<AdminMarkerEditRow>? rows = null;
        Exception? failure = null;
        try { rows = await _api.ReadAsync(lease, limit, cts.Token).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            cts.Dispose();
            if (ReferenceEquals(_read, cts)) _read = null;
            if (seq != _seq || !ReferenceEquals(lease, _lease) || !_lifetime.IsCurrent(lease)) return;
            if (rows != null)
                _state = _state with { Rows = rows, Loading = false, Fetching = false, Failed = false, ErrorKind = null, Revision = _state.Revision + 1 };
            else
            {
                var kind = AdminFailures.Classify(failure!, lease);
                _state = kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled
                    ? _state with { Loading = false, Fetching = false, Revision = _state.Revision + 1 }
                    : _state with { Loading = false, Fetching = false, Failed = true, ErrorKind = kind, Revision = _state.Revision + 1 };
            }
        }
        Raise();
    }

    private void CancelReadLocked()
    {
        // The sequence advances first: a cancelled read may complete inline on this thread.
        _seq++;
        var read = _read;
        _read = null;
        try { read?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void CancelLocked()
    {
        CancelReadLocked();
        _lease = null;
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelLocked();
            _state = AdminMarkerHistoryState.Closed;
        }
        _lifetime.Retired -= OnAuthorityRetired;
        _lifetime.Dispose();
        Changed = null;
        AuthorityRetired = null;
    }
}

/// <summary>Row text for Skip Markers (WebUI <c>MarkerHistoryRow</c>, <c>formatHistoryRange</c>, <c>formatClock</c>).</summary>
public static class AdminMarkerHistoryPresentation
{
    private static string T(string key, params (string, object?)[] values) => AdminPlaybackAuditText.T(key, values);

    /// <summary>The media title, else the file name, else "File {id}".</summary>
    public static string Title(AdminMarkerEditRow row)
    {
        if (!string.IsNullOrEmpty(row.MediaTitle)) return row.MediaTitle;
        var name = FileName(row.FilePath);
        return name.Length > 0 ? name : T("web.admin.markers.history.fileFallback", ("id", row.MediaFileId));
    }

    public static string ItemLabel(AdminMarkerEditRow row) =>
        string.IsNullOrEmpty(row.ItemType) ? Title(row) : T("web.admin.markers.history.itemWithType", ("title", Title(row)), ("type", row.ItemType));

    public static string FileLine(AdminMarkerEditRow row) =>
        !string.IsNullOrEmpty(row.FilePath) ? row.FilePath : T("web.admin.markers.history.mediaFileFallback", ("id", row.MediaFileId));

    public static string FileName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var index = path.LastIndexOfAny(['/', '\\']);
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>Intro, Recap, Credits / Outro and Preview from the shared catalog; any other kind is shown as the server sent it.</summary>
    public static string SegmentLabel(string segment) => segment switch
    {
        "intro" or "recap" or "credits" or "preview" => T("web.markers.kind." + segment),
        "" => "—",
        _ => segment,
    };

    public static bool IsKnownSegment(string segment) => segment is "intro" or "recap" or "credits" or "preview";

    public static string ActionLabel(AdminMarkerEditRow row) =>
        T(row.Action == "clear" ? "web.admin.markers.history.actionCleared" : "web.admin.markers.history.actionSet");

    public static string Range(AdminMarkerSegment? marker) =>
        marker is not { Start: { } start, End: { } end } ? T("web.admin.markers.history.rangeNone") : $"{Clock(start)}-{Clock(end)}";

    public static string RangeChange(AdminMarkerEditRow row) =>
        T("web.admin.markers.history.rangeChange", ("before", Range(row.Before)), ("after", Range(row.After)));

    /// <summary>h:mm:ss / m:ss, rounding half up as JavaScript <c>Math.round</c>.</summary>
    public static string Clock(double seconds)
    {
        var total = (long)Math.Max(0, Math.Floor(seconds + 0.5));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        return hours > 0
            ? $"{hours.ToString(CultureInfo.InvariantCulture)}:{minutes.ToString("00", CultureInfo.InvariantCulture)}:{secs.ToString("00", CultureInfo.InvariantCulture)}"
            : $"{minutes.ToString(CultureInfo.InvariantCulture)}:{secs.ToString("00", CultureInfo.InvariantCulture)}";
    }

    /// <summary>WebUI uses <c>??</c>: only an absent username is "Unknown user".</summary>
    public static string Actor(AdminMarkerEditRow row) => row.Username ?? T("web.admin.markers.history.unknownUser");

    public static string? ImpersonatedVia(AdminMarkerEditRow row) =>
        string.IsNullOrEmpty(row.ImpersonatorUsername) ? null : T("web.admin.markers.history.impersonatedVia", ("name", row.ImpersonatorUsername));

    public static string Request(AdminMarkerEditRow row) => row.RequestId ?? T("web.admin.markers.history.noRequestId");

    public static string Client(AdminMarkerEditRow row) => row.ClientIp ?? row.UserAgent ?? "";
}
