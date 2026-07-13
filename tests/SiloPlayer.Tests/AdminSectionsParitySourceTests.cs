namespace SiloPlayer.Tests;

public class AdminSectionsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSectionsPage.xaml.cs"));

    private static string ViewModelSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminSectionsViewModel.cs"));

    [Fact]
    public void SectionsPaintsSkeletonRowsBeforeEveryScopeLoad()
    {
        Assert.True(PageSource.Split("BuildLoadingSkeletons();", StringSplitOptions.None).Length >= 4);
        Assert.Contains("if (ViewModel.IsLoading) return;", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionsLoadsReferenceDataAndRowsInParallel()
    {
        Assert.Contains("var librariesTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var recipesTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var collectionsTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var sectionsTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("await Task.WhenAll(requests)", ViewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestScopeSelectionWinsOverOlderRequests()
    {
        Assert.Contains("Interlocked.Increment(ref _loadVersion)", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("if (loadVersion != _loadVersion) return;", ViewModelSource, StringComparison.Ordinal);
    }
}
