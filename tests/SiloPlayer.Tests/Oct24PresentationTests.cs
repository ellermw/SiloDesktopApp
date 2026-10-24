using System.Text.Json;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class Oct24PresentationTests
{
    private static JsonElement Call(string type, string method, params object[] args)
    {
        var implementation = typeof(MetadataEditState).Assembly.GetType("SiloPlayer.Core.Services." + type);
        Assert.NotNull(implementation);
        return JsonSerializer.SerializeToElement(implementation.GetMethod(method)!.Invoke(null, args));
    }

    [Theory]
    [InlineData("oauth", true, "provider")]
    [InlineData("credentials", true, "directory")]
    [InlineData("credentials", false, "none")]
    [InlineData("network", false, "network")]
    [InlineData("missing", false, "network")]
    public void LinkedEnabledProviderDeterminesActualSignIn(string mode, bool form, string kind)
    {
        var result = Call("ConnectAppsSignIn", "Resolve", false, form,
            new List<AuthProvider> { new() { Id = "enabled", Mode = mode == "missing" ? "other" : mode, DisplayName = "Provider" } },
            new List<AccountIdentity> { new() { ProviderId = mode == "missing" ? "not-listed" : "enabled", Username = "directory-user", ProviderName = "Network" } });
        Assert.Equal(kind, result.GetProperty("Kind").GetString());
        if (kind == "directory") Assert.Equal("directory-user", result.GetProperty("Username").GetString());
    }

    [Fact]
    public void DisabledIdentityDoesNotOfferLoginAndRecoveryNeedsVisiblePasswordForm()
    {
        var providers = new List<AuthProvider> { new() { Id = "oauth", Mode = "oauth" } };
        var disabled = new List<AccountIdentity> { new() { ProviderId = "", ProviderName = "Disabled" } };
        Assert.Equal("none", Call("ConnectAppsSignIn", "Resolve", false, true, providers, disabled).GetProperty("Kind").GetString());
        Assert.Equal("none", Call("ConnectAppsSignIn", "Resolve", true, false, providers, disabled).GetProperty("Kind").GetString());
        Assert.Equal("password", Call("ConnectAppsSignIn", "Resolve", true, true, providers, disabled).GetProperty("Kind").GetString());
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(44, 44, false)]
    [InlineData(null, 60, true)]
    public void MeasuredWatchTimeIncludingZeroWinsOverEndPosition(int? measured, int seconds, bool estimated)
    {
        var row = JsonSerializer.SerializeToElement(new { media_title = "Pilot", series_title = "Show", season_number = 2, episode_number = 4,
            played_seconds = measured, watched_seconds = 2700, started_at = "2026-10-24T10:00:00Z", ended_at = "2026-10-24T10:01:00Z" });
        var result = Call("PlaybackHistoryPresentation", "Format", row);
        Assert.Equal("Show", result.GetProperty("Title").GetString());
        Assert.Equal("S02E04 · Pilot", result.GetProperty("Detail").GetString());
        Assert.Equal(seconds, result.GetProperty("Seconds").GetDouble());
        Assert.Equal(estimated, result.GetProperty("Estimated").GetBoolean());
    }

    [Fact]
    public void EpisodeNamedForShowDoesNotDuplicateTitleAndOldRowsAreEstimated()
    {
        var row = JsonSerializer.SerializeToElement(new { media_title = "Show", series_title = "Show", season_number = 0, episode_number = 1, watched_seconds = 120 });
        var result = Call("PlaybackHistoryPresentation", "Format", row);
        Assert.Equal("Show", result.GetProperty("Title").GetString()); Assert.Equal("S00E01", result.GetProperty("Detail").GetString());
        Assert.Equal(120, result.GetProperty("Seconds").GetDouble()); Assert.True(result.GetProperty("Estimated").GetBoolean());
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData(2, null)]
    [InlineData(null, null)]
    public void UnknownEpisodeNumberKeepsTitleWithoutInventingPartialEpisodeCode(int? season, int? episode)
    {
        var row = JsonSerializer.SerializeToElement(new { media_title = "Episode", series_title = "Show", season_number = season, episode_number = episode });
        Assert.Equal("Episode", Call("PlaybackHistoryPresentation", "Format", row).GetProperty("Detail").GetString());
    }
}
