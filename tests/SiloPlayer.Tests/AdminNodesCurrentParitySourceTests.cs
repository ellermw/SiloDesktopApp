namespace SiloPlayer.Tests;

public class AdminNodesCurrentParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminNodesPage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminNodesPage.xaml.cs"));

    [Fact]
    public void NodesUsesCurrentPageAndCompleteTables()
    {
        Assert.Contains("Text=\"Stream Nodes\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        foreach (var heading in new[] { "Group", "Streams", "Egress", "Jobs", "Last Check" })
            Assert.Contains($"Text=\"{heading}\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeRowsExposeGroupCapacityAndLiveLoad()
    {
        Assert.Contains("node.Group", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("node.ActiveJobs", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("node.MaxJobs", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("node.EgressKbps", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("node.MaxBandwidthKbps", CodeBehind, StringComparison.Ordinal);
    }
}
