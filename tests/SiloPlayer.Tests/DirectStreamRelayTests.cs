using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DirectStreamRelayTests
{
    [Fact]
    public async Task RelayAsync_RetriesFromLastByte_WhenUpstreamReadFails()
    {
        var bytes = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();
        var calls = 0;
        var handler = new SequenceHandler(request =>
        {
            calls++;
            if (calls == 1)
            {
                return CreateResponse(
                    HttpStatusCode.OK,
                    new ThrowAfterStream(bytes, throwAfterBytes: 256),
                    bytes.Length,
                    contentRange: null);
            }

            Assert.Equal(256, request.Headers.Range?.Ranges.Single().From);
            return CreateResponse(
                HttpStatusCode.PartialContent,
                new MemoryStream(bytes[256..]),
                bytes.Length - 256,
                new ContentRangeHeaderValue(256, bytes.Length - 1, bytes.Length));
        });

        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => "access-token",
            maxRetries: 2);

        using var output = new MemoryStream();

        var result = await relay.RelayAsync(output, rangeHeader: null, writeHeadersAsync: null, CancellationToken.None);

        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(bytes.Length, result.BytesWritten);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-token", request.Headers.Authorization?.Parameter);
        });
    }

    [Fact]
    public async Task RelayAsync_ForwardsClientRange_OnFirstUpstreamRequest()
    {
        var bytes = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        var handler = new SequenceHandler(request =>
        {
            Assert.Equal(100, request.Headers.Range?.Ranges.Single().From);
            Assert.Null(request.Headers.Range?.Ranges.Single().To);

            return CreateResponse(
                HttpStatusCode.PartialContent,
                new MemoryStream(bytes[100..]),
                bytes.Length - 100,
                new ContentRangeHeaderValue(100, bytes.Length - 1, bytes.Length));
        });

        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => null,
            maxRetries: 1);

        using var output = new MemoryStream();

        var result = await relay.RelayAsync(output, "bytes=100-", writeHeadersAsync: null, CancellationToken.None);

        Assert.Equal(bytes[100..], output.ToArray());
        Assert.Equal(HttpStatusCode.PartialContent, result.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RelayAsync_DefaultRetryBudgetSurvivesRepeatedLongStreamDisconnects()
    {
        var bytes = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();
        var calls = 0;
        var handler = new SequenceHandler(request =>
        {
            calls++;

            var start = request.Headers.Range?.Ranges.Single().From ?? 0;
            Assert.Equal((calls - 1) * 128, start);

            var remaining = bytes[(int)start..];
            var statusCode = start == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent;
            var contentRange = start == 0
                ? null
                : new ContentRangeHeaderValue(start, bytes.Length - 1, bytes.Length);

            if (calls <= 6)
            {
                return CreateResponse(
                    statusCode,
                    new ThrowAfterStream(remaining, throwAfterBytes: 128),
                    remaining.Length,
                    contentRange);
            }

            return CreateResponse(
                statusCode,
                new MemoryStream(remaining),
                remaining.Length,
                contentRange);
        });

        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => null);

        using var output = new MemoryStream();

        var result = await relay.RelayAsync(output, rangeHeader: null, writeHeadersAsync: null, CancellationToken.None);

        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(bytes.Length, result.BytesWritten);
        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal(7, result.UpstreamAttempts);
    }

    [Fact]
    public async Task RelayAsync_SequentialModeIgnoresRangesAndDoesNotAdvertiseThem()
    {
        var bytes = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        DirectStreamRelayHeaders? relayedHeaders = null;
        var handler = new SequenceHandler(request =>
        {
            Assert.Null(request.Headers.Range);
            return CreateResponse(
                HttpStatusCode.OK,
                new MemoryStream(bytes),
                bytes.Length,
                contentRange: null);
        });
        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session?seek=90"),
            () => "access-token",
            supportsRanges: false);
        using var output = new MemoryStream();

        var result = await relay.RelayAsync(
            output,
            "bytes=20-",
            (headers, _) =>
            {
                relayedHeaders = headers;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(relayedHeaders);
        Assert.False(relayedHeaders.AcceptRanges);
        Assert.Null(relayedHeaders.ContentRange);
    }

    [Fact]
    public async Task RelayAsync_RetriesRequestFailureAfterPartialBodyWithoutCorruptingOutput()
    {
        var bytes = Enumerable.Range(0, 512).Select(i => (byte)(i % 251)).ToArray();
        var calls = 0;
        var handler = new SequenceHandler(request =>
        {
            calls++;
            if (calls == 1)
            {
                return CreateResponse(
                    HttpStatusCode.OK,
                    new ThrowAfterStream(bytes, throwAfterBytes: 128),
                    bytes.Length,
                    contentRange: null);
            }

            Assert.Equal(128, request.Headers.Range?.Ranges.Single().From);
            if (calls == 2)
                throw new HttpRequestException("Simulated reconnect failure.");

            return CreateResponse(
                HttpStatusCode.PartialContent,
                new MemoryStream(bytes[128..]),
                bytes.Length - 128,
                new ContentRangeHeaderValue(128, bytes.Length - 1, bytes.Length));
        });
        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => null,
            maxRetries: 3);
        using var output = new MemoryStream();

        var result = await relay.RelayAsync(
            output,
            rangeHeader: null,
            writeHeadersAsync: null,
            CancellationToken.None);

        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(3, result.UpstreamAttempts);
    }

    [Fact]
    public async Task RelayAsync_ResumesTheSameRange_WhenUpstreamStopsSendingData()
    {
        var bytes = Enumerable.Range(0, 512).Select(i => (byte)(i % 251)).ToArray();
        var calls = 0;
        var handler = new SequenceHandler(request =>
        {
            calls++;
            if (calls == 1)
            {
                return CreateResponse(
                    HttpStatusCode.OK,
                    new StallAfterStream(bytes, stallAfterBytes: 128),
                    bytes.Length,
                    contentRange: null);
            }

            Assert.Equal(128, request.Headers.Range?.Ranges.Single().From);
            return CreateResponse(
                HttpStatusCode.PartialContent,
                new MemoryStream(bytes[128..]),
                bytes.Length - 128,
                new ContentRangeHeaderValue(128, bytes.Length - 1, bytes.Length));
        });
        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => null,
            maxRetries: 2,
            upstreamIdleTimeout: TimeSpan.FromMilliseconds(50));
        using var output = new MemoryStream();

        var result = await relay.RelayAsync(
            output,
            rangeHeader: null,
            writeHeadersAsync: null,
            CancellationToken.None);

        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(2, result.UpstreamAttempts);
    }

    [Fact]
    public async Task RelayAsync_RejectsMismatchedPartialContentRange()
    {
        var handler = new SequenceHandler(_ => CreateResponse(
            HttpStatusCode.PartialContent,
            new MemoryStream(new byte[10]),
            contentLength: 10,
            new ContentRangeHeaderValue(99, 108, 200)));
        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => null,
            maxRetries: 0);
        using var output = new MemoryStream();

        var error = await Assert.ThrowsAsync<IOException>(() => relay.RelayAsync(
            output,
            "bytes=100-",
            writeHeadersAsync: null,
            CancellationToken.None));

        Assert.Contains("expected byte 100", error.Message, StringComparison.Ordinal);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task RelayAsync_HeadUsesHeadUpstreamAndDoesNotCopyResponseBody()
    {
        var handler = new SequenceHandler(request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            return CreateResponse(
                HttpStatusCode.OK,
                new MemoryStream(new byte[] { 1, 2, 3 }),
                contentLength: 3,
                contentRange: null);
        });
        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => null);
        using var output = new MemoryStream();

        var result = await relay.RelayAsync(
            output,
            rangeHeader: null,
            writeHeadersAsync: null,
            CancellationToken.None,
            headOnly: true);

        Assert.Empty(output.ToArray());
        Assert.Equal(0, result.BytesWritten);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RelayAsync_DoesNotRetryWhenDownstreamWriteFails()
    {
        var handler = new SequenceHandler(_ => CreateResponse(
            HttpStatusCode.OK,
            new MemoryStream(new byte[] { 1, 2, 3 }),
            contentLength: 3,
            contentRange: null));
        var relay = new DirectStreamRelay(
            new HttpClient(handler),
            new Uri("https://example.test/api/v1/stream/session"),
            () => null,
            maxRetries: 5);
        using var output = new ThrowOnWriteStream();

        await Assert.ThrowsAnyAsync<IOException>(() => relay.RelayAsync(
            output,
            rangeHeader: null,
            writeHeadersAsync: null,
            CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    private static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode,
        Stream stream,
        long contentLength,
        ContentRangeHeaderValue? contentRange)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StreamContent(stream)
        };

        response.Headers.AcceptRanges.Add("bytes");
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        response.Content.Headers.ContentLength = contentLength;
        response.Content.Headers.ContentRange = contentRange;
        return response;
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public SequenceHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class ThrowAfterStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly int _throwAfterBytes;
        private int _position;

        public ThrowAfterStream(byte[] bytes, int throwAfterBytes)
        {
            _bytes = bytes;
            _throwAfterBytes = throwAfterBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _bytes.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _throwAfterBytes)
            {
                throw new IOException("Simulated upstream disconnect.");
            }

            var available = Math.Min(count, _throwAfterBytes - _position);
            Array.Copy(_bytes, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _throwAfterBytes)
            {
                throw new IOException("Simulated upstream disconnect.");
            }

            var available = Math.Min(buffer.Length, _throwAfterBytes - _position);
            _bytes.AsMemory(_position, available).CopyTo(buffer);
            _position += available;
            return ValueTask.FromResult(available);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StallAfterStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly int _stallAfterBytes;
        private int _position;

        public StallAfterStream(byte[] bytes, int stallAfterBytes)
        {
            _bytes = bytes;
            _stallAfterBytes = stallAfterBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position >= _stallAfterBytes)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }

            var available = Math.Min(buffer.Length, _stallAfterBytes - _position);
            _bytes.AsMemory(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowOnWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new IOException("Simulated downstream disconnect.");

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Simulated downstream disconnect."));

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated downstream disconnect.");

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Simulated downstream disconnect."));
    }
}
