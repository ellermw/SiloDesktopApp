namespace SiloPlayer.Tests;

public sealed class AdminSettingsDetailPageSourceTests
{
    [Fact]
    public void InlineDiscardButtonExecutesDiscardCommandBeforeRebuildingFields()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Views",
            "Admin",
            "AdminSettingsDetailPage.xaml.cs"));

        Assert.Contains("ViewModel.DiscardCommand.Execute(null);", source);
        Assert.Contains("discardBtn.Click += DiscardButton_Click;", source);
    }

    [Fact]
    public void ScannerSettingsIncludeMarkersSection()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Views",
            "Admin",
            "AdminSettingsDetailPage.xaml.cs"));

        Assert.Contains("AddSectionHeader(\"Markers\")", source);
        Assert.Contains("\"markers.mode\"", source);
        Assert.Contains("\"markers.lazy_playback\"", source);
        Assert.Contains("Fetch Markers at Playback if Missing", source);
    }

    [Fact]
    public void IntegrationsSettingsIncludesMdblistApiKey()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Views",
            "Admin",
            "AdminSettingsDetailPage.xaml.cs"));

        Assert.Contains("MDBList", source);
        Assert.Contains("mdblist.api_key", source);
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
