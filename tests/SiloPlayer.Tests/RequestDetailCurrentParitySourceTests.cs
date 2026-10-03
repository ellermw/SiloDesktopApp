namespace SiloPlayer.Tests;

public sealed class RequestDetailCurrentParitySourceTests
{
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
        // Recommendations now share the same actionable card as discovery grids.
        Assert.Contains("ExternalTitleCard.Build(item, width, request, watchlist)", source);
        Assert.Contains("RequestViewerPolicy.Label", source);
        Assert.Contains("RequestDownloadProgress.Build", source);
        Assert.Contains("FormatVoteCount(item.VoteCount.Value)", source);
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
