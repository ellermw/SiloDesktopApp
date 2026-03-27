namespace ContinuumPlayer.Core.Models.Catalog;
public class Library
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? PosterUrl { get; set; }
}
