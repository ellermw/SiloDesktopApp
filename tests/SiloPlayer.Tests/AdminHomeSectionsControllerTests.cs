using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminHomeSectionsControllerTests
{
    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var text = path.EndsWith("/capabilities") ? """{"available":true,"preview":true,"web_only":true}"""
            : path.EndsWith("/recipes") ? """{"categories":[]}"""
            : path.EndsWith("/order") ? """{"scope":"home","library_id":null,"ordered_ids":["a"]}"""
            : path.EndsWith("/a") ? """{"id":"a","scope":"home","title":"A","section_type":"recently_added","enabled":true,"item_limit":20,"config":{}}"""
            : """{"items":[{"id":"a","scope":"home","title":"A","section_type":"recently_added","enabled":true,"item_limit":20,"config":{}}]}""";
        var response = AdminMonitorKit.Json(text); response.Headers.ETag = EntityTagHeaderValue.Parse("\"r1\""); return response;
    }

    [Fact]
    public async Task UnknownWriteLocksMutationUntilExplicitReviewReload()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (r, _) => Task.FromResult(Respond(r));
        using var controller = new AdminHomeSectionsController(new(kit.Client), authority);
        await controller.OpenAsync(); Assert.True(controller.CanWrite);
        var session = await controller.OpenEditAsync("a");
        kit.Http.Respond = (r, _) => r.Method == HttpMethod.Patch ? throw new HttpRequestException("lost") : Task.FromResult(Respond(r));
        await controller.SaveAsync(session!, AdminHomeSectionDraft.From(session!.Row));
        Assert.True(controller.State.ReviewRequired); Assert.False(controller.CanWrite);
        var writes = kit.Http.Requests.Count(r => r.StartsWith("PATCH "));
        await controller.SaveAsync(session, AdminHomeSectionDraft.From(session.Row));
        Assert.Equal(writes, kit.Http.Requests.Count(r => r.StartsWith("PATCH ")));
        await controller.ReloadAsync(); Assert.True(controller.CanWrite); Assert.False(controller.State.ReviewRequired);
    }

    [Fact]
    public async Task RetirementDropsRowsAndCannotReopenAStaleEditor()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (r, _) => Task.FromResult(Respond(r));
        using var controller = new AdminHomeSectionsController(new(kit.Client), authority);
        await controller.OpenAsync(); var session = await controller.OpenEditAsync("a");
        authority.Retire(); Assert.Null(controller.State.Snapshot); Assert.False(controller.CanWrite);
        await controller.SaveAsync(session!, AdminHomeSectionDraft.From(session!.Row));
        Assert.DoesNotContain(kit.Http.Requests, r => r.StartsWith("PATCH "));
    }
}
