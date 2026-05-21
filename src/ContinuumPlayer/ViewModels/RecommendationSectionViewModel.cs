using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public sealed record RecommendationSectionNavigationArgs(string Kind, string? Key, string Title);

public partial class RecommendationSectionViewModel : ObservableObject
{
    private readonly RecommendationsApi _recommendationsApi;

    public RecommendationSectionViewModel(RecommendationsApi recommendationsApi)
    {
        _recommendationsApi = recommendationsApi;
    }

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty] private string _title = "Recommendations";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public async Task LoadAsync(RecommendationSectionNavigationArgs args, CancellationToken ct = default)
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        Title = string.IsNullOrWhiteSpace(args.Title) ? "Recommendations" : args.Title;
        Items.Clear();

        try
        {
            var response = await _recommendationsApi.GetSectionAsync(args.Kind, args.Key, ct);
            if (!string.IsNullOrWhiteSpace(response.Label))
                Title = response.Label;

            foreach (var item in response.Items)
                Items.Add(item);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load recommendation section: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
