using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Official <c>MarkerTasksCard.tsx</c> + <c>TaskStatusBadge.tsx</c> (217ff25): the last execution of a marker task is shown as a status
/// badge (the server's error text is its tooltip) and a "Last run" / "Last result" line.
/// </summary>
public static class AdminPlaybackMarkerTaskStatus
{
    public enum BadgeTone { Secondary, Destructive, Outline }

    private static long? Number(AdminPlaybackTaskExecution execution, string key) =>
        execution.ResultData is { } data && data.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static string TaskKey(AdminPlaybackTask task, AdminPlaybackTaskExecution execution) => string.IsNullOrEmpty(execution.TaskKey) ? task.Key : execution.TaskKey;

    /// <summary>A completed <c>contribute_markers</c> run that failed some items.</summary>
    public static bool CompletedWithErrors(AdminPlaybackTask task) =>
        task.LastExecution is { } execution && execution.Status == "completed" && TaskKey(task, execution) == "contribute_markers" && Number(execution, "failed") is > 0;

    /// <summary>Null when the task has never run; otherwise the badge text. A status this build does not know is shown as the server sent it.</summary>
    public static string? StatusLabel(AdminPlaybackTask task)
    {
        if (task.LastExecution is not { } execution) return null;
        if (CompletedWithErrors(task)) return AdminTasksText.Message("web.admin.common.taskStatus.completedWithErrors");
        var status = execution.Status ?? "";
        return status switch
        {
            "completed" or "failed" or "cancelled" => AdminTasksText.Message("web.admin.common.taskStatus." + status),
            _ => status,
        };
    }

    public static BadgeTone Tone(AdminPlaybackTask task) =>
        task.LastExecution?.Status == "failed" || CompletedWithErrors(task) ? BadgeTone.Destructive : task.LastExecution?.Status == "cancelled" ? BadgeTone.Outline : BadgeTone.Secondary;

    /// <summary>The server's safe error text, shown as the badge tooltip.</summary>
    public static string? ErrorText(AdminPlaybackTask task) => string.IsNullOrWhiteSpace(task.LastExecution?.ErrorMessage) ? null : task.LastExecution!.ErrorMessage!.Trim();

    /// <summary>Official <c>formatTaskResult</c>.</summary>
    public static string ResultText(AdminPlaybackTask? task)
    {
        if (task?.LastExecution is not { } execution) return AdminPlaybackAdministrationText.T("web.admin.settings.markers.tasks.neverRun");
        if (task.Key == "contribute_markers")
        {
            var parts = new List<string>();
            void Add(string field, string text, bool positiveOnly = false)
            {
                if (Number(execution, field) is not { } count || positiveOnly && count <= 0) return;
                parts.Add(AdminPlaybackAdministrationText.T("web.admin.settings.markers.tasks." + text, new { count, count_text = count.ToString("N0") }));
            }
            Add("submitted", "submitted"); Add("skipped", "skipped"); Add("invalid", "refused", true); Add("failed", "failed");
            if (parts.Count > 0)
            {
                var list = string.Join(AdminPlaybackAdministrationText.T("web.admin.settings.markers.tasks.resultSeparator"), parts);
                return AdminPlaybackAdministrationText.T("web.admin.settings.markers.tasks." + (Number(execution, "retry_after_seconds") is > 0 ? "lastResultRateLimited" : "lastResult"), new { parts = list });
            }
        }
        var date = DateTimeOffset.TryParse(execution.CompletedAt, out var completed) ? AdminSearchSettingsRules.FormatDateTime(completed) : "";
        return AdminPlaybackAdministrationText.T("web.admin.settings.markers.tasks.lastRun", new { date });
    }
}
