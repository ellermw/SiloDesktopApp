using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class Oct20PlaybackNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://oct20-playback.fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        using var service = new PlayerService(new PlaybackApi(api), new CatalogApi(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "oct20-playback-settings")), new SettingsApi(api));
        var manager = new PlaybackManager(new PlaybackApi(api), new CatalogApi(api), auth, api);
        await manager.StartSessionAsync(42, qualityPreference: "1080p-medium");
        manager.UseWatchDetail(new() { ContentId = "fixture", Type = "movie", Versions = [new() { FileId = 42, Resolution = "Fixture source", Duration = 60 }] });
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "fixture");
        Property(service, "Versions", manager.WatchDetail.Versions);
        var wave = Path.Combine(Program.ResultDirectory, "oct20-room-start.wav");
        using (var writer = new BinaryWriter(File.Create(wave)))
        {
            const int samples = 60 * 8000;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
            writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
        }
        var preparedType = typeof(PlayerService).GetNestedType("PreparedPlaybackTransport", BindingFlags.NonPublic)!;
        var prepared = Activator.CreateInstance(preparedType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [PlaybackTransportPlanner.Plan("direct", wave), wave, 0d, 0d, 0d, (double?)60d, true, null], null)!;
        Call(service, "WireMpvEvents");
        Call(service, "ApplyPreparedTransport", prepared);
        await LoadAsync(room: true, pauseDuringLoad: null);
        await Task.Delay(150);
        Require(mpv.GetPropertyFlag("pause") && service.IsPaused, "Actual initial room load autoplayed without a room command.");
        service.SetPaused(false); await Until(() => !mpv.GetPropertyFlag("pause"));
        await LoadAsync(room: true, pauseDuringLoad: true);
        await LoadAsync(room: true, pauseDuringLoad: false);
        await LoadAsync(room: false, pauseDuringLoad: null);
        Call(service, "StopPlaybackStallWatchdog");
        Program.Log("PASS: actual PlayerService BeginMpvLoad/FileLoaded keeps initial room held, publishes duration while busy/paused, preserves later room play/pause authority across reloads, and standalone autoplays.");

        var lua = Path.Combine(Program.ResultDirectory, "oct20-osc-observer.lua");
        await File.WriteAllTextAsync(lua, "mp.register_script_message('osc-set-quality-info', function(payload) mp.commandv('script-message', 'silo-oct20-quality', payload) end)\nmp.commandv('script-message', 'silo-oct20-ready')\n");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string>? qualityReply = null;
        void Message(string[] args)
        {
            if (args.Length == 0) return;
            if (args[0] == "silo-oct20-ready") ready.TrySetResult();
            if (args[0] == "silo-oct20-quality" && args.Length > 1) qualityReply?.TrySetResult(args[1]);
        }
        mpv.ScriptMessageReceived += Message;
        Call(mpv, "Command", (object)new[] { "load-script", lua });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)slot.GetValue(null)!;
        slot.SetValue(null, new Services(previous, service));
        var overlay = new PlayerOverlay { Width = 900, Height = 500 };
        parent.Children.Add(overlay);
        try
        {
            var cases = new[]
            {
                ("1080p-medium", "720p-medium", true, 688, 2000, "720p-medium"),
                ("1080p-medium", "1080p-medium", true, 1080, 3500, "1080p-medium"),
                ("1080p", (string?)null, true, 1080, 6000, ""),
                ("auto", "720p-medium", true, 688, 2000, "auto"),
                ("1080p", "720p", true, 720, 4000, "720p"),
                ("1080p-medium", null, false, 1080, 6000, "1080p-medium"),
            };
            var index = 0;
            await Until(() => overlay.IsLoaded && overlay.ActualWidth > 0 && overlay.ActualHeight > 0);
            foreach (var (intent, active, feature, height, bitrate, expected) in cases)
            {
                wire.Active = active; wire.Feature = feature; wire.Height = height; wire.Bitrate = bitrate;
                wire.Delivery = active == "720p" ? "original_http" : "server_transcode_hls";
                await manager.StartSessionAsync(42, qualityPreference: intent);
                Call(overlay, "PopulateQualityFlyout");
                var flyout = (FlyoutBase)overlay.FindName("QualityFlyout");
                var panel = (StackPanel)overlay.FindName("QualityListPanel");
                var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var caseIndex = index;
                void OnOpened(object? sender, object args)
                {
                    Program.Log($"TRACE quality lifecycle Opened case={caseIndex} panelLoaded={panel.IsLoaded}");
                    opened.TrySetResult();
                }
                void OnClosed(object? sender, object args)
                {
                    Program.Log($"TRACE quality lifecycle Closed case={caseIndex} panelLoaded={panel.IsLoaded}");
                    closed.TrySetResult();
                }
                flyout.Opened += OnOpened; flyout.Closed += OnClosed;
                try
                {
                    Require(!flyout.IsOpen, $"Quality case{index} began before its predecessor closed.");
                    flyout.ShowAt(overlay);
                    await opened.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await WaitForQualityPanelAsync(flyout, panel, index);
                    var known = service.AvailableQualities.Select(quality => quality.Label == "auto" ? "Auto" : quality.Label).ToHashSet();
                    var bold = panel.Children.OfType<Button>().Select(button => button.Content).OfType<Grid>()
                        .Select(row => row.Children.OfType<TextBlock>().FirstOrDefault())
                        .Where(text => text != null && known.Contains(text.Text) && text.FontWeight.Weight == Microsoft.UI.Text.FontWeights.SemiBold.Weight)
                        .Select(text => text!.Text).ToArray();
                    var expectedLabel = expected == "auto" ? "Auto" : expected;
                    Require(bold.SequenceEqual(expected.Length == 0 ? Array.Empty<string>() : [expectedLabel]), "Actual native quality picker highlights requested intent instead of delivery.");
                    Require(service.CaptureSiloCastState()["activeQualityId"]!.GetValue<string>() == expected,
                        "Actual SiloCast state disagrees with delivered quality selection.");
                    qualityReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Call(service, "SendQualityInfoToOsc");
                    using var payload = JsonDocument.Parse(await qualityReply.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                    Require(payload.RootElement.GetProperty("active_quality").GetString() == expected,
                        "Actual libmpv OSC message disagrees with native/SiloCast selection.");
                    Require(service.SelectedQualityIntent == intent && manager.QualityPreference == intent,
                        "Delivered-rung UI replaced desired future replan intent.");
                    Program.Log($"TRACE quality case{index} expected='{expected}' panel={panel.ActualWidth}x{panel.ActualHeight} loaded={panel.IsLoaded}");
                    Require(!closed.Task.IsCompleted && flyout.IsOpen, $"Quality case{index} closed before capture.");
                    await MediaParityNativeFixture.CaptureAsync(panel, $"oct20-delivered-quality-{index}.png");
                    Require(!closed.Task.IsCompleted && flyout.IsOpen && panel.IsLoaded,
                        $"Quality case{index} closed while its rendered state was being captured.");
                    flyout.Hide();
                    // IsLoaded can turn false during the closing animation. The
                    // next ShowAt must wait for the native popup's Closed event.
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await Until(() => !panel.IsLoaded);
                    index++;
                }
                finally
                {
                    try
                    {
                        if (flyout.IsOpen) { flyout.Hide(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
                    }
                    finally { flyout.Opened -= OnOpened; flyout.Closed -= OnClosed; }
                }
            }
            Program.Log("PASS: actual native quality picker, libmpv Lua OSC message and PlayerService SiloCast state agree for decoder/cap/plain/direct/Auto/authoritative-absence/older-server cases; future intent remains intact.");
        }
        finally
        {
            overlay.Deactivate(); parent.Children.Remove(overlay); slot.SetValue(null, previous);
            mpv.ScriptMessageReceived -= Message; Call(service, "DisconnectWebSocket");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null); manager.Dispose();
        }

        async Task LoadAsync(bool room, bool? pauseDuringLoad)
        {
            service.SetWatchTogetherPlayback(room);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sawBusyDuration = false;
            void Duration(double _)
            {
                if (service.IsLoading && service.IsSwitchingContent)
                {
                    sawBusyDuration = true;
                    if (room && pauseDuringLoad != false) Require(mpv.GetPropertyFlag("pause"), "Room readiness was published before native pause settled.");
                }
            }
            void Content() => loaded.TrySetResult();
            service.DurationChanged += Duration; service.ContentLoaded += Content;
            try
            {
                Call(service, "BeginMpvLoad", prepared, false);
                if (pauseDuringLoad is { } paused) service.SetPaused(paused);
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                var expectedPause = room && (pauseDuringLoad ?? true);
                Require(service.IsPaused == expectedPause && mpv.GetPropertyFlag("pause") == expectedPause,
                    "Actual loaded native pause does not match the room/standalone authority.");
                Require(sawBusyDuration && !service.IsLoading && !service.IsSwitchingContent,
                    "Actual FileLoaded published readiness before final pause/position notification.");
                Call(service, "StopPlaybackStallWatchdog");
            }
            finally { service.DurationChanged -= Duration; service.ContentLoaded -= Content; }
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task WaitForQualityPanelAsync(FlyoutBase flyout, StackPanel panel, int caseIndex)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            panel.UpdateLayout();
            foreach (var button in panel.Children.OfType<Button>()) { button.ApplyTemplate(); button.UpdateLayout(); }
            if (flyout.IsOpen && panel.IsLoaded && panel.ActualWidth > 0 && panel.ActualHeight > 0 &&
                panel.Children.OfType<Button>().All(button => button.ActualWidth > 0 && button.ActualHeight > 0)) return;
            await Task.Delay(20);
        }
        throw new InvalidOperationException($"Quality case{caseIndex} did not render after Opened: open={flyout.IsOpen}, loaded={panel.IsLoaded}, size={panel.ActualWidth}x{panel.ActualHeight}.");
    }
    private static async Task Until(Func<bool> ready) { for (var count = 0; count < 100 && !ready(); count++) await Task.Delay(20); Require(ready(), "Native playback state did not settle."); }
    private static object? Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, value);
    private static void Property(object owner, string name, object value) => owner.GetType().GetProperty(name)!.SetValue(owner, value);
    private sealed class Services(IServiceProvider fallback, PlayerService service) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(PlayerService) ? service : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        public string? Active = "720p-medium";
        public bool Feature = true;
        public string Delivery = "original_http";
        public int Height = 688, Bitrate = 2000;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "oct20-playback.fixture.invalid") throw new InvalidOperationException("Playback fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("/settings/")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
            object result = path.EndsWith("/capabilities")
                ? new { allowed = true, state = "available", protocol_versions = new[] { 3 }, installation_id = "11111111-1111-4111-8111-111111111111" }
                : path.EndsWith("/start") ? new
                {
                    protocol_version = 3, outcome = "playable", session_id = "fixture", server_features = Feature ? new[] { "plan_active_quality_v1" } : [],
                    playback_plan = new
                    {
                        plan_id = "plan", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = 42,
                        delivery = Delivery, stream = new { url = "/stream", protocol = Delivery == "server_transcode_hls" ? "hls" : "http_progressive" },
                        source = new { height = 2160, bitrate_kbps = 40000, duration_seconds = 60 },
                        effective_recipe = new { height = Height, bitrate_kbps = Bitrate }, timeline = new { can_seek_anywhere = true }, active_quality = Active,
                        available_qualities = new[] { new { label = "1080p-medium", height = 1080, bitrate_kbps = 6000 },
                            new { label = "720p-medium", height = 720, bitrate_kbps = 2000 }, new { label = "720p", height = 720, bitrate_kbps = 4000 } },
                    },
                } : new { outcome = "applied" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result)) });
        }
    }
}
