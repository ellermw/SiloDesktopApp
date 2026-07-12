using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminRequestsViewModel : ObservableObject
{
    private readonly RequestsApi _requestsApi;

    public AdminRequestsViewModel(RequestsApi requestsApi)
    {
        _requestsApi = requestsApi;
    }

    public ObservableCollection<MediaRequest> Requests { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusFilter = "all";
    [ObservableProperty] private string _outcomeFilter = "all";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusMessage = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Loading media requests...";

        try
        {
            var response = await _requestsApi.GetAdminAsync(StatusFilter, OutcomeFilter, limit: 100);
            Requests.Clear();
            foreach (var request in response.Requests)
                Requests.Add(request);

            StatusMessage = $"{Requests.Count:N0} request(s) loaded.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load media requests: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ApproveAsync(MediaRequest request)
    {
        try
        {
            await _requestsApi.ApproveAsync(request.Id);
            await LoadAsync();
            StatusMessage = "Request approved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to approve request: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeclineAsync(MediaRequest request)
    {
        try
        {
            await _requestsApi.DeclineAsync(request.Id);
            await LoadAsync();
            StatusMessage = "Request declined.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to decline request: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RetryAsync(MediaRequest request)
    {
        try
        {
            await _requestsApi.RetryAsync(request.Id);
            await LoadAsync();
            StatusMessage = "Request queued for retry.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to retry request: {ex.Message}";
        }
    }
}
