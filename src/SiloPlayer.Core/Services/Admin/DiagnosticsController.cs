using System.Diagnostics;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record DiagnosticsPageSnapshot
{
    public MonitorAuthorityLease? Owner { get; init; }
    public DiagnosticsQuery Query { get; init; } = new();
    public DiagnosticsCapabilities? Capabilities { get; init; }
    public bool StatusLoading { get; init; }
    public string? StatusError { get; init; }
    public string? SettingsRevision { get; init; }
    public bool UploadsEnabled { get; init; }
    public bool ToggleBusy { get; init; }
    public bool Loading { get; init; }
    public string? Error { get; init; }
    public string? ActionError { get; init; }
    public string? Notice { get; init; }
    public IReadOnlyList<DiagnosticsReportSummary> Reports { get; init; } = [];
    public int Page { get; init; } = 1;
    public bool CanPrevious { get; init; }
    public string? NextCursor { get; init; }
    public string? SelectedId { get; init; }
    public DiagnosticsReportDetail? Detail { get; init; }
    public bool DetailLoading { get; init; }
    public string? DetailError { get; init; }
    public bool Deleting { get; init; }
    public bool Downloading { get; init; }
    public bool CanToggle => Capabilities != null && StatusError == null && SettingsRevision != null && !ToggleBusy;
}

