using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class HomeLayoutPolicyTests
{
    [Fact]
    public void UnchangedLegacyRowKeepsItsAdminPositionAndNewIdsStayStable()
    {
        var ids = new Dictionary<string, string>();
        var sections = new[] {
            new SettingsSectionEntry { Id = "normal", SectionType = "recently_added", Position = 0 },
            new SettingsSectionEntry { Id = "legacy", Position = 1, Config = new() { ["source"] = "trakt" } },
            new SettingsSectionEntry { Id = "last", Position = 2 } };
        var first = HomeSectionWritePolicy.Build(sections, [], [], ids);
        var second = HomeSectionWritePolicy.Build(sections, [], [], ids);
        Assert.DoesNotContain(first, row => row.SectionId == "legacy");
        Assert.Equal(new int?[] { 0, 2 }, first.Select(row => row.Position));
        Assert.Equal(first.Select(row => row.Id), second.Select(row => row.Id));
        sections[1].Hidden = true;
        Assert.Contains(HomeSectionWritePolicy.Build(sections, [], [], ids), row => row.SectionId == "legacy" && row.Hidden == true && row.Id != null);
    }

    [Fact]
    public void CrossServerPreviewMapsOnlyUniqueLibrariesAndSkipsAccountReferences()
    {
        var file = new HomeLayoutFile { ServerId = "old", Libraries = [new() { Id = 1, Name = "Movies", Type = "movie" }],
            Pages = [new() { Scope = "library", LibraryId = 1, Overrides = [
                new() { IsUserAdded = true, UserSectionType = "recently_added", UserTitle = "Mapped", UserConfig = new() { ["filter_library_ids"] = new[] { 1 } } },
                new() { IsUserAdded = true, UserSectionType = "recently_added", UserTitle = "Account", UserConfig = new() { ["profile_id"] = "old-profile" } },
                new() { SectionId = "server-only", Title = "Server row" }] }] };
        var target = new HomeLayoutTarget("new", [new() { Id = 99, Name = " movies ", Type = "movie" }],
            new Dictionary<string, bool> { ["recently_added"] = false }, false, new HashSet<string>(), new HashSet<string>());
        var plan = HomeLayoutTransfer.Plan(HomeLayoutTransfer.Parse(HomeLayoutTransfer.Serialize(file)), target);
        Assert.False(plan.SameServer); Assert.Equal(99, Assert.Single(plan.Pages).LibraryId);
        var row = Assert.Single(plan.Pages[0].Overrides);
        Assert.NotNull(row.Id); Assert.Equal("Mapped", row.UserTitle);
        Assert.Contains("99", System.Text.Json.JsonSerializer.Serialize(row.UserConfig)); Assert.Equal(2, plan.Skipped.Count);
        var duplicate = target with { Libraries = [new() { Id = 99, Name = "Movies", Type = "movie" }, new() { Id = 100, Name = "Movies", Type = "movie" }] };
        Assert.Empty(HomeLayoutTransfer.Plan(file, duplicate).Pages);
    }

    [Fact]
    public void PreviewSkipsRetiredSourcesAndUnpermittedRecipes()
    {
        var file = new HomeLayoutFile { ServerId = "same", Pages = [new() { Overrides = [
            new() { UserSectionType = "custom_filter", UserTitle = "Custom" },
            new() { UserSectionType = "recently_added", UserConfig = new() { ["source"] = "trakt" } }] }] };
        var target = new HomeLayoutTarget("same", [], new Dictionary<string, bool> { ["custom_filter"] = true }, false, new HashSet<string>(), new HashSet<string>());
        var plan = HomeLayoutTransfer.Plan(file, target); Assert.Empty(plan.Pages); Assert.Equal(2, plan.Skipped.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"format\":\"silo-home-layout\",\"version\":1,\"pages\":[]}")]
    [InlineData("{\"format\":\"silo-home-layout\",\"version\":1,\"server_id\":\"x\",\"libraries\":[null],\"pages\":[]}")]
    [InlineData("{\"format\":\"silo-home-layout\",\"version\":2,\"pages\":[]}")]
    [InlineData("{\"format\":\"silo-home-layout\",\"version\":1,\"pages\":[{\"scope\":\"library\"}]}")]
    public void RejectsMalformedAndUnsupportedLayouts(string json)
        => Assert.ThrowsAny<Exception>(() => HomeLayoutTransfer.Parse(json));
}
