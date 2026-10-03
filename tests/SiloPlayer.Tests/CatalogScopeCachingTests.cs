using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class CatalogScopeCachingTests
{
    [Fact]
    public async Task TextRefinementReusesMetadataForTheSameScope()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        var first = await api.GetFiltersAsync(source: "query", type: "movie", q: "Crime");
        var second = await api.GetFiltersAsync(source: "query", type: "movie", q: "Drama");
        Assert.Same(first, second); Assert.Single(wire.Paths);
        Assert.DoesNotContain("q=", wire.Paths[0]);
    }
    [Fact]
    public async Task TypeCollectionAndProfileHaveIndependentMetadata()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        var movie = await api.GetFiltersAsync(source: "query", type: "movie");
        var series = await api.GetFiltersAsync(source: "query", type: "series");
        var collection = await api.GetFiltersAsync(source: "user_collection", type: "movie", collectionId: "one");
        var otherCollection = await api.GetFiltersAsync(source: "user_collection", type: "movie", collectionId: "two");
        wire.Client.SetProfile("second");
        var nextProfile = await api.GetFiltersAsync(source: "query", type: "movie");
        Assert.Equal(5, wire.Paths.Count); Assert.NotSame(movie, series); Assert.NotSame(collection, otherCollection); Assert.NotSame(movie, nextProfile);
    }
    [Fact]
    public async Task FailureDoesNotBecomeASuccessfulCachedEmptyScope()
    {
        using var wire = new Wire { FailOnce = true }; var api = new CatalogApi(wire.Client);
        await Assert.ThrowsAsync<ApiException>(() => api.GetFiltersAsync(source: "query", type: "movie"));
        var recovered = await api.GetFiltersAsync(source: "query", type: "movie");
        var reused = await api.GetFiltersAsync(source: "query", type: "movie");
        Assert.Equal(2, wire.Paths.Count); Assert.Same(recovered, reused); Assert.Contains("Drama", recovered.Genres);
    }
    [Fact]
    public async Task IgnoredCancellationOldProfileResponseCannotPopulateTheNewCache()
    {
        using var wire = new Wire { DelayFirst = true }; var api = new CatalogApi(wire.Client);
        var old = api.GetFiltersAsync(source: "query", type: "movie"); await wire.Started.Task;
        wire.Client.SetProfile("second"); var current = await api.GetFiltersAsync(source: "query", type: "movie");
        wire.Release.TrySetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await old);
        Assert.Same(current, await api.GetFiltersAsync(source: "query", type: "movie")); Assert.Equal(2, wire.Paths.Count);
    }
    [Fact]
    public async Task InvalidatedPendingMetadataCannotRepopulateAndPersonTechnicalScopesAreIndependent()
    {
        using var wire = new Wire { DelayFirst = true }; var api = new CatalogApi(wire.Client);
        var old = api.GetFiltersAsync(personId: "actor-one", includeTechnical: false); await wire.Started.Task;
        api.InvalidateFilterCache(); wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await old);
        var current = await api.GetFiltersAsync(personId: "actor-one", includeTechnical: false);
        Assert.Same(current, await api.GetFiltersAsync(personId: "actor-one", includeTechnical: false));
        Assert.NotSame(current, await api.GetFiltersAsync(personId: "actor-two", includeTechnical: false));
        Assert.NotSame(current, await api.GetFiltersAsync(personId: "actor-one", includeTechnical: true));
        Assert.Equal(4, wire.Paths.Count); Assert.Contains("include_technical=false", wire.Paths[1]);
    }
    [Fact]
    public async Task AccessGenerationRetiresCompletedMetadataEvenWithTheSameProfile()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        var previous = await api.GetFiltersAsync(libraryId: 1);
        wire.Client.InvalidateAccessContext();
        var current = await api.GetFiltersAsync(libraryId: 1);
        Assert.NotSame(previous, current); Assert.Same(current, await api.GetFiltersAsync(libraryId: 1)); Assert.Equal(2, wire.Paths.Count);
    }
    [Fact]
    public async Task MountedLibraryModelReloadsMetadataWhenAuthorityOrCacheGenerationChanges()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        var model = new SiloPlayer.ViewModels.LibraryViewModel(api) { Library = new() { Id = 1, Type = "movie" } };
        await model.EnsureFiltersLoadedAsync(); await model.EnsureFiltersLoadedAsync(); Assert.Single(wire.Paths);
        api.InvalidateFilterCache(); await model.EnsureFiltersLoadedAsync(); Assert.Equal(2, wire.Paths.Count);
        wire.Client.InvalidateAccessContext(); await model.EnsureFiltersLoadedAsync(); Assert.Equal(3, wire.Paths.Count);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly SiloApiClient Client;
        internal readonly List<string> Paths = [];
        internal bool FailOnce, DelayFirst;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Wire() { Client = new SiloApiClient(new HttpClient(this)); Client.SetBaseUrl("https://scope-cache.invalid"); Client.SetProfile("first"); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            if (DelayFirst && Paths.Count == 1) { Started.TrySetResult(); await Release.Task; }
            if (FailOnce) { FailOnce = false; return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture\",\"message\":\"retry\"}") }; }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { genres = new[] { "Drama" } })) };
        }
    }
}
