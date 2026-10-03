using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class CatalogRatingCapabilityTests
{
    [Fact]
    public async Task OptionalRatingsStayHiddenUntilCapabilityAndReuseTheCurrentAuthority()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        Assert.Empty(api.CachedShownRatingSources);
        var sources = await api.GetShownRatingSourcesAsync();
        Assert.Contains("rt_critic", sources); Assert.Same(sources, await api.GetShownRatingSourcesAsync()); Assert.Equal(1, wire.Reads);
        Assert.True(CatalogRatingSortPolicy.IsAvailable("rating_rt_critic", sources));
        Assert.False(CatalogRatingSortPolicy.IsAvailable("rating_rt_audience", sources));
        Assert.True(CatalogRatingSortPolicy.IsAvailable("rating_imdb", new HashSet<string>()));
        Assert.True(CatalogRatingSortPolicy.IsAvailable("rating_tmdb", new HashSet<string>()));
    }
    [Fact]
    public async Task FailureRetriesAndAnUnavailableCapabilityDoesNotShowSources()
    {
        using var wire = new Wire { FailOnce = true }; var api = new CatalogApi(wire.Client);
        await Assert.ThrowsAsync<ApiException>(() => api.GetShownRatingSourcesAsync()); Assert.Empty(api.CachedShownRatingSources);
        wire.State = "unsupported";
        Assert.Empty(await api.GetShownRatingSourcesAsync()); Assert.Equal(2, wire.Reads);
        Assert.Empty(await api.GetShownRatingSourcesAsync()); Assert.Equal(2, wire.Reads);
    }
    [Fact]
    public async Task AccessInvalidationRejectsAnIgnoredCancellationReplyAndReloadsCapability()
    {
        using var wire = new Wire { DelayFirst = true }; var api = new CatalogApi(wire.Client);
        var previous = api.GetShownRatingSourcesAsync(); await wire.Started.Task;
        wire.Client.InvalidateAccessContext(); api.InvalidateRatingCapability();
        IReadOnlySet<string> current;
        try { current = await api.GetShownRatingSourcesAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        finally { wire.Release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await previous);
        Assert.Equal(2, wire.Reads); Assert.Contains("rt_critic", current);
        wire.Client.SetProfile("second"); Assert.Empty(api.CachedShownRatingSources);
        await api.GetShownRatingSourcesAsync(); Assert.Equal(3, wire.Reads);
    }
    [Fact]
    public void HiddenSavedEditorSortIsRetainedButDoesNotOverrideMediaRelevance()
    {
        var empty = new HashSet<string>();
        Assert.True(CatalogRatingSortPolicy.IsAvailable("rating_rt_audience", empty, "rating_rt_audience"));
        Assert.False(CatalogRatingSortPolicy.AppliesToScope("rating_rt_audience", "audiobook"));
        Assert.False(CatalogRatingSortPolicy.AppliesToScope("last_air_date", "all"));
        Assert.True(CatalogRatingSortPolicy.AppliesToScope("last_air_date", "episode"));
        Assert.False(CatalogRatingSortPolicy.AppliesToScope("runtime", "manga"));
        Assert.True(CatalogRatingSortPolicy.AppliesToScope("author", "manga"));
        Assert.False(CatalogRatingSortPolicy.AppliesToScope("narrator", "ebook"));
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly SiloApiClient Client;
        internal int Reads; internal bool FailOnce, DelayFirst; internal string State = "available";
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Wire() { Client = new(new HttpClient(this)); Client.SetBaseUrl("https://ratings-capability.invalid"); Client.SetProfile("first"); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/v2/capabilities/ratings", request.RequestUri!.AbsolutePath);
            Reads++; if (DelayFirst && Reads == 1) { Started.TrySetResult(); await Release.Task; }
            if (FailOnce) { FailOnce = false; return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture\",\"message\":\"retry\"}") }; }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { state = State, sources = new[] { new { source = "imdb", name = "IMDb" }, new { source = "tmdb", name = "TMDB" }, new { source = "rt_critic", name = "RT Critic" } } })) };
        }
    }
}
