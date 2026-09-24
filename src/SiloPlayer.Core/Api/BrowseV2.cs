using System.Text.Json;

namespace SiloPlayer.Core.Api;

/// <summary>Wire collections used at the v2 boundary, independent of UI response wrappers.</summary>
internal sealed class BrowseCollection<T>
{
    public List<T> Items { get; set; } = [];
    public BrowsePage? Page { get; set; }
    public int Total { get; set; }
    public bool TotalExact { get; set; }
}

public sealed class BrowsePage
{
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}

internal static class BrowseV2
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    internal static string Sort(string field, string? order)
        => order == "desc" && !field.StartsWith('-') ? "-" + field : field;

    internal static async Task<List<T>> AllAsync<T>(SiloApiClient client, string path, CancellationToken ct, int limit = 100)
    {
        var context = client.CaptureContext();
        var result = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Browse context changed.", ct);
            var query = path + (path.Contains('?') ? "&" : "?") + $"limit={limit}";
            if (cursor != null) query += "&cursor=" + Uri.EscapeDataString(cursor);
            var page = await client.GetAsync<BrowseCollection<T>>(query, ct);
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Browse context changed.", ct);
            result.AddRange(page.Items);
            if (page.Page?.HasMore != true) return result;
            cursor = page.Page.NextCursor;
            if (page.Items.Count == 0 || string.IsNullOrEmpty(cursor) || !seen.Add(cursor))
                throw new InvalidOperationException("The server returned a non-advancing browse cursor.");
        } while (true);
    }

    // Offset callers outside the virtual catalog still exist. Walk signed cursors;
    // never synthesize them or send the rejected v1 offset parameter.
    internal static async Task<BrowseCollection<T>> WindowAsync<T>(SiloApiClient client,
        string path, int limit, int offset, CancellationToken ct, int maxPageSize = 200)
    {
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<T>();
        var context = client.CaptureContext();
        while (true)
        {
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Browse context changed.", ct);
            var query = path + (path.Contains('?') ? "&" : "?") + $"limit={Math.Min(limit, maxPageSize)}";
            if (cursor != null) query += "&cursor=" + Uri.EscapeDataString(cursor);
            var page = await client.GetAsync<BrowseCollection<T>>(query, ct);
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Browse context changed.", ct);
            var skipped = Math.Min(offset, page.Items.Count);
            offset -= skipped;
            var available = page.Items.Count - skipped;
            var take = Math.Min(limit - items.Count, available);
            items.AddRange(page.Items.Skip(skipped).Take(take));
            if (items.Count == limit || page.Page?.HasMore != true)
                return new BrowseCollection<T>
                {
                    Items = items, Total = page.Total, TotalExact = page.TotalExact,
                    Page = new() { HasMore = available > take || page.Page?.HasMore == true }
                };
            cursor = page.Page.NextCursor;
            if (page.Items.Count == 0 || string.IsNullOrEmpty(cursor) || !seen.Add(cursor))
                throw new InvalidOperationException("The server returned a non-advancing browse cursor.");
        }
    }
}
