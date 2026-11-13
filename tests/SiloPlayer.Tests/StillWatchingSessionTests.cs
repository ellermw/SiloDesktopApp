using System.Globalization;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

// Upstream #3917 (web/src/player/stillWatching.ts at 0b579aa6): with
// threshold N, N automatic advances in a row are allowed and the next waits
// for the viewer; any viewer start or playback interaction clears the count.
public sealed class StillWatchingSessionTests
{
    [Theory]
    [InlineData(null, 3)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(21, 3)]
    [InlineData(-1, 3)]
    public void ThresholdUsesClientLocalRangeAndDefault(int? stored, int expected)
        => Assert.Equal(expected, StillWatchingThreshold.Normalize(stored));

    [Fact]
    public void AppSettingsDefaultsToMissingThresholdSoTheDefaultApplies()
    {
        var settings = new AppSettings();
        Assert.Null(settings.StillWatchingThreshold);
        Assert.Equal(3, StillWatchingThreshold.Normalize(settings.StillWatchingThreshold));
    }

    [Fact]
    public void DefaultThresholdAllowsThreeAutomaticAdvancesThenWaits()
    {
        var session = new StillWatchingSession();
        for (var episode = 1; episode <= 3; episode++)
        {
            Assert.False(session.IsGated(3));
            Assert.True(session.TryBeginAutomaticAdvance($"e{episode}", $"e{episode + 1}", 3, watchParty: false));
            Assert.True(session.ConsumeStart($"e{episode + 1}"));
        }
        Assert.True(session.IsGated(3));
        Assert.False(session.TryBeginAutomaticAdvance("e4", "e5", 3, watchParty: false));
        Assert.Equal(3, session.AutomaticStarts);
    }

    [Fact]
    public void ZeroNeverAsksAndMaximumAllowsTwentyAdvances()
    {
        var unlimited = new StillWatchingSession();
        for (var episode = 0; episode < 50; episode++)
        {
            Assert.True(unlimited.TryBeginAutomaticAdvance($"z{episode}", $"z{episode + 1}", 0, watchParty: false));
            Assert.True(unlimited.ConsumeStart($"z{episode + 1}"));
        }
        Assert.False(unlimited.IsGated(0));

        var maximum = new StillWatchingSession();
        for (var episode = 0; episode < 20; episode++)
        {
            Assert.True(maximum.TryBeginAutomaticAdvance($"m{episode}", $"m{episode + 1}", 20, watchParty: false));
            Assert.True(maximum.ConsumeStart($"m{episode + 1}"));
        }
        Assert.True(maximum.IsGated(20));
        Assert.False(maximum.TryBeginAutomaticAdvance("m20", "m21", 20, watchParty: false));
    }

    [Fact]
    public void ViewerInteractionAndManualStartsResetTheStreak()
    {
        var session = new StillWatchingSession();
        Advance(session, "a", "b", 1);
        Assert.True(session.IsGated(1));
        session.RecordViewerInteraction();
        Assert.False(session.IsGated(1));

        Advance(session, "b", "c", 1);
        Assert.True(session.IsGated(1));
        // A start nothing announced as automatic (a Play button, a card, a
        // remote device) is the viewer's choice.
        Assert.False(session.ConsumeStart("unrelated"));
        Assert.False(session.IsGated(1));
        Assert.Equal(0, session.AutomaticStarts);
    }

    [Fact]
    public void AutomaticMarkerIsUsedUpSoAReopenedTitleCountsAsViewerChoice()
    {
        var session = new StillWatchingSession();
        Advance(session, "a", "b", 5);
        Assert.Equal(1, session.AutomaticStarts);
        Assert.False(session.ConsumeStart("b"));
        Assert.Equal(0, session.AutomaticStarts);
    }

    [Fact]
    public void CreditsThenEndOfFileFromTheSameEpisodeAdvancesOnlyOnce()
    {
        var session = new StillWatchingSession();
        Assert.True(session.TryBeginAutomaticAdvance("a", "b", 3, watchParty: false));
        // The ended countdown of the same outgoing episode is a stale boundary.
        Assert.False(session.TryBeginAutomaticAdvance("a", "b", 3, watchParty: false));
        Assert.Equal(1, session.AutomaticStarts);
        Assert.True(session.ConsumeStart("b"));
        Assert.True(session.TryBeginAutomaticAdvance("b", "c", 3, watchParty: false));
    }

    [Fact]
    public void PreCheckMatchesTheClaimWithoutConsumingIt()
    {
        var session = new StillWatchingSession();
        Assert.True(session.CanBeginAutomaticAdvance("a", 1, watchParty: false));
        Assert.Equal(0, session.AutomaticStarts);
        Assert.True(session.TryBeginAutomaticAdvance("a", "b", 1, watchParty: false));
        // The same title's other boundary, and anything past the limit, wait.
        Assert.False(session.CanBeginAutomaticAdvance("a", 5, watchParty: false));
        Assert.True(session.ConsumeStart("b"));
        Assert.False(session.CanBeginAutomaticAdvance("b", 1, watchParty: false));
        Assert.True(session.CanBeginAutomaticAdvance("b", 0, watchParty: false));
        Assert.False(session.CanBeginAutomaticAdvance("b", 0, watchParty: true));
    }

    [Fact]
    public void WatchPartyNeverAdvancesLocallyOrCounts()
    {
        var session = new StillWatchingSession();
        Assert.False(session.TryBeginAutomaticAdvance("a", "b", 3, watchParty: true));
        Assert.Equal(0, session.AutomaticStarts);
        Assert.False(session.IsGated(3));
    }

