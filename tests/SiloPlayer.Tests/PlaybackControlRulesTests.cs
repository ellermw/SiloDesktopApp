using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Official silo-server #4392, #4394 (control ticket retries) and #4554 (an administrator's stop reason).</summary>
public sealed class PlaybackControlRulesTests
{
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(4, 10)]
    [InlineData(5, 30)]
    [InlineData(6, 30)]
    [InlineData(400, 30)]
    public void BackoffGrowsToThirtySecondsAndTheLastDelayRepeats(int attempt, double seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), PlaybackControlRules.ReconnectDelay(attempt));

    [Theory]
    [InlineData(0, 4.0, 4)]      // Retry-After longer than the backoff sets the wait
    [InlineData(5, 4.0, 30)]     // never shortens the backoff
    [InlineData(0, 600.0, 60)]   // capped at sixty seconds
    [InlineData(0, 0.0, 0.5)]
    [InlineData(0, -3.0, 0.5)]
    [InlineData(0, double.NaN, 0.5)]
    public void RetryAfterIsAMinimumWaitCappedAtSixtySeconds(int attempt, double retryAfter, double seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), PlaybackControlRules.ReconnectDelay(attempt, retryAfter));

    [Theory]
    [InlineData(410, true)]   // the session ended and will not be rebuilt
    [InlineData(401, true)]   // this sign-in is over
    [InlineData(403, false)]
    [InlineData(404, false)]  // a restarted server rebuilding the session
    [InlineData(409, false)]
    [InlineData(503, false)]
    [InlineData(500, false)]
    public void OnlyAnEndedSessionOrSignInStopsTicketRetries(int status, bool ends)
        => Assert.Equal(ends, PlaybackControlRules.TicketRefusalEndsControl(status));

    // Official socket_close.go / docs/realtime-api.md "Close codes" (#4556).
    [Theory]
    [InlineData(null, false, true, 0.5)]
    [InlineData(1000, false, true, 0.5)]
    [InlineData(4001, false, true, 0.5)] // access_changed: new ticket at once
    [InlineData(4002, true, true, 30)]   // credential_invalid: the lane ends (the delay is never used)
    [InlineData(4003, false, true, 0.5)] // lifetime_ended: new ticket and reconnect
    [InlineData(4004, false, false, 5)]  // authority_unavailable: back off, keep the backoff growing
    public void ServerCloseCodesDecideWhetherAndWhenTheLaneReconnects(int? closeCode, bool ends, bool resets, double firstDelaySeconds)
    {
        Assert.Equal(ends, PlaybackControlRules.CloseEndsControl(closeCode));
        Assert.Equal(resets, PlaybackControlRules.CloseResetsBackoff(closeCode));
        Assert.Equal(TimeSpan.FromSeconds(firstDelaySeconds), PlaybackControlRules.ReconnectDelay(0, null, closeCode));
    }

    [Fact]
    public void AnAuthorityUnavailableCloseNeverShortensALongerBackoffOrRetryAfter()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), PlaybackControlRules.ReconnectDelay(4, null, 4004));
        Assert.Equal(TimeSpan.FromSeconds(8), PlaybackControlRules.ReconnectDelay(0, 8.0, 4004));
    }

    [Fact]
    public void AnAdministratorsReasonComesFromTheEnvelope()
    {
        Assert.Equal("An administrator ended this playback session. Server maintenance",
            PlaybackControlRules.EndNotice("terminate", null, null, "  Server maintenance ", "admin"));
        Assert.Equal("An administrator ended this playback session.",
            PlaybackControlRules.EndNotice("stop", null, null, null, "admin"));
    }

    [Fact]
    public void APayloadMessageWinsOverTheEnvelopeReasonAndAPayloadTitleOverTheDefault()
    {
        Assert.Equal("Playback ended: Bye", PlaybackControlRules.EndNotice("terminate", null, "Bye", "ignored", null));
        Assert.Equal("Playback stopping: Bye", PlaybackControlRules.EndNotice("stop", null, "Bye", null, null));
        Assert.Equal("Heads up! Bye", PlaybackControlRules.EndNotice("stop", "Heads up!", "Bye", null, "admin"));
        Assert.Equal("Playback stopping: quota", PlaybackControlRules.EndNotice("stop", null, null, "quota", "system"));
    }

    [Fact]
    public void AStopWithNothingToSayShowsNothing()
    {
        Assert.Null(PlaybackControlRules.EndNotice("stop", null, null, null, null));
        Assert.Null(PlaybackControlRules.EndNotice("terminate", "Title only", null, "   ", "user"));
    }
}
