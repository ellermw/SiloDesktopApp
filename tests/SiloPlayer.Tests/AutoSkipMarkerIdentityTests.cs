using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AutoSkipMarkerIdentityTests
{
    [Fact]
    public void SameContentTransportReloadDoesNotResetAutoSkipState()
    {
        var identity = new AutoSkipMarkerIdentity();

        Assert.True(identity.ShouldResetFor("episode-1"));
        Assert.False(identity.ShouldResetFor("episode-1"));
    }

    [Fact]
    public void NewContentResetsAutoSkipState()
    {
        var identity = new AutoSkipMarkerIdentity();

        Assert.True(identity.ShouldResetFor("episode-1"));
        Assert.True(identity.ShouldResetFor("episode-2"));
    }

    [Fact]
    public void ClearingPlaybackResetsSameContentOnReplay()
    {
        var identity = new AutoSkipMarkerIdentity();
        Assert.True(identity.ShouldResetFor("episode-1"));
        Assert.False(identity.ShouldResetFor("episode-1"));

        identity.Clear();

        Assert.True(identity.ShouldResetFor("episode-1"));
    }
}
