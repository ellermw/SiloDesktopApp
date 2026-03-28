using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminTasksViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminTasksViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<TaskInfo> Tasks { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var tasks = await _adminApi.GetTasksAsync();
            Tasks.Clear();
            foreach (var t in tasks) Tasks.Add(t);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Run Task =====

    [RelayCommand]
    public async Task RunTaskAsync(string key)
    {
        try
        {
            await _adminApi.RunTaskAsync(key);
            // Refresh after a short delay so the server has time to transition state
            await Task.Delay(300);
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Cancel Task =====

    [RelayCommand]
    public async Task CancelTaskAsync(string key)
    {
        try
        {
            await _adminApi.CancelTaskAsync(key);
            await Task.Delay(300);
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Helpers =====

    /// <summary>Returns true if any task is currently running or cancelling.</summary>
    public bool AnyTaskRunning =>
        Tasks.Any(t => t.State == "running" || t.State == "cancelling");

    /// <summary>Returns tasks grouped by category in canonical order.</summary>
    public IEnumerable<(string Category, string Label, IEnumerable<TaskInfo> Tasks)> GetGroupedTasks()
    {
        var order = new[] { "library", "metadata", "system" };
        var labels = new Dictionary<string, string>
        {
            ["library"]  = "Library",
            ["metadata"] = "Metadata",
            ["system"]   = "System"
        };

        foreach (var cat in order)
        {
            var items = Tasks.Where(t => t.Category == cat).ToList();
            if (items.Count > 0)
                yield return (cat, labels.GetValueOrDefault(cat, cat), items);
        }
    }
}
