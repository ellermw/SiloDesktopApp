namespace SiloPlayer.Tests;

public sealed class AdminInvitationsCurrentParitySourceTests
{
    [Fact]
    public void UsersPageExposesCurrentThreeTabAdminContract()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml.cs");

        Assert.Contains("Text=\"Users\"", xaml);
        Assert.Contains("Text=\"Invitations\"", xaml);
        Assert.Contains("Text=\"Invite Codes\"", xaml);
        Assert.Contains("Invite someone", xaml);
        Assert.Contains("No invitations yet. Invite someone to get started.", xaml);
        Assert.Contains("TabInvitations_Click", page);
        Assert.Contains("BuildInvitationRows", page);
    }

    [Fact]
    public void InvitationWorkflowCarriesAllCurrentFieldsAndOneTimeLinkActions()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Admin", "Invitation.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml.cs");

        Assert.Contains("/api/v1/admin/invitations", api);
        Assert.Contains("/resend", api);
        Assert.Contains("CreateInvitationRequest", model);
        Assert.Contains("AccessGroupId", model);
        Assert.Contains("LibraryIds", model);
        Assert.Contains("CreateProfile", model);
        Assert.Contains("ShowTour", model);
        Assert.Contains("ClaimUrl", model);
        Assert.Contains("Personal note (optional)", page);
        Assert.Contains("Create their first profile", page);
        Assert.Contains("Show the feature tour on first sign-in", page);
        Assert.Contains("Copy link", page);
        Assert.Contains("Revoke invitation", page);
        Assert.Contains("Resend with a fresh link", page);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
