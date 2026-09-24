using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public class PeopleApi(SiloApiClient client)
{
    public async Task<List<Person>> GetPeopleAsync(string? query = null, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        // V2 person search is a bounded result, not a cursor-paged collection.
        var path = $"/api/v2/catalog/people?limit={Math.Clamp((long)limit + Math.Max(0, offset), 1, 100)}";
        if (query != null) path += $"&q={Uri.EscapeDataString(query)}";
        return (await client.GetAsync<BrowseCollection<Person>>(path, ct)).Items.Skip(Math.Max(0, offset)).Take(Math.Max(1, limit)).ToList();
    }

    // B33: id is a string — cast/crew person_id is `string` in WebUI types and may be non-numeric.
    public Task<Person> GetPersonAsync(string id, CancellationToken ct = default)
        => client.GetAsync<Person>($"/api/v2/catalog/people/{Uri.EscapeDataString(id)}", ct);

    public Task<PersonRefreshResponse> RefreshPersonAsync(string id, CancellationToken ct = default)
        => client.PostAsync<PersonRefreshResponse>($"/api/v2/catalog/people/{Uri.EscapeDataString(id)}/refresh", new { }, ct);
}
