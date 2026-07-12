namespace SiloPlayer.Core.Models.Catalog;
public class CatalogFiltersResponse
{
    public List<string> Genres { get; set; } = [];
    public List<string> Studios { get; set; } = [];
    public List<string> Networks { get; set; } = [];
    public List<string> Resolutions { get; set; } = [];
    public List<string> AudioLanguages { get; set; } = [];
    public List<string> SubtitleLanguages { get; set; } = [];
    public List<string> OriginalLanguages { get; set; } = [];
    public List<string> ContentRatings { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<string> Authors { get; set; } = [];
    public List<string> Narrators { get; set; } = [];
    public List<string> Series { get; set; } = [];
}
