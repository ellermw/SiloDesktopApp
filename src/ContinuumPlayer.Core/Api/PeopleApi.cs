using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.Core.Api;

public class PeopleApi(ContinuumApiClient client)
{
    public Task<List<Person>> GetPeopleAsync(string? query = null, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        var path = $"/api/v1/people?limit={limit}&offset={offset}";
        if (query != null) path += $"&q={Uri.EscapeDataString(query)}";
        return client.GetAsync<List<Person>>(path, ct);
    }

    public Task<Person> GetPersonAsync(int id, CancellationToken ct = default)
        => client.GetAsync<Person>($"/api/v1/people/{id}", ct);

    public Task<PersonRefreshResponse> RefreshPersonAsync(int id, CancellationToken ct = default)
        => client.PostAsync<PersonRefreshResponse>($"/api/v1/people/{id}/refresh", new { }, ct);
}
