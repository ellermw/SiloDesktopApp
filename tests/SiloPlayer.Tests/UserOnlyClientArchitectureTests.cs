namespace SiloPlayer.Tests;

public sealed class UserOnlyClientArchitectureTests
{
    [Fact]
    public void DesktopProjectContainsNoAdministrationUiTrees()
    {
        var root = FindRepositoryRoot();

        var adminViews = Path.Combine(root, "src", "SiloPlayer", "Views", "Admin");
        var adminViewModels = Path.Combine(root, "src", "SiloPlayer", "ViewModels", "Admin");
        Assert.False(Directory.Exists(adminViews) && Directory.EnumerateFiles(adminViews, "*", SearchOption.AllDirectories).Any());
        Assert.False(Directory.Exists(adminViewModels) && Directory.EnumerateFiles(adminViewModels, "*", SearchOption.AllDirectories).Any());
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Controls", "AdminCommandPaletteDialog.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Controls", "ServerActivityButton.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Controls", "ServerActivityButton.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "SetupWizardPage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "SetupWizardPage.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "SetupWizardViewModel.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Controls", "ImpersonationBanner.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Controls", "ImpersonationBanner.xaml.cs")));

        var forbiddenNames = Directory.EnumerateFiles(
                Path.Combine(root, "src"),
                "*",
                SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .Where(path => path.Split(Path.DirectorySeparatorChar).Any(segment =>
                segment.Contains("Admin", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("SetupWizard", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("Impersonation", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("MarkerEditor", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Assert.Empty(forbiddenNames);
    }

    [Fact]
    public void DesktopProjectContainsNoDedicatedAdministrationApiOrModels()
    {
        var root = FindRepositoryRoot();

        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs")));
        var adminModels = Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Admin");
        Assert.False(Directory.Exists(adminModels) && Directory.EnumerateFiles(adminModels, "*", SearchOption.AllDirectories).Any());
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "AdminLogStreamClient.cs")));
    }

    [Fact]
    public void ApprovedMaintenanceApiContainsOnlyTheThreeItemOperations()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer.Core", "Api", "MediaMaintenanceApi.cs"));

        Assert.Contains("/match/search", source);
        Assert.Contains("/match/apply", source);
        Assert.Contains("/refresh-metadata", source);
        Assert.Equal(3, source.Split("/api/v1/admin/", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void PlayerContainsNoWritableMarkerEditorSurface()
    {
        var root = FindRepositoryRoot();
        var playerOverlay = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml"));
        var playerOverlayCode = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));
        var playerService = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.DoesNotContain("MarkerEditButton", playerOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkerEditorPanel", playerOverlayCode, StringComparison.Ordinal);
        Assert.DoesNotContain("silo-marker-save", playerService, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagingRejectsEveryForbiddenPublishedSurface()
    {
        var root = FindRepositoryRoot();
        var buildScript = File.ReadAllText(Path.Combine(root, "installer", "build.ps1"));

        Assert.Contains("ForbiddenPublishedResourcePatterns", buildScript, StringComparison.Ordinal);
        Assert.Contains("Admin", buildScript, StringComparison.Ordinal);
        Assert.Contains("SetupWizard", buildScript, StringComparison.Ordinal);
        Assert.Contains("Impersonation", buildScript, StringComparison.Ordinal);
        Assert.Contains("MarkerEditor", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void BothMetadataRefreshCallersUseTheFocusedMaintenanceApi()
    {
        var root = FindRepositoryRoot();
        var detail = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        var menu = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "MediaItemMenu.cs"));

        Assert.Contains("RefreshMetadataDialog", detail, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<MediaMaintenanceApi>()", detail, StringComparison.Ordinal);
        Assert.Contains("RefreshMetadataDialog", menu, StringComparison.Ordinal);
        Assert.Contains("MediaMaintenanceApi maintenanceApi", menu, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("SiloPlayer repository root was not found.");
    }
}
