using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class Oct24UiContractTests
{
    [Theory]
    [InlineData("Continuing")]
    [InlineData("Ended")]
    [InlineData("Unknown lifecycle")]
    public void MetadataLifecycleCannotBecomeMatchStatus(string lifecycle)
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = "series", Title = "Original", ShowStatus = lifecycle });
        Assert.False(state.Fields.ContainsKey("status"));
        state.Set("title", "Corrected");
        var changes = state.Changes([]);
        Assert.Equal("Corrected", changes["title"]);
        Assert.Equal(new[] { "locked_fields", "title" }, changes.Keys.Order());
    }

    [Fact]
    public void EmptySameServerPageRestoresServerRows()
    {
        var file = HomeLayoutTransfer.Parse(HomeLayoutTransfer.Serialize(new HomeLayoutFile { ServerId = "same", Pages = [new()] }));
        var plan = HomeLayoutTransfer.Plan(file, Target("same"));
        var page = Assert.Single(plan.Pages);
        Assert.Empty(page.Overrides);
        Assert.Empty(HomeLayoutMerge.Build(page, [], true));
        Assert.Empty(HomeLayoutTransfer.Plan(file, Target("other")).Pages);
    }

    [Theory]
    [InlineData("{\"scope\":\"home\",\"overrides\":[null]}")]
    [InlineData("{\"scope\":\"home\"}")]
    [InlineData("{\"overrides\":[]}")]
    public void DamagedPageCannotRestoreDefaults(string page)
    {
        var json = "{\"format\":\"silo-home-layout\",\"version\":1,\"server_id\":\"same\",\"pages\":[" + page + "]}";
        Assert.ThrowsAny<Exception>(() => HomeLayoutTransfer.Parse(json));
    }

    [Fact]
    public void ShowTitleHasAllTextOperatorsAndIsNotOfferedOnOlderServers()
    {
        var field = QueryFieldCatalog.Get("show_title");
        Assert.NotNull(field);
        Assert.Equal(new[] { "contains", "not_contains", "is", "is_not", "begins_with", "ends_with" }, field.Operators);
        Assert.DoesNotContain(field, QueryFieldCatalog.Offered("episode", true, null));
        foreach (var op in field.Operators) Assert.Equal("00123", QueryRuleValues.Parse("show_title", op, "00123"));
        Assert.Equal(false, QueryRuleValues.Parse("watched", "is", "false"));
        Assert.Contains(field, QueryFieldCatalog.Offered("episode", false, null, showTitleRules: true));
        foreach (var scope in new string?[] { null, "series", "all", "movie", "video" })
            Assert.DoesNotContain(field, QueryFieldCatalog.Offered(scope, true, null, showTitleRules: true));
    }

    private static HomeLayoutTarget Target(string server) => new(server, [], new Dictionary<string, bool>(), true, new HashSet<string>(), new HashSet<string>());
}
