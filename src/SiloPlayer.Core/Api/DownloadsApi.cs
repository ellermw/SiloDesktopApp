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

    public async Task<DownloadsResponse> CreateDownloadsAsync(DownloadRequest request, CancellationToken ct = default)
    {
        if (request.Series) request.BatchId ??= Guid.NewGuid().ToString();
        var result = new DownloadsResponse();
        var context = client.CaptureContext();
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Download context changed.", ct);
            var path = "/api/v2/downloads" + (cursor == null ? "" : "?cursor=" + Uri.EscapeDataString(cursor));
            var page = await client.SendRequestAsync<DownloadCreated>(context, HttpMethod.Post, path, V2Json.Body(request), ct);
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Download context changed.", ct);
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
        int? libraryId, CancellationToken ct = default)
    {
        var owner = client.CaptureContext();
        void Current()
        {
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("Download context changed.", ct);
        }
        string Scoped(string path) => libraryId.HasValue
            ? path + "?library_id=" + libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : path;
        Current();
        var id = Uri.EscapeDataString(seriesId);
        var seasons = seasonNumber.HasValue ? new List<Season> { new() { SeasonNumber = seasonNumber.Value } }
            : await BrowseV2.AllAsync<Season>(client, Scoped($"/api/v2/catalog/series/{id}/seasons"), ct);
        Current();
        foreach (var season in seasons)
        {
            var number = season.SeasonNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var episodes = await BrowseV2.AllAsync<Episode>(client, Scoped($"/api/v2/catalog/series/{id}/seasons/{number}/episodes"), ct);
            Current();
            foreach (var episode in episodes)
            {
                var readable = episode.Files.Where(file => file.FileId > 0 && file.Unreadable != true).Select(file => file.FileId).ToHashSet();
                if (readable.Count == 0) continue;
                try
                {
                    var versions = await BrowseV2.AllAsync<FileVersion>(client,
                        Scoped($"/api/v2/catalog/items/{Uri.EscapeDataString(episode.ContentId)}/versions"), ct);
                    Current();
                    var candidates = versions.Where(v => readable.Contains(v.FileId)).DistinctBy(v => v.FileId).ToList();
                    if (candidates.Count > 0) return candidates;
                }
                catch (ApiException ex) when (ex.StatusCode is 403 or 404) { Current(); }
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
