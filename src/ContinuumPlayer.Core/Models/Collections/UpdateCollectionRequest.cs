namespace ContinuumPlayer.Core.Models.Collections;

public class UpdateCollectionRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsShared { get; set; }
    public List<string>? AllowedProfileIds { get; set; }
    public QueryDefinition? QueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public string? SourceUrl { get; set; }
    public int? MaxItems { get; set; }
    public bool? IncludeInServerCollections { get; set; }
    public string? PosterSourceUrl { get; set; }
}
