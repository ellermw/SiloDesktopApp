namespace SiloPlayer.Tests;

public sealed class ServerActivityButtonSourceTests
{
    [Fact]
    public void ConnectionProblemIndicatorMatchesWebUiGracePeriodAndCountIndependence()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Controls",
            "ServerActivityButton.xaml.cs"));

        Assert.Contains("TimeSpan.FromSeconds(4)", source);
        Assert.Contains("ApplyConnectionState(_events.CurrentState)", source);
        Assert.Contains("_connectionProblemTimer.Tick += ConnectionProblemTimer_Tick", source);
        Assert.Contains("_showConnectionProblem ? Visibility.Visible : Visibility.Collapsed", source);
        Assert.DoesNotContain("(!_wsConnected && total > 0)", source);
        Assert.Contains("? \"Connecting\\u2026\"", source);
        Assert.Contains(": \"Disconnected\"", source);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
