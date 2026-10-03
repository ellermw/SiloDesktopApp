using System.Text.Json;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NotificationNavigationTests
{
    private static AppNotification Read(string json) => JsonSerializer.Deserialize<AppNotification>(json,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    [Theory]
    [InlineData("request.approved", "movie")]
    [InlineData("request.declined", "series")]
    public void RequestsWithoutLibraryItemNavigateToPayloadTitle(string type, string mediaType)
    {
        var notification = Read(JsonSerializer.Serialize(new { type, reason_flags = new { media_type = mediaType, tmdb_id = 321 } }));
        Assert.Equal(new NotificationDestination(null, mediaType, 321), NotificationNavigation.Resolve(notification));
    }

    [Theory]
    [InlineData("movie", 0)]
    [InlineData("series", -1)]
    [InlineData("episode", 321)]
    public void InvalidRequestPayloadHasNoDestination(string type, int id)
    {
        var notification = Read(JsonSerializer.Serialize(new { type = "request.approved", reason_flags = new { media_type = type, tmdb_id = id } }));
        Assert.Null(NotificationNavigation.Resolve(notification));
    }

    [Fact]
    public void CatalogEpisodeTakesPrecedenceOverSeriesAndRequestPayload()
    {
        var notification = Read("""{"type":"request.approved","episode_id":"ep","series_id":"show","reason_flags":{"media_type":"series","tmdb_id":321}}""");
        Assert.Equal(new NotificationDestination("ep", null, null), NotificationNavigation.Resolve(notification));
    }

    [Fact]
    public void FulfilledRequestAndEpisodeNotificationsKeepCatalogNavigation()
    {
        Assert.Equal(new NotificationDestination("show", null, null), NotificationNavigation.Resolve(
            Read("""{"type":"request.available","series_id":"show"}""")));
        Assert.Null(NotificationNavigation.Resolve(Read("""{"type":"unknown","reason_flags":null}""")));
    }
}
