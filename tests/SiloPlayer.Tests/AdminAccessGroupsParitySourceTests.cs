namespace SiloPlayer.Tests;

public sealed class AdminAccessGroupsParitySourceTests
{
    [Fact]
    public void AccessGroupsExposeCompleteCurrentCrudContract()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Admin", "AccessGroup.cs");

        Assert.Contains("GetAccessGroupsAsync", api);
        Assert.Contains("CreateAccessGroupAsync", api);
        Assert.Contains("UpdateAccessGroupAsync", api);
        Assert.Contains("DeleteAccessGroupAsync", api);
        Assert.Contains("/api/v1/admin/access-groups", api);
        Assert.Contains("public List<int>? LibraryIds", model);
        Assert.Contains("public List<string>? AllowedPermissions", model);
        Assert.Contains("public bool RequestsAllowed", model);
        Assert.Contains("public bool IsDefault", model);
        Assert.Contains("public int MemberCount", model);
    }

    [Fact]
    public void AccessGroupEditorIncludesEveryCurrentWebUiControl()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminAccessGroupsPage.xaml");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "Admin", "AdminAccessGroupsViewModel.cs");

        Assert.Contains("Default for new users", page);
        Assert.Contains("All libraries", page);
        Assert.Contains("Maximum playback quality", page);
        Assert.Contains("Allow transcoded downloads", page);
        Assert.Contains("Allow media requests", page);
        Assert.Contains("Max streams", page);
        Assert.Contains("Max transcodes", page);
        Assert.Contains("Metadata curation", page);
        Assert.Contains("Marker editing", page);
        Assert.Contains("<Setter Property=\"OnContent\" Value=\"\" />", page);
        Assert.Contains("<Setter Property=\"OffContent\" Value=\"\" />", page);
        Assert.Contains("LibraryIds = AllLibraries", viewModel);
        Assert.Contains("DownloadAllowed && DownloadTranscodeAllowed", viewModel);
        Assert.Contains("AllowedPermissions = AllPermissions", viewModel);
        Assert.Contains("x:Name=\"GroupsGridView\"", page);
        Assert.Contains("x:Name=\"GroupIdentityGrid\"", page);
        Assert.Contains("ApplyResponsiveLayout", ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminAccessGroupsPage.xaml.cs"));
    }

    [Fact]
    public void AccessGroupsAreRegisteredAndUserAssignmentIsEditable()
    {
        var app = ReadRepoFile("src", "SiloPlayer", "App.xaml.cs");
        var shell = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs");
        var userModel = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Admin", "AdminUser.cs");
        var userDetail = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminUserDetailPage.xaml.cs");

        Assert.Contains("AdminAccessGroupsViewModel", app);
        Assert.Contains("NavAccessGroups", shell);
        Assert.Contains("typeof(AdminAccessGroupsPage)", shell);
        Assert.Contains("public long? AccessGroupId", userModel);
        Assert.Contains("AccessGroupIdSpecified", userModel);
        Assert.Contains("MakeFormLabel(\"Access Group\")", userDetail);
        Assert.Contains("AccessGroupIdSpecified   = true", userDetail);
    }

    [Fact]
    public void AdminNavigationUsesCurrentWebUiGroupsAndOrder()
    {
        var shell = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs");

        Assert.Contains("AddNavGroup(\"OVERVIEW\", NavDashboard, NavActivity, NavLogs, NavDiagnostics)", shell);
        Assert.Contains("AddNavGroup(\"CONTENT\", NavLibraries, NavCollections, NavSections, NavRequests)", shell);
        Assert.Contains("AddNavGroup(\"AUTOMATION\", NavAutoscan, NavScheduledTasks, NavSubtitles, NavMarkerHistory, NavRecommendations)", shell);
        Assert.Contains("AddNavGroup(\"USERS\", NavUsers, NavAccessGroups, NavDevices, NavPlaybackHistory, NavHistoryImport)", shell);
        Assert.Contains("AddNavGroup(\"SYSTEM\", NavSettings, NavPlugins, NavNodes, NavApiKeys, NavMaintenance)", shell);
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
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
