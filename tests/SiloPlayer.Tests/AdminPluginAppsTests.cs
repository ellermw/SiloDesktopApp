using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Admin Plugin Apps against official df37bfc6d57b1680c0f7a59e51e15c8d0cfa6065
/// (adminNavigation.buildAdminPluginNavItems, pluginRouteHref, buildPluginHref, plugin_launch.go).
/// All servers, ids and tokens are synthetic.
/// </summary>
public sealed class AdminPluginAppsTests
{
    private const string Launch = """{"expires_in":300}""";
    private const string OfficialCookie = "silo_plugin_access=synthetic-plugin-token; Path=/api/v2/plugin-content; Max-Age=300; HttpOnly; SameSite=Lax";

    [Fact]
    public async Task EntriesFollowBuildAdminPluginNavItemsAcrossPages()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(
            request.RequestUri!.Query.Contains("cursor=next", StringComparison.Ordinal)
                ? """{"items":[{"id":"8","plugin_id":"custom-b","enabled":true,"routes":[{"path":"/manager/*","navigable":true,"navigation_kind":"admin","navigation_label":"Custom Artwork"}]}],"page":{"has_more":false}}"""
                : """{"items":[{"id":"7","plugin_id":"custom-a","enabled":true,"routes":[{"path":"/settings","navigable":true,"navigation_kind":"admin","navigation_label":"My Admin App"},{"path":"/user","navigable":true,"navigation_kind":"user"},{"path":"/hidden","navigable":false,"navigation_kind":"admin"}]},{"id":"9","plugin_id":"disabled","enabled":false,"routes":[{"path":"/","navigable":true,"navigation_kind":"admin"}]}],"page":{"has_more":true,"next_cursor":"next"}}"""));

        var apps = await new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease);

        // Installation order, then route order; disabled installations, user routes and
        // non-navigable routes are not listed.
        Assert.Equal(new[] { (7, "/settings", "My Admin App"), (8, "/manager/*", "Custom Artwork") },
            apps.Select(app => (app.InstallationId, app.RoutePath, app.Label)));
        Assert.All(apps, app => Assert.Equal("puzzle", app.Icon));
        Assert.Equal(["GET /api/v2/admin/plugins/installations?limit=100", "GET /api/v2/admin/plugins/installations?limit=100&cursor=next"], kit.Http.Requests);
    }

    [Fact]
    public async Task MissingLabelFallsBackToPluginId()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"7","plugin_id":"custom-a","enabled":true,"routes":[{"path":"/","navigable":true,"navigation_kind":"admin"}]}],"page":{"has_more":false}}"""));
        Assert.Equal("custom-a", Assert.Single(await new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease)).Label);
    }

    [Fact]
    public async Task PluginTextIsPlainBoundedAndFreeOfBidiControls()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var label = "Safe\u202Egnp.exe\u2066 \u200Bname\r\n\tnext" + new string('x', 200);
        var body = System.Text.Json.JsonSerializer.Serialize(new { items = new[] { new { id = "7", plugin_id = "id\u202Bx", enabled = true, routes = new[] { new { path = "/", navigable = true, navigation_kind = "admin", navigation_label = label } } } }, page = new { has_more = false } });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));

        var app = Assert.Single(await new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease));

        Assert.Equal("idx", app.PluginId);
        Assert.StartsWith("Safegnp.exe name next", app.Label, StringComparison.Ordinal);
        Assert.EndsWith("\u2026", app.Label, StringComparison.Ordinal);
        Assert.True(app.Label.Length <= AdminPluginText.MaxLabelLength + 1);
        Assert.DoesNotContain(app.Label, c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
        Assert.Equal("", AdminPluginText.Plain("\u202A\u202C\u200F"));
    }

    [Theory]
    [InlineData("/../admin")]
    [InlineData("/ok/%2e%2e/admin")]
    [InlineData("/ok/%2f/admin")]
    [InlineData("//outside")]
    [InlineData("/page?token=secret")]
    [InlineData("/page#fragment")]
    [InlineData("/page/*/other")]
    [InlineData("/page/%252f")]
    [InlineData("/page\\admin")]
    [InlineData("/page with space")]
    [InlineData("relative")]
    public async Task UnsafeDescriptorsNeverBecomeRows(string path)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var body = System.Text.Json.JsonSerializer.Serialize(new { items = new[] { new { id = "7", plugin_id = "x", enabled = true, routes = new[] { new { path, navigable = true, navigation_kind = "admin" } } } }, page = new { has_more = false } });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
        Assert.Empty(await new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease));
    }

    [Fact]
    public async Task RepeatedContinuationIsRejected()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(
            request.RequestUri!.Query.Contains("cursor=next", StringComparison.Ordinal)
                ? """{"items":[{"id":"8","plugin_id":"y","enabled":true,"routes":[]}],"page":{"has_more":true,"next_cursor":"next"}}"""
                : """{"items":[{"id":"7","plugin_id":"x","enabled":true,"routes":[]}],"page":{"has_more":true,"next_cursor":"next"}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease));
    }

    [Fact]
    public async Task DuplicateInstallationRejectsTheInventoryLikeTheWebUi()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"7","plugin_id":"x","enabled":true,"routes":[]},{"id":"7","plugin_id":"y","enabled":true,"routes":[]}],"page":{"has_more":false}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("server")]
    [InlineData("profile")]
    public async Task RetiredAuthorityDiscardsDelayedInventory(string change)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => gate.Task;
        var pending = new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease);
        Change(kit, change);
        gate.SetResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("user", true)]
    [InlineData("admin", false)]
    public void ReaderCannotCaptureOrdinaryUserOrSecondaryProfile(string role, bool primary)
    {
        using var kit = AdminMonitorKit.Create(role, primary);
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        Assert.Throws<MonitorAuthorityRetiredException>(() => authority.Capture());
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task InventoryWithoutPaginationCannotPublishPartialRows()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"7","plugin_id":"x","enabled":true,"routes":[{"path":"/","navigation_kind":"admin","navigable":true}]}]}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease));
    }

    [Theory]
    [InlineData("/*", "https://silo.test/api/v2/plugin-content/plugins/7?theme=midnight-cinema")]
    [InlineData("/", "https://silo.test/api/v2/plugin-content/plugins/7/?theme=midnight-cinema")]
    [InlineData("/settings", "https://silo.test/api/v2/plugin-content/plugins/7/settings?theme=midnight-cinema")]
    [InlineData("/manager/*", "https://silo.test/api/v2/plugin-content/plugins/7/manager?theme=midnight-cinema")]
    public void StartPageIsPluginRouteHrefWithTheWebUiTheme(string route, string expected)
    {
        var scope = AdminPluginContentScope.Create("https://silo.test", new AdminPluginApp(7, "x", route, "X"));
        Assert.Equal(expected, scope.StartUri.AbsoluteUri);
        Assert.Equal("/api/v2/plugin-content", scope.ContentParent);
    }

    [Theory]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/7/settings", true)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/7", true)]
    [InlineData("https://SILO.test/api/v2/plugin-content/plugins/7/a/b?x=1", true)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugin-assets/7/app.js", true)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/70/settings", false)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/8/settings", false)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugin-assets/8/app.js", false)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/7/../../../admin/users", false)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/7/%2e%2e/%2e%2e/8/", false)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/7/a%2fb", false)]
    [InlineData("https://silo.test/api/v2/plugin-content/plugins/7/a%5cb", false)]
    [InlineData("https://silo.test/api/v2/admin/users", false)]
    [InlineData("https://silo.test/api/v2/auth/refresh", false)]
    [InlineData("https://silo.test/", false)]
    [InlineData("http://silo.test/api/v2/plugin-content/plugins/7/settings", false)]
    [InlineData("https://silo.test:8443/api/v2/plugin-content/plugins/7/settings", false)]
    [InlineData("https://user:pass@silo.test/api/v2/plugin-content/plugins/7/settings", false)]
    [InlineData("https://elsewhere.test/api/v2/plugin-content/plugins/7/settings", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("data:text/html,<p>x</p>", false)]
    [InlineData("ms-appx:///Assets/Icons/puzzle.svg", false)]
    public void DocumentsStayInsideTheInstallationsPagesAndAssets(string target, bool allowed)
    {
        var scope = AdminPluginContentScope.Create("https://silo.test", new AdminPluginApp(7, "x", "/*", "X"));
        Assert.Equal(allowed, Uri.TryCreate(target, UriKind.Absolute, out var uri) && scope.AllowsDocument(uri));
        Assert.Equal(allowed, uri != null && scope.AllowsResource(uri, "GET"));
    }

    [Fact]
    public void AssetsAreReadOnlyAndPagesAcceptPluginDefinedMethods()
    {
        var scope = AdminPluginContentScope.Create("https://silo.test", new AdminPluginApp(7, "x", "/*", "X"));
        Assert.True(scope.AllowsResource(new Uri("https://silo.test/api/v2/plugin-content/plugins/7/save"), "POST"));
        Assert.True(scope.AllowsResource(new Uri("https://silo.test/api/v2/plugin-content/plugins/7/item"), "DELETE"));
        Assert.False(scope.AllowsResource(new Uri("https://silo.test/api/v2/plugin-content/plugin-assets/7/app.js"), "POST"));
        Assert.True(scope.AllowsResource(new Uri("https://silo.test/api/v2/plugin-content/plugin-assets/7/app.js"), "HEAD"));
    }

    [Fact]
    public void ServerBasePathScopesTheContentParent()
    {
        var scope = AdminPluginContentScope.Create("https://silo.test/silo/", new AdminPluginApp(7, "x", "/*", "X"));
        Assert.Equal("https://silo.test/silo/api/v2/plugin-content/plugins/7?theme=midnight-cinema", scope.StartUri.AbsoluteUri);
        Assert.False(scope.AllowsDocument(new Uri("https://silo.test/api/v2/plugin-content/plugins/7/")));
        Assert.True(scope.IsLaunchCookiePath("/silo/api/v2/plugin-content"));
    }

    [Theory]
    [InlineData("ftp://silo.test")]
    [InlineData("https://user:pass@silo.test")]
    [InlineData("not a url")]
    public void InvalidServerAddressesAreRefused(string server) =>
        Assert.Throws<InvalidDataException>(() => AdminPluginContentScope.Create(server, new AdminPluginApp(7, "x", "/*", "X")));

    [Fact]
    public void OfficialLaunchCookieIsAcceptedAndNeverFormatted()
    {
        var scope = AdminPluginContentScope.Create("https://silo.test", new AdminPluginApp(7, "x", "/*", "X"));
        var cookie = AdminPluginAppsApi.ParseLaunchCookie(OfficialCookie + "; Secure", scope);
        Assert.NotNull(cookie);
        Assert.Equal(("silo_plugin_access", "synthetic-plugin-token", "/api/v2/plugin-content", 300, true),
            (cookie.Name, cookie.Value, cookie.Path, cookie.MaxAgeSeconds, cookie.Secure));
        Assert.DoesNotContain("synthetic-plugin-token", cookie.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-plugin-token", System.Text.Json.JsonSerializer.Serialize(cookie.ToString()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("silo_plugin_access=synthetic; Path=/; Max-Age=300; HttpOnly")]
    [InlineData("silo_plugin_access=synthetic; Path=/api; Max-Age=300; HttpOnly")]
    [InlineData("silo_plugin_access=synthetic; Path=/api/v2/plugin-content/plugins; Max-Age=300; HttpOnly")]
    [InlineData("silo_plugin_access=synthetic; Max-Age=300; HttpOnly")]
    [InlineData("silo_plugin_access=synthetic; Path=/api/v2/plugin-content; Domain=test; Max-Age=300; HttpOnly")]
    [InlineData("silo_plugin_access=synthetic; Path=/api/v2/plugin-content; HttpOnly")]
    [InlineData("silo_plugin_access=synthetic; Path=/api/v2/plugin-content; Max-Age=0; HttpOnly")]
    [InlineData("silo_plugin_access=synthetic; Path=/api/v2/plugin-content; Max-Age=999999; HttpOnly")]
    [InlineData("silo_plugin_access=; Path=/api/v2/plugin-content; Max-Age=300; HttpOnly")]
    [InlineData("silo_plugin_access=a b; Path=/api/v2/plugin-content; Max-Age=300; HttpOnly")]
    [InlineData("other_cookie=synthetic; Path=/api/v2/plugin-content; Max-Age=300; HttpOnly")]
    public void LaunchCookieNeverWidensBeyondThePluginContentParent(string header)
    {
        var scope = AdminPluginContentScope.Create("https://silo.test", new AdminPluginApp(7, "x", "/*", "X"));
        Assert.Null(AdminPluginAppsApi.ParseLaunchCookie(header, scope));
    }

    [Fact]
    public async Task LaunchPostsOnceToTheServerWithTheSessionAndReturnsOnlyTheIssuedCookie()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var seen = new List<HttpRequestMessage>();
        kit.Http.Respond = (request, _) =>
        {
            seen.Add(request);
            var response = AdminMonitorKit.Json(Launch);
            response.Headers.Add("Set-Cookie", "unrelated=1; Path=/");
            response.Headers.Add("Set-Cookie", OfficialCookie);
            return Task.FromResult(response);
        };
        var scope = AdminPluginContentScope.Create(lease.Context.BaseUrl, new AdminPluginApp(7, "x", "/*", "X"));

        var cookie = await new AdminPluginAppsApi(kit.Client, kit.Http).LaunchAsync(lease, scope);

        Assert.Equal("synthetic-plugin-token", cookie.Value);
        Assert.Equal(["POST /api/v2/auth/plugin-launch"], kit.Http.Requests);
        Assert.Equal([""], kit.Http.Bodies);
        var request = Assert.Single(seen);
        Assert.Equal("silo.test", request.RequestUri!.Host);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("p1", Assert.Single(request.Headers.GetValues("X-Profile-Id")));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RefusedLaunchKeepsTheServerStatusForTheNativeState(HttpStatusCode status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(status)
        { Content = new StringContent("""{"type":"https://silo.test/problems/denied","title":"Denied","status":0}""", Encoding.UTF8, "application/problem+json") });
        var scope = AdminPluginContentScope.Create(lease.Context.BaseUrl, new AdminPluginApp(7, "x", "/*", "X"));
        var error = await Assert.ThrowsAsync<ApiException>(() => new AdminPluginAppsApi(kit.Client, kit.Http).LaunchAsync(lease, scope));
        Assert.Equal((int)status, error.StatusCode);
    }

    [Theory]
    [InlineData("""{"expires_in":300}""", "silo_plugin_access=x; Path=/; Max-Age=300; HttpOnly")]
    [InlineData("""{"expires_in":300}""", null)]
    [InlineData("""{"other":1}""", OfficialCookie)]
    public async Task LaunchWithoutAUsableCookieOrReceiptOpensNothing(string body, string? cookie)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            var response = AdminMonitorKit.Json(body);
            if (cookie != null) response.Headers.Add("Set-Cookie", cookie);
            return Task.FromResult(response);
        };
        var scope = AdminPluginContentScope.Create(lease.Context.BaseUrl, new AdminPluginApp(7, "x", "/*", "X"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginAppsApi(kit.Client, kit.Http).LaunchAsync(lease, scope));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("server")]
    [InlineData("profile")]
    public async Task LaunchFromARetiredAuthorityIsDiscarded(string change)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => gate.Task;
        var scope = AdminPluginContentScope.Create(lease.Context.BaseUrl, new AdminPluginApp(7, "x", "/*", "X"));
        var pending = new AdminPluginAppsApi(kit.Client, kit.Http).LaunchAsync(lease, scope);
        Change(kit, change);
        var response = AdminMonitorKit.Json(Launch);
        response.Headers.Add("Set-Cookie", OfficialCookie);
        gate.SetResult(response);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("https://silo.test/", "https://silo.test/api/v2/plugin-content/plugins/7/", true)]
    [InlineData("https://silo.test/", "https://silo.test/api/v2/plugin-content/plugin-assets/7/a.js", true)]
    [InlineData("https://silo.test/", "https://silo.test/api/v2/admin/users", false)]
    [InlineData("https://silo.test/", "https://silo.test/api/v2/plugin-contentx/plugins/7/", false)]
    [InlineData("https://silo.test/", "https://silo.test/api/v2/plugin-content/plugins/7/%2e%2e/%2e%2e/%2e%2e/admin", false)]
    [InlineData("https://silo.test/", "https://other.test/api/v2/plugin-content/plugins/7/", false)]
    [InlineData("https://silo.test/silo/", "https://silo.test/silo/api/v2/plugin-content/plugins/7/", true)]
    [InlineData("https://silo.test/silo/", "https://silo.test/api/v2/plugin-content/plugins/7/", false)]
    public void ViewerCredentialsReachOnlyThePluginContentParent(string server, string target, bool covered) =>
        Assert.Equal(covered, PluginContentPaths.IsWithinContentParent(new Uri(server), new Uri(target)));

    [Fact]
    public void ViewerPageAttachesAccountHeadersOnlyInsideThePluginContentParent()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "PluginRoutePage.xaml.cs").ReplaceLineEndings("\n");
        var scopeCheck = page.IndexOf("if (!PluginContentPaths.IsWithinContentParent(origin, target))", StringComparison.Ordinal);
        var attach = page.IndexOf("apiClient.CreateAuthenticatedRequest(owner,", StringComparison.Ordinal);
        Assert.True(scopeCheck > 0 && attach > scopeCheck, "Credential headers must be gated by the plugin-content path scope.");
        Assert.Equal(1, CountOf(page, "CreateAuthenticatedRequest("));
    }

    [Fact]
    public void AdminHostGivesThePluginNoBridgeHeadersOrScripts()
    {
        var admin = ReadRepoFile("src", "SiloPlayer", "Views", "PluginRoutePage.Admin.cs").ReplaceLineEndings("\n");
        foreach (var required in new[]
        {
            "settings.AreHostObjectsAllowed = false;", "settings.IsWebMessageEnabled = false;", "settings.AreDevToolsEnabled = false;",
            "settings.AreDefaultContextMenusEnabled = false;", "settings.AreBrowserAcceleratorKeysEnabled = false;",
            "settings.IsPasswordAutosaveEnabled = false;", "settings.IsGeneralAutofillEnabled = false;",
            "options.IsInPrivateModeEnabled = true;", "core.NewWindowRequested += (_, e) => e.Handled = true;",
            "e.State = CoreWebView2PermissionState.Deny;", "core.DownloadStarting += (_, e) => { e.Cancel = true;",
            "core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;", "issued.IsHttpOnly = true;",
            "CoreWebView2WebResourceRequestSourceKinds.All",
        })
            Assert.Contains(required, admin, StringComparison.Ordinal);
        // The plugin receives the launch cookie only: no account headers, script injection or message channel.
        foreach (var forbidden in new[]
        {
            "CreateAuthenticatedRequest", "SetHeader(", "AddHostObjectToScript", "AddScriptToExecuteOnDocumentCreated",
            "ExecuteScriptAsync", "PostWebMessage", "WebMessageReceived", "Authorization", "AccessToken",
        })
            Assert.DoesNotContain(forbidden, admin, StringComparison.Ordinal);
        // The cookie is installed only after the hardened core exists (filters are attached before
        // EnsureAdminCoreAsync returns it) and right before the page loads.
        var ensure = admin.IndexOf("var core = await EnsureAdminCoreAsync(lease);", StringComparison.Ordinal);
        var install = admin.IndexOf("cookies.AddOrUpdateCookie(issued);", StringComparison.Ordinal);
        var navigate = admin.IndexOf("core.Navigate(scope.StartUri.AbsoluteUri);", StringComparison.Ordinal);
        Assert.True(ensure > 0 && ensure < install && install < navigate);
        var core = admin[admin.IndexOf("private async Task<CoreWebView2?> EnsureAdminCoreAsync", StringComparison.Ordinal)..];
        Assert.True(core.IndexOf("core.WebResourceRequested += OnAdminWebResourceRequested;", StringComparison.Ordinal) <
            core.IndexOf("_adminCore = core;", StringComparison.Ordinal));
    }

    private static void Change(AdminMonitorKit kit, string change)
    {
        if (change == "role") kit.SetRole("user");
        else if (change == "server") kit.Client.SetBaseUrl("https://replacement.test");
        else kit.Auth.SelectProfile("p2", null, new SiloPlayer.Core.Models.Auth.Profile { Id = "p2", Name = "Other", IsPrimary = true });
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
