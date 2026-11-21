using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdminPluginAppsTests
{
    [Fact]
    public async Task AdminOnlyInstallationsPublishCustomLabelsAndRoutesAcrossPages()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(
            request.RequestUri!.Query.Contains("cursor=next", StringComparison.Ordinal)
                ? """{"items":[{"id":"8","plugin_id":"custom-b","enabled":true,"routes":[{"path":"/manager/*","navigable":true,"navigation_kind":"admin","navigation_label":"Custom Artwork"}]}],"page":{"has_more":false}}"""
                : """{"items":[{"id":"7","plugin_id":"custom-a","enabled":true,"routes":[{"path":"/settings","navigable":true,"navigation_kind":"admin","navigation_label":"My Admin App"},{"path":"/user","navigable":true,"navigation_kind":"user"}]},{"id":"9","plugin_id":"disabled","enabled":false,"routes":[{"path":"/","navigable":true,"navigation_kind":"admin"}]}],"page":{"has_more":true,"next_cursor":"next"}}"""));

        var apps = await new AdminPluginAppsApi(kit.Client).GetAppsAsync(lease);

        Assert.Equal(new[] { (7, "/settings", "My Admin App"), (8, "/manager/*", "Custom Artwork") },
            apps.Select(app => (app.InstallationId, app.RoutePath, app.Label)));
        Assert.All(apps, app => Assert.Equal("puzzle", app.Icon));
        Assert.All(kit.Http.Requests, request => Assert.Contains("/api/v2/admin/plugins/installations", request));
        Assert.Equal(2, kit.Http.Requests.Count);
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

    [Theory]
    [InlineData("/../admin")]
    [InlineData("/ok/%2e%2e/admin")]
    [InlineData("//outside")]
    [InlineData("/page?token=secret")]
    [InlineData("/page#fragment")]
    [InlineData("/page/*/other")]
    [InlineData("/page/%252f")]
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
        if (change == "role") kit.SetRole("user");
        else if (change == "server") kit.Client.SetBaseUrl("https://replacement.test");
        else kit.Auth.SelectProfile("p2", null, new SiloPlayer.Core.Models.Auth.Profile { Id = "p2", Name = "Other", IsPrimary = true });
        gate.SetResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task VirtualHostCannotAttachCredentialsToOtherOriginsInstallationsOrServerApis()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        using var content = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        foreach (var uri in new[] { "https://foreign.test/api/v2/plugin-content/plugins/7/", "https://silo.test/api/v2/admin/users",
            new Uri(content.Origin, "/api/v2/admin/users").AbsoluteUri,
            new Uri(content.Origin, "/api/v2/plugin-content/plugins/8/").AbsoluteUri,
            new Uri(content.Origin, "/api/v2/plugin-content/plugins/7/%252fadmin").AbsoluteUri })
            await Assert.ThrowsAsync<InvalidDataException>(() => content.SendAsync(new Uri(uri), "GET", new Dictionary<string, string>(), null, true));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task ContentUsesCapturedAuthorityStripsCookiesAndDoesNotReplayWrites()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) =>
        {
            Assert.Equal("silo.test", request.RequestUri!.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.False(request.Headers.Contains("Cookie"));
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("synthetic") };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "fixture=secret");
            return Task.FromResult(response);
        };
        using var content = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        var response = await content.SendAsync(content.StartUri, "POST", new Dictionary<string, string> { ["Cookie"] = "browser", ["Authorization"] = "untrusted" }, new MemoryStream([1, 2]), true);
        Assert.Equal(503, response.Status);
        Assert.DoesNotContain("Set-Cookie", response.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content-Security-Policy:", response.Headers);
        Assert.Contains("worker-src 'none'", response.Headers);
        Assert.Single(kit.Http.Requests);
        kit.SetRole("user");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => content.SendAsync(content.StartUri, "GET", new Dictionary<string, string>(), null, true));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task RedirectsAreReturnedOnlyWithinTheVirtualInstallation()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var location = "/api/v2/plugin-content/plugins/7/subpage";
        kit.Http.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Content = new StringContent("") };
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        };
        using var content = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        var response = await content.SendAsync(content.StartUri, "GET", new Dictionary<string, string>(), null, true);
        Assert.Contains("Location: " + new Uri(content.Origin, location), response.Headers);
        location = "https://foreign.test/leak";
        await Assert.ThrowsAsync<InvalidDataException>(() => content.SendAsync(content.StartUri, "GET", new Dictionary<string, string>(), null, true));
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task StaticAssetsCannotBeNavigatedOrMutatedAndOriginsAreUniquePerPage()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        using var first = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        using var second = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        Assert.NotEqual(first.Origin, second.Origin);
        var asset = new Uri(first.Origin, "/api/v2/plugin-content/plugin-assets/7/pixel.svg");
        Assert.False(first.CanNavigate(asset));
        Assert.True(first.CanNavigate(first.StartUri));
        await Assert.ThrowsAsync<InvalidDataException>(() => first.SendAsync(asset, "POST", new Dictionary<string, string>(), null, false));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task OversizedContentIsRejectedBeforeBufferingAndWithoutRetry()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0]) };
            response.Content.Headers.ContentLength = 16L * 1024 * 1024 + 1;
            return Task.FromResult(response);
        };
        using var content = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        await Assert.ThrowsAsync<InvalidDataException>(() => content.SendAsync(content.StartUri, "GET", new Dictionary<string, string>(), null, true));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task RedirectCannotResubmitAnAlreadySentWrite()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Content = new StringContent("") };
            response.Headers.Location = new Uri("/api/v2/plugin-content/plugins/7/write-target", UriKind.Relative);
            return Task.FromResult(response);
        };
        using var content = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        await Assert.ThrowsAsync<IOException>(() => content.SendAsync(content.StartUri, "POST", new Dictionary<string, string>(), new MemoryStream([1]), true));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ContentResponseFromRetiredAuthorityCannotReviveAfterRegrant()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => gate.Task;
        using var content = new AdminPluginContentSession(kit.Client, lease, App(), kit.Http);
        var pending = content.SendAsync(content.StartUri, "GET", new Dictionary<string, string>(), null, true);
        kit.SetRole("user"); kit.SetRole("admin");
        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("retired bytes") });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(kit.Http.Requests);
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

    private static AdminPluginApp App() => new(7, "synthetic-plugin", "/custom/*", "Custom Admin App");
}
