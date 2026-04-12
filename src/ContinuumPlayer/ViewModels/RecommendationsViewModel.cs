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

    // Rows displayed on the page (web: rows from /recommendations/discover)
    public ObservableCollection<RecommendationRowDisplay> Rows { get; } = [];

    // Taste profile shown in header (web: TasteProfileCard)
    [ObservableProperty]
    private TasteProfileResponse? _tasteProfile;

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
        Rows.Clear();
        TasteProfile = null;

        try
        {
            // Load taste profile and recommendation rows in parallel (web parity).
            var profileTask = LoadTasteProfileAsync();
            var rowsTask = LoadForYouRowsAsync();
            await Task.WhenAll(profileTask, rowsTask);
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

    private async Task LoadTasteProfileAsync()
    {
        try
        {
            TasteProfile = await _recommendationsApi.GetTasteProfileAsync();
        }
        catch
        {
            // Non-fatal: section is just hidden if no profile is available.
        }
    }

    private async Task LoadForYouRowsAsync()
    {
        try
        {
            // Web uses /recommendations/discover; desktop currently exposes
            // /recommendations/for-you/rows which returns the same row shape.
            var response = await _catalogApi.GetRecommendationsAsync();

            foreach (var row in response.Rows)
            {
                var displayRow = new RecommendationRowDisplay
                {
                    Label = row.Label,
                    Type = row.Type
                };

                // Fetch item details for each recommendation in parallel
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
                            UserState = detail.UserData != null ? new UserState { Played = detail.UserData.Played } : null
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
}

public class RecommendationRowDisplay
{
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";
    public ObservableCollection<MediaItem> Items { get; } = [];
}
