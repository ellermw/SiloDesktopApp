using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionRulePreservationTests
{
    [Fact]
    public async Task ManualCollectionSavesItsVisibleBrowseAndLibraryPreferences()
    {
        using var wire = new Wire { Manual = true }; var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://collection-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("fixture");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture");
        vm.WatchFilter = "unwatched"; vm.MediaFilter = "movie"; vm.IncludeInServerCollections = true;
        await vm.SaveCommand.ExecuteAsync(null); Assert.Null(vm.ErrorMessage);
        var patch = wire.Patch!.Value;
        Assert.True(patch.GetProperty("include_in_server_collections").GetBoolean());
        Assert.True(patch.TryGetProperty("display_query_definition", out _));
    }
    [Fact]
    public async Task NamingEditKeepsNullScopeMixedGroupsTypedValuesAndFutureFields()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://collection-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("fixture");
        var vm = new SmartCollectionWizardViewModel(new CatalogApi(client), new CollectionsApi(client), new AuthApi(client), auth);
        await vm.ConfigureAsync(new(CollectionId: "fixture")); Assert.Null(vm.ErrorMessage);
        vm.Title = "New name"; var query = vm.BuildQueryDefinition();
        Assert.Null(query.MediaScope); Assert.Equal("any", query.Match); Assert.Equal(2, query.Groups.Count);
        Assert.Equal("all", query.Groups[0].Match); Assert.Equal("any", query.Groups[1].Match);
        var json = JsonSerializer.SerializeToElement(query, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Equal(JsonValueKind.Array, json.GetProperty("groups")[0].GetProperty("rules")[0].GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.False, json.GetProperty("groups")[0].GetProperty("rules")[1].GetProperty("value").ValueKind);
        Assert.True(json.GetProperty("future_query_option").GetBoolean());
        Assert.Equal(3, json.GetProperty("groups")[1].GetProperty("future_group_option").GetInt32());
        Assert.Null(query.Limit);
    }

    [Fact]
    public async Task GeneralEditorSendsOriginalGroupsRatherThanFlatteningThem()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://collection-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("fixture");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture"); Assert.Null(vm.ErrorMessage);
        vm.Name = "Renamed"; await vm.SaveCommand.ExecuteAsync(null); Assert.Null(vm.ErrorMessage);
        var query = wire.Patch!.Value.GetProperty("query_definition");
        Assert.Equal("any", query.GetProperty("match").GetString());
        Assert.Equal(2, query.GetProperty("groups").GetArrayLength());
        Assert.Equal("all", query.GetProperty("groups")[0].GetProperty("match").GetString());
        Assert.Equal(JsonValueKind.Array, query.GetProperty("groups")[0].GetProperty("rules")[0].GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.False, query.GetProperty("groups")[0].GetProperty("rules")[1].GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task TMDBListModeUsesEditableUrlWhileRetainingTMDBCollectionType()
    {
        using var wire = new Wire { Imported = true }; var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://collection-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("fixture");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture");
        Assert.Equal("tmdb", vm.CollectionType); Assert.Equal("tmdb_list", vm.SourceKind); Assert.True(vm.HasEditableSourceUrl);
        vm.SourceUrl = "https://www.themoviedb.org/list/123";
        await vm.SaveCommand.ExecuteAsync(null); Assert.Null(vm.ErrorMessage);
        Assert.Equal(vm.SourceUrl, wire.Patch!.Value.GetProperty("source_url").GetString());
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool Imported, Manual;
        public JsonElement? Patch;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Patch) Patch = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            var json = request.RequestUri!.AbsolutePath.EndsWith("/collections/fixture")
                ? """{"id":"fixture","creator_profile_id":"fixture","name":"Old name","collection_type":"smart","query_definition":{"media_scope":null,"match":"any","future_query_option":true,"groups":[{"match":"all","rules":[{"field":"year","op":"between","value":[2020,2026]},{"field":"hdr","op":"is","value":false}]},{"match":"any","future_group_option":3,"rules":[{"field":"genre","op":"is","value":"Drama"}]}],"sort":{"field":"title","order":"asc"},"limit":null}}"""
                : "{\"items\":[]}";
            if (Imported && request.RequestUri!.AbsolutePath.EndsWith("/collections/fixture"))
                json = """{"id":"fixture","creator_profile_id":"fixture","name":"List","collection_type":"tmdb","source_url":"123","source_config":{"mode":"tmdb_list"}}""";
            if (Manual && request.RequestUri!.AbsolutePath.EndsWith("/collections/fixture"))
                json = """{"id":"fixture","creator_profile_id":"fixture","name":"Manual","collection_type":"manual"}""";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            response.Headers.ETag = new("\"fixture-version\""); return response;
        }
    }
}
