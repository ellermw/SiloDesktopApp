using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SiloPlayer.Player;

internal static class DolbyVisionOct24NativeFixture
{
    internal static async Task RunAsync()
    {
        var fixtures = Environment.GetEnvironmentVariable("SILO_DOVI_FIXTURE_DIRECTORY")
            ?? throw new InvalidOperationException("Set SILO_DOVI_FIXTURE_DIRECTORY to the prepared, provenance-recorded Dolby Vision fixtures.");
        var library = NativeLibrary.Load(Path.Combine(Program.AppDirectory, "libmpv-2.dll"));
        NativeLibrary.SetDllImportResolver(typeof(MpvPlayer).Assembly,
            (name, _, _) => name == "libmpv-2.dll" ? library : IntPtr.Zero);
        using var server = new MediaServer(fixtures);
        var window = Native.CreateWindowExW(0x08000080, "STATIC", "Silo isolated Dolby Vision regression", unchecked((int)0x80000000),
            -20000, -20000, 960, 540, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new InvalidOperationException("No isolated rendering window.");
        var reports = new List<object>();
        try
        {
            foreach (var (name, profile, enhancement) in new[] {
                ("p7-fel-repeat10s.mkv", 7, true), ("dovi-p7-hvce.mp4", 7, true),
                ("p7-mel.mkv", 7, false), ("regular.mkv", 8, false),
                ("dolby-profile84.mp4", 8, false), ("dovi-p5.mp4", 5, false) })
            {
                using var player = new MpvPlayer();
                player.InitializeWithWindow(window);
                player.SetProperty("mute", "yes");
                player.SetProperty("keep-open", "always");
                player.LoadFile(server.Origin + "/stream/direct/" + name);
                var snapshot = await UntilAsync(() => player.GetVideoPipelineSnapshot(),
                    value => value is { Renderer: "gpu-next", DolbyVisionReshaping: true } && value.DolbyVisionProfile == profile
                        && value.EnhancementLayerApplied == enhancement, name);
                if (snapshot.EngineVersion != SiloPlayer.Core.Services.MpvNativePlaybackCapabilities.VerifiedMpvVersion
                    || snapshot.FfmpegVersion != SiloPlayer.Core.Services.MpvNativePlaybackCapabilities.VerifiedFfmpegVersion)
                    throw new InvalidOperationException("The packaged engine differs from the advertised version.");
                player.Pause();
                var initial = player.Position;
                await Task.Delay(100);
                if (!player.IsPaused || Math.Abs(player.Position - initial) > .15)
                    throw new InvalidOperationException(name + ": native pause failed.");
                if (name == "p7-fel-repeat10s.mkv" || name == "p7-mel.mkv" || name == "regular.mkv" || name == "dolby-profile84.mp4")
                {
                    player.Seek(5);
                    await UntilAsync(() => player.Position, value => Math.Abs(value - 5) < .3, name + " seek");
                    player.Play();
                    await UntilAsync(() => player.Position, value => value > 5.3, name + " resume");
                    snapshot = await UntilAsync(() => player.GetVideoPipelineSnapshot(),
                        value => value?.DolbyVisionReshaping == true && value.EnhancementLayerApplied == enhancement,
                        name + " after seek");
                }
                reports.Add(new { sample = name, pipeline = snapshot, serverRequests = server.Requests, serverBytes = server.Bytes });
                Program.Log($"PASS: {name}, P{snapshot.DolbyVisionProfile}, {snapshot.Renderer}/{snapshot.HardwareDecoder}, reshaping={snapshot.DolbyVisionReshaping}, enhancement={snapshot.EnhancementLayerApplied}, actual target={snapshot.TargetTransfer}/{snapshot.TargetPixelFormat}; signed native reader/pause/seek checked where duration permits.");
            }
            if (server.Requests < 6 || server.Bytes <= 0) throw new InvalidOperationException("Native direct transport was not exercised.");
            File.WriteAllText(Path.Combine(Program.ResultDirectory, "dolby-vision-pipeline.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Native.DestroyWindow(window); }
    }

    private static async Task<T> UntilAsync<T>(Func<T> read, Func<T, bool> ready, string context)
    {
        var timer = Stopwatch.StartNew();
        T value = default!;
        while (timer.Elapsed < TimeSpan.FromSeconds(9))
        {
            value = read();
            if (ready(value)) return value;
            await Task.Delay(40);
        }
        throw new InvalidOperationException(context + ": native pipeline did not settle: " + JsonSerializer.Serialize(value));
    }

    private sealed class MediaServer : IDisposable
    {
        private readonly string _directory;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _requests;
        private long _bytes;
        internal string Origin { get; }
        internal int Requests => Volatile.Read(ref _requests);
        internal long Bytes => Interlocked.Read(ref _bytes);
        internal MediaServer(string directory)
        {
            _directory = Path.GetFullPath(directory);
            _listener.Start();
            Origin = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = ServeAsync(client); } }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                var request = (await reader.ReadLineAsync(_stop.Token))!.Split(' ');
                var name = Path.GetFileName(request[1]);
                if (name is not ("p7-fel-repeat10s.mkv" or "dovi-p7-hvce.mp4" or "p7-mel.mkv" or "regular.mkv" or "dolby-profile84.mp4" or "dovi-p5.mp4"))
                    throw new InvalidOperationException("Unexpected fixture media route.");
                long from = 0; long? last = null; bool ranged = false;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                    {
                        ranged = true;
                        var range = line[13..].Trim().Split('-'); from = long.Parse(range[0]);
                        if (range.Length > 1 && range[1].Length > 0) last = long.Parse(range[1]);
                    }
                await using var input = File.OpenRead(Path.Combine(_directory, name));
                var end = Math.Min(last ?? input.Length - 1, input.Length - 1);
                var count = end - from + 1;
                if (from < 0 || count <= 0) throw new InvalidOperationException("Invalid fixture range.");
                Interlocked.Increment(ref _requests);
                var rangeHeader = ranged ? $"Content-Range: bytes {from}-{end}/{input.Length}\r\n" : "";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {(ranged ? "206 Partial Content" : "200 OK")}\r\nContent-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\nContent-Length: {count}\r\n{rangeHeader}ETag: \"fixture-video\"\r\nConnection: close\r\n\r\n"), _stop.Token);
                if (request[0] == "HEAD") return;
                input.Position = from;
                var buffer = new byte[64 * 1024];
                while (count > 0)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)), _stop.Token);
                    if (read == 0) break;
                    await stream.WriteAsync(buffer.AsMemory(0, read), _stop.Token);
                    Interlocked.Add(ref _bytes, read); count -= read;
                }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }

    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr CreateWindowExW(uint ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr window);
    }
}
