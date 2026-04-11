using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly HomeApi _homeApi;

    public HomeViewModel(HomeApi homeApi)
    {
        _homeApi = homeApi;
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
            var response = await _homeApi.GetSectionsAsync();

            FeaturedSections.Clear();
            Sections.Clear();

            foreach (var section in response.Sections)
            {
                if (section.Featured)
                    FeaturedSections.Add(section);
                else
                    Sections.Add(section);
            }

            _lastLoadedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load home: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
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
