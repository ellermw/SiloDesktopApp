using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Auth;

public class CreateProfileRequest
{
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Avatar { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pin { get; set; }
    public bool IsChild { get; set; }
    public string MaxContentRating { get; set; } = "";
    [JsonIgnore] public bool MaxAdvisoryAgeSupported { get; set; }
    [JsonIgnore] public bool RequireAdvisoryAgeSupported { get; set; }
    public int? MaxAdvisoryAge { get; set; }
    public bool RequireAdvisoryAge { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public bool LibraryRestrictionsEnabled { get; set; }
    public List<int> AllowedLibraryIds { get; set; } = [];
}
