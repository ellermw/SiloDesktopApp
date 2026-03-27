using ContinuumPlayer.Core.Models.Home;
namespace ContinuumPlayer.Core.Models.Catalog;
public class CatalogResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}
