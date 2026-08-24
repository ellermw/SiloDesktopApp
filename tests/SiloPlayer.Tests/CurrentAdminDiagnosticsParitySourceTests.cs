namespace SiloPlayer.Tests;

public sealed class CurrentAdminDiagnosticsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void DiagnosticsRouteImplementsCurrentWebUiReportWorkflow()
    {
        var api = Read("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var model = Read("src", "SiloPlayer.Core", "Models", "Admin", "DiagnosticReport.cs");
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminDiagnosticsPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "Admin", "AdminDiagnosticsPage.xaml");
        var shell = Read("src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs");

        Assert.Contains("/api/v1/diagnostics/status", api);
        Assert.Contains("/api/v1/admin/diagnostics/reports", api);
        Assert.Contains("/download?proxy=1", api);
        Assert.Contains("public JsonElement Manifest", model);
        Assert.Contains("Client Diagnostics", xaml);
        Assert.Contains("Client uploads", xaml);
        Assert.Contains("Playback sessions", page);
        Assert.Contains("Full manifest JSON", page);
        Assert.Contains("OpenLogsForSession", page);
        Assert.Contains("typeof(AdminDiagnosticsPage)", shell);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));
}
