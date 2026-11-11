using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Models.Downloads;

namespace SiloPlayer.Core.Api;

public class DownloadsApi(SiloApiClient client)
{
    public async Task<Download> CreateDownloadAsync(DownloadRequest request, CancellationToken ct = default)
    {
        var created = await CreateDownloadsAsync(request, ct);
        return created.Downloads.FirstOrDefault()
            ?? throw new InvalidOperationException(created.Skipped.FirstOrDefault()?.Reason ?? "No downloadable media was returned.");
    }

    /// <summary><paramref name="pageAccepted"/> runs for each page the server accepted, also when the batch stops afterwards.</summary>
    public async Task<DownloadsResponse> CreateDownloadsAsync(DownloadRequest request, CancellationToken ct = default, Action? pageAccepted = null)
    {
        if (request.Series) request.BatchId ??= Guid.NewGuid().ToString();
        var result = new DownloadsResponse();
        var context = client.CaptureContext();
        // The viewer outlives request contexts: reconnect catch-up and access refreshes retire requests, not a completed batch.
        var viewer = client.CaptureIdentityContext();
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Download context changed.", ct);
            var path = "/api/v2/downloads" + (cursor == null ? "" : "?cursor=" + Uri.EscapeDataString(cursor));
            var page = await client.SendRequestAsync<DownloadCreated>(context, HttpMethod.Post, path, V2Json.Body(request), ct);
            // Reported before the owner checks: a batch retired here still registered this page.
            pageAccepted?.Invoke();
            // An accepted last page completed the batch. Only a retired viewer discards it; a closed owner or retired request still gets the result.
            if (page.Page?.HasMore == true || !client.IsCurrentIdentityContext(viewer))
            {
                ct.ThrowIfCancellationRequested();
                if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Download context changed.", ct);
            }
            result.Downloads.AddRange(page.Items);
            result.Skipped.AddRange(page.Skipped);
            if (page.Page?.HasMore != true) return result;
            cursor = page.Page.NextCursor;
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)) throw new InvalidOperationException("The server returned a non-advancing download cursor.");
        } while (true);
    }

    /// <summary>Use the first accessible playable episode as the version template source.
    /// Aggregate series and season records deliberately have no versions.</summary>
    public async Task<List<FileVersion>> GetBatchTemplateVersionsAsync(string seriesId, int? seasonNumber,
        int? libraryId, string? seasonContentId = null, CancellationToken ct = default)
    {
        var owner = client.CaptureContext();
        // The viewer outlives request contexts: reconnect catch-up and access refreshes retire reads, not the picker.
        var viewer = client.CaptureIdentityContext();
        try { return await DiscoverBatchTemplateVersionsAsync(owner, seriesId, seasonNumber, libraryId, seasonContentId, ct); }
        // Any other discovery failure, a retired request context included, ends the lookup with no template.
        // Only a retired viewer still stops the picker.
        catch (Exception) when (!ct.IsCancellationRequested && client.IsCurrentIdentityContext(viewer)) { return []; }
    }

    private async Task<List<FileVersion>> DiscoverBatchTemplateVersionsAsync(ApiRequestContext owner, string seriesId,
        int? seasonNumber, int? libraryId, string? seasonContentId, CancellationToken ct)
    {
        void Current()
        {
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("Download context changed.", ct);
        }
        string Scoped(string path) => libraryId.HasValue
            ? path + "?library_id=" + libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : path;
        // Templates are optional: a missing or forbidden list means no template, never a failed batch.
        async Task<List<T>> Optional<T>(string path)
        {
            List<T> items;
            try { items = await BrowseV2.AllAsync<T>(client, Scoped(path), ct); }
            catch (ApiException ex) when (ex.StatusCode is 403 or 404) { items = []; }
            Current();
            return items;
        }
        Current();
        var id = Uri.EscapeDataString(seriesId);
        var seasons = seasonNumber.HasValue ? new List<Season> { new() { ContentId = seasonContentId ?? "", SeasonNumber = seasonNumber.Value } }
            : await Optional<Season>($"/api/v2/catalog/series/{id}/seasons");
        // A whole series takes its template from the earliest regular season; Specials are only the fallback.
        foreach (var season in seasons.OrderBy(s => s.SeasonNumber <= 0).ThenBy(s => s.SeasonNumber))
        {
            // The season item route keeps Specials; the legacy series/number route can discard season zero.
            List<Episode> episodes = string.IsNullOrEmpty(season.ContentId) ? []
                : await Optional<Episode>($"/api/v2/catalog/items/{Uri.EscapeDataString(season.ContentId)}/episodes");
            if (episodes.Count == 0)
            {
                var number = season.SeasonNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                episodes = await Optional<Episode>($"/api/v2/catalog/series/{id}/seasons/{number}/episodes");
            }
            foreach (var episode in episodes)
            {
                var readable = episode.Files.Where(file => file.FileId > 0 && file.Unreadable != true).Select(file => file.FileId).ToHashSet();
                if (readable.Count == 0) continue;
                var versions = await Optional<FileVersion>($"/api/v2/catalog/items/{Uri.EscapeDataString(episode.ContentId)}/versions");
                var candidates = versions.Where(v => readable.Contains(v.FileId)).DistinctBy(v => v.FileId).ToList();
                if (candidates.Count > 0) return candidates;
            }
        }
        return [];
    }

    public async Task<DownloadsResponse> GetDownloadsAsync(CancellationToken ct = default)
        => new() { Downloads = await BrowseV2.AllAsync<Download>(client, "/api/v2/downloads", ct) };

    public Task<DownloadCapability> GetCapabilityAsync(CancellationToken ct = default)
        => client.GetAsync<DownloadCapability>("/api/v2/capabilities/downloads", ct);

    public Task DeleteDownloadAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/downloads/{Uri.EscapeDataString(id)}", ct);

    /// <summary>
    /// Returns the download file URL path for the given download ID.
    /// Use this with an HttpClient directly for streaming the binary content,
    /// since SiloApiClient.GetAsync deserializes JSON.
    /// </summary>
    public static string GetDownloadFilePath(string id) => $"/api/v2/downloads/{Uri.EscapeDataString(id)}/file-proxy";

    public static string GetDirectDownloadPath(int fileId)
        => $"/api/v2/direct-download-proxy?file_id={fileId}";

    private sealed class DownloadCreated
    {
        public List<Download> Items { get; set; } = [];
        public List<SkippedDownload> Skipped { get; set; } = [];
        public BrowsePage? Page { get; set; }
    }
}
