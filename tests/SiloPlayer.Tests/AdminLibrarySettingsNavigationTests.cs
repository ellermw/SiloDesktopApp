using System.Text.RegularExpressions;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminLibrarySettingsNavigationTests
{
    /// <summary>Every web.admin key the Library settings sources name is in AdminText; the Libraries page's own scan excludes these files.</summary>
    [Fact]
    public void EveryLibrarySettingsKeyUsedBySourceHasEnglishText()
    {
        var files = Directory.EnumerateFiles(Path.Combine(TestRepository.Root, "src"), "AdminLibrarySettings*.cs", SearchOption.AllDirectories).ToArray();
        Assert.True(files.Length >= 4, "the Library settings sources were found");
        var keys = files.SelectMany(path => Regex.Matches(File.ReadAllText(path), "\"(web\\.admin\\.[A-Za-z0-9_.]+)\"").Select(match => match.Groups[1].Value))
            .Where(key => !key.EndsWith('.')).Distinct().ToArray();
        Assert.True(keys.Length >= 40, "the Library settings text keys were found");
        Assert.Empty(keys.Where(key => !AdminText.Has(key)));
    }

    [Fact]
    public void ProductionRegistrationAndPackagingIncludeTheFinishedPage()
    {
        var root = TestRepository.Root;
        var registration = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "AdminConfigureServices.cs"));
        Assert.Contains("AddTransient<AdminLibrarySettingsController>()", registration);
        Assert.Contains("AddSingleton<AdminLibrarySettingsApi>()", registration);
        Assert.Contains("new AdminPageRegistration(AdminRoute.LibrarySettings, typeof(AdminLibrarySettingsPage))", registration);
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminLibrarySettingsPage.xaml")));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "build.ps1"));
        Assert.Contains("'Views/Admin/AdminLibrarySettingsPage.xbf'", installer);
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "parity", "admin-monitor-surfaces.json")));
        Assert.Contains("src/SiloPlayer/Views/Admin/AdminLibrarySettingsPage.cs",
            manifest.RootElement.GetProperty("sourceFiles").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void LibrarySettingsRouteIsReachableAndSearchableOnlyWhenRegistered()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/library", out var resolution));
        Assert.Equal(AdminRoute.LibrarySettings, resolution.Request!.Route);
        Assert.Equal("/admin/settings/library", resolution.CanonicalHref);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        var registry = new AdminPageRegistry([new(AdminRoute.LibrarySettings, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.Equal(AdminRoute.LibrarySettings,
            Assert.Single(Assert.Single(AdminNavigation.SearchGroups("subtitle timing", registry.IsRegistered)).Pages).Route);
    }

    [Fact]
    public void EveryOfficialLibraryMetadataMessageResolves()
    {
        var keys = new[]
        {
            "web.admin.settings.libraryMetadata.title", "web.admin.settings.libraryMetadata.general.group",
            "web.admin.settings.libraryMetadata.general.allVersions.label", "web.admin.settings.libraryMetadata.general.allVersions.description",
            "web.admin.settings.libraryMetadata.general.fallbackLanguage.label", "web.admin.settings.libraryMetadata.general.fallbackLanguage.description",
            "web.admin.settings.libraryMetadata.general.fallbackLanguage.original", "web.admin.settings.libraryMetadata.scanning.group",
            "web.admin.settings.libraryMetadata.scanning.restoreDefaults", "web.admin.settings.libraryMetadata.scanning.realtime.label",
            "web.admin.settings.libraryMetadata.scanning.realtime.description", "web.admin.settings.libraryMetadata.scanning.scannerWorkers.label",
            "web.admin.settings.libraryMetadata.scanning.scannerWorkers.description", "web.admin.settings.libraryMetadata.scanning.matcherWorkers.label",
            "web.admin.settings.libraryMetadata.scanning.matcherWorkers.description", "web.admin.settings.libraryMetadata.scanning.imageWorkers.label",
            "web.admin.settings.libraryMetadata.scanning.imageWorkers.description", "web.admin.settings.libraryMetadata.scanning.personRefresh.label",
            "web.admin.settings.libraryMetadata.scanning.personRefresh.description", "web.admin.settings.libraryMetadata.scanning.matcherBatch.label",
            "web.admin.settings.libraryMetadata.scanning.matcherBatch.description", "web.admin.settings.libraryMetadata.maturity.group",
            "web.admin.settings.libraryMetadata.maturity.description", "web.admin.settings.libraryMetadata.maturity.unrated.label",
            "web.admin.settings.libraryMetadata.maturity.unrated.description", "web.admin.settings.libraryMetadata.maturity.unrated.hide",
            "web.admin.settings.libraryMetadata.maturity.unrated.allow", "web.admin.settings.libraryMetadata.ratings.group",
            "web.admin.settings.libraryMetadata.ratings.description", "web.admin.settings.libraryMetadata.ratings.loadError",
            "web.admin.settings.libraryMetadata.ratings.empty", "web.admin.settings.libraryMetadata.ratings.fromProvider",
            "web.admin.settings.libraryMetadata.ratings.undeclared.heading", "web.admin.settings.libraryMetadata.ratings.undeclared.description",
            "web.admin.settings.libraryMetadata.subtitleSync.group", "web.admin.settings.libraryMetadata.subtitleSync.description",
            "web.admin.settings.libraryMetadata.subtitleSync.auto.label", "web.admin.settings.libraryMetadata.subtitleSync.auto.description",
            "web.admin.settings.libraryMetadata.subtitleSync.execution.label", "web.admin.settings.libraryMetadata.subtitleSync.execution.description",
            "web.admin.settings.libraryMetadata.subtitleSync.execution.noNodes", "web.admin.settings.libraryMetadata.subtitleSync.capacity.label",
            "web.admin.settings.libraryMetadata.subtitleSync.capacity.description", "web.admin.settings.playback.execution.localServer",
            "web.admin.settings.playback.execution.preferNodes", "web.admin.settings.playback.execution.nodesOnly",
        };
        Assert.All(keys, key => Assert.NotEqual(key, AdminText.T(key)));
        Assert.Equal("From MDBList", AdminText.T("web.admin.settings.libraryMetadata.ratings.fromProvider", new { provider = "MDBList" }));
        Assert.Equal("Library & Metadata", AdminText.T("web.admin.settings.libraryMetadata.title"));
    }
}
