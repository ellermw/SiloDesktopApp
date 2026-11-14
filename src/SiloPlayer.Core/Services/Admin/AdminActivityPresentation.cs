using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Colour role of a method/decision; the UI maps it to success/info/warning/destructive/muted brushes.</summary>
public enum ActivityTone { Success, Info, Warning, Destructive, Muted }

public sealed record ActivityMethodMeta(string Key, string Label, string Description, ActivityTone Tone);

public enum ActivityRouteKind { Transcode, Proxy, Server, Legacy }

public sealed record ActivityRouteNode(string Key, ActivityRouteKind Kind, string Label, string Name);

/// <summary>
/// Pure session presentation, ported line for line from WebUI
/// <c>web/src/pages/adminActivityPresentation.ts</c> (upstream 1a99f7be). The
/// Activity page, the server-activity popover and the dashboard share it so
/// labels and colours cannot drift apart.
/// </summary>
public static class AdminActivityPresentation
{
    /// <summary>Formats a message id; tests and sort keys may pass an English formatter.</summary>
    public delegate string TextFormat(string key, object? values = null);

    private static readonly TextFormat DefaultFormat = AdminText.T;

    public static string FormatDecisionLabel(string? decision) => decision switch
    {
        "direct" => AdminText.T("web.admin.activity.decision.direct"),
        "copy" => AdminText.T("web.admin.activity.decision.copy"),
        "remux" => AdminText.T("web.admin.activity.decision.remux"),
        "hls" => "HLS",
        "transcode" => AdminText.T("web.admin.activity.decision.transcode"),
        _ => AdminText.T("web.admin.activity.decision.unknown"),
    };

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

    private static string Or(string? value, string? fallback) => string.IsNullOrEmpty(value) ? fallback ?? "" : value;

    public static string VideoDecision(AdminActivitySession s) => NormalizeStreamDecision(Or(s.VideoDecision, s.PlayMethod));

    public static string AudioDecision(AdminActivitySession s) =>
        NormalizeStreamDecision(Or(s.AudioDecision, s.TranscodeAudio ? "transcode" : s.PlayMethod));

    /// <summary>Whole-session method bucket: the server's effective_play_method, else the local reduction.</summary>
    public static string ClassifyActivityMethod(AdminActivitySession s)
    {
        if (!string.IsNullOrEmpty(s.EffectivePlayMethod)) return s.EffectivePlayMethod;
        var video = VideoDecision(s);
        var audio = AudioDecision(s);
        if (video == "transcode") return "transcode";
        if (audio == "transcode" && video != "") return "direct_stream";
        if (video == "direct" && audio == "direct") return "direct";
        if (video == "copy" || audio == "copy") return "remux";
        return "unknown";
    }

    private static readonly string[] MethodOrder = ["direct", "remux", "direct_stream", "transcode", "unknown"];

    private static int MethodRank(string method)
    {
        var index = Array.IndexOf(MethodOrder, method);
        return index < 0 ? MethodOrder.Length : index;
    }

    public static int CompareActivityMethods(string a, string b)
    {
        var diff = MethodRank(a) - MethodRank(b);
        return diff != 0 ? diff : string.Compare(a, b, StringComparison.CurrentCulture);
    }

    public static ActivityMethodMeta MethodMeta(string method) => method switch
    {
        "direct" => new(method, AdminText.T("web.common.activity.method.direct.label"),
            AdminText.T("web.common.activity.method.direct.description"), ActivityTone.Success),
        "remux" => new(method, AdminText.T("web.common.activity.method.remux.label"),
            AdminText.T("web.common.activity.method.remux.description"), ActivityTone.Info),
        "direct_stream" => new(method, AdminText.T("web.common.activity.method.directStream.label"),
            AdminText.T("web.common.activity.method.directStream.description"), ActivityTone.Warning),
        "transcode" => new(method, AdminText.T("web.common.activity.method.transcode.label"),
            AdminText.T("web.common.activity.method.transcode.description"), ActivityTone.Destructive),
        _ => new(method, AdminText.T("web.common.activity.method.unknown.label"),
            AdminText.T("web.common.activity.method.unknown.description"), ActivityTone.Muted),
    };

    /// <summary>Copy and HLS are repackaging, so they share the remux tint.</summary>
    public static ActivityTone DecisionTone(string decision) =>
        decision is "copy" or "hls" ? MethodMeta("remux").Tone : MethodMeta(decision).Tone;

