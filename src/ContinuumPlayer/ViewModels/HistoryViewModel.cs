using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;

    public HistoryViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    public ObservableCollection<HistoryDisplayItem> Items { get; } = [];

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
            var response = await _catalogApi.GetProgressAsync();
            Items.Clear();

            // Fetch item details for each progress entry
            var tasks = response.Progress.Select(async p =>
            {
                try
                {
                    var detail = await _catalogApi.GetItemDetailAsync(p.MediaItemId);
                    return new HistoryDisplayItem
                    {
                        ContentId = p.MediaItemId,
                        Title = detail.Title,
                        Year = detail.Year,
                        PosterUrl = detail.PosterUrl,
                        PosterThumbhash = detail.PosterThumbhash,
                        PositionSeconds = p.PositionSeconds,
                        DurationSeconds = p.DurationSeconds,
                        Completed = p.Completed,
                        ProgressPercent = p.DurationSeconds > 0
                            ? (int)(p.PositionSeconds / p.DurationSeconds * 100)
                            : 0,
                        UpdatedAt = p.UpdatedAt
                    };
                }
                catch
                {
                    return new HistoryDisplayItem
                    {
                        ContentId = p.MediaItemId,
                        Title = p.MediaItemId,
                        PositionSeconds = p.PositionSeconds,
                        DurationSeconds = p.DurationSeconds,
                        Completed = p.Completed,
                        ProgressPercent = p.DurationSeconds > 0
                            ? (int)(p.PositionSeconds / p.DurationSeconds * 100)
                            : 0,
                        UpdatedAt = p.UpdatedAt
                    };
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            foreach (var item in results)
            {
                Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public class HistoryDisplayItem
{
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool Completed { get; set; }
    public int ProgressPercent { get; set; }
    public string UpdatedAt { get; set; } = "";

    public string ProgressText =>
        DurationSeconds > 0
            ? $"{FormatTime(PositionSeconds)} / {FormatTime(DurationSeconds)} ({ProgressPercent}%)"
            : "Unknown duration";

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
