using SiloPlayer.Core.Services;
using System.Text.Json;

namespace SiloPlayer.Tests;

public class HomeRealtimeRefreshGateTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("catalog:library.changed")]
    [InlineData("catalog:catalog.library.changed")]
    [InlineData("catalog:metadata.updated")]
    [InlineData("catalog:catalog.metadata.updated")]
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

    // WebUI realtimeCatalogInvalidation.ts: one catalog sweep per 2 s window, the first after a quiet window at once.
    [Theory]
    [InlineData("catalog:catalog.item.changed")]
    [InlineData("catalog:library.item_added")]
    [InlineData("catalog:library.changed:content_changed")]
    public void ItemCatalogChangesRefreshOncePerWebUiWindow(string reason)
    {
        var gate = new HomeRealtimeRefreshGate(
            debounce: TimeSpan.FromMilliseconds(350),
            catalogBurstCooldown: TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(2), HomeRealtimeRefreshGate.DefaultCatalogWindow);
        Assert.True(HomeRealtimeRefreshGate.IsCatalogItemChange(reason));

        // The first change after a quiet window refreshes as promptly as before.
        Assert.Equal(TimeSpan.FromMilliseconds(350), gate.GetDelay(reason, Epoch));
        gate.MarkRefreshed(reason, Epoch);

        // Later changes in the window wait for its end; one refresh then covers them all.
        Assert.Equal(TimeSpan.FromSeconds(1.5), gate.GetDelay("catalog:catalog.item.changed", Epoch + TimeSpan.FromSeconds(0.5)));
        Assert.Equal(TimeSpan.FromMilliseconds(350), gate.GetDelay("catalog:library.item_added", Epoch + TimeSpan.FromSeconds(1.8)));
        Assert.Equal(TimeSpan.FromMilliseconds(350), gate.GetDelay(reason, Epoch + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void UserAndBurstRefreshesDoNotOpenOrWaitForTheItemWindow()
    {
        var gate = new HomeRealtimeRefreshGate(
            debounce: TimeSpan.FromMilliseconds(350),
            catalogBurstCooldown: TimeSpan.FromSeconds(30));
        gate.MarkRefreshed("catalog:catalog.item.changed", Epoch);

        Assert.False(HomeRealtimeRefreshGate.IsCatalogItemChange("user_state:user_state.changed"));
        Assert.False(HomeRealtimeRefreshGate.IsCatalogItemChange("catalog:library.changed"));
        Assert.Equal(TimeSpan.FromMilliseconds(350), gate.GetDelay("user_state:user_state.changed", Epoch + TimeSpan.FromSeconds(0.5)));
        Assert.Equal(TimeSpan.FromMilliseconds(350), gate.GetDelay("catalog:library.changed", Epoch + TimeSpan.FromSeconds(0.5)));

        gate.MarkRefreshed("catalog:library.changed", Epoch + TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), gate.GetDelay("catalog:library.item_added", Epoch + TimeSpan.FromSeconds(1)));
    }
}
