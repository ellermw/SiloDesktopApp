using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The server folder browser's state for one render (WebUI <c>FolderBrowser</c>).</summary>
public sealed record AdminFolderBrowserState
{
    /// <summary>The folder requested; <see cref="ResolvedPath"/> is what the server answered with.</summary>
    public string CurrentPath { get; init; } = AdminFolderBrowserSession.RootPath;
    public string ResolvedPath { get; init; } = AdminFolderBrowserSession.RootPath;
    public string Parent { get; init; } = AdminFolderBrowserSession.RootPath;
    public IReadOnlyList<AdminFilesystemEntry> Entries { get; init; } = [];
    public bool Loading { get; init; } = true;
    public bool Fetching { get; init; }
    public bool LoadingMore { get; init; }
    public bool HasMore { get; init; }
    public bool Capped { get; init; }
    public string? Error { get; init; }
    public string? ValidationError { get; init; }
    public IReadOnlyList<string> Selected { get; init; } = [];
    public IReadOnlyList<string> ExistingPaths { get; init; } = [];

    public bool CanGoUp => Parent != ResolvedPath;
    public bool AlreadyAdded => ExistingPaths.Contains(ResolvedPath);
    public int AddableSelected => Selected.Count(path => !ExistingPaths.Contains(path));
}

/// <summary>
/// Read-only server directory browsing for the library editor. Each folder is one
/// bounded page (50 entries); "Load more" appends pages up to <see cref="MaxPages"/>
/// (a truthful cap message beyond that). Typing an absolute path browses there after a
/// 200 ms pause; Browse validates and navigates explicitly. Late answers for an earlier
/// folder are discarded. Nothing here reads or changes local Windows folders.
/// </summary>
public sealed class AdminFolderBrowserSession : IDisposable
{
    public const string RootPath = "/";
    public const int MaxPages = 10;

    private readonly AdminLibrariesApi _api;
    private readonly AdminLibrariesController _owner;
    private readonly MonitorAuthorityLease _lease;
    private readonly object _gate = new();
    private AdminFolderBrowserState _state;
    private string? _nextCursor;
    private int _pages;
    private long _sequence;
    private CancellationTokenSource? _load;
    private CancellationTokenSource? _probe;
    private long _probeSequence;
    private bool _probeNavigating;
    private bool _disposed;

    public AdminFolderBrowserSession(AdminLibrariesController owner, MonitorAuthorityLease lease, IEnumerable<string> existingPaths)
    {
        _owner = owner; _api = owner.Api; _lease = lease;
        _state = new AdminFolderBrowserState { ExistingPaths = existingPaths.Where(path => path.Trim().Length > 0).ToArray() };
    }

    public event Action? Changed;
    public AdminFolderBrowserState State { get { lock (_gate) return _state; } }

    public Task OpenAsync() => NavigateAsync(RootPath);

