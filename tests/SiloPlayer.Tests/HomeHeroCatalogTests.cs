using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class HomeHeroCatalogTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(7, "?library_id=7")]
    public async Task HeroDetailKeepsItsLibraryAndEscapesTheContentId(int? libraryId, string query)
    {
        using var handler = new Wire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://hero-library.invalid"); client.SetProfile("alpha");
        var response = await new CatalogApi(client).GetItemDetailAsync("book/id", libraryId);
        Assert.Equal("book/id", response.ContentId);
        Assert.EndsWith("/api/v2/catalog/items/book%2Fid" + query, handler.Uri);
        Assert.Equal("alpha", handler.Profile);
    }

    [Fact]
    public async Task RetiredAuthorityCannotReturnAUsableHeroDetail()
    {
        using var handler = new Wire { Hold = true }; using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://hero-library.invalid"); client.SetProfile("alpha");
        var pending = new CatalogApi(client).GetItemDetailAsync("book/id", 7);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        client.InvalidateAccessContext(); handler.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool Hold;
        internal string? Uri, Profile;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("hero-library.invalid", request.RequestUri!.Host);
            Uri = request.RequestUri.AbsoluteUri;
            Profile = request.Headers.GetValues("X-Profile-Id").Single();
            Started.TrySetResult(); if (Hold) await Release.Task;
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"content_id":"book/id","type":"audiobook"}""") };
        }
    }
}
