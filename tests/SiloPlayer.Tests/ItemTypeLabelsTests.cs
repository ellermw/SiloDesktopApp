using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

/// <summary>
/// A catalog item's kind reaches the screen in the interface language for every kind the server can send that the
/// catalog names, and any other kind shows as the server sent it (the official WebUI's itemTypeLabel).
/// </summary>
public sealed class ItemTypeLabelsTests
{
    private static AppLocale Locale(string language) => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));

    [Theory]
    [InlineData("movie", "web.search.typeMovie", "Movie")]
    [InlineData("series", "web.search.typeSeries", "Series")]
    [InlineData("season", "web.search.typeSeason", "Season")]
    [InlineData("episode", "web.search.typeEpisode", "Episode")]
    [InlineData("ebook", "web.search.typeEbook", "Ebook")]
    [InlineData("audiobook", "web.search.typeAudiobook", "Audiobook")]
    [InlineData("manga", "web.search.typeManga", "Manga")]
    public void EveryNamedKindIsShownInTheInterfaceLanguage(string type, string key, string english)
    {
        Assert.Equal(key, ItemTypeLabels.KeyFor(type));
        foreach (var language in new[] { "en", "de", "fr" })
            using (AppStrings.UseForCurrentFlow(Locale(language)))
                Assert.Equal(Locale(language).Get(key), ItemTypeLabels.Label(type));
        using (AppStrings.UseForCurrentFlow(Locale("en")))
        {
            Assert.Equal(english, ItemTypeLabels.Label(type));
            Assert.NotEqual(type, ItemTypeLabels.Label(type));
        }
    }

    [Theory]
    [InlineData("comic")]
    [InlineData("person")]
    [InlineData("Movie")]
    [InlineData("")]
    public void AnyOtherKindShowsAsTheServerSentIt(string type)
    {
        Assert.Null(ItemTypeLabels.KeyFor(type));
        using (AppStrings.UseForCurrentFlow(Locale("de")))
            Assert.Equal(type, ItemTypeLabels.Label(type));
    }

    [Fact]
    public void TheCollectionEditorTitleSearchLabelsItsRowsThroughTheHelper()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "CollectionEditorPage.TitleSearch.cs"));
        Assert.Contains("ItemTypeLabels.Label(item.Type)", source);
        Assert.DoesNotContain(": item.Type;", source);
    }
}
