using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Reflection deliberately lets the test-only checkpoint compile on Beta and fail an assertion for the missing family.</summary>
public sealed class AdminRequestsManagementTests
{
    private static object Api(SiloApiClient client)
    {
        var type = typeof(SiloApiClient).Assembly.GetType("SiloPlayer.Core.Api.AdminRequestsApi");
        Assert.NotNull(type);
        return Activator.CreateInstance(type!, client)!;
    }

    private static async Task<object?> Call(object api, string method, params object?[] args)
    {
        var member = api.GetType().GetMethod(method)!;
        Assert.NotNull(member);
        var parameters = member.GetParameters();
        var supplied = parameters.Select((p, i) => i < args.Length ? args[i] : p.DefaultValue).ToArray();
        Task task;
        try { task = (Task)member.Invoke(api, supplied)!; }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static HttpResponseMessage Tagged(string json, string tag = "\"r1\"", HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(json, status);
        response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }

    [Fact]
    public async Task Queue_reads_one_bounded_filtered_page_and_preserves_opaque_cursor()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[{\"id\":\"7\",\"title\":\"Synthetic\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"next/+\"}}"));
        var page = (JsonObject)(await Call(Api(kit.Client), "ListQueueAsync", owner, "needs_approval", " a & b ", "series", "8", null))!;
        Assert.Single(page["items"]!.AsArray());
        var path = Assert.Single(kit.Http.Requests);
        Assert.Contains("view=needs_approval", path);
        Assert.Contains("limit=50", path);
        Assert.Contains("q=a%20%26%20b", path);
        Assert.Contains("requested_by_user_id=8", path);
        Assert.Contains("media_type=series", path);
    }

    [Theory]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":true}}")]
    [InlineData("{\"items\":[{\"id\":\"7\"},{\"id\":\"7\"}],\"page\":{\"has_more\":false}}")]
    [InlineData("{\"items\":[{}],\"page\":{\"has_more\":false}}")]
    public async Task Queue_refuses_incomplete_or_duplicate_pages(string json)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(json));
        var api = Api(kit.Client);
        await Assert.ThrowsAnyAsync<Exception>(() => Call(api, "ListQueueAsync", owner, "failed", "", "", null, null));
    }

    [Fact]
    public async Task Settings_save_uses_exact_read_validator_and_does_not_send_absent_watchlist_setting()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        var api = Api(kit.Client);
        string? validator = null;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put) validator = request.Headers.IfMatch.Single().ToString();
            return Task.FromResult(Tagged("{\"requests_enabled\":true,\"global_max_requests\":5,\"global_window_days\":7,\"global_auto_approval_enabled\":false,\"force_dual_quality\":false}"));
        };
        var document = await Call(api, "GetSettingsAsync", owner);
        await Call(api, "SaveDocumentAsync", owner, document, JsonNode.Parse("{\"requests_enabled\":false,\"global_max_requests\":0,\"global_window_days\":1,\"global_auto_approval_enabled\":true,\"force_dual_quality\":true}")!.AsObject());
        Assert.Equal("\"r1\"", validator);
        Assert.DoesNotContain("watchlist_requests", kit.Http.Bodies.Last());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(412)]
    [InlineData(503)]
    public async Task Administrative_writes_are_never_replayed(int status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"about:blank\",\"title\":\"Refused\",\"status\":" + status + "}", (HttpStatusCode)status));
        var api = Api(kit.Client);
        await Assert.ThrowsAnyAsync<Exception>(() => Call(api, "ActionAsync", owner, "7", "approve", null));
        Assert.Equal("POST /api/v2/admin/requests/7/approve", Assert.Single(kit.Http.Requests));
    }

    [Fact]
    public async Task Late_counts_from_a_retired_admin_cannot_publish()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => gate.Task;
        var pending = Call(Api(kit.Client), "GetCountsAsync", owner);
        kit.SetRole("user");
        gate.SetResult(AdminMonitorKit.Json("{\"needs_approval\":9,\"in_progress\":0,\"failed\":0,\"done\":0}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task A_document_read_by_a_retired_authority_cannot_save_under_a_replacement_lease()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var old = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"rows\":[],\"max_rows\":30}"));
        var api = Api(kit.Client);
        var document = await Call(api, "GetLayoutAsync", old);
        authority.Retire();
        using var replacement = authority.Capture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(api, "SaveDocumentAsync", replacement, document, new JsonObject { ["rows"] = new JsonArray() }));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("W/\"weak\"")]
    [InlineData("")]
    public async Task Editor_reads_without_a_strong_validator_fail_closed(string tag)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"rows\":[],\"max_rows\":30}", tag));
        var api = Api(kit.Client);
        await Assert.ThrowsAnyAsync<Exception>(() => Call(api, "GetLayoutAsync", owner));
    }

    [Fact]
    public async Task Retired_lease_cannot_even_begin_an_administrative_write()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        authority.Retire();
        var api = Api(kit.Client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(api, "ActionAsync", owner, "7", "decline", "Synthetic reason"));
        Assert.Empty(kit.Http.Requests);
    }
}
