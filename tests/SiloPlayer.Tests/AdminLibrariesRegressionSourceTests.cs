namespace SiloPlayer.Tests;

public class AdminLibrariesRegressionSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    [Fact]
    public void StaleIdRoutesMatchCurrentLibrariesContract()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));

        Assert.Contains("/api/v1/libraries/stale-ids", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/admin/libraries/stale-ids", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnteringAdminHidesGlobalActivityControl()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var adminClick = source[source.IndexOf("private void Admin_Click", StringComparison.Ordinal)..];

        Assert.Contains("MainServerActivityButton.Visibility = Visibility.Collapsed", adminClick, StringComparison.Ordinal);
        Assert.Contains("OnNavigated_SynchronizeShellChrome", source, StringComparison.Ordinal);
        Assert.Contains("e.SourcePageType != typeof(Views.Admin.AdminShellPage)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LibrariesShowsSkeletonBeforePrimaryRequestCompletes()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));
        var loaded = source[source.IndexOf("private async void Page_Loaded", StringComparison.Ordinal)..];

        Assert.True(
            loaded.IndexOf("BuildLibraryLoadingRows();", StringComparison.Ordinal)
            < loaded.IndexOf("await ViewModel.LoadLibrariesAsync();", StringComparison.Ordinal));
    }

    [Fact]
    public void LibrariesDoesNotStackAProgressRingOrEmptyStateOverItsTableState()
    {
        var markup = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml"));
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));

        Assert.DoesNotContain("<ProgressRing", markup, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))", source, StringComparison.Ordinal);
        Assert.Contains("EmptyState.Visibility = Visibility.Collapsed;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QueuedScansRemainVisibleAsActiveLibraryWork()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));

        Assert.Contains(
            "scan.Status is \"accepted\" or \"queued\" or \"running\"",
            source,
            StringComparison.Ordinal);

        var activitySource = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Controls", "ServerActivityButton.xaml.cs"));
        Assert.Contains(
            "run.Status is \"accepted\" or \"queued\" or \"running\"",
            activitySource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LibrariesHydratesFromTheSharedCachedScanSnapshot()
    {
        var page = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));
        var channel = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Services", "EventChannelClient.cs"));

        Assert.Contains("TryGetLatestSnapshot(\"scans\"", page, StringComparison.Ordinal);
        Assert.Contains("ApplyEventToCachedSnapshot", channel, StringComparison.Ordinal);
        Assert.Contains("_latestSnapshots[channel] = JsonSerializer.SerializeToElement(items);", channel, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryEditorPlacesContentInSecondGridColumn()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));

        Assert.Contains("Grid.SetColumn(sectionBorder, 1);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryEditorUsesExistingAccentButtonStyle()
    {
        var pageSource = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));
        var themeSource = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Themes", "DarkTheme.xaml"));

        Assert.Contains("Resources[\"AccentButtonStyle\"]", pageSource, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AccentButtonStyle\"", themeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TroubleshootingRowsAreLoadedInOneAssignmentAndRenderedLazily()
    {
        var viewModel = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminLibrariesViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));

        Assert.Contains("public List<LibrarySkippedRoot> SkippedRoots", viewModel, StringComparison.Ordinal);
        Assert.Contains("SkippedRoots = await _adminApi.GetSkippedRootsAsync();", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var s in skipped) SkippedRoots.Add(s)", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (SkippedContent.Visibility != Visibility.Visible)", page, StringComparison.Ordinal);
        Assert.Contains("private const int SKIPPED_ROOTS_PAGE_SIZE = 10;", page, StringComparison.Ordinal);
        Assert.Contains(".Take(SKIPPED_ROOTS_PAGE_SIZE)", page, StringComparison.Ordinal);
    }
}
