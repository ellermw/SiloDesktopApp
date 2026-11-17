using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>Reads a retained window without committing snapshots older than a card mutation.</summary>
public static class RetainedCatalogRead
{
    public static async Task<CatalogResponse> ReadAsync(
        Func<int, int, string?, Task<CatalogResponse>> readWindow,
        int windowSize, int pageSize, Func<long> mutationRevision, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var revision = mutationRevision();
            var response = await readWindow(0, pageSize, null);
            ct.ThrowIfCancellationRequested();
            var items = new List<MediaItem>(response.Items);
            while (revision == mutationRevision() && items.Count < windowSize && response.Items.Count > 0
                && (response.HasMore || items.Count < response.Total))
            {
                response = await readWindow(items.Count, Math.Min(pageSize, windowSize - items.Count), response.Snapshot);
                ct.ThrowIfCancellationRequested();
                items.AddRange(response.Items);
            }
            // A watched/list/progress write can change both state and membership.
            // Start with a new server snapshot rather than resurrecting an old row.
            if (revision != mutationRevision()) continue;
            response.Items = items;
            return response;
        }
    }
}
