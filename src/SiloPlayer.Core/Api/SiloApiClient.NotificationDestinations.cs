namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    /// <summary>
    /// DELETE with the validator of the row the person saw. Bound to the captured identity, never refreshes
    /// authentication and is never replayed: the request is sent once.
    /// </summary>
    public async Task DeleteWithETagWithoutRefreshAsync(ApiRequestContext context, string path, string etag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(etag) || etag.StartsWith("W/", StringComparison.Ordinal))
            throw new ArgumentException("A strong validator is required.", nameof(etag));
        using var request = CreateContextBoundRequest(context, HttpMethod.Delete, path, null,
            new Dictionary<string, string> { ["If-Match"] = etag });
        await SendNoContentAsync(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
    }
}
