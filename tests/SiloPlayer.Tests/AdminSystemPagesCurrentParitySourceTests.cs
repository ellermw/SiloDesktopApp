namespace SiloPlayer.Tests;

public class AdminSystemPagesCurrentParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string View(string name, string extension = ".xaml") => File.ReadAllText(
        Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", name + extension));

    [Theory]
    [InlineData("AdminNodesPage")]
    [InlineData("AdminApiKeysPage")]
    [InlineData("AdminMaintenancePage")]
    public void StandardSystemPagesUseCurrentWebPageShellGeometry(string page)
    {
        var markup = View(page);
        Assert.Contains("Padding=\"40,24,40,24\"", markup, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1400\"", markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NodesExposeCurrentTablesHealthAndCrudActions()
    {
        var markup = View("AdminNodesPage");
        var code = View("AdminNodesPage", ".xaml.cs");
        Assert.Contains("Proxy Nodes", markup, StringComparison.Ordinal);
        Assert.Contains("Transcode Nodes", markup, StringComparison.Ordinal);
        Assert.Contains("Egress", markup, StringComparison.Ordinal);
        Assert.Contains("CheckHealthCommand", code, StringComparison.Ordinal);
        Assert.Contains("OpenCreateDialogAsync", code, StringComparison.Ordinal);
        Assert.Contains("OpenEditDialogAsync", code, StringComparison.Ordinal);
        Assert.Contains("OpenDeleteDialogAsync", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProxyNodesTableScroll\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TranscodeNodesTableScroll\"", markup, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeysExposeTierPagingCopyAndOneTimeReveal()
    {
        var markup = View("AdminApiKeysPage");
        var code = View("AdminApiKeysPage", ".xaml.cs");
        Assert.Contains("Create Key", markup, StringComparison.Ordinal);
        Assert.Contains("25, 50, 100", code, StringComparison.Ordinal);
        Assert.Contains("UpdateTierCommand", code, StringComparison.Ordinal);
        Assert.Contains("Copy & Close", code, StringComparison.Ordinal);
        Assert.Contains("MaskKey", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyUsesCurrentShellPipelineAndThreeWorkspaces()
    {
        var markup = View("AdminPolicyPage");
        Assert.Contains("Padding=\"40,32,40,40\"", markup, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1400\"", markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PipelineStrip\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Overrides\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Baseline\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Decision Log\"", markup, StringComparison.Ordinal);
    }
}
