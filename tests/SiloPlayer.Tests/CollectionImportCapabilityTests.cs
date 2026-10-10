using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionImportCapabilityTests
{
    [Fact]
    public async Task PersonalImportRejectsTemplatesRequiringAnAdminSourceProfile()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://collection-fixture.invalid");
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client));
        var created = await vm.ImportTemplateAsync(new() { Title = "Fixture", Template = new() { Source = "tmdb", RequiresProfile = true, Tmdb = new() { Preset = "popular", MediaType = "movie" } } });
        Assert.Null(created); Assert.Empty(wire.Posts);
    }
    [Fact]
    public async Task StaleRetiredTemplateNeverSubmitsAndPublicTmdbListUsesItsRoute()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://collection-fixture.invalid");
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client));
        Assert.Null(await vm.ImportTemplateAsync(new() { Title = "Fixture", Template = new() { Source = "trakt", Trakt = new() { Preset = "trending", MediaType = "movie" } } }));
        Assert.Contains("no longer", vm.TemplateErrorMessage); Assert.Empty(wire.Posts);
        var created = await vm.ImportTemplateAsync(new() { Title = "Fixture", TMDBListUrl = "https://www.themoviedb.org/list/310-fixture", Template = new() { Source = "tmdb_list" } });
        Assert.NotNull(created); Assert.Equal("/api/v2/collections/import/tmdb-list", wire.Posts.Single());
        Assert.Equal("https://www.themoviedb.org/list/310-fixture", wire.Body!.Value.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("310")]
    [InlineData("https://www.themoviedb.org/list/310-fixture")]
    public void AcceptsPublicListIdOrTmdbListPage(string value) => Assert.True(CollectionImportPolicy.IsTMDBListUrl(value));

    [Theory]
    [InlineData("https://other.invalid/list/310")]
    [InlineData("https://www.themoviedb.org/movie/310")]
    [InlineData("https://themoviedb.org.evil.invalid/list/310")]
    public void RejectsWrongSource(string value) => Assert.False(CollectionImportPolicy.IsTMDBListUrl(value));

    private sealed class Wire : HttpMessageHandler
    {
        public List<string> Posts = []; public JsonElement? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post) { Posts.Add(request.RequestUri!.AbsolutePath); Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone(); }
            var json = request.RequestUri!.AbsolutePath.EndsWith("/capabilities") ? "{\"imports\":true,\"import_sources\":[\"mdblist\",\"tmdb\",\"tmdb_list\"]}" : "{\"collection\":{\"id\":\"new\",\"name\":\"Fixture\",\"collection_type\":\"tmdb_list\"}}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
