namespace SiloPlayer.Tests;

public class AdminUsersParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml.cs"));

    [Fact]
    public void UsersPageUsesCurrentHeaderTabsAndActions()
    {
        Assert.Contains("MaxWidth=\"1640\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Access Groups\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Add User\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Invite Codes\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Public Signups\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Usage\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Create Code\"", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"User Defaults\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UsersTableIncludesCurrentSortableColumns()
    {
        foreach (var column in new[] { "Username", "Email", "Role", "Status", "Created", "Last Active" })
            Assert.Contains($"Text=\"{column}\"", Markup, StringComparison.Ordinal);
        Assert.Contains("UserSort_Click", Markup, StringComparison.Ordinal);
        Assert.Contains("_sortColumn", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateUserSortIndicators", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("FormatCreated(user.CreatedAt)", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Copy invite code", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("SignupEnabledToggle_Toggled", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void UsersActionsReachDetailHistoryEditAndDeleteFlows()
    {
        Assert.Contains("AdminUserDetailPage", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("AdminPlaybackHistoryPage", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenEditDialogAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenDeleteDialogAsync", CodeBehind, StringComparison.Ordinal);
    }
}
