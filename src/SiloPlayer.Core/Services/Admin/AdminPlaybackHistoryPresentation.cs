using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Row text for Playback History (WebUI <c>historyRowTitle</c>, <c>watchTime</c>, <c>formatDuration</c>, <c>mediaTypeLabel</c>).</summary>
public static class AdminPlaybackHistoryPresentation
{
    public sealed record FilterChip(string Key, string Label, string[] RemoveKeys, string? ItemId = null);

    private static string T(string key, params (string, object?)[] values) => AdminPlaybackAuditText.T(key, values);

    public static IReadOnlyList<FilterChip> Chips(AdminPlaybackHistoryFilter filter, IReadOnlyList<AdminPlaybackHistoryRow> rows)
    {
        var chips = new List<FilterChip>();
        var first = rows.FirstOrDefault();
        var item = filter.RequestMediaItemId;
        var series = filter.RequestSeriesId;
        if ((item ?? series) is { } titleId)
        {
            var label = item != null && first != null ? RowLabel(first) : titleId;
            if (item == null && first is { SeriesTitle.Length: > 0 }) label = first.SeriesTitle;
            chips.Add(new("title", label, ["media_item_id", "series_id"], titleId));
        }
        Add("client_name", "client", filter.RequestClientName);
        Add("device_platform", "platform", filter.RequestDevicePlatform);
        Add("source_video_codec", "codec", AdminActivityPresentation.FormatCodec(filter.RequestSourceVideoCodec));
        if (filter.RequestAdaptationReason is { } reason)
            Add("adaptation_reason", "reason", AdminActivityReasons.FromCodes([reason]).FirstOrDefault()?.Label ?? reason);
        var from = filter.RequestStartedAfter;
        var to = filter.RequestStartedBefore;
        if (from != null && to != null)
            chips.Add(new("date_range", T("web.admin.history.filter.activeChip.dateRange", ("from", ChipDate(from)), ("to", ChipDate(to))),
                ["started_after", "started_before"]));
        else if (from != null)
            chips.Add(new("started_after", T("web.admin.history.filter.activeChip.fromDate", ("from", ChipDate(from))), ["started_after"]));
        else if (to != null)
            chips.Add(new("started_before", T("web.admin.history.filter.activeChip.toDate", ("to", ChipDate(to))), ["started_before"]));
        return chips;

        void Add(string key, string labelKey, string? value)
        {
            if (value != null) chips.Add(new(key, T("web.admin.history.filter.activeChip." + labelKey, ("value", value)), [key]));
        }
    }

    private static string ChipDate(string value) => AdminPlaybackAuditText.TryParseInstant(value, out var instant)
        ? instant.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : value;

    public static string MethodKey(string method) => method switch
    {
        "direct" or "direct_play" => "direct",
        "remux" or "direct_stream" => "remux",
        "transcode" => "transcode",
        _ => "unknown",
    };

    public static AdminActivitySession AsActivitySession(AdminPlaybackHistoryRow row) => new()
    {
        SessionId = row.SessionId, UserId = row.UserId, Username = row.Username,
        ProfileId = row.ProfileId, ProfileName = row.ProfileName,
        MediaItemId = row.MediaItemId, MediaFileId = row.MediaFileId, RequestedMediaFileId = row.MediaFileId,
        MediaTitle = row.MediaTitle, MediaType = row.MediaType, SeriesTitle = row.SeriesTitle.Length > 0 ? row.SeriesTitle : null,
        SeasonNumber = row.SeasonNumber, EpisodeNumber = row.EpisodeNumber,
        PlayMethod = row.PlayMethod, VideoDecision = row.VideoDecision, AudioDecision = row.AudioDecision,
        PositionSeconds = row.WatchedSeconds, FileDuration = row.DurationSeconds is >= 0 and < long.MaxValue ? (long)row.DurationSeconds.Value : null,
        HasPlaybackControl = false, StartedAt = row.StartedAt, UpdatedAt = row.EndedAt,
        ClientIp = row.ClientIp, ClientName = row.ClientName, ClientVersion = row.ClientVersion,
        DeviceId = row.DeviceId, DevicePlatform = row.DevicePlatform, IsJellyfinClient = row.IsJellyfinClient,
        TranscodeAudio = row.AudioDecision == "transcode" || (row.PlayMethod == "transcode" && !string.IsNullOrEmpty(row.TargetAudioCodec)),
        TargetVideoCodec = row.TargetVideoCodec, TargetResolution = row.TargetResolution, TargetAudioCodec = row.TargetAudioCodec,
        TargetAudioChannels = ChannelCount(row.TargetAudioChannels), TargetBitrateKbps = row.TargetBitrateKbps,
        OutputContainer = row.OutputContainer, OutputProtocol = row.OutputProtocol,
        TranscodeHwAccel = row.TranscodeHwAccel, ToneMapMode = row.ToneMapMode, AdaptationReasons = row.AdaptationReasons.ToList(),
        ReportingNode = row.ReportingNode ?? "", StreamLocation = row.StreamLocation,
        RoutingExecution = row.RoutingExecution, RoutingExecutionNodeId = row.RoutingExecutionNodeId,
        RoutingExecutionNodeName = row.RoutingExecutionNodeName,
        RoutingEgress = row.RoutingEgress, RoutingEgressNodeId = row.RoutingEgressNodeId, RoutingEgressNodeName = row.RoutingEgressNodeName,
        SourceVideoCodec = row.SourceVideoCodec, SourceVideoResolution = row.SourceResolution, SourceAudioCodec = row.SourceAudioCodec,
        SourceAudioChannels = ChannelCount(row.SourceAudioChannels), SourceContainer = row.SourceContainer, SourceBitrateKbps = row.SourceBitrateKbps,
    };

