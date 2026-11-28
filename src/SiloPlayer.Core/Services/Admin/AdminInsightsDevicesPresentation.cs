using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The Devices &amp; Quality tab (official <c>DevicesTab.tsx</c> and <c>DevicesUsageSections.tsx</c>, c59798116) shaped from one Devices read:
/// the coverage banner, KPI cards, the direct-play matrix, the reason, client, hardware and resolution lists, and the app and track breakdowns.
/// Every list that drills into Playback History carries the drilldown it opens, built with the page's library and account and the read's window.
/// </summary>
public static class AdminInsightsDevicesPresentation
{
    private const string Dash = "—";

    /// <summary>The matrix's sample floor: sessions needed for a client to appear (the "5+ sessions" and "All" choices).</summary>
    public enum SampleThreshold { Five, All }

    public static int MinSessions(SampleThreshold threshold) => threshold == SampleThreshold.Five ? AdminInsightsApi.DefaultMinSessions : 1;

    private static string Sessions(long count) => AdminInsightsRules.SessionCount(count);

    private static string Percent(double? rate) => AdminInsightsNumbers.Percent(rate);

    // ── Notice and KPI cards ─────────────────────────────────

    /// <summary>The route-facts banner: none of the sessions have detailed playback info, or only some do (with the date they start).</summary>
    public static string? Notice(AdminInsightsDevices data)
    {
        var coverage = data.Coverage;
        return AdminInsightsRules.RouteFactsNotice(coverage) switch
        {
            AdminInsightsRules.RouteFactsNoticeKind.None => AdminInsightsText.T("web.admin.insights.coverage.routeFactsNone"),
            AdminInsightsRules.RouteFactsNoticeKind.Partial => AdminInsightsText.T("web.admin.insights.coverage.routeFactsPartial",
                ("recorded", AdminInsightsNumbers.Count(coverage.RouteFactsSessions)), ("total", AdminInsightsNumbers.Count(coverage.TotalSessions)),
                ("total_count", coverage.TotalSessions), ("since", AdminInsightsRules.FormatShortCalendarDate(coverage.OldestRouteFactsAt))),
            _ => null,
        };
    }

    public static IReadOnlyList<AdminInsightsKpi> Kpis(AdminInsightsDevices data)
    {
        var summary = data.Summary;
        var knownEncoders = summary.HwTranscodeSessions + summary.SwTranscodeSessions;
        var burnInShare = summary.TranscodeSessions > 0 ? (double)summary.SubtitleBurnInSessions / summary.TranscodeSessions : 0;
        return
        [
            new(AdminInsightsText.T("web.admin.insights.devices.kpi.directPlayRate"), Percent(summary.DirectRate),
                AdminInsightsText.T("web.admin.insights.devices.kpi.directPlayDetail", ("direct", AdminInsightsNumbers.Count(summary.DirectSessions)),
                    ("remux", AdminInsightsNumbers.Count(summary.RemuxSessions)), ("transcode", AdminInsightsNumbers.Count(summary.TranscodeSessions))),
                ActivityTone.Success),
            new(AdminInsightsText.T("web.admin.insights.devices.kpi.hwSplit"),
                knownEncoders > 0 ? Percent((double)summary.HwTranscodeSessions / knownEncoders) : Dash,
                knownEncoders > 0
                    ? AdminInsightsText.T("web.admin.insights.devices.kpi.hwSplitDetail", ("hw", AdminInsightsNumbers.Count(summary.HwTranscodeSessions)),
                        ("sw", AdminInsightsNumbers.Count(summary.SwTranscodeSessions)))
                    : AdminInsightsText.T("web.admin.insights.devices.kpi.hwSplitNone"),
                ActivityTone.Info),
            new(AdminInsightsText.T("web.admin.insights.devices.kpi.subtitleBurnIn"), AdminInsightsNumbers.Count(summary.SubtitleBurnInSessions),
                AdminInsightsText.T("web.admin.insights.devices.kpi.subtitleBurnInDetail", ("percent", AdminInsightsRules.FormatRatePercentNumber(burnInShare))),
                summary.SubtitleBurnInSessions > 0 ? ActivityTone.Warning : null),
            new(AdminInsightsText.T("web.admin.insights.devices.kpi.hdrToneMap"), AdminInsightsNumbers.Count(summary.HdrTonemapSessions),
                AdminInsightsText.T("web.admin.insights.devices.kpi.hdrToneMapDetail", ("hw", AdminInsightsNumbers.Count(summary.HdrTonemapHwSessions)),
                    ("sw", AdminInsightsNumbers.Count(summary.HdrTonemapSwSessions)))),
        ];
    }

