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
    private int _loadVersion;

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
        var version = Interlocked.Increment(ref _loadVersion);

        if (showLoading) IsLoading = true;
        if (showLoading) ErrorMessage = null;

        try
        {
            var infoTask    = _adminApi.GetTaskAsync(taskKey);
            var historyTask = _adminApi.GetTaskHistoryAsync(taskKey, limit: 20);
            var metricsTask = taskKey == "refresh_metadata"
                ? LoadMetricsSafeAsync(taskKey)
                : System.Threading.Tasks.Task.FromResult<MetadataRefreshMetrics?>(null);
            await System.Threading.Tasks.Task.WhenAll(infoTask, historyTask, metricsTask);
            if (version != Volatile.Read(ref _loadVersion)) return;

            TaskDetail = infoTask.Result;
            ReplaceHistory(historyTask.Result);
            Metrics = metricsTask.Result;
        }
        catch (Exception ex)
        {
            if (showLoading && version == Volatile.Read(ref _loadVersion)) ErrorMessage = ex.Message;
        }
        finally
        {
            if (showLoading && version == Volatile.Read(ref _loadVersion)) IsLoading = false;
        }
    }

    private async System.Threading.Tasks.Task<MetadataRefreshMetrics?> LoadMetricsSafeAsync(string taskKey)
    {
        try { return await _adminApi.GetTaskMetricsAsync(taskKey); }
        catch { return null; }
    }

    private void ReplaceHistory(IReadOnlyList<ExecutionResult> incoming)
    {
        for (var targetIndex = 0; targetIndex < incoming.Count; targetIndex++)
        {
            var next = incoming[targetIndex];
            var existingIndex = -1;
            for (var i = targetIndex; i < History.Count; i++)
            {
                if (History[i].Id != next.Id) continue;
                existingIndex = i;
                break;
            }

            if (existingIndex < 0)
            {
                History.Insert(targetIndex, next);
                continue;
            }

            if (existingIndex != targetIndex)
                History.Move(existingIndex, targetIndex);
            if (!ExecutionMatches(History[targetIndex], next))
                History[targetIndex] = next;
        }

        while (History.Count > incoming.Count)
            History.RemoveAt(History.Count - 1);
    }

    private static bool ExecutionMatches(ExecutionResult left, ExecutionResult right) =>
        left.Id == right.Id &&
        left.TaskKey == right.TaskKey &&
        left.StartedAt == right.StartedAt &&
        left.CompletedAt == right.CompletedAt &&
        left.Status == right.Status &&
        left.ErrorMessage == right.ErrorMessage &&
        left.DurationMs == right.DurationMs &&
        System.Text.Json.JsonSerializer.Serialize(left.ResultData) ==
        System.Text.Json.JsonSerializer.Serialize(right.ResultData);

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