public sealed class DiagnosticsController : IDisposable
{
    private readonly AdminMonitorApi _api;
    private readonly LogsAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly List<string?> _cursors = [null];
    private DiagnosticsPageSnapshot _state = new();
    private long _listSequence;
    private long _detailSequence;
    private long _statusSequence;
    private long _listReadAt;
    private long _statusReadAt;
    private bool _listCursorStale;
    private CancellationTokenSource? _listCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _disposed;
    public DiagnosticsController(AdminMonitorApi api, AdminMonitorAuthority authority)
    {
        _api = api; _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    public DiagnosticsPageSnapshot State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();
    public bool IsActionCurrent(MonitorAuthorityLease action) => !_disposed && _lifetime.IsCurrent(action);
    public bool IsCurrent(DiagnosticsPageSnapshot state) { lock (_gate) return !_disposed && ReferenceEquals(state, _state) && (state.Owner == null || _lifetime.IsCurrent(state.Owner)); }

    public async Task OpenAsync(DiagnosticsQuery? query = null)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate) { _cursors.Clear(); _cursors.Add(null); _state = new() { Owner = owner, Query = (query ?? new()).Apply() }; }
        await Task.WhenAll(LoadStatusAsync(owner), LoadReportsAsync(owner)).ConfigureAwait(false);
    }

    public async Task RefreshAsync(bool force = true)
    {
        var owner = _lifetime.Owner;
        if (owner == null || !_lifetime.IsCurrent(owner)) return;
        lock (_gate)
        {
            if (_listCursorStale)
            {
                _listCursorStale = false;
                _cursors.Clear(); _cursors.Add(null); CloseDetailCore();
            }
        }
        var status = force || _statusReadAt == 0 || Stopwatch.GetElapsedTime(_statusReadAt) >= TimeSpan.FromSeconds(30);
        var reports = force || _listReadAt == 0 || Stopwatch.GetElapsedTime(_listReadAt) >= TimeSpan.FromSeconds(5);
        await Task.WhenAll(status ? LoadStatusAsync(owner) : Task.CompletedTask, reports ? LoadReportsAsync(owner) : Task.CompletedTask).ConfigureAwait(false);
    }

    private async Task LoadStatusAsync(MonitorAuthorityLease owner)
    {
        long sequence;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner) || _state.ToggleBusy) return;
            sequence = ++_statusSequence;
            _state = _state with { StatusLoading = true, StatusError = null };
        }
        Changed?.Invoke();
        try
        {
            var capabilities = await _api.DiagnosticsGetCapabilitiesAsync(owner).ConfigureAwait(false);
            Update(owner, () =>
            {
                if (sequence != _statusSequence) return;
                _statusReadAt = Stopwatch.GetTimestamp();
                _state = _state with { Capabilities = capabilities, UploadsEnabled = capabilities.Status != "disabled", StatusLoading = false };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception) { Update(owner, () => { if (sequence == _statusSequence) _state = _state with { StatusLoading = false, StatusError = LogsText.T("logs.native.diagnostics.statusFailed") }; }); }
        try
        {
            var settings = await _api.LoggingGetSettingsAsync(owner).ConfigureAwait(false);
            Update(owner, () => { if (sequence == _statusSequence) _state = _state with { SettingsRevision = settings.ETag }; });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception) { Update(owner, () => { if (sequence == _statusSequence) _state = _state with { SettingsRevision = null, StatusError = LogsText.T("logs.native.diagnostics.revisionFailed") }; }); }
    }

    public Task ApplyAsync(DiagnosticsQuery draft)
    {
        var query = draft.Apply();
        var owner = _lifetime.Owner;
        if (owner == null) return Task.CompletedTask;
        Update(owner, () => { _listCursorStale = false; _cursors.Clear(); _cursors.Add(null); CloseDetailCore(); _state = _state with { Query = query, Reports = [], Page = 1, CanPrevious = false, NextCursor = null }; });
        return LoadReportsAsync(owner);
    }
    public Task ClearFiltersAsync() => ApplyAsync(new());
    public Task NextAsync()
    {
        var owner = _lifetime.Owner;
        lock (_gate)
        {
            if (owner == null || !_lifetime.IsCurrent(owner) || _state.Loading || _state.NextCursor == null) return Task.CompletedTask;
            if (_cursors.Count >= 100 || _cursors.Contains(_state.NextCursor, StringComparer.Ordinal))
            { _listCursorStale = true; _state = _state with { Error = LogsText.T("logs.native.diagnostics.cursorRepeated"), NextCursor = null }; Changed?.Invoke(); return Task.CompletedTask; }
            _cursors.Add(_state.NextCursor); CloseDetailCore();
        }
        return LoadReportsAsync(owner);
    }
    public Task PreviousAsync()
    {
        var owner = _lifetime.Owner;
        lock (_gate)
        {
            if (owner == null || !_lifetime.IsCurrent(owner) || _state.Loading || _cursors.Count <= 1) return Task.CompletedTask;
            _cursors.RemoveAt(_cursors.Count - 1); CloseDetailCore();
        }
        return LoadReportsAsync(owner);
    }

    private async Task LoadReportsAsync(MonitorAuthorityLease owner)
    {
        long sequence;
        CancellationToken token;
        DiagnosticsQuery query;
        string? cursor;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            sequence = ++_listSequence; query = _state.Query; cursor = _cursors.LastOrDefault();
            _listCancellation?.Cancel(); _listCancellation?.Dispose();
            _listCancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); token = _listCancellation.Token;
            _state = _state with { Loading = true, Reports = [], Error = null, Page = _cursors.Count, CanPrevious = _cursors.Count > 1, NextCursor = null };
        }
        Changed?.Invoke();
        try
        {
            var page = await _api.DiagnosticsListReportsAsync(owner, query, cursor, token).ConfigureAwait(false);
            Update(owner, () =>
            {
                if (sequence != _listSequence) return;
                _listReadAt = Stopwatch.GetTimestamp();
                _state = _state with { Reports = page.Items, NextCursor = page.Page!.NextCursor, Loading = false };
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !owner.IsCurrent) { }
        catch (Exception error)
        {
            Update(owner, () =>
            {
                if (sequence != _listSequence) return;
                _listCursorStale = error is ApiException apiError && (apiError.StatusCode == 409 || (apiError.StatusCode == 400 && apiError.ErrorCode == "invalid_cursor"));
                _state = _state with { Loading = false, Error = _listCursorStale ? LogsText.T("logs.native.diagnostics.cursorStale") : LogsText.T("logs.native.diagnostics.listFailed") };
            });
        }
    }

    public async Task SelectAsync(string id)
    {
        var owner = _lifetime.Owner;
        long sequence;
        CancellationToken token;
        lock (_gate)
        {
            if (owner == null || !_lifetime.IsCurrent(owner) || _disposed) return;
            CloseDetailCore(); sequence = _detailSequence;
            _detailCancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); token = _detailCancellation.Token;
            _state = _state with { SelectedId = id, DetailLoading = true };
        }
        Changed?.Invoke();
        try
        {
            var detail = await _api.DiagnosticsGetReportAsync(owner, id, token).ConfigureAwait(false);
            Update(owner, () => { if (sequence == _detailSequence && _state.SelectedId == id) _state = _state with { Detail = detail, DetailLoading = false }; });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !owner.IsCurrent) { }
        catch (Exception) { Update(owner, () => { if (sequence == _detailSequence) _state = _state with { DetailLoading = false, DetailError = LogsText.T("logs.native.diagnostics.detailFailed") }; }); }
    }
    public void CloseDetail()
    {
        var owner = _lifetime.Owner;
        if (owner != null) Update(owner, CloseDetailCore);
    }
    private void CloseDetailCore()
    {
        _detailSequence++; _detailCancellation?.Cancel(); _detailCancellation?.Dispose(); _detailCancellation = null;
        _state = _state with { SelectedId = null, Detail = null, DetailLoading = false, DetailError = null, ActionError = null };
    }

    public async Task SetUploadsEnabledAsync(MonitorAuthorityLease action, bool enabled)
    {
        string etag;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(action) || !_state.CanToggle) return;
            etag = _state.SettingsRevision!; _statusSequence++;
            _state = _state with { ToggleBusy = true, Notice = null, ActionError = null };
        }
        Changed?.Invoke();
        try
        {
            await _api.DiagnosticsSetUploadsEnabledAsync(action, enabled, etag).ConfigureAwait(false);
            Update(action, () => _state = _state with { SettingsRevision = null });
            var capabilities = await _api.DiagnosticsGetCapabilitiesAsync(action).ConfigureAwait(false);
            var settings = await _api.LoggingGetSettingsAsync(action).ConfigureAwait(false);
            Update(action, () => { _statusReadAt = Stopwatch.GetTimestamp(); _state = _state with { Capabilities = capabilities, UploadsEnabled = capabilities.Status != "disabled", SettingsRevision = settings.ETag, ToggleBusy = false, Notice = enabled ? LogsText.T("logs.native.diagnostics.uploadsEnabled") : LogsText.T("logs.native.diagnostics.uploadsDisabled") }; });
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { }
        catch (LoggingSettingsConflictException) { Update(action, () => _state = _state with { ToggleBusy = false, SettingsRevision = null, ActionError = LogsText.T("logs.native.diagnostics.toggleConflict") }); }
        catch (Exception) { Update(action, () => _state = _state with { ToggleBusy = false, ActionError = LogsText.T("logs.native.diagnostics.toggleFailed") }); }
    }

    public async Task DeleteAsync(MonitorAuthorityLease action, string id)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(action) || _state.Deleting || _state.SelectedId != id) return;
            _state = _state with { Deleting = true, ActionError = null, Notice = null };
        }
        Changed?.Invoke();
        try
        {
            await _api.DiagnosticsDeleteReportAsync(action, id).ConfigureAwait(false);
            Update(action, () => { if (_state.SelectedId == id) CloseDetailCore(); _state = _state with { Deleting = false, Reports = _state.Reports.Where(report => report.Id != id).ToArray(), Notice = LogsText.T("logs.native.diagnostics.deleted") }; });
            if (_lifetime.IsCurrent(action)) await LoadReportsAsync(action).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { }
        catch (Exception) { Update(action, () => _state = _state with { Deleting = false, ActionError = LogsText.T("logs.native.diagnostics.deleteFailed") }); }
    }

    public async Task<bool> DownloadAsync(MonitorAuthorityLease action, DiagnosticsReportSummary report, Stream destination, CancellationToken ct = default)
    {
        long limit;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(action) || _state.Downloading || report.State != "ready") return false;
            limit = Math.Clamp(_state.Capabilities?.MaxBundleBytes ?? 256L * 1024 * 1024, 3, 256L * 1024 * 1024);
            _state = _state with { Downloading = true, ActionError = null };
        }
        Changed?.Invoke();
        try
        {
            await _api.DiagnosticsDownloadReportAsync(action, report.Id, destination, limit, ct).ConfigureAwait(false);
            action.ThrowIfRetired();
            return _lifetime.IsCurrent(action);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !action.IsCurrent) { return false; }
        catch (Exception error)
        {
            Update(action, () => _state = _state with { ActionError = error is ApiException apiError ? apiError.StatusCode switch { 409 => LogsText.T("logs.native.diagnostics.downloadNotReady"), 404 => LogsText.T("logs.native.diagnostics.reportMissing"), 503 => LogsText.T("logs.native.diagnostics.storageUnavailable"), _ => LogsText.T("logs.native.diagnostics.downloadFailed") } : error is InvalidDataException ? error.Message : LogsText.T("logs.native.diagnostics.downloadFailed") });
            return false;
        }
        finally { Update(action, () => _state = _state with { Downloading = false }); }
    }

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate) { if (_disposed || !_lifetime.IsCurrent(owner)) return; update(); }
        Changed?.Invoke();
    }
    private void Clear()
    {
        lock (_gate)
        {
            _listSequence++; _statusSequence++; CloseDetailCore();
            _listCancellation?.Cancel(); _listCancellation?.Dispose(); _listCancellation = null;
            _cursors.Clear(); _cursors.Add(null); _listCursorStale = false; _listReadAt = 0; _statusReadAt = 0; _state = new();
        }
        Changed?.Invoke();
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Changed = null;
    }
}
