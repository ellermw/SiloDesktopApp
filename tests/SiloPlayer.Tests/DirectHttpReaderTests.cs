using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public sealed class DirectHttpReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2700)]
    public async Task Http2TricklingConnectionIsReplacedAndEveryRecoveredByteMatches(int headerDelayMilliseconds)
    {
        var bytes = new byte[4 * 1024 * 1024];
        new Random(8712).NextBytes(bytes);
        using var key = RSA.Create(2048);
        var certRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
        {
            listen.Protocols = HttpProtocols.Http2;
            listen.UseHttps(certificate);
        }));
        await using var app = builder.Build();
        var requests = new ConcurrentQueue<(string Connection, string Protocol, long Offset)>();
        string? firstConnection = null;
        var elapsed = new Stopwatch();
        double recoveredAfter = 0;
        app.Run(async context =>
        {
            var from = RangeHeaderValue.Parse(context.Request.Headers.Range.ToString()).Ranges.Single().From!.Value;
            requests.Enqueue((context.Connection.Id, context.Request.Protocol, from));
            Interlocked.CompareExchange(ref firstConnection, context.Connection.Id, null);
            context.Response.StatusCode = 206;
            context.Response.ContentLength = bytes.Length - from;
            context.Response.Headers.ContentRange = $"bytes {from}-{bytes.Length - 1}/{bytes.Length}";
            context.Response.Headers.ETag = "\"fixture\"";
            try
            {
                if (context.Connection.Id == firstConnection)
                {
                    await Task.Delay(headerDelayMilliseconds, context.RequestAborted);
                    for (long offset = from; offset < bytes.Length; offset += 1024)
                    {
                        await context.Response.Body.WriteAsync(bytes.AsMemory((int)offset, (int)Math.Min(1024, bytes.Length - offset)), context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                        await Task.Delay(200, context.RequestAborted);
                    }
                }
                else
                {
                    recoveredAfter = elapsed.Elapsed.TotalSeconds;
                    await context.Response.Body.WriteAsync(bytes.AsMemory((int)from), context.RequestAborted);
                }
            }
            catch (OperationCanceledException) { }
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var diagnosticsDirectory = Path.Combine(Path.GetTempPath(), "silo-http2-flight-" + Guid.NewGuid().ToString("N"));
        using var diagnostics = new PlaybackDiagnostics(directory: diagnosticsDirectory);
        using var reader = new DirectHttpReader(address, () => 4, () => new SocketsHttpHandler
        {
            SslOptions = new() { RemoteCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == certificate.GetCertHashString() }
        }, diagnostics);
        elapsed.Start();
        var actual = await Task.Run(() => ReadAll(reader)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(bytes, actual);
        Assert.Equal(2, requests.Select(r => r.Connection).Distinct().Count());
        Assert.All(requests, r => Assert.Equal("HTTP/2", r.Protocol));
        Assert.InRange(requests.Last().Offset, 1, 32768);
        // The five-second useful-progress deadline covers the whole HTTP
        // transaction. Waiting for headers must not grant another five seconds.
        Assert.InRange(recoveredAfter, 4.8, 6.5);
        await diagnostics.FlushAsync();
        var report = await File.ReadAllTextAsync(Directory.GetFiles(diagnosticsDirectory, "stall-*.json").Single());
        Assert.Contains("ConnectionEstablished", report);
        Assert.Contains("127.0.0.1", report);
        Assert.Contains("response_headers", report);
        Assert.Contains("progress_deadline", report);
        // Evict connection establishment from the ring, then reuse the connection.
        // Each subsequent request must still identify its peer independently.
        for (int i = 0; i < 3000; i++) diagnostics.Record("history_pressure");
        reader.Seek(0);
        Assert.True(reader.Read(new byte[1024], 1024) > 0);
        await diagnostics.FlushAsync();
        var lines = await File.ReadAllLinesAsync(Path.Combine(diagnosticsDirectory, "playback-current.jsonl"));
        using var requestEvent = System.Text.Json.JsonDocument.Parse(lines.Last(line => line.Contains("RequestHeadersStart")));
        Assert.Equal(IPAddress.Loopback, IPAddress.Parse(requestEvent.RootElement.GetProperty("Data").GetProperty("peer").GetString()!).MapToIPv4());
        reader.Seek(bytes.Length); // Close the partially consumed response before shutting down the fixture.
        await app.StopAsync();
    }

    [Theory]
    [InlineData("ignored-range")]
    [InlineData("wrong-start")]
    [InlineData("changed-tag")]
    [InlineData("changed-length")]
    public void InvalidResponseIsRejectedBeforeSplicingBytes(string fault)
    {
        int requests = 0;
        using var reader = new DirectHttpReader("https://fixture.invalid", () => 1, () => new Handler(request =>
        {
            var from = request.Headers.Range!.Ranges.Single().From!.Value;
            var response = Response(from, 128, requests++ == 0 ? "original" : fault == "changed-tag" ? "different" : "original");
            if (requests == 2)
            {
                Assert.Equal("\"original\"", request.Headers.IfRange!.EntityTag!.Tag);
                if (fault == "ignored-range") response.StatusCode = HttpStatusCode.OK;
                if (fault == "wrong-start") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 127, 128);
                if (fault == "changed-length") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, 128, 129);
            }
            return response;
        }));
        Assert.Equal(128, reader.Length);
        reader.Seek(64);
        Assert.Throws<InvalidDataException>(() => reader.Read(new byte[64], 64));
        Assert.Equal(64, reader.Position);
        Assert.Equal(2, requests);
    }

    [Fact]
    public void PersistentFailuresHaveFiniteRetries()
    {
        int requests = 0;
        using var reader = new DirectHttpReader("https://fixture.invalid", () => 1, () => new Handler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        Assert.Throws<InvalidDataException>(() => reader.Read(new byte[1], 1));
        Assert.Equal(9, requests);
    }

    [Fact]
    public async Task CancelInterruptsPendingHeadersWithoutRetry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reader = new DirectHttpReader("https://fixture.invalid", () => 1, () => new BlockingHandler(entered));
        var read = Task.Run(() => reader.Read(new byte[1], 1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        reader.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static byte[] ReadAll(DirectHttpReader reader)
    {
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = reader.Read(buffer, buffer.Length)) > 0) output.Write(buffer, 0, count);
        return output.ToArray();
    }
    private static HttpResponseMessage Response(long from, int length, string tag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[length - from]) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, length - 1, length);
        response.Headers.ETag = new EntityTagHeaderValue('"' + tag + '"');
        return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
    private sealed class BlockingHandler(TaskCompletionSource entered) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        }
    }
}