    // ── Drilldowns ───────────────────────────────────────────

    /// <summary>The Playback History location a Devices number opens: its facets, the page's library and account, and the read's own window.</summary>
    public static AdminRouteRequest Drilldown(AdminInsightsDevices data, AdminInsightsFilterState filters, AdminInsightsHistoryDrilldown facets)
        => AdminInsightsRules.BuildHistoryDrilldown(facets with
        {
            LibraryId = filters.LibraryId?.ToString(CultureInfo.InvariantCulture),
            UserId = filters.UserId?.ToString(CultureInfo.InvariantCulture),
            StartedAfter = data.From,
            StartedBefore = data.To,
        });

    /// <summary>"unknown" stands for a client or platform the server recorded none for.</summary>
    private static string OrUnknown(string value) => string.IsNullOrEmpty(value) ? "unknown" : value;

    public static string ClientLabel(string clientName) => string.IsNullOrEmpty(clientName) ? AdminInsightsText.T("web.admin.insights.devices.unknownClient") : clientName;

    /// <summary>A codec's display label: "H.264", "HEVC", an unknown one in capitals, a dash for none.</summary>
    public static string CodecLabel(string codec) => AdminActivityPresentation.FormatCodec(codec) ?? Dash;

    // ── The matrix ───────────────────────────────────────────

    /// <summary>The colour a matrix cell takes from its direct-play rate: 85% and up good, 50% and up fair, below that poor.</summary>
    public enum CellTone { Good, Fair, Poor }

    public static CellTone ToneOf(double directRate) => directRate >= 0.85 ? CellTone.Good : directRate >= 0.5 ? CellTone.Fair : CellTone.Poor;

    /// <summary>One codec column of a client's row: null when the client had no session with that codec.</summary>
    public sealed record MatrixCell(string Codec, string Percent, string Sessions, CellTone Tone, string Tooltip, AdminRouteRequest History);

    public sealed record MatrixRow(string Client, string Platform, bool IsJellyfin, IReadOnlyList<MatrixCell?> Cells, string Total, string TotalSessions);

    public sealed record Matrix(IReadOnlyList<string> CodecLabels, IReadOnlyList<MatrixRow> Rows);

    /// <summary>Null when there is nothing to draw (no client reached the floor, or no codec): the card says "Not enough sessions yet".</summary>
    public static Matrix? BuildMatrix(AdminInsightsDevices data, AdminInsightsFilterState filters)
    {
        if (data.Matrix.Count == 0 || data.Codecs.Count == 0) return null;
        var rows = data.Matrix.Select(row =>
        {
            var byCodec = new Dictionary<string, AdminInsightsClientCodecCell>(StringComparer.Ordinal);
            foreach (var cell in row.Cells) byCodec[cell.SourceVideoCodec] = cell;
            var client = ClientLabel(row.ClientName);
            var cells = data.Codecs.Select(codec =>
            {
                if (!byCodec.TryGetValue(codec, out var cell)) return null;
                var label = CodecLabel(codec);
                var rate = AdminInsightsRules.FormatRatePercentNumber(cell.DirectRate);
                return new MatrixCell(codec, AdminInsightsText.T("web.admin.insights.percent", ("value", rate)), AdminInsightsNumbers.Count(cell.Sessions), ToneOf(cell.DirectRate),
                    AdminInsightsText.T("web.admin.insights.devices.matrix.cellTooltip", ("client", client), ("codec", label), ("direct_rate", rate), ("sessions", Sessions(cell.Sessions)),
                        ("transcodes", AdminInsightsNumbers.Count(cell.TranscodeSessions)), ("transcode_count", cell.TranscodeSessions)),
                    Drilldown(data, filters, new AdminInsightsHistoryDrilldown
                    {
                        ClientName = OrUnknown(row.ClientName), DevicePlatform = OrUnknown(row.DevicePlatform), SourceVideoCodec = codec,
                    }));
            }).ToArray();
            return new MatrixRow(client, row.DevicePlatform, row.IsJellyfinClient, cells, Percent(row.DirectRate), AdminInsightsNumbers.Count(row.TotalSessions));
        }).ToArray();
        return new Matrix(data.Codecs.Select(CodecLabel).ToArray(), rows);
    }

