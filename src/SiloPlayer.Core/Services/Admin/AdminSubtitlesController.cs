using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Immutable state of the Subtitle Files list for one render.</summary>
public sealed record AdminSubtitlesSnapshot
{
    public MonitorAuthorityLease? Owner { get; init; }
    public AdminSubtitleListQuery Query { get; init; } = new();
    /// <summary>A page request is in flight; its rows are not shown until it settles.</summary>
    public bool Loading { get; init; }
    /// <summary>Whether this owner has settled at least one page (the whole-page skeleton shows until then).</summary>
    public bool Loaded { get; init; }
    public string? Error { get; init; }
    public AdminFailureKind? ErrorKind { get; init; }
    public IReadOnlyList<AdminStoredSubtitle> Items { get; init; } = [];
    public int Total { get; init; }
    public int Uploads { get; init; }
    public int ProviderDownloads { get; init; }
    public int PageIndex { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanNext { get; init; }
    public IReadOnlyList<AdminSubtitleUploader> Uploaders { get; init; } = [];
    public bool UploadersLoading { get; init; }
    public bool UploadersTruncated { get; init; }
    public string? UploadersError { get; init; }
    /// <summary>The row whose bytes are being fetched or published; every download is disabled meanwhile.</summary>
    public string? DownloadingId { get; init; }

    /// <summary>Distinct languages among the shown rows (WebUI "Languages on page").</summary>
    public int LanguagesOnPage => Items.Select(item => item.Language).Distinct(StringComparer.Ordinal).Count();
    /// <summary>WebUI shows the summary/pager row when anything matches or the user has paged on.</summary>
    public bool ShowPagination => !Loading && (Total > 0 || PageIndex > 0);
}

/// <summary>
/// Libraries › Subtitle Files list owner (WebUI <c>pages/AdminSubtitles.tsx</c>).
/// Filters (provider, language, uploader, trimmed <c>q</c>) and the page size
/// scope the cursor history; any change restarts at the first page before the
/// next request is built. One list request is live at a time; superseded,
/// cancelled and retired responses are discarded. Edit, delete and download
/// actions run under action leases bound to the current owner.
/// </summary>
public sealed class AdminSubtitlesController : IDisposable
{
    private readonly AdminSubtitlesApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly AdminCursorPager _pager = new(maxDepth: 1000);
    private AdminSubtitlesSnapshot _state = new();
    private AdminCursorPosition? _shownPosition;
    private string? _nextCursor;
    private bool _restartAtFirstPage;
    private long _listSequence;
    private long _uploaderSequence;
    private CancellationTokenSource? _listCancellation;
    private CancellationTokenSource? _uploaderCancellation;
    private bool _disposed;

    public AdminSubtitlesController(AdminSubtitlesApi api, AdminMonitorAuthority authority)
    {
        _api = api;
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnRetired;
    }

    /// <summary>Raised on any thread after a state change; marshal and read <see cref="State"/>.</summary>
    public event Action? Changed;

    /// <summary>Raised (on any thread) after authority retired and data cleared; reopen when admin is still available.</summary>
    public event Action? Retired;

    public AdminSubtitlesSnapshot State { get { lock (_gate) return _state; } }

    public AdminSubtitlesApi Api => _api;

    public bool IsCurrent(AdminSubtitlesSnapshot state)
    {
        lock (_gate) return !_disposed && ReferenceEquals(state, _state) && (state.Owner == null || _lifetime.IsCurrent(state.Owner));
    }

    /// <summary>A lease for one confirmation, write or file publication; it dies with the current owner.</summary>
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();

    public bool IsActionCurrent(MonitorAuthorityLease? action) => !_disposed && _lifetime.IsCurrent(action);

