using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class HistoryViewModel : ObservableObject,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly CatalogApi _catalogApi;

    public HistoryViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
        // F4: refresh history whenever watched/progress state changes.
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this);
        WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this);
    }

    public void Receive(MediaSurfaceChanged message)
    {
        // Watched toggles + new playback progress should invalidate history
        // caches. We don't have a cache timer here, but scheduling a reload
        // next tick keeps history in sync with the rest of the app.
        if (message.Kind is MediaSurfaceChangeKind.WatchedMarked
            or MediaSurfaceChangeKind.WatchedCleared
            or MediaSurfaceChangeKind.PlaybackProgress)
        {
            _pendingRefresh = true;
        }
    }

    public void Receive(PlaybackProgressUpdated message)
    {
        _pendingRefresh = true;
    }

    /// <summary>Set by the messenger receivers; HistoryPage checks on navigation.</summary>
    private bool _pendingRefresh;
    public bool ConsumePendingRefresh()
    {
        if (!_pendingRefresh) return false;
        _pendingRefresh = false;
        return true;
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
    private bool _loadInProgress;
    private const int PageSize = 30;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loadInProgress) return;

        _offset = 0;
        HasMore = false;

        await LoadPageAsync(replace: true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || _loadInProgress) return;
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

    public async Task RemoveHistoryAsync(IReadOnlyCollection<HistoryDisplayItem> items)
    {
        if (items.Count == 0) return;

        var targets = items.Select(item => new HistoryRemovalTarget
        {
            ContentId = item.ContentId,
            Scope = item.Type is "series" or "season" ? "show" : "item"
        });
        await _catalogApi.RemoveHistoryAsync(targets);
        foreach (var item in items)
        {
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                MediaSurfaceChangeKind.WatchedCleared, item.ContentId));
        }
        await LoadAsync();
    }

    private async Task LoadPageAsync(bool replace = false)
    {
        _loadInProgress = true;
        IsLoading = Items.Count == 0;
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
                TotalCount = response.Total > 0
                    ? response.Total
                    : (replace ? response.Items.Count : TotalCount + response.Items.Count);
                HasMore = response.HasMore;

                if (replace)
                    Items.Clear();

                foreach (var entry in response.Items)
                {
                    var resolvedDuration = entry.DurationSeconds ?? Math.Max(0, entry.Runtime * 60);
                    Items.Add(new HistoryDisplayItem
                    {
                        ContentId = entry.ContentId,
                        Title = entry.Title,
                        Year = entry.Year,
                        PosterUrl = entry.PosterUrl,
                        PosterThumbhash = entry.PosterThumbhash,
                        PositionSeconds = entry.PositionSeconds ?? 0,
                        DurationSeconds = resolvedDuration,
                        Completed = entry.UserState?.Played ?? false,
                        ProgressPercent = resolvedDuration > 0
                            ? (int)((entry.PositionSeconds ?? 0) / resolvedDuration * 100)
                            : 0,
                        UpdatedAt = entry.SortMetrics?.ViewedAt
                            ?? entry.ProgressUpdatedAt
                            ?? entry.AddedAt
                            ?? "",
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
            _loadInProgress = false;
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
            return SiloPlayer.Helpers.DateTimeDisplay.FormatDate(new DateTimeOffset(local), medium: true);
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
