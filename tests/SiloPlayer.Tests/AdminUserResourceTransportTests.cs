using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Definite refusals versus unknown outcomes, and what the admin is told. Synthetic values only.</summary>
public sealed class AdminUserResourceTransportTests
{
    private const string Group = """{"id":"2","name":"Family","is_default":false,"library_ids":null,"allowed_permissions":[],"max_profiles":5}""";
    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK, string? etag = "\"r1\"")
    {
        var response = AdminMonitorKit.Json(body, status);
        if (etag != null) response.Headers.TryAddWithoutValidation("ETag", etag);
        return response;
    }

    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(422)] [InlineData(429)]
    public async Task EveryOtherRefusalIsDefiniteAndLeavesTheAdminFreeToCorrectAndRetry(int status)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminUserResourcesApi(kit.Client);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"title":"Rejected","detail":"Pick a smaller number.","status":400}""", (HttpStatusCode)status, null));
        Assert.False(await controller.WriteAsync(owner => api.CreateGroupAsync(owner, "Family")));
        Assert.False(controller.NeedsReview); // a refusal needs no reload: the draft stays and the admin can retry
        Assert.NotNull(controller.Error);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"id":"5","name":"Family"}""", HttpStatusCode.Created));
        Assert.True(await controller.WriteAsync(owner => api.CreateGroupAsync(owner, "Family"))); // the retry is allowed and sent
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)] [InlineData(HttpStatusCode.InternalServerError)] [InlineData(HttpStatusCode.BadGateway)] [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TimeoutOrServerFaultAfterSendingAWriteIsUnknownAndBlocksFurtherWrites(HttpStatusCode status)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply("{}", status, null));
        using var controller = new AdminUserResourceController(authority); controller.Open();
        Assert.False(await controller.WriteAsync(owner => api.CreateGroupAsync(owner, "Family")));
        Assert.True(controller.NeedsReview); Assert.Single(kit.Http.Requests);
        Assert.False(await controller.WriteAsync(owner => api.CreateGroupAsync(owner, "Family"))); Assert.Single(kit.Http.Requests); // never replayed
    }

    [Fact]
    public async Task AServerFaultOnAReadIsAnOrdinaryFailureNotAnUnknownWrite()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply("{}", HttpStatusCode.InternalServerError, null));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => api.GetGroupAsync(lease, "2"));
        Assert.IsNotType<AdminResourceOutcomeUnknownException>(error);
    }

    [Fact]
    public async Task ACreateAnsweredWithoutAUsableIdIsUnknownNeverNothingSent()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"name":"Family"}""", HttpStatusCode.Created));
        await Assert.ThrowsAsync<AdminResourceOutcomeUnknownException>(() => api.CreateGroupAsync(lease, "Family")); Assert.Single(kit.Http.Requests);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"id":"0","name":"Family"}""", HttpStatusCode.Created));
        await Assert.ThrowsAsync<AdminResourceOutcomeUnknownException>(() => api.CreateGroupAsync(lease, "Family"));
    }

    [Fact]
    public async Task RefusalShowsTheServersSafeDetailAndFieldErrorsAndDropsSecretsAndAddresses()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"type":"https://silo.example/docs/api/v2/problems/validation_failed","title":"Invalid","detail":"Name is already used.","status":422,"errors":[{"location":"body.name","code":"duplicate","detail":"Another group has this name."}]}""", HttpStatusCode.UnprocessableEntity, null));
        var refusal = await Assert.ThrowsAsync<ApiException>(() => api.CreateGroupAsync(lease, "Family"));
        Assert.Equal(422, refusal.StatusCode); Assert.Equal("validation_failed", refusal.ProblemType);
        var shown = AdminUserResourceText.Failure(refusal);
        Assert.Contains("Name is already used.", shown); Assert.Contains("Another group has this name.", shown);
        Assert.Equal("body.name", refusal.FieldErrors.Single().Location);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"title":"Invalid","detail":"Could not reach https://source.invalid with token abc","status":400,"errors":[{"location":"body.base_url","code":"unreachable","detail":"password rejected by https://source.invalid"}]}""", HttpStatusCode.BadRequest, null));
        var unsafeRefusal = await Assert.ThrowsAsync<ApiException>(() => api.CreateGroupAsync(lease, "Family"));
        var text = AdminUserResourceText.Failure(unsafeRefusal);
        Assert.DoesNotContain("source.invalid", text + unsafeRefusal.Message + unsafeRefusal.ToString());
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("The server rejected these values. Review the fields and selected identities.", text);
    }

    [Fact]
    public async Task AFailedSectionReadShowsItsFailureAndTheNextSuccessfulReadOfThatSectionClearsIt()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        Assert.Null(await controller.ReadAsync<string>("runs", _ => throw new HttpRequestException("synthetic")));
        Assert.NotNull(controller.Error); Assert.False(controller.NeedsReview);
        Assert.Equal("kept", await controller.ReadAsync("mappings", _ => Task.FromResult("kept"))); Assert.NotNull(controller.Error); // another section's success does not hide it
        Assert.Equal("fresh", await controller.ReadAsync("runs", _ => Task.FromResult("fresh"))); Assert.Null(controller.Error);
    }

    [Fact]
    public async Task ASupersededReadsLateFailureRaisesNoError()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = controller.ReadAsync("poll", _ => gate.Task);
        Assert.Equal("newer", await controller.ReadAsync("poll", _ => Task.FromResult("newer")));
        gate.SetException(new HttpRequestException("late")); Assert.Null(await first); Assert.Null(controller.Error);
    }

    [Fact]
    public async Task AFollowUpFailureNamesWhatWasNotSavedAndHonorsTheInnerReloadRule()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        Assert.False(await controller.WriteAsync(_ => throw new AdminResourceFollowUpException("web.admin.access.groups.limitNotSavedAfterGroup", new ApiException("validation_failed", "x", 422))));
        Assert.False(controller.NeedsReview); Assert.Contains("request", controller.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.False(await controller.WriteAsync(_ => throw new AdminResourceFollowUpException("web.admin.access.groups.limitNotSaved", new AdminResourceOutcomeUnknownException())));
        Assert.True(controller.NeedsReview);
    }
}
