namespace SiloPlayer.Tests;

public sealed class CurrentLibraryParitySourceTests
{
    [Fact]
    public void AdvancedFilterEventsStaySuppressedThroughoutXamlConstruction()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var initialize = source.IndexOf("this.InitializeComponent();", StringComparison.Ordinal);
        var enableEvents = source.IndexOf("_suppressFilterEvents = false;", initialize, StringComparison.Ordinal);

        Assert.Contains("private bool _suppressFilterEvents = true;", source, StringComparison.Ordinal);
        Assert.True(initialize >= 0 && enableEvents > initialize);
        Assert.DoesNotContain("private bool _suppressFilterEvents;", source, StringComparison.Ordinal);
    }

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
        Assert.Contains("string.Equals(item.Type, \"audiobook\"", card);
        Assert.Contains("SetLayout(Width, itemPosterHeight, Height)", card);
        Assert.Contains("MoreButton_Click", card);
        Assert.Contains("GetOrderedDefinitions()", card);
        Assert.Contains("PosterCard.BuildBadge", card);
        Assert.Contains("case \"date_viewed\"", display);
        Assert.Contains("case \"narrator\"", display);
        Assert.Contains("\"series\" or \"tv\" when browseType == \"episode\" => \"episode\"", page);
        Assert.Contains("scope is \"ebook\" or \"manga\" ? \"Date Read\" : \"Date Viewed\"", page);
        Assert.Contains("scope is \"ebook\" or \"manga\" ? \"Reads\" : \"Plays\"", page);
    }

    [Fact]
    public void FiltersSupportCurrentGuidedPersonalAndTechnicalRules()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "LibraryViewModel.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");

        Assert.Contains("x:Name=\"MinimumRatingBox\"", xaml);
        Assert.Contains("x:Name=\"DecadeComboBox\"", xaml);
        Assert.Contains("Content=\"2030s\" Tag=\"2030\"", xaml);
        Assert.Contains("Text=\"Year From\"", xaml);
        Assert.Contains("Text=\"Year To\"", xaml);
        Assert.Contains("x:Name=\"WatchStatusComboBox\"", xaml);
        Assert.Contains("x:Name=\"DolbyVisionToggle\"", xaml);
        Assert.Contains("Content=\"DOVI\"", xaml);
        Assert.Contains("Content=\"Movies &amp; Series\" Tag=\"video\"", xaml);
        Assert.Contains("Content=\"Audiobooks\" Tag=\"audiobook\"", xaml);
        Assert.Contains("Content=\"Ebooks\" Tag=\"ebook\"", xaml);
        Assert.Contains("Content=\"Manga\" Tag=\"manga\"", xaml);
        Assert.Contains("x:Name=\"GenreMultiSelectButton\"", xaml);
        Assert.Contains("Items must match all selected genres.", xaml);
        Assert.Contains("x:Name=\"OriginalLanguageMultiSelectButton\"", xaml);
        Assert.Contains("Add(\"dolby_vision\", \"is\", true)", viewModel);
        Assert.Contains("Add(\"watched\", \"is\", false)", viewModel);
        Assert.Contains("public IReadOnlyList<string> SelectedGenres", viewModel);
        Assert.Contains("public IReadOnlyList<string> SelectedOriginalLanguages", viewModel);
        Assert.Contains("Match = \"any\"", viewModel);
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
        Assert.Contains("BuildAudiobookGroupSkeletons", page);
        Assert.Contains("viewportWidth >= 1280 ? 6", page);
        Assert.Contains("viewportWidth >= 1280 ? 3", page);
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
        Assert.Contains("Padding=\"40,56,40,56\"", xaml);
        Assert.Contains("CornerRadius=\"32\"", xaml);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml);
        Assert.Contains("x:Name=\"CollectionsSkeletonHost\"", xaml);
        Assert.Contains("x:Name=\"CollectionsHeader\"", xaml);
        Assert.Contains("CollectionsHeader.Visibility = Visibility.Collapsed", page);
        Assert.Contains("CollectionsHeader.Visibility = Visibility.Visible", page);
        Assert.Contains("Math.Clamp(width * 0.04, 32, 48)", page);
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
        Assert.Contains("IsTall=\"True\"", xaml);
        Assert.Contains("GetLibraryLayoutAsync", page);
        Assert.Contains("RecommendedLoading.Visibility = Visibility.Collapsed", page);
        Assert.DoesNotContain("No recommendations available yet.", page);
        Assert.Contains("new SemaphoreSlim(4, 4)", page);
        Assert.Contains("GetLibrarySectionItemsAsync", page);
        Assert.Contains("RetryLibrarySectionAsync", page);
        Assert.Contains("LoadPinnedCollectionRowsAsync", page);
        Assert.Contains("Grid.SetRow(RecommendedPanel, overlay ? 0 : 3)", page);
        Assert.Contains("LibraryHeaderGlassThreshold = 160", page);
        Assert.Contains("ApplyRecommendedHeaderForCurrentScroll();", page);
        Assert.Contains("RecommendedPanel.VerticalOffset > LibraryHeaderGlassThreshold", page);
        Assert.Contains("ApplyHeaderPalette", page);
        Assert.Contains("/layout", api);
        Assert.Contains("HomeSectionItemsResponse", api);
        Assert.Contains("GetSidebarPins", shell);
        Assert.Contains("Math.Clamp(e.NewSize.Height * tallHeroRatio, 420, 760)", page);
        var hero = ReadRepoFile("src", "SiloPlayer", "Controls", "HeroCarousel.xaml.cs");
        Assert.Contains("root.ActualWidth >= 1024 ? 0.72 : 0.60", hero);
        Assert.Contains("Math.Clamp(root.ActualHeight * heightRatio, 420, 760)", hero);
    }

    [Fact]
    public void LibraryRouteRetainsItsPopulatedWindowButResetsForAnotherLibrary()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "LibraryViewModel.cs");

        Assert.Contains("NavigationCacheMode = NavigationCacheMode.Required", page);
        Assert.Contains("var sameLibrary = _activeLibraryId == library.Id", page);
        Assert.Contains("if (!sameLibrary)", page);
        Assert.Contains("ViewModel.CancelCatalogLoads()", page);
        Assert.Contains("ViewModel.SuspendCatalogLoads()", page);
        Assert.DoesNotContain("DetachViewModelEvents();\n        ClearVirtualCards();", page.Replace("\r\n", "\n"));
        Assert.Contains("public void SuspendCatalogLoads()", viewModel);
        Assert.DoesNotContain("public void SuspendCatalogLoads()\n    {\n        CancelCatalogLoads();", viewModel.Replace("\r\n", "\n"));
    }

    [Fact]
    public void CompletedRecommendationAndCollectionSurfacesRemainSmoothAcrossTabChanges()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("if (!_recommendationsLoading)", page);
        Assert.Contains("CancelIncompleteRecommendedContent();", page);
        Assert.Contains("GetLibraryLayoutAsync(libraryId, cancellationToken)", page);
        Assert.Contains("GetLibrarySectionItemsAsync(libraryId, layout.Id, cancellationToken)", page);
        Assert.Contains("if (tag == _currentTab)", page);
        Assert.Contains("_collectionsResizeTimer.Start();", page);
        Assert.DoesNotContain("if (tag == \"Library\")\n            ReleaseRecommendedContent();", page.Replace("\r\n", "\n"));
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
