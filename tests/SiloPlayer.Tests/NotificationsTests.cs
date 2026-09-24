using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Tests;

public sealed class NotificationsTests
{
    [Fact]
    public void NotificationModelMatchesCurrentEpisodeAndReasonPresentation()
    {
        var notification = JsonSerializer.Deserialize<AppNotification>("""
        {
          "id":"notice-1",
          "type":"episode.available",
          "series_title":"The Series",
          "episode_title":"The Episode",
          "season_number":2,
          "episode_number":5,
          "reason_flags":{"favorite":true,"next_up":true},
          "created_at":"2026-07-11T12:00:00Z"
        }
        """, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        Assert.NotNull(notification);
        Assert.Equal("The Series", notification.DisplayTitle);
        Assert.Equal("S2E5 — The Episode", notification.Subtitle);
        Assert.Equal(new[] { "Favorite", "Next Up" }, notification.ReasonLabels);
    }

    [Fact]
    public async Task NotificationPagingSendsStatusAndCursor()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new NotificationsApi(client).GetNotificationsAsync("unread", "cursor/value", 25);

        Assert.Equal(
            "/api/v2/notifications?limit=25&status=unread&cursor=cursor%2Fvalue",
            handler.LastUri?.PathAndQuery);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"notifications\":[],\"next_cursor\":null}")
            });
        }
    }
}
