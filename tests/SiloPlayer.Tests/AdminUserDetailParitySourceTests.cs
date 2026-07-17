namespace SiloPlayer.Tests;

public sealed class AdminUserDetailParitySourceTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static string Page => File.ReadAllText(Path.Combine(Root, "src", "SiloPlayer", "Views", "Admin", "AdminUserDetailPage.xaml.cs"));
    private static string ViewModel => File.ReadAllText(Path.Combine(Root, "src", "SiloPlayer", "ViewModels", "Admin", "AdminUserDetailViewModel.cs"));

    [Fact]
    public void DetailPageExposesEveryCurrentWebUiTab()
    {
        Assert.Contains("[\"Overview\", \"Settings\", \"Devices\", \"Profiles\", \"Watch History\", \"IP History\"]", Page, StringComparison.Ordinal);
        Assert.Contains("[OverviewPanel, SettingsPanel, DevicesPanel, ProfilesPanel, HistoryPanel, IPPanel]", Page, StringComparison.Ordinal);
        Assert.Contains("BuildUserSettingsTab", Page, StringComparison.Ordinal);
        Assert.Contains("BuildDeviceOverridesTab", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondaryTabsLoadIndependentlyFromOverview()
    {
        Assert.Contains("LoadUserSettingsAsync", ViewModel, StringComparison.Ordinal);
        Assert.Contains("LoadDeviceSettingsAsync", ViewModel, StringComparison.Ordinal);
        Assert.Contains("LoadProfilesAsync", ViewModel, StringComparison.Ordinal);
        Assert.Contains("LoadHistoryAsync", ViewModel, StringComparison.Ordinal);
        Assert.Contains("LoadIPsAsync", ViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.WhenAll(userTask, profilesTask", ViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void UserEditorAndOverviewExposeAssignablePermissions()
    {
        Assert.Contains("Marker Editing", Page, StringComparison.Ordinal);
        Assert.Contains("Metadata Curation", Page, StringComparison.Ordinal);
        Assert.Contains("Permissions              = permissions", Page, StringComparison.Ordinal);
        Assert.Contains("AdminDeviceNavigationTarget", Page, StringComparison.Ordinal);
        Assert.Contains("Reset this override?", Page, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
