namespace SiloPlayer.Tests;

public class AdminPluginsCurrentParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminPluginsPage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminPluginsPage.xaml.cs"));

    [Fact]
    public void PluginsUsesCurrentPageGeometryAndTabs()
    {
        Assert.Contains("MaxWidth=\"1560\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Check for updates", Markup, StringComparison.Ordinal);
        Assert.Contains("Search installed plugins", Markup, StringComparison.Ordinal);
        Assert.Contains("Search the plugin catalog", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledCardsExposeConfigurationPolicyAndEnabledSwitch()
    {
        Assert.Contains("OpenPluginConfigurationAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("plugin.GlobalConfigSchema", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("SaveGlobalConfigAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"auto\", \"Auto\")", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("new ToggleSwitch { IsOn = plugin.Enabled", CodeBehind, StringComparison.Ordinal);
    }
}
