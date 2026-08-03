using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Models.Catalog;

public class ItemListResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public bool HasMore { get; set; }
}
