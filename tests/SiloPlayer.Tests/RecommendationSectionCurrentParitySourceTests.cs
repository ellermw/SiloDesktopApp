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
        var customization = ReadRepoFile("src", "SiloPlayer", "Services", "UICustomizationService.cs");

        Assert.Contains("SizeChanged=\"Page_SizeChanged\"", markup);
        Assert.Contains("x:Name=\"ItemsLayout\"", markup);
        Assert.Contains("x:Name=\"LoadingLayout\"", markup);
        Assert.Contains("ElementPrepared=\"ItemsGrid_ElementPrepared\"", markup);
        Assert.Contains("ElementPrepared=\"LoadingSkeleton_ElementPrepared\"", markup);
        Assert.Contains("_uiCustomizationService.GetPosterColumnCount(contentWidth)", page);
        Assert.Contains("public int GetPosterColumnCount(double contentWidth)", customization);
        Assert.Contains("\"compact\" => contentWidth >= 1280 ? 10", customization);
        Assert.Contains("\"large\" => contentWidth >= 1280 ? 6", customization);
        Assert.Contains("_ => contentWidth >= 1280 ? 8", customization);
        Assert.Contains("card.SetCatalogGridLayout(_cardWidth)", page);
        Assert.Contains("poster.Height = _cardWidth * 1.5", page);
        Assert.Contains("PageTitleText.FontSize = width < 640 ? 24 : 30", page);
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
