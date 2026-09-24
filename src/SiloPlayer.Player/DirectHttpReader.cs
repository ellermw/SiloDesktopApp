using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace SiloPlayer.Player;

/// <summary>Seekable original-file reads. Owns HTTP connections, not playback or decoding.</summary>
internal sealed class DirectHttpReader : IDisposable
{
    private const int RangeSize = 32 * 1024 * 1024;
    private const double ProgressWindowSeconds = 5;
    private HttpClient _client;
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly Uri _uri;
    private readonly Func<double> _duration;
    private readonly CancellationTokenSource _stop = new();
    private HttpResponseMessage? _response;
    private Stream? _body;
    private long _length = -1;
    private long _end;
    private string? _entityTag;
    private double _readWaitSeconds;
    private long _progressBytes;
    private int _failures;
    private readonly PlaybackDiagnostics? _diagnostics;
    private long _requestNumber, _requestFrom, _requestTo, _totalBytes, _responseBytes, _reads;
    private long _lastProgress = Stopwatch.GetTimestamp(), _ioStarted, _lastIoEnd = Stopwatch.GetTimestamp();
    private double _bodyWaitMs, _headerMs, _longestReadMs;
    private string _phase = "idle";
    private int _pool;
    private static int s_nextPool;
    private static long s_nextRequest;
    private readonly long? _streamExpires;
    private long _requestStarted;
    public long Position { get; private set; }
    public bool IsCanceled => _stop.IsCancellationRequested;

    public DirectHttpReader(string url, Func<double> duration, Func<HttpMessageHandler>? handlerFactory = null)
        : this(url, duration, handlerFactory, null) { }