    // ── Reasons, clients, hardware and resolution lists ──────

    /// <summary>The server's label for an adaptation reason code, or the code itself when this build does not know it.</summary>
    public static string ReasonLabel(string code)
        => AdminActivityReasons.Get(new AdminActivitySession { AdaptationReasons = [code] }).FirstOrDefault()?.Label ?? code;

    /// <summary>Why streams were not direct played, with each reason's share of the adapted sessions.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> Reasons(AdminInsightsDevices data, AdminInsightsFilterState filters)
        => data.AdaptationReasons.Select(reason => new AdminInsightsBarRow(reason.Reason, ReasonLabel(reason.Reason), reason.Sessions, Percent(reason.ShareOfAdapted),
            History: Drilldown(data, filters, new AdminInsightsHistoryDrilldown { AdaptationReason = reason.Reason }))).ToArray();

    /// <summary>Ranked on the server without the matrix's floor, so a client with a few sessions still shows when it transcodes.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> TopTranscoders(AdminInsightsDevices data, AdminInsightsFilterState filters)
        => data.TopTranscoders.Select(row =>
        {
            var client = ClientLabel(row.ClientName);
            return new AdminInsightsBarRow(row.ClientName + ":" + row.DevicePlatform,
                string.IsNullOrEmpty(row.DevicePlatform) ? client : client + " · " + row.DevicePlatform, row.TranscodeSessions,
                AdminInsightsText.T("web.admin.insights.devices.topTranscoders.transcodeShare", ("transcodes", AdminInsightsNumbers.Count(row.TranscodeSessions)),
                    ("total", AdminInsightsNumbers.Count(row.TotalSessions)), ("total_count", row.TotalSessions)),
                History: Drilldown(data, filters, new AdminInsightsHistoryDrilldown
                {
                    ClientName = OrUnknown(row.ClientName), DevicePlatform = OrUnknown(row.DevicePlatform), PlayMethod = "transcode",
                }));
        }).ToArray();

    public static IReadOnlyList<AdminInsightsBarRow> HardwareAcceleration(AdminInsightsDevices data)
        => data.HwAccelBreakdown.Select(entry => new AdminInsightsBarRow(entry.Key, AdminInsightsRules.HwAccelLabel(entry.Key), entry.Sessions)).ToArray();

    public static IReadOnlyList<AdminInsightsBarRow> BurnInCodecs(AdminInsightsDevices data)
        => data.SubtitleBurnInCodecs.Select(entry => new AdminInsightsBarRow(entry.Key, CodecLabel(entry.Key), entry.Sessions)).ToArray();

    /// <summary>"Open History" beside the burn-in card: every burned-in session of the window. Shown only when the list has rows.</summary>
    public static AdminRouteRequest? BurnInHistory(AdminInsightsDevices data, AdminInsightsFilterState filters)
        => data.SubtitleBurnInCodecs.Count == 0 ? null : Drilldown(data, filters, new AdminInsightsHistoryDrilldown { SubtitleBurnIn = true });

