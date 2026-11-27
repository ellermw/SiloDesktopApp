using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Ports the cases of official #5204 (<c>useShowThenSave</c> in settingWriteOffline.test.ts) to the native tracker.</summary>
public sealed class SettingSaveReconcilerTests
{
    private static readonly Exception Rejected = new ApiException("invalid_value", "Rejected", 422);

    private sealed class Run
    {
        private readonly SettingSaveReconciler _tracker = new();
        private readonly List<SettingSaveReconciler.Change> _changes = [];
        public string Shown { get; private set; } = "normal";
        public int Rereads { get; private set; }
        public string Owner { get; set; } = "a";

        public Run Pick(string value, string key = "ui.text_scale", string? owner = null)
        {
            Owner = owner ?? Owner;
            _changes.Add(_tracker.Begin(Owner, key, value, Shown));
            Shown = value;
            return this;
        }

        public Run Settle(int index, Exception? error = null)
        {
            var settled = error is null ? _tracker.Succeeded(_changes[index], Owner) : _tracker.Failed(_changes[index], error, Owner);
            if (settled.Show is { } stored) Shown = stored;
            if (settled.Reread) Rereads++;
            return this;
        }

        public Run ReadFailed() { _tracker.OweReread(Owner); return this; }
    }

    [Theory]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(429)]
    public void ARefusedChangePutsBackThePreviousValueAtOnce(int status)
    {
        var run = new Run().Pick("large").Settle(0, new ApiException("code", "no", status));
        Assert.Equal("normal", run.Shown);
        Assert.Equal(0, run.Rereads);
    }

    [Fact]
    public void TwoRefusedChangesPutBackTheValueFromBeforeBoth() =>
        Assert.Equal("normal", new Run().Pick("large").Pick("x-large").Settle(0, Rejected).Settle(1, Rejected).Shown);

    [Fact]
    public void AnEarlierChangeThatSavedIsPutBackWhenTheNewerOneIsRefused() =>
        Assert.Equal("large", new Run().Pick("large").Pick("x-large").Settle(0).Settle(1, Rejected).Shown);

    [Fact]
    public void AnEarlierChangeThatSavedAfterANewerRefusalIsShown() =>
        Assert.Equal("large", new Run().Pick("large").Pick("x-large").Settle(1, new ApiException("rate", "slow", 429)).Settle(0).Shown);

    [Fact]
    public void AnEarlierChangeThatSavedWhileALaterOneWasPendingIsPutBack() =>
        Assert.Equal("large", new Run().Pick("large").Pick("x-large").Settle(1, Rejected).Pick("small").Settle(0).Settle(2, Rejected).Shown);

    [Fact]
    public void ALaterChangeThatSavedIsNotReplacedByAnEarlierOneLandingAfterIt() =>
        Assert.Equal("small", new Run().Pick("large").Pick("x-large").Settle(1, Rejected).Pick("small").Settle(2).Settle(0).Shown);

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(408)]
    public void AnUnknownOutcomeKeepsTheValueAndAsksForARereadOnce(int status)
    {
        var run = new Run().Pick("large").Settle(0, new ApiException("code", "maybe", status));
        Assert.Equal("large", run.Shown);
        Assert.Equal(1, run.Rereads);
    }

    [Fact]
    public void ADroppedConnectionIsAnUnknownOutcome()
    {
        var run = new Run().Pick("large").Settle(0, new HttpRequestException("Failed to fetch"));
        Assert.Equal("large", run.Shown);
        Assert.Equal(1, run.Rereads);
    }

    [Fact]
    public void AnUnknownOutcomeSupersededByANewerChangeAsksForNothing()
    {
        var run = new Run().Pick("large").Pick("x-large").Settle(0, new ApiException("code", "bad gateway", 502)).Settle(1);
        Assert.Equal("x-large", run.Shown);
        Assert.Equal(0, run.Rereads);
    }

    [Fact]
    public void ARereadWaitsForAnEarlierSaveThatANewerOneReplacedToSettleLast()
    {
        var run = new Run().Pick("strong", "ui.text_weight").Pick("strong", "ui.text_weight").Pick("large");
        var after = new List<int>();
        run.Settle(2, new ApiException("code", "bad gateway", 502)); after.Add(run.Rereads);
        run.Settle(1); after.Add(run.Rereads);
        run.Settle(0); after.Add(run.Rereads);
        Assert.Equal([0, 0, 1], after);
    }

    [Fact]
    public void ARereadWaitsForAnotherPreferencesSaveToSettle()
    {
        // Reading at the 502 would put back the text weight that is still saving.
        var run = new Run().Pick("large").Pick("strong", "ui.text_weight");
        run.Settle(0, new ApiException("code", "bad gateway", 502));
        Assert.Equal(0, run.Rereads);
        run.Settle(1);
        Assert.Equal(1, run.Rereads);
    }

    [Fact]
    public void ARereadOwedToThePreviousProfileRunsForTheProfileShownNow()
    {
        var run = new Run().Pick("strong", "ui.text_weight", owner: "a").Pick("large", "ui.text_scale", owner: "b");
        run.Settle(1, new ApiException("code", "bad gateway", 502));
        Assert.Equal(0, run.Rereads);
        run.Settle(0);
        Assert.Equal(1, run.Rereads);
    }

    [Fact]
    public void AnotherProfilesUnknownOutcomeIsNotReadForTheProfileShownNow()
    {
        var run = new Run().Pick("large", owner: "a");
        run.Owner = "b";
        run.Settle(0, new ApiException("code", "bad gateway", 502));
        Assert.Equal(0, run.Rereads);
    }

    [Fact]
    public void AFailedRereadIsOwedAgainAfterTheNextSaveSettles()
    {
        var run = new Run().Pick("large").Settle(0, new ApiException("code", "bad gateway", 502));
        Assert.Equal(1, run.Rereads);
        run.ReadFailed().Pick("x-large").Settle(1);
        Assert.Equal(2, run.Rereads);
    }

    [Fact]
    public void ASaveSettlingTwiceCountsOnce()
    {
        var run = new Run().Pick("large").Pick("x-large").Settle(0).Settle(0);
        Assert.Equal("x-large", run.Shown);
    }

    [Theory]
    [InlineData(400, true)]
    [InlineData(404, true)]
    [InlineData(409, true)]
    [InlineData(408, false)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    public void OnlyAFourHundredOtherThanRequestTimeoutIsADefiniteRefusal(int status, bool definite) =>
        Assert.Equal(definite, SettingSaveReconciler.IsDefinitiveRejection(new ApiException("code", "m", status)));

    [Fact]
    public void ACancelledOrTimedOutSaveIsNotADefiniteRefusal()
    {
        Assert.False(SettingSaveReconciler.IsDefinitiveRejection(new TaskCanceledException()));
        Assert.False(SettingSaveReconciler.IsDefinitiveRejection(new TimeoutException()));
    }

    [Fact]
    public void AnOlderServerThatDoesNotReturnTheAppearanceKeysReadsAsTheDefaults()
    {
        var response = new EffectiveSettingsResponse { Settings = [] };
        Assert.Equal(("default", "default", false), AppearancePreferenceValues.Accessibility(response));
        var partial = new EffectiveSettingsResponse { Settings = [new EffectiveSettingEntry { Key = "ui_text_scale", EffectiveValue = "x-large" }] };
        Assert.Equal(("x-large", "default", false), AppearancePreferenceValues.Accessibility(partial));
        Assert.Equal("auto", AppearancePreferenceValues.DateFormat(null));
        Assert.Equal("auto", AppearancePreferenceValues.TimeFormat("unknown"));
        Assert.Equal("YYYY-MM-DD", AppearancePreferenceValues.DateFormat("YYYY-MM-DD"));
        Assert.True(AppearancePreferenceValues.HighContrast("TRUE"));
    }
}
