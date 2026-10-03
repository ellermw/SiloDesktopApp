using SiloPlayer.Core.Models.Catalog;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SiloPlayer.Core.Api;

public class PeopleApi(SiloApiClient client)
{
    public Task<Person> UpdatePersonAsync(string id, IReadOnlyDictionary<string, object?> changes, CancellationToken ct = default)
        => client.PatchAsync<Person>($"/api/v2/admin/people/{Uri.EscapeDataString(id)}", changes, ct);

    public async Task<List<Person>> SearchScopedAsync(string query, string? mediaScope, int limit = 20, CancellationToken ct = default)
    {
        // This flag also guarantees viewer-access filtering for All. Older
        // servers cannot safely populate a viewer-facing people search group.
        var context = client.CaptureContext();
        var capability = await client.GetAsync<PeopleSearchCapability>("/api/v2/catalog/search/capabilities", ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("People search context changed.", ct);
        if (!capability.PeopleMediaScope) return [];
        var path = $"/api/v2/catalog/people?limit={Math.Clamp(limit, 1, 100)}&q={Uri.EscapeDataString(query.Trim())}";
        if (!string.IsNullOrWhiteSpace(mediaScope) && mediaScope != "all") path += "&media_scope=" + Uri.EscapeDataString(mediaScope);
        var result = await client.GetAsync<BrowseCollection<Person>>(path, ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("People search context changed.", ct);
        return result.Items;
    }

    private sealed class PeopleSearchCapability { public bool PeopleMediaScope { get; set; } }

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

    public Task<Person> AdminRefreshPersonAsync(string id, CancellationToken ct = default)
        => client.PostAsync<Person>($"/api/v2/admin/people/{Uri.EscapeDataString(id)}/refresh", new { }, ct, allowTokenRefresh: false);

    /// <summary>
    /// Follow background provider/photo work for the lifetime of an open person
    /// page. A complete response or rotated presigned URL does not mean the
    /// background job has finished. Match the WebUI's 3s, then 30s backoff.
    /// </summary>
    public async IAsyncEnumerable<Person> ObservePersonRefreshAsync(
        Person initial, [EnumeratorCancellation] CancellationToken ct = default, bool queueIfIncomplete = true)
    {
        var context = client.CaptureContext();
        var elapsed = Stopwatch.StartNew();
        if (queueIfIncomplete && (string.IsNullOrEmpty(initial.Bio) || string.IsNullOrEmpty(initial.PhotoUrl) ||
            string.IsNullOrEmpty(initial.BirthDate)))
        {
            try { await RefreshPersonAsync(initial.Id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Reads can already queue a refresh. A denied/failed explicit
                // request must not discard the displayed person or stop reads.
            }
        }

        while (!ct.IsCancellationRequested && client.IsCurrentContext(context))
        {
            await Task.Delay(TimeSpan.FromSeconds(elapsed.Elapsed.TotalSeconds < 30 ? 3 : 30), ct);
            if (!client.IsCurrentContext(context)) yield break;
            Person? refreshed = null;
            try { refreshed = await GetPersonAsync(initial.Id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Keep existing data and retry on the next observation tick.
            }
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(context)) yield break;
            if (refreshed != null) yield return refreshed;
        }
    }
}
