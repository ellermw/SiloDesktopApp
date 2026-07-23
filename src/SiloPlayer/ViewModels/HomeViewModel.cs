using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly HomeApi _homeApi;
    private readonly AuthService _authService;

    public HomeViewModel(HomeApi homeApi, AuthService authService)
    {
        _homeApi = homeApi;
        _authService = authService;
        // F4: subscribe to media state changes + playback progress so the
        // home screen's Continue Watching / Next Up rows reflect activity
        // from anywhere in the app without a full reload.
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this);
        WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this);
    }

    // F4 — messenger receivers.
    public void Receive(MediaSurfaceChanged message)
    {
        // Any favorite/watchlist/watched/rating change could affect downstream
        // recommendations + continue-watching positioning. Invalidate cache so
        // the next navigation back to home triggers a fresh fetch.
        switch (message.Kind)
        {
            case MediaSurfaceChangeKind.WatchedMarked:
                // Mark-watched should also drop the item from Continue Watching
                // and Next Up immediately, not wait for a fetch.
                RemoveFromProgressRows(message.ContentId, message.SeriesId);
                InvalidateCache();
                break;
            case MediaSurfaceChangeKind.WatchedCleared:
            case MediaSurfaceChangeKind.FavoriteAdded:
            case MediaSurfaceChangeKind.FavoriteRemoved:
            case MediaSurfaceChangeKind.WatchlistAdded:
            case MediaSurfaceChangeKind.WatchlistRemoved:
            case MediaSurfaceChangeKind.RatingChanged:
            case MediaSurfaceChangeKind.HomeLayoutChanged:
                InvalidateCache();
                break;
            case MediaSurfaceChangeKind.HomeDismissed:
                RemoveFromProgressRows(message.ContentId, message.SeriesId);
                InvalidateCache();
                break;
        }
    }

    public void Receive(PlaybackProgressUpdated message)
    {
        // Update the Continue Watching row in place so returning from the
        // player shows the latest progress. If the user actually finished
        // the item, drop it from CW / Next Up.
        foreach (var section in FeaturedSections.Concat(Sections))
        {
            if (section.SectionType is not ("continue_watching" or "next_up")) continue;
            for (int i = 0; i < section.Items.Count; i++)
            {
                var item = section.Items[i];
                if (item.ContentId != message.ContentId) continue;

                if (message.Completed)
                {
                    section.Items.RemoveAt(i);
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

        // Next-refresh fetches the authoritative ordering.
        InvalidateCache();
    }

    private void RemoveFromProgressRows(string contentId, string? seriesId)
    {
        var removedAny = false;
        foreach (var section in FeaturedSections.Concat(Sections))
        {
            if (section.SectionType is not ("continue_watching" or "next_up")) continue;
            for (int i = section.Items.Count - 1; i >= 0; i--)
            {
                var item = section.Items[i];
                if (item.ContentId == contentId
                    || (seriesId != null && item.SeriesId == seriesId))
                {
                    section.Items.RemoveAt(i);
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
    private string? _lastDismissedItemId;
    private MediaItem? _lastDismissedItem;
    private HomeSectionWithItems? _lastDismissedSection;
    private int _lastDismissedIndex;

    private DateTime _lastLoadedAt = DateTime.MinValue;
    private bool _loadInProgress;
    private bool _hasLoadedLayout;
    private CancellationTokenSource? _sectionLoadCts;
    private int _sectionLoadGeneration;
    private string? _loadedProfileId;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    // F12: maximum number of concurrent per-section fetches. Matches the
    // webui `MAX_CONCURRENT_SECTION_REQUESTS = 5` limit so we don't overwhelm
    // the server with 15+ parallel section requests.
    private const int MaxConcurrentSectionRequests = 5;

    [ObservableProperty]
    private bool _hasConfiguredSections;

    [ObservableProperty]
    private int _renderRevision;

    private void BumpRenderRevision() => RenderRevision++;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loadInProgress) return;

        var profileId = _authService.SelectedProfileId;
        if (!string.Equals(_loadedProfileId, profileId, StringComparison.Ordinal))
        {
            _sectionLoadCts?.Cancel();
            FeaturedSections.Clear();
            Sections.Clear();
            HasConfiguredSections = false;
            _hasLoadedLayout = false;
            _lastLoadedAt = DateTime.MinValue;
            _loadedProfileId = profileId;
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
        ErrorMessage = null;

        try
        {
            // F12: phase 1 — fetch layout (metadata only, no items). Renders
            // the section skeleton immediately so the user sees structure
            // while items fetch in the background.
            var layout = await _homeApi.GetLayoutAsync();

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
            _ = FetchSectionItemsInBatchesAsync(generation, _sectionLoadCts.Token);
        }
        catch (Exception ex)
        {
            if (!hadContent)
                ErrorMessage = "Unable to load the homepage";
            else
                LocalLog.AppendLine("home_error.txt", $"layout_refresh | {ex.GetType().Name}: {ex.Message}");
            IsLoading = false;
        }
        finally
        {
            _loadInProgress = false;
        }
    }

    private async Task FetchSectionItemsInBatchesAsync(int generation, CancellationToken ct)
    {
        // Gather all placeholder sections in display order (featured first).
        var targets = FeaturedSections.Concat(Sections).ToList();
        if (targets.Count == 0) return;

        using var gate = new SemaphoreSlim(MaxConcurrentSectionRequests);
        var tasks = targets.Select(async section =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var sectionId = section.Id;
                var resp = await _homeApi.GetSectionItemsAsync(sectionId, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                await RunOnUiThreadAsync(() =>
                {
                    if (!IsCurrentSectionLoad(generation, ct, sectionId)) return;

                    var completed = resp.Section ?? CloneSection(section, loadFailed: false);
                    completed.LoadFailed = false;
                    completed.LoadCompleted = true;
                    completed.Items ??= new ObservableCollection<MediaItem>();
                    ReplaceInBoundCollection(completed);
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
        ReplaceInBoundCollection(CloneSection(current, loadFailed: false, loadCompleted: false));
        try
        {
            var response = await _homeApi.GetSectionItemsAsync(sectionId);
            if (!IsCurrentSectionRetry(retryGeneration, retryToken, retryProfileId, sectionId))
                return;

            var completed = response.Section ?? CloneSection(current, loadFailed: false);
            completed.LoadFailed = false;
            completed.LoadCompleted = true;
            completed.Items ??= new ObservableCollection<MediaItem>();
            ReplaceInBoundCollection(completed);
        }
        catch (Exception ex)
        {
            if (!IsCurrentSectionRetry(retryGeneration, retryToken, retryProfileId, sectionId))
                return;

            LogSectionFetchFailure(sectionId, ex);
            ReplaceInBoundCollection(CloneSection(current, loadFailed: true, loadCompleted: true));
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
        Featured = source.Featured,
        ItemLimit = source.ItemLimit,
        TotalCount = source.TotalCount,
        IsCustom = source.IsCustom,
        Customized = source.Customized,
        LoadFailed = loadFailed,
        LoadCompleted = loadCompleted ?? source.LoadCompleted,
        Items = loadFailed
            ? new ObservableCollection<MediaItem>()
            : new ObservableCollection<MediaItem>(source.Items),
    };

    private static HomeSectionWithItems CreateLayoutSlot(
        HomeSection meta,
        HomeSectionWithItems? cached) => new()
    {
        Id = meta.Id,
        SectionType = meta.SectionType,
        Title = meta.Title,
        Featured = meta.Featured,
        ItemLimit = meta.ItemLimit,
        TotalCount = cached?.TotalCount ?? 0,
        IsCustom = meta.IsCustom,
        Customized = meta.Customized,
        LoadFailed = false,
        LoadCompleted = cached?.LoadCompleted == true && cached.LoadFailed == false,
        Items = cached?.LoadCompleted == true && cached.LoadFailed == false
            ? new ObservableCollection<MediaItem>(cached.Items)
            : new ObservableCollection<MediaItem>(),
    };

    private static bool MetadataMatches(HomeSectionWithItems current, HomeSection meta) =>
        current.Id == meta.Id
        && current.SectionType == meta.SectionType
        && current.Title == meta.Title
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
        && FeaturedSections.Concat(Sections).Any(section => section.Id == sectionId);

    private bool IsCurrentSectionRetry(
        int generation,
        CancellationToken ct,
        string? profileId,
        string sectionId) =>
        string.Equals(_loadedProfileId, profileId, StringComparison.Ordinal)
        && IsCurrentSectionLoad(generation, ct, sectionId);

    private static Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
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
        LocalLog.AppendLine("home_error.txt", $"section={sectionId} | {ex.GetType().Name}: {ex.Message}");
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

    public void InvalidateCache()
    {
        _lastLoadedAt = DateTime.MinValue;
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

        // Save undo state
        _lastDismissedSurface = surface;
        _lastDismissedItemId = request.Item.ContentId;
        _lastDismissedItem = request.Item;
        _lastDismissedSection = request.Section;
        _lastDismissedIndex = request.Section.Items.IndexOf(request.Item);

        // Remove from UI immediately
        request.Section.Items.Remove(request.Item);
        BumpRenderRevision();

        // Show undo banner
        UndoMessage = $"\"{request.Item.Title}\" dismissed";
        ShowUndoBanner = true;

        try
        {
            var body = surface == "continue_watching"
                ? new { progress_updated_at = request.Item.ProgressUpdatedAt }
                : (object)new { series_id = request.Item.SeriesId ?? request.Item.ContentId };

            if (surface == "continue_watching" && string.IsNullOrWhiteSpace(request.Item.ProgressUpdatedAt))
                throw new InvalidOperationException("Continue-watching item is missing its progress timestamp.");

            await _homeApi.DismissItemAsync(surface, request.Item.ContentId, body);
        }
        catch
        {
            // Restore on failure
            RestoreDismissedItem();
        }

        // Auto-hide after 5 seconds
        _ = AutoHideUndoBannerAsync();
    }

    [RelayCommand]
    private async Task UndoDismissalAsync()
    {
        if (_lastDismissedSurface == null || _lastDismissedItemId == null) return;

        ShowUndoBanner = false;

        try
        {
            await _homeApi.UndoDismissalAsync(_lastDismissedSurface, _lastDismissedItemId);
            RestoreDismissedItem();
        }
        catch
        {
            // Undo failed -- item stays dismissed
        }

        ClearDismissState();
    }

    private void RestoreDismissedItem()
    {
        if (_lastDismissedSection != null && _lastDismissedItem != null)
        {
            var idx = Math.Min(_lastDismissedIndex, _lastDismissedSection.Items.Count);
            _lastDismissedSection.Items.Insert(idx, _lastDismissedItem);
            BumpRenderRevision();
        }
    }

    private void ClearDismissState()
    {
        _lastDismissedSurface = null;
        _lastDismissedItemId = null;
        _lastDismissedItem = null;
        _lastDismissedSection = null;
        _lastDismissedIndex = 0;
    }

    private async Task AutoHideUndoBannerAsync()
    {
        await Task.Delay(5000);
        if (ShowUndoBanner)
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
