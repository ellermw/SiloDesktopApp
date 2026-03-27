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

    public RecommendationsViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    public ObservableCollection<RecommendationRowDisplay> Rows { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _catalogApi.GetRecommendationsAsync();
            Rows.Clear();

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
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load recommendations: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public class RecommendationRowDisplay
{
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";
    public ObservableCollection<MediaItem> Items { get; } = [];
}
