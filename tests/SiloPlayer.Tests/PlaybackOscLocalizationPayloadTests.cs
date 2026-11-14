using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

// The host's real "osc-set-locale" payload builder against the real OSC.
public sealed partial class MpvOscRuntimeTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    public void HostPayloadIsAcceptedWholeByTheProductionOsc(string language)
    {
        var payload = PlayerService.OscLocalization.Build(PlaybackLocale(language));
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + $$"""
            handlers['{{PlayerService.OscLocalization.MessageName}}'](unpack({{LuaStrings(payload)}}))
            mp.set_property('user-data/locale-ack', table.concat(acks, ';'))
            """, rawAss: true, resultProperty: "user-data/locale-ack", beforeScript: LocaleHarness);
        var parts = output.Split('|');
        Assert.Equal(4, parts.Length);
        Assert.Equal(PlaybackLocale(language).Generation.ToString(), parts[0]);
        // Every value the host sends is a key the OSC draws, and every key the
        // OSC draws arrives: no ignored payload and no English-only leftovers.
        Assert.Equal(((payload.Length - 3) / 2).ToString(), parts[1]);
        Assert.Equal("0", parts[2]);
        Assert.Equal("0", parts[3]);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    public void HudHeadingsUseUnicodeCasingAndRecognizeThreeLetterTrackCodes(string language)
    {
        var locale = PlaybackLocale(language);
        var payload = PlayerService.OscLocalization.Build(locale);
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + $$"""
            handlers['osc-set-locale'](unpack({{LuaStrings(payload)}}))
            s.stats_visible = true
            osc_test_render_stats()
            s.audio_tracks = { {index=0,language='eng',channels=2}, {index=1,language='jpn',channels=2} }
            s.audio_menu_visible = true
            osc_test_render_audio()
            mp.set_property('user-data/locale-unicode', s.stats_overlay.data .. '\n' .. s.audio_menu_overlay.data)
            """, rawAss: true, resultProperty: "user-data/locale-unicode", beforeScript: LocaleHarness);
        Assert.Contains(locale.Get("web.player.info.section.video").ToUpper(System.Globalization.CultureInfo.GetCultureInfo(locale.Language)), output);
        Assert.Contains(locale.Get("language.en"), output);
        Assert.Contains(locale.Get("language.ja"), output);
    }

    [Fact]
    public void VisibleBufferingPromptRefreshesOnLanguageChange()
    {
        var english = PlayerService.OscLocalization.Build(PlaybackLocale("en"));
        var german = PlayerService.OscLocalization.Build(PlaybackLocale("de"));
        german[0] = (long.Parse(english[0]) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + $$"""
            handlers['osc-set-locale'](unpack({{LuaStrings(english)}}))
            handlers['osc-buffering-quality']('prompt')
            local before = s.buffering_quality_overlay.data
            handlers['osc-set-locale'](unpack({{LuaStrings(german)}}))
            mp.set_property('user-data/locale-pill', before .. '\n----\n' .. s.buffering_quality_overlay.data)
            """, rawAss: true, resultProperty: "user-data/locale-pill", beforeScript: LocaleHarness);
        var parts = output.Split("\n----\n");
        Assert.Equal(2, parts.Length);
        Assert.Contains(PlaybackLocale("en").Get("bufferingQuality.prompt.title"), parts[0]);
        Assert.Contains(PlaybackLocale("de").Get("bufferingQuality.prompt.title"), parts[1]);
        Assert.NotEqual(parts[0], parts[1]);
    }

    [Fact]
    public void JapaneseStatsAndArabicPluralsRenderFromTheHostPayload()
    {
        var japanese = PlayerService.OscLocalization.Build(PlaybackLocale("ja"));
        var arabic = PlayerService.OscLocalization.Build(PlaybackLocale("ar"));
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + $$"""
            handlers['osc-set-locale'](unpack({{LuaStrings(japanese)}}))
            s.media_info = { container = 'mkv', file_size = 1610612736, bitrate = 8400, audio_sample_rate = 48000 }
            s.stats_visible = true
            osc_test_render_stats()
            local stats = s.stats_overlay.data
            handlers['osc-set-locale'](unpack({{LuaStrings(arabic)}}))
            s.watch_party = { visible = true, is_host = false, member_count = 3, connection_state = 'connected', code = 'ROOM-7' }
            osc_test_layout(); osc_test_render_hud()
            mp.set_property('user-data/locale-stats', stats .. '\n----\n' .. s.osc_overlay.data)
            """, rawAss: true, resultProperty: "user-data/locale-stats", beforeScript: LocaleHarness);
        var parts = output.Split("\n----\n");
        var ja = PlaybackLocale("ja");
        Assert.Contains(ja.Get("web.player.info.title"), parts[0]);
        Assert.Contains(ja.Get("web.player.info.container"), parts[0]);
        Assert.DoesNotContain("Playback Info", parts[0]);
        // Units and numbers come from the catalog/display culture.
        Assert.Contains(ja.Get("web.common.format.size.gib", new Dictionary<string, object?> { ["size"] = "1.5" }), parts[0]);
        var ar = PlaybackLocale("ar");
        Assert.Contains(ar.Get("web.player.watchTogether.viewerCount", new Dictionary<string, object?> { ["count"] = 3 }), parts[1]);
        Assert.Contains("ROOM-7", parts[1]);
    }

    [Fact]
    public void PayloadKeepsPlaceholdersForLiveValuesAndRendersCountsPerLanguage()
    {
        var arabic = PlayerService.OscLocalization.Build(PlaybackLocale("ar"));
        var pairs = Enumerable.Range(0, (arabic.Length - 3) / 2).ToDictionary(i => arabic[3 + i * 2], i => arabic[4 + i * 2]);
        Assert.Equal("rtl", arabic[2]);
        // Placeholders survive for the OSC to fill (isolated for RTL by the catalog formatter).
        Assert.Contains("{number}", pairs["web.player.audioMenu.trackFallback"]);
        Assert.Contains("{rate}", pairs["web.common.format.bitrate.mbps"]);
        // Plural instances differ by count where Arabic grammar does.
        Assert.NotEqual(pairs["web.player.watchTogether.viewerCount#2"], pairs["web.player.watchTogether.viewerCount#11"]);
        Assert.Equal(PlaybackLocale("ar").Get("web.player.nextEpisode.upNextIn", new Dictionary<string, object?> { ["count"] = 7 }),
            pairs["web.player.nextEpisode.upNextIn#7"]);
        Assert.True(pairs.ContainsKey("@decimal") && pairs.ContainsKey("@am"));
    }
}
