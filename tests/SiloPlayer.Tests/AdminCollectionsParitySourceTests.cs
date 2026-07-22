namespace SiloPlayer.Tests;

public class AdminCollectionsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminCollectionsPage.xaml.cs"));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminCollectionsPage.xaml"));

    private static string ViewModelSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminCollectionsViewModel.cs"));

    [Fact]
    public void CollectionsPaintsSkeletonsBeforeInitialAndScopedLoads()
    {
        Assert.True(PageSource.Split("BuildLoadingSkeletons();", StringSplitOptions.None).Length >= 3);
        Assert.Contains("if (ViewModel.IsLoading) return;", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionsLoadsIndependentAdminDataInParallel()
    {
        Assert.Contains("var libraryTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var collectionsTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var groupsTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("await Task.WhenAll(requests)", ViewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleScopedLoadsCannotOverwriteTheLatestLibrarySelection()
    {
        Assert.Contains("Interlocked.Increment(ref _loadVersion)", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("if (loadVersion != _loadVersion", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource? _loadCts", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("public void CancelLoad()", ViewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedBoardKeepsGlobalCollectionCountsAndTemplateJobFeedback()
    {
        Assert.Contains("AllCollections", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.AllCollections.Count", PageSource, StringComparison.Ordinal);
        Assert.Contains("template_bundle_apply", PageSource, StringComparison.Ordinal);
        Assert.Contains("TemplateApplyJobBanner", Markup, StringComparison.Ordinal);
        Assert.Contains("Collection defaults applied", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionsUsesResponsiveCurrentCanvasAndGlobalMutationFeedback()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", PageSource, StringComparison.Ordinal);
        Assert.Contains("ToastService", PageSource, StringComparison.Ordinal);
        Assert.Contains("SurfaceMutationResult", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedBoardMatchesCurrentWebUiGroupingAndSortControls()
    {
        Assert.Contains("User Collections", PageSource, StringComparison.Ordinal);
        Assert.Contains("Default sort (end-user view)", PageSource, StringComparison.Ordinal);
        Assert.Contains("Recently Updated", PageSource, StringComparison.Ordinal);
        Assert.Contains("Most Items", PageSource, StringComparison.Ordinal);
        Assert.Contains("Reserved slot for user-published collections", PageSource, StringComparison.Ordinal);
        Assert.Contains("Drop collections here to remove them from any group", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedBoardUsesNativeDragDropForGroupsAndCollections()
    {
        Assert.Contains("args.Data.SetText($\"section:", PageSource, StringComparison.Ordinal);
        Assert.Contains("$\"collection:{draggedIds[0]}\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("MoveGroupSectionToAsync", PageSource, StringComparison.Ordinal);
        Assert.Contains("MoveCollectionsToAsync", PageSource, StringComparison.Ordinal);
        Assert.Contains("_selectedCollectionIds", PageSource, StringComparison.Ordinal);
        Assert.Contains("collections:", PageSource, StringComparison.Ordinal);
        Assert.Contains("collectionDragEnabled", PageSource, StringComparison.Ordinal);
        Assert.Contains("ReorderCollectionGroupsAsync", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("ReorderCollectionsInGroupAsync", ViewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedRowsShowCurrentSyncStatusAndGroupEditorProtectsUserGroup()
    {
        Assert.Contains("col.LastSyncStatus", PageSource, StringComparison.Ordinal);
        Assert.Contains("user_collections", PageSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SecondaryButtonText = canDelete ? \"Delete group\"", PageSource, StringComparison.Ordinal);
    }
}
