using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace SiloPlayer.Core.Services;

public sealed record DirectStreamRelayHeaders(
    HttpStatusCode StatusCode,
    string ReasonPhrase,
    string? ContentType,
    long? ContentLength,
    string? ContentRange,
    bool AcceptRanges);

public sealed record DirectStreamRelayResult(
    HttpStatusCode StatusCode,
    long BytesWritten,
    int UpstreamAttempts);

public sealed class DirectStreamRelay
{
    private const int BufferSize = 128 * 1024;
    private const int DefaultMaxRetries = 50;
    private const int MaxInitialStatusRetries = 5;

    private readonly HttpClient _httpClient;
    private readonly Uri _remoteUri;
    private readonly Func<string?> _accessTokenProvider;
    private readonly Action<string>? _log;
    private readonly int _maxRetries;
    private readonly bool _supportsRanges;
    private readonly TimeSpan _upstreamIdleTimeout;
    private string? _strongEntityTag;
    private int _activeRecoveries;
    private long _recoveryStartedTimestamp;

    public DirectStreamRelay(
        HttpClient httpClient,
        Uri remoteUri,
        Func<string?> accessTokenProvider,
        Action<string>? log = null,
        int maxRetries = DefaultMaxRetries,
        bool supportsRanges = true,
        TimeSpan? upstreamIdleTimeout = null)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries));

        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _remoteUri = remoteUri ?? throw new ArgumentNullException(nameof(remoteUri));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _log = log;
        _maxRetries = maxRetries;
        _supportsRanges = supportsRanges;
        _upstreamIdleTimeout = upstreamIdleTimeout ?? DirectPlaybackRecoveryPolicy.UpstreamIdleTimeout;
        if (_upstreamIdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(upstreamIdleTimeout));
    }

    /// <summary>Gets whether at least one relay request is reconnecting upstream.</summary>
    public bool IsRecovering => Volatile.Read(ref _activeRecoveries) > 0;

    /// <summary>Gets the elapsed time of the oldest active upstream reconnect.</summary>
    public TimeSpan? RecoveryElapsed
    {
        get
        {
            if (!IsRecovering)
                return null;

            var startedTimestamp = Volatile.Read(ref _recoveryStartedTimestamp);
            return startedTimestamp > 0
                ? Stopwatch.GetElapsedTime(startedTimestamp)
                : TimeSpan.Zero;
        }
    }

    public async Task<DirectStreamRelayResult> RelayAsync(
        Stream output,
        string? rangeHeader,
        Func<DirectStreamRelayHeaders, CancellationToken, Task>? writeHeadersAsync,
        CancellationToken cancellationToken,
        bool headOnly = false)
    {
        ArgumentNullException.ThrowIfNull(output);

        var requestedRange = _supportsRanges ? TryParseRange(rangeHeader) : null;
        var nextOffset = requestedRange?.From ?? 0;
        var endOffset = requestedRange?.To;
        var bytesWritten = 0L;
        var attempts = 0;
        var headersWritten = false;
        var firstStatusCode = HttpStatusCode.OK;
        var recoveryRegistered = false;

        void BeginRecovery()
        {
            if (!_supportsRanges || recoveryRegistered)
                return;

            if (Interlocked.Increment(ref _activeRecoveries) == 1)
                Volatile.Write(ref _recoveryStartedTimestamp, Stopwatch.GetTimestamp());
            recoveryRegistered = true;
        }

        void CompleteRecovery()
        {
            if (!recoveryRegistered)
                return;

            recoveryRegistered = false;
            if (Interlocked.Decrement(ref _activeRecoveries) == 0)
                Volatile.Write(ref _recoveryStartedTimestamp, 0);
        }

        try
        {
            while (true)
            {
                attempts++;

                try
                {
                    var resumeEntityTag = Volatile.Read(ref _strongEntityTag);
                    using var request = CreateRequest(
                        nextOffset,
                        endOffset,
                        requestedRange is not null,
                        headOnly,
                        resumeEntityTag);
                    using var response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    // CDN edges and a just-started server stream can briefly return
                    // 404/429/5xx before media bytes are available. Retry these
                    // before exposing headers to mpv; after headers or body bytes
                    // have been sent, only byte-range continuation is safe.
                    if (!headersWritten && bytesWritten == 0 &&
                        ShouldRetryInitialStatus(response.StatusCode) &&
                        attempts <= Math.Min(_maxRetries, MaxInitialStatusRetries))
                    {
                        _log?.Invoke(
                            $"Direct stream initial HTTP {(int)response.StatusCode}; retry {attempts}/{Math.Min(_maxRetries, MaxInitialStatusRetries)}.");
                        BeginRecovery();
                        await DelayBeforeRetryAsync(attempts, cancellationToken);
                        continue;
                    }

                    // A strong If-Range validator returning 200 means the source
                    // entity changed. Never splice bytes from the replacement into
                    // mpv's active representation; close this relay so the player
                    // can perform its normal full-session recovery.
                    var responseEntityTag = response.Headers.ETag is { IsWeak: false } strongTag
                        ? strongTag.ToString()
                        : null;
                    if (_supportsRanges &&
                        !string.IsNullOrWhiteSpace(resumeEntityTag) &&
                        (nextOffset > 0 || requestedRange is not null) &&
                        (response.StatusCode == HttpStatusCode.OK ||
                         (!string.IsNullOrWhiteSpace(responseEntityTag) &&
                          !string.Equals(responseEntityTag, resumeEntityTag, StringComparison.Ordinal))))
                    {
                        throw new DirectStreamEntityChangedException();
                    }
                    if (!string.IsNullOrWhiteSpace(responseEntityTag))
                        Volatile.Write(ref _strongEntityTag, responseEntityTag);

                    await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var initialByte = -1;
                    if (!_supportsRanges && !headOnly && response.IsSuccessStatusCode)
                    {
                        initialByte = await ReadInitialByteAsync(
                            input,
                            _upstreamIdleTimeout,
                            cancellationToken);
                        if (initialByte < 0)
                        {
                            if (!headersWritten && bytesWritten == 0 &&
                                attempts <= Math.Min(_maxRetries, MaxInitialStatusRetries))
                            {
                                _log?.Invoke(
                                    $"Direct stream returned no media bytes; retry {attempts}/{Math.Min(_maxRetries, MaxInitialStatusRetries)}.");
                                await DelayBeforeRetryAsync(attempts, cancellationToken);
                                continue;
                            }

                            throw new IOException("Upstream returned a successful response without any media bytes.");
                        }
                    }

                    // An origin is allowed to ignore a client's initial Range request and
                    // return the complete representation with 200. It is not safe to accept
                    // that response after bytes have already been relayed, because doing so
                    // would append the representation from byte zero and corrupt playback.
                    if (_supportsRanges && bytesWritten > 0 && response.StatusCode == HttpStatusCode.OK)
                        throw new IOException("Upstream ignored the resume range request.");

                    if (_supportsRanges &&
                        response.StatusCode == HttpStatusCode.PartialContent &&
                        response.Content.Headers.ContentRange?.From != nextOffset)
                    {
                        throw new IOException(
                            $"Upstream returned an invalid resume range; expected byte {nextOffset}.");
                    }

                    if (!headersWritten)
                    {
                        firstStatusCode = response.StatusCode;
                        if (writeHeadersAsync is not null)
                        {
                            try
                            {
                                await writeHeadersAsync(CreateHeaders(response), cancellationToken);
                            }
                            catch (Exception ex) when (IsTransient(ex))
                            {
                                throw new DirectStreamWriteException(ex);
                            }
                        }

                        headersWritten = true;
                    }

                    if (headOnly)
                        return new DirectStreamRelayResult(firstStatusCode, bytesWritten, attempts);

                    var expectedBytes = response.Content.Headers.ContentLength;
                    if (initialByte >= 0)
                        await output.WriteAsync(
                            new byte[] { (byte)initialByte },
                            cancellationToken);
                    var bytesThisAttempt = await CopyToAsync(
                        input,
                        output,
                        _upstreamIdleTimeout,
                        cancellationToken,
                        recoveryRegistered ? CompleteRecovery : null) + (initialByte >= 0 ? 1 : 0);

                    bytesWritten += bytesThisAttempt;
                    nextOffset += bytesThisAttempt;

                    if (!response.IsSuccessStatusCode)
                        return new DirectStreamRelayResult(firstStatusCode, bytesWritten, attempts);

                    if (!expectedBytes.HasValue || bytesThisAttempt >= expectedBytes.Value)
                        return new DirectStreamRelayResult(firstStatusCode, bytesWritten, attempts);

                    if (!_supportsRanges)
                    {
                        throw new IOException(
                            $"Sequential upstream ended after {bytesThisAttempt} bytes; expected {expectedBytes.Value} bytes.");
                    }

                    if (attempts > _maxRetries)
                    {
                        throw new IOException(
                            $"Upstream ended after {bytesThisAttempt} bytes; expected {expectedBytes.Value} bytes.");
                    }

                    _log?.Invoke(
                        $"Direct stream retry {attempts}/{_maxRetries} from byte {nextOffset}: upstream ended early after {bytesThisAttempt} bytes.");
                    BeginRecovery();
                    await DelayBeforeRetryAsync(attempts, cancellationToken);
                }
                catch (DirectStreamReadException ex) when (_supportsRanges && !cancellationToken.IsCancellationRequested && attempts <= _maxRetries)
                {
                    bytesWritten += ex.BytesCopied;
                    nextOffset += ex.BytesCopied;
                    _log?.Invoke(
                        $"Direct stream retry {attempts}/{_maxRetries} from byte {nextOffset}: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
                    BeginRecovery();
                    await DelayBeforeRetryAsync(attempts, cancellationToken);
                }
                catch (Exception ex) when (_supportsRanges && !cancellationToken.IsCancellationRequested && IsTransient(ex) && attempts <= _maxRetries)
                {
                    _log?.Invoke(
                        $"Direct stream retry {attempts}/{_maxRetries} from byte {nextOffset}: {ex.GetType().Name}: {ex.Message}");
                    BeginRecovery();
                    await DelayBeforeRetryAsync(attempts, cancellationToken);
                }
            }
        }
        finally
        {
            CompleteRecovery();
        }
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        // Give a CDN edge, tunnel, or Wi-Fi handoff time to recover instead of
        // spending the entire retry budget in a tight loop. The first retry is
        // still near-immediate; sustained failures back off to 800 ms.
        var shift = Math.Clamp(attempt - 1, 0, 3);
        var delayMs = 100 * (1 << shift);
        return Task.Delay(delayMs, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(
        long from,
        long? to,
        bool rangeWasRequested,
        bool headOnly,
        string? resumeEntityTag)
    {
        var request = new HttpRequestMessage(headOnly ? HttpMethod.Head : HttpMethod.Get, _remoteUri)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        var token = _accessTokenProvider();

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (_supportsRanges && (from > 0 || rangeWasRequested))
        {
            request.Headers.Range = new RangeHeaderValue(from, to);
            if (!string.IsNullOrWhiteSpace(resumeEntityTag))
                request.Headers.TryAddWithoutValidation("If-Range", resumeEntityTag);
        }

        return request;
    }

    private DirectStreamRelayHeaders CreateHeaders(HttpResponseMessage response)
    {
        return new DirectStreamRelayHeaders(
            response.StatusCode,
            response.ReasonPhrase ?? response.StatusCode.ToString(),
            response.Content.Headers.ContentType?.ToString(),
            response.Content.Headers.ContentLength,
            response.Content.Headers.ContentRange?.ToString(),
            _supportsRanges &&
            (response.StatusCode == HttpStatusCode.PartialContent ||
             response.Headers.AcceptRanges.Any(value =>
                 string.Equals(value, "bytes", StringComparison.OrdinalIgnoreCase))));
    }

    private static async Task<long> CopyToAsync(
        Stream input,
        Stream output,
        TimeSpan upstreamIdleTimeout,
        CancellationToken cancellationToken,
        Action? onFirstMediaBytes = null)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var total = 0L;

        try
        {
            while (true)
            {
                int read;
                try
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readTimeout.CancelAfter(upstreamIdleTimeout);
                    try
                    {
                        read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), readTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"Upstream sent no media data for {upstreamIdleTimeout.TotalSeconds:0} seconds.");
                    }
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    throw new DirectStreamReadException(total, ex);
                }

                if (read == 0)
                    break;

                onFirstMediaBytes?.Invoke();
                onFirstMediaBytes = null;

                try
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    throw new DirectStreamWriteException(ex);
                }
                total += read;
            }

            try
            {
                await output.FlushAsync(cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                throw new DirectStreamWriteException(ex);
            }
            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<int> ReadInitialByteAsync(
        Stream input,
        TimeSpan upstreamIdleTimeout,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(upstreamIdleTimeout);
        try
        {
            return await input.ReadAsync(buffer, readTimeout.Token) == 0 ? -1 : buffer[0];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Upstream sent no media data for {upstreamIdleTimeout.TotalSeconds:0} seconds.");
        }
    }

    private static RangeRequest? TryParseRange(string? rangeHeader)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader) ||
            !RangeHeaderValue.TryParse(rangeHeader, out var range) ||
            range.Ranges.Count != 1)
        {
            return null;
        }

        var rangeItem = range.Ranges.Single();
        if (!rangeItem.From.HasValue)
            return null;

        return new RangeRequest(rangeItem.From.Value, rangeItem.To);
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is not DirectStreamWriteException &&
               ex is not DirectStreamEntityChangedException &&
               (ex is HttpRequestException or IOException or TaskCanceledException or
                   OperationCanceledException or TimeoutException);
    }

    private static bool ShouldRetryInitialStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.NotFound or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private sealed record RangeRequest(long From, long? To);

    private sealed class DirectStreamReadException : IOException
    {
        public DirectStreamReadException(long bytesCopied, Exception innerException)
            : base("The upstream stream failed while reading.", innerException)
        {
            BytesCopied = bytesCopied;
        }

        public long BytesCopied { get; }
    }

    private sealed class DirectStreamWriteException : IOException
    {
        public DirectStreamWriteException(Exception innerException)
            : base("The downstream stream failed while writing.", innerException)
        {
        }
    }

    private sealed class DirectStreamEntityChangedException : IOException
    {
        public DirectStreamEntityChangedException()
            : base("The direct-play source changed while playback was active.")
        {
        }
    }
}
