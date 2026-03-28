using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminLogsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminLogsViewModel(AdminApi adminApi) { _adminApi = adminApi; }

    public ObservableCollection<OperationalLogEntry> AppLogs { get; } = [];
    public ObservableCollection<AuditLogEntry> AuditLogs { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // App log filters
    [ObservableProperty] private string _appLevelFilter = "";
    [ObservableProperty] private string _appComponent = "";

    // Audit log filters — userId only (API limitation)
    [ObservableProperty] private string _auditUserId = "";

    [RelayCommand]
    private async Task LoadAppLogsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var response = await _adminApi.GetAppLogsAsync(
                string.IsNullOrWhiteSpace(AppLevelFilter) ? null : AppLevelFilter.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(AppComponent) ? null : AppComponent.Trim(),
                cursor: null,
                limit: 200);
            AppLogs.Clear();
            foreach (var entry in response.Entries) AppLogs.Add(entry);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task LoadAuditLogsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            int? userId = null;
            if (!string.IsNullOrWhiteSpace(AuditUserId) && int.TryParse(AuditUserId.Trim(), out var parsed))
                userId = parsed;

            var response = await _adminApi.GetAuditLogsAsync(
                userId,
                cursor: null,
                limit: 200);
            AuditLogs.Clear();
            foreach (var entry in response.Entries) AuditLogs.Add(entry);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    public static string FormatTimestamp(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        return dt.ToLocalTime().ToString("HH:mm:ss.fff");
    }

    public static string FormatDate(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    public static string TruncatePath(string path, int max = 60)
    {
        if (path.Length <= max) return path;
        return path[..max] + "...";
    }

    public static string GetAttr(OperationalLogEntry entry, string key)
    {
        if (entry.Attrs == null) return "-";
        if (!entry.Attrs.TryGetValue(key, out var val)) return "-";
        return val?.ToString() ?? "-";
    }

    public static string GetDurationAttr(OperationalLogEntry entry)
    {
        if (entry.Attrs == null) return "-";
        if (!entry.Attrs.TryGetValue("duration_ms", out var val)) return "-";
        if (val is double d) return $"{d:F0} ms";
        var s = val?.ToString();
        return s != null ? $"{s} ms" : "-";
    }
}
