using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminActivityViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private List<AdminSession> _allSessions = [];

    public AdminActivityViewModel(AdminApi adminApi) { _adminApi = adminApi; }

    public ObservableCollection<AdminSession> FilteredSessions { get; } = [];
    public ObservableCollection<IPUserEntry> IPLookupResults { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _methodFilter;
    [ObservableProperty] private string? _typeFilter;
    [ObservableProperty] private string _ipLookupText = "";
    [ObservableProperty] private bool _ipLookupLoading;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _directCount;
    [ObservableProperty] private int _remuxCount;
    [ObservableProperty] private int _transcodeCount;

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnMethodFilterChanged(string? value) => ApplyFilters();
    partial void OnTypeFilterChanged(string? value) => ApplyFilters();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allSessions = await _adminApi.GetSessionsAsync();
            TotalCount = _allSessions.Count;
            DirectCount = _allSessions.Count(s => s.PlayMethod == "direct");
            RemuxCount = _allSessions.Count(s => s.PlayMethod == "remux");
            TranscodeCount = _allSessions.Count(s => s.PlayMethod == "transcode");
            ApplyFilters();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task LookupIPAsync()
    {
        if (string.IsNullOrWhiteSpace(IpLookupText)) return;
        IpLookupLoading = true;
        try
        {
            var results = await _adminApi.GetIPUsersAsync(IpLookupText.Trim(), 30);
            IPLookupResults.Clear();
            foreach (var r in results) IPLookupResults.Add(r);
        }
        catch { }
        finally { IpLookupLoading = false; }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        MethodFilter = null;
        TypeFilter = null;
    }

    private void ApplyFilters()
    {
        var result = _allSessions.AsEnumerable();
        if (!string.IsNullOrEmpty(SearchText))
        {
            result = result.Where(s =>
                (s.Username?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                (s.MediaTitle?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                (s.SeriesName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true));
        }
        if (MethodFilter != null) result = result.Where(s => s.PlayMethod == MethodFilter);
        if (TypeFilter != null) result = result.Where(s => s.MediaType == TypeFilter);

        FilteredSessions.Clear();
        foreach (var s in result) FilteredSessions.Add(s);
    }

    public static string GetTimeAgo(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return "";
        var diff = DateTime.UtcNow - dt.ToUniversalTime();
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    public static string GetElapsed(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return "00:00";
        var diff = DateTime.UtcNow - dt.ToUniversalTime();
        return diff.TotalHours >= 1
            ? $"{(int)diff.TotalHours}:{diff.Minutes:D2}:{diff.Seconds:D2}"
            : $"{diff.Minutes:D2}:{diff.Seconds:D2}";
    }

    public static string FormatDecision(string? decision) =>
        decision switch { "direct" => "Direct", "copy" => "Direct", "transcode" => "Transcode", _ => decision ?? "" };
}
