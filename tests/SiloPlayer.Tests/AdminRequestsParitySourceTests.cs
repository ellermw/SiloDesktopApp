namespace SiloPlayer.Tests;

public class AdminRequestsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminRequestsPage.xaml"));

    private static string Source => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminRequestsPage.xaml.cs"));

    [Fact]
    public void RequestsExposesEveryCurrentAdminTab()
    {
        Assert.Contains("Content=\"Queue\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Settings\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Integrations\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"User Overrides\"", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Media Requests\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueRendersCurrentFulfillmentAndActionDetails()
    {
        Assert.Contains("BuildTargets(request)", Source, StringComparison.Ordinal);
        Assert.Contains("target.InstanceName ?? target.IntegrationKind", Source, StringComparison.Ordinal);
        Assert.Contains("target.LastError", Source, StringComparison.Ordinal);
        Assert.Contains("RequestDetailNavigation", Source, StringComparison.Ordinal);
        Assert.Contains("DeclineAsync(request)", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsIntegrationsAndOverridesUseCurrentServerApis()
    {
        Assert.Contains("GetAdminRequestSettingsAsync", Source, StringComparison.Ordinal);
        Assert.Contains("UpdateAdminRequestSettingsAsync", Source, StringComparison.Ordinal);
        Assert.Contains("GetRequestIntegrationsAsync", Source, StringComparison.Ordinal);
        Assert.Contains("CreateRequestIntegrationAsync", Source, StringComparison.Ordinal);
        Assert.Contains("GetRequestUserLimitAsync", Source, StringComparison.Ordinal);
        Assert.Contains("UpdateRequestUserLimitAsync", Source, StringComparison.Ordinal);
        Assert.Contains("request_router.v1", Source, StringComparison.Ordinal);
    }
}
