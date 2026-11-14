using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Tests;

/// <summary>Ported from upstream web/src/pages/adminActivityPresentation.test.ts (1a99f7be).</summary>
public sealed class AdminActivityPresentationTests
{
    internal static AdminActivitySession Session(Action<AdminActivitySession>? configure = null)
    {
        var session = new AdminActivitySession
        {
            SessionId = "s1", UserId = "7", Username = "alex", ProfileId = "p1", MediaFileId = "44", RequestedMediaFileId = "44",
            MediaTitle = "Dune", MediaType = "movie", PlayMethod = "transcode", ReportingNode = "local",
            StartedAt = "2026-11-14T10:00:00Z", UpdatedAt = "2026-11-14T10:00:00Z",
        };
        configure?.Invoke(session);
        return session;
    }

    [Fact]
    public void DirectStreamKeepsCopiedVideoAndReportsHlsOutput()
    {
        var s = Session(x => { x.SourceContainer = "mkv"; x.OutputContainer = "fmp4"; x.OutputProtocol = "hls"; x.VideoDecision = "copy"; x.AudioDecision = "transcode"; });
        Assert.Equal("fMP4 (HLS)", P.FormatDeliveredContainerSummary(s));
        Assert.Equal("MKV → fMP4 (HLS)", P.FormatContainerDetail(s));
        Assert.Equal("direct_stream", P.ClassifyActivityMethod(s));
    }

    [Fact]
    public void CopyStaysExplicitWithoutChangingRemuxAggregation()
    {
        Assert.Equal("Copy", P.FormatDecisionLabel("copy"));
        Assert.Equal(P.MethodMeta("remux").Tone, P.DecisionTone("copy"));
        Assert.Equal("copy", P.NormalizeStreamDecision("remux"));
        var copied = Session(x => { x.PlayMethod = "remux"; x.VideoDecision = "copy"; x.AudioDecision = "copy"; });
        Assert.Equal("remux", P.ClassifyActivityMethod(copied));
        Assert.Equal("copy", P.FormatPlaybackDecisionSummary(copied));
        Assert.Equal("Copied without re-encoding", P.FormatAudioDetail(copied));
    }

    [Fact]
    public void TranscodeSummariesNeverInferAnOutputContainer()
    {
        var s = Session(x => { x.SourceContainer = "mkv"; x.TargetVideoCodec = "h264"; x.TargetResolution = "1080p"; x.TargetAudioCodec = "aac"; x.TargetAudioChannels = 6; });
        Assert.Equal("hls", P.NormalizeContainerDecision(s.PlayMethod));
        Assert.Equal("MKV", P.FormatSourceContainerSummary(s));
        Assert.Equal("Unknown output container", P.FormatDeliveredContainerSummary(s));
        Assert.Equal("Repackaged for streaming; output container not reported", P.FormatContainerDetail(s));
        Assert.Equal("H.264 · 1080p", P.FormatDeliveredVideoSummary(s));
        Assert.Equal("AAC 5.1", P.FormatDeliveredAudioSummary(s));
        Assert.Equal("HW/SW unknown", P.FormatTranscodeModeSummary(s));
    }

    [Fact]
    public void FullResolutionTranscodeKeepsTheSourceSizeClass()
    {
        var full = Session(x => { x.SourceVideoCodec = "hevc"; x.SourceVideoResolution = "2160p"; x.SourceVideoHeight = 1600; x.TargetVideoCodec = "hevc"; x.TargetResolution = "1600p"; });
        Assert.Equal("HEVC · 2160p", P.FormatVideoSummary(full));
        Assert.Equal("HEVC · 2160p", P.FormatDeliveredVideoSummary(full));
        Assert.Equal("Output → HEVC · 2160p", P.FormatVideoDetail(full));
        full.TargetResolution = "1080p";
        Assert.Equal("Output → HEVC · 1080p", P.FormatVideoDetail(full));
    }

    [Fact]
    public void AutoSwitchedSourcesAreExplained()
    {
        var s = Session(x => { x.RequestedMediaFileId = "41"; x.MediaFileId = "42"; x.RequestedVideoCodec = "hevc"; x.RequestedVideoResolution = "2160p"; x.TargetVideoCodec = "h264"; x.TargetResolution = "1080p"; });
        Assert.Equal("Auto-switched from HEVC · 2160p · Output → H.264 · 1080p", P.FormatVideoDetail(s));
    }

