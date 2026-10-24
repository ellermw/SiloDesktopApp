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
        using var response = await OpenConnectionProbeAsync(owner, path, firstByte.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = Math.Max(1, Math.Ceiling(response.Headers.RetryAfter?.Delta?.TotalSeconds ??
                (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 30));
            throw new ApiException("rate_limited", $"Try again in {retry:0} {(retry == 1 ? "second" : "seconds")}", 429, retryAfterSeconds: retry);
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

    private async Task<HttpResponseMessage> OpenConnectionProbeAsync(ApiIdentityContext owner, string path, CancellationToken ct)
    {
        var target = new Uri(owner.BaseUrl.TrimEnd('/') + path);
        var authenticationRetried = false;
        // The shared API transport intentionally refuses automatic redirects.
        // Only this probe follows the server's short-lived media URL, with no
        // account, profile, PIN or device identity copied to that media host.
        for (var hop = 0; ; hop++)
        {
            if (!IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Connection owner changed.");
            using var request = hop == 0
                ? CreateIdentityBoundRequest(owner, HttpMethod.Get, path, null)
                : new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Remove("X-Profile-Token");
            request.Version = _http.DefaultRequestVersion;
            request.VersionPolicy = _http.DefaultVersionPolicy;
            request.Headers.Accept.ParseAdd("application/octet-stream");
            request.Headers.CacheControl = new() { NoStore = true };
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!IsCurrentIdentityContext(owner))
            { response.Dispose(); throw new OperationCanceledException("Connection owner changed."); }
            if (hop == 0 && !authenticationRetried && response.StatusCode == HttpStatusCode.Unauthorized && _tokenRefresher != null)
            {
                authenticationRetried = true;
                response.Dispose();
                await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Connection owner changed.");
                    if (string.Equals(AccessToken, request.Headers.Authorization?.Parameter, StringComparison.Ordinal))
                        await _tokenRefresher(ct).ConfigureAwait(false);
                }
                finally { _refreshLock.Release(); }
                // Rebuild the account request so it keeps the fresh bearer but
                // still omits the PIN grant. Never refresh on the signed host.
                hop--; continue;
            }
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (hop >= 5 || location == null || !Uri.TryCreate(target, location, out var next) ||
                next.Scheme is not ("http" or "https") || next.UserInfo.Length != 0 ||
                (target.Scheme == "https" && next.Scheme != "https"))
                throw new InvalidDataException("The speed test returned an unsafe or repeated redirect.");
            target = next;
        }
    }
}
