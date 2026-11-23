using System.Text.Json;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

// Official silo-server 3b2b32168, web/src/hooks/queries/notifications.ts notificationToast().
public sealed class NotificationToastRulesTests
{
    private static NotificationToastContent? For(string json)
    {
        using var document = JsonDocument.Parse(json);
        return NotificationToastRules.For(document.RootElement);
    }

    private static string Text(NotificationToastContent content)
        => content.Render(part => part.Text);

    [Fact]
    public void NewEpisodeIsTitledWithSeriesAndDescribedByCodeAndEpisodeTitle()
    {
        var toast = For("""{"type":"episode.available","series_title":"Severance","season_number":2,"episode_number":5,"episode_title":"Trojan Horse"}""")!;
        Assert.Equal(NotificationToastRules.NewEpisodeKey, toast.Title.Key);
        Assert.Equal("Severance", toast.Title.Arguments!["series"]);
        Assert.Equal("New episode of Severance", toast.Title.Text);
        Assert.Collection(toast.Description,
            code =>
            {
                Assert.Equal(NotificationToastRules.EpisodeCodeKey, code.Key);
                Assert.Equal(2L, code.Arguments!["season"]);
                Assert.Equal(5L, code.Arguments["episode"]);
                Assert.Equal("S2E5", code.Text);
            },
            episode => { Assert.Null(episode.Key); Assert.Equal("Trojan Horse", episode.Text); });
        Assert.Equal("New episode of Severance\nS2E5 — Trojan Horse", Text(toast));
    }

    [Theory]
    [InlineData("""{"type":"episode.available","series_title":"Show","episode_title":"Pilot"}""", "New episode of Show\nPilot")]
    [InlineData("""{"type":"episode.available","series_title":"Show","season_number":1,"episode_number":2}""", "New episode of Show\nS1E2")]
    [InlineData("""{"type":"episode.available","series_title":"Show","season_number":1,"episode_title":""}""", "New episode of Show")]
    [InlineData("""{"type":"episode.available","series_title":"Show","season_number":null,"episode_number":3,"episode_title":null}""", "New episode of Show")]
    public void EpisodeDescriptionDropsMissingPieces(string json, string expected)
        => Assert.Equal(expected, Text(For(json)!));

    [Theory]
    [InlineData("""{"type":"episode.available"}""")]
    [InlineData("""{"type":"episode.available","series_title":""}""")]
    [InlineData("""{"type":"episode.available","series_title":null,"episode_title":"Pilot"}""")]
    public void EpisodeWithoutSeriesTitleHasNoToast(string json) => Assert.Null(For(json));

    [Fact]
    public void FulfilledRequestWithSeriesTitle()
    {
        var toast = For("""{"type":"request.fulfilled","series_title":"Dune"}""")!;
        Assert.Equal(NotificationToastRules.SeriesAvailableKey, toast.Title.Key);
        Assert.Equal("Dune", toast.Title.Arguments!["series"]);
        Assert.Equal(NotificationToastRules.RequestArrivedKey, Assert.Single(toast.Description).Key);
        Assert.Equal("Dune is now available\nYour media request has arrived in the library.", Text(toast));
    }

    [Fact]
    public void FulfilledRequestWithoutSeriesTitle()
    {
        var toast = For("""{"type":"request.fulfilled","reason_flags":{"follower":false}}""")!;
        Assert.Equal(NotificationToastRules.RequestAvailableBodyKey, toast.Title.Key);
        Assert.Equal("Your request is now available\nYour media request has arrived in the library.", Text(toast));
        Assert.Equal("Your request is now available\nYour media request has arrived in the library.", Text(For("""{"type":"request.fulfilled"}""")!));
    }

    [Fact]
    public void FulfilledFollowedTitleUsesFollowerWording()
    {
        var bare = For("""{"type":"request.fulfilled","reason_flags":{"follower":true}}""")!;
        Assert.Equal(NotificationToastRules.FollowedTitleAvailableKey, bare.Title.Key);
        Assert.Equal(NotificationToastRules.FollowedArrivedKey, Assert.Single(bare.Description).Key);
        Assert.Equal("A title you followed is now available\nA title you asked to hear about has arrived in the library.", Text(bare));

        var named = For("""{"type":"request.fulfilled","series_title":"Dune","reason_flags":{"follower":true}}""")!;
        Assert.Equal(NotificationToastRules.SeriesAvailableKey, named.Title.Key);
        Assert.Equal(NotificationToastRules.FollowedArrivedKey, Assert.Single(named.Description).Key);
    }

