using SiloPlayer.Core.Services;

public sealed class SidebarPresentationStateTests
{
    [Fact]
    public void DetailChainCompactsAndReturningRestoresBrowsingPreference()
    {
        var state = new SidebarPresentationState { BrowsingOpen = true };
        state.Navigate(true);
        Assert.False(state.IsOpen);
        state.Navigate(true);
        Assert.False(state.IsOpen);
        Assert.True(state.BrowsingOpen);
        state.Navigate(false);
        Assert.True(state.IsOpen);
    }
    [Fact]
    public void ExplicitDetailToggleSurvivesSiblingNavigationWithoutChangingSavedPreference()
    {
        var state = new SidebarPresentationState { BrowsingOpen = false };
        state.Navigate(true);
        Assert.False(state.Toggle(true));
        state.Navigate(true);
        Assert.True(state.IsOpen);
        Assert.False(state.BrowsingOpen);
        state.Navigate(false);
        Assert.False(state.IsOpen);
        state.Navigate(true);
        Assert.False(state.IsOpen);
    }
    [Fact]
    public void BrowsingToggleChangesPersistentPreference()
    {
        var state = new SidebarPresentationState();
        Assert.True(state.Toggle(false));
        state.Navigate(true);
        state.Navigate(false);
        Assert.False(state.IsOpen);
    }
}
