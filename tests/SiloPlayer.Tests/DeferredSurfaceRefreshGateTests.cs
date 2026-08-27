using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DeferredSurfaceRefreshGateTests
{
    [Fact]
    public void RequestDefersWhileHiddenAndRunsOnceWhenSurfaceBecomesActive()
    {
        var gate = new DeferredSurfaceRefreshGate();

        Assert.False(gate.Request(isActive: false));
        Assert.True(gate.HasPendingRefresh);
        Assert.True(gate.Activate());
        Assert.False(gate.HasPendingRefresh);
        Assert.False(gate.Activate());
    }

    [Fact]
    public void RequestRunsImmediatelyForAnActiveSurface()
    {
        var gate = new DeferredSurfaceRefreshGate();

        Assert.True(gate.Request(isActive: true));
        Assert.False(gate.HasPendingRefresh);
    }

    [Fact]
    public void FailedRefreshStaysPendingUntilTheNextActivation()
    {
        var gate = new DeferredSurfaceRefreshGate();

        Assert.True(gate.Request(isActive: true));
        gate.Complete(succeeded: false);

        Assert.True(gate.HasPendingRefresh);
        Assert.True(gate.Activate());
    }
}