    [Theory]
    [InlineData("qsv", "HW QSV")]
    [InlineData("vaapi", "HW VAAPI")]
    [InlineData("nvenc", "HW NVENC")]
    [InlineData("videotoolbox", "HW VideoToolbox")]
    [InlineData("none", "SW")]
    [InlineData("auto", "HW/SW pending")]
    public void TranscodeModes(string accel, string expected) =>
        Assert.Equal(expected, P.FormatTranscodeModeSummary(Session(x => x.TranscodeHwAccel = accel)));

    [Fact]
    public void AudioOnlyTranscodeNamesTheWorkNotAnAccelerationMode()
    {
        var s = Session(x => { x.VideoDecision = "copy"; x.AudioDecision = "transcode"; x.PlayMethod = "remux"; });
        Assert.Equal("Audio Transcode", P.FormatTranscodeModeSummary(s));
    }

    [Fact]
    public void TranscodeAudioUsesTargetChannelsNeverTheSource()
    {
        var downmixed = Session(x => { x.SourceAudioCodec = "truehd"; x.SourceAudioChannels = 8; x.TargetAudioCodec = "aac"; x.TargetAudioChannels = 6; });
        Assert.Equal("AAC 5.1", P.FormatDeliveredAudioSummary(downmixed));
        Assert.Equal("→ AAC 5.1", P.FormatAudioDetail(downmixed));
        var unknown = Session(x => { x.SourceAudioCodec = "truehd"; x.SourceAudioChannels = 8; x.TargetAudioCodec = "aac"; });
        Assert.Equal("AAC", P.FormatDeliveredAudioSummary(unknown));
        Assert.Equal("TrueHD 7.1", P.FormatAudioSummary(unknown));
    }

    [Fact]
    public void ToneMappingOnlyReportsConfirmedExecutors()
    {
        Assert.Equal("HW Tone map", P.FormatToneMapSummary(Session(x => x.ToneMapMode = "hardware"))!.Badge);
        Assert.Equal("Software", P.FormatToneMapSummary(Session(x => x.ToneMapMode = "software"))!.Detail);
        Assert.Null(P.FormatToneMapSummary(Session()));
        Assert.Null(P.FormatToneMapSummary(Session(x => x.ToneMapMode = "future-mode")));
        Assert.Null(P.FormatToneMapSummary(Session(x => { x.ToneMapMode = "hardware"; x.PlayMethod = "direct"; })));
    }

    [Theory]
    [InlineData("direct", "direct", "direct", "direct")]
    [InlineData("transcode", "transcode", "copy", "transcode")]
    [InlineData("remux", "copy", "transcode", "direct_stream")]
    [InlineData("remux", "copy", "copy", "remux")]
    [InlineData("", "", "", "unknown")]
    public void BucketsByPerStreamDecisions(string playMethod, string video, string audio, string expected) =>
        Assert.Equal(expected, P.ClassifyActivityMethod(Session(x => { x.PlayMethod = playMethod; x.VideoDecision = video; x.AudioDecision = audio; })));

    [Fact]
    public void ServerEffectiveMethodWinsAndUnknownStreamsStayUnknown()
    {
        Assert.Equal("remux", P.ClassifyActivityMethod(Session(x => { x.EffectivePlayMethod = "remux"; x.VideoDecision = "transcode"; })));
        Assert.Equal("unknown", P.ClassifyActivityMethod(Session(x => { x.PlayMethod = "mystery"; x.TranscodeAudio = true; })));
    }

    [Fact]
    public void MethodOrderEscalatesByServerWork()
    {
        var sorted = new[] { "unknown", "transcode", "direct", "direct_stream", "remux" }.OrderBy(m => m, Comparer<string>.Create(P.CompareActivityMethods));
        Assert.Equal(["direct", "remux", "direct_stream", "transcode", "unknown"], sorted);
        Assert.Equal(ActivityTone.Success, P.MethodMeta("direct").Tone);
        Assert.Equal(ActivityTone.Info, P.MethodMeta("remux").Tone);
        Assert.Equal(ActivityTone.Warning, P.MethodMeta("direct_stream").Tone);
        Assert.Equal(ActivityTone.Destructive, P.MethodMeta("transcode").Tone);
        Assert.Equal("Unknown", P.MethodMeta("whatever").Label);
    }

