using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackRecoveryAttemptHistoryTests
{
    [Fact]
    public void ProtocolBoundsMatchTheCurrentWebUiContract()
    {
        Assert.Equal(16, PlaybackRecoveryAttemptHistory.MaxAttemptedPlanKeys);
        Assert.Equal(8, PlaybackRecoveryAttemptHistory.MaxAttemptCount);
    }

    [Fact]
    public void FailureAttemptsStopAfterEightRequestsEvenWhenKeysAreBoundedSeparately()
    {
        var history = new PlaybackRecoveryAttemptHistory();

        for (var index = 1; index <= PlaybackRecoveryAttemptHistory.MaxAttemptCount; index++)
        {
            var attempt = history.PrepareFailure($"v3:{index:x16}");
            Assert.Equal(index, attempt.AttemptCount);
            history.CommitFailure(attempt);
        }

        var error = Assert.Throws<PlaybackPlanTerminalException>(
            () => history.PrepareFailure("v3:0000000000000009"));
        Assert.Equal("recovery_exhausted", error.Reason);
        Assert.False(error.Retryable);
    }

    [Fact]
    public void ResetStartsANewRecoveryChain()
    {
        var history = new PlaybackRecoveryAttemptHistory();
        var first = history.PrepareFailure("v3:0000000000000001");
        history.CommitFailure(first);

        history.Reset();

        var reset = history.PrepareFailure("v3:0000000000000002");
        Assert.Equal(1, reset.AttemptCount);
        Assert.Equal(["v3:0000000000000002"], reset.AttemptedPlanKeys);
    }

    [Fact]
    public void RepeatedPlanKeyMovesToTheTailWithoutConsumingAnotherSlot()
    {
        var history = new PlaybackRecoveryAttemptHistory();
        foreach (var key in new[] { "plan:a", "plan:b", "plan:c" })
        {
            var attempt = history.PrepareFailure(key);
            history.CommitFailure(attempt);
        }

        var repeated = history.PrepareFailure("plan:b");

        Assert.Equal(["plan:a", "plan:c", "plan:b"], repeated.AttemptedPlanKeys);
    }

    [Fact]
    public void CommittedHistoryRetainsOnlyTheNewestSixteenUniquePlanKeys()
    {
        var history = new PlaybackRecoveryAttemptHistory();
        var supplied = Enumerable.Range(0, 18)
            .Select(index => $"plan:{index:D2}")
            .ToArray();
        history.CommitFailure(new PlaybackRecoveryAttempt(supplied, AttemptCount: 1));

        var next = history.PrepareFailure("plan:18");

        Assert.Equal(PlaybackRecoveryAttemptHistory.MaxAttemptedPlanKeys, next.AttemptedPlanKeys.Count);
        Assert.Equal(
            Enumerable.Range(3, 16).Select(index => $"plan:{index:D2}"),
            next.AttemptedPlanKeys);
    }
}
