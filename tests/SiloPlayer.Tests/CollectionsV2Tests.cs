using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Tests;

public sealed class CollectionsV2Tests
{
    [Fact]
    public async Task OfficialCollectionFixturesRetainOwnerAndMediaIdentifiers()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        var fixtures = Path.Combine(directory!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "CollectionsV2");
        var api = Api(request => Json(File.ReadAllText(Path.Combine(fixtures,
            request.RequestUri!.AbsolutePath.EndsWith("/items") ? "get_collection_items_ok.json" : "list_collections_ok.json"))));
        Assert.Equal("p-owner", Assert.Single((await api.GetCollectionsAsync()).Collections).CreatorProfileId);
        Assert.Equal("movie:heat-1995", Assert.Single((await api.GetCollectionItemsAsync("c1")).Items).MediaItemId);
    }

    [Fact]
    public async Task ReorderUsesTheRevisionOfTheSelectedGroup()
    {
        var api = Api(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("?group_id=g%2F1", request.RequestUri!.Query);
                var response = Json("{}");
                response.Headers.ETag = new("\"group-revision\"");
                return response;
            }
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("", request.RequestUri!.Query);
            Assert.Equal("\"group-revision\"", request.Headers.IfMatch.Single().Tag);
            return Json("{}");
        });
        await api.ReorderCollectionsAsync(["c1"], "g/1");
    }

    [Fact]
    public async Task CollectionListMapsItemsAndPreservesGroups()
    {
        var api = Api(request => {
            Assert.Equal("/api/v2/collections", request.RequestUri!.AbsolutePath);
            return Json("""{"items":[{"id":"c1","name":"Movies"}],"groups":[{"id":"g1","name":"Mine"}]}""");
        });
        var result = await api.GetCollectionsAsync();
        Assert.Equal("c1", Assert.Single(result.Collections).Id);
        Assert.Equal("g1", Assert.Single(result.Groups).Id);
    }

    [Fact]
    public async Task UpdateUsesConditionalPatchAndPreservesServerConflict()
    {
        var count = 0;
        var api = Api(request => {
            Assert.Equal("/api/v2/collections/c1", request.RequestUri!.AbsolutePath);
            if (++count == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                var response = Json("""{"id":"c1"}""");
                response.Headers.ETag = new("\"revision-1\"");
                return response;
            }
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("\"revision-1\"", request.Headers.IfMatch.Single().Tag);
            var conflict = Json("""{"type":"https://siloserver.org/docs/api/v2/problems/precondition_failed","status":412,"detail":"Changed elsewhere"}""");
            conflict.StatusCode = HttpStatusCode.PreconditionFailed;
            return conflict;
        });
        var error = await Assert.ThrowsAsync<ApiException>(() => api.UpdateCollectionAsync("c1", new() { Name = "New" }));
        Assert.Equal("precondition_failed", error.ErrorCode);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ReorderRejectsAnOrderChangedSinceTheListWasDisplayed()
    {
        var writes = 0;
        var api = Api(request =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            if (request.RequestUri!.AbsolutePath == "/api/v2/collections")
                return Json("""{"items":[{"id":"a","sort_order":0},{"id":"b","sort_order":1}],"groups":[]}""");
            var response = Json("""{"ordered_ids":["b","a"]}""");
            response.Headers.ETag = new("\"newer-order\"");
            return response;
        });
        await api.GetCollectionsAsync();
        var error = await Assert.ThrowsAsync<ApiException>(() => api.ReorderCollectionsAsync(["b", "a"], null));
        Assert.Equal(412, error.StatusCode);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task PosterUrlEditUsesMultipartArtworkOperationAfterMetadataPatch()
    {
        var calls = 0;
        var client = new SiloApiClient(new HttpClient(new AsyncHandler(async request =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
            {
                var response = Json("""{"id":"c1"}""");
                response.Headers.ETag = new("\"revision\"");
                return response;
            }
            var body = await request.Content!.ReadAsStringAsync();
            if (request.Method == HttpMethod.Patch)
            {
                Assert.DoesNotContain("poster_source_url", body);
                Assert.Contains("New name", body);
            }
            else
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.EndsWith("/c1/poster", request.RequestUri!.AbsolutePath);
                Assert.Equal("multipart/form-data", request.Content.Headers.ContentType!.MediaType);
                Assert.Contains("source_url", body);
                Assert.Contains("https://art.invalid/poster.jpg", body);
            }
            return Json("""{"id":"c1"}""");
        })));
        client.SetBaseUrl("https://fixture.invalid");
        await new CollectionsApi(client).UpdateCollectionAsync("c1", new() { Name = "New name", PosterSourceUrl = "https://art.invalid/poster.jpg" });
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArtworkRetryUsesTheBaselineOfItsOwnSuccessfulMetadataWrite(bool changedElsewhere)
    {
        var name = "Original"; var writes = 0; var posters = 0;
        var api = Api(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/poster"))
            {
                var response = Json("{\"id\":\"c1\",\"name\":\"Saved\"}");
                if (++posters == 1) response.StatusCode = HttpStatusCode.UnprocessableEntity;
                return response;
            }
            if (request.Method == HttpMethod.Patch) { writes++; name = "Saved"; }
            var item = new { id = "c1", name, updated_at = name };
            var reply = Json(JsonSerializer.Serialize(path == "/api/v2/collections" ? (object)new { items = new[] { item }, groups = Array.Empty<object>() } : item));
            // PATCH deliberately omits a revision: retry must refresh safely.
            if (request.Method == HttpMethod.Get) reply.Headers.ETag = new("\"" + name + "\"");
            return reply;
        });
        await api.GetCollectionsAsync(); await api.GetCollectionAsync("c1");
        await Assert.ThrowsAsync<ApiException>(() => api.UpdateCollectionAsync("c1", new() { Name = "Saved" }, "cover.png", [1, 2], "image/png"));
        if (changedElsewhere)
        {
            name = "Changed elsewhere";
            var conflict = await Assert.ThrowsAsync<ApiException>(() => api.UpdateCollectionAsync("c1", new() { Name = "Saved" }, "cover.png", [1, 2], "image/png"));
            Assert.Equal(412, conflict.StatusCode); Assert.Equal(1, writes); Assert.Equal(1, posters);
            return;
        }
        await api.UpdateCollectionAsync("c1", new() { Name = "Saved" }, "cover.png", [1, 2], "image/png");
        Assert.Equal(2, writes); Assert.Equal(2, posters);
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>handler(request); }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) {Content=new StringContent(text,Encoding.UTF8,"application/json")};
    private static CollectionsApi Api(Func<HttpRequestMessage,HttpResponseMessage> handler)
    {
        var client = new SiloApiClient(new HttpClient(new Handler(handler)));
        client.SetBaseUrl("https://fixture.invalid");
        return new(client);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(handler(request)); }
}
