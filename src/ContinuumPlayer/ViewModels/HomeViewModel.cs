using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Messaging;

namespace ContinuumPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly HomeApi _homeApi;

    public HomeViewModel(HomeApi homeApi)
    {
        _homeApi = homeApi;
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
                }
            }
        }
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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    // F12: maximum number of concurrent per-section fetches. Matches the
    // webui `MAX_CONCURRENT_SECTION_REQUESTS = 5` limit so we don't overwhelm
    // the server with 15+ parallel section requests.
    private const int MaxConcurrentSectionRequests = 5;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        // Skip API call if data was loaded recently and we already have content
        if (FeaturedSections.Count + Sections.Count > 0
            && DateTime.UtcNow - _lastLoadedAt < CacheDuration)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // F12: phase 1 — fetch layout (metadata only, no items). Renders
            // the section skeleton immediately so the user sees structure
            // while items fetch in the background.
            var layout = await _homeApi.GetLayoutAsync();

            FeaturedSections.Clear();
            Sections.Clear();

            // Seed empty HomeSectionWithItems placeholders so the UI can
            // render loading rows with real titles. Items list stays empty
            // until phase 2 fills it.
            foreach (var meta in layout.Sections)
            {
                var placeholder = new HomeSectionWithItems
                {
                    Id = meta.Id,
                    SectionType = meta.SectionType,
                    Title = meta.Title,
                    Featured = meta.Featured,
                    ItemLimit = meta.ItemLimit,
                    IsCustom = meta.IsCustom,
                    Customized = meta.Customized,
                    Items = new List<MediaItem>(),
                };
                if (meta.Featured)
                    FeaturedSections.Add(placeholder);
                else
                    Sections.Add(placeholder);
            }

            _lastLoadedAt = DateTime.UtcNow;
            IsLoading = false;  // Skeleton is showing; background fetch fills it in.

            // F12: phase 2 — fetch each section's items with a concurrency
            // cap so slow sections don't block fast ones. Each section is a
            // fire-and-forget task that patches its placeholder when done.
            _ = FetchSectionItemsInBatchesAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load home: {ex.Message}";
            IsLoading = false;
        }
    }

    private async Task FetchSectionItemsInBatchesAsync()
    {
        // Gather all placeholder sections in display order (featured first).
        var targets = FeaturedSections.Concat(Sections).ToList();
        if (targets.Count == 0) return;

        using var gate = new SemaphoreSlim(MaxConcurrentSectionRequests);
        var tasks = targets.Select(async section =>
        {
            await gate.WaitAsync();
            try
            {
                var resp = await _homeApi.GetSectionItemsAsync(section.Id);
                if (resp.Section?.Items != null && resp.Section.Items.Count > 0)
                {
                    // Replace the empty placeholder items list in place.
                    // Find the placeholder by reference so we mutate the
                    // collection already bound to the UI.
                    section.Items = resp.Section.Items;
                    // Reassign the reference in the ObservableCollection so
                    // WinUI picks up the new items (HomeSectionWithItems is
                    // not itself observable — HomePage rebuilds rows when
                    // the collection changes).
                    ReplaceInBoundCollection(section);
                }
            }
            catch { /* per-section failure is non-fatal; leave empty. */ }
            finally { gate.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
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
                return;
            }
        }
        for (int i = 0; i < Sections.Count; i++)
        {
            if (Sections[i].Id == updated.Id)
            {
                Sections[i] = updated;
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

        // Show undo banner
        UndoMessage = $"\"{request.Item.Title}\" dismissed";
        ShowUndoBanner = true;

        try
        {
            var body = surface == "continue_watching"
                ? new { progress_updated_at = DateTime.UtcNow.ToString("o") }
                : (object)new { series_id = request.Item.SeriesId ?? request.Item.ContentId };

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
