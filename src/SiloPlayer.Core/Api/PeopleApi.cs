using SiloPlayer.Core.Models.Catalog;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

    /// <summary>
    /// Follow background provider/photo work for the lifetime of an open person
    /// page. A complete response or rotated presigned URL does not mean the
    /// background job has finished. Match the WebUI's 3s, then 30s backoff.
    /// </summary>
    public async IAsyncEnumerable<Person> ObservePersonRefreshAsync(
        Person initial, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var elapsed = Stopwatch.StartNew();
        if (string.IsNullOrEmpty(initial.Bio) || string.IsNullOrEmpty(initial.PhotoUrl) ||
            string.IsNullOrEmpty(initial.BirthDate))
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
