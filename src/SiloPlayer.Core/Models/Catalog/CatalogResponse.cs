using SiloPlayer.Core.Models.Home;
namespace SiloPlayer.Core.Models.Catalog;
public class CatalogResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public int Total { get; set; }
    public bool TotalExact { get; set; } = true;
    public SiloPlayer.Core.Api.BrowsePage? Page { get; set; }
    private bool _hasMore;
    public bool HasMore { get => Page?.HasMore ?? _hasMore; set => _hasMore = value; }
    [System.Text.Json.Serialization.JsonPropertyName("window_cursor")]
    public string? Snapshot { get; set; }
    public SiloPlayer.Core.Models.Collections.QuerySort? EffectiveSort { get; set; }
}
