using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Tests;

public sealed class PlaybackSpeedMenuTests
{
    [Fact]
    public void ValidFloatingPointNoiseUsesOneCanonicalIdentifier()
    {
        var rows = PlaybackSpeedMenuModel.Build(1.3499999999999999, true, false, false);
        var current = Assert.Single(rows.Where(row => row.IsCurrent));
        Assert.Equal(1.35, current.Rate);
        Assert.Equal("rate:1.35", current.Id);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.35)]
    [InlineData(2.95)]
    public void StoredCustomRateRemainsAnExactCurrentChoice(double rate)
    {
        var rows = PlaybackSpeedMenuModel.Build(rate, true, false, false);
        Assert.Equal(10, rows.Count);
        Assert.Equal(rate, Assert.Single(rows.Where(row => row.IsCurrent)).Rate);
        Assert.Contains(rows, row => row.Rate == rate && row.Metadata.Contains("custom", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct().Count());
    }

    [Fact]
    public void OrdinaryRateIsNotDuplicatedAndPendingAllowsNewerManualIntent()
    {
        var rows = PlaybackSpeedMenuModel.Build(1.5, true, false, true);
        Assert.Equal(new[] { .5, .75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3 }, rows.Select(row => row.Rate));
        Assert.Equal(1.5, Assert.Single(rows.Where(row => row.IsPending)).Rate);
        Assert.All(rows, row => Assert.True(row.IsEnabled));
    }

    [Fact]
    public void RoomShowsDesiredCustomPreferenceWithAnExplicitDisabledReason()
    {
        var row = Assert.Single(PlaybackSpeedMenuModel.Build(1.35, false, true, false));
        Assert.Equal(1.35, row.Rate); Assert.True(row.IsCurrent); Assert.False(row.IsEnabled);
        Assert.Contains("Watch Party", row.DisabledReason); Assert.Equal("1.35×", row.Title);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(1.333)]
    [InlineData(4)]
    public void InvalidPresentationInputCannotCreateAnInvalidRateChoice(double rate)
    {
        var current = Assert.Single(PlaybackSpeedMenuModel.Build(rate, true, false, false).Where(row => row.IsCurrent));
        Assert.Equal(1, current.Rate);
    }
}
