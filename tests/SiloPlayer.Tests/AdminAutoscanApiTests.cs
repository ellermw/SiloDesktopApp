using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAutoscanApiTests : IDisposable
{
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminAutoscanApi _api;

    public AdminAutoscanApiTests()
    {
        _authority = new(_kit.Client, _kit.Auth);
        _api = new(_kit.Client);
    }

    public void Dispose() { _authority.Dispose(); _kit.Dispose(); }

    [Fact]
    public async Task SourcesWalkUsesBoundedCursorsAndReturnsOnlyCompleteData()
    {
        var pages = new Queue<string>([
            """{"items":[{"id":"s1","plugin_id":"p","capability_id":"c","delivery_mode":"poll"}],"page":{"has_more":true,"next_cursor":"next"}}""",
            """{"items":[{"id":"s2","plugin_id":"p","capability_id":"c","delivery_mode":"webhook"}],"page":{"has_more":false}}"""
        ]);
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(pages.Dequeue()));
        using var lease = _authority.Capture();
        var result = await _api.ListSourcesAsync(lease);
        Assert.True(result.IsComplete);
        Assert.Equal(["s1", "s2"], result.Items.Select(s => s.Id));
        Assert.Equal(["GET /api/v2/admin/autoscan/sources?limit=100", "GET /api/v2/admin/autoscan/sources?limit=100&cursor=next"], _kit.Http.Requests);
    }

    [Fact]
    public async Task RepeatedContinuationIsRejected()
    {
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":true,"next_cursor":"same"}}"""));
        using var lease = _authority.Capture();
        await Assert.ThrowsAsync<AdminListInconsistentException>(() => _api.ListSourcesAsync(lease));
    }

    [Fact]
    public async Task SourceWritesUseOfficialMethodsAndRejectWrongReturnedIdentity()
    {
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"other","delivery_mode":"poll"}"""));
        using var lease = _authority.Capture();
        var body = new AdminAutoscanSourceWrite { Enabled = true, DeliveryMode = "poll" };
        await Assert.ThrowsAsync<InvalidDataException>(() => _api.UpdateSourceAsync(lease, "source/1", body));
        Assert.Equal("PUT /api/v2/admin/autoscan/sources/source%2F1", Assert.Single(_kit.Http.Requests));
    }

    [Fact]
    public async Task FailedTriggerIsSentOnce()
    {
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
        using var lease = _authority.Capture();
        await Assert.ThrowsAnyAsync<Exception>(() => _api.TriggerAsync(lease));
        Assert.Equal("POST /api/v2/admin/autoscan/trigger", Assert.Single(_kit.Http.Requests));
    }
}
