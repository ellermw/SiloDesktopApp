namespace SiloPlayer.Core.Api;

public class UserApiKey
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? LastUsedAt { get; set; }
}

public class ApiKeysApi(SiloApiClient client)
{
    public Task<UserApiKey> CreateApiKeyAsync(string label, CancellationToken ct = default)
        => client.PostAsync<UserApiKey>("/api/v1/api-keys", new { label }, ct);

    public Task<List<UserApiKey>> GetApiKeysAsync(CancellationToken ct = default)
        => client.GetAsync<List<UserApiKey>>("/api/v1/api-keys", ct);

    public Task DeleteApiKeyAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/api-keys/{id}", ct);
}