    private static int? ChannelCount(long? channels) => channels is >= int.MinValue and <= int.MaxValue ? (int)channels.Value : null;

    public static IReadOnlyList<ActivityRouteNode> RouteNodes(AdminPlaybackHistoryRow row, AdminActivitySession session) =>
        row.RouteFactsRecorded && !(string.IsNullOrWhiteSpace(row.RoutingExecution) &&
            string.IsNullOrWhiteSpace(row.RoutingEgress) && string.IsNullOrWhiteSpace(row.ReportingNode))
            ? AdminActivityPresentation.GetSessionRouteNodes(session) : [];

    public static (string Source, string Delivered) Bitrates(AdminPlaybackHistoryRow row)
    {
        var source = AdminActivityPresentation.FormatSessionBitrate(row.SourceBitrateKbps) ?? "—";
        if (MethodKey(row.PlayMethod) != "transcode" && row.VideoDecision != "transcode") return (source, source);
        var cap = AdminActivityPresentation.FormatSessionBitrate(row.TargetBitrateKbps);
        return (source, cap == null ? "—" : AdminText.T("web.admin.activity.bitrate.limit", new { rate = cap }));
    }

    public static (string Source, string Delivered) Subtitles(AdminPlaybackHistoryRow row)
    {
        const string prefix = "web.admin.history.detail.subtitles";
        if (row.Subtitle is not { } subtitle) return (T(prefix + "None"), T(prefix + "None"));
        var source = string.Join(" · ", new[] { subtitle.Language?.ToUpperInvariant(),
            AdminActivityPresentation.FormatCodec(subtitle.Codec), subtitle.Format?.ToUpperInvariant() }
            .Where(part => !string.IsNullOrEmpty(part)));
        var delivered = subtitle.Delivery switch
        {
            "burn_in" => T(prefix + "BurnIn"),
            "embedded" => T(prefix + "Embedded"),
            "hls_rendition" => T(prefix + "Hls"),
            "sidecar" => T(prefix + "Sidecar"),
            _ => "—",
        };
        return (source, delivered);
    }

    public static string EndReason(string? reason) => reason switch
    {
        "stopped" or "terminated" or "expired" or "aborted" or "replaced" => T("web.admin.history.endReason." + reason),
        null or "" => "—",
        _ => reason,
    };

    /// <summary>An episode leads with its series title; "S02E04 · Episode title" is the detail line.</summary>
    public static (string Title, string Detail) RowTitle(AdminPlaybackHistoryRow row)
    {
        if (row.SeriesTitle.Length > 0)
        {
            var episodeTitle = row.MediaTitle == row.SeriesTitle ? "" : row.MediaTitle;
            var detail = string.Join(" · ", new[] { EpisodeCode(row.SeasonNumber, row.EpisodeNumber), episodeTitle }.Where(part => part.Length > 0));
            return (row.SeriesTitle, detail);
        }
        var title = row.MediaTitle.Length > 0 ? row.MediaTitle : row.MediaItemId.Length > 0 ? row.MediaItemId : T("web.common.activity.title.file", ("id", row.MediaFileId));
        return (title, "");
    }

