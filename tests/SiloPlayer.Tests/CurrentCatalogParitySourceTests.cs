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
        Assert.Contains("x:Name=\"ResultFiltersSheet\"", xaml);
        Assert.Contains("x:Name=\"SearchLoadingRepeater\"", xaml);
        Assert.Contains("ViewChanged=\"ResultsScroll_ViewChanged\"", xaml);
        Assert.Contains("Discover · Outside your library", xaml);
        Assert.Contains("UpdatePeopleSection", code);
        Assert.Contains("PeopleSection.Visibility = Visibility.Collapsed", code);
        Assert.Contains("source: \"query\"", viewModel);
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
        Assert.Contains("ReferenceEquals(_searchCts, searchCts)", viewModel);
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
