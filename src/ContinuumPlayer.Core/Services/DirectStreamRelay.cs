using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace ContinuumPlayer.Core.Services;

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

    private readonly HttpClient _httpClient;
    private readonly Uri _remoteUri;
    private readonly Func<string?> _accessTokenProvider;
    private readonly Action<string>? _log;
    private readonly int _maxRetries;

    public DirectStreamRelay(
        HttpClient httpClient,
        Uri remoteUri,
        Func<string?> accessTokenProvider,
        Action<string>? log = null,
        int maxRetries = DefaultMaxRetries)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries));

        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _remoteUri = remoteUri ?? throw new ArgumentNullException(nameof(remoteUri));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _log = log;
        _maxRetries = maxRetries;
    }

    public async Task<DirectStreamRelayResult> RelayAsync(
        Stream output,
        string? rangeHeader,
        Func<DirectStreamRelayHeaders, CancellationToken, Task>? writeHeadersAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);

        var requestedRange = TryParseRange(rangeHeader);
        var nextOffset = requestedRange?.From ?? 0;
        var endOffset = requestedRange?.To;
        var bytesWritten = 0L;
        var attempts = 0;
        var headersWritten = false;
        var firstStatusCode = HttpStatusCode.OK;

        while (true)
        {
            attempts++;

            try
            {
                using var request = CreateRequest(nextOffset, endOffset, requestedRange is not null);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (nextOffset > 0 && response.StatusCode == HttpStatusCode.OK)
                    throw new IOException("Upstream ignored the resume range request.");

                if (!headersWritten)
                {
                    firstStatusCode = response.StatusCode;
                    if (writeHeadersAsync is not null)
                    {
                        await writeHeadersAsync(CreateHeaders(response), cancellationToken);
                    }

                    headersWritten = true;
                }

                var expectedBytes = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                var bytesThisAttempt = await CopyToAsync(input, output, cancellationToken);

                bytesWritten += bytesThisAttempt;
                nextOffset += bytesThisAttempt;

                if (!response.IsSuccessStatusCode)
                    return new DirectStreamRelayResult(firstStatusCode, bytesWritten, attempts);

                if (!expectedBytes.HasValue || bytesThisAttempt >= expectedBytes.Value)
                    return new DirectStreamRelayResult(firstStatusCode, bytesWritten, attempts);

                if (attempts > _maxRetries)
                {
                    throw new IOException(
                        $"Upstream ended after {bytesThisAttempt} bytes; expected {expectedBytes.Value} bytes.");
                }

                _log?.Invoke(
                    $"Direct stream retry {attempts}/{_maxRetries} from byte {nextOffset}: upstream ended early after {bytesThisAttempt} bytes.");
            }
            catch (DirectStreamReadException ex) when (!cancellationToken.IsCancellationRequested && attempts <= _maxRetries)
            {
                bytesWritten += ex.BytesCopied;
                nextOffset += ex.BytesCopied;
                _log?.Invoke(
                    $"Direct stream retry {attempts}/{_maxRetries} from byte {nextOffset}: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
            }
            catch (Exception ex) when (!headersWritten && !cancellationToken.IsCancellationRequested && IsTransient(ex) && attempts <= _maxRetries)
            {
                _log?.Invoke(
                    $"Direct stream retry {attempts}/{_maxRetries} from byte {nextOffset}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private HttpRequestMessage CreateRequest(long from, long? to, bool rangeWasRequested)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _remoteUri);
        var token = _accessTokenProvider();

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (from > 0 || rangeWasRequested)
            request.Headers.Range = new RangeHeaderValue(from, to);

        return request;
    }

    private static DirectStreamRelayHeaders CreateHeaders(HttpResponseMessage response)
    {
        return new DirectStreamRelayHeaders(
            response.StatusCode,
            response.ReasonPhrase ?? response.StatusCode.ToString(),
            response.Content.Headers.ContentType?.ToString(),
            response.Content.Headers.ContentLength,
            response.Content.Headers.ContentRange?.ToString(),
            response.Headers.AcceptRanges.Any(value => string.Equals(value, "bytes", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<long> CopyToAsync(Stream input, Stream output, CancellationToken cancellationToken)
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
                    read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken);
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    throw new DirectStreamReadException(total, ex);
                }

                if (read == 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
            }

            await output.FlushAsync(cancellationToken);
            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
        return ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException;
    }

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
}
