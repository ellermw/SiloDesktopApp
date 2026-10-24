using System.Diagnostics;
using System.Net;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    public async Task<PlaybackProbeResult?> MeasurePlaybackConnectionAsync(ApiIdentityContext owner,
        PlaybackCapabilityV3 capability, bool manual, string transport, CancellationToken ct)
    {
        if (!capability.Features.Contains("connection_speed_v1") || string.IsNullOrEmpty(capability.BandwidthProbeUrl))
            throw new NotSupportedException("This server does not support connection speed tests.");
        var origin = new Uri(owner.BaseUrl.TrimEnd('/') + "/");
        var uri = new Uri(origin, capability.BandwidthProbeUrl);
        if (uri.Scheme != origin.Scheme || uri.Authority != origin.Authority)
            throw new InvalidDataException("The speed test must start on the connected server.");
        var meter = new ConnectionProbeMeter(manual, capability.BandwidthProbeTargetKbps);
        var path = uri.PathAndQuery + (string.IsNullOrEmpty(uri.Query) ? "?" : "&") + "size=" + meter.MaximumBytes;
        using var firstByte = CancellationTokenSource.CreateLinkedTokenSource(ct);
        firstByte.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = CreateIdentityBoundRequest(owner, HttpMethod.Get, path, null);
        // Probe authentication is account scoped; never send the profile's PIN grant to a media redirect.
        request.Headers.Remove("X-Profile-Token");
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.CacheControl = new() { NoStore = true };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, firstByte.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = Math.Max(1, Math.Ceiling(response.Headers.RetryAfter?.Delta?.TotalSeconds ??
                (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 30));
            throw new ApiException("rate_limited", $"Try again in {retry:0} seconds.", 429, retryAfterSeconds: retry);
        }
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadAsStreamAsync(firstByte.Token).ConfigureAwait(false);
        var buffer = new byte[64 * 1024];
        var read = await body.ReadAsync(buffer, firstByte.Token).ConfigureAwait(false);
        if (read == 0) return null;
        var began = Stopwatch.GetTimestamp(); meter.Add(0, read);
        using var transfer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        transfer.CancelAfter(meter.MaximumMs);
        try
        {
            while (!meter.Done)
            {
                read = await body.ReadAsync(buffer, transfer.Token).ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                if (read == 0) { meter.Finish(elapsed, true); break; }
                meter.Add(elapsed, read);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && transfer.IsCancellationRequested)
        { meter.Finish(meter.MaximumMs); }
        ct.ThrowIfCancellationRequested();
        if (!IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Connection owner changed.");
        if (meter.Result is { } result) result.Transport = transport is "ethernet" or "wifi" or "cellular" ? transport : "other";
        return meter.Result;
    }
}
