using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.ViewModels;

public partial class RecommendationsViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly RecommendationsApi _recommendationsApi;
    private DateTime _lastLoadedAt = DateTime.MinValue;
    private bool _loadInProgress;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

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
    private bool _isTasteProfileLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loadInProgress) return;
        if (Rows.Count > 0 && DateTime.UtcNow - _lastLoadedAt < CacheDuration) return;

        _loadInProgress = true;
        var isInitialLoad = Rows.Count == 0;
        IsLoading = isInitialLoad;
        IsTasteProfileLoading = TasteProfile == null;
        if (isInitialLoad)
            ErrorMessage = null;

        try
        {
            // Match React Query's behavior: retain the mounted result while a
            // stale refresh runs, then replace the rows in one UI-thread pass.
            // This avoids flashing an empty page and rebuilding each row twice.
            var profileTask = GetTasteProfileAsync();
            var rowsTask = GetDiscoverRowsAsync();
            await Task.WhenAll(profileTask, rowsTask);

            var profile = await profileTask;
            var rows = await rowsTask;

            TasteProfile = profile;
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(row);

            ErrorMessage = null;
            _lastLoadedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recommendations load failed: {ex}");
            if (Rows.Count == 0)
                ErrorMessage = "Failed to load recommendations.";
        }
        finally
        {
            _loadInProgress = false;
            IsLoading = false;
            IsTasteProfileLoading = false;
        }
    }

    private async Task<TasteProfileResponse?> GetTasteProfileAsync()
    {
        try
        {
            return await _recommendationsApi.GetTasteProfileAsync();
        }
        catch
        {
            // Non-fatal: section is just hidden if no profile is available.
            return TasteProfile;
        }
    }

    private async Task<IReadOnlyList<RecommendationRowDisplay>> GetDiscoverRowsAsync()
    {
        try
        {
            var response = await _recommendationsApi.GetDiscoverAsync();
            var rows = new List<RecommendationRowDisplay>();

            foreach (var row in response.Rows)
            {
                var displayRow = new RecommendationRowDisplay
                {
                    Label = row.Label,
                    Type = row.Type,
                    SectionKind = row.SectionKind,
                    SectionKey = row.SectionKey
                };

                foreach (var item in row.Items)
                    displayRow.Items.Add(item);

                if (displayRow.Items.Count > 0)
                    rows.Add(displayRow);
            }

            return rows;
        }
        catch (Exception discoverError)
        {
            try
            {
                return await GetLegacyForYouRowsAsync();
            }
            catch (Exception legacyError)
            {
                throw new AggregateException(discoverError, legacyError);
            }
        }
    }

    private async Task<IReadOnlyList<RecommendationRowDisplay>> GetLegacyForYouRowsAsync()
    {
        var response = await _catalogApi.GetRecommendationsAsync();
        var rows = new List<RecommendationRowDisplay>();

        foreach (var row in response.Rows)
        {
            var displayRow = new RecommendationRowDisplay
            {
                Label = row.Label,
                Type = row.Type
            };

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
                rows.Add(displayRow);
        }

        return rows;
    }
}

public class RecommendationRowDisplay
{
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";
    public string? SectionKind { get; set; }
    public string? SectionKey { get; set; }
    public ObservableCollection<MediaItem> Items { get; } = [];
}
