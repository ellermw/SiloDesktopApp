using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PersonalCatalogSortPolicyTests
{
    [Theory]
    [InlineData("favorites")]
    [InlineData("watchlist")]
    public void SavedPersonalListsDefaultToListOrder(string source)
    {
        Assert.True(PersonalCatalogSortPolicy.SupportsSourceOrder(source));
        Assert.Equal("List Order", PersonalCatalogSortPolicy.DefaultSortLabel(source));
        Assert.False(PersonalCatalogSortPolicy.ShouldShowOrderSelector(source, null));
    }

    [Fact]
    public void HistoryKeepsItsDateAddedDefaultAndOrderSelector()
    {
        Assert.False(PersonalCatalogSortPolicy.SupportsSourceOrder("history"));
        Assert.Equal("Date Added", PersonalCatalogSortPolicy.DefaultSortLabel("history"));
        Assert.True(PersonalCatalogSortPolicy.ShouldShowOrderSelector("history", null));
    }

    [Theory]
    [InlineData("favorites")]
    [InlineData("watchlist")]
    public void ExplicitSortShowsTheOrderSelector(string source)
    {
        Assert.True(PersonalCatalogSortPolicy.ShouldShowOrderSelector(source, "added_at"));
    }
}
