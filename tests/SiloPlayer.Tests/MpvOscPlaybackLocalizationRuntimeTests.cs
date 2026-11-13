using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

// Task 5 playback localization: the real silo-osc.lua inside bundled libmpv
// receives catalog text through its ordinary "osc-set-locale" script message.
// Payload values come from the shipped generated catalogs, never from the test.
public sealed partial class MpvOscRuntimeTests
{
    private const string LocaleHarness = """
        handlers = {}
        acks = {}
        local register = mp.register_script_message
        mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
        local commandv = mp.commandv
        mp.commandv = function(...)
            local args = {...}
            if args[1] == 'script-message' and args[2] == 'silo-locale-applied' then
                table.insert(acks, table.concat(args, '|', 3))
            end
            return commandv(...)
        end
        """;

    // A raw media title with Arabic, Latin and ASS-significant characters. It
    // must reach the HUD exactly (escaped), whatever the interface language.
    private const string RawTitle = "Shōgun – مرحبا بالعالم {Episode}";

    private static AppLocale PlaybackLocale(string language)
        => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));

    private static string LuaStrings(IEnumerable<string> values)
        => "{" + string.Join(", ", values.Select(value =>
        {
            Assert.DoesNotContain("]==]", value);
            return "[==[" + value + "]==]";
        })) + "}";

    private static string[] CatalogPayload(string language, long generation, params string[] keys)
    {
        var locale = PlaybackLocale(language);
        var args = new List<string> { generation.ToString(), locale.Language, locale.IsRightToLeft ? "rtl" : "ltr" };
        foreach (var key in keys)
        {
            var value = locale.Get(key, fallback: "");
            Assert.False(string.IsNullOrEmpty(value), key);
            args.Add(key); args.Add(value);
        }
        return [.. args];
    }

    private const string HudSetup = """
        hud_time = 10
        local s = osc_test_state
        s.osc_disabled = false
        s.visible, s.current_alpha, s.target_alpha = true, 1, 1
        s.duration, s.time_pos = 3600, 60
        s.audio_tracks = { {index=0,language='en',channels=6}, {index=1,language='ja',channels=2} }
        s.active_audio = 0
        s.chapters = { {index=0,start_seconds=0,end_seconds=600,title='Cold Open'} }
        """;

    [Theory]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("ar")]
    public void LocalePayloadRelabelsTheRenderedHudWithoutTouchingRawTitles(string language)
    {
        var keys = new[] { "web.player.exit", "web.player.chaptersMenu.trigger", "web.subtitles.menu.off" };
        var payload = CatalogPayload(language, 7, keys);
        var locale = PlaybackLocale(language);
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + $$"""
            s.content_title = [==[{{RawTitle}}]==]
            osc_test_layout(); osc_test_render_hud()
            local before = s.osc_overlay.data
            handlers['osc-set-locale'](unpack({{LuaStrings(payload)}}))
            osc_test_layout(); osc_test_render_hud()
            mp.set_property('user-data/locale-hud', before .. '\n----\n' .. s.osc_overlay.data .. '\n----\n' .. tostring(s.content_title))
            """, rawAss: true, resultProperty: "user-data/locale-hud", beforeScript: LocaleHarness);
        var parts = output.Split("\n----\n");
        Assert.Equal(3, parts.Length);
        Assert.Contains("Exit", parts[0]);
        Assert.Contains("Chapters", parts[0]);
        Assert.Contains(locale.Get("web.player.exit"), parts[1]);
        Assert.Contains(locale.Get("web.player.chaptersMenu.trigger"), parts[1]);
        Assert.Contains(locale.Get("web.subtitles.menu.off"), parts[1]);
        Assert.DoesNotContain("}Exit", parts[1]);
        // The raw title is data: unchanged in state and drawn exactly (ASS-escaped).
        Assert.Equal(RawTitle, parts[2]);
        Assert.Contains("Shōgun – مرحبا بالعالم \\{Episode\\}", parts[1]);
    }

    [Fact]
    public void RightToLeftLocaleKeepsTheLeftToRightPlayerGeometry()
    {
        var payload = CatalogPayload("ar", 3, "web.player.exit", "web.subtitles.menu.off");
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + $$"""
            local function snapshot()
                osc_test_layout(); osc_test_render_hud()
                local L, parts = s.layout, {}
                for _, name in ipairs({'btn_exit','btn_play','btn_skip_back','btn_skip_fwd','btn_fullscreen','time_elapsed'}) do
                    local r = L[name]
                    if r then table.insert(parts, name .. '=' .. math.floor(r.x) .. ',' .. math.floor(r.y) .. ',' .. math.floor(r.w)) end
                end
                table.insert(parts, 'seek=' .. math.floor(L.seek_bar.x1 or L.seek_bar.x or 0) .. ',' .. math.floor(L.seek_bar.y))
                return table.concat(parts, ';')
            end
            local english = snapshot()
            handlers['osc-set-locale'](unpack({{LuaStrings(payload)}}))
            local arabic = snapshot()
            mp.set_property('user-data/locale-rtl', english .. '\n' .. arabic .. '\n' .. s.osc_overlay.data)
            """, rawAss: true, resultProperty: "user-data/locale-rtl", beforeScript: LocaleHarness);
        var lines = output.Split('\n', 3);
        Assert.Contains("btn_exit=", lines[0]);
        Assert.Contains("seek=", lines[0]);
        Assert.Equal(lines[0], lines[1]);
        Assert.Contains(PlaybackLocale("ar").Get("web.player.exit"), lines[2]);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    public void CountdownPluralAndSkipActionUseCatalogTextWhileKeepingTheirIdentity(string language)
    {
        var locale = PlaybackLocale(language);
        var countdown = locale.Get("web.player.nextEpisode.upNextIn", new Dictionary<string, object?> { ["count"] = 7 });
        var payload = CatalogPayload(language, 4, "introSkip.pill.skip", "web.player.nextEpisode.playNow", "common.cancel",
                "web.player.controls.nextEpisode")
            .Concat(["web.player.nextEpisode.upNextIn#7", countdown]).ToArray();
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + $$"""
            handlers['osc-set-locale'](unpack({{LuaStrings(payload)}}))
            s.skip_visible, s.skip_label, s.skip_target = true, 'Skip Intro', 90
            osc_test_render_skip()
            local skip = s.skip_overlay.data
            local skip_identity = s.skip_label
            s.duration, s.time_pos, s.next_ep_available = 100, 96, true
            osc_test_layout(); osc_test_check_next(); osc_test_render_next()
            local next_button = s.next_ep_overlay and s.next_ep_overlay.data or ''
            s.duration, s.time_pos = 3600, 60
            s.next_ep_countdown_active = true
            s.next_ep_countdown_remaining = 7
            s.next_ep_detail = { season_number = 2, episode_number = 5, title = [==[{{RawTitle}}]==] }
            handlers['osc-set-locale'](unpack({{LuaStrings(payload)}}))
            mp.set_property('user-data/locale-skip', skip .. '\n----\n' ..
                s.next_ep_countdown_overlay.data .. '\n----\n' .. skip_identity .. '\n----\n' .. next_button)
            """, rawAss: true, resultProperty: "user-data/locale-skip", beforeScript: LocaleHarness);
        var parts = output.Split("\n----\n");
        Assert.Contains(locale.Get("introSkip.pill.skip"), parts[0]);
        Assert.DoesNotContain("Skip Intro", parts[0]);
        // The count keeps its accent override inside the localized sentence.
        var at = countdown.IndexOf('7');
        Assert.True(at >= 0, countdown);
        Assert.Contains("7{\\c&HB3BABD&}", parts[1].Replace("\\1c", "\\c"));
        Assert.Contains(countdown[..at].Trim(), parts[1]);
        Assert.Contains(locale.Get("web.player.nextEpisode.playNow"), parts[1]);
        Assert.Contains(locale.Get("common.cancel"), parts[1]);
        Assert.Contains("مرحبا بالعالم \\{Episode\\}", parts[1]);
        Assert.Equal("Skip Intro", parts[2]);
        Assert.Contains(locale.Get("web.player.controls.nextEpisode"), parts[3]);
    }

    [Fact]
    public void ProductionOscLeavesRoomForFixtureLocalsUnderTheLuaJitLimit()
    {
        // Native fixtures prepend their own locals to the script; LuaJIT allows
        // 200 per function, so the main chunk must keep headroom.
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "libs", "mpv", "scripts", "silo-osc.lua"));
        var padded = Path.Combine(Path.GetTempPath(), $"silo-osc-padded-{Guid.NewGuid():N}.lua");
        File.WriteAllText(padded, string.Concat(Enumerable.Range(0, 10).Select(i => $"local fixture_pad_{i} = {i}\n")) + source);
        try
        {
            var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
                mp.set_property('user-data/locale-padded', osc_test_state and 'loaded' or 'missing')
                """, rawAss: true, resultProperty: "user-data/locale-padded", scriptOverride: padded);
            Assert.Equal("loaded", output);
        }
        finally { File.Delete(padded); }
    }

    [Fact]
    public void LocalePayloadIsDataNeverMarkupAndOlderGenerationsAreIgnored()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: HudSetup + """
            handlers['osc-set-locale']('9', 'de', 'ltr', 'web.player.exit', [==[Beenden{\fs99}\N%1]==],
                'evil.key', 'x', 'web.player.chaptersMenu.trigger', 'Kapitel', '@decimal', ',')
            handlers['osc-set-locale']('3', 'en', 'ltr', 'web.player.exit', 'Stale')
            osc_test_layout(); osc_test_render_hud()
            mp.set_property('user-data/locale-data', table.concat(acks, ';') .. '\n----\n' .. s.osc_overlay.data)
            """, rawAss: true, resultProperty: "user-data/locale-data", beforeScript: LocaleHarness);
        var parts = output.Split("\n----\n");
        // Generation 9 accepted three values (two texts and the decimal mark) and
        // ignored the unknown key; generation 3 was rejected without an ack.
        Assert.StartsWith("9|3|1|", parts[0]);
        Assert.DoesNotContain(";3|", parts[0]);
        // ASS-significant catalog characters are drawn literally, not as overrides.
        Assert.Contains("Beenden\\{\\", parts[1]);
        Assert.DoesNotContain("{\\fs99}", parts[1]);
        Assert.Contains("%1", parts[1]);
        Assert.DoesNotContain("Stale", parts[1]);
        Assert.Contains("Kapitel", parts[1]);
    }
}
