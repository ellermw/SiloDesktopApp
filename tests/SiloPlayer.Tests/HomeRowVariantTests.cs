using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
namespace SiloPlayer.Tests;
public class HomeRowVariantTests
{
    [Fact] public void MoodInferenceIgnoresCustomRatingButDoesNotGuessUnknownMood()
    {
        Assert.Equal("mood_comfort",HomeRowVariants.Selected("mood_collection",new(){["mood"]="comfort",["min_rating"]=9}));
        Assert.Null(HomeRowVariants.Selected("mood_collection",new(){["mood"]="unknown"}));
    }
    [Fact] public void SelectingVariantChangesOnlyItsKeys()
    {
        var draft=new Dictionary<string,object>{["mood"]="comfort",["min_rating"]=9,["future"]=true};
        HomeRowVariants.Apply("mood_collection",draft,"mood_feel_good");
        Assert.Equal("feel_good",draft["mood"]);Assert.Equal(9,draft["min_rating"]);Assert.True((bool)draft["future"]);
    }
    [Fact] public void UnrecognizedFormatSortHasNoSelectedCard()
    {
        Assert.Null(HomeRowVariants.Selected("format_showcase",new(){["format"]="4k",["sort"]="rating"}));
        Assert.Equal("fs_4k",HomeRowVariants.Selected("format_showcase",new(){["format"]="4k"}));
    }
    [Fact] public void SeasonalLegacyInferenceAndSpotlightRotationMatchWeb()
    {
        Assert.Equal("se_family_movie_night",HomeRowVariants.Selected("seasonal_themed",new(){["theme"]="family_movie_night"}));
        var draft=new Dictionary<string,object>{["subject_type"]="era",["subject"]="1980s",["min_rating"]=8};
        HomeRowVariants.Apply("editorial_spotlight",draft,"es_actor");
        Assert.Equal("actor",draft["subject_type"]);Assert.False(draft.ContainsKey("subject"));Assert.True((bool)draft["auto_rotate"]);Assert.Equal(8,draft["min_rating"]);
    }
}
