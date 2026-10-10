namespace SiloPlayer.Core.Models.Collections;

public class UpdateCollectionRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsShared { get; set; }
    public List<string>? AllowedProfileIds { get; set; }
    public QueryDefinition? QueryDefinition { get; set; }
    public List<int>? LibraryIds { get; set; }
    public DisplayQueryDefinition? DisplayQueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public string? SourceUrl { get; set; }
    public string? SyncSchedule { get; set; }
    public int? MaxItems { get; set; }
    public bool? IncludeInServerCollections { get; set; }
    public string? PosterSourceUrl { get; set; }
    public string? GroupId { get; set; }
}
