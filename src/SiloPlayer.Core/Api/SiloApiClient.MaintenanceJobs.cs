using System.Net.Http.Json;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    /// <summary>
    /// An identity-bound GET that also returns the response's <c>Retry-After</c>
    /// in seconds. Job reads carry it as the server's poll hint on a running
    /// job, and an error keeps it on <see cref="ApiException.RetryAfterSeconds"/>.
    /// A transport catch-up does not retire this read; an identity change does.
    /// </summary>
    internal async Task<(T Body, double? RetryAfterSeconds)> SendIdentityBoundReadWithRetryAfterAsync<T>(
        ApiIdentityContext identity, string path, CancellationToken ct)
    {
        using var request = CreateIdentityBoundRequest(identity, HttpMethod.Get, path, null);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var body = (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
        var retryAfter = response.Headers.RetryAfter;
        double? seconds = retryAfter?.Delta?.TotalSeconds;
        if (seconds == null && retryAfter?.Date is { } retryAt)
            seconds = (retryAt - DateTimeOffset.UtcNow).TotalSeconds;
        return (body, seconds is { } value && double.IsFinite(value) ? Math.Max(0, value) : null);
    }
}
