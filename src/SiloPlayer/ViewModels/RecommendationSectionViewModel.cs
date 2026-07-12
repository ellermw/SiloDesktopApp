using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.ViewModels;

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
        Title = string.IsNullOrWhiteSpace(args.Title) ? GetFallbackTitle(args.Kind, args.Key) : args.Title;
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
            System.Diagnostics.Debug.WriteLine($"Recommendation section load failed: {ex}");
            ErrorMessage = "Failed to load this section.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal static string GetFallbackTitle(string kind, string? key) => kind switch
    {
        "for-you-main" => "For You",
        "cluster" => string.IsNullOrWhiteSpace(key) ? "Personalized cluster" : $"Personalized cluster {key}",
        "similar-users" => "Users Like You Also Enjoyed",
        "popular" => "Popular on This Server",
        "recently-added" => "Recently Added",
        "top-rated" => "Top Rated",
        "genre" => string.IsNullOrWhiteSpace(key) ? "Genre picks" : $"Popular in {key}",
        _ => "Recommendations"
    };
}
