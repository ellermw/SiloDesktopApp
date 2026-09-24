using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Downloads;

namespace SiloPlayer.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly DownloadsApi _downloadsApi;
    private bool _loadInProgress;

    public DownloadsViewModel(DownloadsApi downloadsApi)
    {
        _downloadsApi = downloadsApi;
    }

    public ObservableCollection<Download> Downloads { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadDownloadsAsync()
    {
        if (_loadInProgress) return;

        _loadInProgress = true;
        // Keep populated rows visible during a background refresh. Replacing
        // the entire page with a spinner on every visit creates a visible
        // reload even when the cached data is already usable.
        IsLoading = Downloads.Count == 0;
        ErrorMessage = null;

        try
        {
            var response = await _downloadsApi.GetDownloadsAsync();
            Downloads.Clear();
            foreach (var dl in response.Downloads)
                Downloads.Add(dl);

            IsEmpty = Downloads.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load downloads: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _loadInProgress = false;
        }
    }

    [RelayCommand]
    private async Task DeleteDownloadAsync(string id, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            await _downloadsApi.DeleteDownloadAsync(id, cancellationToken);
            var item = Downloads.FirstOrDefault(d => d.Id == id);
            if (item != null)
                Downloads.Remove(item);

            IsEmpty = Downloads.Count == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Explicit cancellation leaves the registry row intact. A network
            // timeout without user cancellation still reports a failure.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is ApiException { StatusCode: 403 }
                ? "You do not have permission to delete this download. It is still listed. Check your profile permissions and try again."
                : "Could not delete the download from the server. It is still listed; check your connection and try again.";
        }
    }
}
