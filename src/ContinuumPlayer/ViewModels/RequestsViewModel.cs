using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Requests;

namespace ContinuumPlayer.ViewModels;

public partial class RequestsViewModel : ObservableObject
{
    private readonly RequestsApi _requestsApi;

    public RequestsViewModel(RequestsApi requestsApi)
    {
        _requestsApi = requestsApi;
    }

    public ObservableCollection<RequestDiscoverySection> DiscoverySections { get; } = [];
    public ObservableCollection<RequestMediaResult> SearchResults { get; } = [];
    public ObservableCollection<MediaRequest> MyRequests { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _requestsEnabled = true;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _selectedMediaType = "all";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusMessage = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Loading requests...";

        try
        {
            var status = await _requestsApi.GetStatusAsync();
            RequestsEnabled = status.RequestsEnabled;
            if (!RequestsEnabled)
            {
                DiscoverySections.Clear();
                MyRequests.Clear();
                StatusMessage = "Requests are disabled on this server.";
                return;
            }

            var mineTask = _requestsApi.GetMineAsync(limit: 50);
            var discoverTask = _requestsApi.GetDiscoveryAsync();
            await Task.WhenAll(mineTask, discoverTask);

            MyRequests.Clear();
            foreach (var request in mineTask.Result.Requests)
                MyRequests.Add(request);

            DiscoverySections.Clear();
            foreach (var section in discoverTask.Result.Sections)
                DiscoverySections.Add(section);

            StatusMessage = $"{MyRequests.Count:N0} request(s) loaded.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load requests: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (query.Length < 2)
        {
            SearchResults.Clear();
            StatusMessage = "Enter at least 2 characters to search.";
            return;
        }

        IsSearching = true;
        ErrorMessage = null;
        StatusMessage = "Searching...";

        try
        {
            var response = await _requestsApi.SearchAsync(SelectedMediaType, query);
            SearchResults.Clear();
            foreach (var item in response.Results)
                SearchResults.Add(item);

            StatusMessage = $"{SearchResults.Count:N0} result(s).";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task SubmitRequestAsync(RequestMediaResult result)
    {
        try
        {
            StatusMessage = $"Submitting {result.Title}...";
            await _requestsApi.CreateAsync(new CreateMediaRequestInput
            {
                MediaType = result.MediaType,
                TmdbId = result.TmdbId,
                Title = result.Title,
                Year = result.Year,
                Overview = result.Overview,
                PosterPath = result.PosterPath,
                BackdropPath = result.BackdropPath,
            });
            await LoadAsync();
            if (!string.IsNullOrWhiteSpace(SearchQuery))
                await SearchAsync();
            StatusMessage = "Request submitted.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to submit request: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CancelRequestAsync(MediaRequest request)
    {
        try
        {
            await _requestsApi.CancelAsync(request.Id);
            await LoadAsync();
            StatusMessage = "Request cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to cancel request: {ex.Message}";
        }
    }
}
