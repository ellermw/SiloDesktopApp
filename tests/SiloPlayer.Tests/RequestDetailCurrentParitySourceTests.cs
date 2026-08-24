namespace SiloPlayer.Tests;

public sealed class RequestDetailCurrentParitySourceTests
{
    [Fact]
    public void FailedRequestActionsRestoreTheirLabelsAndUseGlobalFeedback()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "RequestDetailPage.xaml.cs");

        Assert.Contains("button.Content = $\"＋  Request", source);
        Assert.Contains("button.Content = \"Request\"", source);
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
        Assert.Contains("openDetail.Click += Recommendation_Click", source);
        Assert.Contains("Open {item.Title} request details", source);
        Assert.Contains("StatusTone(item.Request.Status)", source);
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
