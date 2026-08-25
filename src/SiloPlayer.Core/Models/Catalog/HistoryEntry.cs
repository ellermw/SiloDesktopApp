using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Models.Catalog;

// NOTE: Server GET /history returns {"items": [...]}, same shape as itemsListResponse.
// The server does NOT include "total" or "has_more" fields — they default to 0/false.
// The items are MediaItem-shaped objects (resolved from history entries).
public class HistoryResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public int Total { get; set; }
    public bool HasMore { get; set; }
}

public sealed class HistoryRemovalTarget
{
    public string ContentId { get; set; } = "";
    public string Scope { get; set; } = "item";
}

public sealed class RemoveHistoryRequest
{
    public List<HistoryRemovalTarget> Targets { get; set; } = [];
}
