namespace SiloPlayer.Tests;

public class AdminCollectionsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminCollectionsPage.xaml.cs"));

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
        Assert.Contains("if (loadVersion != _loadVersion) return;", ViewModelSource, StringComparison.Ordinal);
    }
}
