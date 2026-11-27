using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

/// <summary>The words of the Users confirmations (official ConfirmDialog callers): each names the thing acted on and no key falls through as developer text.</summary>
public sealed class AdminUsersConfirmTextTests
{
    private static string T(string key, params (string Name, object? Value)[] args) => AdminUsersText.T(key, args);

    [Theory]
    [InlineData("web.admin.users.page.disableTitle", "Disable Target?")]
    [InlineData("web.admin.users.page.transferTitle", "Make Target the server owner?")]
    [InlineData("web.admin.users.deleteDialog.title", "Delete Target?")]
    [InlineData("web.admin.users.deleteDialog.confirmLabel", "Type Target to confirm")]
    [InlineData("web.settings.sessions.confirm.allTitle", "Sign Target out everywhere?")]
    public void ATitleNamesTheAccount(string key, string expected) => Assert.Equal(expected, T(key, ("username", "Target")));

    [Fact]
    public void ProfileAndProviderConfirmationsNameWhatTheyAreAbout()
    {
        Assert.Equal("Remove Kids's PIN?", T("web.admin.users.detail.profiles.removePinTitle", ("name", "Kids")));
        Assert.Equal("Delete Kids?", T("web.admin.users.detail.profiles.deleteTitle", ("name", "Kids")));
        Assert.Equal("Unlink Plex?", T("web.admin.common.adminUserSignIn.unlinkConfirm.title", ("provider", "Plex")));
        Assert.Equal("Sign out Pixel 8?", T("web.settings.sessions.confirm.deviceTitle", ("device", "Pixel 8")));
        Assert.Equal("Reset Kids's Home sections?", T("web.admin.users.detail.preferences.home.resetTitle", ("profile", "Kids")));
    }

    [Fact]
    public void TheDeleteDescriptionCountsTheProfilesAndTheUnlinkWarningNeedsTheLastWay()
    {
        Assert.Contains("its 1 profile,", T("web.admin.users.deleteDialog.descriptionCounted", ("count", 1), ("count_text", 1)));
        Assert.Contains("its 3 profiles,", T("web.admin.users.deleteDialog.descriptionCounted", ("count", 3), ("count_text", 3)));
        Assert.Contains("won't be able to sign in until", T("web.admin.common.adminUserSignIn.unlinkConfirm.description", ("username", "Target"), ("last_way", "yes")));
        Assert.DoesNotContain("won't be able to sign in", T("web.admin.common.adminUserSignIn.unlinkConfirm.description", ("username", "Target"), ("last_way", "no")));
    }

    [Theory]
    [InlineData("web.admin.settings.inviteCodes.delete.confirm", "Delete")]
    [InlineData("web.admin.settings.invitations.revoke.confirm", "Revoke")]
    [InlineData("web.admin.users.impersonateDialog.confirm", "View as user")]
    [InlineData("web.admin.users.page.transferConfirm", "Make owner")]
    [InlineData("web.admin.users.deleteDialog.disableInstead", "Disable instead")]
    [InlineData("admin.native.users.prefs.close", "Close")]
    public void TheConfirmLabelsAreTheOfficialOnes(string key, string expected) => Assert.Equal(expected, T(key));

    [Fact]
    public void EveryKeyTheConfirmationsUseIsInTheCatalog()
    {
        string[] keys =
        [
            "web.admin.users.page.disableDescription", "web.admin.users.page.disableConfirm", "web.admin.users.page.transferDescription", "web.admin.users.deleteDialog.description",
            "web.admin.users.deleteDialog.disableHint", "web.admin.users.detail.profiles.removePinDescription", "web.admin.users.detail.profiles.removePinPrimary",
            "web.admin.users.detail.profiles.deleteDescription", "web.admin.settings.invitations.resend.title", "web.admin.settings.invitations.resend.description",
            "web.admin.settings.invitations.revoke.title", "web.admin.settings.invitations.revoke.linkDescription", "web.admin.settings.invitations.revoke.emailDescription",
            "web.admin.settings.inviteCodes.delete.title", "web.settings.sessions.confirm.adminClientDescription", "web.settings.sessions.confirm.allDescription", "web.settings.sessions.confirm.note",
            "web.admin.users.detail.preferences.home.resetDescription", "web.admin.users.detail.preferences.home.resetConfirm", "web.admin.users.detail.preferences.removal.titleDefault",
            "web.admin.users.detail.preferences.removal.toDefault", "web.admin.users.detail.preferences.removal.confirmDefault", "web.admin.users.impersonateDialog.descriptionAdmin",
            "web.admin.users.impersonateDialog.descriptionUser", "web.admin.users.impersonateDialog.title", "admin.native.users.confirmFailed", "admin.native.users.quota.resetTitle",
            "admin.native.users.quota.resetGroup", "admin.native.users.quota.resetServer", "admin.native.users.serverRefused", "admin.native.users.reloadFirst",
        ];
        foreach (var key in keys) Assert.True(AdminUsersText.Has(key), key);
    }

    [Fact]
    public void TheImpersonationTextNamesTheAccountItContinuesAs() =>
        Assert.StartsWith("Continue as \"Target\"?", T("web.admin.users.impersonateDialog.descriptionUser", ("username", "Target")));
}
