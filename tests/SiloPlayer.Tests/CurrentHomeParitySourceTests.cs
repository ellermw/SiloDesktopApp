namespace SiloPlayer.Tests;

public sealed class CurrentHomeParitySourceTests
{
    [Fact]
    public void HomeUsesCurrentHeroTastePromptAndEmptyState()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml.cs");

        Assert.Contains("x:Name=\"HeroLoadingSkeleton\"", xaml);
        Assert.Contains("x:Name=\"HeroErrorPanel\"", xaml);
        Assert.Contains("Personalize your home", xaml);
        Assert.Contains("Pick a few titles you love and we'll tailor your recommendations.", xaml);
        Assert.Contains("Customize Home Screen", xaml);
        Assert.DoesNotContain("Watch Tonight", xaml);
        Assert.Contains("TasteSeedBannerDismissedProfileIds", code);
        Assert.Contains("RetrySectionAsync", code);
    }

    [Fact]
    public void SectionRowsUseCurrentBrowsePinRetryAndDismissContracts()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml.cs");
        var home = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml.cs");
        var library = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("FontSize=\"20\"", xaml);
        Assert.Contains("x:Name=\"PinSectionBtn\"", xaml);
        Assert.Contains("This section could not be loaded right now.", xaml);
        Assert.DoesNotContain("RefreshSectionBtn", xaml);
        Assert.Contains("\"custom_filter\"", code);
        Assert.Contains("\"random\"", code);
        Assert.DoesNotContain("\"popular\"", code);
        Assert.Contains("OnRetry", code);
        Assert.Contains("Scope: \"home\"", home);
        Assert.Contains("Scope: \"library\"", library);
        Assert.Contains("LibraryId = libraryId", library);
    }

    [Fact]
    public void SectionCatalogNavigationPreservesStoredRecipeScope()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "CatalogPage.xaml.cs");
        var shell = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("section_id=", api);
        Assert.Contains("&scope=", api);
        Assert.Contains("_source == \"section\"", page);
        Assert.Contains("FilterPanel.Visibility = Visibility.Collapsed", page);
        Assert.Contains("SectionId: pinTag.PinId", shell);
        Assert.Contains("IsSidebarPin", shell);
    }

    [Fact]
    public void FailedHomeSectionsRemainRetryableInsteadOfDisappearing()
    {
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Home", "HomeSectionsResponse.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "HomeViewModel.cs");

        Assert.Contains("LoadFailed", model);
        Assert.Contains("CloneSection(section, loadFailed: true)", viewModel);
        Assert.Contains("public async Task RetrySectionAsync", viewModel);
    }

    [Fact]
    public void ContinueWatchingCardsUseCurrentWideMenuOverlayAndMetadataLayout()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "LandscapeCard.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs");
        var menu = ReadRepoFile("src", "SiloPlayer", "Controls", "MediaItemMenu.cs");

        Assert.Contains("Width=\"315\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"More actions\"", xaml);
        Assert.Contains("x:Name=\"OverlayTopLeft\"", xaml);
        Assert.Contains("x:Name=\"TimeLeftText\"", xaml);
        Assert.Contains("item.BackdropUrl", code);
        Assert.Contains("PosterCard.BuildBadge", code);
        Assert.Contains("item.ProgressUpdatedAt", menu);
        Assert.Contains("MediaSurfaceChangeKind.HomeDismissed", menu);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = FindRepositoryRoot();
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
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
