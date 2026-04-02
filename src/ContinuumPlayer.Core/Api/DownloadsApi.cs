using ContinuumPlayer.Core.Models.Downloads;

namespace ContinuumPlayer.Core.Api;

public class DownloadsApi(ContinuumApiClient client)
{
    public Task<Download> CreateDownloadAsync(DownloadRequest request, CancellationToken ct = default)
        => client.PostAsync<Download>("/api/v1/downloads", request, ct);

    public Task<DownloadsResponse> GetDownloadsAsync(CancellationToken ct = default)
        => client.GetAsync<DownloadsResponse>("/api/v1/downloads", ct);

    public Task DeleteDownloadAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/downloads/{id}", ct);

    /// <summary>
    /// Returns the download file URL path for the given download ID.
    /// Use this with an HttpClient directly for streaming the binary content,
    /// since ContinuumApiClient.GetAsync deserializes JSON.
    /// </summary>
    public static string GetDownloadFilePath(int id) => $"/api/v1/downloads/{id}/file";
}
