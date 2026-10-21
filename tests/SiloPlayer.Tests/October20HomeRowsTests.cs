using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class October20HomeRowsTests
{
    [Fact]
    public void FamilyNightCannotKeepInvisibleCustomHolidaysOrDates()
    {
        var draft = new Dictionary<string, object>
        {
            ["custom_holidays"] = new[] { new { name = "Birthday", start = "03-02", end = "03-02", list_url = "https://mdblist.com/lists/family/birthday" } },
            ["theme_dates"] = new { halloween = new { start = "09-01", end = "11-01" } },
            ["filter_library_ids"] = new[] { 9 }, ["source"] = "lists"
        };
        HomeRowVariants.Apply("seasonal_themed", draft, "se_family_movie_night");
        Assert.False(draft.ContainsKey("custom_holidays"));
        Assert.False(draft.ContainsKey("theme_dates"));
        Assert.Equal("lists", draft["source"]);
        Assert.Equal(new[] { 9 }, draft["filter_library_ids"]);
    }

    [Fact]
    public void CustomOnlyRowUsesHolidaysAndDefaultIncludesNewYearAndEaster()
    {
        var draft = new Dictionary<string, object> { ["custom_holidays"] = Array.Empty<object>() };
        Assert.Equal("se_auto", HomeRowVariants.Selected("seasonal_themed", draft));
        HomeRowVariants.Apply("seasonal_themed", draft, "se_auto");
        var themes = Assert.IsType<string[]>(draft["enabled_themes"]);
        Assert.Contains("new_year", themes);
        Assert.Contains("easter", themes);
    }

    [Fact]
    public void LayoutImportSkipsAddedNextUpWithoutDiscardingOtherRows()
    {
        var file = new HomeLayoutFile { ServerId = "same", Pages = [new() { Overrides = [
            new() { IsUserAdded = true, UserSectionType = "next_up", UserTitle = "On deck" },
            new() { IsUserAdded = true, UserSectionType = "recently_added", UserTitle = "Recent" }] }] };
        var target = new HomeLayoutTarget("same", [], new Dictionary<string, bool> { ["recently_added"] = false }, false, new HashSet<string>(), new HashSet<string>());
        var plan = HomeLayoutTransfer.Plan(HomeLayoutTransfer.Parse(HomeLayoutTransfer.Serialize(file)), target);
        Assert.Equal("recently_added", Assert.Single(Assert.Single(plan.Pages).Overrides).UserSectionType);
        Assert.Contains("Next Up", Assert.Single(plan.Skipped));
    }
}
