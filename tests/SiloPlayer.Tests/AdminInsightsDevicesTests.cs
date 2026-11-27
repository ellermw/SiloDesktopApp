using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using D = SiloPlayer.Core.Services.Admin.AdminInsightsDevicesPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// The Devices &amp; Quality tab (official <c>DevicesTab.tsx</c> and <c>DevicesUsageSections.tsx</c> at c59798116): the coverage banner, KPI
/// cards, the colour-coded matrix and its tooltips, reason and label mapping, the usage breakdowns and every History drilldown. Synthetic data only.
/// </summary>
public sealed class AdminInsightsDevicesTests
{
    public AdminInsightsDevicesTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    private static AdminInsightsDevices Devices() => JsonSerializer.Deserialize<AdminInsightsDevices>(AdminInsightsWire.Devices)!;

    private static AdminInsightsFilterState Filters(params (string, string)[] query)
        => AdminInsightsFilterState.Parse(query.ToDictionary(pair => pair.Item1, pair => pair.Item2), "UTC", new DateOnly(2026, 11, 26));

    private static AdminInsightsClientMatrixRow Row(string client, string platform, double rate, params (string Codec, double Rate, long Sessions, long Transcodes)[] cells)
        => new()
        {
            ClientName = client, DevicePlatform = platform, DirectRate = rate, TotalSessions = cells.Sum(cell => cell.Sessions), IsJellyfinClient = client == "Jellyfin Web",
            Cells = cells.Select(cell => new AdminInsightsClientCodecCell { SourceVideoCodec = cell.Codec, DirectRate = cell.Rate, Sessions = cell.Sessions, TranscodeSessions = cell.Transcodes }).ToList(),
        };

    // ── Banner and KPI cards ──────────────────────────────────

    [Fact]
    public void TheCoverageBannerNamesWhatIsMissing()
    {
        var data = Devices();
        Assert.Null(D.Notice(data));
        data.Coverage.RouteFactsSessions = 0;
        Assert.Equal("Detailed playback info isn’t available for these older sessions. New sessions record it automatically.", D.Notice(data));
        data.Coverage.RouteFactsSessions = 5;
        var partial = D.Notice(data)!;
        Assert.StartsWith("Detailed playback info is available for 5 of 12 sessions (tracked since ", partial);
        Assert.Contains(AdminInsightsRules.FormatShortCalendarDate(data.Coverage.OldestRouteFactsAt), partial);
        data.Coverage.TotalSessions = 0;
        Assert.Null(D.Notice(data));
    }

    [Fact]
    public void FourKpiCardsCarryTheOfficialValuesSubtextsAndAccents()
    {
        var kpis = D.Kpis(Devices());
        Assert.Equal(["Direct play rate", "Hardware transcoding", "Subtitle burn-ins", "HDR tone mapping"], kpis.Select(kpi => kpi.Label));
        Assert.Equal("75%", kpis[0].Value);
        Assert.Equal("12 direct · 12 direct stream · 12 transcode", kpis[0].Subtext);
        Assert.Equal(ActivityTone.Success, kpis[0].Accent);
        Assert.Equal("50%", kpis[1].Value);
        Assert.Equal("12 hardware · 12 CPU", kpis[1].Subtext);
        Assert.Equal(ActivityTone.Info, kpis[1].Accent);
        Assert.Equal("12", kpis[2].Value);
        Assert.Equal("100% of transcodes", kpis[2].Subtext);
        Assert.Equal(ActivityTone.Warning, kpis[2].Accent);
        Assert.Equal("12", kpis[3].Value);
        Assert.Equal("12 hardware · 12 CPU", kpis[3].Subtext);
        Assert.Null(kpis[3].Accent);
    }

    [Fact]
    public void WithoutTranscodesHardwareTranscodingIsADashAndBurnInsAreNotWarned()
    {
        var data = Devices();
        data.Summary.HwTranscodeSessions = 0;
        data.Summary.SwTranscodeSessions = 0;
        data.Summary.TranscodeSessions = 0;
        data.Summary.SubtitleBurnInSessions = 0;
        var kpis = D.Kpis(data);
        Assert.Equal("—", kpis[1].Value);
        Assert.Equal("No transcodes", kpis[1].Subtext);
        Assert.Equal("0", kpis[2].Value);
        Assert.Equal("0% of transcodes", kpis[2].Subtext);
        Assert.Null(kpis[2].Accent);
    }

    // ── The matrix ────────────────────────────────────────────

    [Theory]
    [InlineData(1.0, D.CellTone.Good)]
    [InlineData(0.85, D.CellTone.Good)]
    [InlineData(0.8499, D.CellTone.Fair)]
    [InlineData(0.5, D.CellTone.Fair)]
    [InlineData(0.4999, D.CellTone.Poor)]
    [InlineData(0, D.CellTone.Poor)]
    public void ACellIsGreenFromEightyFivePercentAmberFromFiftyAndRedBelow(double rate, D.CellTone tone) => Assert.Equal(tone, D.ToneOf(rate));

