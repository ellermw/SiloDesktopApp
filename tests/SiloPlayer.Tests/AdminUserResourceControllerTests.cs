using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminUserResourceControllerTests
{
    [Fact]
    public async Task SupersededReadCancelsItsLeaseAndCannotPublish()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); MonitorAuthorityLease? firstOwner = null;
        var first = controller.ReadAsync("detail", owner => { firstOwner = owner; return gate.Task; });
        Assert.Equal("current", await controller.ReadAsync("detail", owner => { Assert.False(firstOwner!.IsCurrent); return Task.FromResult("current"); }));
        gate.SetResult("stale"); Assert.Null(await first);
    }
    [Fact]
    public async Task ConcurrentWriteDoesNotQueueOrReplay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var sends = 0;
        var pending = controller.WriteAsync(_ => { sends++; return gate.Task; });
        Assert.False(await controller.WriteAsync(_ => { sends++; return Task.CompletedTask; }));
        gate.SetResult(); Assert.True(await pending); Assert.Equal(1, sends);
    }
    [Theory]
    [InlineData(412)] [InlineData(428)]
    public async Task ConflictBarrierSurvivesReadsUntilExplicitReview(int status)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        Assert.False(await controller.WriteAsync(_ => throw new ApiException("conflict", "fixture", status))); Assert.True(controller.NeedsReview);
        Assert.Equal("fresh", await controller.ReadAsync("detail", _ => Task.FromResult("fresh")));
        Assert.False(await controller.WriteAsync(_ => Task.CompletedTask)); controller.BeginReview();
        Assert.False(controller.NeedsReview); Assert.True(await controller.WriteAsync(_ => Task.CompletedTask));
    }
    [Fact]
    public async Task UnknownWriteBlocksResubmission()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open(); var sends = 0;
        Assert.False(await controller.WriteAsync(_ => { sends++; throw new AdminResourceOutcomeUnknownException(); }));
        Assert.False(await controller.WriteAsync(_ => { sends++; return Task.CompletedTask; })); Assert.Equal(1, sends); Assert.True(controller.NeedsReview);
    }
    [Fact]
    public async Task RetiredWriteCannotPoisonReplacementState()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var pending = controller.WriteAsync(_ => gate.Task);
        controller.Suspend(); controller.Open(); gate.SetException(new AdminResourceOutcomeUnknownException());
        Assert.False(await pending); Assert.False(controller.NeedsReview); Assert.Null(controller.Error);
    }
    [Fact]
    public async Task CompleteConfigurationRejectsDuplicateRowsAcrossPages()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(kit.Http.Requests.Count == 1 ? """{"items":[{"id":"2"}],"page":{"has_more":true,"next_cursor":"next"}}""" : """{"items":[{"id":"2"}],"page":{"has_more":false}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ConfigurationAsync(lease, cursor => api.ListGroupsAsync(lease, cursor)));
    }
    [Fact]
    public async Task MemberMoveRequiresConfirmedGroupAndAccountValidator()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); using var confirmed = JsonDocument.Parse("""{"id":"7","role":"user","access_group_id":"2","requests_allowed":null}""");
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"7","role":"user","access_group_id":"3","requests_allowed":null}"""));
        var fresh = (await api.GetMemberAsync(lease, "7")) with { ETag = "\"account-r1\"" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.MoveMemberAsync(lease, confirmed.RootElement, fresh, "4")); Assert.Single(kit.Http.Requests);
    }
    [Theory]
    [InlineData("android tv", "tv")] [InlineData("iPad", "tablet")] [InlineData("iOS", "mobile")] [InlineData("Windows", "desktop")] [InlineData("unclassified", "unknown")]
    public void PlatformClassificationMatchesOfficialSource(string source, string expected) => Assert.Equal(expected, AdminDevicePresentation.Platform(source));
    [Fact]
    public async Task MappingDeleteUsesItsCanonicalValidator()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture(); var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Delete) { Assert.Equal("\"mapping-r1\"", request.Headers.IfMatch.Single().ToString()); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            var reply = AdminMonitorKit.Json("""{"id":"9","source_id":"3","external_user_id":"external","silo_user_id":"7","silo_profile_id":"p7"}"""); reply.Headers.TryAddWithoutValidation("ETag", "\"mapping-r1\""); return Task.FromResult(reply);
        };
        await api.DeleteImportMappingAsync(lease, await api.GetImportMappingAsync(lease, "9"));
        Assert.Equal(["GET /api/v2/admin/history-imports/mappings/9", "DELETE /api/v2/admin/history-imports/mappings/9"], kit.Http.Requests);
    }
}