    /// <summary>Shows a folder's first page (Up, an entry, a resolved draft).</summary>
    public Task NavigateAsync(string path)
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            _state = _state with { CurrentPath = path, ValidationError = null };
        }
        return LoadAsync(append: false);
    }

    public Task RefreshAsync() => LoadAsync(append: false);

    public Task LoadMoreAsync()
    {
        lock (_gate) { if (_disposed || _nextCursor == null || _state.LoadingMore) return Task.CompletedTask; }
        return LoadAsync(append: true);
    }

    private async Task LoadAsync(bool append, long? probeSequence = null)
    {
        long sequence;
        string path;
        string? cursor;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            if (probeSequence.HasValue)
            {
                if (probeSequence != _probeSequence || _probe == null) return;
            }
            else InvalidateProbeLocked();
            _probeNavigating = probeSequence.HasValue;
            if (append && _pages >= MaxPages) { _state = _state with { Capped = true, HasMore = false }; return; }
            sequence = ++_sequence;
            path = _state.CurrentPath;
            cursor = append ? _nextCursor : null;
            _load?.Cancel(); _load?.Dispose();
            _load = CancellationTokenSource.CreateLinkedTokenSource(_lease.Token, probeSequence.HasValue ? _probe!.Token : _lease.Token);
            token = _load.Token;
            _state = append ? _state with { LoadingMore = true } : _state with { Fetching = true, Loading = _state.Entries.Count == 0 || _state.ResolvedPath != path, Error = null };
        }
        Changed?.Invoke();
        try
        {
            var page = await _api.BrowseAsync(_lease, path, null, cursor, AdminLibrariesApi.BrowsePageSize, token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || sequence != _sequence || token.IsCancellationRequested || !_owner.IsActionCurrent(_lease)) return;
                _pages = append ? _pages + 1 : 1;
                _nextCursor = page.NextCursor;
                var capped = page.NextCursor != null && _pages >= MaxPages;
                _state = _state with
                {
                    ResolvedPath = page.Path.Length > 0 ? page.Path : path, Parent = page.Parent.Length > 0 ? page.Parent : path,
                    Entries = append ? _state.Entries.Concat(page.Entries.Where(entry => _state.Entries.All(existing => existing.Path != entry.Path))).ToArray() : page.Entries,
                    HasMore = page.NextCursor != null && !capped, Capped = capped, Loading = false, Fetching = false, LoadingMore = false, Error = null,
                };
            }
        }
        catch (Exception error)
        {
            if (token.IsCancellationRequested || AdminFailures.Classify(error, _lease) is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
            lock (_gate)
            {
                if (_disposed || sequence != _sequence) return;
                _state = _state with
                {
                    Loading = false, Fetching = false, LoadingMore = false,
                    Error = AdminLibraryPresentation.Describe(error, "web.admin.common.folderBrowser.browseFailed"),
                };
            }
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Browse (or Enter in the path box): an absolute path is resolved with a
    /// one-entry read first, then shown; a failure stays in the dialog as validation.
    /// </summary>
    public Task<string?> SubmitDraftAsync(string draft) => ProbeDraftAsync(draft, submit: true);

    public Task<string?> TryFollowDraftAsync(string draft) => ProbeDraftAsync(draft, submit: false);

    private async Task<string?> ProbeDraftAsync(string draft, bool submit)
    {
        var trimmed = draft.Trim();
        long sequence;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || !_owner.IsActionCurrent(_lease)) return null;
            InvalidateProbeLocked();
            sequence = _probeSequence;
            if (!trimmed.StartsWith(RootPath, StringComparison.Ordinal))
            {
                if (submit) _state = _state with { ValidationError = AdminLibrariesText.T("web.admin.common.folderBrowser.absolutePath") };
                token = default;
            }
            else
            {
                if (!submit && trimmed == _state.CurrentPath) return null;
                _probe = CancellationTokenSource.CreateLinkedTokenSource(_lease.Token);
                token = _probe.Token;
            }
        }
        if (!token.CanBeCanceled) { Changed?.Invoke(); return null; }
        try
        {
            var probe = await _api.BrowseAsync(_lease, trimmed, null, null, 1, token).ConfigureAwait(false);
            Task navigation;
            lock (_gate)
            {
                if (_disposed || sequence != _probeSequence || token.IsCancellationRequested || !_owner.IsActionCurrent(_lease)) return null;
                if (!submit && probe.Path == _state.CurrentPath) return null;
                _state = _state with { CurrentPath = probe.Path, ValidationError = null };
                navigation = LoadAsync(append: false, sequence);
            }
            await navigation.ConfigureAwait(false);
            lock (_gate) return !_disposed && sequence == _probeSequence && !token.IsCancellationRequested && _owner.IsActionCurrent(_lease) ? probe.Path : null;
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (!submit || _disposed || sequence != _probeSequence || token.IsCancellationRequested || !_owner.IsActionCurrent(_lease)) return null;
                _state = _state with { ValidationError = AdminLibraryPresentation.Describe(error, "web.admin.common.folderBrowser.browseFailed") };
            }
            Changed?.Invoke();
            return null;
        }
    }

    public void DraftChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;
            InvalidateProbeLocked();
            _state = _state with { ValidationError = null };
        }
        Changed?.Invoke();
    }

    public void ClearValidation() => DraftChanged();

    private void InvalidateProbeLocked()
    {
        _probeSequence++;
        _probe?.Cancel(); _probe?.Dispose(); _probe = null;
        if (!_probeNavigating) return;
        _sequence++;
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _probeNavigating = false;
        _state = _state with { Fetching = false, Loading = false, LoadingMore = false };
    }

    public void ToggleSelected(string path)
    {
        lock (_gate)
        {
            if (_state.ExistingPaths.Contains(path)) return;
            var selected = _state.Selected.Contains(path) ? _state.Selected.Where(entry => entry != path).ToArray() : _state.Selected.Append(path).Take(AdminLibrariesApi.MaxPaths).ToArray();
            _state = _state with { Selected = selected };
        }
        Changed?.Invoke();
    }

    /// <summary>The folders "Add N folders" (or "Use current folder") would add; empty when nothing is addable.</summary>
    public IReadOnlyList<string> Selection()
    {
        var state = State;
        if (state.AddableSelected > 0) return state.Selected.Where(path => !state.ExistingPaths.Contains(path)).ToArray();
        return state.AlreadyAdded ? [] : [state.ResolvedPath];
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; InvalidateProbeLocked(); _load?.Cancel(); _load?.Dispose(); _load = null; }
        Changed = null;
    }
}

