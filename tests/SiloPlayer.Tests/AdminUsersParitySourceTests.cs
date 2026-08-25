namespace SiloPlayer.Tests;

public class AdminUsersParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml.cs"));

    private static string UserModel => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer.Core", "Models", "Admin", "AdminUser.cs"));

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

    [Fact]
    public void UserAndInviteTablesKeepCurrentGeometryAtNarrowWidths()
    {
        Assert.Contains("x:Name=\"UsersTableScroll\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InviteCodesTableScroll\"", Markup, StringComparison.Ordinal);
        Assert.Contains("AdminUsersPage_SizeChanged", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFormIncludesCurrentAccessAndAccountContract()
    {
        Assert.Contains("Marker Editing", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Metadata Curation", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Permissions = permissionList", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("CreateDefaultProfile = true", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidateCreateUser", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidateUpdateUser", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("public List<string> Permissions", UserModel, StringComparison.Ordinal);
        Assert.Contains("public bool CreateDefaultProfile", UserModel, StringComparison.Ordinal);
    }

    [Fact]
    public void InviteMutationsOnlyReportSuccessAfterApiSuccess()
    {
        Assert.Contains("InviteCodesViewModel.ErrorMessage", CodeBehind, StringComparison.Ordinal);
        var createCall = CodeBehind.IndexOf("var created = await InviteCodesViewModel.CreateInviteCodeAsync", StringComparison.Ordinal);
        Assert.True(createCall >= 0, "Expected the invite creation API call.");
        var createValidated = CodeBehind.IndexOf("if (created != null)", createCall, StringComparison.Ordinal);
        Assert.True(createValidated > createCall, "Expected invite creation to validate its API result.");
        var createGuard = ExtractBlock(CodeBehind, createValidated);
        Assert.Contains("ShowStatus(InviteCodesViewModel.StatusMessage", createGuard, StringComparison.Ordinal);
        Assert.Contains("Title = \"Top Up Invite Code\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Add Uses\"", CodeBehind, StringComparison.Ordinal);
        var topUpCall = CodeBehind.IndexOf("var updated = await InviteCodesViewModel.TopUpInviteCodeAsync", StringComparison.Ordinal);
        Assert.True(topUpCall >= 0, "Expected the invite top-up API call.");
        var topUpValidated = CodeBehind.IndexOf("if (updated != null)", topUpCall, StringComparison.Ordinal);
        Assert.True(topUpValidated > topUpCall, "Expected invite top-up to validate its API result.");
        var topUpGuard = ExtractBlock(CodeBehind, topUpValidated);
        Assert.Contains("ShowStatus(InviteCodesViewModel.StatusMessage", topUpGuard, StringComparison.Ordinal);
    }

    private static string ExtractBlock(string source, int statementStart)
    {
        var openingBrace = source.IndexOf('{', statementStart);
        Assert.True(openingBrace >= 0, "Expected guarded block opening brace.");
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[openingBrace..(index + 1)];
        }

        throw new InvalidOperationException("Expected guarded block closing brace.");
    }
}
