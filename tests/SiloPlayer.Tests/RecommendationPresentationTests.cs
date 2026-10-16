using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class RecommendationPresentationTests
{
    [Fact]
    public void SharedRowsExplainColdStartWithoutClaimingPersonalization()
    {
        Assert.Equal("Highly rated and recently added titles from your library. Watch and rate titles to personalize this page.",
            RecommendationPresentation.PageSubtitle(["top_rated", "recently_added"]));
        Assert.Equal("", RecommendationPresentation.PageSubtitle([]));
    }

    [Theory]
    [InlineData("cluster")]
    [InlineData("similar_users_liked")]
    public void PersonalRowsExplainHistoryAndRatings(string rowType) => Assert.Equal(
        "Personalized picks based on your viewing history and ratings.",
        RecommendationPresentation.PageSubtitle(["top_rated", rowType]));

    [Theory]
    [InlineData("similar-users", null, "Profiles Like You Enjoyed")]
    [InlineData("top-rated", null, "Highly Rated in Your Library")]
    [InlineData("genre", "Drama", "Top Drama")]
    [InlineData("genre", null, "Genre picks")]
    [InlineData("unknown", null, "Recommendations")]
    public void SectionHeadingsMatchCurrentUpstream(string kind, string? key, string expected) =>
        Assert.Equal(expected, RecommendationPresentation.FallbackTitle(kind, key));
}
