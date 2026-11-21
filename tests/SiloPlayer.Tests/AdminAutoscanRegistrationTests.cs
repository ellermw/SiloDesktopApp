using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Autoscan Sources must be reachable through the production Admin registration, shell resources and surface policy.</summary>
public sealed class AdminAutoscanRegistrationTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    [Fact]
    public void SourcesIsRegisteredWithTheProductionAppAndItsController()
    {
        var app = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/App.xaml.cs"));
        Assert.Contains("services.AddAdminAutoscanSources();", app);
        var registration = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/Views/Admin/AdminAutoscanRegistration.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.AutoscanSources, typeof(AdminAutoscanSourcesPage))", registration);
        Assert.Contains("AddTransient<AdminAutoscanController>()", registration);
    }

    [Fact]
    public void SourcesRouteIsAnOfficialSupportedLeafNotAnUnsupportedPattern()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/autoscan", out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal(AdminRoute.AutoscanSources, resolution.Request!.Route);
        // Activity and Settings are separate families: they resolve as routes but have no registration here.
        var registration = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/Views/Admin/AdminAutoscanRegistration.cs"));
        Assert.DoesNotContain("AutoscanActivity", registration);
        Assert.DoesNotContain("AutoscanSettings", registration);
    }

    [Fact]
    public void XbfAndSourcesArePolicyListed()
    {
        var build = File.ReadAllText(Path.Combine(Root(), "installer/build.ps1"));
        Assert.Contains("'Views/Admin/AdminAutoscanSourcesPage.xbf'", build);
        var policy = File.ReadAllText(Path.Combine(Root(), "docs/parity/admin-monitor-surfaces.json"));
        foreach (var file in new[] { "AdminAutoscanSourcesPage.cs", "AdminAutoscanSourcesPage.xaml", "AdminAutoscanSourceEditor.cs", "AdminAutoscanSourceEditor.Connection.cs", "AdminAutoscanSourceEditor.Paths.cs", "AdminAutoscanSourceEditor.Webhook.cs", "AdminAutoscanRegistration.cs" })
            Assert.Contains("src/SiloPlayer/Views/Admin/" + file, policy);
        foreach (var file in new[] { "AdminAutoscanController.cs", "AdminAutoscanEditorDraft.cs", "AdminAutoscanOutcome.cs", "AdminAutoscanPresentation.cs", "AdminAutoscanRunNow.cs", "AdminAutoscanSourceDraft.cs", "AdminAutoscanText.cs" })
            Assert.Contains("src/SiloPlayer.Core/Services/Admin/" + file, policy);
        Assert.Contains("src/SiloPlayer.Core/Api/AdminAutoscanApi.cs", policy);
        Assert.Contains("src/SiloPlayer.Core/Models/Admin/AdminAutoscanModels.cs", policy);
    }

    [Fact]
    public void SharedSchemaFormKeepsItsTwoArgumentBehaviourForExistingCallers()
    {
        var form = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/Controls/ProviderSchemaForm.cs"));
        Assert.Contains("this(form, values, null, false)", form);
        Assert.Contains("if (!_nameControls) return;", form);
    }
}