    [Fact]
    public void TheSampleToggleIsFiveOrAll()
    {
        Assert.Equal(5, D.MinSessions(D.SampleThreshold.Five));
        Assert.Equal(1, D.MinSessions(D.SampleThreshold.All));
        Assert.EndsWith("min_sessions=5", SiloPlayer.Core.Api.AdminInsightsApi.DevicesPath(Filters(("tz", "UTC")).ToWindow(), D.MinSessions(D.SampleThreshold.Five)));
        Assert.EndsWith("min_sessions=1", SiloPlayer.Core.Api.AdminInsightsApi.DevicesPath(Filters(("tz", "UTC")).ToWindow(), D.MinSessions(D.SampleThreshold.All)));
    }

    [Fact]
    public void TheMatrixHasOneCellPerCodecColumnAndADashWhereAClientHasNone()
    {
        var data = Devices();
        data.Codecs = ["h264", "hevc", "av1"];
        data.Matrix = [Row("Silo Desktop", "Windows", 0.9, ("h264", 0.95, 40, 2), ("hevc", 0.6, 10, 4)), Row("", "", 0.2, ("av1", 0.1, 5, 4))];
        var matrix = D.BuildMatrix(data, Filters())!;
        Assert.Equal(["H.264", "HEVC", "AV1"], matrix.CodecLabels);
        Assert.Equal(["Silo Desktop", "Unknown client"], matrix.Rows.Select(row => row.Client));
        Assert.Equal([D.CellTone.Good, D.CellTone.Fair, null], matrix.Rows[0].Cells.Select(cell => cell?.Tone));
        Assert.Null(matrix.Rows[1].Cells[0]);
        Assert.Equal(["95%", "40"], [matrix.Rows[0].Cells[0]!.Percent, matrix.Rows[0].Cells[0]!.Sessions]);
        Assert.Equal(["90%", "50"], [matrix.Rows[0].Total, matrix.Rows[0].TotalSessions]);
        Assert.False(matrix.Rows[0].IsJellyfin);
    }

    [Fact]
    public void ACellTooltipSaysTheClientCodecRateAndTranscodes()
    {
        var data = Devices();
        data.Codecs = ["hevc"];
        data.Matrix = [Row("Silo Desktop", "Windows", 0.9, ("hevc", 0.6, 10, 4)), Row("Roku", "Roku", 0.9, ("hevc", 0.6, 10, 1))];
        var matrix = D.BuildMatrix(data, Filters())!;
        Assert.Equal("Silo Desktop · HEVC: 60% direct (10 sessions; 4 transcodes)", matrix.Rows[0].Cells[0]!.Tooltip);
        Assert.Equal("Roku · HEVC: 60% direct (10 sessions; 1 transcode)", matrix.Rows[1].Cells[0]!.Tooltip);
    }

    [Fact]
    public void ACellDrillsIntoHistoryWithItsClientPlatformCodecAndTheWindow()
    {
        var data = Devices();
        data.Codecs = ["hevc"];
        data.Matrix = [Row("Silo Desktop", "Windows", 0.9, ("hevc", 0.6, 10, 4)), Row("", "", 0.9, ("hevc", 0.6, 10, 4))];
        var matrix = D.BuildMatrix(data, Filters(("library_id", "2"), ("user_id", "9")))!;
        var window = "started_after=" + Uri.EscapeDataString(data.From) + "&started_before=" + Uri.EscapeDataString(data.To);
        Assert.Equal("/admin/history?client_name=Silo%20Desktop&device_platform=Windows&library_id=2&source_video_codec=hevc&" + window + "&user_id=9", matrix.Rows[0].Cells[0]!.History.Href);
        // A client or platform the server recorded none for matches the rows with none.
        Assert.Contains("client_name=unknown", matrix.Rows[1].Cells[0]!.History.Href);
        Assert.Contains("device_platform=unknown", matrix.Rows[1].Cells[0]!.History.Href);
    }

    [Fact]
    public void WithNoClientOrNoCodecTheMatrixIsEmpty()
    {
        var data = Devices();
        data.Matrix = [];
        Assert.Null(D.BuildMatrix(data, Filters()));
        data = Devices();
        data.Codecs = [];
        Assert.Null(D.BuildMatrix(data, Filters()));
    }

    [Fact]
    public void ACodecIsLabelledLikeTheRestOfTheAppAndUnknownOnesAreUppercased()
    {
        Assert.Equal(["H.264", "HEVC", "AV1", "VP9", "—"], new[] { "h264", "hevc", "av1", "vp9", "" }.Select(code => D.CodecLabel(code)));
    }

