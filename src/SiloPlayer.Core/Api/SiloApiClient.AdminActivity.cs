using System.Net.Http.Json;

namespace SiloPlayer.Core.Api;

/// <summary>A sequenced Activity command was refused; carries the server's latest applied sequence when it named one.</summary>
public sealed class AdminActivityCommandException(ApiException inner, string? latestSequenceHeader)
    : ApiException(inner.ErrorCode, inner.Message, inner.StatusCode, inner.ErrorLocation, inner.RetryAfterSeconds)
{
    /// <summary>Raw <c>X-Silo-Latest-Sequence</c>; parse with <c>AdminActivityCommandLedger.ParseLatestSequence</c>.</summary>
    public string? LatestSequenceHeader { get; } = latestSequenceHeader;
}

public partial class SiloApiClient
{
    /// <summary>
    /// Sends one sequenced Admin Activity playback command bound to
    /// <paramref name="context"/>, without authentication replay. A refusal
    /// keeps only the safe <c>X-Silo-Latest-Sequence</c> header (409 stale) so the
    /// caller can raise its allocation floor; no other response header escapes.
    /// </summary>
    internal async Task<T> SendAdminActivityCommandAsync<T>(ApiRequestContext context, string path, object body,
        CancellationToken ct)
    {
        using var request = CreateContextBoundRequest(context, HttpMethod.Post, path, body);
        using var response = await SendWithRetryAsync(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var latest = response.Headers.TryGetValues("X-Silo-Latest-Sequence", out var values) ? values.FirstOrDefault() : null;
            try
            {
                await ThrowApiException(response, request, ct).ConfigureAwait(false);
            }
            catch (ApiException error) when (error is not AdminActivityCommandException)
            {
                throw new AdminActivityCommandException(error, latest);
            }
        }
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
    }
}
