using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminTaskDetailViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminTaskDetailViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private TaskInfo? _taskDetail;

    public ObservableCollection<ExecutionResult> History { get; } = [];

    // ===== Load =====

    [RelayCommand]
    public async System.Threading.Tasks.Task LoadAsync(string taskKey)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var infoTask    = _adminApi.GetTaskAsync(taskKey);
            var historyTask = _adminApi.GetTaskHistoryAsync(taskKey, limit: 20);
            await System.Threading.Tasks.Task.WhenAll(infoTask, historyTask);

            TaskDetail = infoTask.Result;

            History.Clear();
            foreach (var h in historyTask.Result) History.Add(h);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Run =====

    [RelayCommand]
    public async System.Threading.Tasks.Task RunAsync()
    {
        if (TaskDetail == null) return;
        string key = TaskDetail.Key;
        try
        {
            await _adminApi.RunTaskAsync(key);
            await System.Threading.Tasks.Task.Delay(300);
            await LoadAsync(key);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Cancel =====

    [RelayCommand]
    public async System.Threading.Tasks.Task CancelAsync()
    {
        if (TaskDetail == null) return;
        string key = TaskDetail.Key;
        try
        {
            await _adminApi.CancelTaskAsync(key);
            await System.Threading.Tasks.Task.Delay(300);
            await LoadAsync(key);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Helpers =====

    public bool IsRunning => TaskDetail?.State == "running" || TaskDetail?.State == "cancelling";
}
