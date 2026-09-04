namespace SiloPlayer.Tests;

public sealed class CodeRabbitPr6RegressionTests
{
    [Fact]
    public void InstallerQuotesEverySpacedSigningArgumentForInnoSetup()
    {
        var source = ReadRepoFile("installer", "build.ps1");

        Assert.Contains("`$q$InnoPowerShell`$q", source, StringComparison.Ordinal);
        Assert.Contains("`$q$InnoSignScript`$q", source, StringComparison.Ordinal);
        Assert.Contains("`$q$TimestampServer`$q", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewPlayOwnershipClearsAutoSkipLatchesBeforePublishingContent()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");
        var start = source.IndexOf("private async Task PlayCoreAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private static (string Title, string Detail) DescribePlaybackError", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        var clear = method.IndexOf("_autoSkipMarkerIdentity.Clear();", StringComparison.Ordinal);
        var takeOwnership = method.IndexOf("ContentId = contentId;", StringComparison.Ordinal);
        Assert.True(clear >= 0 && clear < takeOwnership);
    }

    [Fact]
    public void SetupRequiredPageHandlesLaunchFailureAndStaleStatusRequests()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "ServerSetupRequiredPage.xaml.cs");

        Assert.Contains("var launched = await Windows.System.Launcher.LaunchUriAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (!launched)", source, StringComparison.Ordinal);
        Assert.Contains("private CancellationTokenSource? _setupStatusCts;", source, StringComparison.Ordinal);
        Assert.Contains("protected override void OnNavigatedFrom", source, StringComparison.Ordinal);
        Assert.Contains("GetSetupStatusAsync(requestCts.Token)", source, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(Frame?.Content, this)", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path))
                return File.ReadAllText(path);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }
}
