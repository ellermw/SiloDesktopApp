using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class RecommendationsViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly RecommendationsApi _recommendationsApi;
    private DateTime _lastLoadedAt = DateTime.MinValue;
    private bool _loadInProgress;
    private bool _reloadRequested;
    private bool _hasLoaded;
    private long _loadGeneration;
    private ApiRequestContext? _loadedContext;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public RecommendationsViewModel(CatalogApi catalogApi, RecommendationsApi recommendationsApi)
    {
        _catalogApi = catalogApi;
        _recommendationsApi = recommendationsApi;
        WeakReferenceMessenger.Default.Register<NotInterestedChange>(this, (_, change) =>
        {
            if (!_catalogApi.IsCurrentContext(change.Context)) return;
            _lastLoadedAt = DateTime.MinValue;
            ++_loadGeneration;
            foreach (var row in Rows)
                for (var index = row.Items.Count - 1; index >= 0; index--)
                    if (row.Items[index].ContentId == change.ItemId && change.Marked.HasValue)
                    {
                        MediaItemStateUpdater.SetNotInterested(row.Items[index], change.Marked.Value);
                        if (change.Marked.Value) row.Items.RemoveAt(index);
                    }
            if (_loadInProgress) _reloadRequested = true;
            else if (_hasLoaded) _ = LoadAsync();
        });
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
        var context = _catalogApi.CaptureContext();
        if (_loadedContext == context && Rows.Count > 0 && DateTime.UtcNow - _lastLoadedAt < CacheDuration) return;
        if (_loadedContext != context) { Rows.Clear(); TasteProfile = null; _hasLoaded = false; _loadedContext = context; }
        var generation = ++_loadGeneration;

        _loadInProgress = true;
        var isInitialLoad = Rows.Count == 0;
        IsLoading = isInitialLoad;
        IsTasteProfileLoading = TasteProfile == null;
        if (isInitialLoad)
            ErrorMessage = null;

        try
        {
            // The WebUI treats taste profile and discover rows as independent
            // queries. Publish the small header card as soon as it is ready;
            // it must not wait behind the much larger discover response.
            var profileTask = LoadAndPublishTasteProfileAsync(context, generation);
            var rows = await GetDiscoverRowsAsync();
            if (!_catalogApi.IsCurrentContext(context) || generation != _loadGeneration) { await profileTask; return; }
            ReconcileRows(rows);
            _hasLoaded = true;

            ErrorMessage = null;
            _lastLoadedAt = DateTime.UtcNow;
            IsLoading = false;

            await profileTask;
        }
        catch (Exception ex)
        {
            if (!_catalogApi.IsCurrentContext(context) || generation != _loadGeneration) return;
            System.Diagnostics.Debug.WriteLine($"Recommendations load failed: {ex}");
            if (Rows.Count == 0)
                ErrorMessage = "Failed to load recommendations.";
        }
        finally
        {
            _loadInProgress = false;
            IsLoading = false;
            IsTasteProfileLoading = false;
            if (_reloadRequested) { _reloadRequested = false; _ = LoadAsync(); }
        }
    }

    private async Task LoadAndPublishTasteProfileAsync(ApiRequestContext context, long generation)
    {
        try
        {
            var profile = await GetTasteProfileAsync();
            if (_catalogApi.IsCurrentContext(context) && generation == _loadGeneration) TasteProfile = profile;
        }
        finally
        {
            IsTasteProfileLoading = false;
        }
    }

    private void ReconcileRows(IReadOnlyList<RecommendationRowDisplay> incoming)
    {
        static string Key(RecommendationRowDisplay row) => row.Type + "\n" + row.SectionKind + "\n" + row.SectionKey + "\n" + row.Label;
        var desired = new List<RecommendationRowDisplay>();
        foreach (var next in incoming)
        {
            var mounted = Rows.FirstOrDefault(row => Key(row) == Key(next));
            if (mounted == null) desired.Add(next);
            else { MediaItemCollectionReconciler.Apply(mounted.Items, next.Items.ToArray()); desired.Add(mounted); }
        }
        foreach (var stale in Rows.Where(row => !desired.Contains(row)).ToArray()) Rows.Remove(stale);
        for (var index = 0; index < desired.Count; index++)
        {
            var existing = Rows.IndexOf(desired[index]);
            if (existing < 0) Rows.Insert(index, desired[index]);
            else if (existing != index) Rows.Move(existing, index);
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

                foreach (var item in row.Items.Where(item => item.UserState?.NotInterested != true))
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
                        UserState = detail.UserState != null ? new UserState { Played = detail.UserState.Played,
                            IsFavorite = detail.UserState.IsFavorite, InWatchlist = detail.UserState.InWatchlist,
                            NotInterested = detail.UserState.NotInterested } : detail.UserData != null ? new UserState { Played = detail.UserData.Played } : null
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
                if (item != null && item.UserState?.NotInterested != true)
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
