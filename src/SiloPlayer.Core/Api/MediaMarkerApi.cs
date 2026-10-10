using System.Text.Json;

namespace SiloPlayer.Core.Api;

/// <summary>Item-scoped marker and seek-preview operations from the current viewer action bar.</summary>
public sealed class MediaMarkerApi(SiloApiClient client)
{
    public Task<JsonElement> GetMarkersAsync(ApiRequestContext context, string id, CancellationToken ct = default)
        => Read(context, $"/api/v2/markers/items/{Uri.EscapeDataString(id)}", ct);
    public Task<JsonElement> GetHistoryAsync(ApiRequestContext context, string id, CancellationToken ct = default)
        => Read(context, $"/api/v2/admin/markers/items/{Uri.EscapeDataString(id)}/history?limit=25", ct);
    public Task<JsonElement> SetMarkersAsync(ApiRequestContext context, string id, Dictionary<string, object?> body, CancellationToken ct = default)
        => Write(context, HttpMethod.Put, $"/api/v2/markers/items/{Uri.EscapeDataString(id)}", body, ct);
    public Task<JsonElement> GetPreviewsAsync(ApiRequestContext context, string id, CancellationToken ct = default)
        => Read(context, $"/api/v2/admin/items/{Uri.EscapeDataString(id)}/trickplay", ct);
    public Task<JsonElement> RegeneratePreviewsAsync(ApiRequestContext context, string id, CancellationToken ct = default)
        => Write(context, HttpMethod.Post, $"/api/v2/admin/items/{Uri.EscapeDataString(id)}/trickplay/regenerate", null, ct);
    public async Task<JsonElement> RedetectAsync(ApiRequestContext context, string id, string type, string kind, CancellationToken ct = default, bool legacyIntro = false)
    {
        if (kind is not ("intro" or "credits" or "all")) throw new ArgumentOutOfRangeException(nameof(kind));
        if (legacyIntro && (type != "episode" || kind != "intro")) throw new ArgumentException("Legacy detection is episode-intro only.");
        var path = $"/api/v2/admin/items/{Uri.EscapeDataString(id)}";
        if (legacyIntro) return await Write(context, HttpMethod.Post, path + "/redetect-intro", null, ct);
        try { return await Write(context, HttpMethod.Post, path + "/redetect-markers", new { kind }, ct); }
        catch (ApiException error) when (error.StatusCode == 404 && type == "episode" && kind == "intro")
        { return await Write(context, HttpMethod.Post, path + "/redetect-intro", null, ct); }
    }
    private async Task<JsonElement> Read(ApiRequestContext context, string path, CancellationToken ct)
    {
        var result = await client.SendRequestAsync<JsonElement>(context, HttpMethod.Get, path, null, ct);
        ct.ThrowIfCancellationRequested(); if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Media context changed.", ct); return result;
    }
    private async Task<JsonElement> Write(ApiRequestContext context, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var result = await client.SendRequestWithoutRefreshAsync<JsonElement>(context, method, path, body, ct);
        ct.ThrowIfCancellationRequested(); if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Media context changed.", ct); return result;
    }
}
