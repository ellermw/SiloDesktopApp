using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Models.Playback;

public sealed record ShuffleScopeRequest(string Kind, string Id);

public sealed class ShuffleScope
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ParentTitle { get; set; }
    [JsonIgnore] public string Label => string.IsNullOrWhiteSpace(ParentTitle) ? Title : $"{ParentTitle} · {Title}";
}

public sealed class Shuffle
{
    public string Id { get; set; } = "";
    public ShuffleScope Scope { get; set; } = new();
    public MediaItem Current { get; set; } = new();
    public MediaItem Next { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
