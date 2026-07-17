using SiloPlayer.Core.Models.Downloads;

namespace SiloPlayer.Core.Api;

public class DownloadsApi(SiloApiClient client)
{
    public Task<Download> CreateDownloadAsync(DownloadRequest request, CancellationToken ct = default)
        => client.PostAsync<Download>("/api/v1/downloads", request, ct);

    public Task<DownloadsResponse> GetDownloadsAsync(CancellationToken ct = default)
        => client.GetAsync<DownloadsResponse>("/api/v1/downloads", ct);

    public Task<DownloadCapability> GetCapabilityAsync(CancellationToken ct = default)
        => client.GetAsync<DownloadCapability>("/api/v1/downloads/capability", ct);

    public Task DeleteDownloadAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/downloads/{Uri.EscapeDataString(id)}", ct);

    /// <summary>
    /// Returns the download file URL path for the given download ID.
    /// Use this with an HttpClient directly for streaming the binary content,
    /// since SiloApiClient.GetAsync deserializes JSON.
    /// </summary>
    public static string GetDownloadFilePath(string id) => $"/api/v1/downloads/{Uri.EscapeDataString(id)}/file";

    public static string GetDirectDownloadPath(int fileId)
        => $"/api/v1/direct-download?file_id={fileId}";
}
