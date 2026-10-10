using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class LibraryRuleRoundTripBehaviorTests
{
    [Fact]
    public async Task LibraryScopeStillAppliesWhenSharedQueryGroupsAreActive()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://library-rules.invalid");
        var vm = new LibraryViewModel(new CatalogApi(client)) { Library = new Library { Id = 1, Type = "mixed" }, UseAdvancedRules = true, SelectedType = "ebook" };
        await vm.ApplyFilterCommand.ExecuteAsync(null);
        Assert.Contains("type=ebook", wire.LastCatalog!.Query);
    }

    [Fact]
    public void MigratingGuidedDraftRetainsAlternativeLanguagesAndDoesNotDuplicateGenres()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://library-rules.invalid");
        var vm = new LibraryViewModel(new CatalogApi(client)) { SelectedGenre = "Comedy", SelectedGenres = ["Comedy", "Drama"], SelectedOriginalLanguages = ["en", "fr"], SelectedType = "series" };
        vm.SeedAdvancedRulesFromGuided();
        Assert.Equal(2, vm.AdvancedGroups.SelectMany(group => group.Rules).Count(rule => rule.Field == "genre"));
        var languages = Assert.Single(vm.AdvancedGroups.Where(group => group.Rules.Any(rule => rule.Field == "original_language")));
        Assert.Equal("any", languages.Match);
        Assert.Equal(new[] { "en", "fr" }, languages.Rules.Select(rule => rule.Value));
        Assert.DoesNotContain(vm.AdvancedGroups.SelectMany(group => group.Rules), rule => rule.Field == "type");
    }
    [Fact]
    public async Task ApplyingLibraryRulesRetainsOpaquePropertiesFalseAndNullValuesInActualRequest()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://library-rules.invalid");
        var vm = new LibraryViewModel(new CatalogApi(client)) { Library = new Library { Id = 1, Type = "mixed" }, UseAdvancedRules = true, AdvancedRulesMatch = "any" };
        vm.AdvancedGroups.Clear(); var group = new EditableQueryGroup { Match = "any", AdditionalProperties = new() { ["future_group"] = JsonSerializer.Deserialize<JsonElement>("{\"retain\":true}") } };
        group.Rules.Add(new() { Field = "future_field", Op = "future_op", Value = false, AdditionalProperties = new() { ["future_rule"] = JsonSerializer.Deserialize<JsonElement>("{\"retain\":7.5}") } });
        group.Rules.Add(new() { Field = "future_null", Op = "exists", Value = null });
        vm.AdvancedGroups.Add(group);
        await vm.ApplyFilterCommand.ExecuteAsync(null);
        var parameters = wire.LastCatalog!.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
        Assert.Equal("any", parameters["match"]);
        using var groups = JsonDocument.Parse(parameters["groups"]);
        Assert.Equal("any", groups.RootElement[0].GetProperty("match").GetString());
        Assert.True(groups.RootElement[0].GetProperty("future_group").GetProperty("retain").GetBoolean());
        var rules = groups.RootElement[0].GetProperty("rules");
        Assert.Equal(2, rules.GetArrayLength());
        Assert.False(rules[0].GetProperty("value").GetBoolean());
        Assert.Equal(7.5, rules[0].GetProperty("future_rule").GetProperty("retain").GetDouble());
        Assert.Equal("future_null", rules[1].GetProperty("field").GetString());
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal Uri? LastCatalog;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/catalog") LastCatalog = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"total\":0,\"page\":{\"has_more\":false}}") });
        }
    }
}
