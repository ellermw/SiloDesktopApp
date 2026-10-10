using System.Net;
using System.Text;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class PlaybackLibraryScopeTests
{
    [Fact]
    public async Task WatchPreparationCarriesLibraryAndExplicitFileWithoutChangingLegacyRoute()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://scope-fixture.invalid"); client.SetProfile("fixture-profile");
        var api = new PlaybackApi(client);
        var scoped = await api.GetWatchDetailAsync("item one", libraryId: 7, fileId: 42);
        Assert.Equal("/api/v2/watch/item%20one?library_id=7&file_id=42", handler.Paths[0]);
        Assert.Equal(7, scoped.PreparedLibraryId); Assert.Equal(42, scoped.PreparedFileId);
        await api.GetWatchDetailAsync("item one"); Assert.Equal("/api/v2/watch/item%20one", handler.Paths[1]);
    }

    [Fact]
    public async Task CatalogSeasonEpisodeReadsPreserveSpecialZeroAndKnownLibraryScope()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://scope-fixture.invalid");
        var api = new CatalogApi(client);
        await api.GetSeasonsAsync("series one", libraryId: 7);
        await api.GetEpisodesAsync("series one", 0, libraryId: 7);
        await api.GetItemEpisodesAsync("season one", libraryId: 7);
        await api.GetSeasonsAsync("series one");
        Assert.Equal(new[] { "/api/v2/catalog/series/series%20one/seasons?library_id=7", "/api/v2/catalog/series/series%20one/seasons/0/episodes?library_id=7", "/api/v2/catalog/items/season%20one/episodes?library_id=7", "/api/v2/catalog/series/series%20one/seasons" }, handler.Paths);
    }

    [Fact]
    public async Task StaleWatchReplyCannotBeConsumedUnderAReplacementProfile()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://scope-fixture.invalid"); client.SetProfile("first");
        handler.BeforeReply = () => client.SetProfile("second");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlaybackApi(client).GetWatchDetailAsync("item", libraryId: 7, fileId: 42));
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<string> Paths = [];
        internal Action? BeforeReply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.PathAndQuery); BeforeReply?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("/watch/") ? "{\"content_id\":\"item\",\"versions\":[]}" : "{\"items\":[]}", Encoding.UTF8, "application/json") });
        }
    }
}