    // ── Reasons, transcoders, hardware and resolution ─────────

    [Fact]
    public void AReasonUsesTheServersLabelAndAnUnknownOneReadsAsItsCode()
    {
        var data = Devices();
        data.AdaptationReasons =
        [
            new() { Reason = "video_codec_unsupported", Sessions = 30, ShareOfAdapted = 0.6 },
            new() { Reason = "bandwidth_cap", Sessions = 5, ShareOfAdapted = 0.06 },
            new() { Reason = "a_newer_reason", Sessions = 1, ShareOfAdapted = 0.02 },
        ];
        var rows = D.Reasons(data, Filters(("library_id", "4")));
        Assert.Equal(AdminText.T("web.admin.activity.reason.videoCodecUnsupported.label"), rows[0].Label);
        Assert.Equal(AdminText.T("web.admin.activity.reason.bandwidthCap.label"), rows[1].Label);
        Assert.Equal("a_newer_reason", rows[2].Label);
        Assert.Equal(["60%", "6.0%", "2.0%"], rows.Select(row => row.Secondary));
        Assert.Equal([30d, 5d, 1d], rows.Select(row => row.Value));
        Assert.Contains("adaptation_reason=video_codec_unsupported", rows[0].History!.Href);
        Assert.Contains("library_id=4", rows[0].History!.Href);
    }

    [Fact]
    public void ClientsTranscodingMostShowTheirShareAndDrillIntoTheirTranscodes()
    {
        var data = Devices();
        data.TopTranscoders = [new() { ClientName = "Roku", DevicePlatform = "Roku OS", TranscodeSessions = 8, TotalSessions = 20 }, new() { ClientName = "", DevicePlatform = "", TranscodeSessions = 1, TotalSessions = 1 }];
        var rows = D.TopTranscoders(data, Filters(("user_id", "5")));
        Assert.Equal(["Roku · Roku OS", "Unknown client"], rows.Select(row => row.Label));
        Assert.Equal(["8 of 20 sessions", "1 of 1 session"], rows.Select(row => row.Secondary));
        Assert.Contains("play_method=transcode", rows[0].History!.Href);
        Assert.Contains("client_name=Roku", rows[0].History!.Href);
        Assert.Contains("user_id=5", rows[0].History!.Href);
        Assert.Contains("client_name=unknown", rows[1].History!.Href);
    }

    [Fact]
    public void HardwareAccelerationUsesTheOfficialLabels()
    {
        var data = Devices();
        data.HwAccelBreakdown = new[] { "vaapi", "qsv", "nvenc", "videotoolbox", "none", "software", "unknown", "amf" }.Select(key => new AdminInsightsLabeledCount { Key = key, Sessions = 1 }).ToList();
        Assert.Equal(["VAAPI", "QSV", "NVENC", "VideoToolbox", "Software (CPU)", "Software (CPU)", "Unknown", "AMF"], D.HardwareAcceleration(data).Select(row => row.Label));
    }

    [Fact]
    public void BurnInCodecsLinkToEveryBurnedInSessionOnlyWhenThereAreSome()
    {
        var data = Devices();
        data.SubtitleBurnInCodecs = [new() { Key = "ass", Sessions = 3 }, new() { Key = "hevc", Sessions = 1 }];
        Assert.Equal(["ASS", "HEVC"], D.BurnInCodecs(data).Select(row => row.Label));
        Assert.Contains("subtitle_burn_in=true", D.BurnInHistory(data, Filters())!.Href);
        data.SubtitleBurnInCodecs = [];
        Assert.Null(D.BurnInHistory(data, Filters()));
    }

    [Fact]
    public void ResolutionsAreNamedAndDownscalesReadAsFromTo()
    {
        Assert.Equal(["8K", "4K", "SD", "1080p", "720p", "Unknown", "Unknown"], new[] { "4320p", "2160p", "480p", "1080p", "720p", "unknown", "540p" }.Select(key => D.ResolutionLabel(key)));
        var data = Devices();
        data.ResolutionChanges = [new() { From = "2160p", To = "1080p", Sessions = 4 }];
        Assert.Equal(["4K → 1080p"], D.Downscales(data).Select(row => row.Label));
        Assert.Equal([4d], D.Downscales(data).Select(row => row.Value));
    }

    // ── App and track breakdowns ──────────────────────────────