    public static string RowLabel(AdminPlaybackHistoryRow row)
    {
        var (title, detail) = RowTitle(row);
        return detail.Length > 0 ? $"{title} · {detail}" : title;
    }

    public static string EpisodeCode(int? season, int? episode) =>
        season is not { } s || episode is not { } e ? "" : T("web.admin.users.detail.common.episodeCode",
            ("season", s.ToString("00", CultureInfo.InvariantCulture)), ("episode", e.ToString("00", CultureInfo.InvariantCulture)));

    /// <summary>Measured watch time, or the server estimate (end position capped at the session length).</summary>
    public static (double Seconds, bool Estimated) WatchTime(AdminPlaybackHistoryRow row)
    {
        if (row.PlayedSeconds is { } played) return (played, false);
        var capped = row.WatchedSeconds;
        if (AdminPlaybackAuditText.TryParseInstant(row.EndedAt, out var ended) && AdminPlaybackAuditText.TryParseInstant(row.StartedAt, out var started))
            capped = Math.Min(row.WatchedSeconds, Math.Max((ended - started).TotalSeconds, 0));
        return (Math.Max(capped, 0), true);
    }

    public static string WatchTimeText(AdminPlaybackHistoryRow row)
    {
        var (seconds, estimated) = WatchTime(row);
        return estimated ? T("web.admin.history.row.estimatedDuration", ("duration", FormatDuration(seconds))) : FormatDuration(seconds);
    }

    public static string FormatDuration(double? seconds)
    {
        if (seconds is not { } value || value <= 0 || !double.IsFinite(value)) return T("web.admin.history.duration.none");
        var rounded = (long)Math.Max(0, Math.Floor(value));
        var hours = rounded / 3600;
        var minutes = rounded % 3600 / 60;
        var secs = rounded % 60;
        if (hours > 0) return T("web.admin.history.duration.hoursMinutes", ("hours", hours), ("minutes", minutes));
        if (minutes > 0) return T("web.admin.history.duration.minutesSeconds", ("minutes", minutes), ("seconds", secs));
        return T("web.admin.history.duration.seconds", ("seconds", secs));
    }

    /// <summary>Known media types are labelled; others print as sent; empty is "unknown".</summary>
    public static string MediaTypeLabel(string mediaType) => mediaType switch
    {
        "" => T("web.admin.history.mediaType.unknown"),
        "movie" or "episode" or "series" => T("web.admin.history.mediaType." + mediaType),
        _ => mediaType,
    };

    public static string Meta(AdminPlaybackHistoryRow row) =>
        T("web.admin.history.row.meta", ("media_type", MediaTypeLabel(row.MediaType)), ("session", row.SessionId.Length > 8 ? row.SessionId[..8] : row.SessionId));

    public static string Username(AdminPlaybackHistoryRow row) =>
        row.Username.Length > 0 ? row.Username : T("web.admin.activity.userFallback", ("id", row.UserId));

    public static string ProfileLabel(AdminPlaybackHistoryRow row) => row.ProfileName.Length > 0 ? row.ProfileName : row.ProfileId;

    public static string Method(AdminPlaybackHistoryRow row) => AdminActivityPresentation.FormatDecisionLabel(row.PlayMethod);

    public static string Status(AdminPlaybackHistoryRow row) => T(row.Completed ? "web.admin.history.row.completed" : "web.admin.history.row.partial");

    public static string Started(AdminPlaybackHistoryRow row, DateTimeOffset now) =>
        T("web.admin.history.row.started", ("time", AdminPlaybackAuditText.Relative(row.StartedAt, now) ?? row.StartedAt));

    /// <summary>Logs links (Monitor › Server Log), focused on this playback session.</summary>
    public static AdminRouteRequest LogsRequest(AdminPlaybackHistoryRow row, bool ffmpeg)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal) { ["playback_session_id"] = row.SessionId, ["focus"] = "playback" };
        if (ffmpeg) query["component"] = "ffmpeg";
        return new AdminRouteRequest(AdminRoute.ServerLog, query);
    }

    /// <summary>The profile link: this page, filtered to the row's account and profile.</summary>
    public static AdminRouteRequest ProfileRequest(AdminPlaybackHistoryRow row) =>
        new(AdminRoute.PlaybackHistory, new AdminPlaybackHistoryFilter(row.UserId, row.ProfileId).ToQuery());
}
