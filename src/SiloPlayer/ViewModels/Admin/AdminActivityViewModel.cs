using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminActivityViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private List<AdminSession> _allSessions = [];
    public bool HasLoaded { get; private set; }

    public AdminActivityViewModel(AdminApi adminApi) { _adminApi = adminApi; }

    public ObservableCollection<AdminSession> FilteredSessions { get; } = [];
    public ObservableCollection<IPUserEntry> IPLookupResults { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _methodFilter;
    [ObservableProperty] private string? _nodeFilter;
    [ObservableProperty] private string? _typeFilter;
    [ObservableProperty] private string _ipLookupText = "";
    [ObservableProperty] private bool _ipLookupLoading;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _directCount;
    [ObservableProperty] private int _remuxCount;
    [ObservableProperty] private int _transcodeCount;

    // Sort state: field name + direction
    [ObservableProperty] private string _sortField = "started";
    [ObservableProperty] private bool _sortAscending;

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnMethodFilterChanged(string? value) => ApplyFilters();
    partial void OnNodeFilterChanged(string? value) => ApplyFilters();
    partial void OnTypeFilterChanged(string? value) => ApplyFilters();
    partial void OnSortFieldChanged(string value) => ApplyFilters();
    partial void OnSortAscendingChanged(bool value) => ApplyFilters();

    /// <summary>
    /// Toggle sort: same field flips direction; new field defaults desc for "started", asc for others.
    /// </summary>
    public void ToggleSort(string field)
    {
        if (SortField == field)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortField = field;
            SortAscending = field != "started"; // "started" defaults desc, others default asc
        }
    }

    /// <summary>
    /// Returns method -> count dictionary from all sessions.
    /// </summary>
    public Dictionary<string, int> GetMethodCounts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var s in _allSessions)
        {
            var key = s.PlayMethod ?? "unknown";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }

    /// <summary>
    /// Returns node -> count dictionary from all sessions.
    /// </summary>
    public Dictionary<string, int> GetNodeCounts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var s in _allSessions)
        {
            var key = s.ReportingNode ?? "unknown";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }

    public bool HasActiveFilters =>
        !string.IsNullOrEmpty(SearchText)
        || MethodFilter != null
        || NodeFilter != null
        || TypeFilter != null;

    public int ActiveFilterCount =>
        (MethodFilter != null ? 1 : 0)
        + (NodeFilter != null ? 1 : 0)
        + (TypeFilter != null ? 1 : 0);

    [RelayCommand]
    private Task LoadAsync() => LoadInternalAsync(silent: false);

    /// <summary>
    /// Realtime event-channel refresh path — skips toggling IsLoading so the
    /// ProgressRing doesn't flash on every websocket update.
    /// </summary>
    public Task RefreshSilentAsync() => LoadInternalAsync(silent: true);

    private async Task LoadInternalAsync(bool silent)
    {
        if (!silent) IsLoading = true;
        if (!silent) ErrorMessage = null;
        try
        {
            _allSessions = await _adminApi.GetSessionsAsync();
            TotalCount = _allSessions.Count;
            DirectCount = _allSessions.Count(s => s.PlayMethod == "direct");
            RemuxCount = _allSessions.Count(s => s.PlayMethod == "remux");
            TranscodeCount = _allSessions.Count(s => s.PlayMethod == "transcode");
            ApplyFilters();
            HasLoaded = true;
        }
        catch (Exception ex) { if (!silent) ErrorMessage = ex.Message; }
        finally { if (!silent) IsLoading = false; }
    }

    [RelayCommand]
    private async Task LookupIPAsync()
    {
        if (string.IsNullOrWhiteSpace(IpLookupText)) return;
        IpLookupLoading = true;
        try
        {
            var results = await _adminApi.GetIPUsersAsync(IpLookupText.Trim(), 30);
            IPLookupResults.Clear();
            foreach (var r in results) IPLookupResults.Add(r);
        }
        catch { }
        finally { IpLookupLoading = false; }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        // Only reset filter toggles — do NOT reset SearchText (webui behaviour)
        MethodFilter = null;
        NodeFilter = null;
        TypeFilter = null;
    }

    private void ApplyFilters()
    {
        var result = _allSessions.AsEnumerable();
        if (!string.IsNullOrEmpty(SearchText))
        {
            result = result.Where(s =>
                (s.Username?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                (s.MediaTitle?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                (s.SeriesName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                (s.EpisodeName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                GetSessionClientLabelFull(s).Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (s.ClientUserAgent?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
                (s.ClientIp?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true));
        }
        if (MethodFilter != null) result = result.Where(s => s.PlayMethod == MethodFilter);
        if (NodeFilter != null) result = result.Where(s => s.ReportingNode == NodeFilter);
        if (TypeFilter != null) result = result.Where(s => s.MediaType == TypeFilter);

        // Apply sort
        result = ApplySort(result);

        FilteredSessions.Clear();
        foreach (var s in result) FilteredSessions.Add(s);
    }

    private IEnumerable<AdminSession> ApplySort(IEnumerable<AdminSession> sessions)
    {
        Func<AdminSession, string> keySelector = SortField switch
        {
            "username" => s => s.Username ?? "",
            "media" => s => GetDisplayTitle(s),
            "method" => s => s.PlayMethod ?? "",
            "node" => s => s.ReportingNode ?? "",
            "started" => s => s.StartedAt ?? "",
            _ => s => s.StartedAt ?? ""
        };

        return SortAscending
            ? sessions.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
            : sessions.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase);
    }

    // ===== Formatting helpers =====

    private static readonly Dictionary<string, string> CodecLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aac"] = "AAC",
        ["ac3"] = "AC3",
        ["av1"] = "AV1",
        ["dts"] = "DTS",
        ["dtshd"] = "DTS-HD",
        ["eac3"] = "EAC3",
        ["flac"] = "FLAC",
        ["h264"] = "H.264",
        ["hevc"] = "HEVC",
        ["mp3"] = "MP3",
        ["opus"] = "Opus",
        ["truehd"] = "TrueHD",
    };

    public static string FormatCodecLabel(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec)) return "\u2014";
        var trimmed = codec.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "\u2014";
        return CodecLabels.TryGetValue(trimmed, out var label) ? label : trimmed.ToUpperInvariant();
    }

    public static string FormatChannelLayout(int? channels)
    {
        if (!channels.HasValue || channels.Value <= 0) return "";
        return channels.Value switch
        {
            1 => "1.0",
            2 => "2.0",
            6 => "5.1",
            8 => "7.1",
            _ => $"{channels.Value}ch"
        };
    }

    public static string GetDisplayTitle(AdminSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.SeriesName) && session.SeasonNumber != null && session.EpisodeNumber != null)
            return !string.IsNullOrWhiteSpace(session.EpisodeName)
                ? session.EpisodeName
                : $"S{session.SeasonNumber}E{session.EpisodeNumber}";
        return !string.IsNullOrWhiteSpace(session.MediaTitle)
            ? session.MediaTitle
            : $"File #{session.MediaFileId}";
    }

    public static string? GetDisplaySubtitle(AdminSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.SeriesName) && session.SeasonNumber != null && session.EpisodeNumber != null)
        {
            var ep = $"S{session.SeasonNumber}E{session.EpisodeNumber}";
            return $"{ep} \u00b7 {session.SeriesName}";
        }
        if (session.MediaType == "movie") return "Movie";
        if (session.MediaType == "series") return "Series";
        return null;
    }

    public static string FormatVideoSummary(AdminSession session)
    {
        var parts = new List<string>();
        var codec = FormatCodecLabel(session.SourceVideoCodec);
        if (codec != "\u2014") parts.Add(codec);
        var res = session.SourceVideoResolution?.Trim();
        if (!string.IsNullOrEmpty(res)) parts.Add(res);
        return parts.Count > 0 ? string.Join(" \u00b7 ", parts) : "Unknown source";
    }

    public static string FormatVideoDetail(AdminSession session)
    {
        var decision = NormalizeStreamDecision(session.VideoDecision ?? session.PlayMethod);

        var targetParts = new List<string>();
        var targetCodec = FormatCodecLabel(session.TargetVideoCodec);
        if (targetCodec != "\u2014") targetParts.Add(targetCodec);
        var targetResolution = session.TargetResolution?.Trim();
        if (!string.IsNullOrEmpty(targetResolution)) targetParts.Add(targetResolution);
        var target = string.Join(" \u00b7 ", targetParts);

        // Auto-switched source hint: server chose a different file than requested.
        if (session.RequestedMediaFileId > 0 && session.MediaFileId > 0
            && session.RequestedMediaFileId != session.MediaFileId)
        {
            var reqParts = new List<string>();
            var reqCodec = FormatCodecLabel(session.RequestedVideoCodec);
            if (reqCodec != "\u2014") reqParts.Add(reqCodec);
            var reqRes = session.RequestedVideoResolution?.Trim();
            if (!string.IsNullOrEmpty(reqRes)) reqParts.Add(reqRes);
            if (reqParts.Count > 0)
            {
                var autoSwitchParts = new List<string> { $"Auto-switched from {string.Join(" \u00b7 ", reqParts)}" };
                if (!string.IsNullOrEmpty(target)) autoSwitchParts.Add($"Output \u2192 {target}");
                else if (decision == "transcode") autoSwitchParts.Add("Transcoding");
                return string.Join(" \u00b7 ", autoSwitchParts);
            }
        }

        if (decision == "transcode")
        {
            return !string.IsNullOrEmpty(target) ? $"Output \u2192 {target}" : "Transcoding";
        }
        if (decision == "copy") return "Video stream copied";
        if (decision == "direct") return "No video conversion";
        return "\u2014";
    }

    public static string FormatAudioSummary(AdminSession session)
    {
        var lead = session.SourceAudioTitle?.Trim();
        if (string.IsNullOrEmpty(lead)) lead = session.SourceAudioLanguage?.Trim();

        var formatParts = new List<string>();
        var codec = FormatCodecLabel(session.SourceAudioCodec);
        if (codec != "\u2014") formatParts.Add(codec);
        var ch = FormatChannelLayout(session.SourceAudioChannels);
        if (!string.IsNullOrEmpty(ch)) formatParts.Add(ch);
        var format = string.Join(" ", formatParts);

        var summaryParts = new List<string>();
        if (!string.IsNullOrEmpty(lead)) summaryParts.Add(lead);
        if (!string.IsNullOrEmpty(format)) summaryParts.Add(format);
        return summaryParts.Count > 0 ? string.Join(" \u00b7 ", summaryParts) : "Unknown source";
    }

    public static string FormatAudioDetail(AdminSession session)
    {
        var decision = NormalizeStreamDecision(session.AudioDecision ?? (session.TranscodeAudio ? "transcode" : session.PlayMethod));
        if (decision == "transcode")
        {
            var parts = new List<string>();
            var codec = FormatCodecLabel(session.TargetAudioCodec ?? "aac");
            if (codec != "\u2014") parts.Add(codec);
            var ch = FormatChannelLayout(session.SourceAudioChannels);
            if (!string.IsNullOrEmpty(ch)) parts.Add(ch);
            var target = string.Join(" ", parts);
            return !string.IsNullOrEmpty(target) ? $"\u2192 {target}" : "Audio transcode";
        }
        if (decision == "copy") return "Audio stream copied";
        if (decision == "direct") return "No audio conversion";
        return "\u2014";
    }

    public static string FormatSourceContainer(AdminSession session)
        => string.IsNullOrWhiteSpace(session.SourceContainer) ? "Unknown source" : session.SourceContainer.Trim().ToUpperInvariant();

    public static string FormatDeliveredContainer(AdminSession session)
        => session.PlayMethod switch
        {
            "direct" => FormatSourceContainer(session),
            "remux" => "Remux",
            "transcode" or "hls" => "HLS",
            _ => FormatSourceContainer(session),
        };

    public static string FormatContainerDetail(AdminSession session)
        => session.PlayMethod switch
        {
            "direct" => "Original container",
            "remux" => $"{FormatSourceContainer(session)} → Remux",
            "transcode" or "hls" => $"{FormatSourceContainer(session)} → HLS",
            _ => "—",
        };

    public static string FormatDeliveredVideo(AdminSession session)
        => session.VideoDecision == "transcode" || session.PlayMethod == "transcode"
            ? string.Join(" · ", new[] { FormatCodecLabel(session.TargetVideoCodec), session.TargetResolution?.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value) && value != "—")) is { Length: > 0 } target ? target : "Transcoding"
            : FormatVideoSummary(session);

    public static string FormatDeliveredAudio(AdminSession session)
        => session.AudioDecision == "transcode" || session.TranscodeAudio
            ? string.Join(" ", new[] { FormatCodecLabel(session.TargetAudioCodec ?? "aac"), FormatChannelLayout(session.SourceAudioChannels) }.Where(value => !string.IsNullOrWhiteSpace(value) && value != "—"))
            : FormatAudioSummary(session);

    public static string? FormatTranscodeMode(AdminSession session)
    {
        var videoTranscode = session.VideoDecision == "transcode" || session.PlayMethod == "transcode";
        var audioTranscode = session.AudioDecision == "transcode" || session.TranscodeAudio;
        if (!videoTranscode && !audioTranscode) return null;
        if (!videoTranscode) return "Audio SW";
        return session.TranscodeHwAccel?.Trim().ToLowerInvariant() switch
        {
            "qsv" => "HW QSV",
            "vaapi" => "HW VAAPI",
            "none" => "SW",
            "auto" => "HW/SW pending",
            null or "" => "HW/SW unknown",
            var value => $"HW {value.ToUpperInvariant()}",
        };
    }

    public static string FormatSessionBitrate(int? kbps)
    {
        if (!kbps.HasValue || kbps.Value <= 0) return "";
        if (kbps.Value >= 1000) return $"{kbps.Value / 1000.0:F1} Mbps";
        return $"{kbps.Value} kbps";
    }

    public static string GetSessionClientLabel(AdminSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.ClientLabel)) return session.ClientLabel.Trim();
        if (!string.IsNullOrWhiteSpace(session.ClientName) && !string.IsNullOrWhiteSpace(session.ClientVersion))
            return $"{session.ClientName.Trim()} {session.ClientVersion.Trim()}";
        return session.ClientName?.Trim() ?? "";
    }

    /// <summary>
    /// Exact client identity for expanded details and tooltips. Current servers
    /// provide this separately so compact activity rows do not become wider as
    /// version, build, and non-release channel metadata is added.
    /// </summary>
    public static string GetSessionClientLabelFull(AdminSession session) =>
        !string.IsNullOrWhiteSpace(session.ClientLabelFull)
            ? session.ClientLabelFull.Trim()
            : GetSessionClientLabel(session);

    public static string NormalizeContainerDecision(string? playMethod) => playMethod?.Trim() switch
    {
        "direct" => "direct",
        "remux" => "remux",
        "transcode" or "hls" => "hls",
        _ => "",
    };

    public static string NormalizeStreamDecision(string? decision) => decision?.Trim() switch
    {
        "direct" => "direct",
        "copy" or "remux" => "copy",
        "transcode" => "transcode",
        _ => "",
    };

    public static string FormatPlaybackPosition(AdminSession session)
    {
        var current = FormatClockTime(session.PositionSeconds);
        return session.FileDuration is > 0 ? $"{current} / {FormatClockTime(session.FileDuration.Value)}" : current;
    }

    private static string FormatClockTime(double seconds)
    {
        var value = Math.Max(0, (int)Math.Floor(seconds));
        var hours = value / 3600;
        var minutes = value % 3600 / 60;
        var secs = value % 60;
        return hours > 0 ? $"{hours}:{minutes:D2}:{secs:D2}" : $"{minutes}:{secs:D2}";
    }

    public static string GetElapsed(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return "0:00";
        var diff = DateTime.UtcNow - dt.ToUniversalTime();
        if (diff.TotalSeconds < 0) diff = TimeSpan.Zero;
        int totalSec = (int)diff.TotalSeconds;
        int h = totalSec / 3600;
        int m = (totalSec % 3600) / 60;
        int s = totalSec % 60;
        if (h > 0) return $"{h}:{m:D2}:{s:D2}";
        return $"{m}:{s:D2}";
    }

    public static string FormatDecision(string? decision) =>
        decision switch
        {
            "direct" => "Direct",
            "copy" => "Copy",
            "remux" => "Remux",
            "hls" => "HLS",
            "transcode" => "Transcode",
            _ => "Unknown"
        };

    public static string FormatLocaleDateTime(string dateStr)
    {
        if (!DateTimeOffset.TryParse(dateStr, out var dt)) return "";
        return DateTimeDisplay.FormatDateTime(dt);
    }
}
