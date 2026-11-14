using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Tests;

// Task 5 playback localization: display text follows the interface language
// while identifiers, wire values and raw media/user text stay unchanged.
public sealed class PlaybackLocalizationDisplayTests
{
    private static AppLocale Locale(string language) => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));

    [Fact]
    public void FailureTitlesFollowTheLanguageButServerMessagesStayAsSent()
    {
        var german = Locale("de");
        using (AppStrings.UseForCurrentFlow(german))
        {
            var policy = PlaybackFailureDescription.Describe(new PlaybackPlanTerminalException("policy_denied", null, retryable: false));
            Assert.Equal(german.Get("web.player.watch.playbackUnavailable"), policy.Title);
            Assert.Equal(german.Get("web.player.error.notPermitted.message"), policy.Message);
            Assert.NotEqual("Playback unavailable", policy.Title);
            var server = PlaybackFailureDescription.Describe(new PlaybackPlanTerminalException("no_alternate_version", "Server wrote this.", retryable: true));
            Assert.Equal("Server wrote this.", server.Message);
            var limit = PlaybackFailureDescription.DescribeTransport(new ApiException("stream_limit_reached", "too many", 429));
            Assert.Equal(german.Get("web.player.error.streamLimitReached.title"), limit!.Title);
        }
    }

    [Fact]
    public void SubtitleSourceLabelsMatchTheWebComposition()
    {
        SubtitleTrackInfo Track(int index, string label, string source, bool forced = false, bool sdh = false) => new()
        { Index = index, Language = "ja", Label = label, Source = source, Forced = forced, HearingImpaired = sdh };
        var tracks = new[] { Track(0, "Commentary «raw»", "embedded", forced: true), Track(1, "", "external", sdh: true),
            Track(2, "", "external", sdh: true) };
        var german = Locale("de");
        using (AppStrings.UseForCurrentFlow(german))
        {
            var labels = SubtitleSourceLabels.Build(tracks, code => AudioTrackDisplayText.LanguageName(code, "Japanese"));
            var japanese = german.Get("language.ja");
            Assert.NotEqual("Japanese", japanese);
            Assert.Equal(string.Join(" · ", japanese, "Commentary «raw»", german.Get("web.subtitles.translate.forced"),
                german.Get("web.subtitles.translate.source.embedded")), labels[tracks[0]]);
            var duplicate = string.Join(" · ", japanese, german.Get("web.subtitles.translate.sdh"), german.Get("web.subtitles.translate.source.external"));
            Assert.Equal(german.Get("web.subtitles.translate.trackNumbered", new Dictionary<string, object?> { ["label"] = duplicate, ["number"] = 2 }),
                labels[tracks[1]]);
            Assert.Equal(3, labels.Values.Distinct().Count());
        }
    }

    [Fact]
    public void LanguageNamesAndBitratesUseTheInterfaceLanguageAndDisplayCulture()
    {
        var german = Locale("de");
        using (AppStrings.UseForCurrentFlow(german))
        {
            Assert.Equal(german.Get("language.ja"), AudioTrackDisplayText.LanguageName("jpn", "Japanese"));
            Assert.Equal("Klingon", AudioTrackDisplayText.LanguageName("tlh", "Klingon"));
            Assert.Equal(german.Get("web.common.format.bitrate.kbps", new Dictionary<string, object?> { ["rate"] = "640" }),
                AudioTrackDisplayText.FormatBitrate(640));
        }
    }

    [Fact]
    public void QualityAndSpeedMenusLocalizeLabelsButKeepWireIdentifiers()
    {
        var german = Locale("de");
        using (AppStrings.UseForCurrentFlow(german))
        {
            var speed = PlaybackSpeedMenuModel.Build(1.35, true, false, false);
            var custom = Assert.Single(speed.Where(row => row.IsCurrent));
            Assert.Equal("rate:1.35", custom.Id);
            Assert.Equal(german.Get("web.player.speed.rate", new Dictionary<string, object?> { ["rate"] = "1,35" }), custom.Title);
            var versions = new List<FileVersion> { new() { FileId = 41, Resolution = "1080p" }, new() { FileId = 71, Resolution = "2160p" } };
            var session = new PlaybackStartResponse { MediaFileId = 41, ProtocolVersion = 3,
                AvailableQualities = [new PlaybackQualityV3 { Label = "auto", DisplayName = "Auto" },
                    new PlaybackQualityV3 { Label = "720p", DisplayName = "720p", BitrateKbps = 4500 }] };
            var rows = PlaybackQualityMenuModel.Build(versions, session, "720p", 71, false, true, "");
            Assert.Contains(german.Get("web.player.quality.versionPlaying"), rows.Single(r => r.Id == "file:41").Badges);
            Assert.Contains(german.Get("web.player.quality.versionRequested"), rows.Single(r => r.Id == "file:71").Badges);
            Assert.Equal(german.Get("web.common.format.bitrate.mbps", new Dictionary<string, object?> { ["rate"] = "4,5" }),
                rows.Single(r => r.Id == "quality:720p").Metadata);
        }
    }

    [Fact]
    public void HistoryFallbackNamesTheFileInTheInterfaceLanguage()
    {
        using var row = JsonDocument.Parse("""{"media_file_id": 12}""");
        var japanese = Locale("ja");
        using (AppStrings.UseForCurrentFlow(japanese))
            Assert.Equal(japanese.Get("web.common.activity.title.file", new Dictionary<string, object?> { ["id"] = "12" }),
                PlaybackHistoryPresentation.Format(row.RootElement).Title);
    }
}
