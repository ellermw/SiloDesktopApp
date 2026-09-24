using System.Runtime.InteropServices;

namespace SiloPlayer.Tests;

public sealed class MpvOscRuntimeTests
{
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
        bool rawAss = false, int windowWidth = 3840, int windowHeight = 2160)
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
            dofile([[{{script}}]])
            mp.add_timeout(0.05, function()
                {{setup}}
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
                var ptr = get(handle, "user-data/stats-test-output");
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
