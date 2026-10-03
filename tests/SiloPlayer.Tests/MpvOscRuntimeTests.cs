using System.Runtime.InteropServices;

namespace SiloPlayer.Tests;

public sealed class MpvOscRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoplayIntroPreferenceRefreshPreservesTheLiveControlsScript(bool refreshWhileSeekPending)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            handlers['osc-set-intro-mode']('always')
            handlers['osc-set-markers']('{"content_id":"episode-2","reset_auto_skip":true,"marker_segments":[{"kind":"intro","start_seconds":0,"end_seconds":113.032}]}')
            state.time_pos = 0
            osc_test_check()
            local original_key = state.intro_prompt_key
            if not {{(refreshWhileSeekPending ? "true" : "false")}} then
                state.time_pos = 113.032
                osc_test_check()
            end
            -- Replay the logged delayed host preference response after the
            -- successor episode's intro seek has already started/completed.
            handlers['osc-set-intro-mode']('always')
            state.time_pos = 113.032
            osc_test_check()
            local retained = state.intro_prompt_key == original_key
            state.intro_prompt_remaining = 0
            osc_test_check()
            handlers['osc-mouse-move']('100', '100')
            state.current_alpha = 1
            osc_test_tick()
            keys['silo-fs-override']()
            keys['silo-menu-escape']()
            mp.set_property('user-data/probe-result', tostring(retained)..':'..tostring(state.visible)..':'..tostring(state.osc_overlay.data ~= '')..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers, keys, intents = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2]:find('silo-', 1, true) == 1 then
                    if args[2] == 'silo-fullscreen-toggle' or args[2] == 'silo-escape-unhandled' then table.insert(intents, args[2]) end
                    return
                end
                return command(...)
            end
            """);
        Assert.True(result == "true:true:true:silo-fullscreen-toggle,silo-escape-unhandled", result);
    }

    [Theory]
    [InlineData("ask", false)]
    [InlineData("ask", true)]
    [InlineData("never", false)]
    [InlineData("never", true)]
    public void ChangedIntroPreferenceCancelsItsPreviousActionTogetherWithItsKey(string mode, bool pending)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            handlers['osc-set-intro-mode']('always')
            handlers['osc-set-markers']('{"content_id":"episode","intro_start":0,"intro_end":30}')
            state.time_pos = 0; osc_test_check()
            if not {{(pending ? "true" : "false")}} then state.time_pos = 30; osc_test_check() end
            handlers['osc-set-intro-mode']('{{mode}}')
            local cancelled = state.intro_pending == nil and state.intro_undo_origin == nil
            state.time_pos = 30; osc_test_check()
            mp.set_property('user-data/probe-result', tostring(cancelled)..':'..tostring(state.intro_prompt_key ~= nil)..':'..tostring(not state.skip_visible))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("true:true:true", result);
    }

    [Fact]
    public void FillControlTogglesNativeContainCoverWithoutChangingTransport()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.controller_focus_name = 'btn_fill'
            osc_test_activate()
            local fill = mp.get_property_number('panscan', 0)
            osc_test_activate()
            mp.set_property('user-data/probe-result', tostring(fill) .. ':' .. tostring(mp.get_property_number('panscan', 0)))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("1:0", result);
    }
    [Fact]
    public void CompactControlLayoutKeepsUtilitiesReachableThroughOverflow()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            osc_test_layout()
            local layout = osc_test_state.layout
            mp.set_property('user-data/probe-result', tostring(layout.btn_more ~= nil) .. ':' .. tostring(layout.btn_audio == nil) .. ':' .. tostring(layout.btn_pip == nil))
            """, rawAss: true, windowWidth: 500, windowHeight: 450, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true:true", result);
    }

    [Fact]
    public void QualityChoicesRespectEmptyServerTiersAndHidePartyVersionSwitching()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.quality_info = {qualities={}, versions={{file_id=1}}}
            local empty = not osc_test_quality()
            state.quality_info.versions = {{file_id=1}, {file_id=2}}
            local versions = osc_test_quality()
            state.watch_party = true
            local party = not osc_test_quality()
            state.quality_info.qualities = {{id='original'}, {id='720p'}}
            local tiers = osc_test_quality()
            mp.set_property('user-data/probe-result', tostring(empty)..':'..tostring(versions)..':'..tostring(party)..':'..tostring(tiers))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true:true:true", result);
    }
    [Fact]
    public void ExpiredAskOfferReturnsOnReentryWithoutPretendingTheIntroWasSkipped()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.intro_mode = 'ask'; state.marker_content_id = 'episode'; state.intro_start = 0; state.intro_end = 30
            state.time_pos = 5; osc_test_check()
            state.intro_prompt_remaining = 0; osc_test_check()
            local expired = not state.skip_visible
            state.time_pos = 35; osc_test_check()
            state.time_pos = 5; osc_test_check()
            mp.set_property('user-data/probe-result', tostring(expired) .. ':' .. tostring(state.skip_visible))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true", result);
    }
    [Fact]
    public void BundledSubRipDecoderPreservesOriginalAlignmentAndTextStyle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"silo-original-subrip-{Guid.NewGuid():N}.srt");
        File.WriteAllText(path, "1\n00:00:00,000 --> 00:00:04,000\n{\\an8}<b>Bold</b> <i>Italic</i>\n");
        try
        {
            var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
                mp.register_event('file-loaded', function()
                    mp.commandv('sub-add', [[{{path.Replace('\\', '/')}}]], 'select')
                end)
                mp.commandv('loadfile', 'av://lavfi:color=c=black:s=16x16:r=10:d=5')
                mp.add_periodic_timer(0.05, function()
                    local text = mp.get_property('sub-text-ass', '')
                    if text:find('Bold', 1, true) then mp.set_property('user-data/subrip-probe', text) end
                end)
                """, rawAss: true, resultProperty: "user-data/subrip-probe");
            Assert.Contains("\\an8", result); Assert.Contains("\\b1", result); Assert.Contains("\\i1", result);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void SharedVideoSeekIntervalsReachTheRealKeyboardTransport()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            handlers['osc-set-seek-intervals']('45', '90')
            keys['silo-seek-back']()
            keys['silo-seek-fwd']()
            mp.set_property('user-data/probe-result', table.concat(seeks, ','))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers, keys, seeks = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[2] == 'silo-seek-relative' then table.insert(seeks, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("-45,90", result);
    }

    [Fact]
    public void IntroUndoWaitsForMovementAndSurvivesStructuredRangeExit()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.intro_mode = 'always'
            state.marker_content_id = 'episode'
            state.marker_segments = {{kind='intro', start_seconds=0, end_seconds=30}}
            state.time_pos = 5
            osc_test_check()
            local waiting = state.intro_pending ~= nil and state.intro_undo_origin == nil
            state.time_pos = 30
            osc_test_check()
            local undo = state.skip_visible and state.skip_label == 'Watch Intro' and state.skip_target == 0
            state.marker_segments = {}
            state.intro_pending = nil; state.intro_undo_origin = nil; state.intro_prompt_key = nil
            osc_test_check()
            mp.set_property('user-data/probe-result', tostring(waiting) .. ':' .. tostring(undo) .. ':' .. tostring(state.skip_visible))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true:false", result);
    }
    [Fact]
    public void WatchPartyKeyboardTransportEmitsOneIntentWithoutMutatingPause()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local previous = mp.get_property_bool('input-default-bindings')
            mp.set_property_bool('input-default-bindings', false)
            room_keys['silo-play-pause-space']()
            room_keys['silo-play-pause-k']()
            local disabled = mp.get_property_bool('input-default-bindings') == false
            mp.set_property_bool('input-default-bindings', previous)
            mp.set_property('user-data/room-probe-output', tostring(room_intents) .. ':' .. tostring(room_direct_pauses) .. ':' .. tostring(disabled))
            """, rawAss: true, resultProperty: "user-data/room-probe-output", beforeScript: """
            room_keys = {}
            room_intents = 0
            room_direct_pauses = 0
            local add_key = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags)
                room_keys[name] = fn
                return add_key(key, name, fn, flags)
            end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-pause-toggle' then
                    room_intents = room_intents + 1
                    return
                end
                if args[1] == 'cycle' and args[2] == 'pause' then room_direct_pauses = room_direct_pauses + 1 end
                return command(...)
            end
            """);
        Assert.Equal("2:0:true", output);
    }

    [Theory]
    [InlineData(960, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public void LongEnhancementStatusUsesSeparateLinesWithoutLosingText(int width, int height)
    {
        const string status = "Requested driver enhancement is unavailable on the current video output";
        var ass = CaptureStats(1920, 1080, 1920, 1080, false,
            "mp.set_property('user-data/silo-upscaling-status', '" + status + "')",
            rawAss: true, windowWidth: width, windowHeight: height);
        var entries = System.Text.RegularExpressions.Regex.Matches(ass,
            @"\\pos\(([\d.]+),([\d.]+)\).*?\}([^\{\r\n]+)");
        var label = entries.Cast<System.Text.RegularExpressions.Match>()
            .First(m => m.Groups[3].Value == "Enhancement status");
        var value = entries.Cast<System.Text.RegularExpressions.Match>()
            .First(m => m.Groups[3].Value.StartsWith("Requested driver"));
        Assert.True(double.Parse(value.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
            > double.Parse(label.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        var text = System.Text.RegularExpressions.Regex.Replace(ass, @"\{[^}]*\}|\\N", "")
            .Replace("\r", "").Replace("\n", "");
        Assert.Contains(status, text);
    }
    [Fact]
    public void IntelStatsIdentifyRequestedDriverProcessing()
    {
        var text = CaptureStats(1920, 1080, 3840, 2160, false, processor: "intel");
        Assert.Contains("UpscalerIntel VSR (requested)", text);
        Assert.DoesNotContain("RTX", text);
    }

    [Fact]
    public void NeuralStatsSeparateRendererReconstructionFromDecodedVideo()
    {
        var text = CaptureStats(1920, 1080, 1920, 1080, false, neural: true);
        Assert.Contains("Processed video1920x1080", text);
        Assert.Contains("UpscalerFSRCNNX AI (requested)", text);
        Assert.Contains("AI luma reconstruction (requested)3840x2160 (2x)", text);
        Assert.DoesNotContain("VSR active", text);
    }

    [Theory]
    [InlineData(1920, 1080, 3840, 2160, true, "3840x2160 (4K)", "2.00x", "4.00x", "RTX VSR (requested)")]
    [InlineData(1920, 1080, 1920, 1080, false, "1920x1080", "None", "1.00x", "Normal renderer")]
    [InlineData(3840, 2160, 3840, 2160, false, "3840x2160 (4K)", "None", "1.00x", "Normal renderer")]
    [InlineData(1920, 800, 3840, 1600, true, "3840x1600", "2.00x", "4.00x", "RTX VSR (requested)")]
    [InlineData(1920, 1080, 2880, 1620, true, "2880x1620", "1.50x", "2.25x", "RTX VSR (requested)")]
    public void PlaybackInfoDistinguishesProcessedVideoFromFullscreenWindow(int sourceWidth, int sourceHeight,
        int outputWidth, int outputHeight, bool rtx, string processed, string factor, string pixels, string upscaler)
    {
        var text = CaptureStats(sourceWidth, sourceHeight, outputWidth, outputHeight, rtx);
        Assert.Contains($"Source video{sourceWidth}x{sourceHeight}", text);
        Assert.Contains($"Processed video{processed}", text);
        Assert.Contains($"Scale per dimension{factor}", text);
        Assert.Contains($"Pixel count{pixels}", text);
        Assert.Contains($"Upscaler{upscaler}", text);
        Assert.DoesNotContain("VSR active", text);
    }

    [Theory]
    [InlineData(2000, 1800000, 128000, "2.0 Mbps", "1.8 Mbps", "128 kbps")]
    [InlineData(1500, 1200000, 96000, "1.5 Mbps", "1.2 Mbps", "96 kbps")]
    [InlineData(0, 8400000, 192000, "Not applicable", "8.4 Mbps", "192 kbps")]
    [InlineData(2000, 0, 0, "2.0 Mbps", "Unavailable", "Unavailable")]
    [InlineData(-1, 0, 0, "Unavailable", "Unavailable", "Unavailable")]
    public void PlaybackInfoSeparatesTargetAndMeasuredBitratesFromSource(int targetKbps,
        int videoBps, int audioBps, string target, string video, string audio)
    {
        var setup = $$"""
            mp.commandv('script-message', 'osc-set-media-info', '{"bitrate":8400,"video_bitrate":0}')
            mp.commandv('script-message', 'osc-set-stream-info', 'Transcode', 'HLS', 'https', 'H264', 'AAC', '6000')
            mp.commandv('script-message', 'osc-set-stream-info', '{{(targetKbps != 0 ? "Transcode" : "Direct Play")}}', 'HLS', 'https', 'H264', 'AAC', '{{targetKbps}}')
            """;
        var text = CaptureStats(1280, 720, 2560, 1440, true, setup, videoBps, audioBps);
        var stream = text.Split("PLAYBACK STREAM INFO")[1].Split("CURRENT SOURCE FILE")[0];
        var source = text.Split("CURRENT SOURCE FILE")[1];
        Assert.Contains($"Target video bitrate{target}", stream);
        Assert.Contains($"Video bitrate (measured){video}", stream);
        Assert.Contains($"Audio bitrate (measured){audio}", stream);
        Assert.Contains("Bitrate8.4 Mbps", source);
        Assert.Contains("Video bitrateNot supplied", source);
    }

    private static string CaptureStats(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight,
        bool rtx, string setup = "", int videoBps = 0, int audioBps = 0, string? processor = null, bool neural = false,
        bool rawAss = false, int windowWidth = 3840, int windowHeight = 2160,
        string beforeScript = "", string resultProperty = "user-data/stats-test-output")
    {
        var root = FindRepositoryRoot();
        var probe = Path.Combine(Path.GetTempPath(), $"silo-stats-{Guid.NewGuid():N}.lua");
        var script = Path.Combine(root, "libs", "mpv", "scripts", "silo-osc.lua").Replace('\\', '/');
        // Run the real OSC inside bundled mpv with controlled input/output
        // properties. Capture the rendered ASS, not the Lua source text.
        File.WriteAllText(probe, $$"""
            local mp = require 'mp'
            local get_number = mp.get_property_number
            local get_native = mp.get_property_native
            local create_overlay = mp.create_osd_overlay
            local values = {
                ['osd-width']={{windowWidth}}, ['osd-height']={{windowHeight}},
                ['video-params/w']={{sourceWidth}}, ['video-params/h']={{sourceHeight}},
                ['video-out-params/w']={{outputWidth}}, ['video-out-params/h']={{outputHeight}},
                ['video-bitrate']={{videoBps}}, ['audio-bitrate']={{audioBps}}
            }
            mp.get_property_number = function(name, fallback)
                if values[name] ~= nil then return values[name] end
                return get_number(name, fallback)
            end
            mp.get_property_native = function(name, fallback)
                if name == 'osd-dimensions' then return {w={{windowWidth}}, h={{windowHeight}}} end
                if name == 'vf' then return {{(rtx || processor != null ? "{{name='d3d11vpp',label='silo-rtx',enabled=true,params={['scaling-mode']='" + (processor ?? "nvidia") + "'}}}" : "{}")}} end
                if name == 'glsl-shaders' then return {{(neural ? "{'C:/fixture/FSRCNNX_x2_8-0-4-1.glsl'}" : "{}")}} end
                return get_native(name, fallback)
            end
            mp.create_osd_overlay = function(kind)
                local overlay = create_overlay(kind)
                overlay.update = function(self)
                    if self.data and self.data:find('Playback Info', 1, true) then
                        mp.set_property('user-data/stats-test-output', self.data)
                    end
                end
                return overlay
            end
            {{beforeScript}}
            local source = assert(io.open([[{{script}}]], 'r'))
            local code = source:read('*a'); source:close()
            assert(loadstring(code .. '\nosc_test_state = state; osc_test_check = check_skip_markers; osc_test_quality = quality_choices_available; osc_test_activate = activate_controller_focus; osc_test_layout = compute_layout; osc_test_tick = tick'))()
            mp.add_timeout(0.05, function()
                local ok, err = pcall(function()
                    {{setup}}
                end)
                if not ok then
                    mp.set_property('{{resultProperty}}', 'Lua error: ' .. tostring(err))
                    return
                end
                mp.commandv('script-message', 'osc-toggle-stats')
            end)
            """);
        var library = NativeLibrary.Load(Path.Combine(root, "libs", "mpv", "libmpv-2.dll"));
        var handle = LoadDelegate<MpvCreate>(library, "mpv_create")();
        try
        {
            var option = LoadDelegate<MpvSetOptionString>(library, "mpv_set_option_string");
            foreach (var (key, value) in new[] { ("vo", "null"), ("ao", "null"), ("idle", "yes"), ("osc", "no"), ("scripts", probe) })
                Assert.True(option(handle, key, value) >= 0);
            Assert.True(LoadDelegate<MpvInitialize>(library, "mpv_initialize")(handle) >= 0);
            var get = LoadDelegate<MpvGetPropertyString>(library, "mpv_get_property_string");
            var free = LoadDelegate<MpvFree>(library, "mpv_free");
            string? rendered = null;
            for (var attempt = 0; attempt < 100 && rendered == null; attempt++)
            {
                var ptr = get(handle, resultProperty);
                try { if (ptr != IntPtr.Zero) rendered = Marshal.PtrToStringUTF8(ptr); }
                finally { if (ptr != IntPtr.Zero) free(ptr); }
                if (rendered == null) Thread.Sleep(10);
            }
            Assert.NotNull(rendered);
            var ass = rendered!.StartsWith('"') ? System.Text.Json.JsonSerializer.Deserialize<string>(rendered)! : rendered;
            if (rawAss) return ass;
            var text = System.Text.RegularExpressions.Regex.Replace(ass, @"\{[^}]*\}|\\N", "")
                .Replace("\r", "").Replace("\n", "");
            return text;
        }
        finally
        {
            LoadDelegate<MpvTerminateDestroy>(library, "mpv_terminate_destroy")(handle);
            NativeLibrary.Free(library);
            File.Delete(probe);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MpvGetPropertyString(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpvFree(IntPtr value);

    [Fact]
    public void BundledLibMpv_LoadsOscWithoutLuaErrors()
    {
        var root = FindRepositoryRoot();
        var libraryPath = Path.Combine(root, "libs", "mpv", "libmpv-2.dll");
        var scriptPath = Path.Combine(root, "libs", "mpv", "scripts", "silo-osc.lua");
        var logPath = Path.Combine(Path.GetTempPath(), $"silo-osc-probe-{Guid.NewGuid():N}.log");

        Assert.True(File.Exists(libraryPath), $"Missing bundled libmpv: {libraryPath}");
        Assert.True(File.Exists(scriptPath), $"Missing OSC script: {scriptPath}");

        var library = IntPtr.Zero;
        var handle = IntPtr.Zero;
        MpvTerminateDestroy? terminate = null;

        try
        {
            library = NativeLibrary.Load(libraryPath);
            var create = LoadDelegate<MpvCreate>(library, "mpv_create");
            var setOption = LoadDelegate<MpvSetOptionString>(library, "mpv_set_option_string");
            var initialize = LoadDelegate<MpvInitialize>(library, "mpv_initialize");
            var commandString = LoadDelegate<MpvCommandString>(library, "mpv_command_string");
            terminate = LoadDelegate<MpvTerminateDestroy>(library, "mpv_terminate_destroy");
            handle = create();
            Assert.NotEqual(IntPtr.Zero, handle);

            void Set(string name, string value) =>
                Assert.True(setOption(handle, name, value) >= 0, $"libmpv rejected {name}={value}");

            Set("vo", "null");
            Set("ao", "null");
            Set("idle", "yes");
            Set("osc", "no");
            Set("log-file", logPath);
            Set("msg-level", "all=v");
            Set("scripts", scriptPath);

            Assert.True(initialize(handle) >= 0, "The bundled libmpv instance did not initialize.");
            Thread.Sleep(350);
            Assert.True(
                commandString(handle, "script-message osc-set-visibility false") >= 0,
                "The OSC rejected its Playing Next hide transition.");
            Assert.True(
                commandString(handle, "script-message osc-set-visibility true") >= 0,
                "The OSC rejected its autoplay re-enable transition.");
            Assert.True(
                commandString(handle, "script-message osc-mouse-move 100 100") >= 0,
                "The OSC rejected its first pointer wake after autoplay.");
            Thread.Sleep(150);
        }
        finally
        {
            if (handle != IntPtr.Zero)
                terminate?.Invoke(handle);
            if (library != IntPtr.Zero)
                NativeLibrary.Free(library);
        }

        try
        {
            var log = File.ReadAllText(logPath);
            Assert.Contains("silo-osc.lua", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("syntax error", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("error loading script", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stack traceback", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    private static T LoadDelegate<T>(IntPtr library, string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SiloPlayer repository root.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MpvCreate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvSetOptionString(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvInitialize(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvCommandString(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string command);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpvTerminateDestroy(IntPtr handle);
}
