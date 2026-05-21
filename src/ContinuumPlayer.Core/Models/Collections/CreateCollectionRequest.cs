namespace ContinuumPlayer.Core.Models.Collections;

public class CreateCollectionRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CollectionType { get; set; }
    public bool? IsShared { get; set; }
    public List<string>? AllowedProfileIds { get; set; }
    public QueryDefinition? QueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public bool? IncludeInServerCollections { get; set; }
    public string? PosterSourceUrl { get; set; }
    public string? GroupId { get; set; }
}