    /// <summary>"4K", "1080p", "SD", "8K"; anything else is unknown.</summary>
    public static string ResolutionLabel(string key) => key switch
    {
        "4320p" => AdminInsightsText.T("web.admin.insights.devices.resolution.fuhd"),
        "2160p" => AdminInsightsText.T("web.admin.insights.devices.resolution.uhd"),
        "480p" => AdminInsightsText.T("web.admin.insights.devices.resolution.sd"),
        "1080p" or "720p" => key,
        _ => AdminInsightsText.T("web.admin.insights.devices.resolution.unknown"),
    };

    public static IReadOnlyList<AdminInsightsBarRow> Resolutions(IReadOnlyList<AdminInsightsLabeledCount> entries)
        => entries.Select(entry => new AdminInsightsBarRow(entry.Key, ResolutionLabel(entry.Key), entry.Sessions)).ToArray();

    /// <summary>Sessions delivered below their source: "4K → 1080p".</summary>
    public static IReadOnlyList<AdminInsightsBarRow> Downscales(AdminInsightsDevices data)
        => data.ResolutionChanges.Select(change => new AdminInsightsBarRow(change.From + ":" + change.To,
            AdminInsightsText.T("web.admin.insights.devices.resolution.change", ("from", ResolutionLabel(change.From)), ("to", ResolutionLabel(change.To))), change.Sessions)).ToArray();

    // ── App and track breakdowns ─────────────────────────────

    /// <summary>
    /// Silo apps against Jellyfin apps per bucket as stacked columns on the bucket grid (quiet buckets are zero columns), or null when no
    /// session was recorded.
    /// </summary>
    public static DashboardInsightsColumns? ProtocolColumns(AdminInsightsDevices data)
    {
        if (!data.ProtocolBuckets.Any(bucket => bucket.NativeSessions + bucket.JellyfinSessions > 0)) return null;
        var byDate = new Dictionary<string, AdminInsightsProtocolBucket>(StringComparer.Ordinal);
        foreach (var bucket in data.ProtocolBuckets) byDate[bucket.LabelDate] = bucket;
        var dates = AdminInsightsRules.ChartDates(data.From, data.To, data.Tz, data.BucketUnit, data.ProtocolBuckets.Select(bucket => bucket.LabelDate));
        var columns = dates.Select(date => (Time: DashboardInsightsPresentation.BucketTime(date), Bucket: byDate.GetValueOrDefault(date)))
            .Where(item => item.Time != null)
            .Select(item => new DashboardChartColumn(item.Time!.Value, [item.Bucket?.NativeSessions ?? 0, item.Bucket?.JellyfinSessions ?? 0])).ToArray();
        return columns.Length == 0 ? null : new DashboardInsightsColumns(columns,
            [AdminInsightsText.T("web.admin.insights.devices.protocol.silo"), AdminInsightsText.T("web.admin.insights.devices.protocol.jellyfin")]);
    }

    public sealed record VersionRow(string Client, string Platform, bool IsJellyfin, string Version, string Sessions, string LastSeen);

    public static IReadOnlyList<VersionRow> Versions(AdminInsightsDevices data)
        => data.ClientVersions.Select(row => new VersionRow(ClientLabel(row.ClientName), row.DevicePlatform, row.IsJellyfinClient, row.ClientVersion,
            Sessions(row.Sessions), AdminInsightsRules.FormatShortCalendarDate(row.LastSeenAt))).ToArray();

    private static readonly AdminInsightsSubtitleUsage NoSubtitles = new();
    private static readonly AdminInsightsAudioUsage NoAudio = new();

    /// <summary>A language's name, "Unknown" for an untagged one, and a code the catalog does not know as stored.</summary>
    public static string LanguageLabel(string key)
        => key == "unknown" ? AppStrings.Get("web.languageName.unknown", fallback: "Unknown")
            : AdminSubtitlesText.LanguageName(key) is { Length: > 0 } name ? name : key;

    private static IReadOnlyList<AdminInsightsBarRow> LanguageRows(IReadOnlyList<AdminInsightsLabeledCount> counts, long total)
        => counts.Select(count => new AdminInsightsBarRow(count.Key, LanguageLabel(count.Key), count.Sessions,
            Percent(total > 0 ? (double)count.Sessions / total : 0))).ToArray();

