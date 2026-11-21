using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorCorrectionsTests
{
    [Fact]
    public void RequestedSourceSwitchAndProgressUseCurrentSessionFacts()
    {
        var session = new AdminActivitySession { RequestedMediaFileId = "10", MediaFileId = "11", RequestedVideoCodec = "hevc", RequestedVideoResolution = "2160p", FileDuration = 100, PositionSeconds = 125 };
        Assert.Equal("Auto-switched from HEVC · 2160p", P.FormatRequestedSourceSwitch(session));
        Assert.Equal(1d, P.GetPlaybackProgress(session));
        session.PositionSeconds = -4;
        Assert.Equal(0d, P.GetPlaybackProgress(session));
        session.FileDuration = null;
        Assert.Null(P.GetPlaybackProgress(session));
        session.RequestedMediaFileId = session.MediaFileId;
        Assert.Null(P.FormatRequestedSourceSwitch(session));
    }

    [Fact]
    public async Task ConcurrentPeakMinuteSplitSurvivesTheRealDashboardTransport()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"resolution_seconds":60,"points":[{"t":"2026-11-17T10:00:00Z","streams":6,"direct":8,"remux":5,"transcode":4,"streams_by_method":{"direct":3,"remux":2,"transcode":1}}]}"""));
        var series = await new AdminMonitorApi(kit.Client).GetDashboardTimeseriesAsync(lease, 24);
        Assert.Equal(6, Assert.Single(series.Points!).Streams);
        Assert.Equal(3, series.Points![0].StreamsByMethod!["direct"]);
        Assert.Equal([3d, 2d, 1d], DashboardChartMath.ConcurrentMethodBands(series).Select(b => Assert.Single(b.Points).Value!.Value));
    }

    [Fact]
    public void ConcurrentViewRejectsUnknownValuesAndDoesNotStampItsDefault()
    {
        var entries = DashboardLayoutDocument.Sanitize("""{"version":1,"entries":[{"id":"concurrent-streams-24h","view":"future-view"}]}""")!;
        Assert.Null(Assert.Single(entries).View);
        Assert.Equal("lines", DashboardLayoutDocument.EffectiveView(entries[0]));
        Assert.Same(entries, DashboardLayoutDocument.SetView(entries, "concurrent-streams-24h", "future-view"));
        Assert.Same(entries, DashboardLayoutDocument.SetView(entries, "concurrent-streams-24h", "lines"));
        Assert.DoesNotContain("\"view\"", DashboardLayoutDocument.Serialize(entries));
    }

    [Theory]
    [InlineData("video_codec_unsupported", "Video codec not supported")]
    [InlineData("video_capability_unconfirmed", "Video support unconfirmed")]
    [InlineData("dynamic_range_unsupported", "HDR not supported")]
    [InlineData("resolution_limit", "Above device resolution")]
    [InlineData("bandwidth_limit", "Connection too slow")]
    [InlineData("buffering", "Earlier buffering")]
    [InlineData("bandwidth_cap", "Maximum bitrate setting")]
    [InlineData("server_bitrate_limit", "Access group limit")]
    [InlineData("quality_selected", "Quality chosen by viewer")]
    [InlineData("subtitle_burn_in", "Subtitles burned in")]
    [InlineData("direct_play_failed", "Direct play failed")]
    [InlineData("audio_codec_unsupported", "Audio codec not supported")]
    [InlineData("audio_channels_unsupported", "Too many audio channels")]
    [InlineData("device_workaround", "Device audio workaround")]
    [InlineData("audio_track_selection", "Audio track selection")]
    [InlineData("container_unsupported", "Container not supported")]
    [InlineData("subtitle_unsupported", "Subtitles not supported")]
    public void ClosedServerReasonsUseExactOfficialLabelsAndExplanations(string code, string label)
    {
        var reason = Assert.Single(AdminActivityReasons.Get(new() { AdaptationReasons = [code] }));
        Assert.Equal(code, reason.Code);
        Assert.Equal(label, reason.Label);
        Assert.False(string.IsNullOrWhiteSpace(reason.Description));
        Assert.DoesNotContain("web.admin", reason.Description);
    }

    [Fact]
    public async Task ReasonsSurviveTheRealV2SessionTransportInServerOrderAndUnknownCodesAreOmitted()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var row = AdminActivityPresentationTests.Session(s => s.AdaptationReasons = ["bandwidth_cap", "future_reason", "video_codec_unsupported"]);
        var json = JsonSerializer.Serialize(new { items = new[] { row }, page = new { has_more = false } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(json));
        var session = Assert.Single(await new AdminMonitorApi(kit.Client).ListActivitySessionsAsync(lease));
        Assert.Equal(row.AdaptationReasons, session.AdaptationReasons);
        Assert.Equal(["bandwidth_cap", "video_codec_unsupported"], AdminActivityReasons.Get(session).Select(reason => reason.Code));
        Assert.Empty(AdminActivityReasons.Get(new() { PlayMethod = "transcode" }));
    }

    [Theory]
    [InlineData(8000, 16000, 0.5)]
    [InlineData(18000, 16000, 1.0)]
    [InlineData(0, 16000, null)]
    [InlineData(8000, 0, null)]
    public void BitrateShareUsesMeasuredDeliveryAndSource(long live, long source, double? expected)
    {
        var rates = P.GetSessionBitrate(new() { StreamBitrateKbps = live, SourceBitrateKbps = source, TargetBitrateKbps = 12000 });
        Assert.Equal(expected, rates.Share);
        Assert.Equal(P.FormatSessionBitrate(12000), rates.Limit);
    }

    [Fact]
    public void BitrateSortStartsDescendingAndPreservesEqualRateOrder()
    {
        AdminActivitySession[] rows = [new() { SessionId = "unknown" }, new() { SessionId = "first", StreamBitrateKbps = 8000 }, new() { SessionId = "second", StreamBitrateKbps = 8000 }, new() { SessionId = "small", StreamBitrateKbps = 500 }];
        var state = new AdminActivityListState();
        state.ToggleSort(ActivitySortField.Bitrate);
        Assert.False(state.SortAscending);
        Assert.Equal(["first", "second", "small", "unknown"], state.Apply(rows).Select(s => s.SessionId));
        state.ToggleSort(ActivitySortField.Bitrate);
        Assert.Equal(["unknown", "small", "first", "second"], state.Apply(rows).Select(s => s.SessionId));
    }

    [Fact]
    public void StackedActivityCardsKeepTheMeasuredBitrateReading()
    {
        // Below the table boundary there is no Bitrate column; each collapsed card carries its own rate.
        var rows = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.Rows.cs"));
        var start = rows.IndexOf("private FrameworkElement StackedRow(", StringComparison.Ordinal);
        var end = rows.IndexOf("private FrameworkElement MethodLine(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Contains("BitrateLine(s)", rows[start..end]);
    }

    [Fact]
    public void CachedBrandingBodiesSelectTheSvgDecoderByContent()
    {
        static bool Svg(string body) => SvgArtwork.IsSvgContent(System.Text.Encoding.UTF8.GetBytes(body));
        Assert.True(Svg("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 224 96\"/>"));
        Assert.True(Svg("﻿<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<!-- wordmark -->\n<svg viewBox=\"0 0 224 96\"/>"));
        Assert.True(Svg("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n<svg/>"));
        Assert.False(Svg("<?xml version=\"1.0\"?><plist/>"));
        Assert.False(Svg(""));
        Assert.False(SvgArtwork.IsSvgContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, (byte)'<', (byte)'s', (byte)'v', (byte)'g']));
        // The cache's .img names and extensionless wordmark URLs cannot select the decoder.
        var branding = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.Branding.cs"));
        Assert.Contains("SvgArtwork.IsSvgContent(", branding);
        Assert.Contains("new SvgImageSource()", branding);
    }

    [Fact]
    public void RouteColumnOnlyAppearsForDistinctRoutesOrAnExplicitNetwork()
    {
        var a = new AdminActivitySession { RoutingExecution = "api", RoutingEgress = "api", ReportingNode = "local" };
        var b = new AdminActivitySession { RoutingExecution = "api", RoutingEgress = "api", ReportingNode = "local" };
        Assert.False(P.SessionRoutesVary([]));
        Assert.False(P.SessionRoutesVary([a, b]));
        b.RoutingNetworkProvider = "";
        Assert.False(P.SessionRoutesVary([a, b]));
        b.RoutingNetworkProvider = "Private route";
        Assert.True(P.SessionRoutesVary([a, b]));
        b.RoutingNetworkProvider = null;
        b.RoutingExecution = "transcode"; b.RoutingExecutionNodeId = "2";
        Assert.True(P.SessionRoutesVary([a, b]));
    }

    [Fact]
    public void ConcurrentBandsUseThePeakMinuteSplitRatherThanIndependentPeaks()
    {
        var point = new DashboardTimeseriesPoint { T = DateTimeOffset.UtcNow, Streams = 10, Direct = 9, Remux = 8, Transcode = 7, StreamsByMethod = new() { ["direct"] = 4, ["remux"] = 3, ["transcode"] = 2 } };
        var series = new DashboardTimeseries { ResolutionSeconds = 60, Points = [point] };
        Assert.True(DashboardChartMath.HasMethodSplit(series));
        var bands = DashboardChartMath.ConcurrentMethodBands(series);
        Assert.Equal(["direct", "remux", "transcode", "other"], bands.Select(b => b.Key));
        Assert.Equal([4d, 3d, 2d, 1d], bands.Select(b => Assert.Single(b.Points).Value!.Value));
        Assert.Equal(10d, bands.Sum(b => b.Points[0].Value!.Value));
        Assert.Equal([9d, 8d, 7d], DashboardChartMath.ConcurrentMethodLines(series).Select(line => Assert.Single(line.Points).Value!.Value));
        point.StreamsByMethod["transcode"] = 3;
        Assert.DoesNotContain(DashboardChartMath.ConcurrentMethodBands(series), b => b.Key == "other");
    }

    [Fact]
    public void ConcurrentSplitAvailabilityAndGapsHandleOlderReplicas()
    {
        var time = DateTimeOffset.UtcNow;
        var series = new DashboardTimeseries { ResolutionSeconds = 60, From = time, To = time.AddMinutes(2), Points = [new() { T = time, Streams = 2, StreamsByMethod = new() { ["direct"] = 2 } }, new() { T = time.AddMinutes(2), Streams = 1 }] };
        Assert.False(DashboardChartMath.HasMethodSplit(series));
        series.Points[1].StreamsByMethod = new() { ["transcode"] = 1 };
        Assert.True(DashboardChartMath.HasMethodSplit(series));
        Assert.All(DashboardChartMath.ConcurrentMethodBands(series), band => Assert.Contains(band.Points, point => point.Value == null));
    }

    [Theory]
    [InlineData("lines")]
    [InlineData("stacked")]
    [InlineData("total")]
    public void ConcurrentViewRoundTripsWithThePlacement(string view)
    {
        var json = "{\"version\":1,\"entries\":[{\"id\":\"concurrent-streams-24h\",\"view\":\"" + view + "\"}]}";
        var entries = DashboardLayoutDocument.Sanitize(json)!;
        Assert.Equal(view, DashboardLayoutDocument.EffectiveView(Assert.Single(entries)));
        Assert.Equal(view, DashboardLayoutDocument.EffectiveView(Assert.Single(DashboardLayoutDocument.Sanitize(DashboardLayoutDocument.Serialize(entries))!)));
    }
}
