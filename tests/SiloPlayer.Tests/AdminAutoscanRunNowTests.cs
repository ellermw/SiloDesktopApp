using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Run-now visibility from the pinned WebUI runNow.ts contract.</summary>
public sealed class AdminAutoscanRunNowTests
{
    [Fact]
    public void EnabledPollingSourceShowsActionRegardlessOfOtherSources()
    {
        Assert.True(AdminAutoscanRunNow.IsVisible(true, [new(true, "poll")]));
        Assert.True(AdminAutoscanRunNow.IsVisible(true, [new(true, "webhook"), new(true, "poll")]));
        Assert.True(AdminAutoscanRunNow.IsVisible(null, [new(true, "poll")]));
    }

    [Fact]
    public void NoEnabledPollingSourceHidesAction()
    {
        Assert.False(AdminAutoscanRunNow.IsVisible(true, []));
        Assert.False(AdminAutoscanRunNow.IsVisible(true, [new(true, "webhook")]));
        Assert.False(AdminAutoscanRunNow.IsVisible(true, [new(false, "poll")]));
    }

    [Fact]
    public void DisabledAutoscanHidesActionEvenWhileSourcesAreLoading()
    {
        Assert.False(AdminAutoscanRunNow.IsVisible(false, [new(true, "poll")]));
        Assert.False(AdminAutoscanRunNow.IsVisible(false, null));
    }

    [Fact]
    public void UnknownSourcesKeepActionAvailableUnlessKnownDisabled()
    {
        Assert.True(AdminAutoscanRunNow.IsVisible(true, null));
        Assert.True(AdminAutoscanRunNow.IsVisible(null, null));
    }
}
