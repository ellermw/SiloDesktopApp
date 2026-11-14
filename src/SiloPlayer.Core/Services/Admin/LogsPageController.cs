using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record LogsPageSnapshot
{
    public MonitorAuthorityLease? Owner { get; init; }
    public long Selection { get; init; }
    public string Stream { get; init; } = "app";
    public LogsQuery Query { get; init; } = new();
    public string Connection { get; init; } = "disconnected";
    public bool Loading { get; init; }
    public bool History { get; init; }
    public bool HistoryStale { get; init; }
    public int Page { get; init; }
    public bool CanNewer { get; init; }
    public string? NextCursor { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<LogsOperationalEntry> AppRows { get; init; } = [];
    public IReadOnlyList<LogsAuditEntry> AuditRows { get; init; } = [];
}

public sealed class LogsPageController : IDisposable
{
    private readonly AdminMonitorApi _api;
    private readonly LogsStreamClient _stream;
    private readonly LogsAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly LogsCursorHistory _history = new();
    private readonly LogsLiveBuffer _app = new();
    private readonly LogsLiveBuffer<LogsAuditEntry> _audit = new();
    private CancellationTokenSource? _attemptCancellation;
    private long _attempt;
    private bool _disposed;
    private LogsPageSnapshot _state = new();
    public LogsPageController(AdminMonitorApi api, AdminMonitorAuthority authority, LogsStreamClient? stream = null)
    {
        _api = api; _stream = stream ?? new(api); _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    public LogsPageSnapshot State { get { lock (_gate) return _state; } }
    public bool IsCurrent(LogsPageSnapshot snapshot) { lock (_gate) return !_disposed && ReferenceEquals(snapshot, _state) && (snapshot.Owner == null || _lifetime.IsCurrent(snapshot.Owner)); }
    public async Task OpenAsync(string stream, LogsQuery query, bool debounce = true)
    {
        try { _lifetime.Open(); await SelectAsync(stream, query, debounce).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
    public Task ReconnectAsync() { var state = State; return SelectAsync(state.Stream, state.Query, false); }

    public async Task SelectAsync(string stream, LogsQuery query, bool debounce = true)
    {
        if (stream is not ("app" or "audit")) throw new ArgumentOutOfRangeException(nameof(stream));
        MonitorAuthorityLease? owner;
        CancellationToken token;
        long attempt;
        lock (_gate)
        {
            if (_disposed) return;
            owner = _lifetime.Owner;
            if (!_lifetime.IsCurrent(owner)) return;
            (attempt, token) = BeginAttempt(owner!);
            _history.Clear(); _app.Clear(); _audit.Clear();
            _state = new() { Owner = owner, Selection = attempt, Stream = stream, Query = query, Connection = "connecting", Loading = true };
        }
        Changed?.Invoke();
        var pending = new List<LogsSocketFrame>();
        var flushScheduled = false;
        long flushVersion = 0;
        try
        {
            if (debounce) await Task.Delay(250, token).ConfigureAwait(false);
            owner!.ThrowIfRetired();
            await _stream.RunAsync(owner, stream, query, frame =>
            {
                lock (_gate)
                {
                    if (!Current(owner, attempt)) return;
                    if (frame.Type == "snapshot")
                    {
                        pending.Clear(); flushScheduled = false; flushVersion++;
                        _app.Snapshot(frame.Entries); _audit.Snapshot(frame.AuditEntries);
                        _state = _state with { Connection = "live", Loading = false, Error = null, NextCursor = frame.NextCursor, AppRows = _app.Rows.ToArray(), AuditRows = _audit.Rows.ToArray() };
                    }
                    else if (frame.Type == "append")
                    {
                        if (_state.Loading) return;
                        if (pending.Count == 200) pending.RemoveAt(0);
                        pending.Add(frame);
                        if (!flushScheduled) { flushScheduled = true; _ = FlushAsync(flushVersion); }
                        return;
                    }
                    else _state = _state with { Error = frame.Message ?? LogsText.T("logs.native.logs.streamFailed") };
                }
                Changed?.Invoke();
            }, token).ConfigureAwait(false);
            Update(owner, attempt, state => state with { Connection = "disconnected", Loading = false });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || owner?.IsCurrent != true)
        {
            if (owner != null) Update(owner, attempt, state => state with { Connection = "disconnected", Loading = false });
        }
        catch (Exception)
        {
            if (owner != null) Update(owner, attempt, state => state with { Connection = "disconnected", Loading = false, Error = LogsText.T("logs.native.logs.connectFailed") });
        }

        async Task FlushAsync(long version)
        {
            try
            {
                await Task.Delay(75, token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!Current(owner!, attempt) || flushVersion != version) return;
                    foreach (var frame in pending)
                    {
                        if (frame.Entry != null) _app.Append(frame.Entry);
                        if (frame.AuditEntry != null) _audit.Append(frame.AuditEntry);
                    }
                    pending.Clear(); flushScheduled = false;
                    _state = _state with { AppRows = _app.Rows.ToArray(), AuditRows = _audit.Rows.ToArray() };
                }
                Changed?.Invoke();
            }
            catch (OperationCanceledException) { }
        }
    }

    public Task BrowseHistoryAsync() { lock (_gate) _history.Start(); return LoadHistoryAsync(); }
    public Task OlderAsync()
    {
        lock (_gate)
        {
            if (_state.Loading || _state.NextCursor == null || _state.Error != null) return Task.CompletedTask;
            try { _history.Older(_state.NextCursor); }
            catch (InvalidDataException error) { _state = _state with { Error = error.Message, HistoryStale = true }; Changed?.Invoke(); return Task.CompletedTask; }
        }
        return LoadHistoryAsync();
    }
    public Task NewerAsync() { lock (_gate) { if (_state.Loading || !_history.CanNewer) return Task.CompletedTask; _history.Newer(); } return LoadHistoryAsync(); }
    public Task RetryHistoryAsync() { lock (_gate) { if (_state.HistoryStale) _history.Start(); } return LoadHistoryAsync(); }

    private async Task LoadHistoryAsync()
    {
        MonitorAuthorityLease? owner;
        CancellationToken token;
        long attempt;
        string? cursor;
        LogsPageSnapshot initial;
        lock (_gate)
        {
            if (_disposed) return;
            owner = _lifetime.Owner;
            if (!_lifetime.IsCurrent(owner)) return;
            (attempt, token) = BeginAttempt(owner!);
            cursor = _history.Cursor;
            _app.Clear(); _audit.Clear();
            initial = _state = _state with { Selection = attempt, Loading = true, History = true, HistoryStale = false, Error = null, Connection = "disconnected", Page = _history.Page, CanNewer = _history.CanNewer, NextCursor = null, AppRows = [], AuditRows = [] };
        }
        Changed?.Invoke();
        try
        {
            if (initial.Stream == "app")
            {
                var page = await _api.LogsGetOperationalAsync(owner!, initial.Query, cursor, 50, token).ConfigureAwait(false);
                Update(owner!, attempt, state => state with { Loading = false, AppRows = page.Items, NextCursor = page.Page!.NextCursor });
            }
            else
            {
                var page = await _api.LogsGetAuditAsync(owner!, initial.Query, cursor, 50, token).ConfigureAwait(false);
                Update(owner!, attempt, state => state with { Loading = false, AuditRows = page.Items, NextCursor = page.Page!.NextCursor });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || owner?.IsCurrent != true) { }
        catch (ApiException error) when (error.StatusCode == 409 || (error.StatusCode == 400 && error.ErrorCode == "invalid_cursor"))
        {
            Update(owner!, attempt, state => state with { Loading = false, HistoryStale = true, Error = LogsText.T("logs.native.logs.cursorStale") });
        }
        catch (Exception) { Update(owner!, attempt, state => state with { Loading = false, Error = LogsText.T("logs.native.logs.historyFailed") }); }
    }

    private (long, CancellationToken) BeginAttempt(MonitorAuthorityLease owner)
    {
        _attemptCancellation?.Cancel(); _attemptCancellation?.Dispose();
        _attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
        return (++_attempt, _attemptCancellation.Token);
    }
    private bool Current(MonitorAuthorityLease owner, long attempt) => !_disposed && attempt == _attempt && _lifetime.IsCurrent(owner);
    private void Update(MonitorAuthorityLease owner, long attempt, Func<LogsPageSnapshot, LogsPageSnapshot> update)
    {
        lock (_gate) { if (!Current(owner, attempt)) return; _state = update(_state); }
        Changed?.Invoke();
    }
    private void Clear()
    {
        lock (_gate)
        {
            _attempt++; _attemptCancellation?.Cancel(); _attemptCancellation?.Dispose(); _attemptCancellation = null;
            _app.Clear(); _audit.Clear(); _history.Clear(); _state = new() { Selection = _attempt };
        }
        Changed?.Invoke();
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose();
        Changed = null;
    }
}
