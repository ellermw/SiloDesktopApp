using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Tests;

public sealed class ItemMetadataEditorCurrentParityTests
{
    [Fact]
    public async Task ItemImageEndpoints_UseCurrentContractIncludingRevision()
    {
        string? requestJson = null;
        var handler = new DelegateHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("""
                    {
                      "images":[{"provider_id":"tmdb","url":"https://img.test/p.jpg","original_url":"https://origin.test/p.jpg","type":"poster","language":"en","width":1000,"height":1500,"rating":5.5}],
                      "current":{"poster_url":"stored/poster.jpg"},
                      "provider_errors":{"fanart":"timeout"}
                    }
                    """);
            }

            requestJson = await request.Content!.ReadAsStringAsync(ct);
            return JsonResponse("""
                {"content_id":"movie-1","stored_path":"items/movie-1/poster.jpg","thumbhash":"abc","image_url":"https://img.test/new.jpg","revision":"rev-2"}
                """);
        });
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        client.SetAccessToken("token");
        var api = new AdminApi(client);

        var images = await api.GetItemImagesAsync("movie-1");
        var applied = await api.ApplyItemImageAsync("movie-1", new ApplyItemImageRequest
        {
            OriginalUrl = "https://origin.test/p.jpg",
            Type = "poster",
            ProviderId = "tmdb",
        });

        Assert.Single(images.Images);
        Assert.Equal("tmdb", images.Images[0].ProviderId);
        Assert.Equal("timeout", images.ProviderErrors!["fanart"]);
        Assert.Contains("\"original_url\":\"https://origin.test/p.jpg\"", requestJson);
        Assert.Contains("\"provider_id\":\"tmdb\"", requestJson);
        Assert.Equal("https://img.test/new.jpg", applied.ImageUrl);
        Assert.Equal("rev-2", applied.Revision);
    }

    [Fact]
    public void NativeEditor_CoversCurrentWebUiSectionsAndLockGroups()
    {
        var source = File.ReadAllText(SourcePath(
            "src", "SiloPlayer", "Controls", "EditMetadataDialog.cs"));

        Assert.Contains("Dates & Ratings", source);
        Assert.Contains("Tags & Genres", source);
        Assert.Contains("External IDs", source);
        Assert.Contains("Image changes apply immediately", source);
        Assert.Contains("FieldReleaseDates = 13", source);
        Assert.Contains("FieldTags = 8", source);
        Assert.Contains("_item.Type != \"season\"", source);
        Assert.Contains("locked_fields", source);
        Assert.Contains("Reset to Provider", source);
        Assert.Contains("Reset & Refresh", source);
        Assert.Contains("This will unlock all fields and refresh metadata from your providers.", source);
        Assert.Contains("Any manual edits will be overwritten on the next refresh.", source);
        Assert.Contains("_tagline.PlaceholderText = \"No tagline\"", source);
        Assert.Contains("Add genre...", source);
        Assert.Contains("Add studio...", source);
        Assert.Contains("Add network...", source);
        Assert.Contains("Add country...", source);
        Assert.Contains("Translate with AI", source);
        Assert.Contains("IncludeChildren = _item.Type == \"series\"", source);
        Assert.Contains("GetItemMetadataTranslationJobsAsync", source);
        Assert.Contains("TimeSpan.FromMilliseconds(1500)", source);
        Assert.Contains("BuildChangedPayload()", source);
        Assert.Contains("if (payload.Count == 0)", source);
        Assert.Contains("PayloadValuesEqual", source);
    }

    [Fact]
    public async Task MetadataTranslationEndpoints_UseCurrentJobContract()
    {
        var requests = new List<(HttpMethod Method, string Path, string? Json)>();
        var handler = new DelegateHandler(async (request, ct) =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(ct)));
            return request.Method == HttpMethod.Post
                ? JsonResponse("""{"job":{"id":42,"content_id":"series-1","status":"pending","target_language":"fr","include_children":true,"force":true}}""")
                : JsonResponse("""{"jobs":[{"id":42,"content_id":"series-1","status":"running","progress_message":"Translating episodes","fields_done":3,"fields_total":10}]}""");
        });
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        client.SetAccessToken("token");
        var api = new AdminApi(client);

        var started = await api.StartItemMetadataTranslationAsync("series-1", new TranslateItemMetadataRequest
        {
            TargetLanguage = "fr",
            IncludeChildren = true,
            Force = true,
        });
        var jobs = await api.GetItemMetadataTranslationJobsAsync("series-1");

        Assert.Equal(42, started.Job.Id);
        Assert.True(jobs.Jobs[0].IsActive);
        Assert.Contains(requests, request => request.Method == HttpMethod.Post
            && request.Path.EndsWith("/admin/items/series-1/metadata-translation"));
        Assert.Contains("\"target_language\":\"fr\"", requests[0].Json);
        Assert.Contains("\"include_children\":true", requests[0].Json);
        Assert.Contains(requests, request => request.Method == HttpMethod.Get
            && request.Path.EndsWith("/admin/items/series-1/metadata-translation/jobs"));
    }

    private static string SourcePath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "SiloPlayer")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. parts]);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
