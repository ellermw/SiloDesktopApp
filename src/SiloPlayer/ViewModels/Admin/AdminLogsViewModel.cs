using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminLogsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminLogsViewModel(AdminApi adminApi) { _adminApi = adminApi; }

    public ObservableCollection<OperationalLogEntry> AppLogs { get; } = [];
    public ObservableCollection<AuditLogEntry> AuditLogs { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private string? _errorMessage;

    // Cursor-pagination state (webui exposes this via the server response's
    // NextCursor; null means the end of the feed has been reached).
    [ObservableProperty] private string? _appLogsNextCursor;
    [ObservableProperty] private string? _auditLogsNextCursor;

    public bool AppLogsHasMore => !string.IsNullOrEmpty(AppLogsNextCursor);
    public bool AuditLogsHasMore => !string.IsNullOrEmpty(AuditLogsNextCursor);

    partial void OnAppLogsNextCursorChanged(string? value) => OnPropertyChanged(nameof(AppLogsHasMore));
    partial void OnAuditLogsNextCursorChanged(string? value) => OnPropertyChanged(nameof(AuditLogsHasMore));

    // Connection state display
    [ObservableProperty] private string _connectionState = "Disconnected";

    // Playback session filter (shared across both tabs)
    [ObservableProperty] private string _playbackSessionId = "";

    // App log filters
    [ObservableProperty] private string _appRequestId = "";
    [ObservableProperty] private string _appMessageQuery = "";
    [ObservableProperty] private string _appComponent = "";

    // Audit log filters
    [ObservableProperty] private string _auditRequestId = "";
    [ObservableProperty] private string _auditMethod = "";
    [ObservableProperty] private string _auditClientIp = "";

    // Playback session summary computed fields
    [ObservableProperty] private int _summaryAppCount;
    [ObservableProperty] private int _summaryFfmpegCount;
    [ObservableProperty] private int _summaryAuditCount;
    [ObservableProperty] private string _summaryFirstSeen = "-";
    [ObservableProperty] private string _summaryLastSeen = "-";
    [ObservableProperty] private string _summaryNodes = "-";

    [RelayCommand]
    private async Task LoadAppLogsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        ConnectionState = "Connecting...";
        try
        {
            var response = await _adminApi.GetAppLogsAsync(
                level: null,
                component: string.IsNullOrWhiteSpace(AppComponent) ? null : AppComponent.Trim(),
                requestId: string.IsNullOrWhiteSpace(AppRequestId) ? null : AppRequestId.Trim(),
                q: string.IsNullOrWhiteSpace(AppMessageQuery) ? null : AppMessageQuery.Trim(),
                playbackSessionId: string.IsNullOrWhiteSpace(PlaybackSessionId) ? null : PlaybackSessionId.Trim(),
                cursor: null,
                limit: 200);
            AppLogs.Clear();
            foreach (var entry in response.Entries) AppLogs.Add(entry);
            AppLogsNextCursor = response.NextCursor;
            ConnectionState = "Live";
            UpdatePlaybackSummary();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ConnectionState = "Disconnected";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task LoadMoreAppLogsAsync()
    {
        if (IsLoadingMore || string.IsNullOrEmpty(AppLogsNextCursor)) return;
        IsLoadingMore = true;
        try
        {
            var response = await _adminApi.GetAppLogsAsync(
                level: null,
                component: string.IsNullOrWhiteSpace(AppComponent) ? null : AppComponent.Trim(),
                requestId: string.IsNullOrWhiteSpace(AppRequestId) ? null : AppRequestId.Trim(),
                q: string.IsNullOrWhiteSpace(AppMessageQuery) ? null : AppMessageQuery.Trim(),
                playbackSessionId: string.IsNullOrWhiteSpace(PlaybackSessionId) ? null : PlaybackSessionId.Trim(),
                cursor: AppLogsNextCursor,
                limit: 200);
            foreach (var entry in response.Entries) AppLogs.Add(entry);
            AppLogsNextCursor = response.NextCursor;
            UpdatePlaybackSummary();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoadingMore = false; }
    }

    [RelayCommand]
    private async Task LoadAuditLogsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        ConnectionState = "Connecting...";
        try
        {
            var response = await _adminApi.GetAuditLogsAsync(
                userId: null,
                requestId: string.IsNullOrWhiteSpace(AuditRequestId) ? null : AuditRequestId.Trim(),
                method: string.IsNullOrWhiteSpace(AuditMethod) ? null : AuditMethod.Trim(),
                clientIp: string.IsNullOrWhiteSpace(AuditClientIp) ? null : AuditClientIp.Trim(),
                playbackSessionId: string.IsNullOrWhiteSpace(PlaybackSessionId) ? null : PlaybackSessionId.Trim(),
                cursor: null,
                limit: 200);
            AuditLogs.Clear();
            foreach (var entry in response.Entries) AuditLogs.Add(entry);
            AuditLogsNextCursor = response.NextCursor;
            ConnectionState = "Live";
            UpdatePlaybackSummary();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ConnectionState = "Disconnected";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task LoadMoreAuditLogsAsync()
    {
        if (IsLoadingMore || string.IsNullOrEmpty(AuditLogsNextCursor)) return;
        IsLoadingMore = true;
        try
        {
            var response = await _adminApi.GetAuditLogsAsync(
                userId: null,
                requestId: string.IsNullOrWhiteSpace(AuditRequestId) ? null : AuditRequestId.Trim(),
                method: string.IsNullOrWhiteSpace(AuditMethod) ? null : AuditMethod.Trim(),
                clientIp: string.IsNullOrWhiteSpace(AuditClientIp) ? null : AuditClientIp.Trim(),
                playbackSessionId: string.IsNullOrWhiteSpace(PlaybackSessionId) ? null : PlaybackSessionId.Trim(),
                cursor: AuditLogsNextCursor,
                limit: 200);
            foreach (var entry in response.Entries) AuditLogs.Add(entry);
            AuditLogsNextCursor = response.NextCursor;
            UpdatePlaybackSummary();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoadingMore = false; }
    }

    public void UpdatePlaybackSummary()
    {
        if (string.IsNullOrWhiteSpace(PlaybackSessionId))
        {
            SummaryAppCount = 0;
            SummaryFfmpegCount = 0;
            SummaryAuditCount = 0;
            SummaryFirstSeen = "-";
            SummaryLastSeen = "-";
            SummaryNodes = "-";
            return;
        }

        var pid = PlaybackSessionId.Trim();
        var matchingApp = AppLogs.Where(r => r.PlaybackSessionId == pid).ToList();
        var matchingAudit = AuditLogs.Where(r => r.PlaybackSessionId == pid).ToList();

        SummaryAppCount = matchingApp.Count;
        SummaryFfmpegCount = matchingApp.Count(r => r.Component == "ffmpeg");
        SummaryAuditCount = matchingAudit.Count;

        var timestamps = matchingApp.Select(r => r.Timestamp)
            .Concat(matchingAudit.Select(r => r.Timestamp))
            .OrderBy(t => t)
            .ToList();

        SummaryFirstSeen = timestamps.Count > 0 ? FormatDateTime(timestamps[0]) : "-";
        SummaryLastSeen = timestamps.Count > 1 ? FormatDateTime(timestamps[^1]) : "-";

        var nodes = matchingApp.Select(r => r.NodeId).Where(n => !string.IsNullOrEmpty(n))
            .Concat(matchingAudit.Select(r => r.NodeId).Where(n => !string.IsNullOrEmpty(n)))
            .Distinct()
            .ToList();
        SummaryNodes = nodes.Count > 0 ? string.Join(", ", nodes) : "-";
    }

    // ===== Formatting helpers =====

    public static string FormatTimestamp(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        return dt.ToLocalTime().ToString("HH:mm:ss.fff");
    }

    public static string FormatDateTime(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        return dt.ToLocalTime().ToString("g"); // short date + short time (locale)
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

    public static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "-";
        if (id.Length <= 12) return id;
        return $"{id[..8]}...{id[^4..]}";
    }

    public static string FormatClientIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return "-";
        var slashIdx = ip.IndexOf('/');
        return slashIdx >= 0 ? ip[..slashIdx] : ip;
    }
}
