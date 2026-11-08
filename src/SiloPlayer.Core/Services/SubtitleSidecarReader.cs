using System.Diagnostics;
using System.Net;

namespace SiloPlayer.Core.Services;

public enum SubtitleSidecarReadOutcome { Ready, Fallback, Busy, Error }
public enum SubtitleSidecarReadFailure { None, DownloadLimit, Timeout }
public sealed record SubtitleSidecarRead(SubtitleSidecarReadOutcome Outcome, byte[]? Bytes = null, string? Extension = null,
    bool WasBusy = false, SubtitleSidecarReadFailure Failure = SubtitleSidecarReadFailure.None)
{
    // Error describes this bounded prefetch, not refusal of the playback
    // choice. Only a non-busy failure may try the longer disk-streaming URL read.
    public bool CanUseUrlFallback => !WasBusy && Outcome is SubtitleSidecarReadOutcome.Error or SubtitleSidecarReadOutcome.Fallback;
}

/// <summary>Bounded text-sidecar reads. Only a subtitle read's 503 is retried.</summary>
public static class SubtitleSidecarReader
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(250);
    private const int MaxAttempts = 4;

    public static async Task<SubtitleSidecarRead> ReadAsync(HttpClient http, Uri url, string? codec, CancellationToken ct,
        TimeSpan? readTimeout = null)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
            return new(SubtitleSidecarReadOutcome.Fallback);
        var wasBusy = false;
        // Any busy history suppresses the URL fallback, even if a later
        // attempt fails differently: a fresh download must not reset the
        // bounded 503/Retry-After budget.
        SubtitleSidecarRead Failure(SubtitleSidecarReadFailure reason = SubtitleSidecarReadFailure.None)
            => new(wasBusy ? SubtitleSidecarReadOutcome.Busy : SubtitleSidecarReadOutcome.Error, WasBusy: wasBusy, Failure: reason);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Complete-track extraction may need a longer header/body deadline,
        // without extending the short 503/Retry-After retry window.
        bounded.CancelAfter(readTimeout ?? Budget);
        var clock = Stopwatch.StartNew();
        try
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (wasBusy && clock.Elapsed >= Budget) return Failure();
                // Use the advertised URL verbatim. Account headers belong to
                // API requests, never to a server-advertised CDN/worker URL.
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    wasBusy = true;
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
                if (!response.IsSuccessStatusCode) return Failure();
                if (response.Content.Headers.ContentLength > SubtitleSidecarFile.MaxBytes)
                    return Failure(SubtitleSidecarReadFailure.DownloadLimit);
                var extension = SubtitleSidecarFile.Extension(response.Content.Headers.ContentType?.MediaType, url, codec);
                // Missing path/codec metadata must not bypass MIME discovery or
                // 503 handling. Opaque formats still use the owned disk download
                // for local probing, but never after observing a busy response.
                if (extension == null) return wasBusy ? Failure() : new(SubtitleSidecarReadOutcome.Fallback);
                using var buffer = new MemoryStream();
                await using var body = await response.Content.ReadAsStreamAsync(bounded.Token).ConfigureAwait(false);
                var chunk = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(chunk, bounded.Token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > SubtitleSidecarFile.MaxBytes)
                        return Failure(SubtitleSidecarReadFailure.DownloadLimit);
                    buffer.Write(chunk, 0, read);
                }
                if (buffer.Length == 0) return Failure();
                return new(SubtitleSidecarReadOutcome.Ready, buffer.ToArray(), extension, wasBusy);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failure(SubtitleSidecarReadFailure.Timeout); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { return Failure(); }
        ct.ThrowIfCancellationRequested();
        return Failure();
    }
}
