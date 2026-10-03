namespace SiloPlayer.Core.Models.Catalog;
public sealed class RatingsCapability
{
    public string State { get; set; } = "";
    public List<RatingCapabilitySource> Sources { get; set; } = [];
}
public sealed class RatingCapabilitySource
{
    public string Source { get; set; } = "";
    public string Name { get; set; } = "";
}