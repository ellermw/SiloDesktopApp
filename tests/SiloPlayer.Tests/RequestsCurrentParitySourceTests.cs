namespace SiloPlayer.Tests;

public sealed class RequestsCurrentParitySourceTests
{
    [Fact]
    public void SearchUsesAnImmutableLatestWinsQuery()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "RequestsViewModel.cs");

        Assert.Contains("Interlocked.Exchange(ref _searchCts, owner)", source);
        Assert.Contains("previous?.Cancel()", source);
        Assert.Contains("var requestedMediaType = SelectedMediaType", source);
        Assert.Contains("var requestedPage = SearchPage", source);
        Assert.Contains("ReferenceEquals(Volatile.Read(ref _searchCts), owner)", source);
        Assert.DoesNotContain("StatusMessage = \"Searching...\";\n        SearchResults.Clear();", source);
    }

    [Fact]
    public void CachedRequestsPageCancelsSearchOnNavigation()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RequestsPage.xaml.cs");
        Assert.Contains("ViewModel.CancelSearch();", page);
    }

    [Fact]
    public void DiscoveryCardsExposeKeyboardNavigationAndStatusSemantics()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RequestsPage.xaml.cs");

        Assert.Contains("open.Click += RequestCard_Click", page);
        Assert.Contains("Open {result.Title} request details", page);
        Assert.Contains("request.GotFocus", page);
        Assert.Contains("BuildRequestRibbon(result, ribbonLabel)", page);
        Assert.Contains("RequestStatusColors(tone)", page);
        Assert.Contains("Open {result.Title} in library", page);
        Assert.Contains("Open {request.Title} request details", page);
        Assert.Contains("Open {request.Title} in library", page);
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
