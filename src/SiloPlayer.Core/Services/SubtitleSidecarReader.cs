using System.Diagnostics;
using System.Net;

namespace SiloPlayer.Core.Services;

public enum SubtitleSidecarReadOutcome { Ready, Fallback, Busy, Error }
public sealed record SubtitleSidecarRead(SubtitleSidecarReadOutcome Outcome, byte[]? Bytes = null, string? Extension = null, bool WasBusy = false);

/// <summary>Bounded text-sidecar reads. Only a subtitle read's 503 is retried.</summary>
public static class SubtitleSidecarReader
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(250);
    private const int MaxAttempts = 4;

    public static async Task<SubtitleSidecarRead> ReadAsync(HttpClient http, Uri url, string? codec, CancellationToken ct)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || SubtitleSidecarFile.Extension(null, url, codec) == null)
            return new(SubtitleSidecarReadOutcome.Fallback);
        var busy = false;
        // Fallback is only for formats outside this reader. A failed text
        // read must never retry the same URL through synchronous mpv sub-add.
        SubtitleSidecarRead Failure() => new(busy ? SubtitleSidecarReadOutcome.Busy : SubtitleSidecarReadOutcome.Error, WasBusy: busy);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(Budget);
        var clock = Stopwatch.StartNew();
        try
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                // Use the advertised URL verbatim. Account headers belong to
                // API requests, never to a server-advertised CDN/worker URL.
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    busy = true;
                    if (attempt == MaxAttempts - 1) return Failure();
                    var retry = response.Headers.RetryAfter;
                    var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : DefaultDelay);
                    if (delay < MinimumDelay) delay = MinimumDelay;
                    // Do not truncate Retry-After and retry before the server
                    // permits it. A delay beyond this operation's budget fails.
                    if (delay >= Budget - clock.Elapsed) return Failure();
                    response.Dispose();
                    await Task.Delay(delay, bounded.Token).ConfigureAwait(false);
                    continue;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > SubtitleSidecarFile.MaxBytes) return Failure();
                var extension = SubtitleSidecarFile.Extension(response.Content.Headers.ContentType?.MediaType, url, codec);
                if (extension == null) return Failure();
                using var buffer = new MemoryStream();
                await using var body = await response.Content.ReadAsStreamAsync(bounded.Token).ConfigureAwait(false);
                var chunk = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(chunk, bounded.Token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > SubtitleSidecarFile.MaxBytes) return Failure();
                    buffer.Write(chunk, 0, read);
                }
                if (buffer.Length == 0) return Failure();
                return new(SubtitleSidecarReadOutcome.Ready, buffer.ToArray(), extension, busy);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failure(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { return Failure(); }
        ct.ThrowIfCancellationRequested();
        return Failure();
    }
}
