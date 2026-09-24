using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public sealed class PlaybackDiagnosticsTests
{
    [Theory]
    [InlineData("headers", "headers", "progress_deadline")]
    [InlineData("body", "body", "progress_deadline")]
    [InlineData("disconnect", "body", "IOException")]
    [InlineData("auth", "headers_or_validation", "authorization_rejected")]
    public async Task ReportsIdentifyFailureStageAndPreserveEvidenceWithoutSecrets(string fault, string stage, string reason)
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-flight-test-" + Guid.NewGuid().ToString("N"));
        using var diagnostics = new PlaybackDiagnostics(directory: directory);
        diagnostics.Record("before_stall", new { position = 123.4 });
        int pool = 0;
        using var reader = new DirectHttpReader("https://fixture.invalid/stream/direct/secret-token?signature=secret-signature", () => 1,
            () => new Source(fault, Interlocked.Increment(ref pool) == 1), diagnostics);
        var buffer = new byte[65536];
        if (fault == "auth") Assert.Throws<InvalidDataException>(() => reader.Read(buffer, buffer.Length));
        else
        {
            long bytes = 0;
            int count;
            while ((count = reader.Read(buffer, buffer.Length)) > 0) bytes += count;
            Assert.Equal(1024 * 1024, bytes);
        }
        diagnostics.Record("buffer_recovered", reader.DiagnosticSnapshot());
        await diagnostics.FlushAsync();
        var report = await File.ReadAllTextAsync(Directory.GetFiles(directory, "stall-*.json").Single());
        Assert.Contains("before_stall", report);
        Assert.Contains("buffer_recovered", report);
        Assert.Contains(stage, report);
        Assert.Contains(reason, report);
        Assert.Contains("request_start", report);
        Assert.Contains("budgetRemainingMs", report);
        Assert.DoesNotContain("secret-token", report);
        Assert.DoesNotContain("secret-signature", report);
        Assert.DoesNotContain("fixture.invalid", report);
        if (fault == "disconnect")
        {
            Assert.Contains("ConnectionReset", report);
            Assert.Contains("nativeError", report);
        }
        if (fault != "auth") { Assert.Contains("resumeByte", report); Assert.Contains("range_complete", report); }
    }

    [Fact]
    public async Task RecorderBoundsHistoryAndReportsDroppedPendingEvents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-flight-bound-" + Guid.NewGuid().ToString("N"));
        using var recorder = new PlaybackDiagnostics(directory: directory);
        for (int i = 0; i < 5000; i++) recorder.Record("fixture", new { sequence = i });
        recorder.Incident("fixture_stall");
        await recorder.FlushAsync();
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(directory, "stall-*.json").Single()));
        Assert.InRange(document.RootElement.GetProperty("before").GetArrayLength(), 1, 2048);
        Assert.True(document.RootElement.GetProperty("dropped").GetInt64() > 0);
    }

    [Fact]
    public async Task FinalIncidentSurvivesTemporaryWriteFailureAndNonFiniteNativeValues()
    {
        var directory = Path.GetTempFileName();
        var recorder = new PlaybackDiagnostics(directory: directory);
        recorder.Record("native_sample", new { cache = double.NaN, speed = double.PositiveInfinity });
        recorder.Incident("fixture_stall");
        recorder.Dispose();
        await recorder.FlushAsync();
        File.Delete(directory);
        await recorder.FlushAsync();
        var path = Directory.GetFiles(directory, "stall-*.json").Single();
        using var report = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.True(report.RootElement.GetProperty("complete").GetBoolean());
        Assert.Contains("native_sample", report.RootElement.ToString());
        Assert.Contains("NaN", report.RootElement.ToString());
        Assert.Contains("IOException", report.RootElement.ToString());
    }

    [Fact]
    public async Task LoggingFailureDoesNotFailTheReader()
    {
        var blockedDirectory = Path.GetTempFileName();
        using var diagnostics = new PlaybackDiagnostics(directory: blockedDirectory);
        using var reader = new DirectHttpReader("https://fixture.invalid", () => 1, () => new Source("none", false), diagnostics);
        Assert.True(reader.Read(new byte[16], 16) > 0);
        diagnostics.Incident("fixture");
        await diagnostics.FlushAsync();
        Assert.True(reader.Read(new byte[16], 16) > 0);
    }

    private sealed class Source(string fault, bool first) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (first && fault == "headers") await Task.Delay(Timeout.Infinite, ct);
            if (fault == "auth") return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var from = request.Headers.Range!.Ranges.Single().From!.Value;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            { Content = new StreamContent(first && fault is "body" or "disconnect"
                ? new FaultBody(fault) : new MemoryStream(new byte[1024 * 1024 - from])) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, 1024 * 1024 - 1, 1024 * 1024);
            return response;
        }
    }
    private sealed class FaultBody(string fault) : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (fault == "body") await Task.Delay(Timeout.Infinite, ct);
            throw new IOException("secret-signature must not be logged", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
