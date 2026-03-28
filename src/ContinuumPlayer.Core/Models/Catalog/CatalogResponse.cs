using ContinuumPlayer.Core.Models.Home;
namespace ContinuumPlayer.Core.Models.Catalog;
public class CatalogResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public int Total { get; set; }
    public bool HasMore { get; set; }
}
