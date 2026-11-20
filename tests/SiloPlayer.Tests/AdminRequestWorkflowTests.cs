using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminRequestWorkflowTests
{
    [Theory]
    [InlineData("pending", "active", "needs_approval", "approve,decline")]
    [InlineData("approved", "active", "in_progress", "cancel")]
    [InlineData("queued", "active", "in_progress", "")]
    [InlineData("downloading", "active", "in_progress", "")]
    [InlineData("completed", "active", "done", "")]
    [InlineData("approved", "failed", "failed", "retry,cancel")]
    [InlineData("pending", "declined", "done", "")]
    [InlineData("pending", "cancelled", "done", "")]
    public void Queue_actions_follow_official_view_and_withdrawal_rules(string status, string outcome, string view, string actions)
    {
        var row = new JsonObject { ["status"] = status, ["outcome"] = outcome, ["targets"] = new JsonArray() };
        Assert.Equal(view, AdminRequestRules.View(row));
        Assert.Equal(actions, string.Join(",", AdminRequestRules.Actions(row)));
    }

    [Fact]
    public void An_approved_request_with_a_sent_target_cannot_be_cancelled()
    {
        var row = JsonNode.Parse("{\"status\":\"approved\",\"outcome\":\"active\",\"targets\":[{\"status\":\"queued\"}]}")!;
        Assert.Empty(AdminRequestRules.Actions(row));
        row["targets"]![0]!["status"] = "failed";
        Assert.Equal(new[] { "cancel" }, AdminRequestRules.Actions(row));
    }

    [Fact]
    public void Discover_replacement_excludes_read_only_source_and_translation_metadata()
    {
        var row = JsonNode.Parse("{\"id\":\"row\",\"type\":\"mdblist\",\"title\":\"Synthetic\",\"enabled\":true,\"limit\":20,\"list\":{\"url\":\"https://mdblist.com/lists/synthetic/example\"},\"source\":{\"state\":\"ready\"},\"title_key\":\"private.key\",\"title_args\":{}}")!.AsObject();
        var body = AdminRequestRules.WriteBody("request-discover/layout", new JsonObject { ["rows"] = new JsonArray(row) });
        var sent = body["rows"]![0]!.AsObject();
        Assert.False(sent.ContainsKey("source")); Assert.False(sent.ContainsKey("title_key")); Assert.False(sent.ContainsKey("title_args"));
        Assert.Equal("mdblist", sent["type"]!.ToString());
    }

    [Fact]
    public void Empty_saved_secret_is_omitted_and_response_only_fields_are_not_written()
    {
        var body = AdminRequestRules.WriteBody("request-integrations/new", JsonNode.Parse("{\"id\":\"i1\",\"name\":\"Synthetic\",\"enabled\":true,\"base_url\":\"router.test:8989\",\"installation_id\":\"7\",\"capability_id\":\"arr\",\"api_key_ref\":\"\",\"has_api_key\":true,\"last_check_error\":\"old\",\"supported_media_types\":[],\"plugin_config\":{}}")!.AsObject());
        Assert.Equal("http://router.test:8989", body["base_url"]!.ToString());
        foreach (var readOnly in new[] { "id", "has_api_key", "last_check_error", "api_key_ref" }) Assert.False(body.ContainsKey(readOnly));
    }

    [Fact]
    public async Task Opaque_integration_and_request_ids_are_escaped_without_a_numeric_assumption()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var api = new AdminRequestsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"id\":\"integration-1\"}"));
        var current = await api.GetIntegrationAsync(owner, "integration-1");
        Assert.Equal("integration-1", current.Value["id"]!.ToString());
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"id\":\"request/1\"}"));
        await api.ActionAsync(owner, "request/1", "approve");
        Assert.Contains(kit.Http.Requests, call => call.Contains("request%2F1/approve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_document_cannot_be_adopted_by_another_authority_with_the_same_epoch_and_account()
    {
        using var first = AdminMonitorKit.Create(); using var a = new AdminMonitorAuthority(first.Client, first.Auth); using var old = a.Capture();
        first.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"mode\":\"standard\"}"));
        var document = await new AdminRequestsApi(first.Client).GetRoutingAsync(old);
        using var second = AdminMonitorKit.Create(); using var b = new AdminMonitorAuthority(second.Client, second.Auth); using var replacement = b.Capture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AdminRequestsApi(second.Client).SaveDocumentAsync(replacement, document, new JsonObject { ["mode"] = "advanced" }));
        Assert.Empty(second.Http.Requests);
    }

    [Fact]
    public async Task Bulk_actions_bound_concurrency_and_retain_each_failure_without_replay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var active = 0; var peak = 0; var calls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = async (request, token) =>
        {
            var id = request.RequestUri!.Segments[^2].Trim('/'); calls.AddOrUpdate(id, 1, (_, value) => value + 1);
            var count = Interlocked.Increment(ref active); InterlockedExtensions.Max(ref peak, count); if (count == 4) entered.TrySetResult();
            await gate.Task.WaitAsync(token); Interlocked.Decrement(ref active);
            if (id == "2") throw new HttpRequestException("Synthetic lost answer");
            return AdminMonitorKit.Json("{\"id\":\"" + id + "\"}");
        };
        var rows = Enumerable.Range(1, 9).Select(index => new JsonObject { ["id"] = index.ToString(), ["title"] = "Synthetic " + index, ["status"] = "pending", ["outcome"] = "active" }).ToArray();
        var pending = new AdminRequestsController(new AdminRequestsApi(kit.Client)).BulkAsync(owner, rows, "approve", null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(4, calls.Count); gate.SetResult();
        var result = await pending;
        Assert.Equal(9, result.Done); Assert.Equal(4, peak); Assert.Equal("2", Assert.Single(result.Failures).Id); Assert.All(calls.Values, value => Assert.Equal(1, value));
    }

    [Fact]
    public async Task Count_store_coalesces_readers_and_clears_after_authority_retirement()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => gate.Task;
        using var store = new AdminRequestCountStore(new AdminRequestsApi(kit.Client), authority);
        var first = store.GetAsync(owner); var second = store.GetAsync(owner);
        Assert.Single(kit.Http.Requests);
        gate.SetResult(AdminMonitorKit.Json("{\"needs_approval\":3,\"in_progress\":2,\"failed\":1,\"done\":9}"));
        await Task.WhenAll(first, second); Assert.Equal(3, store.Current!.Count);
        authority.Retire(); Assert.Null(store.Current);
    }

    private static HttpResponseMessage Tagged(string body) { var response = AdminMonitorKit.Json(body); response.Headers.TryAddWithoutValidation("ETag", "\"synthetic\""); return response; }

    [Fact]
    public async Task Route_order_requires_the_complete_canonical_set_and_excludes_fallback()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/request-routes")) return Task.FromResult(AdminMonitorKit.Json("{\"items\":[{\"id\":\"a\"},{\"id\":\"b\"},{\"id\":\"fallback-movie\"}],\"page\":{\"has_more\":false}}"));
            var id = path.Split('/')[^1];
            return Task.FromResult(Tagged("{\"id\":\"" + id + "\",\"media_type\":\"movie\",\"is_fallback\":" + (id == "fallback-movie" ? "true" : "false") + "}"));
        };
        var api = new AdminRequestsApi(kit.Client); var snapshot = await api.ListRoutesAsync(owner);
        await Assert.ThrowsAsync<ArgumentException>(() => api.ReorderRoutesAsync(owner, snapshot, "movie", ["a"]));
        Assert.DoesNotContain(kit.Http.Requests, call => call.StartsWith("POST"));
        await api.ReorderRoutesAsync(owner, snapshot, "movie", ["b", "a"]);
        Assert.Equal("POST /api/v2/admin/request-routes/order", kit.Http.Requests.Last());
        var sent = JsonNode.Parse(kit.Http.Bodies.Last())!;
        Assert.Equal("movie", sent["media_type"]!.ToString()); Assert.Equal(new[] { "b", "a" }, sent["ids"]!.AsArray().Select(value => value!.ToString()));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(422)]
    public async Task Success_and_problem_bodies_share_the_four_MiB_bound(int status)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(new string('x', 4 * 1024 * 1024 + 1), (HttpStatusCode)status));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminRequestsApi(kit.Client).GetCapabilitiesAsync(owner));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task Connection_options_normalize_a_host_address_and_leave_saved_credentials_unspecified()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"options\":{}}"));
        await new AdminRequestsApi(kit.Client).LoadOptionsAsync(owner, "integration-1", new JsonObject { ["base_url"] = "router.test:7878", ["name"] = "not a probe field" });
        var body = JsonNode.Parse(kit.Http.Bodies.Last())!.AsObject();
        Assert.Equal("http://router.test:7878", body["base_url"]!.ToString()); Assert.False(body.ContainsKey("name")); Assert.False(body.ContainsKey("api_key_ref"));
    }

    [Fact]
    public async Task Missing_settings_flags_fail_closed_before_an_editor_can_replace_them_with_false()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"global_max_requests\":5,\"global_window_days\":7}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminRequestsApi(kit.Client).GetSettingsAsync(owner));
        Assert.Single(kit.Http.Requests);
    }
    private static class InterlockedExtensions
    {
        internal static void Max(ref int location, int value) { int before; do { before = Volatile.Read(ref location); if (before >= value) return; } while (Interlocked.CompareExchange(ref location, value, before) != before); }
    }
}