    public static bool IsJellyfinSession(AdminActivitySession s) => s.IsJellyfinClient == true;

    public static string FormatPlaybackDecisionSummary(AdminActivitySession s)
    {
        var video = VideoDecision(s);
        var audio = AudioDecision(s);
        if (video != "" && video == audio) return video;
        if (video == "transcode" || audio == "transcode") return "transcode";
        if (video == "copy" || audio == "copy") return "copy";
        return Or(Or(video, audio), s.PlayMethod);
    }

    public static string? FormatTranscodeModeSummary(AdminActivitySession s)
    {
        var video = VideoDecision(s);
        var audio = AudioDecision(s);
        if (video != "transcode" && audio != "transcode") return null;
        if (video != "transcode") return AdminText.T("web.admin.activity.transcodeMode.audioTranscode");
        var accel = s.TranscodeHwAccel?.Trim().ToLowerInvariant();
        return accel switch
        {
            "qsv" => AdminText.T("web.admin.activity.transcodeMode.hardware", new { accel = "QSV" }),
            "vaapi" => AdminText.T("web.admin.activity.transcodeMode.hardware", new { accel = "VAAPI" }),
            "videotoolbox" => AdminText.T("web.admin.activity.transcodeMode.hardware", new { accel = "VideoToolbox" }),
            "none" => AdminText.T("web.admin.activity.transcodeMode.software"),
            "auto" => AdminText.T("web.admin.activity.transcodeMode.pending"),
            null or "" => AdminText.T("web.admin.activity.transcodeMode.unknown"),
            _ => AdminText.T("web.admin.activity.transcodeMode.hardware", new { accel = accel.ToUpperInvariant() }),
        };
    }

    public sealed record ToneMapSummary(string Badge, string Detail, string Mode);

    public static ToneMapSummary? FormatToneMapSummary(AdminActivitySession s)
    {
        if (VideoDecision(s) != "transcode") return null;
        return s.ToneMapMode?.Trim().ToLowerInvariant() switch
        {
            "hardware" => new(AdminText.T("web.admin.activity.toneMap.hardwareBadge"), AdminText.T("web.admin.activity.toneMap.hardware"), "hardware"),
            "software" => new(AdminText.T("web.admin.activity.toneMap.softwareBadge"), AdminText.T("web.admin.activity.toneMap.software"), "software"),
            _ => null,
        };
    }

    /// <summary>Bitrate units are written the same way in every language.</summary>
    public static string? FormatSessionBitrate(long? kbps)
    {
        if (kbps is not > 0) return null;
        if (kbps >= 1000) return (kbps.Value / 1000d).ToString("0.0", CultureInfo.CurrentCulture) + " Mbps";
        return Math.Round((double)kbps.Value).ToString(CultureInfo.CurrentCulture) + " kbps";
    }