    [Theory]
    [InlineData("""{"type":"request.fulfilled","reason_flags":{"follower":"true"}}""")]
    [InlineData("""{"type":"request.fulfilled","reason_flags":{"follower":1}}""")]
    [InlineData("""{"type":"request.fulfilled","reason_flags":null}""")]
    public void OnlyABooleanTrueFollowerFlagCounts(string json)
        => Assert.Equal(NotificationToastRules.RequestAvailableBodyKey, For(json)!.Title.Key);

    [Fact]
    public void WatchlistArrivalWithAndWithoutSeriesTitle()
    {
        var named = For("""{"type":"watchlist.available","series_title":"Andor"}""")!;
        Assert.Equal(NotificationToastRules.SeriesAvailableKey, named.Title.Key);
        Assert.Equal(NotificationToastRules.WatchlistArrivedKey, Assert.Single(named.Description).Key);
        Assert.Equal("Andor is now available\nA title on your watchlist has arrived in the library.", Text(named));

        var bare = For("""{"type":"watchlist.available"}""")!;
        Assert.Equal(NotificationToastRules.WatchlistTitleAvailableKey, bare.Title.Key);
        Assert.Equal("A title on your watchlist is now available\nA title on your watchlist has arrived in the library.", Text(bare));
    }

    [Fact]
    public void ApprovedRequestNamesItsTitleAndHasNoDescription()
    {
        var titled = For("""{"type":"request.approved","reason_flags":{"title":"Heat","reason":"ignored"}}""")!;
        Assert.Equal(NotificationToastRules.RequestApprovedTitleKey, titled.Title.Key);
        Assert.Equal("Heat", titled.Title.Arguments!["title"]);
        Assert.False(titled.HasDescription);
        Assert.Equal("Request approved: Heat", Text(titled));

        var bare = For("""{"type":"request.approved"}""")!;
        Assert.Equal(NotificationToastRules.RequestApprovedKey, bare.Title.Key);
        Assert.Equal("Your request was approved", Text(bare));
        Assert.Equal("Your request was approved", Text(For("""{"type":"request.approved","reason_flags":{"title":""}}""")!));
    }

    [Fact]
    public void DeclinedRequestCarriesTheServerReasonAsTheDescription()
    {
        var titled = For("""{"type":"request.declined","reason_flags":{"title":"Heat","reason":"Already in the library"}}""")!;
        Assert.Equal(NotificationToastRules.RequestDeclinedTitleKey, titled.Title.Key);
        var reason = Assert.Single(titled.Description);
        Assert.Null(reason.Key);
        Assert.Equal("Request declined: Heat\nAlready in the library", Text(titled));

        Assert.Equal("Your request was declined\nNot wanted", Text(For("""{"type":"request.declined","reason_flags":{"reason":"Not wanted"}}""")!));
        Assert.Equal("Your request was declined", Text(For("""{"type":"request.declined"}""")!));
        Assert.Equal("Your request was declined", Text(For("""{"type":"request.declined","reason_flags":{"reason":""}}""")!));
    }

    [Theory]
    [InlineData("webhook.auto_disabled", NotificationToastRules.WebhookStoppedKey, "A webhook stopped working")]
    [InlineData("plugin_destination.auto_disabled", NotificationToastRules.DestinationStoppedKey, "A notification destination stopped working")]
    public void StoppedDeliveryPointsToNotificationSettings(string type, string key, string title)
    {
        var toast = For("{\"type\":\"" + type + "\"}")!;
        Assert.Equal(key, toast.Title.Key);
        Assert.Equal(NotificationToastRules.FixInSettingsKey, Assert.Single(toast.Description).Key);
        Assert.Equal(title + "\nOpen notification settings to fix it.", Text(toast));
    }

    [Theory]
    [InlineData("""{"type":"something.else","series_title":"Show"}""")]
    [InlineData("""{"type":"media.updated"}""")]
    [InlineData("""{"series_title":"Show"}""")]
    [InlineData("""{"type":null}""")]
    [InlineData("""{"type":7}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("""{"type":"episode.available","series_title":5}""")]
    public void UnknownOrMalformedPayloadsHaveNoToast(string json) => Assert.Null(For(json));

    [Fact]
    public void OfficialKeysShippedInTheEnglishCatalogResolve()
    {
        var episode = For("""{"type":"episode.available","series_title":"Show","season_number":1,"episode_number":2}""")!;
        Assert.Equal("New episode of Show", AppStrings.English(NotificationToastRules.NewEpisodeKey, episode.Title.Arguments, "missing"));
        Assert.Equal("S1E2", AppStrings.English(NotificationToastRules.EpisodeCodeKey, episode.Description[0].Arguments, "missing"));
        Assert.Equal("Show is now available", AppStrings.English(NotificationToastRules.SeriesAvailableKey, episode.Title.Arguments, "missing"));
    }
}
