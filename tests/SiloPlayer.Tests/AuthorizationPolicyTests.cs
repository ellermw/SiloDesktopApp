using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AuthorizationPolicyTests
{
    [Fact]
    public void AdminUsesAdminPowersOnlyWithoutProfileOrOnPrimaryProfile()
    {
        var admin = new UserInfo { Role = "admin" };

        Assert.True(AuthorizationPolicy.IsActingAdmin(admin, null, hasSelectedProfile: false));
        Assert.True(AuthorizationPolicy.IsActingAdmin(admin, new Profile { IsPrimary = true }, hasSelectedProfile: true));
        Assert.False(AuthorizationPolicy.IsActingAdmin(admin, new Profile { IsPrimary = false }, hasSelectedProfile: true));
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
        const string explicitPermission = "personal_collection_share";
        var admin = new UserInfo { Role = "admin", Permissions = [explicitPermission] };
        var child = new Profile { IsPrimary = false };

        Assert.True(AuthorizationPolicy.HasPermission(admin, child, true, explicitPermission));
        Assert.False(AuthorizationPolicy.HasPermission(admin, child, true, AuthorizationPolicy.MetadataCuration));
    }

    [Fact]
    public void RegularUserCanUseAssignedCuratorPermission()
    {
        var user = new UserInfo { Role = "user", Permissions = [AuthorizationPolicy.MetadataCuration] };

        Assert.True(AuthorizationPolicy.HasPermission(user, new Profile(), true, AuthorizationPolicy.MetadataCuration));
        Assert.False(AuthorizationPolicy.IsActingAdmin(user, null, hasSelectedProfile: false));
    }
}
