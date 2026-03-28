using System.Text.Json.Serialization;

namespace ContinuumPlayer.Core.Models.Catalog;
public class Library
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    [JsonPropertyName("poster_url")]
    public string? PosterUrl { get; set; }
    public bool Enabled { get; set; } = true;
}
