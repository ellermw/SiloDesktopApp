using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminMarkerHistoryViewModel(AdminApi adminApi) : ObservableObject
{
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<MarkerHistoryRowViewModel> Rows { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int _limit = 50;
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private bool _showEmptyState;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var ownerCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, ownerCts);
        previous?.Cancel();
        previous?.Dispose();

        IsLoading = true;
        ErrorMessage = null;
        HasRows = false;
        ShowEmptyState = false;
        try
        {
            var response = await adminApi.GetMarkerHistoryAsync(Limit, ownerCts.Token);
            ownerCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, ownerCts))
                return;

            Rows.Clear();
            foreach (var entry in response.History)
                Rows.Add(MarkerHistoryRowViewModel.From(entry));
            HasRows = Rows.Count > 0;
            ShowEmptyState = Rows.Count == 0;
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, ownerCts))
            {
                ErrorMessage = ex.Message;
                ShowEmptyState = false;
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, ownerCts), ownerCts))
                IsLoading = false;
            ownerCts.Dispose();
        }
    }

    public void Cancel()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoading = false;
    }
}

public sealed record MarkerHistoryRowViewModel(
    long Id,
    string When,
    string ItemLabel,
    string ItemDetail,
    string? ItemId,
    string Segment,
    string Action,
    string Change,
    string Actor,
    string ActorDetail,
    string Request,
    string RequestDetail)
{
    public static MarkerHistoryRowViewModel From(MarkerHistoryEntry entry)
    {
        var title = !string.IsNullOrWhiteSpace(entry.MediaTitle)
            ? entry.MediaTitle
            : !string.IsNullOrWhiteSpace(entry.FilePath)
                ? Path.GetFileName(entry.FilePath) ?? entry.FilePath
                : $"File {entry.MediaFileId}";
        var itemLabel = !string.IsNullOrWhiteSpace(entry.ItemType)
            ? $"{title} ({entry.ItemType})"
            : title;
        var actor = string.IsNullOrWhiteSpace(entry.Username) ? "Unknown user" : entry.Username;
        var actorDetail = string.IsNullOrWhiteSpace(entry.ImpersonatorUsername)
            ? ""
            : $"via {entry.ImpersonatorUsername}";
        var request = string.IsNullOrWhiteSpace(entry.RequestId) ? "No request id" : entry.RequestId;
        var requestDetail = entry.ClientIp ?? entry.UserAgent ?? "";

        return new MarkerHistoryRowViewModel(
            entry.Id,
            entry.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),
            itemLabel,
            entry.FilePath ?? $"Media file {entry.MediaFileId}",
            entry.ItemId,
            entry.Segment switch
            {
                "intro" => "Intro",
                "recap" => "Recap",
                "credits" => "Credits / Outro",
                "preview" => "Preview",
                _ => entry.Segment,
            },
            string.Equals(entry.Action, "clear", StringComparison.OrdinalIgnoreCase)
                ? "Cleared"
                : "Set",
            $"{FormatRange(entry.Before)}  ->  {FormatRange(entry.After)}",
            actor,
            actorDetail,
            request,
            requestDetail);
    }

    private static string FormatRange(MarkerHistorySegment? marker)
    {
        if (marker?.Start is not double start || marker.End is not double end)
            return "none";
        return $"{FormatClock(start)}-{FormatClock(end)}";
    }

    private static string FormatClock(double seconds)
    {
        // Match JavaScript Math.round used by the WebUI. TimeSpan's component
        // accessors truncate fractional seconds and made marker endpoints render
        // one second early for values such as 1299.8.
        var roundedSeconds = Math.Floor(Math.Max(0, seconds) + 0.5);
        var value = TimeSpan.FromSeconds(roundedSeconds);
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }
}
