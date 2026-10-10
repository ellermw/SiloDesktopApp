using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
namespace SiloPlayer.Tests;
public class HomeRecipePresentationTests
{
    [Fact] public void MostSpecificPresetWinsAndUnmatchedDraftHasNoSelection()
    {
        var common=new GalleryPreset{Key="base",DefaultParams=new(){["genre"]="Drama"}};
        var specific=new GalleryPreset{Key="specific",DefaultParams=new(){["genre"]="Drama",["min_rating"]=8d}};
        var definition=new RecipeDefinition{Presets=[common,specific]};
        Assert.Same(specific,HomeRecipePresentation.FindMatchingPreset(definition,new(){["genre"]="Drama",["min_rating"]=8d}));
        Assert.Null(HomeRecipePresentation.FindMatchingPreset(definition,new(){["genre"]="Comedy"}));
    }
    [Fact] public void PresetSelectionHandlesJsonValuesAndEmptyPresetWithoutMutatingDraft()
    {
        var values=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,object>>("{\"window\":\"7d\",\"future\":true}")!;
        var definition=new RecipeDefinition{Presets=[new(){Key="empty"},new(){Key="week",DefaultParams=new(){["window"]="7d"}}]};
        Assert.Equal("week",HomeRecipePresentation.FindMatchingPreset(definition,values)!.Key);
        Assert.True(values.ContainsKey("future"));
    }
    [Theory]
    [InlineData("hidden_gems","Rated 7.5+ on TMDB with 100+ votes, and never watched")]
    [InlineData("critically_acclaimed","Rated 8.0+ on TMDB with 500+ votes")]
    [InlineData("forgotten_favorites","Rated 7.0+ on TMDB with 100+ votes, and not watched in the past year")]
    [InlineData("genre_roulette","A different genre every week, rated 6.0+ on TMDB with 100+ votes")]
    [InlineData("short_watches","Movies of 95 minutes or less, rated 6.0+ on TMDB with 100+ votes")]
    [InlineData("recommended_for_you","Picked from your watch history")]
    public void CurrentProfileDescriptionsSayWhatActuallyQualifies(string type,string expected)
        => Assert.Equal(expected,HomeRecipePresentation.Describe(new(){SectionType=type}));
    [Fact] public void CustomFloorsPlayCountsAndCadenceAreReflected()
    {
        Assert.Equal("Rated 8.2+ on TMDB with 100+ votes, and watched twice or less",HomeRecipePresentation.Describe(new(){SectionType="hidden_gems",Config=new(){["min_rating"]=8.2,["max_play_count"]=2}}));
        Assert.Equal("A different genre every day, rated 6.5+ on TMDB with 100+ votes",HomeRecipePresentation.Describe(new(){SectionType="genre_roulette",Config=new(){["rotation_cadence"]="daily",["min_rating"]=6.5}}));
    }
    [Fact] public void RenamesAndLibraryScopeKeepTheirMeaning()
    {
        Assert.Equal("Renamed from Original",HomeRecipePresentation.Describe(new(){Title="Mine",DefaultTitle="Original",SectionType="favorites"}));
        Assert.Equal("Newest movies in this library",HomeRecipePresentation.Describe(new(){SectionType="recently_added",Config=new(){["media_scope"]="movie"}},true));
        Assert.Equal("1 title",HomeRecipePresentation.TitleCount(1));
    }
}
