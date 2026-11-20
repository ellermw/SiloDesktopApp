namespace SiloPlayer.Tests;

/// <summary>Providers must be reachable through the production Admin registration, shell resources and surface policy.</summary>
public sealed class AdminProvidersRegistrationTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    [Fact]
    public void ProvidersIsRegisteredWithItsApiControllerAndSharedSettingsKeys()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/Services/AdminConfigureServices.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.ProviderSettings, typeof(AdminProvidersPage))", source);
        Assert.Contains("AddTransient<AdminProvidersController>()", source);
        Assert.Contains("AddSingleton<AdminProvidersApi>()", source);
        Assert.Contains("AdminProvidersController.Keys", source);
        Assert.Contains("AdminProvidersController.Secrets", source);
    }

    [Fact]
    public void ProvidersRouteIsAnOfficialNavLeafAndItsXbfAndSourcesArePolicyListed()
    {
        var catalog = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer.Core/Services/Admin/AdminRouteCatalog.cs"));
        Assert.Contains("new AdminNavLeaf(AdminRoute.ProviderSettings, \"/admin/settings/providers\"", catalog);
        var build = File.ReadAllText(Path.Combine(Root(), "installer/build.ps1"));
        Assert.Contains("'Views/Admin/AdminProvidersPage.xbf'", build);
        var policy = File.ReadAllText(Path.Combine(Root(), "docs/parity/admin-monitor-surfaces.json"));
        foreach (var file in new[] { "AdminProvidersPage.cs", "AdminProvidersPage.Cards.cs", "AdminProvidersPage.xaml" })
            Assert.Contains("src/SiloPlayer/Views/Admin/" + file, policy);
        Assert.Contains("src/SiloPlayer.Core/Api/AdminProvidersApi.cs", policy);
        Assert.Contains("src/SiloPlayer.Core/Services/Admin/AdminProvidersController.cs", policy);
        Assert.Contains("src/SiloPlayer.Core/Models/Admin/AdminProviderModels.cs", policy);
    }
}