    /// <summary>Opens the list under a new owner with the URL's filters and reads the first page and the uploader choices.</summary>
    public async Task OpenAsync(AdminSubtitleListQuery query)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            if (_disposed) return;
            var normalized = query.Normalize();
            _pager.SetScope(ScopeOf(owner, normalized));
            _shownPosition = null; _nextCursor = null; _restartAtFirstPage = false;
            _state = new AdminSubtitlesSnapshot { Owner = owner, Query = normalized };
        }
        Changed?.Invoke();
        await Task.WhenAll(LoadPageAsync(owner), LoadUploadersAsync(owner)).ConfigureAwait(false);
    }

    /// <summary>Explicit activation after <see cref="Suspend"/>.</summary>
    public void Resume() => _lifetime.Resume();

    /// <summary>Outer Admin departure: retire the owner and clear private data without requesting a reopen.</summary>
    public void Suspend()
    {
        _lifetime.Retire();
        Clear();
    }

    /// <summary>Applies new filters or page size; the cursor history restarts synchronously.</summary>
    public Task SetQueryAsync(AdminSubtitleListQuery query)
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return Task.CompletedTask;
            var normalized = query.Normalize();
            if (normalized == _state.Query) return Task.CompletedTask;
            _state = _state with { Query = normalized };
            if (_pager.SetScope(ScopeOf(owner, normalized))) _restartAtFirstPage = false;
        }
        return LoadPageAsync(owner);
    }

    public Task NextAsync()
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner) || _state.Loading || _shownPosition == null) return Task.CompletedTask;
            if (_pager.TryAdvance(_shownPosition, _nextCursor)) owner = _state.Owner;
            else
            {
                // A repeated, missing or oversized continuation (or the depth cap) ends paging here.
                _nextCursor = null;
                _state = _state with { CanNext = false };
                owner = null;
            }
        }
        if (owner == null) { Changed?.Invoke(); return Task.CompletedTask; }
        return LoadPageAsync(owner);
    }

    public Task PreviousAsync()
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner) || _state.Loading || !_pager.Back()) return Task.CompletedTask;
        }
        return LoadPageAsync(owner);
    }

    /// <summary>
    /// Re-reads the shown page (Retry, or after a confirmed save/delete). A
    /// stale or inconsistent cursor restarts at the first page; nothing is
    /// replayed, and uploaders reload only when they failed.
    /// </summary>
    public Task ReloadAsync()
    {
        MonitorAuthorityLease? owner;
        bool uploaders;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return Task.CompletedTask;
            if (_restartAtFirstPage) { while (_pager.Back()) { } _restartAtFirstPage = false; }
            uploaders = _state.UploadersError != null && !_state.UploadersLoading;
        }
        return Task.WhenAll(LoadPageAsync(owner), uploaders ? LoadUploadersAsync(owner) : Task.CompletedTask);
    }

    private async Task LoadPageAsync(MonitorAuthorityLease owner)
    {
        long sequence;
        CancellationToken token;
        AdminCursorPosition position;
        AdminSubtitleListQuery query;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            query = _state.Query;
            _pager.SetScope(ScopeOf(owner, query));
            position = _pager.Capture();
            sequence = ++_listSequence;
            _listCancellation?.Cancel(); _listCancellation?.Dispose();
            _listCancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            token = _listCancellation.Token;
            _shownPosition = null; _nextCursor = null;
            _state = _state with
            {
                Loading = true, Error = null, ErrorKind = null, Items = [], PageIndex = _pager.PageIndex,
                CanPrevious = false, CanNext = false,
            };
        }
        Changed?.Invoke();
        try
        {
            var page = await _api.ListAsync(owner, query, position.Cursor, token).ConfigureAwait(false);
            Update(owner, () =>
            {
                if (sequence != _listSequence || _pager.Capture() != position) return;
                _shownPosition = position;
                _nextCursor = page.Page!.HasMore ? page.Page.NextCursor : null;
                _state = _state with
                {
                    Loading = false, Loaded = true, Items = page.Items.AsReadOnly(), Total = page.Total, Uploads = page.Uploads,
                    ProviderDownloads = page.ProviderDownloads, PageIndex = _pager.PageIndex, CanPrevious = _pager.CanGoBack, CanNext = _nextCursor != null,
                };
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !owner.IsCurrent) { }
        catch (Exception error)
        {
            var kind = AdminFailures.Classify(error, owner);
            if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
            Update(owner, () =>
            {
                if (sequence != _listSequence) return;
                _restartAtFirstPage = position.Cursor != null && (error is AdminListInconsistentException ||
                    error is ApiException { ErrorCode: "invalid_cursor" } || error is ApiException { ProblemType: "invalid_cursor" });
                _state = _state with
                {
                    Loading = false, Loaded = true, Items = [], Total = 0, Uploads = 0, ProviderDownloads = 0,
                    Error = AdminSubtitlesFailures.Describe(error, "web.admin.errors.subtitleListInvalid"), ErrorKind = kind,
                    PageIndex = _pager.PageIndex, CanPrevious = _pager.CanGoBack, CanNext = false,
                };
            });
        }
    }

    private async Task LoadUploadersAsync(MonitorAuthorityLease owner)
    {
        long sequence;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            sequence = ++_uploaderSequence;
            _uploaderCancellation?.Cancel(); _uploaderCancellation?.Dispose();
            _uploaderCancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            token = _uploaderCancellation.Token;
            _state = _state with { UploadersLoading = true, UploadersError = null };
        }
        Changed?.Invoke();
        try
        {
            var read = await _api.ListUploadersAsync(owner, token).ConfigureAwait(false);
            Update(owner, () =>
            {
                if (sequence != _uploaderSequence) return;
                _state = _state with { Uploaders = read.Items, UploadersTruncated = !read.IsComplete, UploadersLoading = false };
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !owner.IsCurrent) { }
        catch (Exception error)
        {
            var kind = AdminFailures.Classify(error, owner);
            if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
            Update(owner, () =>
            {
                if (sequence == _uploaderSequence)
                    _state = _state with { Uploaders = [], UploadersTruncated = false, UploadersLoading = false, UploadersError = AdminSubtitlesText.T("admin.native.subtitles.uploadersFailed") };
            });
        }
    }

    /// <summary>Starts an editor for one row under a new action lease (WebUI <c>captureAdminSubtitleEditIntent</c>).</summary>
    public AdminSubtitleEditorSession BeginEdit(AdminStoredSubtitle row) => new(this, _api, CaptureAction(), row);

    /// <summary>Starts a delete confirmation for one row under a new action lease.</summary>
    public AdminSubtitleDeletionSession BeginDelete(AdminStoredSubtitle row) => new(_api, CaptureAction(), row, this);

    /// <summary>
    /// Fetches the complete bounded attachment, then publishes it to
    /// <paramref name="destinationPath"/> atomically, rechecking the action's
    /// authority before the fetch, after it, before the file is moved into
    /// place and after (a late retirement rolls the file back). Nothing is
    /// written to disk until the bytes are complete and validated.
    /// </summary>
    public async Task<AdminSubtitleDownloadOutcome> DownloadAsync(MonitorAuthorityLease action, AdminStoredSubtitle row, string destinationPath, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(action) || _state.DownloadingId != null) return AdminSubtitleDownloadOutcome.Ignored;
            _state = _state with { DownloadingId = row.Id };
        }
        Changed?.Invoke();
        try
        {
            var bytes = await _api.DownloadAsync(action, row, ct).ConfigureAwait(false);
            action.ThrowIfRetired();
            if (!_lifetime.IsCurrent(action)) return AdminSubtitleDownloadOutcome.Ignored;
            var saved = await DiagnosticsFileTransfer.SaveAsync(action, destinationPath, async (stream, token) =>
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                return true;
            }, ct).ConfigureAwait(false);
            return saved && _lifetime.IsCurrent(action) ? AdminSubtitleDownloadOutcome.Saved : AdminSubtitleDownloadOutcome.Ignored;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !action.IsCurrent) { return AdminSubtitleDownloadOutcome.Ignored; }
        catch (Exception error)
        {
            if (!_lifetime.IsCurrent(action)) return AdminSubtitleDownloadOutcome.Ignored;
            return AdminSubtitleDownloadOutcome.Failed(AdminSubtitlesFailures.DescribeDownload(error));
        }
        finally
        {
            lock (_gate) { if (_state.DownloadingId == row.Id) _state = _state with { DownloadingId = null }; }
            Changed?.Invoke();
        }
    }

    /// <summary>A confirmed outcome to show as a toast (saved, deleted). Raised on any thread; never for a retired owner.</summary>
    public event Action<string, bool>? Notice;

    internal void Announce(string message, bool error)
    {
        if (!_disposed) Notice?.Invoke(message, error);
    }

    /// <summary>Re-reads the shown page after a confirmed write (WebUI invalidates the list query).</summary>
    internal void AfterMutation(MonitorAuthorityLease action)
    {
        if (_lifetime.IsCurrent(action)) _ = ReloadAsync();
    }

    private static string ScopeOf(MonitorAuthorityLease owner, AdminSubtitleListQuery query) =>
        AdminCursorPager.ScopeOf(owner, query.PageSize, query.Provider, query.Language, query.UserId, query.Search);

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
        }
        Changed?.Invoke();
    }

    private void OnRetired()
    {
        Clear();
        Retired?.Invoke();
    }

    private void Clear()
    {
        lock (_gate)
        {
            _listSequence++; _uploaderSequence++;
            _listCancellation?.Cancel(); _listCancellation?.Dispose(); _listCancellation = null;
            _uploaderCancellation?.Cancel(); _uploaderCancellation?.Dispose(); _uploaderCancellation = null;
            _shownPosition = null; _nextCursor = null; _restartAtFirstPage = false;
            _state = new AdminSubtitlesSnapshot { Query = _state.Query };
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose();
        lock (_gate)
        {
            _listCancellation?.Cancel(); _listCancellation?.Dispose(); _listCancellation = null;
            _uploaderCancellation?.Cancel(); _uploaderCancellation?.Dispose(); _uploaderCancellation = null;
            _state = new AdminSubtitlesSnapshot();
        }
        Changed = null; Retired = null; Notice = null;
    }
}