    /// <summary>Rows for keys with catalog labels; a key this build does not know reads as unknown.</summary>
    private static IReadOnlyList<AdminInsightsBarRow> LabeledRows(IReadOnlyList<AdminInsightsLabeledCount> counts, IReadOnlyDictionary<string, string> labels)
        => counts.Select(count => new AdminInsightsBarRow(count.Key,
            labels.TryGetValue(count.Key, out var key) ? AdminInsightsText.T(key) : AdminInsightsText.T("web.admin.insights.devices.hwAccel.unknown"), count.Sessions)).ToArray();

    private static readonly IReadOnlyDictionary<string, string> DeliveryLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["embedded"] = "web.admin.insights.devices.subtitles.delivery.embedded",
        ["sidecar"] = "web.admin.insights.devices.subtitles.delivery.sidecar",
        ["hls_rendition"] = "web.admin.insights.devices.subtitles.delivery.hlsRendition",
        ["burn_in"] = "web.admin.insights.devices.subtitles.delivery.burnIn",
    };

    private static readonly IReadOnlyDictionary<string, string> DecisionLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["direct"] = "web.admin.insights.devices.audio.decision.direct",
        ["remux"] = "web.admin.insights.devices.audio.decision.remux",
        ["transcode"] = "web.admin.insights.devices.audio.decision.transcode",
    };

    private static readonly IReadOnlyDictionary<string, string> ChannelLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mono"] = "web.admin.insights.devices.audio.channels.mono",
        ["stereo"] = "web.admin.insights.devices.audio.channels.stereo",
        ["surround"] = "web.admin.insights.devices.audio.channels.surround",
    };

    public sealed record SubtitleSection(string Subtitle, IReadOnlyList<AdminInsightsBarRow> Languages, IReadOnlyList<AdminInsightsBarRow> Deliveries, string? Flags);

    /// <summary>Subtitles cover the sessions that recorded their route facts; with none, the card says so instead of a share.</summary>
    public static SubtitleSection Subtitles(AdminInsightsDevices data)
    {
        var usage = data.Subtitles ?? NoSubtitles;
        var subtitle = usage.Sessions > 0
            ? AdminInsightsText.T("web.admin.insights.devices.subtitles.summary", ("percent", AdminInsightsRules.FormatRatePercentNumber((double)usage.SubtitledSessions / usage.Sessions)),
                ("subtitled", AdminInsightsNumbers.Count(usage.SubtitledSessions)), ("total", AdminInsightsNumbers.Count(usage.Sessions)), ("total_count", usage.Sessions))
            : AdminInsightsText.T("web.admin.insights.devices.subtitles.noFacts");
        var flags = usage.ForcedSessions + usage.HearingImpairedSessions > 0
            ? AdminInsightsText.T("web.admin.insights.devices.subtitles.flags", ("forced", AdminInsightsNumbers.Count(usage.ForcedSessions)),
                ("hearing_impaired", AdminInsightsNumbers.Count(usage.HearingImpairedSessions)))
            : null;
        return new SubtitleSection(subtitle, LanguageRows(usage.Languages, usage.SubtitledSessions), LabeledRows(usage.Deliveries, DeliveryLabels), flags);
    }

    public sealed record AudioSection(string Subtitle, IReadOnlyList<AdminInsightsBarRow> Languages, IReadOnlyList<AdminInsightsBarRow> Decisions, IReadOnlyList<AdminInsightsBarRow> Channels);

    public static AudioSection Audio(AdminInsightsDevices data)
    {
        var usage = data.Audio ?? NoAudio;
        return new AudioSection(
            usage.Sessions > 0 ? AdminInsightsText.T("web.admin.insights.devices.audio.subtitle") : AdminInsightsText.T("web.admin.insights.devices.subtitles.noFacts"),
            LanguageRows(usage.Languages, usage.Sessions), LabeledRows(usage.Decisions, DecisionLabels), LabeledRows(usage.DeliveredChannels, ChannelLabels));
    }
}
