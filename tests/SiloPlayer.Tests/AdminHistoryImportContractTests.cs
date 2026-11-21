using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminHistoryImportContractTests
{
    private const string Source = """{"id":"3","name":"Fixture","base_url":"https://source.invalid","source_type":"jellyfin","enabled":true,"has_admin_token":true}""";
    private static JsonElement J(string body) { using var doc = JsonDocument.Parse(body); return doc.RootElement.Clone(); }
    private static HttpResponseMessage Reply(string body, string? tag = "\"source-r1\"", HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(body, status); if (tag != null) response.Headers.TryAddWithoutValidation("ETag", tag); return response;
    }
    [Fact]
    public async Task TokenReplacementAndClearUseSourceDetailValidatorAndDedicatedRoutes()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) Assert.Equal("\"source-r1\"", request.Headers.IfMatch.Single().ToString());
            return Task.FromResult(request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Reply(Source));
        };
        var source = await api.GetImportSourceAsync(owner, "3");
        await api.SetImportTokenAsync(owner, source, "synthetic-fixture-credential"); await api.ClearImportTokenAsync(owner, source);
        Assert.Equal(["GET /api/v2/admin/history-import-sources/3", "PUT /api/v2/admin/history-imports/sources/3/token", "DELETE /api/v2/admin/history-imports/sources/3/token"], kit.Http.Requests);
        Assert.Equal("synthetic-fixture-credential", J(kit.Http.Bodies.ElementAt(1)).Text("token"));
    }
    [Fact]
    public async Task ExplicitCredentialClearAllowsAddressChangeAndBlankOmissionPreservesCredential()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply(Source)); var source = await api.GetImportSourceAsync(owner, "3");
        await api.SaveImportSourceAsync(owner, source, J("""{"name":"Fixture","base_url":"https://other.invalid","enabled":true,"admin_token":""}"""));
        Assert.Equal("", J(kit.Http.Bodies.Last()).Text("admin_token", "missing"));
        await api.SaveImportSourceAsync(owner, source, J("""{"name":"Renamed","base_url":"https://source.invalid","enabled":true}"""));
        Assert.Null(J(kit.Http.Bodies.Last()).Member("admin_token"));
    }
    [Theory]
    [InlineData("https://name:pass@source.invalid")]
    [InlineData("https://source.invalid/?api_key=synthetic")]
    [InlineData("https://source.invalid/#credential")]
    [InlineData("file:///C:/source")]
    public async Task UnsafeSourceAddressIsRejectedBeforeSend(string url)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        await Assert.ThrowsAsync<ArgumentException>(() => api.CreateImportSourceAsync(owner, JsonSerializer.SerializeToElement(new { name = "Fixture", base_url = url, source_type = "emby", enabled = true })));
        Assert.Empty(kit.Http.Requests);
    }
    [Fact]
    public async Task MappingBindsSelectedAccountAndProfileAsStringsWithoutProfileHeaderMutation()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            Assert.Equal("p1", request.Headers.GetValues("X-Profile-Id").Single());
            return Task.FromResult(Reply("""{"id":"9","source_id":"3","external_user_id":"external","silo_user_id":"7","silo_profile_id":"p7"}"""));
        };
        await api.CreateImportMappingAsync(owner, "3", "external", "Fixture External", "7", "p7");
        var body = J(kit.Http.Bodies.Single()); Assert.Equal("7", body.Text("silo_user_id")); Assert.Equal("p7", body.Text("silo_profile_id")); Assert.Equal("p1", kit.Auth.SelectedProfileId);
    }
    [Fact]
    public async Task BulkMixedOutcomesRetainAcceptedActiveAndFailedSeparately()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"accepted":1,"active":1,"failed":1,"outcomes":[{"mapping_id":"7","status":"accepted","run":{"id":"r7","terminal":false},"location":"/api/v2/admin/history-imports/runs/r7"},{"mapping_id":"8","status":"active","run":{"id":"r8","terminal":false},"location":"/api/v2/admin/history-imports/runs/r8"},{"mapping_id":"9","status":"failed"}]}"""));
        var result = await api.BulkImportAsync(owner, "3", "jellyfin", 24, true, 8760);
        Assert.Equal(["accepted", "active", "failed"], result.Rows("outcomes").Select(x => x.Text("status")));
        Assert.Equal("POST /api/v2/admin/history-imports/sources/3/bulk-run", kit.Http.Requests.Single());
    }
    [Fact]
    public async Task CancellationReceiptRemainsCancelingUntilServerSaysTerminal()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => { var response = Reply("""{"id":"r7","status":"canceling","terminal":false,"cancelable":true}""", null, HttpStatusCode.Accepted); response.Headers.Location = new Uri("/api/v2/admin/history-imports/runs/r7", UriKind.Relative); return Task.FromResult(response); };
        var result = await api.CancelImportRunAsync(owner, "r7"); Assert.False(result.Value.Flag("terminal")); Assert.Equal("canceling", result.Value.Text("status")); Assert.Single(kit.Http.Requests);
    }
    [Fact]
    public async Task SourceEditorCannotCrossServerAuthorityInstancesWithSameAccountAndEpoch()
    {
        using var first = AdminMonitorKit.Create(); using var second = AdminMonitorKit.Create(); second.Client.SetBaseUrl("https://different.invalid");
        using var a = new AdminMonitorAuthority(first.Client, first.Auth); using var b = new AdminMonitorAuthority(second.Client, second.Auth); using var ownerA = a.Capture(); using var ownerB = b.Capture();
        first.Http.Respond = (_, _) => Task.FromResult(Reply(Source)); var source = await new AdminUserResourcesApi(first.Client).GetImportSourceAsync(ownerA, "3");
        await Assert.ThrowsAsync<MonitorAuthorityRetiredException>(() => new AdminUserResourcesApi(second.Client).DeleteImportSourceAsync(ownerB, source)); Assert.Empty(second.Http.Requests);
    }
    [Fact]
    public async Task CanonicalValueWalkRejectsContractRevisionDrift()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply(kit.Http.Requests.Count == 1 ? """{"revision":28,"items":[],"page":{"has_more":true,"next_cursor":"next"}}""" : """{"revision":29,"items":[],"page":{"has_more":false}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetDeviceValuesAsync(owner, "7")); Assert.Equal(2, kit.Http.Requests.Count);
    }
}
