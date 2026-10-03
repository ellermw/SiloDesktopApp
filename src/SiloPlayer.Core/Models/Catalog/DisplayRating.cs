using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Catalog;

/// <summary>A server-selected provider mark and already-formatted score, in server display order.</summary>
public sealed class DisplayRating
{
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("display")] public string Display { get; set; } = "";
}