    [Fact]
    public void SiloAppsAgainstJellyfinAppsFillQuietBucketsAndNeedAtLeastOneSession()
    {
        var data = Devices();
        data.BucketUnit = "day";
        data.From = "2026-11-20T00:00:00Z";
        data.To = "2026-11-24T00:00:00Z";
        data.Tz = "UTC";
        data.ProtocolBuckets = [new() { LabelDate = "2026-11-21", NativeSessions = 3, JellyfinSessions = 1 }, new() { LabelDate = "2026-11-23", NativeSessions = 2, JellyfinSessions = 0 }];
        var shape = D.ProtocolColumns(data)!;
        Assert.Equal(["Silo apps", "Jellyfin apps"], shape.SeriesLabels);
        Assert.Equal(4, shape.Columns.Count);
        Assert.Equal([0d, 3d, 0d, 2d], shape.Columns.Select(column => column.Segments[0]));
        Assert.Equal([0d, 1d, 0d, 0d], shape.Columns.Select(column => column.Segments[1]));
        data.ProtocolBuckets = [new() { LabelDate = "2026-11-21" }];
        Assert.Null(D.ProtocolColumns(data));
    }

    [Fact]
    public void AppVersionsShowTheClientVersionSessionsAndLastSeen()
    {
        var rows = D.Versions(Devices());
        var row = Assert.Single(rows);
        Assert.Equal(["client name sample", "device platform sample", "client version sample", "12 sessions"], [row.Client, row.Platform, row.Version, row.Sessions]);
        Assert.True(row.IsJellyfin);
        Assert.Equal(AdminInsightsRules.FormatShortCalendarDate("2026-11-20T10:30:00.000Z"), row.LastSeen);
        var data = Devices();
        data.ClientVersions = [new() { ClientName = "", ClientVersion = "1.0", Sessions = 1, LastSeenAt = null }];
        Assert.Equal(["Unknown client", "—", "1 session"], [D.Versions(data)[0].Client, D.Versions(data)[0].LastSeen, D.Versions(data)[0].Sessions]);
    }

    [Fact]
    public void SubtitlesSummariseShareLanguagesDeliveryAndFlags()
    {
        var data = Devices();
        data.Subtitles = new AdminInsightsSubtitleUsage
        {
            Sessions = 40, SubtitledSessions = 10, ForcedSessions = 2, HearingImpairedSessions = 1,
            Languages = [new() { Key = "en", Sessions = 6 }, new() { Key = "unknown", Sessions = 4 }],
            Deliveries = [new() { Key = "embedded", Sessions = 5 }, new() { Key = "burn_in", Sessions = 3 }, new() { Key = "hls_rendition", Sessions = 1 }, new() { Key = "sidecar", Sessions = 1 }, new() { Key = "future", Sessions = 1 }],
        };
        var section = D.Subtitles(data);
        Assert.Equal("25% of sessions showed subtitles (10 of 40 sessions with detailed playback info)", section.Subtitle);
        Assert.Equal(["English", "Unknown"], section.Languages.Select(row => row.Label));
        Assert.Equal(["60%", "40%"], section.Languages.Select(row => row.Secondary));
        Assert.Equal(["In the stream", "Burned in", "Streaming track", "Separate file", "Unknown"], section.Deliveries.Select(row => row.Label));
        Assert.Equal("2 forced · 1 for the deaf and hard of hearing", section.Flags);
        data.Subtitles.ForcedSessions = 0;
        data.Subtitles.HearingImpairedSessions = 0;
        Assert.Null(D.Subtitles(data).Flags);
    }

    [Fact]
    public void WithoutRouteFactsSubtitlesAndAudioSayThereIsNothingToShow()
    {
        var data = Devices();
        data.Subtitles = null;
        data.Audio = null;
        Assert.Equal("No sessions with detailed playback info in this period yet.", D.Subtitles(data).Subtitle);
        Assert.Empty(D.Subtitles(data).Languages);
        Assert.Equal("No sessions with detailed playback info in this period yet.", D.Audio(data).Subtitle);
        Assert.Empty(D.Audio(data).Decisions);
    }

    [Fact]
    public void AudioNamesItsHandlingAndChannelsAndTreatsAnUnknownKeyAsUnknown()
    {
        var data = Devices();
        data.Audio = new AdminInsightsAudioUsage
        {
            Sessions = 10,
            Languages = [new() { Key = "fr", Sessions = 5 }],
            Decisions = [new() { Key = "direct", Sessions = 6 }, new() { Key = "remux", Sessions = 3 }, new() { Key = "transcode", Sessions = 1 }],
            DeliveredChannels = [new() { Key = "stereo", Sessions = 6 }, new() { Key = "surround", Sessions = 3 }, new() { Key = "mono", Sessions = 1 }, new() { Key = "unknown", Sessions = 1 }],
        };
        var audio = D.Audio(data);
        Assert.Equal("The audio track each session played, how it reached the app, and its channels", audio.Subtitle);
        Assert.Equal(["French"], audio.Languages.Select(row => row.Label));
        Assert.Equal("50%", audio.Languages[0].Secondary);
        Assert.Equal(["Original file", "Copied", "Converted"], audio.Decisions.Select(row => row.Label));
        Assert.Equal(["Stereo", "Surround", "Mono", "Unknown"], audio.Channels.Select(row => row.Label));
    }
}
