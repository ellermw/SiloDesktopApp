using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Downloads;

namespace ContinuumPlayer.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly DownloadsApi _downloadsApi;

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
        if (IsLoading) return;

        IsLoading = true;
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
        }
    }

    [RelayCommand]
    private async Task DeleteDownloadAsync(int id)
    {
        try
        {
            await _downloadsApi.DeleteDownloadAsync(id);
            var item = Downloads.FirstOrDefault(d => d.Id == id);
            if (item != null)
                Downloads.Remove(item);

            IsEmpty = Downloads.Count == 0;
        }
        catch
        {
            // Delete failure is non-fatal
        }
    }
}
