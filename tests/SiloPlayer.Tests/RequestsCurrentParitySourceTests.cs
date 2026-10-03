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
        Assert.DoesNotContain("StatusMessage = \"Searching...\";\n        SearchResults.Clear();", source.ReplaceLineEndings("\n"));
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
        var card = ReadRepoFile("src", "SiloPlayer", "Controls", "ExternalTitleCard.cs");
        Assert.Contains("ExternalTitleCard.Build(result", page);
        Assert.Contains("Open {item.Title} request details", card);
        Assert.Contains("action.Button.GotFocus", card);
        Assert.Contains("StatusBadge(label, overlay: true)", card);
        Assert.Contains("RequestViewerPolicy.Label", card);
        Assert.Contains("Open {item.Title} in library", card);
        Assert.Contains("CanCancel(request)", page);
        Assert.Contains("Navigate<RequestDetailPage>(new RequestDetailNavigation(request.MediaType,request.TmdbId))", page);
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