/// <summary>What a download attempt produced. <see cref="Ignored"/> covers cancellation, retirement and a busy download.</summary>
public sealed record AdminSubtitleDownloadOutcome(bool Success, string? Error)
{
    public static readonly AdminSubtitleDownloadOutcome Saved = new(true, null);
    public static readonly AdminSubtitleDownloadOutcome Ignored = new(false, null);
    public static AdminSubtitleDownloadOutcome Failed(string error) => new(false, error);
}

/// <summary>Display text for failed subtitle reads and writes (WebUI shows the error's own message).</summary>
public static class AdminSubtitlesFailures
{
    public static string Describe(Exception error, string fallbackKey) => error switch
    {
        AdminSubtitleIdentityException => AdminSubtitlesText.T("web.admin.errors.subtitleIdentityChanged"),
        AdminSubtitleValidatorException => AdminSubtitlesText.T("web.admin.errors.subtitleValidatorRequired"),
        AdminListInconsistentException => AdminSubtitlesText.T("web.admin.errors.subtitleListInvalid"),
        MonitorAuthorityRetiredException => AdminSubtitlesText.T("admin.native.subtitles.retired"),
        ApiException api => ApiProblemText.Display(api),
        OperationCanceledException { InnerException: TimeoutException } => AppStrings.Get("error.request_timeout", fallback: AdminSubtitlesText.T(fallbackKey)),
        HttpRequestException or IOException => AdminSubtitlesText.T("admin.native.subtitles.transport"),
        _ => AdminSubtitlesText.T(fallbackKey),
    };

    public static string DescribeDownload(Exception error) => error switch
    {
        AdminSubtitleDownloadException { Problem: AdminSubtitleDownloadProblem.TooLarge } =>
            AdminSubtitlesText.T("admin.native.subtitles.downloadTooLarge", ("megabytes", AdminSubtitlesApi.MaxDownloadBytes / (1024 * 1024))),
        AdminSubtitleDownloadException => AdminSubtitlesText.T("web.admin.errors.subtitleDownloadUnexpected"),
        ApiException api => AdminSubtitlesText.T("web.admin.errors.subtitleDownloadFailed", ("status", api.StatusCode)),
        _ => AdminSubtitlesText.T("web.admin.common.subtitles.table.downloadFailed"),
    };
}
