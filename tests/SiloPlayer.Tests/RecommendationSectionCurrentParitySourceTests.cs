namespace SiloPlayer.Tests;

public sealed class RecommendationSectionCurrentParitySourceTests
{
    [Fact]
    public void SectionLoadIsLatestWinsAndCanceledOnNavigation()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "RecommendationSectionViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RecommendationSectionPage.xaml.cs");

        Assert.Contains("Interlocked.Exchange(ref _loadCts, owner)", viewModel);
        Assert.Contains("previous?.Cancel()", viewModel);
        Assert.Contains("ReferenceEquals(Volatile.Read(ref _loadCts), owner)", viewModel);
        Assert.Contains("ViewModel.CancelLoad();", page);
    }

    [Fact]
    public void PosterGridUsesCurrentResponsiveColumnCountsAndGutters()
    {
        var markup = ReadRepoFile("src", "SiloPlayer", "Views", "RecommendationSectionPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RecommendationSectionPage.xaml.cs");

        Assert.Contains("SizeChanged=\"Page_SizeChanged\"", markup);
        Assert.Contains("x:Name=\"ItemsLayout\"", markup);
        Assert.Contains("width < 640 ? 3", page);
        Assert.Contains("width < 768 ? 4", page);
        Assert.Contains("width < 1024 ? 5", page);
        Assert.Contains("width < 1280 ? 6 : 7", page);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
