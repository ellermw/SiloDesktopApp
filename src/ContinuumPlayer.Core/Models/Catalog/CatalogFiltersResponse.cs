namespace ContinuumPlayer.Core.Models.Catalog;
public class CatalogFiltersResponse
{
    public List<string> Genres { get; set; } = [];
    public List<string> Studios { get; set; } = [];
    public List<string> Resolutions { get; set; } = [];
    public List<string> AudioLanguages { get; set; } = [];
    public List<string> ContentRatings { get; set; } = [];
    public List<string> Countries { get; set; } = [];
}