    public static string GetSessionClientLabel(AdminActivitySession s)
    {
        var explicitLabel = s.ClientLabel?.Trim();
        if (!string.IsNullOrEmpty(explicitLabel)) return explicitLabel;
        var name = s.ClientName?.Trim();
        var version = s.ClientVersion?.Trim();
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version)) return $"{name} {version}";
        return name ?? "";
    }

    public static string GetSessionClientLabelFull(AdminActivitySession s)
    {
        var full = s.ClientLabelFull?.Trim();
        return string.IsNullOrEmpty(full) ? GetSessionClientLabel(s) : full;
    }

    private static ActivityRouteNode NamedRouteNode(TextFormat format, ActivityRouteKind kind, string? name, string? id)
    {
        var label = kind == ActivityRouteKind.Transcode
            ? format("web.admin.activity.route.transcode")
            : format("web.admin.activity.route.proxy");
        var trimmed = name?.Trim();
        var unknown = kind == ActivityRouteKind.Transcode
            ? format("web.admin.activity.route.unknownTranscodeNode")
            : format("web.admin.activity.route.unknownProxyNode");
        var prefix = kind == ActivityRouteKind.Transcode ? "transcode" : "proxy";
        return new ActivityRouteNode(
            $"{prefix}:{id ?? (string.IsNullOrEmpty(trimmed) ? "unknown" : trimmed)}",
            kind,
            label,
            !string.IsNullOrEmpty(trimmed) ? trimmed : id != null ? format("web.admin.activity.route.nodeId", new { id }) : unknown);
    }

    private static ActivityRouteNode ReportingServerRouteNode(TextFormat format, AdminActivitySession s)
    {
        var reported = s.ReportingNode?.Trim();
        if (string.IsNullOrEmpty(reported) || reported.Equals("local", StringComparison.OrdinalIgnoreCase))
            return new("server:local", ActivityRouteKind.Server, format("web.admin.activity.route.server"),
                format("web.admin.activity.route.localServer"));
        return new($"server:{reported}", ActivityRouteKind.Server, format("web.admin.activity.route.server"), reported);
    }

    /// <summary>Registered playback nodes in work-to-viewer order; keys never follow the interface language.</summary>
    public static IReadOnlyList<ActivityRouteNode> GetSessionRouteNodes(AdminActivitySession s, TextFormat? format = null)
    {
        format ??= DefaultFormat;
        var execution = s.RoutingExecution?.Trim();
        var egress = s.RoutingEgress?.Trim();
        var resolved = !string.IsNullOrEmpty(s.RoutingWorkload?.Trim()) || !string.IsNullOrEmpty(execution) || !string.IsNullOrEmpty(egress);
        if (!resolved)
        {
            var reported = Or(s.NodeDisplayName?.Trim(), s.ReportingNode?.Trim());
            var isLocal = reported == "" || reported.Equals("local", StringComparison.OrdinalIgnoreCase);
            return [new(isLocal ? "legacy:local" : $"legacy:{reported}", ActivityRouteKind.Legacy,
                format("web.admin.activity.route.node"), isLocal ? format("web.admin.activity.route.localServer") : reported)];
        }
        var nodes = new List<ActivityRouteNode>();
        var executionName = Or(s.RoutingExecutionNodeName?.Trim(), s.NodeDisplayName);
        if (execution == "transcode") nodes.Add(NamedRouteNode(format, ActivityRouteKind.Transcode, executionName, s.RoutingExecutionNodeId));
        else if (execution == "proxy") nodes.Add(NamedRouteNode(format, ActivityRouteKind.Proxy, executionName, s.RoutingExecutionNodeId));
        if (egress == "proxy")
        {
            var egressNode = NamedRouteNode(format, ActivityRouteKind.Proxy, s.RoutingEgressNodeName, s.RoutingEgressNodeId);
            if (nodes.All(node => node.Key != egressNode.Key)) nodes.Add(egressNode);
        }
        if (egress == "api" || (execution == "api" && nodes.Count == 0)) nodes.Add(ReportingServerRouteNode(format, s));
        return nodes.Count > 0 ? nodes : [ReportingServerRouteNode(format, s)];
    }

    public static string FormatSourceContainerSummary(AdminActivitySession s) =>
        FormatContainer(s.SourceContainer) ?? AdminText.T("web.admin.activity.summary.unknownSource");

    public static string FormatDeliveredContainerSummary(AdminActivitySession s)
    {
        if (NormalizeContainerDecision(s.PlayMethod) == "direct") return FormatSourceContainerSummary(s);
        var container = s.OutputContainer?.Trim().ToLowerInvariant();
        string label;
        if (!string.IsNullOrEmpty(container))
            label = container switch
            {
                "fmp4" => "fMP4",
                "mpegts" => "MPEG-TS",
                _ => FormatContainer(container) ?? AdminText.T("web.admin.activity.summary.unknownOutputContainer"),
            };
        else
            label = AdminText.T("web.admin.activity.summary.unknownOutputContainer");
        return s.OutputProtocol == "hls" ? AdminText.T("web.admin.activity.summary.hlsOutput", new { container = label }) : label;
    }

    public static string FormatContainerDetail(AdminActivitySession s)
    {
        var source = FormatSourceContainerSummary(s);
        if (!string.IsNullOrEmpty(s.OutputContainer) && NormalizeContainerDecision(s.PlayMethod) != "direct")
            return AdminText.T("web.admin.activity.detail.conversion", new { source, output = FormatDeliveredContainerSummary(s) });
        return NormalizeContainerDecision(s.PlayMethod) switch
        {
            "direct" => AdminText.T("web.admin.activity.detail.originalContainer"),
            "remux" or "hls" => AdminText.T("web.admin.activity.detail.repackaged"),
            _ => "—",
        };
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrEmpty(part)));

    public static string FormatVideoSummary(AdminActivitySession s) =>
        Or(Join(" · ", FormatCodec(s.SourceVideoCodec), s.SourceVideoResolution?.Trim()), AdminText.T("web.admin.activity.summary.unknownSource"));

    private static string? DeliveredResolutionLabel(AdminActivitySession s)
    {
        var target = s.TargetResolution?.Trim();
        var source = s.SourceVideoResolution?.Trim();
        int? targetHeight = null;
        if (target is { Length: > 1 } && (target.EndsWith('p') || target.EndsWith('P')) &&
            int.TryParse(target[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            targetHeight = parsed;
        if (!string.IsNullOrEmpty(source) && s.SourceVideoHeight is > 0 && targetHeight == s.SourceVideoHeight) return source;
        return target;
    }

    public static string FormatDeliveredVideoSummary(AdminActivitySession s)
    {
        var decision = Or(s.VideoDecision, s.PlayMethod);
        if (decision != "transcode") return FormatVideoSummary(s);
        return Or(Join(" · ", FormatCodec(s.TargetVideoCodec), DeliveredResolutionLabel(s)), AdminText.T("web.admin.activity.detail.transcoding"));
    }

    public static string FormatVideoDetail(AdminActivitySession s)
    {
        var decision = VideoDecision(s);
        var requested = FormatRequestedVideoSource(s);
        var target = Join(" · ", FormatCodec(s.TargetVideoCodec), DeliveredResolutionLabel(s));
        if (HasRequestedSourceSwitch(s) && requested != null)
        {
            var parts = new List<string> { AdminText.T("web.admin.activity.detail.autoSwitched", new { source = requested }) };
            if (target != "") parts.Add(AdminText.T("web.admin.activity.detail.output", new { output = target }));
            else if (decision == "transcode") parts.Add(AdminText.T("web.admin.activity.detail.transcoding"));
            return string.Join(" · ", parts);
        }
        return decision switch
        {
            "transcode" => target != ""
                ? AdminText.T("web.admin.activity.detail.output", new { output = target })
                : AdminText.T("web.admin.activity.detail.transcoding"),
            "copy" => AdminText.T("web.admin.activity.detail.copied"),
            "direct" => AdminText.T("web.admin.activity.detail.noVideoConversion"),
            _ => "—",
        };
    }

    public static string FormatAudioSummary(AdminActivitySession s)
    {
        var lead = Or(s.SourceAudioTitle?.Trim(), s.SourceAudioLanguage?.Trim());
        var format = Join(" ", FormatCodec(s.SourceAudioCodec), FormatChannelLayout(s.SourceAudioChannels));
        return Or(Join(" · ", lead, format), AdminText.T("web.admin.activity.summary.unknownSource"));
    }

    /// <summary>Target codec plus the target layout only when reported; never the source's channel count.</summary>
    private static string FormatTargetAudio(AdminActivitySession s) =>
        Join(" ", FormatCodec(Or(s.TargetAudioCodec, "aac")), FormatChannelLayout(s.TargetAudioChannels));

    public static string FormatDeliveredAudioSummary(AdminActivitySession s)
    {
        var decision = Or(s.AudioDecision, s.TranscodeAudio ? "transcode" : s.PlayMethod);
        if (decision != "transcode") return FormatAudioSummary(s);
        return Or(FormatTargetAudio(s), AdminText.T("web.admin.activity.transcodeMode.audioTranscode"));
    }

    public static string FormatAudioDetail(AdminActivitySession s) => AudioDecision(s) switch
    {
        "transcode" => FormatTargetAudio(s) is { Length: > 0 } target
            ? AdminText.T("web.admin.activity.detail.audioOutput", new { output = target })
            : AdminText.T("web.admin.activity.transcodeMode.audioTranscode"),
        "copy" => AdminText.T("web.admin.activity.detail.copied"),
        "direct" => AdminText.T("web.admin.activity.detail.noAudioConversion"),
        _ => "—",
    };

    private static bool HasRequestedSourceSwitch(AdminActivitySession s) =>
        long.TryParse(s.RequestedMediaFileId, out var requested) && long.TryParse(s.MediaFileId, out var actual) &&
        requested > 0 && actual > 0 && requested != actual;

    private static string? FormatRequestedVideoSource(AdminActivitySession s)
    {
        var value = Join(" · ", FormatCodec(s.RequestedVideoCodec), s.RequestedVideoResolution?.Trim());
        return value == "" ? null : value;
    }

    private static readonly IReadOnlyDictionary<string, string> CodecLabels = new Dictionary<string, string>
    {
        ["aac"] = "AAC", ["ac3"] = "AC3", ["av1"] = "AV1", ["dts"] = "DTS", ["dtshd"] = "DTS-HD", ["eac3"] = "EAC3",
        ["flac"] = "FLAC", ["h264"] = "H.264", ["hevc"] = "HEVC", ["mp3"] = "MP3", ["opus"] = "Opus", ["truehd"] = "TrueHD",
    };

    /// <summary>WebUI <c>formatCodecLabel</c>: exact-match label, unknown codecs uppercased.</summary>
    public static string? FormatCodec(string? codec)
    {
        var trimmed = codec?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return CodecLabels.TryGetValue(trimmed.ToLowerInvariant(), out var label) ? label : trimmed.ToUpperInvariant();
    }

    private static string? FormatContainer(string? container)
    {
        var trimmed = container?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    private static bool IsEpisode(AdminActivitySession s) =>
        !string.IsNullOrEmpty(s.SeriesName) && s.SeasonNumber != null && s.EpisodeNumber != null;

    public static string GetPlaybackSessionTitle(AdminActivitySession s, TextFormat? format = null)
    {
        format ??= DefaultFormat;
        if (IsEpisode(s)) return Or(s.EpisodeName, $"S{s.SeasonNumber}E{s.EpisodeNumber}");
        return Or(s.MediaTitle, format("web.common.activity.title.file", new { id = s.MediaFileId }));
    }

    public static string? GetPlaybackSessionSubtitle(AdminActivitySession s)
    {
        if (IsEpisode(s)) return $"S{s.SeasonNumber}E{s.EpisodeNumber} · {s.SeriesName}";
        return s.MediaType switch
        {
            "movie" => AdminText.T("web.common.activity.subtitle.movie"),
            "series" => AdminText.T("web.common.activity.subtitle.series"),
            _ => null,
        };
    }

    public static string? FormatChannelLayout(int? channels) => channels switch
    {
        null or <= 0 => null,
        1 => "1.0",
        2 => "2.0",
        6 => "5.1",
        8 => "7.1",
        _ => $"{channels}ch",
    };

    /// <summary>"m:ss" or "h:mm:ss", as the WebUI's clock format.</summary>
    public static string FormatClockTime(double seconds)
    {
        var safe = (long)Math.Max(0, Math.Floor(double.IsFinite(seconds) ? seconds : 0));
        var h = safe / 3600;
        var m = safe % 3600 / 60;
        var sec = safe % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    public static string FormatPlaybackPosition(AdminActivitySession s)
    {
        var position = FormatClockTime(s.PositionSeconds);
        return s.FileDuration is not > 0 ? position : $"{position} / {FormatClockTime(s.FileDuration.Value)}";
    }

    /// <summary>Elapsed time since the session started, as "m:ss"/"h:mm:ss".</summary>
    public static string GetElapsed(string startedAt, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
            return FormatClockTime(0);
        return FormatClockTime(Math.Max(0, (now - started).TotalSeconds));
    }

    /// <summary>FFmpeg console text: ffmpeg_line, then ffmpeg_event, then the message.</summary>
    public static string FfmpegRowText(AdminActivityLogRow row)
    {
        var line = LogAttr(row, "ffmpeg_line");
        if (line != "-") return line;
        var ev = LogAttr(row, "ffmpeg_event");
        return ev != "-" ? ev : row.Message;
    }

    public static string FfmpegRowKind(AdminActivityLogRow row) =>
        row.Message.Contains("stderr", StringComparison.Ordinal)
            ? AdminText.T("web.admin.activity.panel.stderr")
            : AdminText.T("web.admin.activity.panel.event");

    /// <summary>WebUI <c>stringAttr</c>: non-empty strings and numbers, otherwise "-".</summary>
    public static string LogAttr(AdminActivityLogRow row, string key)
    {
        if (row.Attrs == null || !row.Attrs.TryGetValue(key, out var value)) return "-";
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String when value.GetString() is { Length: > 0 } text => text,
            System.Text.Json.JsonValueKind.Number => value.GetRawText(),
            _ => "-",
        };
    }

    /// <summary>The playback-log route the row menu and console open, as WebUI's logsHref.</summary>
    public static AdminRouteRequest LogsRoute(string sessionId, bool ffmpeg)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["playback_session_id"] = sessionId,
            ["focus"] = "playback",
        };
        if (ffmpeg) query["component"] = "ffmpeg";
        return new AdminRouteRequest(AdminRoute.ServerLog, query);
    }
}
