namespace SiloPlayer.Tests;

public sealed class AdminAutoscanParitySourceTests
{
    [Fact]
    public void AutoscanImplementsCurrentFourTabRouteAndContracts()
    {
        var api = Read("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml");
        string[] endpoints = ["settings", "connections", "connections/test", "scan-source-plugins", "sources", "rewrite-suggestions", "webhook/rotate", "events", "scans", "trigger", "status"];
        foreach (var endpoint in endpoints) Assert.Contains(endpoint, api);
        foreach (var tab in new[] { "Sources", "Activity", "Connections", "Settings" }) Assert.Contains($"Content=\"{tab}\"", page);
        Assert.Contains("WebUI line tabs", page);
        Assert.Contains("INTERVAL &amp; SETTINGS", page);
        Assert.Contains("RUNNING SCANS", page);
        Assert.Contains("Use configured libraries", Read("src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml.cs"));
        Assert.Contains("Reuse from Requests", Read("src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml.cs"));
        Assert.Contains("Interval = TimeSpan.FromSeconds(15)", Read("src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml.cs"));
    }

    [Fact]
    public void AutoscanIsRegisteredInCurrentAutomationOrder()
    {
        var shell = Read("src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs");
        var app = Read("src", "SiloPlayer", "App.xaml.cs");
        Assert.Contains("AdminAutoscanViewModel", app);
        Assert.Contains("typeof(AdminAutoscanPage)", shell);
        Assert.Contains("AddNavGroup(\"AUTOMATION\", NavAutoscan, NavScheduledTasks, NavSubtitles, NavMarkerHistory, NavRecommendations)", shell);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Could not find repository root.");
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }
}
