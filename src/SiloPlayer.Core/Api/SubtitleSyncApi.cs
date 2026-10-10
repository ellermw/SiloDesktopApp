using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public sealed class SubtitleSyncApi(SiloApiClient client)
{
    public Task<SubtitleSyncCapability> GetCapabilityAsync(ApiRequestContext context, CancellationToken ct = default)
        => Scoped(client.SendRequestAsync<SubtitleSyncCapability>(context, HttpMethod.Get, "/api/v2/subtitles/sync/status", null, ct), context, ct);
    public Task<SubtitleSyncInventory> GetInventoryAsync(ApiRequestContext context, int fileId, CancellationToken ct = default)
        => Scoped(client.SendRequestAsync<SubtitleSyncInventory>(context, HttpMethod.Get, Path(fileId), null, ct), context, ct);
    public Task<ApiResponse<SubtitleSyncResponse>> GetAsync(ApiRequestContext context, int fileId, string key, CancellationToken ct = default)
        => client.GetWithETagAsync<SubtitleSyncResponse>(context, Path(fileId, key), ct);
    public Task<SubtitleSyncResponse> StartAsync(ApiRequestContext context, int fileId, string key, CancellationToken ct = default)
        => Scoped(client.SendRequestWithoutRefreshAsync<SubtitleSyncResponse>(context, HttpMethod.Post, Path(fileId, key), null, ct), context, ct);
    public Task<ApiResponse<SubtitleSyncResponse>> ResetAsync(ApiRequestContext context, int fileId, string key, string etag, CancellationToken ct = default)
        => client.PutWithETagWithoutRefreshAsync<SubtitleSyncResponse>(context, Path(fileId, key) + "/timing",
            new Dictionary<string, object?> { ["offset_ms"] = 0, ["scale"] = 1d }, etag, ct);

    private async Task<T> Scoped<T>(Task<T> request, ApiRequestContext context, CancellationToken ct)
    {
        var value = await request.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Subtitle sync authority changed.", ct);
        return value;
    }

    private static string Path(int fileId, string? key = null)
    {
        if (fileId <= 0) throw new ArgumentOutOfRangeException(nameof(fileId));
        if (key != null && !Services.SubtitleSyncPolicy.IsKey(key)) throw new ArgumentException("Invalid subtitle sync key.", nameof(key));
        return $"/api/v2/subtitles/{fileId}/sync" + (key == null ? "" : "/" + Uri.EscapeDataString(key));
    }
}
