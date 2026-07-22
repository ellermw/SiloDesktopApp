using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AuthorizationPolicyTests
{
    [Fact]
    public void AdminUsesAdminPowersOnlyWithoutProfileOrOnPrimaryProfile()
    {
        var admin = new UserInfo { Role = "admin" };

        Assert.True(AuthorizationPolicy.IsActingAdmin(admin, null));
        Assert.True(AuthorizationPolicy.IsActingAdmin(admin, new Profile { IsPrimary = true }));
        Assert.False(AuthorizationPolicy.IsActingAdmin(admin, new Profile { IsPrimary = false }));
    }

    [Fact]
    public void SelectedButUnresolvedProfileFailsClosedLikeTheWebUi()
    {
        var admin = new UserInfo { Role = "admin" };

        Assert.False(AuthorizationPolicy.IsActingAdmin(admin, profile: null, hasSelectedProfile: true));
        Assert.True(AuthorizationPolicy.IsActingAdmin(admin, profile: null, hasSelectedProfile: false));
    }

    [Fact]
    public void ExplicitPermissionsRemainAvailableOnNonPrimaryProfiles()
    {
        var admin = new UserInfo { Role = "admin", Permissions = [AuthorizationPolicy.MarkerEdit] };
        var child = new Profile { IsPrimary = false };

        Assert.True(AuthorizationPolicy.HasPermission(admin, child, AuthorizationPolicy.MarkerEdit));
        Assert.False(AuthorizationPolicy.HasPermission(admin, child, AuthorizationPolicy.MetadataCuration));
    }

    [Fact]
    public void RegularUserCanUseAssignedCuratorPermission()
    {
        var user = new UserInfo { Role = "user", Permissions = [AuthorizationPolicy.MetadataCuration] };

        Assert.True(AuthorizationPolicy.HasPermission(user, new Profile(), AuthorizationPolicy.MetadataCuration));
        Assert.False(AuthorizationPolicy.IsActingAdmin(user, null));
    }
}
