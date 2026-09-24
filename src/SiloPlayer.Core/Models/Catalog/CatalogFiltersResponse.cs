namespace SiloPlayer.Core.Models.Catalog;
public class CatalogFiltersResponse
{
    public CatalogTechnicalFilters? Technical { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<string> Studios { get; set; } = [];
    public List<string> Networks { get; set; } = [];
    private List<string> _resolutions = [], _audioLanguages = [], _subtitleLanguages = [];
    public List<string> Resolutions { get => Technical?.Resolutions ?? _resolutions; set => _resolutions = value; }
    public List<string> AudioLanguages { get => Technical?.AudioLanguages ?? _audioLanguages; set => _audioLanguages = value; }
    public List<string> SubtitleLanguages { get => Technical?.SubtitleLanguages ?? _subtitleLanguages; set => _subtitleLanguages = value; }
    public List<string> OriginalLanguages { get; set; } = [];
    public List<string> ContentRatings { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<string> Authors { get; set; } = [];
    public List<string> Narrators { get; set; } = [];
    public List<string> Series { get; set; } = [];
}

public sealed class CatalogTechnicalFilters
{
    public List<string> Resolutions { get; set; } = [];
    public List<string> AudioLanguages { get; set; } = [];
    public List<string> SubtitleLanguages { get; set; } = [];
}