/// <summary>
/// Inline path completion for the editor's folder fields (WebUI <c>PathAutocompleteInput</c>):
/// browse the typed path's parent with the last segment as <c>name_prefix</c> and offer the
/// first matching folder's remaining text, accepted with Tab.
/// </summary>
public static class AdminPathCompletion
{
    public sealed record Context(string BrowsePath, string Fragment);

    /// <summary>WebUI <c>getAutocompleteContext</c>; null for a relative path.</summary>
    public static Context? ContextOf(string path)
    {
        var trimmed = path.Trim();
        if (!trimmed.StartsWith('/')) return null;
        if (trimmed == "/") return new("/", "");
        var endsWithSlash = trimmed.EndsWith('/');
        var normalized = endsWithSlash && trimmed.Length > 1 ? trimmed[..^1] : trimmed;
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash < 0) return null;
        if (endsWithSlash) return new(normalized.Length > 0 ? normalized : "/", "");
        return new(lastSlash == 0 ? "/" : normalized[..lastSlash], normalized[(lastSlash + 1)..]);
    }

    /// <summary>The text Tab would append for the first suggestion that extends the typed value; empty for none.</summary>
    public static string Suffix(string value, Context? context, IEnumerable<AdminFilesystemEntry> entries)
    {
        if (context == null) return "";
        var fragment = context.Fragment.Trim().ToLowerInvariant();
        var first = entries.FirstOrDefault(entry => fragment.Length == 0 || entry.Name.ToLowerInvariant().StartsWith(fragment, StringComparison.Ordinal));
        var trimmed = value.Trim();
        if (first == null || first.Path == trimmed || !first.Path.StartsWith(trimmed, StringComparison.Ordinal)) return "";
        return first.Path[trimmed.Length..];
    }

    /// <summary>Reads the suggestions for one typed value under the editor's lease (one bounded page).</summary>
    public static async Task<string> SuggestAsync(AdminLibrariesApi api, MonitorAuthorityLease lease, string value, CancellationToken ct)
    {
        var context = ContextOf(value);
        if (context == null) return "";
        var page = await api.BrowseAsync(lease, context.BrowsePath, context.Fragment.Length > 0 ? context.Fragment : null, null, AdminLibrariesApi.BrowsePageSize, ct).ConfigureAwait(false);
        return Suffix(value, context, page.Entries);
    }
}
