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

    public Task<List<UserApiKey>> GetApiKeysAsync(CancellationToken ct = default)
        => client.GetAllItemsAsync<UserApiKey>("/api/v2/api-keys", ct);

}
