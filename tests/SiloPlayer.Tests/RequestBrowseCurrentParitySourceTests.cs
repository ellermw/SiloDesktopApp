namespace SiloPlayer.Tests;

public sealed class RequestBrowseCurrentParitySourceTests
{
    [Fact]
    public void LatestBrowseQueryWinsAndEmptyResultsHaveARealState()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml.cs");
        var markup = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml");
        var session = ReadRepoFile("src", "SiloPlayer.Core", "Services", "RequestBrowseSession.cs");

        Assert.Contains("_browse.ResetAsync", page);
        Assert.Contains("Interlocked.Exchange(ref _owner, null)?.Cancel()", session);
        Assert.Contains("generation != _generation || owner.IsCancellationRequested || _owner != owner", session);
        Assert.Contains("ObservableCollection<RequestMediaResult>", session);
        Assert.Contains("seen.Add((item.MediaType, item.TmdbId))", session);
        Assert.Contains("TraverseEmptyPagesAsync", page);
        Assert.Contains("MoreError", session);
        Assert.Contains("LoadMore_Click", markup);
        Assert.Contains("x:Name=\"EmptyState\"", markup);
        Assert.Contains("Nothing matched", markup);
    }

    [Fact]
    public void FailedRequestMutationRestoresTheCardAndUsesGlobalFeedback()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml.cs");
        var sharedCard = ReadRepoFile("src", "SiloPlayer", "Controls", "ExternalTitleCard.cs");
        Assert.Contains("ExternalTitleCard.Build", page);
        Assert.Contains("if (pending) return", sharedCard);
        Assert.Contains("finally { pending = false; button.IsEnabled = true; button.Content = NormalContent(); MediaItemCardActions.BindText(button, \"Name\"", sharedCard);
        Assert.Contains("ToastService>().Error", sharedCard);
    }

    [Fact]
    public void BrowseCardsExposeCurrentWebUiStatusAndKeyboardActions()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml.cs");
        var markup = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml");

        var sharedCard = ReadRepoFile("src", "SiloPlayer", "Controls", "ExternalTitleCard.cs");
        Assert.Contains("StatusBadge(label, overlay: true, display:", sharedCard);
        Assert.Contains("web.requests.hub.openInLibraryFor", sharedCard);
        Assert.Contains("action.Button.GotFocus", sharedCard);
        Assert.Contains("web.item.menu.viewDetails", sharedCard);
        Assert.Contains("Could not load this browse page. Try a different sort or media type.", page);
        Assert.Contains("Click=\"RetryBrowse_Click\"", markup);
        Assert.Contains("_browse.Error != null && !missing ? Visibility.Visible : Visibility.Collapsed", page);
        Assert.Contains("MoreErrorText.Visibility = _browse.MoreError != null", page);
        Assert.Contains("web.common.ui.loadMore.button", page);
        Assert.DoesNotContain("catch (Exception ex) { Fail(ex.Message); }", page);
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
