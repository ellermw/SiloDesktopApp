using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class RecommendationsViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly RecommendationsApi _recommendationsApi;

    public RecommendationsViewModel(CatalogApi catalogApi, RecommendationsApi recommendationsApi)
    {
        _catalogApi = catalogApi;
        _recommendationsApi = recommendationsApi;
    }

    public ObservableCollection<RecommendationRowDisplay> Rows { get; } = [];

    // Hero section: top "For You" picks
    public ObservableCollection<MediaItem> HeroItems { get; } = [];

    // Popular section
    public ObservableCollection<MediaItem> PopularItems { get; } = [];

    // Recently Added section
    public ObservableCollection<MediaItem> RecentlyAddedItems { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasHeroItems;

    [ObservableProperty]
    private bool _hasPopularItems;

    [ObservableProperty]
    private bool _hasRecentlyAddedItems;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        Rows.Clear();
        HeroItems.Clear();
        PopularItems.Clear();
        RecentlyAddedItems.Clear();
        HasHeroItems = false;
        HasPopularItems = false;
        HasRecentlyAddedItems = false;

        try
        {
            // Load all sections in parallel
            var forYouTask = LoadForYouRowsAsync();
            var heroTask = LoadHeroItemsAsync();
            var popularTask = LoadPopularAsync();
            var recentlyAddedTask = LoadRecentlyAddedAsync();

            await Task.WhenAll(forYouTask, heroTask, popularTask, recentlyAddedTask);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load recommendations: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadForYouRowsAsync()
    {
        try
        {
            var response = await _catalogApi.GetRecommendationsAsync();

            foreach (var row in response.Rows)
            {
                var displayRow = new RecommendationRowDisplay
                {
                    Label = row.Label,
                    Type = row.Type
                };

                // Fetch item details for each recommendation (in parallel)
                var tasks = row.Items.Select(async recItem =>
                {
                    try
                    {
                        var detail = await _catalogApi.GetItemDetailAsync(recItem.MediaItemId);
                        return new MediaItem
                        {
                            ContentId = detail.ContentId,
                            Type = detail.Type,
                            Title = detail.Title,
                            Year = detail.Year,
                            Genres = detail.Genres,
                            Overview = detail.Overview,
                            PosterUrl = detail.PosterUrl,
                            PosterThumbhash = detail.PosterThumbhash,
                            BackdropUrl = detail.BackdropUrl,
                            BackdropThumbhash = detail.BackdropThumbhash,
                            LogoUrl = detail.LogoUrl,
                            UserState = detail.UserState
                        };
                    }
                    catch
                    {
                        return null;
                    }
                }).ToList();

                var items = await Task.WhenAll(tasks);
                foreach (var item in items)
                {
                    if (item != null)
                        displayRow.Items.Add(item);
                }

                if (displayRow.Items.Count > 0)
                    Rows.Add(displayRow);
            }
        }
        catch
        {
            // For You rows load failure is non-fatal
        }
    }

    private async Task LoadHeroItemsAsync()
    {
        try
        {
            var response = await _recommendationsApi.GetForYouMainAsync();
            foreach (var row in response.Rows)
            {
                var tasks = row.Items.Take(5).Select(async recItem =>
                {
                    try
                    {
                        var detail = await _catalogApi.GetItemDetailAsync(recItem.MediaItemId);
                        return new MediaItem
                        {
                            ContentId = detail.ContentId,
                            Type = detail.Type,
                            Title = detail.Title,
                            Year = detail.Year,
                            Genres = detail.Genres,
                            Overview = detail.Overview,
                            PosterUrl = detail.PosterUrl,
                            PosterThumbhash = detail.PosterThumbhash,
                            BackdropUrl = detail.BackdropUrl,
                            BackdropThumbhash = detail.BackdropThumbhash,
                            LogoUrl = detail.LogoUrl,
                            UserState = detail.UserState
                        };
                    }
                    catch { return null; }
                }).ToList();

                var items = await Task.WhenAll(tasks);
                foreach (var item in items)
                {
                    if (item != null)
                        HeroItems.Add(item);
                }
                break; // Only use first row for hero
            }
            HasHeroItems = HeroItems.Count > 0;
        }
        catch
        {
            // Hero items load failure is non-fatal
        }
    }

    private async Task LoadPopularAsync()
    {
        try
        {
            var response = await _recommendationsApi.GetPopularAsync(days: 30);
            var tasks = response.Items.Take(20).Select(async s =>
            {
                try
                {
                    var detail = await _catalogApi.GetItemDetailAsync(s.MediaItemId);
                    return new MediaItem
                    {
                        ContentId = detail.ContentId,
                        Type = detail.Type,
                        Title = detail.Title,
                        Year = detail.Year,
                        Genres = detail.Genres,
                        Overview = detail.Overview,
                        PosterUrl = detail.PosterUrl,
                        PosterThumbhash = detail.PosterThumbhash,
                        BackdropUrl = detail.BackdropUrl,
                        BackdropThumbhash = detail.BackdropThumbhash,
                        LogoUrl = detail.LogoUrl,
                    };
                }
                catch { return null; }
            }).ToList();

            var items = await Task.WhenAll(tasks);
            foreach (var item in items)
            {
                if (item != null)
                    PopularItems.Add(item);
            }
            HasPopularItems = PopularItems.Count > 0;
        }
        catch
        {
            // Popular items load failure is non-fatal
        }
    }

    private async Task LoadRecentlyAddedAsync()
    {
        try
        {
            var response = await _recommendationsApi.GetRecentlyAddedAsync();
            var tasks = response.Items.Take(20).Select(async s =>
            {
                try
                {
                    var detail = await _catalogApi.GetItemDetailAsync(s.MediaItemId);
                    return new MediaItem
                    {
                        ContentId = detail.ContentId,
                        Type = detail.Type,
                        Title = detail.Title,
                        Year = detail.Year,
                        Genres = detail.Genres,
                        Overview = detail.Overview,
                        PosterUrl = detail.PosterUrl,
                        PosterThumbhash = detail.PosterThumbhash,
                        BackdropUrl = detail.BackdropUrl,
                        BackdropThumbhash = detail.BackdropThumbhash,
                        LogoUrl = detail.LogoUrl,
                    };
                }
                catch { return null; }
            }).ToList();

            var items = await Task.WhenAll(tasks);
            foreach (var item in items)
            {
                if (item != null)
                    RecentlyAddedItems.Add(item);
            }
            HasRecentlyAddedItems = RecentlyAddedItems.Count > 0;
        }
        catch
        {
            // Recently added items load failure is non-fatal
        }
    }
}

public class RecommendationRowDisplay
{
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";
    public ObservableCollection<MediaItem> Items { get; } = [];
}
