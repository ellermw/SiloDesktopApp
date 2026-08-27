using SiloPlayer.Core.Services;
using System.Text.Json;

namespace SiloPlayer.Tests;

public class HomeRealtimeRefreshGateTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("catalog:library.changed")]
    [InlineData("catalog:catalog.library.changed")]
    public void LibraryChangeAliasesShareTheCatalogBurstCooldown(string reason)
    {
        var gate = new HomeRealtimeRefreshGate(
            debounce: TimeSpan.FromMilliseconds(350),
            catalogBurstCooldown: TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromMilliseconds(350), gate.GetDelay(reason, Epoch));

        gate.MarkRefreshed(reason, Epoch + TimeSpan.FromMilliseconds(350));

        Assert.Equal(
            TimeSpan.FromSeconds(27.35),
            gate.GetDelay("catalog:library.changed", Epoch + TimeSpan.FromSeconds(3)));
    }

    [Theory]
    [InlineData("catalog:library.item_added")]
    [InlineData("catalog:catalog.item.changed")]
    [InlineData("catalog:metadata.updated")]
    [InlineData("user_state:user_state.changed")]
    [InlineData("playback_completed")]
    public void ItemAndUserChangesRemainResponsiveDuringCatalogCooldown(string reason)
    {
        var gate = new HomeRealtimeRefreshGate(
            debounce: TimeSpan.FromMilliseconds(350),
            catalogBurstCooldown: TimeSpan.FromSeconds(30));
        gate.MarkRefreshed("catalog:library.changed", Epoch);

        Assert.Equal(
            TimeSpan.FromMilliseconds(350),
            gate.GetDelay(reason, Epoch + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ADeferredCatalogRefreshBecomesEligibleAtTheOriginalDeadline()
    {
        var gate = new HomeRealtimeRefreshGate(
            debounce: TimeSpan.FromMilliseconds(350),
            catalogBurstCooldown: TimeSpan.FromSeconds(30));
        gate.MarkRefreshed("catalog:library.changed", Epoch);

        var firstDelay = gate.GetDelay("catalog:library.changed", Epoch + TimeSpan.FromSeconds(3));
        var laterDelay = gate.GetDelay("catalog:catalog.library.changed", Epoch + TimeSpan.FromSeconds(20));

        Assert.Equal(Epoch + TimeSpan.FromSeconds(30), Epoch + TimeSpan.FromSeconds(3) + firstDelay);
        Assert.Equal(Epoch + TimeSpan.FromSeconds(30), Epoch + TimeSpan.FromSeconds(20) + laterDelay);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("updated")]
    [InlineData("missing")]
    public void MaterialLibraryChangesBypassTheScanBurstCooldown(string counter)
    {
        using var document = JsonDocument.Parse($$"""{ "{{counter}}": 1, "matched_files": 4 }""");

        var reason = HomeRealtimeRefreshGate.ClassifyCatalogEvent(
            "library.changed",
            document.RootElement);

        Assert.False(HomeRealtimeRefreshGate.IsCatalogBurst(reason));
    }

    [Fact]
    public void MatchOnlyLibraryNotificationsRemainCatalogBursts()
    {
        using var document = JsonDocument.Parse("""{ "new": 0, "updated": 0, "missing": 0, "matched_files": 4 }""");

        var reason = HomeRealtimeRefreshGate.ClassifyCatalogEvent(
            "library.changed",
            document.RootElement);

        Assert.True(HomeRealtimeRefreshGate.IsCatalogBurst(reason));
    }
}
