using SiloPlayer.Core.Models.MediaMaintenance;
using System.Text.Json;

namespace SiloPlayer.Core.Api;

public sealed class MediaSplitApi(SiloApiClient client)
{
    public async Task<List<JsonElement>> GetFilesAsync(ApiRequestContext context, string id, CancellationToken ct = default)
    {
        var files = new List<JsonElement>(); var seen = new HashSet<string>(); string? cursor = null;
        while (true)
        {
            Current(context, ct);
            var page = await client.SendRequestAsync<JsonElement>(context, HttpMethod.Get,
                $"/api/v2/admin/items/{Uri.EscapeDataString(id)}/files?limit=200" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), null, ct);
            Current(context, ct); files.AddRange(page.GetProperty("items").EnumerateArray());
            if (!page.TryGetProperty("page", out var pagination) || !pagination.TryGetProperty("has_more", out var more) || !more.GetBoolean()) return files;
            cursor = pagination.TryGetProperty("next_cursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)) throw new InvalidOperationException("Incomplete item file list. Reload to try again.");
        }
    }
    public async Task<ItemMatchSearchResponse> SearchAsync(ApiRequestContext context, string id, ItemMatchSearchRequest request, CancellationToken ct = default)
    {
        Current(context, ct); var result = await client.SendRequestAsync<ItemMatchSearchResponse>(context, HttpMethod.Post,
            $"/api/v2/admin/items/{Uri.EscapeDataString(id)}/match/search", V2Json.Body(request), ct); Current(context, ct); return result;
    }
    public async Task<JsonElement> SplitAsync(ApiRequestContext context, string id, object request, CancellationToken ct = default)
    {
        Current(context, ct); var result = await client.SendRequestWithoutRefreshAsync<JsonElement>(context, HttpMethod.Post,
            $"/api/v2/admin/items/{Uri.EscapeDataString(id)}/split", request, ct); Current(context, ct); return result;
    }
    private void Current(ApiRequestContext context, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Split authority changed.", ct); }
}
