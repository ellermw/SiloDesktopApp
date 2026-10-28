using System.Text.Json;
using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Tests;

public sealed class CurrentCardPresentationTests
{
    [Theory]
    [InlineData("artwork", "large")]
    [InlineData("title", "compact")]
    [InlineData("title_metadata", "standard")]
    public void RetiredCaptionPreferenceCannotHideTitlesOrDetails(string oldCaption, string size)
    {
        var stored = JsonSerializer.Deserialize<CardPresentation>($$"""{"poster_size":"{{size}}","caption":"{{oldCaption}}"}""")!;
        var current = stored.Normalize();
        Assert.Equal(size, current.PosterSize);
        Assert.Equal("title_metadata", current.Caption);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(current));
        Assert.Equal("title_metadata", saved.RootElement.GetProperty("caption").GetString());
        Assert.Equal(size, saved.RootElement.GetProperty("poster_size").GetString());
    }
}
