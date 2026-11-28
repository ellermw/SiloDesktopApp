using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminPlaybackHistoryPresentation;
using A = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Tests;

public sealed class AdminPlaybackHistoryPresentationTests
{
    private static AdminPlaybackHistoryRow Row() => new(
        "sess-12345678", "7", "alice", "p-alice", "Alice", "episode-1", "42", "Dune", "episode", "transcode",
        "2026-11-20T20:00:00Z", "2026-11-20T21:00:00Z", 3400, 9000, false, 3200, "Synthetic Series", 2, 4)
    {
        PausedSeconds = 120, ClientIp = "203.0.113.42", ClientName = "Chrome", ClientVersion = "131", DevicePlatform = "web",
        VideoDecision = "transcode", AudioDecision = "transcode", EndReason = "stopped", IsJellyfinClient = true,
        Subtitle = new() { Language = "en", Codec = "hdmv_pgs_subtitle", Delivery = "burn_in" },
        TargetVideoCodec = "h264", TargetResolution = "1080p", TargetAudioCodec = "aac", TargetAudioChannels = 2,
        OutputContainer = "fmp4", OutputProtocol = "hls", TargetBitrateKbps = 8000, TranscodeHwAccel = "vaapi",
        ToneMapMode = "hardware", AdaptationReasons = ["video_codec_unsupported", "subtitle_burn_in"],
        ReportingNode = "api-1", StreamLocation = "remote", RoutingExecution = "local", RoutingEgress = "proxy",
        RoutingEgressNodeName = "edge-eu-1", RoutingEgressNodeId = "3", RouteFactsRecorded = true,
        SourceVideoCodec = "hevc", SourceResolution = "2160p", SourceAudioCodec = "truehd", SourceAudioChannels = 8,
        SourceContainer = "mkv", SourceBitrateKbps = 42000, SourceHdr = true, SourceVideoRangeType = "HDR10", LibraryName = "Movies",
    };

    [Fact]
    public void RowFormattingMatchesTheOfficialHistoryFormatRules()
    {
        var row = Row();
        Assert.Equal(("Synthetic Series", "S02E04 · Dune"), P.RowTitle(row));
        Assert.Equal("Synthetic Series · S02E04 · Dune", P.RowLabel(row));
        Assert.Equal(("Synthetic Series", "S02E04"), P.RowTitle(row with { MediaTitle = row.SeriesTitle }));
        Assert.Equal(("Dune", ""), P.RowTitle(row with { SeriesTitle = "" }));
        Assert.Equal(("episode-1", ""), P.RowTitle(row with { SeriesTitle = "", MediaTitle = "" }));
        Assert.Equal("episode · session sess-123", P.Meta(row));
        Assert.Equal((3200d, false), P.WatchTime(row));
        Assert.Equal((3600d, true), P.WatchTime(row with { PlayedSeconds = null, WatchedSeconds = 7200 }));
        Assert.Equal((0d, true), P.WatchTime(row with { PlayedSeconds = null, EndedAt = "2026-11-20T19:00:00Z" }));
        Assert.Equal((3400d, true), P.WatchTime(row with { PlayedSeconds = null, EndedAt = "invalid" }));
        Assert.Equal("1h 0m", P.FormatDuration(3600.9));
        Assert.Equal("1m 2s", P.FormatDuration(62.9));
        Assert.Equal("9s", P.FormatDuration(9.9));
        Assert.All(new double?[] { null, 0, -1, double.NaN, double.PositiveInfinity }, duration => Assert.Equal("0m", P.FormatDuration(duration)));
    }

