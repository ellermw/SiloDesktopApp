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
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Download context changed.", ct);
            var path = "/api/v2/downloads" + (cursor == null ? "" : "?cursor=" + Uri.EscapeDataString(cursor));
            var page = await client.PostAsync<DownloadCreated>(path, V2Json.Body(request), ct);
            result.Downloads.AddRange(page.Items);
            result.Skipped.AddRange(page.Skipped);
            if (page.Page?.HasMore != true) return result;
            cursor = page.Page.NextCursor;
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)) throw new InvalidOperationException("The server returned a non-advancing download cursor.");
        } while (true);
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
