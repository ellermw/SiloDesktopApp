namespace ContinuumPlayer.Tests;

public sealed class AdminLogStreamClientSourceTests
{
    [Fact]
    public void AdminLogsStreamUsesCurrentAccessTokenOnReconnect()
    {
        var root = FindRepositoryRoot();
        var clientSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ContinuumPlayer.Core",
            "Services",
            "AdminLogStreamClient.cs"));
        var pageSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "ContinuumPlayer",
            "Views",
            "Admin",
            "AdminLogsPage.xaml.cs"));

        Assert.Contains("Func<string?> accessTokenProvider", clientSource);
        Assert.Contains("_accessTokenProvider()", clientSource);
        Assert.Contains("new AdminLogStreamClient(api.BaseUrl, () => api.AccessToken)", pageSource);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "ContinuumPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
