using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Api;

/// <summary>Current v2 allowance resources. Every request retains the initiating access/PIN context.</summary>
public sealed class TitleAllowancesApi(SiloApiClient client)
{
    private static string Id(string value) => Uri.EscapeDataString(value);
    private static string ProfilePath(string profile) => $"/api/v2/profiles/{Id(profile)}";

    private async Task<T> ReadAsync<T>(ApiRequestContext owner, string path, CancellationToken ct)
    {
        var value = await client.SendRequestAsync<T>(owner, HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        EnsureCurrent(owner, ct);
        return value;
    }

    private void EnsureCurrent(ApiRequestContext owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("The allowance owner changed.", ct);
    }

    public async Task<TitleAllowanceCapability> GetCapabilityAsync(ApiRequestContext owner, CancellationToken ct = default)
    {
        try { return await ReadAsync<TitleAllowanceCapability>(owner, "/api/v2/capabilities/title-allowances", ct); }
        catch (ApiException error) when (error.StatusCode is 404 or 405 or 501)
        { EnsureCurrent(owner, ct); return new(); }
    }

    public Task<ApiCollectionPage<TitleAllowanceProfile>> GetTitleProfilesAsync(ApiRequestContext owner, string itemId, CancellationToken ct = default)
        => ReadAsync<ApiCollectionPage<TitleAllowanceProfile>>(owner, $"/api/v2/catalog/items/{Id(itemId)}/title-allowances", ct);

    public Task<ApiCollectionPage<MediaItem>> GetAllowedTitlesAsync(ApiRequestContext owner, string profileId, string? cursor = null, CancellationToken ct = default)
        => ReadAsync<ApiCollectionPage<MediaItem>>(owner, ProfilePath(profileId) + "/allowed-titles?limit=50" + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Id(cursor)), ct);

    public Task<ApiCollectionPage<AllowedCollection>> GetAllowedCollectionsAsync(ApiRequestContext owner, string profileId, CancellationToken ct = default)
        => ReadAsync<ApiCollectionPage<AllowedCollection>>(owner, ProfilePath(profileId) + "/allowed-collections", ct);

    public async Task SetTitleAsync(ApiRequestContext owner, string profileId, string itemId, bool allowed, CancellationToken ct = default)
    {
        await client.SendNoContentRequestAsync(owner, allowed ? HttpMethod.Put : HttpMethod.Delete,
            ProfilePath(profileId) + "/allowed-titles/" + Id(itemId), null, ct).ConfigureAwait(false);
        EnsureCurrent(owner, ct);
    }

    public async Task SetCollectionAsync(ApiRequestContext owner, string profileId, string collectionId, bool allowed, CancellationToken ct = default)
    {
        await client.SendNoContentRequestAsync(owner, allowed ? HttpMethod.Put : HttpMethod.Delete,
            ProfilePath(profileId) + "/allowed-collections/" + Id(collectionId), null, ct).ConfigureAwait(false);
        EnsureCurrent(owner, ct);
    }

    /// <summary>Non-retryable copy. After an uncertain result the caller must read the title list first.</summary>
    public async Task<AllowCollectionTitlesResult> CopyCollectionAsync(ApiRequestContext owner, string profileId, string collectionId, CancellationToken ct = default)
    {
        var result = await client.SendRequestWithoutRefreshAsync<AllowCollectionTitlesResult>(owner, HttpMethod.Post,
            ProfilePath(profileId) + "/allowed-titles/copy-collection", new { CollectionId = collectionId }, ct).ConfigureAwait(false);
        EnsureCurrent(owner, ct);
        return result;
    }

    public Task<ProfilesResponse> GetProfilesAsync(ApiRequestContext owner, CancellationToken ct = default)
        => ReadAsync<ProfilesResponse>(owner, "/api/v2/profiles", ct);

    public Task<ApiCollectionPage<MediaItem>> SearchAsync(ApiRequestContext owner, string query, CancellationToken ct = default)
        => ReadAsync<ApiCollectionPage<MediaItem>>(owner, "/api/v2/catalog?source=query&limit=10&seek=0&q=" + Id(query.Trim()), ct);
}
