using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleSidecarReaderTests
{
    private static readonly Uri Url = new("https://sidecar.invalid/eng.srt?synthetic=1");

    [Theory]
    [InlineData("0")]
    [InlineData("date")]
    [InlineData("invalid")]
    [InlineData(null)]
    public async Task BusyReadRetriesThenReturnsLocalBytes(string? retry)
    {
        using var wire = new Wire((n, _) => n < 3 ? Busy(retry) : Ok());
        using var http = new HttpClient(wire);
        var pending = SubtitleSidecarReader.ReadAsync(http, Url, "srt", CancellationToken.None);
        Assert.False(pending.IsCompleted);
        var result = await pending;
        Assert.Equal(SubtitleSidecarReadOutcome.Ready, result.Outcome);
        Assert.True(result.WasBusy);
        Assert.False(result.CanUseUrlFallback);
        Assert.Equal(".srt", result.Extension);
        Assert.Equal("subtitle", System.Text.Encoding.UTF8.GetString(result.Bytes!));
        Assert.Equal(3, wire.Requests);
        Assert.All(wire.Urls, u => Assert.Equal(Url, u));
    }

    [Fact]
    public async Task RepeatedBusyStopsAtFourAttemptsAndNeverOffersFallback()
    {
        using var wire = new Wire((_, _) => Busy("0")); using var http = new HttpClient(wire);
        var result = await SubtitleSidecarReader.ReadAsync(http, Url, "srt", default);
        Assert.Equal(SubtitleSidecarReadOutcome.Busy, result.Outcome);
        Assert.True(result.WasBusy);
        Assert.False(result.CanUseUrlFallback);
        Assert.Equal(4, wire.Requests);
    }

    [Theory]
    [InlineData("60")]
    [InlineData("far-date")]
    public async Task RetryAfterOutsideDeadlineDoesNotRetryEarly(string retry)
    {
        using var wire = new Wire((_, _) => Busy(retry)); using var http = new HttpClient(wire);
        Assert.Equal(SubtitleSidecarReadOutcome.Busy, (await SubtitleSidecarReader.ReadAsync(http, Url, "srt", default)).Outcome);
        Assert.Equal(1, wire.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DoesNotRetryBeforeDeltaOrDatePermitsIt(bool useDate)
    {
        var due = DateTimeOffset.UtcNow.AddSeconds(1);
        using var wire = new Wire((n, _) =>
        {
            if (n != 1) { Assert.True(DateTimeOffset.UtcNow >= due.AddMilliseconds(-50)); return Ok(); }
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = useDate ? new RetryConditionHeaderValue(due) : new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        });
        using var http = new HttpClient(wire);
        Assert.Equal(SubtitleSidecarReadOutcome.Ready, (await SubtitleSidecarReader.ReadAsync(http, Url, "srt", default)).Outcome);
        Assert.Equal(2, wire.Requests);
    }

    [Fact]
    public async Task CancellationDuringRetryStopsWithoutAnotherRequest()
    {
        using var cancellation = new CancellationTokenSource();
        using var wire = new Wire((_, _) => Busy("2")); using var http = new HttpClient(wire);
        var pending = SubtitleSidecarReader.ReadAsync(http, Url, "srt", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, wire.Requests);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.NotFound)]
    [InlineData(true, HttpStatusCode.NotFound)]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.InternalServerError)]
    public async Task NonBusyResponseAllowsUrlFallbackOnlyWithoutBusyHistory(bool priorBusy, HttpStatusCode status)
    {
        using var wire = new Wire((n, _) => priorBusy && n == 1 ? Busy("0") : new(status));
        using var http = new HttpClient(wire);
        var result = await SubtitleSidecarReader.ReadAsync(http, Url, "srt", default);
        Assert.Equal(priorBusy ? SubtitleSidecarReadOutcome.Busy : SubtitleSidecarReadOutcome.Error, result.Outcome);
        Assert.Equal(priorBusy, result.WasBusy);
        Assert.Equal(!priorBusy, result.CanUseUrlFallback);
        Assert.Equal(priorBusy ? 2 : 1, wire.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedSidecarIsNotBufferedButAllowsAsyncUrlFallback(bool chunked)
    {
        using var wire = new Wire((_, _) => new(HttpStatusCode.OK) { Content = new OversizeContent(chunked) });
        using var http = new HttpClient(wire);
        var result = await SubtitleSidecarReader.ReadAsync(http, Url, "srt", default);
        Assert.Equal(SubtitleSidecarReadOutcome.Error, result.Outcome);
        Assert.True(result.CanUseUrlFallback);
        Assert.Null(result.Bytes); Assert.Equal(1, wire.Requests);
    }

    [Fact]
    public async Task StalledTextReadExhaustsPrefetchBudgetButAllowsAsyncUrlFallback()
    {
        using var wire = new HangingWire(); using var http = new HttpClient(wire);
        var pending = SubtitleSidecarReader.ReadAsync(http, Url, "srt", default);
        Assert.False(pending.IsCompleted);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(12));
        Assert.Equal(SubtitleSidecarReadOutcome.Error, result.Outcome);
        Assert.True(result.CanUseUrlFallback);
        Assert.Null(result.Bytes); Assert.Equal(1, wire.Requests);
    }

    [Fact]
    public async Task UnsupportedFormatStillOffersFallbackWithoutRequestingIt()
    {
        using var wire = new Wire((_, _) => Ok()); using var http = new HttpClient(wire);
        var result = await SubtitleSidecarReader.ReadAsync(http, new("https://sidecar.invalid/captions.sup"), "pgs", default);
        Assert.Equal(SubtitleSidecarReadOutcome.Fallback, result.Outcome);
        Assert.Equal(0, wire.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransportFailureAllowsUrlFallbackOnlyWithoutBusyHistory(bool priorBusy, bool ioFailure)
    {
        using var wire = new Wire((n, _) => priorBusy && n == 1 ? Busy("0") :
            throw (ioFailure ? (Exception)new IOException("Interrupted body") : new HttpRequestException("Connection reset")));
        using var http = new HttpClient(wire);
        var result = await SubtitleSidecarReader.ReadAsync(http, Url, "srt", default);
        Assert.Equal(priorBusy ? SubtitleSidecarReadOutcome.Busy : SubtitleSidecarReadOutcome.Error, result.Outcome);
        Assert.Equal(priorBusy, result.WasBusy);
        Assert.Equal(!priorBusy, result.CanUseUrlFallback);
        Assert.Equal(priorBusy ? 2 : 1, wire.Requests);
    }

    [Fact]
    public async Task SidecarRequestUsesAdvertisedAuthorityWithoutAccountHeader()
    {
        using var wire = new Wire((_, request) => { Assert.Null(request.Headers.Authorization); return Ok(); });
        using var http = new HttpClient(wire);
        Assert.Equal(SubtitleSidecarReadOutcome.Ready, (await SubtitleSidecarReader.ReadAsync(http, Url, "srt", default)).Outcome);
        Assert.Equal(Url, Assert.Single(wire.Urls));
    }

    [Fact]
    public async Task NonHttpUrlDoesNotMakeARequest()
    {
        using var wire = new Wire((_, _) => Ok()); using var http = new HttpClient(wire);
        Assert.Equal(SubtitleSidecarReadOutcome.Fallback, (await SubtitleSidecarReader.ReadAsync(http, new("file:///C:/eng.srt"), "srt", default)).Outcome);
        Assert.Equal(0, wire.Requests);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("subtitle") };
    private static HttpResponseMessage Busy(string? retry)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        if (retry == "date") response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow);
        else if (retry == "far-date") response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(1));
        else if (retry != null) response.Headers.TryAddWithoutValidation("Retry-After", retry);
        return response;
    }
    private sealed class Wire(Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests; public List<Uri> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Urls.Add(request.RequestUri!); return Task.FromResult(respond(++Requests, request)); }
    }
    private sealed class HangingWire : HttpMessageHandler
    {
        public int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The stalled read must be cancelled by its budget.");
        }
    }
    private sealed class OversizeContent(bool chunked) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = SubtitleSidecarFile.MaxBytes + 1L; return !chunked; }
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => await stream.WriteAsync(new byte[SubtitleSidecarFile.MaxBytes + 1]);
    }
}
