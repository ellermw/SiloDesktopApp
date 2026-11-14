namespace SiloPlayer.Tests;

public sealed class UserOnlyClientArchitectureTests
{
    [Fact]
    public void DesktopProjectContainsOnlyApprovedMonitorAdministrationSurfaces()
    {
        var root = FindRepositoryRoot();
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "docs", "parity", "admin-monitor-surfaces.json")));
        var approved = manifest.RootElement.GetProperty("sourceFiles").EnumerateArray()
            .Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("src/SiloPlayer/Views/Admin/AdminShellPage.cs", approved);
        var unexpected = Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .Where(path => path.Split('/').Any(segment => segment.Contains("Admin", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("SetupWizard", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("Impersonation", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("MarkerEditor", StringComparison.OrdinalIgnoreCase)))
            .Where(path => path != "src/SiloPlayer/Views/Dialogs/MarkerEditorDialog.cs")
            .Where(path => !approved.Contains(path)).ToArray();
        Assert.Empty(unexpected);
        Assert.All(approved, path => Assert.True(File.Exists(Path.Combine(root, path)), path));
        Assert.DoesNotContain(approved, path => path.Contains("SetupWizard", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Impersonation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MonitorDoesNotRestoreLegacyOrUnrelatedAdministrationApis()
    {
        var root = FindRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs")));
        var apiSources = Directory.EnumerateFiles(Path.Combine(root, "src", "SiloPlayer.Core", "Api"), "AdminMonitorApi*.cs")
            .Select(File.ReadAllText);
        foreach (var source in apiSources)
        {
            Assert.DoesNotContain("/api/v1/", source);
            foreach (var forbidden in new[] { "/admin/access-groups", "/admin/invitations", "/admin/plugins", "/admin/impersonation", "/admin/setup" })
                Assert.DoesNotContain(forbidden, source);
        }
    }

    [Fact]
    public void ApprovedMaintenanceApiContainsOnlyItemScopedMaintenanceOperations()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer.Core", "Api", "MediaMaintenanceApi.cs"));

        Assert.Contains("/match/search", source);
        Assert.Contains("/match/apply", source);
        Assert.Contains("/refresh-metadata", source);
        Assert.Contains("/metadata\"", source);
        Assert.Contains("/metadata-translation\"", source);
        Assert.Contains("/images/apply\"", source);
        Assert.DoesNotContain("/api/v2/admin/users", source);
        Assert.DoesNotContain("/api/v2/admin/settings", source);
        Assert.DoesNotContain("/api/v2/admin/libraries", source);
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
        Assert.Contains("ApprovedMonitorPublishedResources", buildScript, StringComparison.Ordinal);
        Assert.Contains("Views/Admin/AdminShellPage.xbf", buildScript, StringComparison.Ordinal);
        Assert.Contains("'Admin'", buildScript, StringComparison.Ordinal);
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
