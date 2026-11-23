namespace SiloPlayer.Core.Api;

/// <summary>A key in "Your keys" (openapi PersonalAPIKeyListItem). It never holds the secret.</summary>
public sealed class PersonalApiKey
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Label { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string RateTier { get; set; } = "";
    public List<string> Scopes { get; set; } = [];
    /// <summary>The profile the key is bound to; absent on a whole-account key.</summary>
    public string? ProfileId { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? LastUsedAt { get; set; }
}

/// <summary>One scope the server offers (openapi APIKeyScope); <c>Audience</c> is "user" or "admin".</summary>
public sealed class PersonalApiKeyScope
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Audience { get; set; } = "";
}

/// <summary>Which scopes exist and whether this server lets users create keys (GET /api/v2/api-keys/scopes).</summary>
public sealed class PersonalApiKeyScopes
{
    public bool Available { get; set; }
    public long MaxUserKeys { get; set; }
    public List<PersonalApiKeyScope> Scopes { get; set; } = [];
    public bool UserKeysEnabled { get; set; }
}

public sealed class PersonalApiKeyPageInfo
{
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}

/// <summary>The wire body of GET /api/v2/api-keys.</summary>
public sealed class PersonalApiKeyListBody
{
    public List<PersonalApiKey>? Items { get; set; }
    public PersonalApiKeyPageInfo? Page { get; set; }
}

/// <summary>One page of keys; <c>NextCursor</c> is null at the end, or when the server repeated the cursor it was asked for.</summary>
public sealed record PersonalApiKeyPage(IReadOnlyList<PersonalApiKey> Items, string? NextCursor);

/// <summary>
/// The creation response, the only place the credential appears. It is shown once and must never be logged,
/// stored or echoed, so <see cref="ToString"/> hides it.
/// </summary>
public sealed class PersonalApiKeyCreated
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
    public string? ProfileId { get; set; }
    public List<string> Scopes { get; set; } = [];
    public override string ToString() => "PersonalApiKeyCreated";
}

/// <summary>
/// The signed-in account's own keys (openapi tag api-keys), as the official WebUI's Settings > API Keys uses them.
/// Every call belongs to the account and profile that started it: it is refused before it is sent once that identity changed.
/// </summary>
public sealed class ApiKeysApi(SiloApiClient client)
{
    public const string Path = "/api/v2/api-keys";
    public const int PageSize = 50;

    public Task<PersonalApiKeyScopes> GetScopesAsync(ApiIdentityContext owner, CancellationToken ct = default)
        => client.SendIdentityBoundRequestAsync<PersonalApiKeyScopes>(owner, HttpMethod.Get, Path + "/scopes", null, ct);

    /// <summary>One page of the keys the acting profile may see.</summary>
    public async Task<PersonalApiKeyPage> GetPageAsync(ApiIdentityContext owner, string? cursor = null, CancellationToken ct = default)
    {
        var path = Path + "?limit=" + PageSize + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        var body = await client.SendIdentityBoundRequestAsync<PersonalApiKeyListBody>(owner, HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        var next = body.Page is { HasMore: true, NextCursor: { Length: > 0 } token } ? token : null;
        // A cursor that repeats would page forever.
        return new(body.Items ?? [], next != null && next != cursor ? next : null);
    }

    /// <summary>
    /// Creates a key; a non-empty <paramref name="profileId"/> binds it to that profile. Sent once: no token-refresh
    /// replay and no retry, because a replay could mint a second key.
    /// </summary>
    public Task<PersonalApiKeyCreated> CreateAsync(ApiIdentityContext owner, string label, IReadOnlyList<string> scopes,
        string? profileId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["label"] = label, ["scopes"] = scopes.ToArray() };
        if (!string.IsNullOrEmpty(profileId)) body["profile_id"] = profileId;
        return client.SendIdentityBoundRequestWithoutRefreshAsync<PersonalApiKeyCreated>(owner, HttpMethod.Post, Path, body, ct);
    }

    public Task RevokeAsync(ApiIdentityContext owner, string id, CancellationToken ct = default)
        => client.SendIdentityBoundNoContentAsync(owner, HttpMethod.Delete, Path + "/" + Uri.EscapeDataString(id), null, ct);
}
