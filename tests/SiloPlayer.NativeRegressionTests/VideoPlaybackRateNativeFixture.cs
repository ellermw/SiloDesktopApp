using System.Net;
using System.Reflection;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

/// <summary>Actual service/settings/libmpv behavior on generated media; menu and physical input are separate gates.</summary>
internal static class VideoPlaybackRateNativeFixture
{
    internal static async Task RunAsync()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://video-speed.invalid");
        client.SetProfile("synthetic"); client.SetDeviceMetadata("synthetic-desktop", "Fixture", "Windows");
        using var auth = new AuthService(client, new AuthApi(client));
        var api = new SettingsApi(client); var playback = new PlaybackApi(client); var catalog = new CatalogApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "video-speed-settings"));
        using var service = new PlayerService(playback, catalog, auth, client, settings, api);
        using var manager = new PlaybackManager(playback, catalog, auth, client);
        var accepted = new PlaybackStartResponse { SessionId = "synthetic-session", MediaFileId = 42 };
        typeof(PlaybackManager).GetProperty(nameof(PlaybackManager.CurrentSession))!.SetValue(manager, accepted);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16);
        mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "synthetic-video");
        var wave = Path.Combine(Program.ResultDirectory, "video-speed.wav");
        using (var writer = new BinaryWriter(File.Create(wave)))
        {
            const int bytes = 8000 * 60 * 2;
            writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded() => loaded.TrySetResult();
        mpv.FileLoaded += Loaded; mpv.LoadFile(wave); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8));
        mpv.FileLoaded -= Loaded; mpv.Play(); await Task.Delay(200);
        try
        {
            Call(service, "EnsureVideoPlaybackRateSession");
            await wire.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Require(service.CanChangeVideoPlaybackRate && !service.IsLoading, "Optional held settings read blocked active video.");
            var pauseEvents = 0;
            void Paused(bool paused) => Interlocked.Increment(ref pauseEvents);
            mpv.PauseChanged += Paused;
            var position = mpv.Position;
            await service.SetVideoPlaybackRateAsync(1.75); await Task.Delay(250);
            Require(Math.Abs(service.NativePlaybackRate - 1.75) < 0.00001 && !mpv.IsPaused && pauseEvents == 0,
                "Manual speed did not update real mpv without forcing pause.");
            Require(mpv.Position > position && mpv.Position - position < 1.5, "Speed action caused an unexpected seek.");
            mpv.PauseChanged -= Paused;
            mpv.Pause(); await Task.Delay(150); position = mpv.Position;
            var nativePosition = mpv.GetPropertyDouble("time-pos");
            var nativeLogPath = Path.Combine(Program.ResultDirectory, "mpv_log.txt");
            var logStart = ReadNativeLog(nativeLogPath).Length;
            Property(service, "IsPaused", true);
            await service.SetVideoPlaybackRateAsync(0.75); await Task.Delay(200);
            var nativeAfter = mpv.GetPropertyDouble("time-pos");
            var actionLog = ReadNativeLog(nativeLogPath)[logStart..];
            Require(mpv.IsPaused && mpv.GetPropertyFlag("pause") && actionLog.Contains("Set property: speed=\"0.75\"")
                && !actionLog.Contains("Run command: seek") && !actionLog.Contains("Set property: pause="),
                "Paused speed action issued a seek/pause command or changed pause state.");
            // libmpv recalculates its audio-delay-derived time-pos when speed
            // changes while paused. Reproduce with only direct native property
            // writes to distinguish this clock correction from a service seek.
            mpv.SetProperty("speed", "1.75"); await Task.Delay(50);
            var directBefore = mpv.GetPropertyDouble("time-pos");
            mpv.SetProperty("speed", "0.75"); await Task.Delay(50);
            var directAfter = mpv.GetPropertyDouble("time-pos");
            Program.Log($"Paused native clock: service {nativePosition:R}->{nativeAfter:R}; direct-property {directBefore:R}->{directAfter:R}; pause={mpv.GetPropertyFlag("pause")}; action log contains speed only.");
            Require(Math.Abs((nativeAfter - nativePosition) - (directAfter - directBefore)) < 0.03,
                "Service rate change had different clock effects from direct libmpv property writes.");
            wire.ReadRelease.TrySetResult(); await Task.Delay(100);
            Require(service.DesiredVideoPlaybackRate == 0.75 && service.NativePlaybackRate == 0.75,
                "Late settings read replaced current manual desire.");
            service.SetWatchTogetherPlayback(true); service.NativePlaybackRate = 1.03;
            var writes = wire.Writes.Count;
            await service.SetVideoPlaybackRateAsync(2);
            Require(!service.CanChangeVideoPlaybackRate && wire.Writes.Count == writes && service.DesiredVideoPlaybackRate == 0.75,
                "Room-owned rate was manually changed or persisted drift.");
            service.SetWatchTogetherPlayback(false);
            Require(service.NativePlaybackRate == 0.75 && mpv.IsPaused, "Room departure failed to restore viewer desire without resuming.");
            service.NativePlaybackRate = 1;
            Call(service, "RestoreVideoPlaybackRate");
            Require(service.NativePlaybackRate == 0.75 && wire.Writes.Count == writes, "Same-session restore wrote preferences or lost desire.");
            wire.FailWrite = true; await service.SetVideoPlaybackRateAsync(1.35);
            Require(service.DesiredVideoPlaybackRate == 1.35 && service.VideoPlaybackRateError?.Contains("retry") == true,
                "Current failure did not retain rate and retry feedback.");
            wire.FailWrite = false; await service.SetVideoPlaybackRateAsync(1.35);
            Require(service.VideoPlaybackRateError == null, "Successful retry did not clear feedback.");
            accepted.MediaFileId = 43;
            Require(!service.CanChangeVideoPlaybackRate, "Replacement file retained old speed command authority.");
            writes = wire.Writes.Count; await service.SetVideoPlaybackRateAsync(2);
            Require(wire.Writes.Count == writes, "Old file speed request dispatched for replacement media.");
            Call(service, "ResetVideoPlaybackRateSession");
            Property(service, "IsAudiobook", true); Call(service, "EnsureVideoPlaybackRateSession");
            Require(!service.CanChangeVideoPlaybackRate && wire.Writes.Count == writes, "Audiobook entered normal-video preference path.");
            Property(service, "IsAudiobook", false);
            Set(service, "_activeAuthority", new PlaybackAuthorityScope(client, auth));
            var reads = wire.Reads; Call(service, "EnsureVideoPlaybackRateSession"); await service.SetVideoPlaybackRateAsync(2);
            Require(!service.CanChangeVideoPlaybackRate && wire.Reads == reads && wire.Writes.Count == writes,
                "Temporary remote authority entered desktop preference path.");
            Program.Log("PASS: video-speed actual PlayerService/SettingsApi/libmpv: optional read, manual/latest/read, playing/paused, no seek, room/drift/departure, restore, failure/retry, file/audiobook/temporary-owner guards.");
        }
        finally
        {
            wire.ReadRelease.TrySetResult();
            // The service normally owns these. Keep fixture disposal unambiguous.
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, Flags)!.SetValue(target, value);
    private static void Property(object target, string property, object value) => target.GetType().GetProperty(property)!.SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Flags)!.Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string ReadNativeLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Reads;
        public bool FailWrite;
        public List<double> Writes { get; } = [];
        public TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                Interlocked.Increment(ref Reads); ReadEntered.TrySetResult(); await ReadRelease.Task;
                return Json(HttpStatusCode.OK, "{\"items\":[{\"key\":\"player.playback_speed\",\"value\":1.35}]}");
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Writes.Add(body.RootElement.GetProperty("value").GetDouble());
            return Json(FailWrite ? HttpStatusCode.InternalServerError : HttpStatusCode.OK, "{}");
        }
        private static HttpResponseMessage Json(HttpStatusCode code, string value) => new(code) { Content = new StringContent(value) };
    }
}
