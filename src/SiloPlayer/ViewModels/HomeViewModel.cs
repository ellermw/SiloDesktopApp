using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<HomePreferenceChanged>,
    IRecipient<HomeDismissalApplied>,
    IRecipient<HomeDismissalUndone>,
    IRecipient<NotInterestedChange>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly HomeApi _homeApi;
    private readonly AuthService _authService;
    private readonly SettingsApi? _settingsApi;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private IReadOnlyList<HomeSection> _layoutOrder = [];
    private readonly Dictionary<string, HomeSectionWithItems> _rawSections = new(StringComparer.Ordinal);
    private readonly HashSet<(int Generation, string SectionId)> _completingSections = [];
    private readonly HashSet<string> _completionWantedSections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _completionRetryAfter = new(StringComparer.Ordinal);
    private long _rowCompletionOrder;
    private long _progressRemovalRevision;
    private readonly Dictionary<string, (long Revision, string? SeriesId)> _progressRemovals = new(StringComparer.Ordinal);
    private HashSet<string> _avoidDuplicateTypes = new(StringComparer.Ordinal);

    public HomeViewModel(HomeApi homeApi, AuthService authService, SettingsApi? settingsApi = null)
    {
        _homeApi = homeApi;
        _authService = authService;
        _settingsApi = settingsApi;
        // F4: subscribe to media state changes + playback progress so the
        // home screen's Continue Watching / Next Up rows reflect activity
        // from anywhere in the app without a full reload.
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this);
        WeakReferenceMessenger.Default.Register<HomePreferenceChanged>(this);
        WeakReferenceMessenger.Default.Register<HomeDismissalApplied>(this);
        WeakReferenceMessenger.Default.Register<HomeDismissalUndone>(this);
        WeakReferenceMessenger.Default.Register<NotInterestedChange>(this);
        WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this);
    }

    public void Receive(HomePreferenceChanged message)
    {
        if (!_homeApi.IsCurrentContext(message.Context)) return;
        _ = RunOnUiThreadAsync(() =>
        {
            if (!_homeApi.IsCurrentContext(message.Context) || _loadedContext != message.Context) return;
            if (message.ReloadLayout) _ = RefreshPreferenceLayoutAsync(message.Context);
            else QueueRealtimeRefresh("home_preference", message.Context);
        });
    }

    private async Task RefreshPreferenceLayoutAsync(ApiRequestContext context)
    {
        while (_loadInProgress && _homeApi.IsCurrentContext(context)) await Task.Delay(20);
        if (!_homeApi.IsCurrentContext(context) || _loadedContext != context) return;
        InvalidateCache();
        if (_isActive) await LoadAsync();
    }

    // F4 — messenger receivers.
    public void Receive(NotInterestedChange message)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            if (!_homeApi.IsCurrentContext(message.Context) || _loadedProfileId != message.Context.ProfileId) return;
            // Retire reads begun before this mutation, but leave a live token
            // for near-end demand delivered before the debounced refresh.
            // Fresh REST state remains authoritative for missed events too.
            _sectionLoadCts?.Cancel();
            _sectionLoadCts?.Dispose();
            _sectionLoadCts = new CancellationTokenSource();
            ++_sectionLoadGeneration;
            if (message.Marked.HasValue && NotInterestedPolicy.Apply(FeaturedSections.Concat(Sections).Concat(_rawSections.Values), message.ItemId, message.Marked.Value))
                BumpRenderRevision();
            QueueRealtimeRefresh("user_state:not_interested");
        });
    }

    public void Receive(MediaSurfaceChanged message)
    {
        var owner = message.Context ?? _homeApi.CaptureContext();
        _ = RunOnUiThreadAsync(() =>
        {
            if (!_homeApi.IsCurrentContext(owner) || _loadedContext != owner) return;
            switch (message.Kind)
            {
                case MediaSurfaceChangeKind.WatchedMarked:
                case MediaSurfaceChangeKind.HomeDismissed:
                    RemoveFromProgressRows(message.ContentId, message.SeriesId);
                    break;
            }

            QueueRealtimeRefresh($"media_surface:{message.Kind}", owner);
        });
    }

    public void Receive(HomeDismissalApplied message)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            if (!_homeApi.IsCurrentContext(message.Context) || _loadedProfileId != message.Context.ProfileId) return;
            _lastDismissedSurface = message.Surface; _lastDismissedItemId = message.Item.ContentId;
            _dismissContext = message.Context;
            _dismissSnapshot = HomeDismissalSnapshot.Capture(FeaturedSections.Concat(Sections), message.Item.ContentId,
                HomeDismissalPolicy.SeriesIdentity(message.Item));
            SetLocalizedMessage(nameof(UndoMessage), () => message.Item.Type is "episode" or "series"
                ? SiloPlayer.Core.Localization.AppStrings.Get("web.home.dismiss.removedContinueWatchingAndNextUp")
                : message.Surface == "next_up" ? SiloPlayer.Core.Localization.AppStrings.Get("web.home.dismiss.removedNextUp")
                : message.Item.Type == "audiobook" ? SiloPlayer.Core.Localization.AppStrings.Get("web.home.dismiss.removedContinueListening")
                : message.Item.Type == "ebook" ? SiloPlayer.Core.Localization.AppStrings.Get("web.home.dismiss.removedContinueReading")
                : SiloPlayer.Core.Localization.AppStrings.Get("web.home.dismiss.removedContinueWatching"), value => UndoMessage = value ?? "");
            ShowUndoBanner = true;
            _ = AutoHideUndoBannerAsync();
        });
    }

    public void Receive(HomeDismissalUndone message)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            if (!_homeApi.IsCurrentContext(message.Context) || _loadedContext != message.Context) return;
            if (_lastDismissedSurface == message.Surface && _lastDismissedItemId == message.ItemId
                && _dismissContext == message.Context)
            {
                // This surface still offers the same Undo: finish it as its own Undo does.
                RestoreDismissedItem();
                ShowUndoBanner = false;
                ClearDismissState();
            }
            else
            {
                // Responses read before the Undo describe the dismissed state.
                _sectionLoadCts?.Cancel();
                _sectionLoadCts?.Dispose();
                _sectionLoadCts = new CancellationTokenSource();
                ++_sectionLoadGeneration;
                _progressRemovals.Remove(message.ItemId);
            }
            QueueRealtimeRefresh("home_dismissal_undo", message.Context);
        });
    }

    public void Receive(PlaybackProgressUpdated message)
    {
        var owner = message.Identity ?? _homeApi.CaptureIdentityContext();
        if (!_homeApi.IsCurrentIdentityContext(owner)) return;
        _ = RunOnUiThreadAsync(() =>
        {
            if (!_homeApi.IsCurrentIdentityContext(owner) || _loadedIdentityContext != owner) return;
            // Apply the position/removal optimistically, then fetch the server's
            // authoritative rows so Next Up can advance to the following episode.
            foreach (var section in FeaturedSections.Concat(Sections).Concat(_rawSections.Values))
            {
                if (section.SectionType is not ("continue_watching" or "next_up")) continue;
                for (int i = 0; i < section.Items.Count; i++)
                {
                    var item = section.Items[i];
                    if (item.ContentId != message.ContentId) continue;

                    if (message.Completed)
                    {
                        HomeRowWindow.RemoveAt(section, i);
                    }
                    else
                    {
                        item.PositionSeconds = message.PositionSeconds;
                        if (message.DurationSeconds > 0)
                            item.DurationSeconds = message.DurationSeconds;
                        item.ProgressUpdatedAt = message.UpdatedAt.ToString("o");
                    }
                    break;
                }
            }

            BumpRenderRevision();
            QueueRealtimeRefresh(message.Completed ? "playback_completed" : "playback_progress");
        });
    }

    private void RemoveFromProgressRows(string contentId, string? seriesId)
    {
        // Keep the server's whole-show predicate, including members absent
        // from the first20 cards, for reads already in flight at dismissal.
        _progressRemovals[contentId] = (++_progressRemovalRevision, seriesId);
        var removedAny = false;
        foreach (var section in FeaturedSections.Concat(Sections).Concat(_rawSections.Values))
        {
            if (section.SectionType is not ("continue_watching" or "next_up")) continue;
            for (int i = section.Items.Count - 1; i >= 0; i--)
            {
                var item = section.Items[i];
                if (item.ContentId == contentId
                    || (seriesId != null && (item.SeriesId == seriesId || item.ContentId == seriesId)))
                {
                    HomeRowWindow.RemoveAt(section, i);
                    removedAny = true;
                }
            }
        }
        if (removedAny)
            BumpRenderRevision();
    }

    public ObservableCollection<HomeSectionWithItems> FeaturedSections { get; } = [];
    public ObservableCollection<HomeSectionWithItems> Sections { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // Dismissal undo state
    [ObservableProperty]
    private bool _showUndoBanner;

    [ObservableProperty]
    private string _undoMessage = "";

    private string? _lastDismissedSurface;
    private HomeDismissalSnapshot? _dismissSnapshot;
    private ApiRequestContext? _dismissContext;
    private long _undoGeneration;
    private string? _lastDismissedItemId;
    private MediaItem? _lastDismissedItem;
    private HomeSectionWithItems? _lastDismissedSection;
    private int _lastDismissedIndex;
    private bool _lastDismissedWasInitial;

    private DateTime _lastLoadedAt = DateTime.MinValue;
    private bool _loadInProgress;
    private bool _localizedReloadPending;
    private bool _hasLoadedLayout;
    private CancellationTokenSource? _sectionLoadCts;
    private int _sectionLoadGeneration;
    private string? _loadedProfileId;
    private ApiRequestContext? _loadedContext;
    private ApiIdentityContext? _loadedIdentityContext;
    private CancellationTokenSource? _realtimeRefreshCts;
    private bool _realtimeRefreshInProgress;
    private bool _realtimeRefreshRequested;
    private bool _isActive;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RealtimeRefreshDebounce = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan CompletionRetryCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CatalogBurstRefreshCooldown = TimeSpan.FromSeconds(30);
    private readonly HomeRealtimeRefreshGate _realtimeRefreshGate = new(
        RealtimeRefreshDebounce,
        CatalogBurstRefreshCooldown);

    // F12: maximum number of concurrent per-section fetches. Matches the
    // webui `MAX_CONCURRENT_SECTION_REQUESTS = 5` limit so we don't overwhelm
    // the server with 15+ parallel section requests.
    private const int MaxConcurrentSectionRequests = 5;

    [ObservableProperty]
    private bool _hasConfiguredSections;

    [ObservableProperty]
    private int _renderRevision;

    private void BumpRenderRevision() => RenderRevision++;
    public ApiRequestContext? SurfaceContext => _loadedContext;
    /// <summary>Changes only when the mounted owner's data is actually retired.</summary>
    public long SurfaceGeneration { get; private set; }

    /// <summary>
    /// Tracks whether the cached Home surface is the current navigation target.
    /// Realtime events still invalidate inactive data, but only a visible Home
    /// page performs network work immediately.
    /// </summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        if (!active)
        {
            var pending = Interlocked.Exchange(ref _realtimeRefreshCts, null);
            pending?.Cancel();
            pending?.Dispose();
        }
    }

    /// <summary>
    /// Mirrors the WebUI home refresh signal: coalesce bursts from progress,
    /// watched-state, and catalog scan events, then re-fetch every mounted Home
    /// section without navigating away or tearing down the page.
    /// </summary>
    public void QueueRealtimeRefresh(string reason, ApiRequestContext? context = null)
    {
        InvalidateCache();
        if (!_isActive) return;

        var delay = _realtimeRefreshGate.GetDelay(reason, DateTimeOffset.UtcNow);
        var existing = Volatile.Read(ref _realtimeRefreshCts);
        if (HomeRealtimeRefreshGate.IsCatalogBurst(reason)
            && delay > RealtimeRefreshDebounce
            && existing is { IsCancellationRequested: false })
        {
            // Keep the already-scheduled refresh. Repeated scan notifications
            // share one absolute cooldown deadline instead of perpetually
            // cancelling and pushing that refresh farther into the future.
            return;
        }

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _realtimeRefreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _ = QueueRealtimeRefreshAsync(reason, delay, cts, context);
    }

    private async Task QueueRealtimeRefreshAsync(
        string reason,
        TimeSpan delay,
        CancellationTokenSource owner,
        ApiRequestContext? context)
    {
        try
        {
            await Task.Delay(delay, owner.Token).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
            {
                if (!ReferenceEquals(_realtimeRefreshCts, owner) || owner.IsCancellationRequested || !_isActive)
                    return;
                if (context.HasValue && (!_homeApi.IsCurrentContext(context.Value) || _loadedContext != context)) return;

                _realtimeRefreshGate.MarkRefreshed(reason, DateTimeOffset.UtcNow);
                _ = RefreshMountedSectionsAsync(reason);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // A newer event superseded this debounce window or Home went inactive.
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _realtimeRefreshCts, null, owner), owner))
                owner.Dispose();
        }
    }

    private async Task RefreshMountedSectionsAsync(string reason)
    {
        _realtimeRefreshRequested = true;
        if (_realtimeRefreshInProgress) return;

        _realtimeRefreshInProgress = true;
        try
        {
            while (_realtimeRefreshRequested && _isActive)
            {
                _realtimeRefreshRequested = false;

                // Preserve the mounted visual tree and patch every row from the
                // authoritative section endpoints. If the first layout has not
                // loaded yet, fall back to the ordinary initial-load path.
                if (!_hasLoadedLayout || FeaturedSections.Count + Sections.Count == 0
                    || !_loadedContext.HasValue || !_homeApi.IsCurrentContext(_loadedContext.Value))
                {
                    await LoadAsync();
                    continue;
                }

                _sectionLoadCts?.Cancel();
                _sectionLoadCts?.Dispose();
                _sectionLoadCts = new CancellationTokenSource();
                var generation = ++_sectionLoadGeneration;
                LocalLog.AppendLine("home_refresh.txt", $"realtime | reason={reason} | generation={generation}");
                await FetchSectionItemsInBatchesAsync(generation, _sectionLoadCts.Token);
                _lastLoadedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine("home_error.txt", $"realtime_refresh | reason={reason} | {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _realtimeRefreshInProgress = false;
        }
    }

    public async Task CatchUpAfterReconnectAsync(ApiRequestContext context)
    {
        if (!_homeApi.IsCurrentContext(context)) return;
        AdoptReconnectContext(context);
        await LoadAsync();
    }

    /// <summary>
    /// Moves retained same-owner content to a reconnect's request context without
    /// fetching, so later refreshes patch rows instead of resetting the surface.
    /// </summary>
    public void AdoptReconnectContext(ApiRequestContext context)
    {
        if (!_homeApi.IsCurrentContext(context)) return;
        // Only transport recovery for the same owner may keep mounted content.
        // Explicit permission changes still use the ordinary route retirement.
        if (_loadedIdentityContext is { } identity && _homeApi.IsCurrentIdentityContext(identity))
        {
            _sectionLoadCts?.Cancel();
            _sectionLoadCts?.Dispose();
            _sectionLoadCts = new CancellationTokenSource();
            ++_sectionLoadGeneration;
            _loadedContext = context;
            InvalidateCache();
            LocalLog.AppendLine("home_refresh.txt", $"transport_catchup | readGeneration={context.RequestContextGeneration} | retainedRows={Sections.Count}");
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loadInProgress) return;

        var profileId = _authService.SelectedProfileId;
        var context = _homeApi.CaptureContext();
        if (_loadedContext != context || !string.Equals(_loadedProfileId, profileId, StringComparison.Ordinal))
        {
            SurfaceGeneration++;
            _sectionLoadCts?.Cancel();
            ++_sectionLoadGeneration;
            FeaturedSections.Clear();
            Sections.Clear();
            _rawSections.Clear();
            _completionWantedSections.Clear();
            _completionRetryAfter.Clear();
            _progressRemovals.Clear();
            _layoutOrder = [];
            _avoidDuplicateTypes.Clear();
            ClearDismissState();
            ShowUndoBanner = false;
            HasConfiguredSections = false;
            _hasLoadedLayout = false;
            _lastLoadedAt = DateTime.MinValue;
            _loadedProfileId = profileId;
            _loadedContext = context;
            _loadedIdentityContext = _homeApi.CaptureIdentityContext();
        }

        // Skip API call if data was loaded recently and we already have content
        if (_hasLoadedLayout
            && DateTime.UtcNow - _lastLoadedAt < CacheDuration)
        {
            return;
        }

        _loadInProgress = true;
        var hadContent = FeaturedSections.Count + Sections.Count > 0;
        IsLoading = !hadContent;
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);

        try
        {
            // F12: phase 1 — fetch layout (metadata only, no items). Renders
            // the section skeleton immediately so the user sees structure
            // while items fetch in the background.
            var layout = await _homeApi.GetLayoutAsync();
            if (!_homeApi.IsCurrentContext(context)) return;

            _layoutOrder = layout.Sections;
            var activeIds = layout.Sections.Select(section => section.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var staleId in _rawSections.Keys.Where(id => !activeIds.Contains(id)).ToList())
                _rawSections.Remove(staleId);
            _completionWantedSections.RemoveWhere(id => !activeIds.Contains(id));
            foreach (var staleId in _completionRetryAfter.Keys.Where(id => !activeIds.Contains(id)).ToList())
                _completionRetryAfter.Remove(staleId);

            var previous = FeaturedSections
                .Concat(Sections)
                .ToDictionary(section => section.Id, StringComparer.Ordinal);
            var nextFeatured = new List<HomeSectionWithItems>();
            var nextRows = new List<HomeSectionWithItems>();

            // Preserve populated slots when the recipe is unchanged. The
            // current WebUI keeps cached rows mounted while refreshing; doing
            // the same avoids a full blank/skeleton flash on every stale
            // refresh or playback-state invalidation.
            var heroSectionId = layout.Sections.FirstOrDefault(section => section.Featured)?.Id;
            foreach (var meta in layout.Sections)
            {
                HomeSectionWithItems slot;
                if (previous.TryGetValue(meta.Id, out var cached)
                    && MetadataMatches(cached, meta)
                    && !cached.LoadFailed)
                {
                    slot = cached;
                }
                else
                {
                    slot = CreateLayoutSlot(meta, cached);
                }

                // Match WebUI homeSectionState: the first featured section is
                // the hero slot; every other layout section, including any
                // additional featured section, remains visible as a normal row.
                if (string.Equals(meta.Id, heroSectionId, StringComparison.Ordinal))
                    nextFeatured.Add(slot);
                else
                    nextRows.Add(slot);
            }

            var featuredChanged = ReconcileCollection(FeaturedSections, nextFeatured);
            var rowsChanged = ReconcileCollection(Sections, nextRows);
            if (featuredChanged || rowsChanged)
                BumpRenderRevision();
            HasConfiguredSections = layout.Sections.Count > 0;
            _hasLoadedLayout = true;

            _lastLoadedAt = DateTime.UtcNow;
            IsLoading = false;  // Skeleton is showing; background fetch fills it in.

            // F12: phase 2 — fetch each section's items with a concurrency
            // cap so slow sections don't block fast ones. Each section is a
            // fire-and-forget task that patches its placeholder when done.
            _sectionLoadCts?.Cancel();
            _sectionLoadCts?.Dispose();
            _sectionLoadCts = new CancellationTokenSource();
            var generation = ++_sectionLoadGeneration;
            ApplySectionProjection();
            _ = FetchSectionItemsInBatchesAsync(generation, _sectionLoadCts.Token);
        }
        catch (Exception ex)
        {
            if (!_homeApi.IsCurrentContext(context)) return;
            if (!hadContent)
                SetLocalizedMessage(nameof(ErrorMessage), () => SiloPlayer.Core.Localization.AppStrings.Get("web.home.page.loadFailedTitle", fallback: "Unable to load the homepage"), value => ErrorMessage = value);
            else
                LocalLog.AppendLine("home_error.txt", $"layout_refresh | {ex.GetType().Name}: {ex.Message}");
            IsLoading = false;
        }
        finally
        {
            _loadInProgress = false;
            if (_localizedReloadPending)
            {
                _localizedReloadPending = false;
                if (_isActive) _ = LoadAsync();
            }
            if (!_homeApi.IsCurrentContext(context))
            {
                IsLoading = false;
                // A cached page can remain mounted while its profile/server
                // changes. Finish the new owner even when its first load was
                // coalesced behind this retired request.
                if (_isActive) _ = LoadAsync();
            }
        }
    }

    private async Task FetchSectionItemsInBatchesAsync(int generation, CancellationToken ct)
    {
        _ = LoadRecipeTypesAsync(generation, ct);
        // Gather all placeholder sections in display order (featured first).
        var targets = FeaturedSections.Concat(Sections).ToList();
        if (targets.Count == 0) return;
        if (_loadedContext is not { } owner) return;
        var language = _homeApi.LanguageGeneration;
        var removalRevision = _progressRemovalRevision;
        // Capture cache state on the UI thread before semaphore continuations
        // resume on worker threads. The cache itself belongs to this thread.
        var reads = targets.ToDictionary(section => section.Id, section => (
            Whole: ShouldReadWhole(section),
            ConfiguredLimit: WholeItemLimit(section.Id),
            ExpandedLimit: ExpandedItemLimit(section.Id),
            Shown: _rawSections.TryGetValue(section.Id, out var cached) ? HomeSectionDedupe.Snapshot(cached) : null));

        using var gate = new SemaphoreSlim(MaxConcurrentSectionRequests);
        var tasks = targets.Select(async section =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var sectionId = section.Id;
                var isCurrent = false;
                await RunOnUiThreadAsync(() => isCurrent = IsCurrentSectionLoad(generation, ct, sectionId)
                    && _homeApi.IsCurrentReadContext(owner, language)).ConfigureAwait(false);
                if (!isCurrent) return;
                var read = reads[sectionId];
                // Reuse completion's uncapped read for every expanded row,
                // including rows that have since been promoted to the hero.
                var resp = await (read.ExpandedLimit is { } limit
                    ? _homeApi.GetWholeSectionItemsAsync(owner, language, sectionId, limit, ct)
                    : read.Whole ? _homeApi.GetSectionItemsAsync(owner, language, sectionId, ct)
                    : _homeApi.GetInitialSectionItemsAsync(owner, language, sectionId, read.ConfiguredLimit, ct)).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                await RunOnUiThreadAsync(() =>
                {
                    if (!IsCurrentSectionLoad(generation, ct, sectionId)) return;

                    // A successful HTTP response can still contain JSON null or a
                    // section without an items array. Keep the mounted Home row
                    // alive instead of turning a malformed/partial response into
                    // a UI-thread NullReferenceException.
                    var completed = resp?.Section ?? CloneSection(section, loadFailed: false);
                    ApplySectionReadResult(completed, read.Shown, read.Whole, removalRevision);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A newer layout generation owns the surface now.
            }
            catch (Exception ex)
            {
                LogSectionFetchFailure(section.Id, ex);
                try
                {
                    await RunOnUiThreadAsync(() =>
                    {
                        if (!IsCurrentSectionLoad(generation, ct, section.Id)) return;
                        var mounted = FindMountedSection(section.Id);
                        if (mounted is not { LoadCompleted: true })
                            ReplaceInBoundCollection(CloneSection(section, loadFailed: true, loadCompleted: true));
                    }).ConfigureAwait(false);
                }
                catch (Exception uiEx)
                {
                    LogSectionFetchFailure(section.Id, uiEx);
                }
            }
            finally { gate.Release(); }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected when a newer layout refresh supersedes this batch.
        }
    }

    public async Task RetrySectionAsync(string sectionId)
    {
        var current = FeaturedSections.Concat(Sections).FirstOrDefault(s => s.Id == sectionId);
        if (current == null) return;

        var retryGeneration = _sectionLoadGeneration;
        var retryProfileId = _loadedProfileId;
        var retryToken = _sectionLoadCts?.Token ?? CancellationToken.None;
        if (_loadedContext is not { } owner) return;
        var language = _homeApi.LanguageGeneration;
        var removalRevision = _progressRemovalRevision;
        var whole = ShouldReadWhole(current);
        var configuredLimit = WholeItemLimit(sectionId);
        var expandedLimit = ExpandedItemLimit(sectionId);
        var shown = _rawSections.TryGetValue(sectionId, out var retryRaw) ? HomeSectionDedupe.Snapshot(retryRaw) : null;
        ReplaceInBoundCollection(CloneSection(current, loadFailed: false, loadCompleted: false));
        try
        {
            var response = await (expandedLimit is { } limit
                ? _homeApi.GetWholeSectionItemsAsync(owner, language, sectionId, limit, retryToken)
                : whole ? _homeApi.GetSectionItemsAsync(owner, language, sectionId, retryToken)
                : _homeApi.GetInitialSectionItemsAsync(owner, language, sectionId, configuredLimit, retryToken));
            if (!IsCurrentSectionRetry(retryGeneration, retryToken, retryProfileId, sectionId))
                return;

            var completed = response?.Section ?? CloneSection(current, loadFailed: false);
            ApplySectionReadResult(completed, shown, whole, removalRevision);
        }
        catch (Exception ex)
        {
            if (!IsCurrentSectionRetry(retryGeneration, retryToken, retryProfileId, sectionId))
                return;

            LogSectionFetchFailure(sectionId, ex);
            ReplaceInBoundCollection(CloneSection(current, loadFailed: true, loadCompleted: true));
        }
    }

    private bool ShouldReadWhole(HomeSectionWithItems section)
        => _layoutOrder.FirstOrDefault(entry => entry.Id == section.Id)?.Featured == true
            || (_rawSections.TryGetValue(section.Id, out var cached) && HomeRowWindow.IsExpanded(cached));

    private int? ExpandedItemLimit(string sectionId)
        => _rawSections.TryGetValue(sectionId, out var cached) && HomeRowWindow.IsExpanded(cached)
            ? WholeItemLimit(sectionId) : null;

    // Pass the layout's display budget, never the bounded response's echo.
    // HomeApi keeps completion uncapped independently of this value.
    private int WholeItemLimit(string sectionId)
        => _layoutOrder.FirstOrDefault(entry => entry.Id == sectionId)?.ItemLimit ?? 0;

    private void ApplySectionReadResult(HomeSectionWithItems completed, HomeSectionWithItems? shown, bool whole, long removalRevision)
    {
        completed.LoadFailed = false;
        completed.LoadCompleted = true;
        completed.Items ??= new ObservableCollection<MediaItem>();
        if (!PrepareReadResult(completed, shown, whole)) return;
        FilterProgressRemovals(completed, removalRevision);
        // Refresh membership, order and card data belong to the server. The
        // stable prefix merge is only for CompleteSectionAsync's first expansion.
        PreserveExpandedState(completed);
        ApplyRefreshedSection(completed, whole);
        // Demand survives a bounded read overtaking completion, but a whole
        // refresh already serviced it; do not chain an identical whole read.
        if (whole || completed.HasReadWhole || !completed.CanComplete)
        {
            _completionWantedSections.Remove(completed.Id);
            _completionRetryAfter.Remove(completed.Id);
        }
        else if (_completionWantedSections.Contains(completed.Id)) _ = CompleteSectionAsync(completed.Id, userDemand: false);
    }

    private void FilterProgressRemovals(HomeSectionWithItems incoming, long readRevision)
    {
        if (incoming.SectionType is not ("continue_watching" or "next_up")) return;
        for (var index = incoming.Items.Count - 1; index >= 0; index--)
        {
            var item = incoming.Items[index];
            if (_progressRemovals.Any(removal => removal.Value.Revision > readRevision &&
                (item.ContentId == removal.Key || (removal.Value.SeriesId is { } series && (item.SeriesId == series || item.ContentId == series)))))
                HomeRowWindow.RemoveAt(incoming, index);
        }
    }

    private bool PrepareReadResult(HomeSectionWithItems incoming, HomeSectionWithItems? shown, bool whole)
    {
        if (!_rawSections.TryGetValue(incoming.Id, out var current)) return true;
        // A whole-row completion can overtake a bounded refresh in the same
        // generation. Its expanded result must keep ownership of the cache.
        if (!whole && HomeRowWindow.IsExpanded(current)) return false;
        if (shown != null) HomeRowWindow.FilterRemoved(shown, current, incoming);
        return true;
    }

    private void PreserveExpandedState(HomeSectionWithItems incoming)
    {
        _rawSections.TryGetValue(incoming.Id, out var cached);
        if (cached?.ShownCount is { } shown)
        {
            // Retain the priority budget and completion order, not stale cards,
            // even while the layout promotes this expanded row to featured.
            incoming.ShownCount = Math.Min(shown, incoming.Items.Count);
            incoming.CompletedOrder = cached.CompletedOrder;
        }
    }

    /// <summary>Recover the rest of a partial row without shifting cards already shown.</summary>
    public Task CompleteSectionAsync(string sectionId) => CompleteSectionAsync(sectionId, userDemand: true);

    private async Task CompleteSectionAsync(string sectionId, bool userDemand)
    {
        var generation = _sectionLoadGeneration;
        var ct = _sectionLoadCts?.Token ?? CancellationToken.None;
        var key = (generation, sectionId);
        // CanComplete excludes successful uncapped reads for both automatic
        // and user demand, including later frames of the same scroll gesture.
        if (!IsCurrentSectionLoad(generation, ct, sectionId)
            || !_rawSections.TryGetValue(sectionId, out var raw) || !raw.CanComplete) return;
        // Only fresh user navigation can bypass a failed cycle's cooldown.
        // In particular, hidden-row projection must not rearm it on every refresh.
        if (!userDemand && _completionRetryAfter.TryGetValue(sectionId, out var retryAfter)
            && DateTimeOffset.UtcNow < retryAfter) return;
        _completionRetryAfter.Remove(sectionId);
        _completionWantedSections.Add(sectionId);
        if (!_completingSections.Add(key)) return;
        var owner = _loadedContext!.Value;
        var language = _homeApi.LanguageGeneration;
        var removalRevision = _progressRemovalRevision;
        var shown = HomeSectionDedupe.Snapshot(raw);
        var itemLimit = WholeItemLimit(sectionId);
        var exhausted = false;
        try
        {
            // Retry one transient failure/null response without requiring a
            // ViewChanged event at maximum offset. Bound retries so a broken
            // endpoint cannot create an automatic request loop.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var applied = false;
                try
                {
                    var response = await _homeApi.GetWholeSectionItemsAsync(owner, language, sectionId, itemLimit, ct).ConfigureAwait(false);
                    await RunOnUiThreadAsync(() =>
                    {
                        if (!IsCurrentSectionLoad(generation, ct, sectionId)
                            || !_homeApi.IsCurrentReadContext(owner, language)
                            || !_rawSections.TryGetValue(sectionId, out var current) || !current.CanComplete
                            || response.Section == null) return;
                        response.Section.Items ??= new ObservableCollection<MediaItem>();
                        FilterProgressRemovals(response.Section, removalRevision);
                        var completed = HomeRowWindow.Complete(shown, response.Section, current, ++_rowCompletionOrder);
                        ApplyRefreshedSection(completed, whole: true);
                        _completionWantedSections.Remove(sectionId);
                        applied = true;
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested || !_homeApi.IsCurrentReadContext(owner, language)) { throw; }
                catch (Exception ex) { LogSectionFetchFailure(sectionId, ex); }

                if (applied) return;
                if (attempt == 1)
                {
                    exhausted = true;
                    return;
                }
                await Task.Delay(RealtimeRefreshDebounce, ct).ConfigureAwait(false);
                var stillWanted = false;
                await RunOnUiThreadAsync(() => stillWanted = IsCurrentSectionLoad(generation, ct, sectionId)
                    && _homeApi.IsCurrentReadContext(owner, language)
                    && _completionWantedSections.Contains(sectionId)).ConfigureAwait(false);
                if (!stillWanted) return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !_homeApi.IsCurrentReadContext(owner, language)) { }
        catch (Exception ex)
        {
            LogSectionFetchFailure(sectionId, ex);
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                if (exhausted && IsCurrentSectionLoad(generation, ct, sectionId)
                    && _homeApi.IsCurrentReadContext(owner, language))
                {
                    _completionWantedSections.Remove(sectionId);
                    _completionRetryAfter[sectionId] = DateTimeOffset.UtcNow + CompletionRetryCooldown;
                }
                _completingSections.Remove(key);
            }).ConfigureAwait(false);
        }
    }

    private static HomeSectionWithItems CloneSection(
        HomeSectionWithItems source,
        bool loadFailed,
        bool? loadCompleted = null) => new()
    {
        Id = source.Id,
        SectionType = source.SectionType,
        Title = source.Title,
        TitleKey = source.TitleKey,
        TitleArgs = SectionTitles.CopyArguments(source.TitleArgs),
        KeyedTitle = source.KeyedTitle,
        Featured = source.Featured,
        ItemLimit = source.ItemLimit,
        TotalCount = source.TotalCount,
        IsCustom = source.IsCustom,
        Customized = source.Customized,
        LoadFailed = loadFailed,
        LoadCompleted = loadCompleted ?? source.LoadCompleted,
        HasMore = source.HasMore,
        HasReadWhole = source.HasReadWhole,
        ShownCount = source.ShownCount,
        CompletedOrder = source.CompletedOrder,
        ReadLanguageGeneration = source.ReadLanguageGeneration,
        Items = loadFailed
            ? new ObservableCollection<MediaItem>()
            : new ObservableCollection<MediaItem>(source.Items ?? []),
    };

    private static HomeSectionWithItems CreateLayoutSlot(
        HomeSection meta,
        HomeSectionWithItems? cached) => new()
    {
        Id = meta.Id,
        SectionType = meta.SectionType,
        Title = meta.Title,
        TitleKey = meta.TitleKey,
        TitleArgs = SectionTitles.CopyArguments(meta.TitleArgs),
        KeyedTitle = meta.KeyedTitle,
        Featured = meta.Featured,
        ItemLimit = meta.ItemLimit,
        TotalCount = cached?.TotalCount ?? 0,
        IsCustom = meta.IsCustom,
        Customized = meta.Customized,
        LoadFailed = false,
        LoadCompleted = cached?.LoadCompleted == true && cached.LoadFailed == false,
        HasMore = cached?.HasMore ?? false,
        HasReadWhole = cached?.HasReadWhole ?? false,
        ShownCount = cached?.ShownCount,
        CompletedOrder = cached?.CompletedOrder ?? 0,
        ReadLanguageGeneration = cached?.ReadLanguageGeneration,
        Items = cached?.LoadCompleted == true && cached.LoadFailed == false
            ? cached.Items
            : new ObservableCollection<MediaItem>(),
    };

    private static bool MetadataMatches(HomeSectionWithItems current, HomeSection meta) =>
        current.Id == meta.Id
        && current.SectionType == meta.SectionType
        && current.Title == meta.Title
        && current.TitleKey == meta.TitleKey
        && current.KeyedTitle == meta.KeyedTitle
        && ((current.TitleArgs == null && meta.TitleArgs == null)
            || (current.TitleArgs != null && meta.TitleArgs != null && current.TitleArgs.Count == meta.TitleArgs.Count
                && current.TitleArgs.All(pair => meta.TitleArgs.TryGetValue(pair.Key, out var value) && value == pair.Value)))
        && current.Featured == meta.Featured
        && current.ItemLimit == meta.ItemLimit
        && current.IsCustom == meta.IsCustom
        && current.Customized == meta.Customized;

    private static bool ReconcileCollection(
        ObservableCollection<HomeSectionWithItems> target,
        IReadOnlyList<HomeSectionWithItems> desired)
    {
        if (target.Count == desired.Count
            && target.Select(section => section.Id).SequenceEqual(desired.Select(section => section.Id), StringComparer.Ordinal))
        {
            var changed = false;
            for (var i = 0; i < desired.Count; i++)
            {
                if (!ReferenceEquals(target[i], desired[i]))
                {
                    target[i] = desired[i];
                    changed = true;
                }
            }
            return changed;
        }

        target.Clear();
        foreach (var section in desired)
            target.Add(section);
        return true;
    }

    private bool IsCurrentSectionLoad(int generation, CancellationToken ct, string sectionId) =>
        !ct.IsCancellationRequested
        && generation == _sectionLoadGeneration
        && _loadedContext.HasValue && _homeApi.IsCurrentContext(_loadedContext.Value)
        && FeaturedSections.Concat(Sections).Any(section => section.Id == sectionId);

    private bool IsCurrentSectionRetry(
        int generation,
        CancellationToken ct,
        string? profileId,
        string sectionId) =>
        string.Equals(_loadedProfileId, profileId, StringComparison.Ordinal)
        && IsCurrentSectionLoad(generation, ct, sectionId);

    private Task RunOnUiThreadAsync(Action action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        var actionId = LocalLog.CurrentOperationId;
        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? _dispatcher;
        if (dispatcher == null || dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                LocalLog.AppendException("errors.txt", ex, $"HomeViewModel.RunOnUiThreadAsync caller={operation}", actionId);
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue home section update on the UI thread."));
        }

        return tcs.Task;
    }

    private static void LogSectionFetchFailure(string sectionId, Exception ex)
    {
        LocalLog.AppendLine("home_error.txt", $"section={sectionId} | {ex}");
    }

    private void ReplaceInBoundCollection(HomeSectionWithItems updated)
    {
        // Home sections are observed as a whole via CollectionChanged on
        // FeaturedSections / Sections. Replacing the item at its current
        // index fires Replace notifications so HomePage can re-render just
        // that row. This is still O(N) but N is ≤ ~20 for home sections.
        for (int i = 0; i < FeaturedSections.Count; i++)
        {
            if (FeaturedSections[i].Id == updated.Id)
            {
                FeaturedSections[i] = updated;
                BumpRenderRevision();
                return;
            }
        }
        for (int i = 0; i < Sections.Count; i++)
        {
            if (Sections[i].Id == updated.Id)
            {
                Sections[i] = updated;
                BumpRenderRevision();
                return;
            }
        }
    }

    private HomeSectionWithItems? FindMountedSection(string sectionId) =>
        FeaturedSections.Concat(Sections).FirstOrDefault(section => section.Id == sectionId);

    private void ApplyRefreshedSection(HomeSectionWithItems updated, bool whole = false)
    {
        _rawSections[updated.Id] = HomeSectionDedupe.Snapshot(updated);
        ApplySectionProjection(whole ? updated.Id : null);
    }

    private void ApplySectionProjection(string? wholeReadSectionId = null)
    {
        var projected = HomeSectionDedupe.Project(_layoutOrder, _rawSections, _avoidDuplicateTypes);
        foreach (var entry in _layoutOrder)
        {
            if (!projected.TryGetValue(entry.Id, out var section)) continue;
            // Layout metadata owns hero placement. A cached partial response
            // must not demote a row that the new layout just made featured.
            if (section.Featured != entry.Featured)
            {
                section = HomeSectionDedupe.Snapshot(section);
                section.Featured = entry.Featured;
            }
            if (TryApplyRefreshedSection(FeaturedSections, section)) continue;
            TryApplyRefreshedSection(Sections, section);
        }
        // A duplicate-filtered or dismissed partial row has no carousel left
        // to report near-end demand. Recover it once, before expansion. Even a
        // successful whole read may remain hidden and truncated; do not
        // automatically repeat it when projecting other rows.
        foreach (var section in FeaturedSections.Concat(Sections).Where(section => section.CanComplete && section.Items.Count == 0
            && section.Id != wholeReadSectionId
            && _rawSections.TryGetValue(section.Id, out var raw) && !raw.HasReadWhole && !HomeRowWindow.IsExpanded(raw)).ToList())
            _ = CompleteSectionAsync(section.Id, userDemand: false);
    }

    private async Task LoadRecipeTypesAsync(int generation, CancellationToken ct)
    {
        if (_settingsApi == null) return;
        try
        {
            var catalog = await _settingsApi.GetRecipeCatalogAsync(ct).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
            {
                if (ct.IsCancellationRequested || generation != _sectionLoadGeneration
                    || !_loadedContext.HasValue || !_homeApi.IsCurrentContext(_loadedContext.Value)) return;
                _avoidDuplicateTypes = HomeSectionDedupe.AvoidTypes(catalog);
                ApplySectionProjection();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Older servers can omit the recipe gallery. Keep rows as served.
            LocalLog.AppendLine("home_error.txt", $"recipe_catalog | {ex.GetType().Name}");
        }
    }

    private bool TryApplyRefreshedSection(
        ObservableCollection<HomeSectionWithItems> sections,
        HomeSectionWithItems updated)
    {
        NotInterestedPolicy.FilterMarked(updated);
        for (var i = 0; i < sections.Count; i++)
        {
            var mounted = sections[i];
            if (!string.Equals(mounted.Id, updated.Id, StringComparison.Ordinal))
                continue;

            // The initial empty layout slot is cheap to replace once. Every
            // subsequent refresh reconciles against the mounted collection so
            // unchanged cards, images, focus, and scroll position stay alive.
            if (!mounted.LoadCompleted && mounted.Items.Count == 0)
            {
                sections[i] = HomeSectionDedupe.Snapshot(updated);
                BumpRenderRevision();
                return true;
            }

            var change = HomeSectionReconciler.Apply(mounted, updated);
            if (change == HomeSectionChange.None)
                return true;

            if (change.HasFlag(HomeSectionChange.Metadata))
                sections[i] = mounted;
            BumpRenderRevision();
            return true;
        }
        return false;
    }

    public void InvalidateCache()
    {
        _lastLoadedAt = DateTime.MinValue;
    }

    public void InvalidateLocalizedReads()
    {
        _localizedReloadPending |= _loadInProgress;
        _sectionLoadCts?.Cancel();
        _sectionLoadCts?.Dispose();
        _sectionLoadCts = new CancellationTokenSource();
        ++_sectionLoadGeneration;
        QueueRealtimeRefresh("metadata_language");
    }

    [RelayCommand]
    private async Task DismissItemAsync(DismissRequest request)
    {
        if (request.Section == null || request.Item == null) return;

        var surface = request.Section.SectionType switch
        {
            "continue_watching" => "continue_watching",
            "next_up" => "next_up",
            _ => null
        };
        if (surface == null) return;
        var dismissalSeriesId = HomeDismissalPolicy.SeriesIdentity(request.Item);
        if (surface == "next_up" && string.IsNullOrWhiteSpace(dismissalSeriesId)) return;

        // Save undo state
        _lastDismissedSurface = surface;
        _lastDismissedItemId = request.Item.ContentId;
        _lastDismissedItem = request.Item;
        _lastDismissedSection = request.Section;
        _lastDismissedIndex = request.Section.Items.IndexOf(request.Item);
        _lastDismissedWasInitial = !request.Section.ShownCount.HasValue || _lastDismissedIndex < request.Section.ShownCount.Value;

        // Remove from UI immediately
        var dismissedIndex = request.Section.Items.IndexOf(request.Item);
        if (dismissedIndex >= 0) HomeRowWindow.RemoveAt(request.Section, dismissedIndex);
        if (_rawSections.TryGetValue(request.Section.Id, out var raw))
        {
            var original = raw.Items.FirstOrDefault(item => item.ContentId == request.Item.ContentId);
            if (original != null) HomeRowWindow.RemoveAt(raw, raw.Items.IndexOf(original));
        }
        BumpRenderRevision();

        // Show undo banner
        UndoMessage = $"\"{request.Item.Title}\" dismissed";
        ShowUndoBanner = true;

        try
        {
            var body = surface == "continue_watching"
                ? new { progress_updated_at = request.Item.ProgressUpdatedAt }
                : (object)new { series_id = dismissalSeriesId! };

            if (surface == "continue_watching" && string.IsNullOrWhiteSpace(request.Item.ProgressUpdatedAt))
                throw new InvalidOperationException("Continue-watching item is missing its progress timestamp.");

            await _homeApi.DismissItemAsync(surface, request.Item.ContentId, body);
        }
        catch (Exception ex)
        {
            // Restore on failure
            LocalLog.AppendException("errors.txt", ex, "HomeViewModel.DismissItemAsync");
            RestoreDismissedItem();
            QueueRealtimeRefresh("home_dismissal_undo", _loadedContext);
            ShowUndoBanner = false;
            ClearDismissState();
            return;
        }

        // Auto-hide after 5 seconds
        _ = AutoHideUndoBannerAsync();
    }

    [RelayCommand]
    private async Task UndoDismissalAsync()
    {
        if (_lastDismissedSurface == null || _lastDismissedItemId == null) return;
        // Bind the DELETE to the dismissal's owner: a profile, access or server change that
        // lands after this check refuses the request instead of sending it under the replacement.
        var owner = _dismissContext ?? _homeApi.CaptureContext();
        if (!_homeApi.IsCurrentContext(owner)) { ClearDismissState(); ShowUndoBanner = false; return; }
        var generation = _undoGeneration;

        ShowUndoBanner = false;

        try
        {
            await _homeApi.UndoDismissalAsync(owner, _lastDismissedSurface, _lastDismissedItemId);
            if (generation != _undoGeneration || (_dismissContext.HasValue && !_homeApi.IsCurrentContext(_dismissContext.Value))) return;
            RestoreDismissedItem();
            QueueRealtimeRefresh("home_dismissal_undo", _loadedContext);
        }
        catch (Exception ex)
        {
            // A retired owner was refused, not failed: nothing here is left to retry.
            if (!_homeApi.IsCurrentContext(owner)) { if (generation == _undoGeneration) ClearDismissState(); return; }
            LocalLog.AppendException("errors.txt", ex, "HomeViewModel.UndoDismissalAsync");
            if (generation == _undoGeneration) { SetLocalizedMessage(nameof(ErrorMessage), () => SiloPlayer.Core.Localization.AppStrings.Get("web.home.dismiss.undoFailed", fallback: "Could not undo the dismissal. Try again."), value => ErrorMessage = value); ShowUndoBanner = true; }
            return;
        }

        ClearDismissState();
    }

    private void RestoreDismissedItem()
    {
        // Responses read before Undo describe the dismissed state. Retire
        // their generation before restoring the visible/cache snapshot.
        _sectionLoadCts?.Cancel();
        _sectionLoadCts?.Dispose();
        _sectionLoadCts = new CancellationTokenSource();
        ++_sectionLoadGeneration;
        if (_lastDismissedItemId != null) _progressRemovals.Remove(_lastDismissedItemId);
        if (_dismissSnapshot != null)
        {
            _dismissSnapshot.Restore(FeaturedSections.Concat(Sections));
            _dismissSnapshot.Restore(_rawSections.Values);
            BumpRenderRevision(); return;
        }
        if (_lastDismissedSection != null && _lastDismissedItem != null)
        {
            var idx = Math.Min(_lastDismissedIndex, _lastDismissedSection.Items.Count);
            _lastDismissedSection.Items.Insert(idx, _lastDismissedItem);
            if (_lastDismissedWasInitial && _lastDismissedSection.ShownCount.HasValue) _lastDismissedSection.ShownCount++;
            if (_rawSections.TryGetValue(_lastDismissedSection.Id, out var raw)
                && !raw.Items.Any(item => item.ContentId == _lastDismissedItem.ContentId))
            {
                raw.Items.Insert(Math.Min(_lastDismissedIndex, raw.Items.Count), _lastDismissedItem);
                if (_lastDismissedWasInitial && raw.ShownCount.HasValue) raw.ShownCount++;
            }
            BumpRenderRevision();
        }
    }

    private void ClearDismissState()
    {
        _dismissSnapshot = null; _dismissContext = null;
        _undoGeneration++;
        _lastDismissedSurface = null;
        _lastDismissedItemId = null;
        _lastDismissedItem = null;
        _lastDismissedSection = null;
        _lastDismissedIndex = 0;
        _lastDismissedWasInitial = false;
    }

    private async Task AutoHideUndoBannerAsync()
    {
        var generation = ++_undoGeneration;
        await Task.Delay(5000);
        if (ShowUndoBanner && generation == _undoGeneration)
        {
            ShowUndoBanner = false;
            ClearDismissState();
        }
    }
}

public class DismissRequest
{
    public HomeSectionWithItems? Section { get; set; }
    public MediaItem? Item { get; set; }
}
