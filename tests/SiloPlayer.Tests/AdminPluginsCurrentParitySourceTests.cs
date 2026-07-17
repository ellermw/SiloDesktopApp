namespace SiloPlayer.Tests;

public class AdminPluginsCurrentParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminPluginsPage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminPluginsPage.xaml.cs"));
    private static string Api => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer.Core", "Api", "PluginsApi.cs"));
    private static string MainWindow => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "MainWindow.xaml.cs"));
    private static string PluginRoutePage => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "PluginRoutePage.xaml.cs"));
    private static string SettingsModel => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer.Core", "Models", "Plugins", "PluginSettings.cs"));

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
        Assert.Contains("SaveAuthBindingAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("SaveTaskBindingAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenPluginRouteAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"auto\", \"Auto\")", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("IsOn = plugin.Enabled", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"notify\", \"Notify\")", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"off\", \"Off\")", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogUsesResponsiveCardsInlineRepositoriesAndManualUpload()
    {
        Assert.Contains("x:Name=\"AvailableCards\"", Markup, StringComparison.Ordinal);
        Assert.Contains("AvailableCards_SizeChanged", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Manual Install\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RepoForm\"", Markup, StringComparison.Ordinal);
        Assert.Contains("UploadPluginAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("/api/v1/admin/plugins/uploads", Api, StringComparison.Ordinal);
        Assert.Contains("/api/v1/admin/plugins/uploads/chunked", Api, StringComparison.Ordinal);
        Assert.Contains("DefaultUploadChunkSize", Api, StringComparison.Ordinal);
        Assert.Contains("This plugin has no additional configuration.", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void UserPluginAppsAreGroupedAndOpenAuthenticatedRoutes()
    {
        Assert.Contains("public string? Category", SettingsModel, StringComparison.Ordinal);
        Assert.Contains("route.NavigationKind.Equals(\"user\"", MainWindow, StringComparison.Ordinal);
        Assert.Contains("BuildPluginApps", MainWindow, StringComparison.Ordinal);
        Assert.Contains("PluginRoutePage", MainWindow, StringComparison.Ordinal);
        Assert.Contains("/api/v1/plugins/{args.InstallationId}{path}", PluginRoutePage, StringComparison.Ordinal);
        Assert.Contains("Authorization", PluginRoutePage, StringComparison.Ordinal);
        Assert.Contains("X-Profile-Id", PluginRoutePage, StringComparison.Ordinal);
    }
}
