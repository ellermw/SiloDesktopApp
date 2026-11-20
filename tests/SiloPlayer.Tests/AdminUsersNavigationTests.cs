using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminUsersNavigationTests
{
    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/admin/users?tab=invitations")]
    [InlineData("/admin/users?tab=invite-codes")]
    [InlineData("/admin/users/account-2?tab=overview")]
    [InlineData("/admin/users/account-2?tab=profiles")]
    [InlineData("/admin/users/account-2?tab=access")]
    [InlineData("/admin/users/account-2?tab=sign-in")]
    [InlineData("/admin/users/account-2?tab=activity")]
    [InlineData("/admin/users/account-2?tab=downloads")]
    [InlineData("/admin/users/account-2?tab=preferences&level=profile_device")]
    public void UsersRegistrationOpensOnlyItsOwnLeafAndDescendants(string href)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRoute.Users, resolution.Request!.Route);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        var registry = new AdminPageRegistry([new(AdminRoute.Users, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.False(registry.IsRegistered(AdminRoute.AccessGroups));
        Assert.False(registry.IsRegistered(AdminRoute.Devices));
        Assert.False(registry.IsRegistered(AdminRoute.HistoryImport));
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.Users));
    }
}
