using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData("admin", true, false, true)]
    [InlineData("user", true, false, true)]
    [InlineData("admin", false, false, false)]
    [InlineData("user", false, false, false)]
    [InlineData("admin", false, true, true)]
    [InlineData("user", false, true, false)]
    public void HouseholdManagementRequiresPrimaryOrServerCheckedProfilelessAdmin(string role, bool primary, bool profileless, bool expected)
        => Assert.Equal(expected, AuthorizationPolicy.CanManageHousehold(new UserInfo { Role = role },
            profileless ? null : new Profile { IsPrimary = primary }, !profileless));

    [Fact]
    public void HouseholdManagementFailsClosedForUnresolvedProfile()
        => Assert.False(AuthorizationPolicy.CanManageHousehold(new UserInfo { Role = "admin" }, null, true));

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
