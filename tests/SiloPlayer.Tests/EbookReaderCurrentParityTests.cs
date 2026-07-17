using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

public sealed class EbookReaderCurrentParityTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static string Markup => Read("src", "SiloPlayer", "Views", "EbookReaderPage.xaml");
    private static string CodeBehind => Read("src", "SiloPlayer", "Views", "EbookReaderPage.xaml.cs");

    [Fact]
    public void ReaderConfigEnvelopeDeserializesCurrentNestedSettingsShape()
    {
        const string json = """
        {
          "content_id": "book-1",
          "config": {
            "settings": {
              "theme": "sepia",
              "fontFamily": "inherit",
              "fontSize": 126,
              "fontWeight": 500,
              "readingRulerTop": 42
            }
          }
        }
        """;
        var value = JsonSerializer.Deserialize<EbookReaderConfigEnvelope>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        });

        Assert.NotNull(value);
        Assert.Equal(JsonValueKind.Object, value.Config["settings"].ValueKind);
        Assert.Equal(126, value.Config["settings"].GetProperty("fontSize").GetInt32());
    }

    [Fact]
    public void ReaderUsesCurrentWebUiToolbarPanelAndFileSelection()
    {
        Assert.Contains("x:Name=\"FileSelector\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PanelToggleButton\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DownloadButton\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EndOfBookNextButton\"", Markup, StringComparison.Ordinal);
        Assert.Contains("BuildFileSelector", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("LoadMangaNavigationAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenVersionAsync(version, restoreProgress: true)", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderPersistsCurrentSettingsContractAndMigratesLegacyKeys()
    {
        Assert.Contains("[\"settings\"] = new Dictionary<string, object?>", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("[\"fontFamily\"]", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("[\"fontWeight\"]", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("[\"readingRulerTop\"]", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("[\"fontFamily\", \"font_family\"]", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("[\"readingRulerTop\", \"reading_ruler_top\"]", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("FontSizeSlider.Value = 112", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("HyphenationCheck.IsChecked = true", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderCarriesCurrentAccessibilityAndReadingTools()
    {
        Assert.Contains("x:Name=\"VoiceCombo\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FontWeightSlider\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SpreadCombo\"", Markup, StringComparison.Ordinal);
        Assert.Contains("RulerDragButton_PointerMoved", Markup, StringComparison.Ordinal);
        Assert.Contains("PopulateVoicesAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateRulerOverlay", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("DisplayRequest", CodeBehind, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ignored.epub", "AZW3", "azw3")]
    [InlineData("book.fb2.zip", "zip", "fbz")]
    [InlineData("comic.cbz", "zip", "cbz")]
    [InlineData("comic.cbr", "rar", "cbr")]
    [InlineData("/library/book.pdf", "", "pdf")]
    public void ReaderFileFormatMatchesCurrentWebUiDetection(string fileName, string container, string expected)
    {
        var version = new FileVersion { FileName = fileName, Container = container };
        Assert.Equal(expected, SiloPlayer.Core.Services.EbookReaderFormat.Detect(version));
        Assert.True(SiloPlayer.Core.Services.EbookReaderFormat.IsSupported(version));
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));
}