    [Fact]
    public void RouteNodesFollowWorkToViewerOrder()
    {
        var routed = Session(x => { x.RoutingWorkload = "video_transcode"; x.RoutingExecution = "transcode"; x.RoutingExecutionNodeId = "3"; x.RoutingExecutionNodeName = "gpu-1"; x.RoutingEgress = "proxy"; x.RoutingEgressNodeId = "4"; });
        var nodes = P.GetSessionRouteNodes(routed);
        Assert.Equal(["transcode:3", "proxy:4"], nodes.Select(n => n.Key));
        Assert.Equal("gpu-1", nodes[0].Name);
        Assert.Equal("Node #4", nodes[1].Name);

        var api = Session(x => { x.RoutingExecution = "api"; x.RoutingEgress = "api"; x.ReportingNode = "edge-a"; });
        Assert.Equal(["server:edge-a"], P.GetSessionRouteNodes(api).Select(n => n.Key));

        var legacy = Session(x => x.ReportingNode = "local");
        var node = Assert.Single(P.GetSessionRouteNodes(legacy));
        Assert.Equal(("legacy:local", "Local server"), (node.Key, node.Name));
    }

    [Fact]
    public void ClientLabelsFallBackFromFullToCompactToNameVersion()
    {
        Assert.Equal("Silo iOS 2.1 (45)", P.GetSessionClientLabelFull(Session(x => { x.ClientLabel = "Silo iOS 2.1"; x.ClientLabelFull = "Silo iOS 2.1 (45)"; })));
        Assert.Equal("Silo iOS 2.1", P.GetSessionClientLabelFull(Session(x => x.ClientLabel = "Silo iOS 2.1")));
        Assert.Equal("Silo iOS 2.1", P.GetSessionClientLabel(Session(x => { x.ClientName = "Silo iOS"; x.ClientVersion = "2.1"; })));
        Assert.Equal("Silo iOS", P.GetSessionClientLabelFull(Session(x => x.ClientName = "Silo iOS")));
        Assert.Equal("", P.GetSessionClientLabelFull(Session()));
    }

    [Fact]
    public void TitlesClocksAndBitrates()
    {
        var episode = Session(x => { x.SeriesName = "Severance"; x.SeasonNumber = 2; x.EpisodeNumber = 1; x.EpisodeName = ""; });
        Assert.Equal("S2E1", P.GetPlaybackSessionTitle(episode));
        Assert.Equal("S2E1 · Severance", P.GetPlaybackSessionSubtitle(episode));
        Assert.Equal("File #44", P.GetPlaybackSessionTitle(Session(x => x.MediaTitle = "")));
        Assert.Equal("Movie", P.GetPlaybackSessionSubtitle(Session()));
        Assert.Equal("1:02:03 / 2:00:00", P.FormatPlaybackPosition(Session(x => { x.PositionSeconds = 3723.9; x.FileDuration = 7200; })));
        Assert.Equal("0:05", P.FormatPlaybackPosition(Session(x => { x.PositionSeconds = 5; x.FileDuration = null; })));
        Assert.Equal("8.0 Mbps", P.FormatSessionBitrate(8000));
        Assert.Equal("640 kbps", P.FormatSessionBitrate(640));
        Assert.Null(P.FormatSessionBitrate(0));
        Assert.Equal("1:01", P.GetElapsed("2026-11-14T10:00:00Z", DateTimeOffset.Parse("2026-11-14T10:01:01Z")));
    }

    [Fact]
    public void FfmpegRowsPreferLineThenEventThenMessage()
    {
        var json = System.Text.Json.JsonDocument.Parse("""{"ffmpeg_event":"restart","restart_count":2,"hw_accel":""}""").RootElement;
        var attrs = json.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var row = new AdminActivityLogRow { Id = "1", Message = "ffmpeg stderr", Attrs = attrs };
        Assert.Equal("restart", P.FfmpegRowText(row));
        Assert.Equal("stderr", P.FfmpegRowKind(row));
        Assert.Equal("2", P.LogAttr(row, "restart_count"));
        Assert.Equal("-", P.LogAttr(row, "hw_accel"));
        Assert.Equal("plain", P.FfmpegRowText(new AdminActivityLogRow { Message = "plain" }));
    }

    [Fact]
    public void LogsRouteMirrorsTheWebUiPlaybackFocusUrl()
    {
        var route = P.LogsRoute("sess-1", ffmpeg: true);
        Assert.Equal(AdminRoute.ServerLog, route.Route);
        Assert.Equal("sess-1", route.Get("playback_session_id"));
        Assert.Equal("playback", route.Get("focus"));
        Assert.Equal("ffmpeg", route.Get("component"));
        Assert.Null(P.LogsRoute("sess-1", ffmpeg: false).Get("component"));
    }
}