    public DirectHttpReader(string url, Func<double> duration, Func<HttpMessageHandler>? handlerFactory, PlaybackDiagnostics? diagnostics)
    {
        _uri = new Uri(url);
        _streamExpires = ReadExpiry(_uri);
        _duration = duration;
        _diagnostics = diagnostics;
        _handlerFactory = handlerFactory ?? (() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            // Reuse the healthy connection across bounded responses. HTTP/2 is
            // preferred when available; HTTP/1.1 keeps its connection as well.
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        });
        _client = CreateClient();
    }

    private HttpClient CreateClient()
    {
        _pool = Interlocked.Increment(ref s_nextPool);
        _diagnostics?.Record("transport_pool", new { pool = _pool });
        return new(_handlerFactory()) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static long? ReadExpiry(Uri uri)
    {
        try
        {
            var token = uri.Segments.LastOrDefault()?.Split('.');
            if (token?.Length != 3 || token[1].Length > 16384) return null;
            var payload = token[1].Replace('-', '+').Replace('_', '/');
            using var json = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
            return json.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var value) ? value : null;
        }
        catch { return null; }
    }

    internal object DiagnosticSnapshot() => new {
        request = _requestNumber, pool = _pool, phase = _phase, position = Position, length = _length, streamExpiresUnix = _streamExpires,
        rangeFrom = _requestFrom, rangeTo = _requestTo, responseBytes = _responseBytes, totalBytes = _totalBytes,
        reads = _reads, headersMs = _headerMs, bodyWaitMs = _bodyWaitMs, longestReadMs = _longestReadMs,
        pendingIoMs = _phase is "headers" or "body" ? Stopwatch.GetElapsedTime(_ioStarted).TotalMilliseconds : 0,
        consumerIdleMs = _phase == "idle" ? Stopwatch.GetElapsedTime(_lastIoEnd).TotalMilliseconds : 0,
        sinceLastByteMs = Stopwatch.GetElapsedTime(_lastProgress).TotalMilliseconds,
        usefulBytes = _progressBytes, budgetRemainingMs = Math.Max(0, ProgressWindowSeconds - _readWaitSeconds -
            (_phase is "headers" or "body" ? Stopwatch.GetElapsedTime(_ioStarted).TotalSeconds : 0)) * 1000,
        consecutiveFailures = _failures, requiredBytesPerSecond = _duration() > 0 && _length > 0 ? Math.Max(16384, _length / _duration()) : 65536
    };

    private void BeginIo(string phase) { _ioStarted = Stopwatch.GetTimestamp(); Volatile.Write(ref _phase, phase); }
    private void EndIo() { _lastIoEnd = Stopwatch.GetTimestamp(); Volatile.Write(ref _phase, "idle"); }
    private void Failure(Exception error, string stage, bool terminal)
    {
        _diagnostics?.Record("read_failure", new { request = _requestNumber, pool = _pool, stage, terminal,
            kind = error is OperationCanceledException ? (IsCanceled ? "cancelled" : "progress_deadline") : error.GetType().Name,
            httpError = error is HttpRequestException http ? http.HttpRequestError.ToString() : null,
            causes = ErrorCodes(error),
            state = DiagnosticSnapshot() });
        if (!IsCanceled) _diagnostics?.Incident(terminal ? "direct_read_failed" : "direct_read_retry");
    }

    private static object[] ErrorCodes(Exception error)
    {
        var causes = new List<object>();
        for (Exception? current = error; current != null && causes.Count < 8; current = current.InnerException)
            causes.Add(new { type = current.GetType().Name, hresult = current.HResult,
                socketError = current is System.Net.Sockets.SocketException socket ? socket.SocketErrorCode.ToString() : null,
                nativeError = current is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : (int?)null });
        return causes.ToArray();
    }

    public long Length
    {
        get { if (_length < 0) EnsureResponseAsync().GetAwaiter().GetResult(); return _length; }
    }

    public int Read(byte[] buffer, int count) => ReadAsync(buffer.AsMemory(0, count)).GetAwaiter().GetResult();

    private async Task<int> ReadAsync(Memory<byte> buffer)
    {
        while (true)
        {
            _stop.Token.ThrowIfCancellationRequested();
            if (_length >= 0 && Position >= _length) return 0;
            try
            {
                await EnsureResponseAsync().ConfigureAwait(false);
                var duration = _duration();
                var minimumRate = duration > 0 && _length > 0
                    ? Math.Max(16 * 1024, _length / duration)
                    : 64 * 1024;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(0.001, ProgressWindowSeconds - _readWaitSeconds)));
                var started = Stopwatch.GetTimestamp();
                int read;
                BeginIo("body");
                try
                {
                    read = await _body!.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _end - Position)], deadline.Token).ConfigureAwait(false);
                }
                finally
                {
                    var waited = Stopwatch.GetElapsedTime(started);
                    _readWaitSeconds += waited.TotalSeconds;
                    _bodyWaitMs += waited.TotalMilliseconds;
                    _longestReadMs = Math.Max(_longestReadMs, waited.TotalMilliseconds);
                    EndIo();
                }
                if (read == 0)
                {
                    _diagnostics?.Record("response_truncated", DiagnosticSnapshot());
                    throw new IOException("Direct response ended before its declared range.");
                }
                Position += read;
                if (_responseBytes == 0) _diagnostics?.Record("first_byte", new { request = _requestNumber, pool = _pool,
                    elapsedMs = Stopwatch.GetElapsedTime(_requestStarted).TotalMilliseconds, count = read });
                _responseBytes += read;
                _totalBytes += read;
                _reads++;
                _lastProgress = Stopwatch.GetTimestamp();
                _progressBytes += read;
                // Count time waiting for headers and reads, not time mpv is
                // paused or its demux cache is full. Tiny reads cannot restart
                // the deadline, and useful bytes are never discarded on retry.
                if (_progressBytes >= minimumRate * ProgressWindowSeconds)
                {
                    _progressBytes = 0;
                    _readWaitSeconds = 0;
                    _failures = 0;
                }
                if (Position == _end) { _diagnostics?.Record("range_complete", DiagnosticSnapshot()); CloseResponse(); }
                return read;
            }
            catch (Exception ex) when (IsRetryable(ex) && !IsCanceled)
            {
                Failure(ex, "body", terminal: false);
                await RetryAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { Failure(ex, "read", terminal: true); throw; }
        }
    }

    private async Task EnsureResponseAsync()
    {
        while (_body == null)
        {
            _stop.Token.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _uri)
                {
                    Version = HttpVersion.Version20,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                };
                var last = _length < 0 ? Position + RangeSize - 1 : Math.Min(Position + RangeSize, _length) - 1;
                request.Headers.Range = new RangeHeaderValue(Position, last);
                _requestNumber = Interlocked.Increment(ref s_nextRequest);
                _requestStarted = Stopwatch.GetTimestamp();
                _headerMs = 0;
                _requestFrom = Position; _requestTo = last; _responseBytes = 0; _bodyWaitMs = 0; _longestReadMs = 0;
                _diagnostics?.Record("request_start", DiagnosticSnapshot());
                using var trace = _diagnostics == null ? null : PlaybackNetworkDiagnostics.Request(_diagnostics, _requestNumber, _pool);
                if (_entityTag != null) request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(_entityTag));
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                // Header and body waits consume the same progress budget.
                // Giving every range a fresh header deadline lets repeated
                // slow responses drain the cache without timely recovery.
                deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(0.001, ProgressWindowSeconds - _readWaitSeconds)));
                var started = Stopwatch.GetTimestamp();
                HttpResponseMessage response;
                BeginIo("headers");
                try
                {
                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                }
                finally { _headerMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; _readWaitSeconds += _headerMs / 1000; EndIo(); }
                try
                {
                    var status = (int)response.StatusCode;
                    _diagnostics?.Record("response_headers", new { request = _requestNumber, pool = _pool, status,
                        protocol = response.Version.ToString(), elapsedMs = _headerMs,
                        from = response.Content.Headers.ContentRange?.From, to = response.Content.Headers.ContentRange?.To,
                        length = response.Content.Headers.ContentRange?.Length, contentLength = response.Content.Headers.ContentLength,
                        etagPresent = response.Headers.ETag != null });
                    if (status is 404 or 408 or 429 || status >= 500)
                        throw new HttpRequestException("Temporary direct stream response.");
                    // Never splice an error page, a replacement entity, or an
                    // ignored Range response into the decoder's existing bytes.
                    var range = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent ||
                        range?.Unit != "bytes" || range.From != Position ||
                        range.Length is not > 0 || range.To != Math.Min(last, range.Length.Value - 1) ||
                        (_length >= 0 && range.Length != _length))
                    {
                        _diagnostics?.Record("response_rejected", new { request = _requestNumber,
                            reason = status is 401 or 403 ? "authorization_rejected" : status != 206 ? "range_not_honored" : "inconsistent_range" });
                        throw new InvalidDataException("Direct stream returned an inconsistent byte range.");
                    }
                    var tag = response.Headers.ETag is { IsWeak: false } etag ? etag.Tag : null;
                    if (_entityTag != null && tag != null && tag != _entityTag)
                    {
                        _diagnostics?.Record("response_rejected", new { request = _requestNumber, reason = "entity_changed" });
                        throw new InvalidDataException("Direct stream entity changed.");
                    }
                    _entityTag ??= tag;
                    _length = range.Length.Value;
                    _end = range.To.Value + 1;
                    _body = await response.Content.ReadAsStreamAsync(_stop.Token).ConfigureAwait(false);
                    _response = response;
                }
                catch { response.Dispose(); throw; }
            }
            catch (Exception ex) when (IsRetryable(ex) && !IsCanceled)
            {
                Failure(ex, "headers", terminal: false);
                await RetryAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { Failure(ex, "headers_or_validation", terminal: true); throw; }
        }
    }

    private static bool IsRetryable(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or IOException;

    private async Task RetryAsync()
    {
        _diagnostics?.Record("retry", new { request = _requestNumber, pool = _pool, resumeByte = Position, attempt = _failures + 1 });
        CloseResponse();
        // Cancelling an HTTP/2 body resets its stream, not its pooled TCP
        // connection. A failed connection must not survive into the retry.
        _client.Dispose();
        _client = CreateClient();
        _readWaitSeconds = 0;
        _progressBytes = 0;
        if (++_failures > 8) throw new InvalidDataException("Direct stream recovery exhausted.");
        Volatile.Write(ref _phase, "retry_delay");
        try { await Task.Delay(Math.Min(_failures * 100, 500), _stop.Token).ConfigureAwait(false); }
        finally { EndIo(); }
    }

    public long Seek(long position)
    {
        _stop.Token.ThrowIfCancellationRequested();
        if (position < 0 || (_length >= 0 && position > _length)) return -1;
        if (position != Position)
        {
            _diagnostics?.Record("byte_seek", new { from = Position, to = position });
            CloseResponse();
            Position = position;
            _readWaitSeconds = 0;
            _progressBytes = 0;
            _failures = 0;
        }
        return Position;
    }

    private void CloseResponse()
    {
        _body?.Dispose();
        _body = null;
        _response?.Dispose();
        _response = null;
    }

    public void Cancel() { _diagnostics?.Record("reader_cancel", DiagnosticSnapshot()); try { _ = _stop.CancelAsync(); } catch (ObjectDisposedException) { } }
    public void Dispose() { Cancel(); CloseResponse(); _client.Dispose(); _stop.Dispose(); Volatile.Write(ref _phase, "closed"); _diagnostics?.Record("reader_closed", DiagnosticSnapshot()); }
}
