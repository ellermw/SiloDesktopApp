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

        Assert.Contains("MainServerActivityButton.SetHostVisibility(false)", adminClick, StringComparison.Ordinal);
        Assert.Contains("OnNavigated_SynchronizeShellChrome", source, StringComparison.Ordinal);
        Assert.Contains("e.SourcePageType != typeof(Views.Admin.AdminShellPage)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityTriggerUsesOneHostGateAndCurrentWebUiIdleBehavior()
    {
        var windowMarkup = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "MainWindow.xaml"));
        var controlMarkup = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Controls", "ServerActivityButton.xaml"));
        var control = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Controls", "ServerActivityButton.xaml.cs"));

        Assert.Contains("HideWhenEmpty=\"True\"", windowMarkup, StringComparison.Ordinal);
        Assert.Contains("SetHostVisibility(bool allowed)", control, StringComparison.Ordinal);
        Assert.Contains("_hostVisibilityAllowed && (!HideWhenEmpty || total > 0)", control, StringComparison.Ordinal);
        Assert.Contains("Width=\"36\"", controlMarkup, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,-2,-2,0\"", controlMarkup, StringComparison.Ordinal);
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

    [Fact]
    public void LibrariesCarriesCurrentUnreachableRootSafetyContract()
    {
        var page = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));
        var model = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Models", "Catalog", "Library.cs"));

        Assert.Contains("library.ScanWarningCode is \"empty_root\" or \"dead_root\"", page, StringComparison.Ordinal);
        Assert.Contains("Root unreachable", page, StringComparison.Ordinal);
        Assert.Contains("Check Mount", page, StringComparison.Ordinal);
        Assert.Contains("Confirm Cleanup", page, StringComparison.Ordinal);
        Assert.Contains("r.SuspectEmpty", page, StringComparison.Ordinal);
        Assert.Contains("public bool SuspectEmpty", model, StringComparison.Ordinal);
    }

    [Fact]
    public void LibrariesMatchesWebUiResponsiveHeaderAndScrollableTable()
    {
        var markup = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml"));
        var page = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));

        Assert.Contains("x:Name=\"PageHeaderActions\"", markup, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1160\"", markup, StringComparison.Ordinal);
        Assert.Contains("contentWidth < 1080", page, StringComparison.Ordinal);
        Assert.Contains("width >= 1280 ? 40", page, StringComparison.Ordinal);
    }
}
