namespace SiloPlayer.Tests;

public sealed class CurrentLibraryParitySourceTests
{
    [Fact]
    public void HeaderAndToolbarFollowCurrentLibraryShell()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");

        Assert.Contains("x:Name=\"HeaderGrid\" Grid.Row=\"0\" Height=\"70\"", xaml);
        Assert.Contains("x:Name=\"HeaderDivider\"", xaml);
        Assert.Contains("SizeChanged=\"Page_SizeChanged\"", xaml);
        Assert.Contains("x:Name=\"LibraryTitle\"", xaml);
        Assert.Contains("Visibility=\"Collapsed\"", xaml);
        Assert.Contains("x:Name=\"SortComboBox\"", xaml);
        Assert.Contains("x:Name=\"OrderComboBox\"", xaml);
        Assert.Contains("x:Name=\"OpenFiltersButton\"", xaml);
        Assert.Contains("x:Name=\"FiltersSheet\"", xaml);
        Assert.Contains("Refine your catalog results", xaml);
        Assert.DoesNotContain("AVAILABLE NOW", xaml);
    }

    [Fact]
    public void LibraryCardsUseResponsiveGridOverlaysMenusAndCurrentSortMetadata()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var card = ReadRepoFile("src", "SiloPlayer", "Controls", "LibraryGridCard.cs");
        var display = ReadRepoFile("src", "SiloPlayer.Core", "Services", "MediaItemDisplayText.cs");

        Assert.Contains(">= 1000 => 8", page);
        Assert.Contains("itemWidth * 1.5", page);
        Assert.Contains("isAudiobook ? itemWidth", page);
        Assert.Contains("MoreButton_Click", card);
        Assert.Contains("OverlayRegistry.All", card);
        Assert.Contains("PosterCard.BuildBadge", card);
        Assert.Contains("case \"date_viewed\"", display);
        Assert.Contains("case \"narrator\"", display);
    }

    [Fact]
    public void FiltersSupportCurrentGuidedPersonalAndTechnicalRules()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "LibraryViewModel.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");

        Assert.Contains("x:Name=\"MinimumRatingBox\"", xaml);
        Assert.Contains("x:Name=\"WatchStatusComboBox\"", xaml);
        Assert.Contains("x:Name=\"DolbyVisionToggle\"", xaml);
        Assert.Contains("Add(\"dolby_vision\", \"is\", true)", viewModel);
        Assert.Contains("Add(\"watched\", \"is\", false)", viewModel);
        Assert.Contains("extraRules", api);
        Assert.Contains("FormatRuleValue", api);
    }

    [Fact]
    public void AdvancedFiltersExposeEditableTypedRulesAndMatchMode()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "LibraryViewModel.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");

        Assert.Contains("x:Name=\"AdvancedFilterModeButton\"", xaml);
        Assert.Contains("x:Name=\"AdvancedRulesHost\"", xaml);
        Assert.Contains("x:Name=\"AdvancedMatchComboBox\"", xaml);
        Assert.Contains("BuildAdvancedRuleRow", page);
        Assert.Contains("CoerceAdvancedValue", page);
        Assert.Contains("GetAdvancedOperators", page);
        Assert.Contains("BuildAdvancedValueEditor", page);
        Assert.Contains("rule.Op == \"between\"", page);
        Assert.Contains("IsAdvancedBooleanField", page);
        Assert.Contains("ObservableCollection<EditableQueryGroup> AdvancedGroups", viewModel);
        Assert.Contains("BuildAdvancedGroupCard", page);
        Assert.Contains("AddAdvancedGroup_Click", page);
        Assert.Contains("UseAdvancedRules ? null : SelectedType", viewModel);
        Assert.Contains("queryGroups: BuildAdvancedGroups()", viewModel);
        Assert.Contains("extraRulesMatch", api);
        Assert.Contains("queryGroupsMatch", api);
    }

    [Fact]
    public void AudiobooksExposeCurrentAxesGroupedBrowseAndSquareCovers()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");

        Assert.Contains("x:Name=\"AudiobookAxisPanel\"", xaml);
        Assert.Contains("Content=\"Books\"", xaml);
        Assert.Contains("Content=\"Narrators\"", xaml);
        Assert.Contains("x:Name=\"AudiobookGroupsPanel\"", xaml);
        Assert.Contains("AudiobookGroupsPanel_ViewChanged", page);
        Assert.Contains("SelectAudiobookGroupAsync", page);
        Assert.Contains("GetAudiobookGroupsAsync", api);
        Assert.Contains("NowListeningHero", ReadRepoFile("src", "SiloPlayer", "Controls", "NowListeningHero.xaml"));
        Assert.Contains("AudiobookSquareCard", ReadRepoFile("src", "SiloPlayer", "Controls", "AudiobookSquareCard.xaml"));
    }

    [Fact]
    public void CollectionsUseCurrentGroupedResponsivePosterContractAndSkeletons()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("Text=\"Collections\"", xaml);
        Assert.Contains("FontSize=\"48\"", xaml);
        Assert.Contains("MaxWidth=\"1320\"", xaml);
        Assert.Contains("x:Name=\"CollectionsSkeletonHost\"", xaml);
        Assert.Contains("var posterHeight = cardWidth * 1.5", page);
        Assert.Contains("User collection", page);
        Assert.Contains("ToggleSidebarPinAsync", page);
    }

    [Fact]
    public void RecommendedUsesLayoutFirstBoundedSectionLoadingRetriesAndPinnedCollections()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");
        var shell = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"RecommendedHeroSkeleton\"", xaml);
        Assert.Contains("GetLibraryLayoutAsync", page);
        Assert.Contains("new SemaphoreSlim(4, 4)", page);
        Assert.Contains("GetLibrarySectionItemsAsync", page);
        Assert.Contains("RetryLibrarySectionAsync", page);
        Assert.Contains("LoadPinnedCollectionRowsAsync", page);
        Assert.Contains("Grid.SetRow(RecommendedPanel, overlay ? 0 : 3)", page);
        Assert.Contains("ApplyHeaderPalette", page);
        Assert.Contains("/layout", api);
        Assert.Contains("HomeSectionItemsResponse", api);
        Assert.Contains("GetSidebarPins", shell);
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
