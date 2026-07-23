namespace SiloPlayer.Tests;

public sealed class CurrentCatalogParitySourceTests
{
    [Fact]
    public void SearchSurfaceUsesCurrentEmptyResultsScopeToolbarAndRequestSections()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SearchViewModel.cs");

        Assert.Contains("Find films, series, performances", xaml);
        Assert.Contains("Content=\"Media\" Tag=\"video\"", xaml);
        Assert.Contains("x:Name=\"ResultTypeCombo\"", xaml);
        Assert.Contains("x:Name=\"ResultSortCombo\"", xaml);
        Assert.Contains("x:Name=\"ResultCountPanel\"", xaml);
        Assert.Contains("ResultCountPanel.Visibility", code);
        Assert.Contains("x:Name=\"ResultFiltersSheet\"", xaml);
        Assert.Contains("x:Name=\"SearchLoadingRepeater\"", xaml);
        Assert.Contains("ViewChanged=\"ResultsScroll_ViewChanged\"", xaml);
        Assert.Contains("x:Name=\"SearchScrollToTopButton\"", xaml);
        Assert.Contains("ResultsScroll.ChangeView(null, 0, null)", code);
        Assert.Contains("Discover · Outside your library", xaml);
        Assert.Contains("UpdatePeopleSection", code);
        Assert.Contains("PeopleSection.Visibility = Visibility.Collapsed", code);
        Assert.Contains("source: \"query\"", viewModel);
        Assert.Contains("type: MediaType", viewModel);
        Assert.Contains("MediaType = MediaScope == \"all\" ? null : MediaScope", viewModel);
        Assert.DoesNotContain("scope: MediaType == null && MediaScope != \"all\" ? MediaScope : null", viewModel);
        Assert.Contains("private string? _mediaType;", viewModel);
        Assert.Contains("var typeSelection = ViewModel.MediaType", code);
        Assert.DoesNotContain("private string? _mediaType = \"video\"", viewModel);
        Assert.Contains("limit: 60", viewModel);
        Assert.Contains("LoadMoreAsync", viewModel);
        Assert.DoesNotContain("Task.Delay(300", viewModel);
        Assert.Contains("TimeSpan.FromMilliseconds(100)", code);
        Assert.Contains("x:Name=\"ResultsToolbar\"", xaml);
        Assert.Contains("x:Name=\"SearchLoadingGridLayout\"", xaml);
        Assert.Contains("ElementPrepared=\"SearchLoadingRepeater_ElementPrepared\"", xaml);
        Assert.Contains("await EnsureInitializedAsync();", code);
        Assert.Contains("ViewModel.CancelPendingSearch();", code);
        Assert.Contains("querySnapshot", code);
        Assert.Contains("SearchLoadingGridLayout.MinItemWidth = _catalogCardWidth", code);
        Assert.Contains("public void CancelPendingSearch()", viewModel);
        Assert.Contains("Results.Clear();", viewModel);
        Assert.Contains("IsCurrentSearchOwner(searchCts, querySnapshot)", viewModel);
    }

    [Fact]
    public void PersonalCatalogSurfacesUseCurrentHeaderToolbarFilterSheetAndLockedSectionState()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "CatalogPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "CatalogPage.xaml.cs");

        Assert.Contains("FontSize=\"56\"", xaml);
        Assert.Contains("x:Name=\"FilterPanel\"", xaml);
        Assert.Contains("x:Name=\"FilterCountBadge\"", xaml);
        Assert.Contains("x:Name=\"FiltersSheet\"", xaml);
        Assert.Contains("Filters are locked to this source.", xaml);
        Assert.Contains("Watch History", xaml);
        Assert.Contains("MaximumRowsOrColumns=\"8\"", xaml);
        Assert.Contains("x:Name=\"CatalogScrollToTopButton\"", xaml);
        Assert.Contains("CatalogScrollViewer.ChangeView(null, 0, null)", code);
        Assert.Contains("Current WebUI ItemGrid does not expose a manual \"Load more\"", code);
        Assert.DoesNotContain("LoadMoreButton.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;", code);
        Assert.Contains("LockedFiltersPanel.Visibility = Visibility.Visible", code);
        Assert.Contains("UpdateFilterCount", code);
    }

    [Fact]
    public void CollectionCatalogShowsAndIndividuallyClearsAppliedFilterBadges()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml.cs");

        Assert.Contains("x:Name=\"ActiveFiltersPanel\"", xaml);
        Assert.Contains("BuildActiveFilterBadges", code);
        Assert.Contains("ActiveFilterBadge_Click", code);
        Assert.Contains("Clear {badge.Label}", code);
        Assert.Contains("await LoadFirstPageAsync();", code);
    }

    [Fact]
    public void LibraryMultiSelectFiltersUseSearchableBoundedFlyout()
    {
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("private const int MaxVisibleMultiSelectOptions = 80;", code);
        Assert.Contains("PlaceholderText = \"Filter options...\"", code);
        Assert.Contains("matches.Take(MaxVisibleMultiSelectOptions)", code);
        Assert.Contains("Type to narrow the list.", code);
        Assert.DoesNotContain("var flyout = new MenuFlyout();\r\n        foreach (var value in values.Where", code);
    }

    [Fact]
    public void LibraryCatalogLoadedFlagIsOnlySetAfterSuccessfulLoadAndCurrentTabCanRetry()
    {
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.DoesNotContain("if (_libraryCatalogLoaded) return;\r\n        _libraryCatalogLoaded = true;", code);
        Assert.Contains("_libraryCatalogLoaded = _isNavigated && string.IsNullOrWhiteSpace(ViewModel.ErrorMessage);", code);
        Assert.Contains("if (tag == \"Library\" && !_libraryCatalogLoaded)", code);
        Assert.Contains("await EnsureLibraryCatalogLoadedAsync();", code);
        Assert.Contains("await FillViewportAsync();", code);
    }

    [Fact]
    public void LibraryCollectionsRenderGroupedSectionsWithoutStaleDynamicInsertion()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "LibraryViewModel.cs");

        Assert.Contains("x:Name=\"CollectionSectionsHost\"", xaml);
        Assert.Contains("CollectionSectionsHost.Children.Clear();", code);
        Assert.Contains("CollectionSectionsHost.Children.Add(BuildCollectionSection(section));", code);
        Assert.Contains("BuildCollectionSections(response)", viewModel);
        Assert.Contains("response.Groups.Where(g => g.Collections.Count > 0)", viewModel);
        Assert.Contains("response.Ungrouped?.Collections.Count > 0", viewModel);
        Assert.Contains("OrderBy(s => s.SortOrder)", viewModel);
        Assert.DoesNotContain("CollectionsRepeater", xaml);
        Assert.DoesNotContain("RemoveDynamicCollectionPanels", code);
        Assert.DoesNotContain("Tag = \"CollectionGrid\"", code);
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
