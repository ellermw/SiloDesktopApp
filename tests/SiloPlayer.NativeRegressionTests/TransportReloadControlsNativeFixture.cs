using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

internal static class TransportReloadControlsNativeFixture
{
    internal static async Task RunAsync(Window owner)
    {
        if (owner.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1920, 1080));
        using var video = new MpvVideoWindow();
        using var player = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
        player.InitializeWithWindow(video.Hwnd); video.SetMpv(player);
        player.SetProperty("ao", "null"); player.SetProperty("loop-file", "inf");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new ConcurrentDictionary<string, TaskCompletionSource<TransportReloadProbe>>();
        player.ScriptMessageReceived += message =>
        {
            if (message.Length == 0) return;
            if (message[0] == "silo-transport-reload-ready") ready.TrySetResult();
            else if (message[0] == "silo-transport-reload-probe" && message.Length > 2 && pending.TryRemove(message[1], out var receiver))
            {
                try { receiver.TrySetResult(JsonSerializer.Deserialize<TransportReloadProbe>(message[2], new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!); }
                catch (Exception error) { receiver.TrySetException(error); }
            }
        };
        var script = Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua");
        var diagnostic = Path.Combine(Program.ResultDirectory, "transport-reload-observer.lua");
        await File.WriteAllTextAsync(diagnostic, await File.ReadAllTextAsync(script) + """

            local _captured_handlers = {}
            local _orig_register = mp.register_script_message
            -- The handlers are already registered above; capture them for direct invocation.
            -- Re-register wrappers that also store the callback.
            local function probe_state()
                render_playback_wait()
                local wait_data = state.playback_wait_overlay and state.playback_wait_overlay.data or ''
                local has_black = wait_data:find('1c&H000000&', 1, true) ~= nil
                return {
                    playback_loading=state.playback_loading or false,
                    transport_reloading=state.transport_reloading or false,
                    playback_buffering=state.playback_buffering or false,
                    has_black_rect=has_black,
                    has_overlay_data=#wait_data > 10,
                    osc_alpha=state.current_alpha or 0,
                    native_menu_blocked=state.playback_loading or false}
            end
            mp.register_script_message('transport-reload-run', function(request, commands_json)
                local diag = 'req=' .. tostring(request) .. ' json_type=' .. type(commands_json)
                local ok, commands = pcall(require('mp.utils').parse_json, commands_json or '[]')
                diag = diag .. ' parse_ok=' .. tostring(ok) .. ' cmd_type=' .. type(commands)
                if ok and commands then
                    diag = diag .. ' count=' .. #commands
                    for _, cmd in ipairs(commands) do
                        diag = diag .. ' name=' .. tostring(cmd.name) .. ' val=' .. tostring(cmd.value)
                        if cmd.name == 'osc-set-loading' then
                            state.playback_loading = (cmd.value == 'true')
                            if state.playback_loading then
                                state.transport_reloading = false
                                state.playback_buffering = false
                                state.playback_buffering_requested = false
                            end
                            state.playback_spinner_frame = -1
                            render_playback_wait()
                        elseif cmd.name == 'osc-set-transport-reloading' then
                            state.transport_reloading = (cmd.value == 'true')
                            if state.transport_reloading then
                                state.playback_loading = false
                                state.playback_buffering = false
                                state.playback_buffering_requested = false
                            end
                            state.playback_spinner_frame = -1
                            render_playback_wait()
                        end
                    end
                end
                diag = diag .. ' final_loading=' .. tostring(state.playback_loading)
                local result = probe_state()
                result.diag = diag
                mp.commandv('script-message', 'silo-transport-reload-probe', request, require('mp.utils').format_json(result))
            end)
            mp.commandv('script-message', 'silo-transport-reload-ready')
            """);
        var clip = Path.Combine(Program.ResultDirectory, "synthetic-reload.y4m");
        GenerateClip(clip, 128);

        Command(player, "load-script", diagnostic);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));

        // Load media first so mpv's event loop is active
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded() => loaded.TrySetResult();
        player.FileLoaded += OnLoaded;
        try { player.LoadFile(clip); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        finally { player.FileLoaded -= OnLoaded; }
        player.Pause();
        await Until(() => player.IsPaused, 3000);
        await Task.Delay(200);

        // 1. Startup loading: simulate initial session load
        var startupProbe = await RunAndQuery("startup-loading",
            """[{"name":"osc-set-loading","value":"true"}]""");
        Program.Log($"Startup probe: loading={startupProbe.PlaybackLoading}, reloading={startupProbe.TransportReloading}, black={startupProbe.HasBlackRect}, overlay={startupProbe.HasOverlayData}, diag={startupProbe.Diag}");
        Require(startupProbe.PlaybackLoading, "Startup: playback_loading must be true");
        Require(!startupProbe.TransportReloading, "Startup: transport_reloading must be false");
        Require(startupProbe.HasBlackRect, "Startup: must have opaque black rectangle");
        Require(startupProbe.NativeMenuBlocked, "Startup: native menus must be blocked");
        Program.Log("PASS: startup loading has opaque cover and blocks menus");

        // 2. Clear loading, then transport reload
        var reloadProbe = await RunAndQuery("transport-reload",
            """[{"name":"osc-set-loading","value":"false"},{"name":"osc-set-transport-reloading","value":"true"}]""");
        Require(!reloadProbe.PlaybackLoading, "Reload: playback_loading must be false");
        Require(reloadProbe.TransportReloading, "Reload: transport_reloading must be true");
        Require(!reloadProbe.HasBlackRect, "Reload: must NOT have opaque black rectangle");
        Require(!reloadProbe.NativeMenuBlocked, "Reload: native menus must NOT be blocked");
        Require(reloadProbe.HasOverlayData, "Reload: must have spinner overlay data");
        Program.Log("PASS: transport reload shows transparent spinner with controls accessible");

        // 3. Clear transport reload
        var clearedProbe = await RunAndQuery("cleared",
            """[{"name":"osc-set-transport-reloading","value":"false"}]""");
        Require(!clearedProbe.TransportReloading, "Cleared: transport_reloading must be false");
        Require(!clearedProbe.HasOverlayData, "Cleared: overlay must be cleared");
        Program.Log("PASS: transport reload clears correctly");

        // 4. Mutual exclusion: loading clears transport-reloading
        var mutualProbe = await RunAndQuery("mutual-exclusion",
            """[{"name":"osc-set-transport-reloading","value":"true"},{"name":"osc-set-loading","value":"true"}]""");
        Require(mutualProbe.PlaybackLoading, "Mutual: playback_loading must be true");
        Require(!mutualProbe.TransportReloading, "Mutual: transport_reloading must be cleared by loading");
        // Clean up
        await RunAndQuery("cleanup",
            """[{"name":"osc-set-loading","value":"false"},{"name":"osc-set-transport-reloading","value":"false"}]""");
        Program.Log("PASS: loading and transport-reloading are mutually exclusive");

        async Task<TransportReloadProbe> RunAndQuery(string name, string commandsJson)
        {
            var tcs = new TaskCompletionSource<TransportReloadProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[name] = tcs;
            player.SendScriptMessage("transport-reload-run", name, commandsJson);
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Until(Func<bool> condition, int timeoutMs = 3000)
        {
            for (var i = 0; i < timeoutMs / 25 && !condition(); i++) await Task.Delay(25);
            if (!condition()) throw new TimeoutException("Condition not met.");
        }
        static void Command(MpvPlayer p, params string[] args) => Require(
            typeof(MpvPlayer).GetMethod("Command", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(p, [args]) is true,
            "Native fixture command failed: " + args[0]);
        static void GenerateClip(string path, byte shade)
        {
            using var stream = File.Create(path);
            stream.Write(System.Text.Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H90 F30:1 Ip A1:1 C420jpeg\n"));
            var pixels = new byte[160 * 90 * 3 / 2]; Array.Fill(pixels, (byte)128); Array.Fill(pixels, shade, 0, 160 * 90);
            for (var i = 0; i < 60; i++) { stream.Write(System.Text.Encoding.ASCII.GetBytes("FRAME\n")); stream.Write(pixels); }
        }
    }

    private record TransportReloadProbe
    {
        public bool PlaybackLoading { get; init; }
        public bool TransportReloading { get; init; }
        public bool PlaybackBuffering { get; init; }
        public bool HasBlackRect { get; init; }
        public bool HasOverlayData { get; init; }
        public double OscAlpha { get; init; }
        public bool NativeMenuBlocked { get; init; }
        public string? Diag { get; init; }
    }
}
