namespace SiloPlayer.Tests;

public sealed class CurrentLibraryParitySourceTests
{
    [Fact]
    public void AdvancedFilterEventsStaySuppressedThroughoutXamlConstruction()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var initialize = source.IndexOf("this.InitializeComponent();", StringComparison.Ordinal);
        Assert.True(initialize >= 0, "LibraryPage.xaml.cs no longer calls InitializeComponent().");
        var enableEvents = source.IndexOf("_suppressFilterEvents = false;", initialize, StringComparison.Ordinal);

        Assert.Contains("private bool _suppressFilterEvents = true;", source, StringComparison.Ordinal);
        Assert.True(enableEvents > initialize);
        Assert.DoesNotContain("private bool _suppressFilterEvents;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderAndToolbarFollowCurrentLibraryShell()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");

        Assert.Contains("x:Name=\"HeaderGrid\" Grid.Row=\"0\" Height=\"81\"", xaml);
        Assert.Contains("x:Name=\"HeaderDivider\"", xaml);
        Assert.Contains("SizeChanged=\"Page_SizeChanged\"", xaml);
        Assert.Contains("x:Name=\"LibraryTitle\"", xaml);
        Assert.Contains("Visibility=\"Collapsed\"", xaml);
        Assert.Contains("x:Name=\"SortComboBox\"", xaml);
        Assert.Contains("x:Name=\"OrderComboBox\"", xaml);
        Assert.Contains("x:Name=\"OpenFiltersButton\"", xaml);
        Assert.Contains("x:Name=\"FiltersSheet\"", xaml);
        Assert.Contains("x:Name=\"GuidedFiltersScroll\"", xaml);
        Assert.DoesNotContain("AVAILABLE NOW", xaml);
    }

    [Fact]
    public void LibraryCardsUseResponsiveGridOverlaysMenusAndCurrentSortMetadata()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var card = ReadRepoFile("src", "SiloPlayer", "Controls", "LibraryGridCard.cs");
        var badges = ReadRepoFile("src", "SiloPlayer", "Controls", "CardBadges.cs");
        var display = ReadRepoFile("src", "SiloPlayer.Core", "Services", "MediaItemDisplayText.cs");
        var customization = ReadRepoFile("src", "SiloPlayer", "Services", "UICustomizationService.cs");

        Assert.Contains("_uiCustomizationService.CardPresentation.PosterSize switch", page);
        Assert.Contains("var breakpointWidth = WebUiViewport.Width(this, ActualWidth)", page);
        Assert.Contains("\"compact\" => breakpointWidth switch", page);
        Assert.Contains("\"large\" => breakpointWidth switch", page);
        Assert.Contains("_ => contentWidth >= 1280 ? 8", customization);
        Assert.Contains("itemWidth * 1.5", page);
        Assert.Contains("isAudiobook ? itemWidth", page);
        Assert.Contains("string.Equals(item.Type, \"audiobook\"", card);
        Assert.Contains("SetLayout(Width, itemPosterHeight, Height)", card);
        Assert.Contains("MoreButton_Click", card);
        Assert.Contains("CardBadgeHosts.For(item, service", card);
        Assert.Contains("GetOrderedDefinitions()", badges);
        Assert.Contains("OverlayBadgeRenderer.Build", badges);
        Assert.Contains("case \"date_viewed\"", display);
        Assert.Contains("case \"narrator\"", display);
        Assert.Contains("\"series\" or \"tv\" when browseType == \"episode\" => \"episode\"", page);
        Assert.Contains("web.common.sort.dateRead", page);
        Assert.Contains("web.common.sort.reads", page);
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
        Assert.Contains("JsonSerializer.Serialize(groups, BrowseV2.Json)", api);
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
        Assert.Contains("new QueryFilterEditor", page);
        Assert.Contains("_sharedFilterEditor?.IsValid == false", page);
        Assert.Contains("GuidedQuerySupport.CanEdit", page);
        Assert.Contains("ObservableCollection<EditableQueryGroup> AdvancedGroups", viewModel);
        Assert.Contains("BuildLibraryRuleDraft", page);
        Assert.Contains("AddAdvancedGroup_Click", page);
        Assert.Contains("type: SelectedType", viewModel);
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
        Assert.Contains("viewportWidth >= 1280 ? 3", page);
        Assert.Contains("GetAudiobookGroupsAsync", api);
        Assert.Contains("NowListeningHero", ReadRepoFile("src", "SiloPlayer", "Controls", "NowListeningHero.xaml"));
        Assert.Contains("AudiobookSquareCard", ReadRepoFile("src", "SiloPlayer", "Controls", "AudiobookSquareCard.xaml"));
    }

    [Fact]
    public void AudiobookSeriesUseOneSquareCoverWithARunBarInTheBooksGridColumns()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var series = Slice(page, "private FrameworkElement CreateAudiobookSeriesCard(", "private void AudiobookGroupsPanel_SizeChanged(");
        var width = Slice(page, "private double GetAudiobookSeriesCardWidth()", "private void BuildAudiobookGroupSkeletons(");
        var cover = Slice(page, "private async Task LoadAudiobookSeriesCoverAsync(", "private static string AudiobookGroupPosterKey(");
        var bar = Slice(page, "private static FrameworkElement CreateAudiobookSeriesRunBar(", "private async Task SelectAudiobookGroupAsync(");

        // Official #4397: one square cover, a decorative run bar, wrapping listening text.
        Assert.Contains("if (large) return CreateAudiobookSeriesCard(group, cardWidth, ct);", page);
        Assert.Contains("CreateAudiobookSeriesCover(group, cardWidth, ct)", series);
        Assert.Contains("CreateAudiobookSeriesRunBar(AudiobookSeriesPresentation.Run(group))", series);
        Assert.Contains("TextWrapping = TextWrapping.WrapWholeWords", series);
        Assert.Contains("AudiobookSeriesPresentation.Listening(group)", series);
        Assert.DoesNotContain("Take(3)", series);
        Assert.DoesNotContain("index * 4", page);

        // A failed URL falls through to the next; none loading shows initials.
        Assert.Contains("AudiobookSeriesPresentation.FirstUntriedCover(group.PosterUrls, failed)", cover);
        Assert.Contains("image.ImageFailed +=", cover);
        Assert.Contains("failed.Add(", cover);
        Assert.Contains("GroupInitials(group.Name)", cover);
        Assert.Contains("GetImageDiskPathAsync(AudiobookGroupPosterKey(group.Name, url), \"poster\", url, httpClient, ct)", cover);

        // The bar is decorative for UI Automation; the stats text carries the meaning.
        Assert.Contains("AccessibilityView.Raw", bar);
        Assert.Contains("run.IsContinuous ? run.ContinuousFill : run.SegmentFill(index)", bar);

        // Same columns as the Books grid for the profile's poster size.
        Assert.Contains("UICustomizationService.PosterColumnCount(posterSize, WebUiViewport.Width(this, ActualWidth))", width);
        Assert.Contains("LibraryPosterSizing.GetColumnCount(contentWidth, posterWidth)", width);
        Assert.Contains("posterSize == \"large\" ? 16d : 12d", width);
        Assert.Contains("SizeChanged=\"AudiobookGroupsPanel_SizeChanged\"", xaml);
        Assert.Contains("ResizeAudiobookSeriesCards();", page);

        // Square loading skeletons.
        var skeletons = Slice(page, "private void BuildAudiobookGroupSkeletons(", "private FrameworkElement CreateAudiobookGroupCover(");
        Assert.Contains("Height = cardWidth,", skeletons);
    }

    [Fact]
    public void AudiobookGroupActivationReloadsMissingStaleOrInterruptedQueries()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var tabs = Slice(page, "private async void Tab_Click(", "private async Task EnsureLibraryCatalogLoadedAsync(");
        // Both a tab change and a deliberate retry must activate groups, not the books catalog.
        Assert.Equal(2, tabs.Split("await EnsureAudiobookGroupsLoadedAsync();").Length - 1);
        var activation = Slice(page, "private async Task EnsureAudiobookGroupsLoadedAsync(", "private async Task LoadAudiobookGroupsAsync(");
        Assert.Contains("_audiobookGroupsKey == CurrentAudiobookGroupsKey()", activation);
        Assert.Contains("if (_audiobookGroupsLoadCompleted)", activation);
        Assert.Contains("if (_isLoadingAudiobookGroups) return;", activation);
        Assert.Contains("await LoadAudiobookGroupsAsync(reset: true);", activation);

        var key = Slice(page, "private AudiobookGroupsKey CurrentAudiobookGroupsKey()", "private void ResetAudiobookGroups(");
        Assert.Contains("ViewModel.Library?.Id", key);
        Assert.Contains("_currentAudiobookAxis", key);
        Assert.Contains("AudiobookGroupSearchBox.Text", key);
        Assert.Contains("AudiobookGroupSortComboBox.SelectedItem", key);
        var reset = Slice(page, "private void ResetAudiobookGroups(", "private async Task EnsureAudiobookGroupsLoadedAsync(");
        Assert.Contains("AudiobookGroupsHost.Children.Clear();", reset);
        Assert.Contains("_audiobookGroupsOffset = 0;", reset);
        Assert.Contains("_audiobookGroupsTotal = 0;", reset);
        Assert.Contains("_audiobookGroupsHasMore = false;", reset);
        Assert.Contains("_audiobookGroupsLoadCompleted = false;", reset);
        Assert.Contains("ResetRecommendedContent();\n                ResetAudiobookGroups();", page.Replace("\r\n", "\n"));

        var departure = Slice(page, "protected override void OnNavigatedFrom(", "private void UICustomization_Changed(");
        Assert.Contains("if (_isLoadingAudiobookGroups) _audiobookGroupsLoadCompleted = false;", departure);
        var covers = Slice(page, "private void ResumeAudiobookSeriesCovers(", "private FrameworkElement CreateAudiobookSeriesCover(");
        Assert.Contains("if (!_audiobookGroupsLoadCompleted || _audiobookGroupsKey != CurrentAudiobookGroupsKey()) return;", covers);
        var load = Slice(page, "private async Task LoadAudiobookGroupsAsync(", "private FrameworkElement CreateAudiobookGroupCard(");
        Assert.Contains("reset |= _audiobookGroupsKey != key || !_audiobookGroupsLoadCompleted;", load);
        Assert.Contains("key != CurrentAudiobookGroupsKey()", load);
        Assert.Contains("_audiobookGroupsLoadCompleted = true;", load);
    }

    [Fact]
    public void AudiobookGroupPaginationFailureKeepsLoadedPagesAndRetriesTheSameOffset()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var scroll = Slice(page, "private async void AudiobookGroupsPanel_ViewChanged(", "private AudiobookGroupsKey CurrentAudiobookGroupsKey()");
        Assert.Contains("await LoadAudiobookGroupsAsync(reset: false);", scroll);
        var load = Slice(page, "private async Task LoadAudiobookGroupsAsync(", "private FrameworkElement CreateAudiobookGroupCard(");
        var beforeRequest = Slice(load, "var key = CurrentAudiobookGroupsKey();", "var response = await");

        // Only reset invalidates completion. A failed append must not make the
        // next ViewChanged promote its retry to a reset and discard loaded cards.
        Assert.Contains("reset |= _audiobookGroupsKey != key || !_audiobookGroupsLoadCompleted;", beforeRequest);
        Assert.Contains("if (reset)\n        {\n            ResetAudiobookGroups();", beforeRequest.Replace("\r\n", "\n"));
        Assert.DoesNotContain("_audiobookGroupsLoadCompleted = false;", load);
        // Reject assignments, not the equality comparison used by the count text.
        Assert.DoesNotMatch(@"\b_audiobookGroupsOffset\s*=(?!=)", load);
        Assert.Contains("offset: _audiobookGroupsOffset,", load);
        Assert.Contains("_audiobookGroupsOffset += response.Groups.Count;", load);

        var failure = load[load.IndexOf("catch (OperationCanceledException)", StringComparison.Ordinal)..];
        // HttpClient timeouts with an uncancelled caller token show the error
        // rather than being silently swallowed as navigation cancellation.
        Assert.Contains("catch (OperationCanceledException) when (ct.IsCancellationRequested)", failure);
        Assert.Contains("web.audiobooks.groups.loadFailed", failure);
        Assert.Contains("if (reset) AudiobookGroupsHost.Children.Clear();", failure);
        Assert.Equal(1, failure.Split("AudiobookGroupsHost.Children.Clear();").Length - 1);
        Assert.DoesNotContain("_audiobookGroupsOffset", failure);
        Assert.DoesNotContain("_audiobookGroupsHasMore =", failure);
        Assert.Contains("_isLoadingAudiobookGroups = false;", failure);
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"Missing '{start}'.");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"Missing '{end}' after '{start}'.");
        return source[from..to];
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
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs")
            + ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.PinTitles.cs");
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
