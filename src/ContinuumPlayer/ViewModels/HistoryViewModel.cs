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

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _selectedTab = "in_progress";

    private int _offset;
    private const int PageSize = 30;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        _offset = 0;
        Items.Clear();
        HasMore = false;

        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading) return;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task SwitchTabAsync(string tab)
    {
        if (SelectedTab == tab) return;
        SelectedTab = tab;
        _offset = 0;
        Items.Clear();
        HasMore = false;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            if (SelectedTab == "in_progress")
            {
                // Use progress endpoint for in-progress items
                var response = await _catalogApi.GetProgressAsync();
                Items.Clear();

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
                    Items.Add(item);

                TotalCount = Items.Count;
                HasMore = false;
            }
            else
            {
                // Use history endpoint for full watch history
                var response = await _catalogApi.GetHistoryAsync(limit: PageSize, offset: _offset);
                TotalCount = response.Total;
                HasMore = response.HasMore;

                foreach (var entry in response.Items)
                {
                    Items.Add(new HistoryDisplayItem
                    {
                        ContentId = entry.ContentId,
                        Title = entry.Title,
                        Year = entry.Year,
                        PosterUrl = entry.PosterUrl,
                        PosterThumbhash = entry.PosterThumbhash,
                        PositionSeconds = entry.PositionSeconds,
                        DurationSeconds = entry.DurationSeconds,
                        Completed = entry.Completed,
                        ProgressPercent = entry.DurationSeconds > 0
                            ? (int)(entry.PositionSeconds / entry.DurationSeconds * 100)
                            : 0,
                        UpdatedAt = entry.WatchedAt,
                        Type = entry.Type
                    });
                }

                _offset += response.Items.Count;
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
    public string Type { get; set; } = "";

    public string ProgressText =>
        DurationSeconds > 0
            ? $"{FormatTime(PositionSeconds)} / {FormatTime(DurationSeconds)} ({ProgressPercent}%)"
            : "Unknown duration";

    public string TimestampDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(UpdatedAt)) return "";
            if (!DateTime.TryParse(UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return UpdatedAt;

            var local = dt.ToLocalTime();
            var now = DateTime.Now;
            var diff = now - local;

            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return local.ToString("MMM d, yyyy");
        }
    }

    public string StatusText =>
        Completed ? "Completed" : ProgressPercent > 0 ? $"{ProgressPercent}% watched" : "Started";

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
