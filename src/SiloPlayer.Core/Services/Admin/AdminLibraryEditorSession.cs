using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminLibraryEditorSection { General, Folders, Metadata, Advanced }

/// <summary>The library editor's draft and transaction state for one render.</summary>
public sealed record AdminLibraryEditorState
{
    /// <summary>The library being edited; null while adding (becomes the created library once the server confirms it).</summary>
    public AdminLibrary? Library { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Paths { get; init; } = [""];
    public string Type { get; init; } = "movies";
    public bool Enabled { get; init; } = true;
    public string MetadataLanguage { get; init; } = "en";
    public bool AutoTranslateMetadata { get; init; }
    public bool ChapterThumbnailsEnabled { get; init; }
    public bool TrickplayEnabled { get; init; }
    public bool BoxSetsEnabled { get; init; }
    public int BoxSetMinMovies { get; init; } = AdminLibraryPresentation.DefaultBoxSetMinMovies;
    public bool BoxSetsInSearch { get; init; } = true;
    /// <summary>Null while a new library follows its type's default (on for series and mixed, off for movies).</summary>
    public bool? IntroDetectionChoice { get; init; }
    public IReadOnlyList<string> TrailerKinds { get; init; } = AdminLibraryPresentation.ProviderTrailerKinds;
    public bool RealtimeMonitoring { get; init; } = true;

    public AdminProviderChains? CurrentChain { get; init; }
    public bool CurrentChainFailed { get; init; }
    public AdminProviderChains? Defaults { get; init; }
    public string? DefaultsType { get; init; }
    public bool DefaultsFailed { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> EditedChains { get; init; } = new Dictionary<string, IReadOnlyList<AdminProviderChainItem>>();
    public bool ChainDirty { get; init; }

    public bool SubmitAttempted { get; init; }
    public bool Saving { get; init; }
    public bool Saved { get; init; }
    /// <summary>A write's answer was lost: saving stays off until the editor is reopened over reloaded data.</summary>
    public bool OutcomeUnknown { get; init; }
    public string? Failure { get; init; }
    public bool PosterUploading { get; init; }
    public bool PosterDeleting { get; init; }
    public bool Retired { get; init; }

    // Baselines for the members sent only when they change.
    internal bool SavedTrickplay { get; init; }
    internal bool SavedBoxSets { get; init; }
    internal int SavedBoxSetMinMovies { get; init; } = AdminLibraryPresentation.DefaultBoxSetMinMovies;
    internal bool SavedBoxSetsInSearch { get; init; } = true;
    internal AdminLibraryEditorState? Baseline { get; init; }

    public bool IsEditing => Library != null;
    public AdminLibraryPresentation.SettingSupport Support => AdminLibraryPresentation.Support(Type);
    public bool IntroDetectionEnabled => IntroDetectionChoice ?? !Support.CreditsOnlyDetection;
    public IReadOnlyList<string> ContentLevels => AdminLibraryPresentation.ContentLevels(Type);
    public int FolderCount => Paths.Count(path => path.Trim().Length > 0);

    public string? NameError => Name.Trim().Length == 0 ? AdminLibrariesText.T("web.admin.common.libraryEditor.errors.nameRequired") : null;
    public string? PathsError => FolderCount == 0 ? AdminLibrariesText.T("web.admin.common.libraryEditor.errors.pathsRequired") : null;
    /// <summary>Errors show only after a submit attempt (WebUI <c>submitAttempted</c>).</summary>
    public string? ShownNameError => SubmitAttempted ? NameError : null;
    public string? ShownPathsError => SubmitAttempted ? PathsError : null;

    private IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> DefaultChains =>
        DefaultsType == Type ? AdminLibraryPresentation.LevelChains(Defaults) : new Dictionary<string, IReadOnlyList<AdminProviderChainItem>>();

    /// <summary>WebUI <c>activeLevelChains</c>: the edited chain, or the saved chain merged with the type's defaults.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> ActiveChains
    {
        get
        {
            if (ChainDirty) return EditedChains;
            if (Library == null) return DefaultChains;
            if (CurrentChain == null) return EditedChains;
            return AdminLibraryPresentation.MergeWithDefaults(AdminLibraryPresentation.LevelChains(CurrentChain), DefaultChains, Type);
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> RemoteChains => AdminLibraryPresentation.RemoteChains(ActiveChains);

    /// <summary>Nothing truthful to show until the saved chain (when editing) and the type's defaults have arrived.</summary>
    public bool ChainLoading => !ChainDirty && ((DefaultsType != Type && !DefaultsFailed) || (Library != null && CurrentChain == null && !CurrentChainFailed));
    public bool ChainFailed => !ChainDirty && (DefaultsFailed || (Library != null && CurrentChainFailed));
    public bool HasLocalMetadata => ActiveChains.Values.SelectMany(items => items).Any(item => item.Local);
    public bool LocalMetadataEnabled => ActiveChains.Values.SelectMany(items => items).Any(item => item.Local && item.Enabled);
    public bool HasMetadataProviders => AdminLibraryPresentation.HasProviders(ActiveChains);
    public bool HasRemoteProviders => AdminLibraryPresentation.HasProviders(RemoteChains);
    public bool ShowAdvanced => Support.ChapterThumbnails || Support.IntroDetection;

    /// <summary>Whether the draft differs from what was loaded (drives the leave guard).</summary>
    public bool IsDirty
    {
        get
        {
            if (Baseline == null || Saved || Retired) return false;
            var b = Baseline;
            return ChainDirty || Name != b.Name || !Paths.Where(p => p.Trim().Length > 0).SequenceEqual(b.Paths.Where(p => p.Trim().Length > 0)) || Type != b.Type
                   || Enabled != b.Enabled || MetadataLanguage != b.MetadataLanguage || AutoTranslateMetadata != b.AutoTranslateMetadata
                   || ChapterThumbnailsEnabled != b.ChapterThumbnailsEnabled || TrickplayEnabled != b.TrickplayEnabled || BoxSetsEnabled != b.BoxSetsEnabled
                   || BoxSetMinMovies != b.BoxSetMinMovies || BoxSetsInSearch != b.BoxSetsInSearch || IntroDetectionChoice != b.IntroDetectionChoice
                   || !TrailerKinds.SequenceEqual(b.TrailerKinds) || RealtimeMonitoring != b.RealtimeMonitoring;
        }
    }

    public bool CanSave => !Saving && !Saved && !OutcomeUnknown && !Retired;
}

/// <summary>
/// Add/Edit library (WebUI <c>useLibraryForm</c> + <c>LibraryEditorDialog</c>). Owns one
/// draft under an action lease bound to the page owner. Provider chains come from the
/// server (the saved chain for an existing library, the type's seed for new ones) and
/// are written only when edited. A save runs once: create or update, then the chain;
/// a lost answer leaves saving disabled with an unknown-outcome message (no replay).
/// A created library turns the session into an edit of it, so a failed chain write is
/// retried as an update instead of creating a second library.
/// </summary>
public sealed class AdminLibraryEditorSession : IDisposable
{
    private readonly AdminLibrariesApi _api;
    private readonly AdminLibrariesController _owner;
    private readonly MonitorAuthorityLease _lease;
    private readonly object _gate = new();
    private AdminLibraryEditorState _state;
    private long _defaultsSequence;
    private bool _disposed;

    internal AdminLibraryEditorSession(AdminLibrariesApi api, AdminLibrariesController owner, MonitorAuthorityLease lease, AdminLibrary? library)
    {
        _api = api; _owner = owner; _lease = lease;
        _state = Initial(library);
    }

    /// <summary>Starts an editor under a new action lease (throws when the owner retired).</summary>
    public static AdminLibraryEditorSession Begin(AdminLibrariesController owner, AdminLibrary? library) =>
        new(owner.Api, owner, owner.CaptureAction(), library);

    public event Action? Changed;
    public AdminLibraryEditorState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease Lease => _lease;
    public bool IsCurrent => !_disposed && _owner.IsActionCurrent(_lease);

    private static AdminLibraryEditorState Initial(AdminLibrary? library)
    {
        var state = library == null ? new AdminLibraryEditorState() : new AdminLibraryEditorState
        {
            Library = library,
            Name = library.Name,
            Paths = library.Paths.Count > 0 ? library.Paths.ToArray() : [""],
            Type = library.Type,
            Enabled = library.Enabled,
            MetadataLanguage = library.MetadataLanguage,
            AutoTranslateMetadata = library.AutoTranslateMetadata,
            ChapterThumbnailsEnabled = library.ChapterThumbnailsEnabled,
            TrickplayEnabled = library.TrickplayEnabled,
            BoxSetsEnabled = library.BoxSetsEnabled,
            BoxSetMinMovies = library.BoxSetMinMovies is >= 2 and <= 10 ? library.BoxSetMinMovies : AdminLibraryPresentation.DefaultBoxSetMinMovies,
            BoxSetsInSearch = library.BoxSetsInSearch,
            IntroDetectionChoice = library.IntroDetectionEnabled,
            TrailerKinds = library.TrailerKinds.ToArray(),
            RealtimeMonitoring = library.RealtimeMonitoring,
            SavedTrickplay = library.TrickplayEnabled,
            SavedBoxSets = library.BoxSetsEnabled,
            SavedBoxSetMinMovies = library.BoxSetMinMovies is >= 2 and <= 10 ? library.BoxSetMinMovies : AdminLibraryPresentation.DefaultBoxSetMinMovies,
            SavedBoxSetsInSearch = library.BoxSetsInSearch,
        };
        return state with { Baseline = state };
    }

    /// <summary>Reads the saved chain (editing) and the type's server-seeded defaults.</summary>
    public Task LoadAsync()
    {
        var state = State;
        return Task.WhenAll(LoadDefaultsAsync(state.Type), state.Library is { } library ? LoadCurrentChainAsync(library.Id) : Task.CompletedTask);
    }

    private async Task LoadCurrentChainAsync(string libraryId)
    {
        try
        {
            var chain = await _api.GetProvidersAsync(_lease, libraryId, _lease.Token).ConfigureAwait(false);
            Update(state => state.Library?.Id == libraryId ? state with { CurrentChain = chain, CurrentChainFailed = false } : state);
        }
        catch (Exception error)
        {
            if (AdminFailures.Classify(error, _lease) is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
            Update(state => state.Library?.Id == libraryId ? state with { CurrentChainFailed = true } : state);
        }
    }

    private async Task LoadDefaultsAsync(string type)
    {
        long sequence;
        lock (_gate) sequence = ++_defaultsSequence;
        if (string.IsNullOrWhiteSpace(type)) return;
        try
        {
            var defaults = await _api.GetProviderDefaultsAsync(_lease, type, _lease.Token).ConfigureAwait(false);
            Update(state => sequence == _defaultsSequence && state.Type == type ? state with { Defaults = defaults, DefaultsType = type, DefaultsFailed = false } : state);
        }
        catch (Exception error)
        {
            if (AdminFailures.Classify(error, _lease) is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
            Update(state => sequence == _defaultsSequence && state.Type == type ? state with { DefaultsFailed = true, Defaults = null, DefaultsType = null } : state);
        }
    }

    /// <summary>Retries the provider reads after a failure.</summary>
    public Task RetryChainsAsync()
    {
        Update(state => state with { DefaultsFailed = false, CurrentChainFailed = false });
        return LoadAsync();
    }

    // ── Field edits ──────────────────────────────────────────────────────

    public void SetName(string value) => Edit(state => state with { Name = value });
    public void SetEnabled(bool value) => Edit(state => state with { Enabled = value });
    public void SetMetadataLanguage(string value) => Edit(state => state with { MetadataLanguage = value });
    public void SetAutoTranslate(bool value) => Edit(state => state with { AutoTranslateMetadata = value });
    public void SetChapterThumbnails(bool value) => Edit(state => state with { ChapterThumbnailsEnabled = value });
    public void SetTrickplay(bool value) => Edit(state => state with { TrickplayEnabled = value });
    public void SetBoxSets(bool value) => Edit(state => state with { BoxSetsEnabled = value });
    public void SetBoxSetMinMovies(int value) { if (value is >= 2 and <= 10) Edit(state => state with { BoxSetMinMovies = value }); }
    public void SetBoxSetsInSearch(bool value) => Edit(state => state with { BoxSetsInSearch = value });
    public void SetIntroDetection(bool value) => Edit(state => state with { IntroDetectionChoice = value });
    public void SetRealtimeMonitoring(bool value) => Edit(state => state with { RealtimeMonitoring = value });

    public void ToggleTrailerKind(string kind) => Edit(state => state with
    {
        TrailerKinds = state.TrailerKinds.Contains(kind) ? state.TrailerKinds.Where(existing => existing != kind).ToArray() : state.TrailerKinds.Append(kind).ToArray(),
    });

    public void UpdatePath(int index, string value) => Edit(state =>
    {
        if (index < 0 || index >= state.Paths.Count) return state;
        var paths = state.Paths.ToArray();
        paths[index] = value;
        return state with { Paths = paths };
    });

    public void AddPath() => Edit(state => state.Paths.Count >= AdminLibrariesApi.MaxPaths ? state : state with { Paths = state.Paths.Append("").ToArray() });

    public void RemovePath(int index) => Edit(state =>
        index < 0 || index >= state.Paths.Count ? state : state with { Paths = state.Paths.Where((_, i) => i != index).DefaultIfEmpty("").ToArray() });

    /// <summary>WebUI <c>mergeBrowsedPaths</c>: keeps filled paths and appends new browsed ones.</summary>
    public void MergeBrowsedPaths(IEnumerable<string> selected) => Edit(state =>
    {
        var merged = state.Paths.Where(path => path.Trim().Length > 0).ToList();
        foreach (var path in selected) if (!merged.Contains(path) && merged.Count < AdminLibrariesApi.MaxPaths) merged.Add(path);
        return state with { Paths = merged.Count > 0 ? merged.ToArray() : [""] };
    });

    /// <summary>A new library drops chain edits: the new type's defaults come from the server.</summary>
    public void SetType(string type)
    {
        var reload = false;
        Edit(state =>
        {
            if (state.Type == type) return state;
            reload = true;
            return state.Library == null
                ? state with { Type = type, EditedChains = new Dictionary<string, IReadOnlyList<AdminProviderChainItem>>(), ChainDirty = false }
                : state with { Type = type };
        });
        if (reload) _ = LoadDefaultsAsync(type);
    }

    public void ReorderLevel(string level, IReadOnlyList<AdminProviderChainItem> remote) => Edit(state =>
    {
        var active = state.ActiveChains;
        var items = active.TryGetValue(level, out var existing) ? existing : [];
        var next = new Dictionary<string, IReadOnlyList<AdminProviderChainItem>>(active, StringComparer.Ordinal) { [level] = AdminLibraryPresentation.WithLocalFirst(items, remote) };
        return state with { EditedChains = next, ChainDirty = true };
    });

    public void MoveProvider(string level, int remoteIndex, int direction)
    {
        var remote = State.RemoteChains.TryGetValue(level, out var items) ? items : [];
        if (AdminLibraryPresentation.MoveProvider(remote, remoteIndex, direction) is { } moved) ReorderLevel(level, moved);
    }

    public void ToggleProvider(string level, int remoteIndex) => Edit(state =>
    {
        var active = state.ActiveChains;
        var items = active.TryGetValue(level, out var existing) ? existing : [];
        var remote = items.Where(item => !item.Local).ToArray();
        if (remoteIndex < 0 || remoteIndex >= remote.Length) return state;
        remote[remoteIndex] = remote[remoteIndex] with { Enabled = !remote[remoteIndex].Enabled };
        var next = new Dictionary<string, IReadOnlyList<AdminProviderChainItem>>(active, StringComparer.Ordinal) { [level] = AdminLibraryPresentation.WithLocalFirst(items, remote) };
        return state with { EditedChains = next, ChainDirty = true };
    });

    public void SetLocalMetadata(bool enabled) => Edit(state => state with
    {
        EditedChains = AdminLibraryPresentation.SetLocalEnabled(state.ActiveChains, enabled), ChainDirty = true,
    });

    // ── Save ─────────────────────────────────────────────────────────────

    /// <summary>The body the WebUI builds (members gated by support; change-only members compared with the saved values).</summary>
    public static AdminLibraryWrite BuildBody(AdminLibraryEditorState state)
    {
        var support = state.Support;
        var trickplay = support.Trickplay && state.TrickplayEnabled;
        var boxSets = support.BoxSets && state.BoxSetsEnabled;
        return new AdminLibraryWrite
        {
            Name = state.Name.Trim(),
            Paths = state.Paths.Where(path => path.Trim().Length > 0).ToArray(),
            Type = state.Type,
            Enabled = state.Enabled,
            MetadataLanguage = state.MetadataLanguage,
            AutoTranslateMetadata = state.AutoTranslateMetadata,
            ChapterThumbnailsEnabled = support.ChapterThumbnails && state.ChapterThumbnailsEnabled,
            IntroDetectionEnabled = support.IntroDetection && state.IntroDetectionEnabled,
            TrailerKinds = support.Trailers ? state.TrailerKinds.ToArray() : [],
            RealtimeMonitoring = state.RealtimeMonitoring,
            TrickplayEnabled = trickplay != state.SavedTrickplay ? trickplay : null,
            BoxSetsEnabled = boxSets != state.SavedBoxSets ? boxSets : null,
            BoxSetMinMovies = boxSets && state.BoxSetMinMovies != state.SavedBoxSetMinMovies ? state.BoxSetMinMovies : null,
            BoxSetsInSearch = boxSets && state.BoxSetsInSearch != state.SavedBoxSetsInSearch ? state.BoxSetsInSearch : null,
        };
    }

    /// <summary>
    /// Submits once. Returns the section holding a validation error (the view
    /// switches to it), or null when the request was sent or refused for state.
    /// </summary>
    public async Task<AdminLibraryEditorSection?> SubmitAsync()
    {
        AdminLibraryEditorState state;
        lock (_gate)
        {
            if (_disposed || !_state.CanSave) return null;
            _state = _state with { SubmitAttempted = true, Failure = null };
            state = _state;
        }
        if (state.NameError != null || state.PathsError != null)
        {
            Changed?.Invoke();
            return state.NameError != null ? AdminLibraryEditorSection.General : AdminLibraryEditorSection.Folders;
        }
        if (!IsCurrent) { Update(s => s with { Retired = true }); return null; }
        var body = BuildBody(state);
        var chains = state.ChainDirty ? state.ActiveChains : null;
        Update(s => s with { Saving = true });
        AdminLibrary saved;
        var editing = state.Library;
        try
        {
            saved = editing != null
                ? await _api.UpdateAsync(_lease, editing.Id, body, _lease.Token).ConfigureAwait(false)
                : await _api.CreateAsync(_lease, body, _lease.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Failed(error, "web.admin.libraries.toast.saveFailed");
            return null;
        }
        if (!IsCurrent) return null;
        _owner.Announce(AdminLibrariesText.T(editing != null ? "web.admin.libraries.toast.libraryUpdated" : "web.admin.libraries.toast.libraryCreated"), false);
        _owner.AfterLibrarySaved(_lease, body.TrickplayEnabled != null || body.Enabled != (editing?.Enabled ?? true));
        // From here the library exists: a retry is an update of it.
        Update(s => s with
        {
            Library = saved, SavedTrickplay = saved.TrickplayEnabled, SavedBoxSets = saved.BoxSetsEnabled,
            SavedBoxSetMinMovies = saved.BoxSetMinMovies is >= 2 and <= 10 ? saved.BoxSetMinMovies : s.SavedBoxSetMinMovies, SavedBoxSetsInSearch = saved.BoxSetsInSearch,
        });
        if (chains == null) { Update(s => s with { Saving = false, Saved = true }); return null; }
        try
        {
            await _api.SetProvidersAsync(_lease, saved.Id, chains, _lease.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Failed(error, "web.admin.libraries.toast.providerChainFailed");
            return null;
        }
        if (!IsCurrent) return null;
        _owner.Announce(AdminLibrariesText.T("web.admin.libraries.toast.providerChainUpdated"), false);
        Update(s => s with { Saving = false, Saved = true, ChainDirty = false });
        return null;
    }

    private void Failed(Exception error, string fallbackKey)
    {
        if (!IsCurrent || AdminFailures.Classify(error, _lease) is AdminFailureKind.Retired) { Update(s => s with { Saving = false, Retired = true }); return; }
        var unknown = AdminLibraryPresentation.IsUnknownOutcome(error);
        var message = unknown ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown") : AdminLibraryPresentation.Describe(error, fallbackKey);
        Update(s => s with { Saving = false, OutcomeUnknown = unknown, Failure = message });
        _owner.Announce(message, true);
        if (unknown) _ = _owner.ReloadAsync();
    }

    // ── Poster (separate immediate writes, as the WebUI) ────────────────

    public async Task UploadPosterAsync(byte[] bytes, string mediaType)
    {
        AdminLibrary? library;
        lock (_gate)
        {
            library = _state.Library;
            if (_disposed || library == null || _state.PosterUploading || _state.PosterDeleting) return;
            _state = _state with { PosterUploading = true };
        }
        Changed?.Invoke();
        try
        {
            var updated = await _api.UploadPosterAsync(_lease, library.Id, bytes, mediaType, _lease.Token).ConfigureAwait(false);
            if (!IsCurrent) return;
            Update(s => s with { PosterUploading = false, Library = s.Library == null ? null : WithPoster(s.Library, updated.PosterUrl) });
            _owner.Announce(AdminLibrariesText.T("web.admin.libraries.toast.posterUpdated"), false);
            _ = _owner.ReloadAsync();
        }
        catch (Exception error)
        {
            if (!IsCurrent) return;
            Update(s => s with { PosterUploading = false });
            _owner.Announce(AdminLibraryPresentation.IsUnknownOutcome(error) ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown")
                : AdminLibraryPresentation.Describe(error, "web.admin.libraries.toast.posterUploadFailed"), true);
            if (AdminLibraryPresentation.IsUnknownOutcome(error)) _ = _owner.ReloadAsync();
        }
    }

    public async Task DeletePosterAsync()
    {
        AdminLibrary? library;
        lock (_gate)
        {
            library = _state.Library;
            if (_disposed || library == null || _state.PosterUploading || _state.PosterDeleting) return;
            _state = _state with { PosterDeleting = true };
        }
        Changed?.Invoke();
        try
        {
            await _api.DeletePosterAsync(_lease, library.Id, _lease.Token).ConfigureAwait(false);
            if (!IsCurrent) return;
            Update(s => s with { PosterDeleting = false, Library = s.Library == null ? null : WithPoster(s.Library, null) });
            _owner.Announce(AdminLibrariesText.T("web.admin.libraries.toast.posterRemoved"), false);
            _ = _owner.ReloadAsync();
        }
        catch (Exception error)
        {
            if (!IsCurrent) return;
            Update(s => s with { PosterDeleting = false });
            _owner.Announce(AdminLibraryPresentation.IsUnknownOutcome(error) ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown")
                : AdminLibraryPresentation.Describe(error, "web.admin.libraries.toast.posterRemoveFailed"), true);
            if (AdminLibraryPresentation.IsUnknownOutcome(error)) _ = _owner.ReloadAsync();
        }
    }

    private static AdminLibrary WithPoster(AdminLibrary library, string? poster) => new()
    {
        Id = library.Id, Name = library.Name, Type = library.Type, Paths = library.Paths, Enabled = library.Enabled, MetadataLanguage = library.MetadataLanguage,
        AutoTranslateMetadata = library.AutoTranslateMetadata, ChapterThumbnailsEnabled = library.ChapterThumbnailsEnabled,
        ChapterThumbnailsSupported = library.ChapterThumbnailsSupported, IntroDetectionEnabled = library.IntroDetectionEnabled,
        TrickplayEnabled = library.TrickplayEnabled, TrickplaySupported = library.TrickplaySupported, BoxSetsEnabled = library.BoxSetsEnabled,
        BoxSetMinMovies = library.BoxSetMinMovies, BoxSetsInSearch = library.BoxSetsInSearch, TrailerKinds = library.TrailerKinds,
        RealtimeMonitoring = library.RealtimeMonitoring, SortOrder = library.SortOrder, PosterUrl = poster, LastScannedAt = library.LastScannedAt,
        ScanWarningCode = library.ScanWarningCode, ScanWarningMessage = library.ScanWarningMessage, ScanWarningAt = library.ScanWarningAt,
    };

    /// <summary>Discards the draft (leave guard): the session stays open but reports clean.</summary>
    public void Discard() => Update(state => state with { Baseline = null });

    private void Edit(Func<AdminLibraryEditorState, AdminLibraryEditorState> edit)
    {
        lock (_gate)
        {
            if (_disposed || _state.Saving || _state.Saved || _state.Retired) return;
            var next = edit(_state);
            if (ReferenceEquals(next, _state)) return;
            _state = next;
        }
        Changed?.Invoke();
    }

    private void Update(Func<AdminLibraryEditorState, AdminLibraryEditorState> update)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _state = update(_state);
            if (!_owner.IsActionCurrent(_lease)) _state = _state with { Retired = true, Saving = false };
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lease.Dispose();
        Changed = null;
    }
}

/// <summary>
/// The editor's translation prewarm panel (WebUI <c>LibraryTranslatePanel</c>): shown only for
/// a saved library while AI description translation is available. Jobs are polled every
/// 2 s only while one is active and the editor is open; a started or seen-running job is
/// watched so its completion or failure is reported once, even after the editor closed.
/// </summary>
public sealed class AdminLibraryTranslationSession : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly AdminLibrariesApi _api;
    private readonly AdminLibrariesController _owner;
    private readonly MonitorAuthorityLease _lease;
    private readonly string _libraryId;
    private readonly ISet<string> _watched;
    private readonly IAdminScheduler _scheduler;
    private readonly object _gate = new();
    private IDisposable? _poll;
    private bool _disposed;

    public AdminLibraryTranslationSession(AdminLibrariesController owner, MonitorAuthorityLease lease, string libraryId, ISet<string> watched)
    {
        _owner = owner; _api = owner.Api; _lease = lease; _libraryId = libraryId; _watched = watched; _scheduler = owner.Scheduler;
    }

    public event Action? Changed;
    public bool Enabled { get; private set; }
    public IReadOnlyList<AdminTranslationJob> Jobs { get; private set; } = [];
    public IReadOnlyList<AdminTranslationJob> ActiveJobs => Jobs.Where(job => job.IsActive).ToArray();
    public bool Starting { get; private set; }
    public bool Cancelling { get; private set; }
    public bool Busy => Starting || ActiveJobs.Count > 0;

    public async Task OpenAsync()
    {
        try
        {
            var capability = await _api.GetMetadataAiCapabilityAsync(_lease, _lease.Token).ConfigureAwait(false);
            if (!Current()) return;
            Enabled = capability.State == "available";
        }
        catch (Exception) { if (!Current()) return; Enabled = false; }
        Changed?.Invoke();
        if (Enabled) await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var jobs = await _api.ListTranslationJobsAsync(_lease, _libraryId, _lease.Token).ConfigureAwait(false);
            if (!Current()) return;
            var finished = false;
            foreach (var job in jobs)
            {
                if (job.IsActive) { lock (_watched) _watched.Add(job.Id); continue; }
                bool watched;
                lock (_watched) watched = _watched.Remove(job.Id);
                if (!watched) continue;
                finished = true;
                if (job.Status == "completed")
                    _owner.Announce(job.ProgressMessage.Length > 0 ? job.ProgressMessage : AdminLibrariesText.T("web.admin.common.libraryEditor.translate.finished"), false);
                else if (job.Status == "failed")
                    _owner.Announce(string.IsNullOrEmpty(job.ErrorMessage) ? AdminLibrariesText.T("web.admin.common.libraryEditor.translate.failed") : job.ErrorMessage, true);
            }
            Jobs = jobs;
            _ = finished;
            Changed?.Invoke();
            lock (_gate)
            {
                _poll?.Dispose(); _poll = null;
                if (!_disposed && jobs.Any(job => job.IsActive)) _poll = _scheduler.Schedule(PollInterval, () => _ = RefreshAsync());
            }
        }
        catch (Exception)
        {
            // WebUI shows nothing for a failed jobs read; the next start or poll reads again.
        }
    }

    public async Task StartAsync(string targetLanguage)
    {
        if (string.IsNullOrEmpty(targetLanguage)) { _owner.Announce(AdminLibrariesText.T("web.admin.common.libraryEditor.translate.pickLanguage"), true); return; }
        lock (_gate) { if (_disposed || Busy) return; Starting = true; }
        Changed?.Invoke();
        try
        {
            var job = await _api.StartTranslationAsync(_lease, _libraryId, targetLanguage, _lease.Token).ConfigureAwait(false);
            lock (_watched) _watched.Add(job.Id);
        }
        catch (Exception error)
        {
            if (Current())
                _owner.Announce(AdminLibraryPresentation.IsUnknownOutcome(error) ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown")
                    : AdminLibraryPresentation.Describe(error, "web.item.toast.translateFailed"), true);
        }
        finally { Starting = false; }
        if (!Current()) return;
        Changed?.Invoke();
        await RefreshAsync().ConfigureAwait(false);
    }

    public async Task CancelAsync(string jobId)
    {
        lock (_gate) { if (_disposed || Cancelling) return; Cancelling = true; }
        Changed?.Invoke();
        try { await _api.CancelTranslationAsync(_lease, _libraryId, jobId, _lease.Token).ConfigureAwait(false); }
        catch (Exception error)
        {
            if (Current()) _owner.Announce(AdminLibraryPresentation.Describe(error, "web.admin.errors.translationCancelFailed"), true);
        }
        finally { Cancelling = false; }
        if (!Current()) return;
        Changed?.Invoke();
        await RefreshAsync().ConfigureAwait(false);
    }

    private bool Current() => !_disposed && _owner.IsActionCurrent(_lease);

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _poll?.Dispose(); _poll = null; }
        Changed = null;
    }
}
