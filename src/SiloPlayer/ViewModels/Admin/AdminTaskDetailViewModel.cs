using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

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
    [ObservableProperty] private MetadataRefreshMetrics? _metrics;

    public ObservableCollection<ExecutionResult> History { get; } = [];

    // ===== Load =====

    [RelayCommand]
    public System.Threading.Tasks.Task LoadAsync(string taskKey)
        => LoadInternalAsync(taskKey, showLoading: true);

    public System.Threading.Tasks.Task RefreshSilentAsync(string taskKey)
        => LoadInternalAsync(taskKey, showLoading: false);

    private async System.Threading.Tasks.Task LoadInternalAsync(string taskKey, bool showLoading)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return;

        if (showLoading) IsLoading = true;
        if (showLoading) ErrorMessage = null;

        try
        {
            var infoTask    = _adminApi.GetTaskAsync(taskKey);
            var historyTask = _adminApi.GetTaskHistoryAsync(taskKey, limit: 20);
            await System.Threading.Tasks.Task.WhenAll(infoTask, historyTask);

            TaskDetail = infoTask.Result;

            History.Clear();
            foreach (var h in historyTask.Result) History.Add(h);

            // Load metrics for refresh_metadata task
            if (taskKey == "refresh_metadata")
            {
                try { Metrics = await _adminApi.GetTaskMetricsAsync(taskKey); }
                catch { Metrics = null; }
            }
            else
            {
                Metrics = null;
            }
        }
        catch (Exception ex) { if (showLoading) ErrorMessage = ex.Message; }
        finally { if (showLoading) IsLoading = false; }
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
            await RefreshSilentAsync(key);
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
            await RefreshSilentAsync(key);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Update Triggers =====

    public async System.Threading.Tasks.Task UpdateTriggersAsync(string taskKey, List<TriggerConfig> triggers)
    {
        try
        {
            await _adminApi.UpdateTaskTriggersAsync(taskKey, triggers);
            await RefreshSilentAsync(taskKey);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Helpers =====

    public bool IsRunning => TaskDetail?.State == "running" || TaskDetail?.State == "cancelling";
}
