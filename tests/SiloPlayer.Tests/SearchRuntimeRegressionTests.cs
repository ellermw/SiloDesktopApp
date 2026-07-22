namespace SiloPlayer.Tests;

public sealed class SearchRuntimeRegressionTests
{
    [Fact]
    public void TypingDoesNotWaitForFilterWarmupAndOnlyUsesTheNativeClearButton()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SearchPage.xaml.cs");
        var handler = Slice(code, "private void SearchBox_TextChanged", "private void SearchBox_KeyDown");

        Assert.DoesNotContain("SearchBoxClearButton", xaml);
        Assert.DoesNotContain("ResultsSearchBoxClearButton", xaml);
        Assert.DoesNotContain("EnsureInitializedAsync", handler);
        Assert.Contains("TimeSpan.FromMilliseconds(100)", handler);
        Assert.Contains("SearchBox.FocusState != FocusState.Unfocused", handler);
        Assert.Contains("_pendingResultsSearchFocus = true", handler);
        Assert.Contains("ShowResultsShellForCurrentQuery();", handler);
        Assert.Contains("FocusResultsSearchBox(_pendingResultsSearchFocusState);", handler);
        Assert.True(handler.IndexOf("FocusResultsSearchBox(_pendingResultsSearchFocusState);", StringComparison.Ordinal) <
                    handler.IndexOf("await ViewModel.SearchCommand.ExecuteAsync", StringComparison.Ordinal));

        var focusHelper = Slice(code, "private void FocusResultsSearchBox", "private void SearchBox_KeyDown");
        Assert.Contains("ResultsSearchBox.Focus", focusHelper);
        Assert.Contains("ResultsSearchBox.SelectionStart = ResultsSearchBox.Text.Length", focusHelper);
    }

    [Fact]
    public void LocalCatalogResultsAreNotBlockedByOptionalDiscovery()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SearchViewModel.cs");
        var catalogPublish = source.IndexOf("var response = await catalogTask", StringComparison.Ordinal);
        var discoveryPublish = source.IndexOf("_ = PublishOutsideLibraryResultsAsync", StringComparison.Ordinal);

        Assert.True(catalogPublish >= 0);
        Assert.True(discoveryPublish > catalogPublish);
        Assert.DoesNotContain("await Task.WhenAll(catalogTask, peopleTask, outsideTask)", source);
        Assert.Contains("timeout.CancelAfter(TimeSpan.FromSeconds(6))", source);
        Assert.Contains("ReferenceEquals(_searchCts, owner)", source);
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
