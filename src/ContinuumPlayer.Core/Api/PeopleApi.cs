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

    // B33: id is a string — cast/crew person_id is `string` in WebUI types and may be non-numeric.
    public Task<Person> GetPersonAsync(string id, CancellationToken ct = default)
        => client.GetAsync<Person>($"/api/v1/people/{Uri.EscapeDataString(id)}", ct);

    public Task<PersonRefreshResponse> RefreshPersonAsync(string id, CancellationToken ct = default)
        => client.PostAsync<PersonRefreshResponse>($"/api/v1/people/{Uri.EscapeDataString(id)}/refresh", new { }, ct);
}
