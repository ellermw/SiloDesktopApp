using System.Text.Json;
using SiloPlayer.Core.Services.Admin;

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
    public void TheRouteIsReachableThroughTheRealNavigationAndNotListedAsUnsupported()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/autoscan", out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind); Assert.Equal(AdminRoute.AutoscanSettings, resolution.Request!.Route);
        Assert.True(AdminRouteCatalog.IsValid(resolution.Request));
        Assert.Equal("/admin/settings/autoscan", AdminRouteCatalog.Leaf(AdminRoute.AutoscanSettings).Href);
        // The page's real registration is what makes the sidebar entry openable.
        var registry = new AdminPageRegistry(AdminNavigation.MonitorRoutes.Select(route => new AdminPageRegistration(route, typeof(object))),
            [new AdminPageRegistration(AdminRoute.AutoscanSettings, typeof(string))]);
        Assert.True(registry.CanOpen(resolution.Request)); Assert.Equal(typeof(string), registry.PageTypeOf(AdminRoute.AutoscanSettings));
        var topic = AdminRouteCatalog.Sections.Single(section => section.Id == "manage").Entries.Cast<AdminNavTopic>().Single(entry => entry.Id == "autoscan");
        Assert.True(registry.HasAny(topic));
    }

    [Fact]
    public void DomainNeverImportsUnmergedSourcesAndWritesHaveNoGenericSettingsOrRevisionContract()
    {
        var api = Read("src/SiloPlayer.Core/Api/AdminAutoscanSettingsApi.cs");
        Assert.DoesNotContain("/api/v2/admin/settings", api); Assert.DoesNotContain("If-Match", api);
        Assert.DoesNotContain("AdminAutoscanSources", api);
        // Bodies reach the transport as Dictionary<string, object?>; any other object would be reflected as its own properties.
        Assert.DoesNotContain("new { enabled", api); Assert.Contains("Dictionary<string, object?>", api);
        var transport = Read("src/SiloPlayer.Core/Api/SiloApiClient.AdminAutoscanSettings.cs");
        Assert.Contains("allowTokenRefresh: false", transport); Assert.Contains("HttpStatusCode.NoContent", transport);
        var page = Read("src/SiloPlayer/Views/Admin/AdminAutoscanSettingsPage.cs");
        Assert.DoesNotContain("ToggleSwitch", page); Assert.DoesNotContain("Run now", page);
        foreach (var file in new[] { api, transport, page, Read("src/SiloPlayer/Views/Admin/AdminAutoscanSettingsPage.Dialogs.cs"), Read("src/SiloPlayer.Core/Services/Admin/AdminAutoscanSettingsController.cs") })
        {
            Assert.DoesNotContain("217ff25ab", file);
            Assert.DoesNotContain("/api/" + "v1", file);
        }
    }

    [Fact]
    public void PageAndDialogsAvoidTheKnownCrashPatterns()
    {
        var dialogs = Read("src/SiloPlayer/Views/Admin/AdminAutoscanSettingsPage.Dialogs.cs") + Read("src/SiloPlayer/Views/Admin/AdminAutoscanSettingsPage.cs");
        // No search-root focus movement and no method group passed where a lambda must call Hide on the live dialog.
        Assert.DoesNotContain("TryMoveFocus", dialogs); Assert.DoesNotContain("dialog.Hide)", dialogs); Assert.DoesNotContain("dialog.Hide,", dialogs);
        // Rows are rebuilt from the state on every render, never reused from an old visual tree.
        Assert.Contains("_rows.Children.Clear();", dialogs);
        // Add and delete are Buttons, so Enter and Space activate them; the save bar owns Save and Discard.
        Assert.DoesNotContain("Tapped +=", dialogs); Assert.DoesNotContain("PointerPressed +=", dialogs);
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
        var program = Read("tests/SiloPlayer.NativeRegressionTests/Program.cs");
        var selector = program.IndexOf("\"admin-autoscan-settings\"", StringComparison.Ordinal);
        Assert.True(selector >= 0);
        var block = program[selector..program.IndexOf("SILO_NATIVE_TEST_ONLY", selector + 10, StringComparison.Ordinal)];
        Assert.Contains("AdminAutoscanSettingsNativeFixture.RunAsync(_window)", block); Assert.Contains("PASS: native regression run completed.", block);
        Assert.Contains("Exit(); return;", block);
    }

    // Every string below is the English value of the official web/src/locales/en.json key at Silo-Server/silo-server 66b836f82.
    public static TheoryData<string, string> OfficialText => new()
    {
        { "title", "Autoscan" }, // web.admin.settings.autoscan.title
        { "loadingSettings", "Loading settings" }, // web.admin.settings.fields.loadingSettings
        { "loadError", "Autoscan settings could not be loaded. Reload before editing." },
        { "polling.group", "Polling defaults" }, { "polling.description", "Used by sources that don't set their own." },
        { "polling.interval", "Default check interval" }, { "polling.debounce", "Debounce" }, { "seconds", "seconds" },
        { "polling.debounceDescription", "Skips repeat reports of an unchanged file within this window. Changed or deleted files still scan." },
        { "connections.title", "Connections" },
        { "connections.description", "Credentials for sources that reach a server, such as Sonarr or Radarr. Webhook sources need none." },
        { "connections.add", "Add connection" }, { "connections.empty", "No connections yet." }, { "loading", "Loading…" },
        { "name", "Name" }, { "kind", "Kind" }, { "source", "Source" }, { "url", "URL" }, { "own", "Own" }, { "reuse", "Reused from Requests" },
        { "edit", "Edit connection" }, { "delete", "Delete connection" },
        { "dialog.description", "Reuse a server you already configured in Requests (Sonarr/Radarr), or enter your own credentials." },
        { "dialog.type", "Connection type" }, { "dialog.own", "Enter credentials manually" }, { "dialog.reuse", "Reuse a server from Requests" },
        { "dialog.requestServer", "Request server" }, { "dialog.loadingServers", "Loading servers…" }, { "dialog.selectServer", "Select a server…" },
        { "dialog.requests", "Settings › Requests" }, { "dialog.save", "Save changes" }, { "dialog.saving", "Saving…" },
        { "key", "API key" }, { "key.keep", "(leave blank to keep existing)" }, { "key.placeholder", "Enter API key" },
        { "baseUrl", "Base URL" }, { "name.placeholder", "My Sonarr" }, { "test", "Test connection" }, { "testing", "Testing…" },
        { "connected", "Connected" }, { "failed", "Connection failed" }, { "testRequestFailed", "Connection test failed" },
        { "delete.title", "Delete connection?" }, { "delete.confirm", "Delete" },
        { "saved", "Autoscan settings saved" }, { "created", "Autoscan connection created" }, { "updated", "Autoscan connection updated" }, { "deleted", "Autoscan connection deleted" },
        { "settingsUnconfirmed", "Settings persistence could not be confirmed. Reload settings before submitting again." },
        { "createUnconfirmed", "Connection creation could not be confirmed. Refresh connections before submitting again." },
        { "updateUnconfirmed", "Connection update could not be confirmed. Refresh connections before submitting again." },
        { "deleteUnconfirmed", "Connection deletion could not be confirmed. Refresh connections and check source bindings before submitting again." },
        { "reschedule.failed", "Settings saved, but poll-task rescheduling failed. Runtime may retain its previous schedule until restart." },
        { "reschedule.not_configured", "Settings saved; no poll-task rescheduler is configured on this server." },
    };

    [Theory]
    [MemberData(nameof(OfficialText))]
    public void EveryOfficialStringMatchesThePinnedCatalog(string key, string english) => Assert.Equal(english, AdminAutoscanSettingsText.T(key));

    [Fact]
    public void ComposedOfficialStringsMatchThePinnedCatalog()
    {
        Assert.Equal("No Sonarr or Radarr servers yet. Add one under Settings › Requests first, or enter credentials manually.",
            string.Join(" ", AdminAutoscanSettingsText.T("dialog.noServers"), AdminAutoscanSettingsText.T("dialog.requests"), AdminAutoscanSettingsText.T("dialog.noServersEnd")));
        Assert.Equal("“Movies” will be removed. Deletion is blocked while scan sources reference this connection. Reconfigure those sources first.", AdminAutoscanSettingsText.DeleteDescription("Movies"));
        Assert.Equal("Connected (v4.0.1)", AdminAutoscanSettingsText.Connected("4.0.1")); Assert.Equal("Connected", AdminAutoscanSettingsText.Connected(null));
        Assert.Equal("Movies (Radarr)", AdminAutoscanSettingsText.RequestServerOption("Movies", "radarr"));
        Assert.Equal("lidarr", AdminAutoscanSettingsText.KindLabel("lidarr"));
    }
}
