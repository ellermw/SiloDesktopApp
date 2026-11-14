using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorAuthorityTests
{
    [Theory]
    [InlineData("user", true, true)]
    [InlineData("admin", false, true)]   // non-primary profile
    [InlineData("admin", true, false)]   // selected profile not resolved yet: fail closed
    public void NonActingPrincipalsCannotCapture(string role, bool primary, bool resolved)
    {
        using var kit = AdminMonitorKit.Create(role, primary, resolved);
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);

        Assert.False(authority.IsAvailable);
        Assert.Throws<MonitorAuthorityRetiredException>(() => authority.Capture());
    }

    [Fact]
    public void ActingAdminCapturesCurrentLeaseBoundToContextAndAccount()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);

        using var lease = authority.Capture();

        Assert.True(authority.IsAvailable);
        Assert.True(lease.IsCurrent);
        Assert.True(authority.IsCurrent(lease));
        Assert.Equal(kit.Client.CaptureContext(), lease.Context);
        Assert.Equal("1", lease.AccountId);
        Assert.False(lease.Token.IsCancellationRequested);
        lease.ThrowIfRetired();
    }

    [Fact]
    public void SameAccountAdminToUserToAdminNeverRevivesTheOldLease()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var changes = 0;
        authority.AvailabilityChanged += () => changes++;
        var old = authority.Capture();

        kit.SetRole("user");
        Assert.False(authority.IsAvailable);
        Assert.False(old.IsCurrent);
        Assert.True(old.Token.IsCancellationRequested);
        Assert.Throws<MonitorAuthorityRetiredException>(() => authority.Capture());

        kit.SetRole("admin");
        Assert.True(authority.IsAvailable);
        Assert.False(old.IsCurrent);
        Assert.Throws<MonitorAuthorityRetiredException>(old.ThrowIfRetired);
        using var fresh = authority.Capture();
        Assert.True(fresh.Epoch > old.Epoch);
        Assert.True(fresh.IsCurrent);
        Assert.True(changes >= 2);
    }

    [Fact]
    public void BenignAccountRefreshAndTokenRefreshKeepTheLease()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();

        kit.Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
        kit.Client.SetAccessToken("rotated-synthetic-access");

        Assert.True(lease.IsCurrent);
        Assert.False(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public void AccessInvalidationRetiresLikeTheEventChannelsAccessChanged()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var raised = 0;
        authority.AvailabilityChanged += () => raised++;
        var lease = authority.Capture();

        // EventChannelClient's access_changed/4001 invalidates access authority,
        // unlike transport-only catch-up.
        kit.Client.InvalidateAccessContext();

        Assert.False(lease.IsCurrent);
        Assert.True(lease.Token.IsCancellationRequested);
        Assert.Equal(1, raised);
        Assert.True(authority.IsAvailable);
        using var next = authority.Capture();
        Assert.True(next.IsCurrent);
    }

    [Fact]
    public void TransportCatchUpRebindsRequestsWithoutRetiringPageOwnership()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var epoch = authority.Epoch;
        var raised = 0;
        authority.AvailabilityChanged += () => raised++;
        var before = lease.Context;

        Assert.True(kit.Client.TryInvalidateAccessContext(before, out var updated, transportCatchUp: true));

        Assert.NotEqual(before, updated);
        Assert.True(lease.IsCurrent);
        Assert.False(lease.Token.IsCancellationRequested);
        Assert.Equal(updated, lease.Context);
        Assert.Equal(epoch, authority.Epoch);
        Assert.Equal(0, raised);
        using var action = authority.Capture();
        Assert.Equal(lease.Epoch, action.Epoch);
        Assert.Equal(lease.Context, action.Context);

        Assert.True(kit.Client.TryInvalidateAccessContext(updated, out _));
        Assert.False(lease.IsCurrent);
        Assert.False(action.IsCurrent);
        Assert.True(lease.Token.IsCancellationRequested);
        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameProfileRebindAndProofChangesStillRetireAfterCatchUp(bool proofOnly)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        Assert.True(kit.Client.TryInvalidateAccessContext(lease.Context, out var updated, transportCatchUp: true));
        if (proofOnly) kit.Client.AdoptProfileProof(updated, "synthetic-proof");
        else kit.Client.SetProfile("p1");
        Assert.False(lease.IsCurrent);
        Assert.Throws<MonitorAuthorityRetiredException>(lease.ThrowIfRetired);
    }

    [Fact]
    public void ProfileSwitchAndLogoutRetire()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var lease = authority.Capture();

        kit.Auth.SelectProfile("p2", null, new Profile { Id = "p2", IsPrimary = false });
        Assert.False(lease.IsCurrent);
        Assert.False(authority.IsAvailable);

        kit.Auth.SelectProfile("p1", null, new Profile { Id = "p1", IsPrimary = true });
        var second = authority.Capture();
        kit.Auth.Logout();
        Assert.False(second.IsCurrent);
        Assert.True(second.Token.IsCancellationRequested);
        Assert.False(authority.IsAvailable);
    }

    [Fact]
    public void ContextChangeWithoutAnEventIsStillNotCurrent()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();

        // A raw client profile change raises no auth event; the live check still fails closed.
        kit.Client.SetProfile("other");

        Assert.False(lease.IsCurrent);
        Assert.Throws<MonitorAuthorityRetiredException>(lease.ThrowIfRetired);
    }

    [Fact]
    public void ExplicitRetireDisposeAndCallerTokenEndLeases()
    {
        using var kit = AdminMonitorKit.Create();
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var caller = new CancellationTokenSource();
        var linked = authority.Capture(caller.Token);
        caller.Cancel();
        Assert.True(linked.Token.IsCancellationRequested);
        Assert.False(linked.IsCurrent);

        var retired = authority.Capture();
        authority.Retire();
        Assert.False(retired.IsCurrent);

        var disposedLease = authority.Capture();
        disposedLease.Dispose();
        Assert.False(authority.IsCurrent(disposedLease));

        var last = authority.Capture();
        authority.Dispose();
        Assert.False(last.IsCurrent);
        Assert.False(authority.IsAvailable);
        Assert.Throws<ObjectDisposedException>(() => authority.Capture());
    }

    [Fact]
    public void LeaseFromAnotherAuthorityIsNeverCurrent()
    {
        using var kit = AdminMonitorKit.Create();
        using var first = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var second = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = first.Capture();
        Assert.False(second.IsCurrent(lease));
    }
}