    [Fact]
    public void ShuffleRecordsBeforeTheServerPickIsKnownThenExpectsThatPick()
    {
        var session = new StillWatchingSession();
        Assert.True(session.TryBeginAutomaticAdvance("s1", null, 2, watchParty: false));
        session.ExpectAutomaticStart("s-random");
        Assert.True(session.ConsumeStart("s-random"));
        Assert.Equal(1, session.AutomaticStarts);
        // A viewer's Pick Another / Play Now is a viewer start.
        session.RecordViewerInteraction();
        Assert.False(session.ConsumeStart("s-other"));
        Assert.Equal(0, session.AutomaticStarts);
    }

    [Fact]
    public void AdvanceThatNeverStartsIsWithdrawnWithoutCountingOrBlockingTheTitle()
    {
        var session = new StillWatchingSession();
        Advance(session, "a", "b", 3);
        // Credits claim the advance, then the server pick fails.
        Assert.True(session.TryBeginAutomaticAdvance("b", null, 3, watchParty: false));
        Assert.Equal(2, session.AutomaticStarts);
        session.CancelAutomaticAdvance("b");
        Assert.Equal(1, session.AutomaticStarts);
        // The ended countdown of the same title may still advance, once.
        Assert.True(session.CanBeginAutomaticAdvance("b", 3, watchParty: false));
        Assert.True(session.TryBeginAutomaticAdvance("b", "c", 3, watchParty: false));
        Assert.True(session.ConsumeStart("c"));
        Assert.Equal(2, session.AutomaticStarts);
    }

    [Fact]
    public void WithdrawnAdvanceLeavesNoAnnouncementForALaterViewerStart()
    {
        var session = new StillWatchingSession();
        Advance(session, "a", "b", 3);
        Assert.True(session.TryBeginAutomaticAdvance("b", "c", 3, watchParty: false));
        session.CancelAutomaticAdvance("b");
        // The viewer opening that title later is the viewer's choice.
        Assert.False(session.ConsumeStart("c"));
        Assert.Equal(0, session.AutomaticStarts);
    }

    [Fact]
    public void WithdrawingAStartedOrUnrelatedAdvanceChangesNothing()
    {
        var session = new StillWatchingSession();
        var changes = 0;
        Advance(session, "a", "b", 3);
        session.Changed += () => changes++;
        // The start already used the claim up.
        session.CancelAutomaticAdvance("a");
        Assert.True(session.TryBeginAutomaticAdvance("b", "c", 3, watchParty: false));
        session.CancelAutomaticAdvance("other");
        Assert.Equal(2, session.AutomaticStarts);
        Assert.Equal(1, changes);
        Assert.True(session.ConsumeStart("c"));
        // A viewer interaction after the claim already cleared the count.
        Assert.True(session.TryBeginAutomaticAdvance("c", null, 3, watchParty: false));
        session.RecordViewerInteraction();
        session.CancelAutomaticAdvance("c");
        Assert.Equal(0, session.AutomaticStarts);
    }

    [Fact]
    public void ContinuationOfTheSameTitleKeepsTheStreak()
    {
        var session = new StillWatchingSession();
        Advance(session, "a", "b", 3);
        // A multi-part file continues as an announced, non-viewer start.
        session.ExpectAutomaticStart("b");
        Assert.True(session.ConsumeStart("b"));
        Assert.Equal(1, session.AutomaticStarts);
    }

    [Fact]
    public void ChangesAreRaisedOnlyWhenTheGateInputsChange()
    {
        var session = new StillWatchingSession();
        var changes = 0;
        session.Changed += () => changes++;
        session.RecordViewerInteraction();
        Assert.Equal(0, changes);
        Advance(session, "a", "b", 3);
        Assert.Equal(1, changes);
        session.RecordViewerInteraction();
        Assert.Equal(2, changes);
    }

    private static void Advance(StillWatchingSession session, string from, string to, int threshold)
    {
        Assert.True(session.TryBeginAutomaticAdvance(from, to, threshold, watchParty: false));
        Assert.True(session.ConsumeStart(to));
    }
}

public sealed class PlaybackEndTimeClockTests
{
    [Theory]
    [InlineData("12h", "en-GB", "12h")]
    [InlineData("24h", "en-US", "24h")]
    [InlineData("auto", "en-US", "12h")]
    [InlineData("auto", "de-DE", "24h")]
    [InlineData("auto", "fr-CA", "24h")]
    [InlineData(null, "en-GB", "24h")]
    [InlineData("unknown", "ja-JP", "24h")]
    public void ClockFollowsProfilePreferenceThenDisplayCulture(string? preference, string culture, string expected)
        => Assert.Equal(expected, PlaybackEndTime.ResolveClock(preference, CultureInfo.GetCultureInfo(culture)));

    // Culture data differs between platforms, so the literal forms are pinned here.
    [Theory]
    [InlineData("HH 'h' mm", "24h")]
    [InlineData("HH \"h\" mm", "24h")]
    [InlineData("H\\hmm", "24h")]
    [InlineData("HH:mm", "24h")]
    [InlineData("h:mm tt", "12h")]
    [InlineData("tt h 'h' mm", "12h")]
    public void QuotedOrEscapedHourLetterIsNotTheTwelveHourSpecifier(string pattern, string expected)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortTimePattern = pattern;
        Assert.Equal(expected, PlaybackEndTime.ResolveClock("auto", culture));
    }

    [Fact]
    public void EnglishFallbackTemplateNamesTheTimeArgument()
    {
        Assert.Equal("player.scrubber.endsAt", PlaybackEndTime.LabelKey);
        Assert.Contains("{time}", PlaybackEndTime.FallbackLabelTemplate, StringComparison.Ordinal);
        Assert.Equal("Ends 2:38 AM", PlaybackEndTime.FallbackLabelTemplate.Replace("{time}", "2:38 AM", StringComparison.Ordinal));
    }
}
