namespace SiloPlayer.Tests;

public sealed class RequestBrowseCurrentParitySourceTests
{
    [Fact]
    public void LatestBrowseQueryWinsAndEmptyResultsHaveARealState()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml.cs");
        var markup = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml");

        Assert.Contains("Interlocked.Exchange(ref _loadCts, owner)", page);
        Assert.Contains("previous?.Cancel()", page);
        Assert.Contains("var requestedPage = _page", page);
        Assert.Contains("ReferenceEquals(Volatile.Read(ref _loadCts), owner)", page);
        Assert.Contains("x:Name=\"EmptyState\"", markup);
        Assert.Contains("Nothing matched", markup);
    }

    [Fact]
    public void FailedRequestMutationRestoresTheCardAndUsesGlobalFeedback()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml.cs");
        Assert.Contains("button.Content = item.RequestLabel", page);
        Assert.Contains("button.IsEnabled = item.Request.Requestable", page);
        Assert.Contains("ToastService>().Error", page);
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
