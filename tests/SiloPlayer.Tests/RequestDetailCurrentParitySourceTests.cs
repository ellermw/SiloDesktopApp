namespace SiloPlayer.Tests;

public sealed class RequestDetailCurrentParitySourceTests
{
    [Fact]
    public void RetainedActionsKeepTheirStableLabelAndAccessibleToggleState()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "RequestDetailPage.xaml.cs");
        Assert.Contains("var labelText = new TextBlock", source);
        Assert.Contains("labelText.Text = text", source);
        Assert.Contains("AutomationProperties.SetHelpText(target, active ? \"On\" : \"Off\")", source);
        Assert.DoesNotContain("((TextBlock)content.Children[1]).Text", source);
    }

    [Fact]
    public void FailedRequestActionsRestoreTheirLabelsAndUseGlobalFeedback()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "RequestDetailPage.xaml.cs");

        // Actual rejection/pending/retry is covered by RequestDetailParityNativeFixture.
        Assert.Contains("var original = button.Content", source);
        Assert.Contains("button.Content = original", source);
        Assert.Contains("RequestSeasonsDialog.PickAsync", source);
        Assert.Contains("ToastService>().Error", source);
        Assert.DoesNotContain("button.Content = ex.Message", source);
    }

    [Fact]
    public void RequestDetailKeepsNavigationAndCurrentWebUiStatePresentation()
    {
        var markup = ReadRepoFile("src", "SiloPlayer", "Views", "RequestDetailPage.xaml");
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "RequestDetailPage.xaml.cs");

        Assert.Contains("x:Name=\"LoadingBackButton\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Go back\"", markup);
        // Recommendations retain navigation/watchlist while requesting stays on detail.
        Assert.Contains("ExternalTitleCard.Build(item, width, watchlist: watchlist)", source);
        Assert.Contains("RequestViewerPolicy.Label", source);
        Assert.Contains("RequestDownloadProgress.Build", source);
        Assert.Contains("web.detail.overview.tmdbVotes", source);
        Assert.Contains("(item.Genres ?? []).Take(4)", source);
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
