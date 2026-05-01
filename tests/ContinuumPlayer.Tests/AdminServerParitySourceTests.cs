namespace ContinuumPlayer.Tests;

public sealed class AdminServerParitySourceTests
{
    [Fact]
    public void AdminUserModelIncludesServerLastActiveField()
    {
        var source = ReadRepoFile("src", "ContinuumPlayer.Core", "Models", "Admin", "AdminUser.cs");

        Assert.Contains("public string? LastActiveAt { get; set; }", source);
    }

    [Fact]
    public void InviteCodeApiIncludesTopUpEndpoint()
    {
        var api = ReadRepoFile("src", "ContinuumPlayer.Core", "Api", "AdminApi.cs");
        var model = ReadRepoFile("src", "ContinuumPlayer.Core", "Models", "Admin", "InviteCode.cs");

        Assert.Contains("TopUpInviteCodeAsync", api);
        Assert.Contains("""/api/v1/admin/invite-codes/{id}/top-up""", api);
        Assert.Contains("public int AdditionalUses { get; set; }", model);
    }

    [Fact]
    public void InviteCodeScreensExposeTopUpAction()
    {
        var standalonePage = ReadRepoFile("src", "ContinuumPlayer", "Views", "Admin", "AdminInviteCodesPage.xaml.cs");
        var usersPage = ReadRepoFile("src", "ContinuumPlayer", "Views", "Admin", "AdminUsersPage.xaml.cs");

        Assert.Contains("OpenTopUpDialogAsync", standalonePage);
        Assert.Contains("OpenTopUpInviteCodeDialogAsync", usersPage);
        Assert.Contains("TopUpInviteCodeAsync", standalonePage);
        Assert.Contains("TopUpInviteCodeAsync", usersPage);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var pathParts = new string[parts.Length + 1];
        pathParts[0] = FindRepositoryRoot();
        Array.Copy(parts, 0, pathParts, 1, parts.Length);
        return File.ReadAllText(Path.Combine(pathParts));
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "ContinuumPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
