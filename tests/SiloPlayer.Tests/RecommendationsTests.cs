using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Core.Services;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class RecommendationsTests
{
    [Fact]
    public async Task TasteProfileUsesCurrentServerContract()
    {
        var handler = new CaptureHandler("""
        {
          "top_genres":["Drama","Science Fiction"],
          "favorite_directors":["Denis Villeneuve"],
          "signal_counts":{"rated_5":3,"watch_high":4},
          "updated_at":"2026-07-11T12:00:00Z"
        }
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var profile = await new RecommendationsApi(client).GetTasteProfileAsync();

        Assert.Equal(new[] { "Drama", "Science Fiction" }, profile.TopGenres);
        Assert.Equal(new[] { "Denis Villeneuve" }, profile.FavoriteDirectors);
        Assert.Equal(7, profile.SignalCounts.Values.Sum());
        Assert.Equal("/api/v2/recommendations/taste-profile", handler.LastUri?.AbsolutePath);
    }

    [Fact]
    public async Task DiscoverUsesCurrentEndpointAndSectionMetadata()
    {
        var handler = new CaptureHandler("""
        {"items":[{"type":"genre","title":"Popular in Drama","kind":"genre","key":"Drama","items":[]}]}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var response = await new RecommendationsApi(client).GetDiscoverAsync();

        Assert.Equal("genre", response.Rows[0].SectionKind);
        Assert.Equal("Drama", response.Rows[0].SectionKey);
        Assert.Equal("/api/v2/recommendations/discover", handler.LastUri?.AbsolutePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataCompletionRereadsMountedRecommendationsAndInvalidatesOffscreenCopies(bool offscreen)
    {
        using var wire = new MetadataWire();
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://recommendations-metadata.invalid");
        var api = new RecommendationsApi(client);
        var recommendations = new RecommendationsViewModel(new CatalogApi(client), api);
        var section = new RecommendationSectionViewModel(api);
        var args = new RecommendationSectionNavigationArgs("genre", "drama", "Drama");
        try
        {
            await recommendations.LoadCommand.ExecuteAsync(null);
            await section.LoadAsync(args);
            var row = Assert.Single(recommendations.Rows);
            var keep = row.Items[2]; var sectionKeep = section.Items[2];
            if (offscreen) { recommendations.Suspend(); section.CancelLoad(); }
            var reads = wire.Reads;
            wire.Fresh = true;
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                MediaSurfaceChangeKind.ItemMetadataRefreshed, "old", context: client.CaptureContext()));
            if (offscreen)
            {
                Assert.Equal(reads, wire.Reads);
                Assert.False(section.Holds(args));
                Assert.False(section.TryResume(args));
                Assert.True(section.Retains(args)); // Back can reread without clearing or scrolling.
                recommendations.BeginVisit();
                await recommendations.LoadCommand.ExecuteAsync(null);
                await section.LoadAsync(args);
            }
            await Until(() => row.Items[0].ContentId == "replacement" && section.Items[0].ContentId == "replacement");
            Assert.Same(row, Assert.Single(recommendations.Rows));
            Assert.Same(keep, row.Items[2]); Assert.Same(sectionKeep, section.Items[2]);
            Assert.Equal("Fresh", row.Items[1].Title); Assert.Equal("/fresh.jpg", row.Items[1].PosterUrl);
            Assert.Equal("Fresh", section.Items[1].Title); Assert.Equal("/fresh.jpg", section.Items[1].PosterUrl);
            Assert.DoesNotContain(row.Items, item => item.ContentId == "old");
            Assert.DoesNotContain(section.Items, item => item.ContentId == "old");
            Assert.True(section.Holds(args));
        }
        finally { recommendations.Suspend(); section.CancelLoad(); WeakReferenceMessenger.Default.UnregisterAll(recommendations); WeakReferenceMessenger.Default.UnregisterAll(section); }
    }

    [Theory]
    [InlineData(MaintenanceJobWaitOutcome.Succeeded)]
    [InlineData(MaintenanceJobWaitOutcome.TimedOut)]
    public async Task DetailPageRefreshInvalidatesRecommendationsAndAHeldSectionForBack(MaintenanceJobWaitOutcome outcome)
    {
        using var wire = new MetadataWire();
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://recommendations-metadata.invalid");
        var api = new RecommendationsApi(client);
        var recommendations = new RecommendationsViewModel(new CatalogApi(client), api);
        var section = new RecommendationSectionViewModel(api);
        var args = new RecommendationSectionNavigationArgs("genre", "drama", "Drama");
        try
        {
            await recommendations.LoadCommand.ExecuteAsync(null);
            await section.LoadAsync(args);
            var row = Assert.Single(recommendations.Rows);
            // The detail page covers both surfaces while its refresh runs.
            recommendations.Suspend(); section.CancelLoad();
            var reads = wire.Reads;
            wire.Fresh = true;
            // What the detail page's follow publishes once its job ends or its wait runs out.
            foreach (var change in MetadataRefreshFollow.ChangedItems(new(outcome), "old", "quick", "movie", null))
                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                    MediaSurfaceChangeKind.ItemMetadataRefreshed, change.ContentId, context: client.CaptureContext()));
            Assert.Equal(reads, wire.Reads);
            Assert.False(section.Holds(args));
            Assert.True(section.Retains(args));
            // Back inside the five-minute cache window still rereads.
            recommendations.BeginVisit();
            await recommendations.LoadCommand.ExecuteAsync(null);
            await section.LoadAsync(args);
            await Until(() => row.Items[0].ContentId == "replacement" && section.Items[0].ContentId == "replacement");
            Assert.True(wire.Reads > reads);
            Assert.True(section.Holds(args));
        }
        finally { recommendations.Suspend(); section.CancelLoad(); WeakReferenceMessenger.Default.UnregisterAll(recommendations); WeakReferenceMessenger.Default.UnregisterAll(section); }
    }

    [Fact]
    public async Task CanceledSectionMetadataReadCannotRevalidateItsRetainedOwnership()
    {
        using var wire = new MetadataWire();
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://recommendations-metadata.invalid");
        var section = new RecommendationSectionViewModel(new RecommendationsApi(client));
        var args = new RecommendationSectionNavigationArgs("genre", "drama", "Drama");
        try
        {
            await section.LoadAsync(args);
            wire.Fresh = true; wire.HoldNext = true;
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                MediaSurfaceChangeKind.ItemMetadataRefreshed, "old", context: client.CaptureContext()));
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            section.CancelLoad();
            wire.Release.SetResult();
            Assert.False(section.Holds(args));
            Assert.True(section.Retains(args));
            await section.LoadAsync(args);
            Assert.True(section.Holds(args));
            Assert.Equal("replacement", section.Items[0].ContentId);
        }
        finally { wire.Release.TrySetResult(); section.CancelLoad(); WeakReferenceMessenger.Default.UnregisterAll(section); }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class MetadataWire : HttpMessageHandler
    {
        public int Reads;
        public bool Fresh, HoldNext;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/taste-profile")) return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            Interlocked.Increment(ref Reads);
            var items = Fresh
                ? """[{"content_id":"replacement","title":"Replacement"},{"content_id":"same","title":"Fresh","poster_url":"/fresh.jpg"},{"content_id":"keep","title":"Keep"}]"""
                : """[{"content_id":"old","title":"Old"},{"content_id":"same","title":"Old"},{"content_id":"keep","title":"Keep"}]""";
            if (HoldNext) { HoldNext = false; Started.SetResult(); await Release.Task; }
            var json = path.EndsWith("/discover")
                ? "{\"items\":[{\"title\":\"Drama\",\"type\":\"genre\",\"kind\":\"genre\",\"key\":\"drama\",\"items\":" + items + "}]}"
                : "{\"items\":" + items + "}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }
}
