using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SiloPlayer.Player;

namespace SiloPlayer.Tests;

[CollectionDefinition("Native streaming", DisableParallelization = true)]
public class NativeStreamingCollection;

[Collection("Native streaming")]
public sealed class NativeDirectStreamingTests
{
    [Theory]
    [InlineData("initial-bad-range", false)]
    [InlineData("bad-range", true)]
    public async Task FailedDirectReadSignalsTransportFailureWithoutEndingTitle(string fault, bool loadedBeforeFailure)
    {
        await using var source = new RangeMediaSource(fault);
        var previous = Environment.GetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY");
        Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", Path.Combine(Path.GetTempPath(), "silo-fault-" + Guid.NewGuid().ToString("N")));
        try
        {
            using var player = new MpvPlayer();
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int ended = 0, eof = 0, errors = 0, loaded = 0;
            player.DirectStreamError += _ => failed.TrySetResult();
            player.PlaybackEnded += () => Interlocked.Increment(ref ended);
            player.EofReached += () => Interlocked.Increment(ref eof);
            player.PlaybackError += _ => Interlocked.Increment(ref errors);
            player.FileLoaded += () => Interlocked.Increment(ref loaded);
            player.Initialize(16, 16);
            player.SetProperty("ao", "null");
            player.SetProperty("ao-null-untimed", "yes");
            player.SetProperty("video", "no");
            player.LoadFile(source.Url);
            player.Play();
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(loadedBeforeFailure, loaded > 0);
            Assert.Equal(0, ended);
            Assert.Equal(0, eof);
            Assert.Equal(0, errors);
        }
        finally { Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", previous); }
    }

    [Fact]
    public async Task SignedDirectPlaybackRetainsHealthyConnectionAcrossBoundedRanges()
    {
        await using var source = new RangeMediaSource("keepalive");
        var previousLogDirectory = Environment.GetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY");
        Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", Path.Combine(Path.GetTempPath(), "silo-keepalive-" + Guid.NewGuid().ToString("N")));
        try
        {
            using var player = new MpvPlayer();
            player.DirectHttpHandlerFactory = source.CreateHandler;
            player.Initialize(16, 16);
            player.SetProperty("ao", "null");
            player.SetProperty("ao-null-untimed", "yes");
            player.SetProperty("video", "no");
            player.LoadFile(source.Url);
            player.Play();
            await WaitUntilAsync(() => player.Position > 524, TimeSpan.FromSeconds(12));
            Assert.Empty(source.Errors);

            await player.Diagnostics!.FlushAsync();
            var diagnosticText = await File.ReadAllTextAsync(Path.Combine(player.Diagnostics.DirectoryPath, "playback-current.jsonl"));
            Assert.Contains("request_start", diagnosticText);
            Assert.Contains("response_headers", diagnosticText);
            Assert.Contains("range_complete", diagnosticText);
            Assert.DoesNotContain("fixture-signed-url", diagnosticText);
            Assert.DoesNotContain("must-not-leak", diagnosticText);
            Assert.True(source.Requests.Count >= 3);
            Assert.Equal(1, source.ConnectionCount);
            Assert.All(source.Requests, range => Assert.InRange(range.To!.Value - range.From + 1, 1, 33554432));
        }
        finally { Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", previousLogDirectory); }
    }

    [Fact]
    public async Task ReusedPlayerReopensAfterRepeatedHttpsCacheStallsWithoutAppRestart()
    {
        var previousLogDirectory = Environment.GetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY");
        Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY",
            Path.Combine(Path.GetTempPath(), "silo-native-reuse-" + Guid.NewGuid().ToString("N")));
        try
        {
            using var player = new MpvPlayer();
            player.Initialize(16, 16);
            player.SetProperty("ao", "null");
            player.SetProperty("speed", "16");
            player.SetProperty("video", "no");
            for (var attempt = 0; attempt < 4; attempt++)
            {
                await using var source = new RangeMediaSource("https-body-stall");
                player.DirectHttpHandlerFactory = source.CreateHandler;
                player.LoadFile(source.Url);
                player.Play();
                await WaitUntilAsync(() => source.Interruptions == 1 &&
                    player.IsBufferingForCache && player.Position > 70 && player.Position < 100,
                    TimeSpan.FromSeconds(8));
                // Reproduce closing playback while an HTTPS body read is stuck,
                // then resume on the SAME native instance before its read timeout.
                var recorder = player.Diagnostics!;
                player.Stop();
                player.LoadFile(source.Url, startSeconds: 80);
                Assert.Same(recorder, player.Diagnostics);
                player.Play();
                await WaitUntilAsync(() => !player.IsBufferingForCache && player.Position > 110,
                    TimeSpan.FromSeconds(5));
                Assert.Empty(source.Errors);
                await recorder.FlushAsync();
                var incidents = string.Join("\n", Directory.GetFiles(recorder.DirectoryPath, "stall-*.json").Select(File.ReadAllText));
                Assert.Contains("buffer_empty", incidents);
                Assert.Contains("stop_requested", incidents);
                Assert.Contains("buffer_recovered", incidents);
                Assert.Contains("\"startPosition\":80", incidents);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", previousLogDirectory);
        }
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("http503")]
    [InlineData("header-stall")]
    [InlineData("initial404")]
    [InlineData("initial429")]
    [InlineData("body-stall")]
    [InlineData("https-body-stall")]
    [InlineData("https-header-stall")]
    [InlineData("https-body-trickle")]
    public async Task SignedDirectPlaybackBoundsRequestsAndRecoversInterruptedMediaWithoutRelay(string reconnectFault)
    {
        await using var source = new RangeMediaSource(reconnectFault);
        var logDirectory = Path.Combine(Path.GetTempPath(), "silo-native-streaming-" + Guid.NewGuid().ToString("N"));
        var previousLogDirectory = Environment.GetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY");
        Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", logDirectory);
        try
        {
            using var player = new MpvPlayer();
            player.DirectHttpHandlerFactory = source.CreateHandler;
            var playbackErrors = new ConcurrentQueue<string>();
            player.PlaybackError += playbackErrors.Enqueue;
            player.Initialize(16, 16);
            // Silent, unpaced output exercises the real decoder and transport in seconds.
            player.SetProperty("ao", "null");
            player.SetProperty("ao-null-untimed", "yes");
            player.SetProperty("video", "no");
            player.LoadFile(source.Url, "Bearer must-not-leak-to-signed-stream");
            player.Play();
            await WaitUntilAsync(() =>
            {
                Assert.Empty(source.Errors);
                Assert.Empty(playbackErrors);
                return player.Position > 524;
            }, TimeSpan.FromSeconds(20));

            Assert.All(source.Requests, range =>
            {
                Assert.NotNull(range.To);
                Assert.InRange(range.To!.Value - range.From + 1, 1, 33_554_432);
            });
            Assert.Equal(1, source.Interruptions);
            Assert.Contains(source.Requests, range => reconnectFault == "https-body-trickle"
                ? range.From >= 16_777_216 && range.From < 17_500_000
                : range.From == 16_777_216);
            Assert.All(source.Paths, path => Assert.Equal("/stream/direct/fixture-signed-url?signature=fixture", path));
            Assert.Empty(source.AuthorizationHeaders);
            Assert.Empty(source.Errors);

            // Reload at a saved position, then seek while paused. This must use the
            // original media timeline, not restart at the beginning after recovery.
            player.Pause();
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.FileLoaded += () => loaded.TrySetResult();
            player.LoadFile(source.Url, startSeconds: 180);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => Math.Abs(player.Position - 180) < 0.1, TimeSpan.FromSeconds(5));
            player.Seek(300);
            await WaitUntilAsync(() => Math.Abs(player.Position - 300) < 0.1, TimeSpan.FromSeconds(5));
            Assert.True(player.IsPaused);
            player.Play();
            await WaitUntilAsync(() => player.Position > 524, TimeSpan.FromSeconds(10));

            // Switching away from signed direct playback must reset its options
            // and restore explicit bearer auth for non-signed HTTP loads.
            var requestCount = source.Requests.Count;
            player.LoadFile(source.Url.Replace("/stream/direct/", "/legacy/"), "Bearer fixture-account-token");
            player.Play();
            await WaitUntilAsync(() => source.Requests.Count > requestCount && player.Position > 524,
                TimeSpan.FromSeconds(10));
            Assert.Contains(source.Requests.Skip(requestCount), range => range.To is null);
            Assert.Contains("Authorization: Bearer fixture-account-token", source.AuthorizationHeaders);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", previousLogDirectory);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var limit = new CancellationTokenSource(timeout);
        while (!predicate())
            await Task.Delay(10, limit.Token);
    }

    private sealed class RangeMediaSource : IAsyncDisposable
    {
        private readonly byte[] _media = new byte[96 * 1024 * 1024];
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _clients = [];
        private readonly Task _accept;
        private int _interruptions;
        private int _reconnectFaults;
        private int _connections;
        public int ConnectionCount => Volatile.Read(ref _connections);
        private readonly string _reconnectFault;
        private readonly X509Certificate2? _certificate;
        public int Interruptions => Volatile.Read(ref _interruptions);
        public ConcurrentQueue<(long From, long? To)> Requests { get; } = new();
        public ConcurrentQueue<string> Paths { get; } = new();
        public ConcurrentQueue<string> AuthorizationHeaders { get; } = new();
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public string Url { get; }

        public HttpMessageHandler CreateHandler()
        {
            var handler = new SocketsHttpHandler();
            if (_certificate != null)
            {
                var expected = _certificate.GetCertHashString();
                handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate?.GetCertHashString() == expected;
            }
            return handler;
        }

        public RangeMediaSource(string reconnectFault)
        {
            _reconnectFault = reconnectFault.Replace("https-", "");
            if (reconnectFault.StartsWith("https-"))
            {
                using var key = RSA.Create(2048);
                var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
                _certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx));
            }
            using var writer = new BinaryWriter(new MemoryStream(_media));
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(_media.Length - 8);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
            writer.Write((short)2); writer.Write(48000); writer.Write(192000);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(_media.Length - 44);
            _listener.Start();
            Url = $"{(_certificate == null ? "http" : "https")}://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/stream/direct/fixture-signed-url?signature=fixture";
            _accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                    _clients.Add(ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token), Interlocked.Increment(ref _connections)));
            }
            catch (OperationCanceledException) { }
        }

        private async Task ServeAsync(TcpClient client, int connectionNumber)
        {
            using (client)
            try
            {
                using Stream stream = _certificate == null ? client.GetStream() : new SslStream(client.GetStream());
                if (stream is SslStream tls)
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _stop.Token);
                using var reader = new StreamReader(stream, leaveOpen: true);
                while (!_stop.IsCancellationRequested)
                {
                var first = await reader.ReadLineAsync(_stop.Token);
                if (first == null) return;
                Paths.Enqueue(first!.Split(' ')[1]);
                long from = 0;
                long? to = null;
                bool close = false;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
                {
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    {
                        var range = line.Split('=')[1].Split('-');
                        from = long.Parse(range[0]);
                        if (range[1].Length > 0) to = long.Parse(range[1]);
                    }
                    if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                        AuthorizationHeaders.Enqueue(line);
                    if (line.Equals("Connection: close", StringComparison.OrdinalIgnoreCase)) close = true;
                }
                Requests.Enqueue((from, to));
                if (_reconnectFault == "initial-bad-range" || (_reconnectFault == "bad-range" && from >= 16_777_216))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token);
                    return;
                }
                if ((from == 16_777_216 || (from == 0 && _reconnectFault.StartsWith("initial"))) &&
                    _reconnectFault is not ("disconnect" or "body-stall" or "body-trickle") &&
                    Interlocked.CompareExchange(ref _reconnectFaults, 1, 0) == 0)
                {
                    if (_reconnectFault == "header-stall")
                        await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                    else
                    {
                        var status = _reconnectFault switch { "initial404" => 404, "initial429" => 429, _ => 503 };
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 {status} Temporary Failure\r\nContent-Length: 5\r\nETag: \"error-page\"\r\nConnection: close\r\n\r\nerror"), _stop.Token);
                    }
                    return;
                }
                var end = Math.Min(to ?? (_media.Length - 1), _media.Length - 1);
                var persistent = _reconnectFault == "keepalive" && !close;
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 206 Partial Content\r\nContent-Type: audio/wav\r\nAccept-Ranges: bytes\r\nContent-Range: bytes {from}-{end}/{_media.Length}\r\nContent-Length: {end - from + 1}\r\nConnection: {(persistent ? "keep-alive" : "close")}\r\n\r\n"), _stop.Token);
                var count = (int)(end - from + 1);
                // The player's demux cache consumes the initial response. A new
                // request at exactly 16 MiB proves recovery of this interruption.
                var interrupt = _reconnectFault != "keepalive" && from == 0 && count > 16 * 1024 * 1024 &&
                    Interlocked.CompareExchange(ref _interruptions, 1, 0) == 0;
                if (interrupt)
                    count = 16 * 1024 * 1024;
                if (_reconnectFault == "keepalive" && connectionNumber > 1)
                {
                    for (var offset = (int)from; offset <= end; offset += 4096)
                    {
                        await Task.Delay(200, _stop.Token);
                        await stream.WriteAsync(_media.AsMemory(offset, (int)Math.Min(4096, end - offset + 1)), _stop.Token);
                    }
                }
                else await stream.WriteAsync(_media.AsMemory((int)from, count), _stop.Token);
                if (interrupt && _reconnectFault == "body-stall")
                    await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                if (interrupt && _reconnectFault == "body-trickle")
                {
                    // Match the incident's continuing tiny reads: they keep a
                    // socket inactivity timeout alive while playable data drains.
                    for (var offset = (int)from + count; offset <= end; offset += 4096)
                    {
                        await Task.Delay(200, _stop.Token);
                        await stream.WriteAsync(_media.AsMemory(offset, (int)Math.Min(4096, end - offset + 1)), _stop.Token);
                    }
                }
                if (!persistent || interrupt) return;
                }
            }
            catch (IOException) { /* mpv closes probing and seek requests early */ }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Errors.Enqueue(ex); }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _accept;
            _listener.Stop();
            await Task.WhenAll(_clients);
            _certificate?.Dispose();
            _stop.Dispose();
        }
    }
}
