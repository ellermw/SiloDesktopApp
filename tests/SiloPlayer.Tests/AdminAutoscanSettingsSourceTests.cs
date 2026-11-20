using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class AdminAutoscanSettingsSourceTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(TestRepository.Root, relative));

    [Fact]
    public void RegisteredPageAndEveryDomainSourceAreApprovedAndXbfIsRequired()
    {
        Assert.Contains("services.AddAdminAutoscanSettings();", Read("src/SiloPlayer/App.xaml.cs"));
        Assert.Contains("AdminRoute.AutoscanSettings, typeof(AdminAutoscanSettingsPage)", Read("src/SiloPlayer/Views/Admin/AdminAutoscanSettingsRegistration.cs"));
        Assert.Contains("Views/Admin/AdminAutoscanSettingsPage.xbf", Read("installer/build.ps1"));
        using var manifest = JsonDocument.Parse(Read("docs/parity/admin-monitor-surfaces.json"));
        var files = manifest.RootElement.GetProperty("sourceFiles").EnumerateArray().Select(item => item.GetString()).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TestRepository.Root, "src"), "*AutoscanSettings*", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
            Assert.Contains(Path.GetRelativePath(TestRepository.Root, file).Replace('\\', '/'), files);
    }

    [Fact]
    public void DomainNeverImportsUnmergedSourcesAndWritesHaveNoGenericSettingsOrRevisionContract()
    {
        var api = Read("src/SiloPlayer.Core/Api/AdminAutoscanSettingsApi.cs");
        Assert.DoesNotContain("/api/v2/admin/settings", api); Assert.DoesNotContain("If-Match", api);
        Assert.DoesNotContain("AdminAutoscanSources", api);
        var transport = Read("src/SiloPlayer.Core/Api/SiloApiClient.AdminAutoscanSettings.cs");
        Assert.Contains("allowTokenRefresh: false", transport); Assert.Contains("HttpStatusCode.NoContent", transport);
        var page = Read("src/SiloPlayer/Views/Admin/AdminAutoscanSettingsPage.cs");
        Assert.DoesNotContain("ToggleSwitch", page); Assert.DoesNotContain("Run now", page);
    }

    [Fact]
    public void NativeLaneUsesProductionShellAndAwaitsDialogAndUnloadBarriers()
    {
        var fixture = Read("tests/SiloPlayer.NativeRegressionTests/AdminAutoscanSettingsNativeFixture.cs");
        Assert.Contains("registrations.AddAdminAutoscanSettings()", fixture);
        Assert.Contains("AdminShellPage.TryOpenHref", fixture); Assert.Contains("new Frame()", fixture);
        var barrier = fixture.IndexOf("Task.WhenAll(pageUnloaded.Task, shellUnloaded.Task, dialogsClosed)", StringComparison.Ordinal);
        var restore = fixture.IndexOf("servicesField.SetValue(null, previousServices)", StringComparison.Ordinal);
        Assert.True(barrier >= 0 && restore > barrier);
        Assert.Contains("admin-autoscan-settings", Read("tests/SiloPlayer.NativeRegressionTests/Program.cs"));
    }
}

