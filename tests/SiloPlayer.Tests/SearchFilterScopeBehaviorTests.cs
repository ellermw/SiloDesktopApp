using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class SearchFilterScopeBehaviorTests
{
    [Fact]
    public async Task EbookTypeChangeRemovesNarratorsBeforeDispatchAndRetainsOtherRuleGroups()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://filter-scope.invalid");
        var vm = new SearchViewModel(new CatalogApi(client), new PeopleApi(client), new RequestsApi(client), new SettingsApi(client)) { Query = "Example" };
        var retained = new QueryRule { Field = "future_field", Op = "future_op", Value = JsonSerializer.Deserialize<JsonElement>("{\"exact\":[false,7.5]}") };
        vm.AdvancedQuery.Match = "any";
        vm.AdvancedQuery.Groups.AddRange([
            new() { Rules = [new() { Field = "narrator", Op = "is", Value = "Narrator only" }] },
            new() { Match = "any", Rules = [retained, new() { Field = "narrator", Op = "is_not", Value = "Other narrator" }] },
            new() { Rules = [new() { Field = "genre", Op = "is", Value = "Drama" }] }
        ]);
        await vm.SetMediaTypeAsync("ebook");
        Assert.DoesNotContain(vm.AdvancedQuery.Groups.SelectMany(group => group.Rules), rule => rule.Field == "narrator");
        Assert.Equal(2, vm.AdvancedQuery.Groups.Count);
        Assert.Equal("any", vm.AdvancedQuery.Match);
        Assert.Equal("any", vm.AdvancedQuery.Groups[0].Match);
        Assert.Same(retained, vm.AdvancedQuery.Groups[0].Rules.Single());
        var parameters = wire.LastCatalog!.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
        Assert.Equal("ebook", parameters["type"]);
        Assert.Equal("any", parameters["match"]);
        using var groups = JsonDocument.Parse(parameters["groups"]);
        Assert.DoesNotContain(groups.RootElement.EnumerateArray().SelectMany(group => group.GetProperty("rules").EnumerateArray()), rule => rule.GetProperty("field").GetString() == "narrator");
        Assert.Equal("{\"exact\":[false,7.5]}", groups.RootElement[0].GetProperty("rules")[0].GetProperty("value").GetRawText());
    }

    private sealed class Wire : HttpMessageHandler
    {
        public Uri? LastCatalog { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = "{\"requests_enabled\":false}";
            if (path == "/api/v2/catalog/search/capabilities") body = "{\"people_media_scope\":true}";
            if (path == "/api/v2/catalog") { LastCatalog = request.RequestUri; body = "{\"items\":[],\"page\":{\"has_more\":false}}"; }
            if (path == "/api/v2/catalog/people") body = "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
