namespace SiloPlayer.Tests;

public sealed class SearchRuntimeRegressionTests
{
    [Fact]
    public void TypingUsesOnePersistentNativeInputAndDoesNotWaitForFilterWarmup()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml.cs");
        var handler = Slice(code, "private void SearchBox_TextChanged", "private async void SearchBox_KeyDown");

        Assert.DoesNotContain("SearchBoxClearButton", xaml);
        Assert.DoesNotContain("ResultsSearchBoxClearButton", xaml);
        Assert.Equal(1, xaml.Split("x:Name=\"SearchBox\"").Length - 1);
        Assert.DoesNotContain("x:Name=\"ResultsSearchBox\"", xaml);
        Assert.DoesNotContain("EnsureInitializedAsync", handler);
        Assert.Contains("TimeSpan.FromMilliseconds(100)", handler);
        Assert.Contains("ShowResultsShellForCurrentQuery();", handler);
        Assert.Contains("RestoreSearchFocus(focusState);", handler);
        Assert.True(handler.IndexOf("RestoreSearchFocus(focusState);", StringComparison.Ordinal) <
                    handler.IndexOf("await ViewModel.SearchCommand.ExecuteAsync", StringComparison.Ordinal));

        var positionHelper = Slice(code, "private void PositionSearchSurface", "private async void SearchBox_KeyDown");
        Assert.Contains("? EmptySearchHost", positionHelper);
        Assert.Contains(": ResultsSearchHost", positionHelper);
        Assert.Contains("TransformToVisual(SearchRoot)", positionHelper);
        Assert.DoesNotContain("Children.Remove(SearchSurface)", code);
        Assert.DoesNotContain("Children.Add(SearchSurface)", code);
        Assert.Contains("SearchBox.Focus", positionHelper);
        Assert.Contains("SearchBox.SelectionStart = SearchBox.Text.Length", positionHelper);
    }

    [Fact]
    public void SearchChromeUsesTheWebUiDimensionsWithoutOversizedCornerRadii()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml.cs");

        Assert.True(xaml.Split("Height=\"56\"").Length - 1 >= 4);
        Assert.True(xaml.Split("VerticalContentAlignment=\"Center\"").Length - 1 >= 1);
        Assert.Contains("Padding=\"48,16,12,16\"", xaml);
        Assert.DoesNotContain("Padding=\"48,0,12,0\"", xaml);
        Assert.True(xaml.Split("TextWrapping=\"NoWrap\"").Length - 1 >= 1);
        Assert.Contains("AutomationProperties.Name=\"Search scope\"", xaml);
        Assert.Contains("Height=\"40\"", xaml);
        Assert.Contains("CornerRadius=\"20\"", xaml);
        Assert.True(xaml.Split("Height=\"32\"").Length - 1 >= 3);
        Assert.True(xaml.Split("CornerRadius=\"16\"").Length - 1 >= 3);
        Assert.DoesNotContain("CornerRadius=\"999\"", xaml);
        Assert.DoesNotContain("new CornerRadius(999)", code);
        Assert.Contains("new CornerRadius(16)", code);
    }

    [Fact]
    public void PrimarySearchDoesNotCompeteWithAnUnscopedFacetScanOrForceDateSorting()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SearchViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml.cs");
        var initialize = Slice(page, "private async Task InitializeAsync()", "private void UpdateResultsState()");
        var fetch = Slice(viewModel, "private Task<CatalogResponse> FetchCatalogPageAsync", "private async Task<List<RequestMediaResult>> SearchOutsideLibraryAsync");
        var debounce = Slice(page, "private void SearchBox_TextChanged", "private void ShowResultsShellForCurrentQuery");

        Assert.DoesNotContain("LoadFiltersAsync", initialize);
        Assert.Contains("await ViewModel.LoadMediaScopeAsync();", initialize);
        Assert.Contains("_ = EnsureSearchFiltersLoadedAsync();", debounce);
        Assert.Contains("ViewModel.LoadFiltersAsync(query, mediaType)", page);
        Assert.Contains("q: normalizedQuery", viewModel);
        Assert.Contains("type: normalizedType", viewModel);
        Assert.Contains("private string _sortField = \"relevance\"", viewModel);
        Assert.Contains("sort: SortField", fetch);
        Assert.Contains("order: SortOrder", fetch);
        Assert.DoesNotContain("sort: \"added_at\"", fetch);
        Assert.Contains("search_timing.txt", viewModel);
    }

    [Fact]
    public void LocalCatalogResultsAreNotBlockedByOptionalDiscovery()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SearchViewModel.cs");
        var searchMethod = Slice(source, "private async Task SearchAsync()", "public void CancelPendingSearch()");
        var catalogPublish = source.IndexOf("var response = await catalogTask", StringComparison.Ordinal);
        var discoveryPublish = source.IndexOf("_ = PublishOutsideLibraryResultsAsync", StringComparison.Ordinal);

        Assert.True(catalogPublish >= 0);
        Assert.True(discoveryPublish > catalogPublish);
        Assert.Contains("[RelayCommand(AllowConcurrentExecutions = true)]", source);
        Assert.DoesNotContain("await Task.WhenAll(catalogTask, peopleTask, outsideTask)", source);
        Assert.Contains("timeout.CancelAfter(TimeSpan.FromSeconds(6))", source);
        Assert.Contains("ReferenceEquals(_searchCts, owner)", source);
        Assert.Contains("IsCurrentSearchOwner(searchCts, querySnapshot)", source);
        Assert.Contains("BuildSearchKey(querySnapshot)", source);
        Assert.Contains("string.Equals(_lastAppliedSearchKey, searchKey, StringComparison.Ordinal)", source);
        Assert.Contains("ReplaceMediaResults(response.Items)", source);
        Assert.Contains("BulkObservableCollection<MediaItem> Results", source);
        Assert.Contains("Results.AddRange(items)", source);
        Assert.Contains("ReplacePeopleResults(people)", source);
        Assert.Contains("_lastAppliedSearchKey = searchKey", source);
        Assert.Contains("IsCurrentSearchQuery(querySnapshot)", source);
        Assert.DoesNotContain("if (ct.IsCancellationRequested || !IsCurrentSearchQuery(querySnapshot)) return;", searchMethod);
        Assert.Contains("private bool IsCurrentSearchOwner(CancellationTokenSource owner, string querySnapshot)", source);
        Assert.Contains("ReferenceEquals(_searchCts, owner)", source);
        Assert.Contains("StableRequestResultKey", source);
        Assert.Contains("SequenceEqual(outside.Select(StableRequestResultKey))", source);
        Assert.Contains("outside.Count == 0 && OutsideLibraryResults.Count == 0", source);
        Assert.True(
            searchMethod.IndexOf("ReplaceMediaResults(response.Items)", StringComparison.Ordinal) <
            searchMethod.IndexOf("SearchOutsideLibraryAsync(querySnapshot, ct)", StringComparison.Ordinal));
    }

    [Fact]
    public void ScopeChangesStartTheVisibleSearchBeforePersistingPreferenceCompletes()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SearchViewModel.cs");
        var scopeHandler = Slice(source, "public async Task SetMediaScopeAsync", "public async Task SetMediaTypeAsync");
        var typeHandler = Slice(source, "public async Task SetMediaTypeAsync", "private async Task SaveMediaScopePreferenceAsync");

        Assert.Contains("_ = SaveMediaScopePreferenceAsync(MediaScope);", scopeHandler);
        Assert.Contains("_ = SaveMediaScopePreferenceAsync(MediaScope);", typeHandler);
        Assert.DoesNotContain("await _settingsApi.PutSettingAsync(\"search.media_scope\"", scopeHandler);
        Assert.DoesNotContain("await _settingsApi.PutSettingAsync(\"search.media_scope\"", typeHandler);
        Assert.True(scopeHandler.IndexOf("_ = SaveMediaScopePreferenceAsync(MediaScope);", StringComparison.Ordinal) <
                    scopeHandler.IndexOf("await SearchAsync();", StringComparison.Ordinal));
        Assert.True(typeHandler.IndexOf("_ = SaveMediaScopePreferenceAsync(MediaScope);", StringComparison.Ordinal) <
                    typeHandler.IndexOf("await SearchAsync();", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionalDiscoveryAndErrorsRefreshVisibleSearchState()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml.cs");
        var web = ReadWebUiFile("web", "src", "pages", "Catalog.tsx");
        var ctor = Slice(source, "public SearchPage()", "protected override void OnNavigatedTo");
        var state = Slice(source, "private void UpdateResultsState", "private void UpdatePeopleSection");

        Assert.Contains("state.source === \"query\" ? \"in library\"", web);
        Assert.Contains("const showExactResultCount = state.source !== \"section\" && !isQuerySource;", web);
        Assert.Contains("{showExactResultCount ?", web);
        Assert.Contains("ViewModel.OutsideLibraryResults.CollectionChanged", ctor);
        Assert.Contains("UpdateRequestResults();", ctor);
        Assert.Contains("if (ViewModel.Results.Count == 0 && ViewModel.PeopleResults.Count == 0)", ctor);
        Assert.Contains("UpdateResultsState();", ctor);
        Assert.Contains("nameof(ViewModel.ErrorMessage)", ctor);
        Assert.Contains("ViewModel.OutsideLibraryResults.Count", state);
        Assert.Contains("NoResultsText.Visibility", state);
        Assert.Contains("var totalDisplay = mediaCount + peopleCount;", state);
        Assert.Contains("ResultCountText.Text = $\"{totalDisplay:N0} in library\";", state);
        Assert.Contains("ResultCountPanel.Visibility = Visibility.Collapsed;", state);
        Assert.DoesNotContain("ResultCountPanel.Visibility = ViewModel.IsLoading", state);
        Assert.Contains("SearchLoadingRepeater.Visibility = hasQuery &&", state);
        Assert.Contains("ViewModel.Results.Count == 0", state);
        Assert.Contains("UpdateRequestResults();", state);
        Assert.DoesNotContain("var totalDisplay = mediaCount + peopleCount + ViewModel.OutsideLibraryResults.Count", state);

        var requestState = Slice(source, "private void UpdateRequestResults()", "private void RequestResult_Click");
        Assert.Contains("RequestResultsSection.Visibility = ViewModel.OutsideLibraryResults.Count > 0", requestState);
        Assert.DoesNotContain("hasLocalResults", requestState);
    }

    [Fact]
    public void SearchResultsExposeWebStyleScrollToTopAffordance()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml");
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml.cs");

        Assert.Contains("x:Name=\"SearchScrollToTopButton\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Back to top\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Back to top\"", xaml);
        Assert.Contains("ResultsScroll.VerticalOffset > 720", source);
        Assert.Contains("ResultsScroll.ChangeView(null, 0, null)", source);
        Assert.Contains("SearchBox.Focus(FocusState.Programmatic)", source);
        Assert.Contains("_resultsScrollUserScrolled", source);
        Assert.Contains("ResultsScroll.ChangeView(null, 0, null, disableAnimation: true)", source);
        Assert.Contains("ResultsScroll.VerticalOffset > 24", source);
        Assert.Contains("Avoid firing an automatic \"load more\" while the initial result grid", source);
        Assert.Contains("if (!_resultsScrollUserScrolled", source);
    }

    [Fact]
    public void NullableArtworkStringsAreConvertedBeforeReachingImageSource()
    {
        var views = Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Views");
        foreach (var path in Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories))
        {
            var xaml = File.ReadAllText(path);
            Assert.DoesNotContain("Source=\"{x:Bind PosterUrl}\"", xaml);
            Assert.DoesNotContain("Source=\"{x:Bind Item.PosterUrl}\"", xaml);
        }

        var converter = ReadRepoFile("src", "SiloPlayer", "Converters", "UrlToImageSourceConverter.cs");
        Assert.Contains("Uri.TryCreate", converter);
        Assert.Contains("new BitmapImage(uri)", converter);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));

    private static string ReadWebUiFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), ".codex-tmp", "silo-server-current", .. parts]));

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