    [Fact]
    public void DrilldownChipsHaveOfficialLabelsAndRemoveOnlyTheirOwnKeys()
    {
        var filter = new AdminPlaybackHistoryFilter(MediaItemId: "episode-1", SeriesId: "series-1",
            ClientName: "Chrome", DevicePlatform: "Windows", SourceVideoCodec: "h264", AdaptationReason: "server-new-code",
            StartedAfter: "invalid-from", StartedBefore: "invalid-to", Search: "dune", SessionId: "sess-12345678");
        var chips = P.Chips(filter, [Row()]);
        Assert.Equal(["title", "client_name", "device_platform", "source_video_codec", "adaptation_reason", "date_range"],
            chips.Select(chip => chip.Key));
        Assert.Equal(["Synthetic Series · S02E04 · Dune", "Client: Chrome", "Platform: Windows", "Codec: H.264",
            "Reason: server-new-code", "Date: invalid-from – invalid-to"], chips.Select(chip => chip.Label));
        Assert.Equal(["media_item_id", "series_id"], chips[0].RemoveKeys);
        Assert.Equal(["started_after", "started_before"], chips[^1].RemoveKeys);
        Assert.Equal("episode-1", chips[0].ItemId);
        Assert.Equal("Synthetic Series", P.Chips(filter with { MediaItemId = null }, [Row()])[0].Label);
        Assert.Equal("episode-1", P.Chips(filter, [])[0].Label);
        Assert.Equal("From invalid-from", P.Chips(filter with { StartedBefore = null }, [])[5].Label);
        Assert.Equal("Until invalid-to", P.Chips(filter with { StartedAfter = null }, [])[5].Label);
        Assert.Equal("Reason: " + AdminActivityReasons.FromCodes(["subtitle_burn_in"])[0].Label,
            P.Chips(new(AdaptationReason: "subtitle_burn_in"), [])[0].Label);
    }

    [Fact]
    public void AHistoryTranscodeMapsToTheLiveActivityFormatters()
    {
        var row = Row();
        var session = P.AsActivitySession(row);
        Assert.Equal(row.SessionId, session.SessionId);
        Assert.Equal(row.MediaFileId, session.RequestedMediaFileId);
        Assert.Equal(row.WatchedSeconds, session.PositionSeconds);
        Assert.Equal(row.EndedAt, session.UpdatedAt);
        Assert.True(session.TranscodeAudio);
        Assert.False(session.HasPlaybackControl);
        Assert.Null(session.StreamBitrateKbps);
        Assert.True(A.IsJellyfinSession(session));
        Assert.Equal("HEVC · 2160p", A.FormatVideoSummary(session));
        Assert.Equal("H.264 · 1080p", A.FormatDeliveredVideoSummary(session));
        Assert.Equal("TrueHD 7.1", A.FormatAudioSummary(session));
        Assert.Equal("AAC 2.0", A.FormatDeliveredAudioSummary(session));
        Assert.Equal("MKV", A.FormatSourceContainerSummary(session));
        Assert.Contains("fMP4", A.FormatDeliveredContainerSummary(session));
        Assert.Contains("VAAPI", A.FormatTranscodeModeSummary(session));
        Assert.NotNull(A.FormatToneMapSummary(session));
        Assert.Equal(row.AdaptationReasons, AdminActivityReasons.Get(session).Select(reason => reason.Code));
        Assert.Equal("edge-eu-1", Assert.Single(P.RouteNodes(row, session)).Name);
        Assert.Empty(P.RouteNodes(row with { RouteFactsRecorded = false }, session));
        Assert.Empty(P.RouteNodes(row with { RoutingExecution = null, RoutingEgress = null, ReportingNode = null }, session));
        Assert.StartsWith("limit ", P.Bitrates(row).Delivered);
        Assert.Equal("—", P.Bitrates(row with { TargetBitrateKbps = null }).Delivered);
        Assert.Equal(("EN · HDMV_PGS_SUBTITLE", "Burned in"), P.Subtitles(row));
        Assert.Equal("Streamed (HLS)", P.Subtitles(row with { Subtitle = new() { Delivery = "hls_rendition" } }).Delivered);
        Assert.Equal("Stopped by admin", P.EndReason("terminated"));
        Assert.Equal("future", P.EndReason("future"));
        Assert.Equal("—", P.EndReason(null));
    }

    [Fact]
    public void DirectPlayKeepsTheSourceAndShowsNoSubtitleOrActiveTranscode()
    {
        var row = Row() with { PlayMethod = "direct", VideoDecision = null, AudioDecision = null, Subtitle = null };
        var session = P.AsActivitySession(row);
        Assert.False(session.TranscodeAudio);
        Assert.Equal(A.FormatVideoSummary(session), A.FormatDeliveredVideoSummary(session));
        Assert.Equal(A.FormatAudioSummary(session), A.FormatDeliveredAudioSummary(session));
        Assert.Equal(A.FormatSourceContainerSummary(session), A.FormatDeliveredContainerSummary(session));
        Assert.Equal(P.Bitrates(row).Source, P.Bitrates(row).Delivered);
        Assert.Equal(("None", "None"), P.Subtitles(row));
        Assert.Null(A.FormatTranscodeModeSummary(session));
        Assert.Null(A.FormatToneMapSummary(session));
    }
}
