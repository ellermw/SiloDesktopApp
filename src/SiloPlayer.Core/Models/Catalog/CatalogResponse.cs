using SiloPlayer.Core.Models.Home;
namespace SiloPlayer.Core.Models.Catalog;
public class CatalogResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public int Total { get; set; }
    public bool TotalExact { get; set; } = true;
    public bool HasMore { get; set; }
    public string? Snapshot { get; set; }
    public SiloPlayer.Core.Models.Collections.QuerySort? EffectiveSort { get; set; }
}
