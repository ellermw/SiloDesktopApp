using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

// Request shapes of every plugin write, against official 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e.
public sealed class AdminPluginsCommandTests
{
    [Theory]
    [InlineData(AdminPluginCommand.Enable, "PUT", "/7", "enabled", "true")]
    [InlineData(AdminPluginCommand.Disable, "PUT", "/7", "enabled", "false")]
    [InlineData(AdminPluginCommand.Update, "POST", "/7/update", "", "")]
    [InlineData(AdminPluginCommand.Restart, "POST", "/7/restart", "", "")]
    public async Task LifecycleMatchesMethodBodyAndReturnedIdentity(AdminPluginCommand command, string method, string suffix, string key, string value)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(AdminPluginsTests.Installation.Replace("\"enabled\":true", command == AdminPluginCommand.Disable ? "\"enabled\":false" : "\"enabled\":true")));
        await new AdminPluginsApi(kit.Client).CommandAsync(lease, "7", command);
        Assert.Equal(method + " /api/v2/admin/plugins/installations" + suffix, Assert.Single(kit.Http.Requests));
        if (key.Length > 0) { using var document = JsonDocument.Parse(Assert.Single(kit.Http.Bodies)); Assert.Equal(value, document.RootElement.GetProperty(key).ToString().ToLowerInvariant()); Assert.Single(document.RootElement.EnumerateObject()); }
        else Assert.Equal("", Assert.Single(kit.Http.Bodies));
    }

    [Theory]
    [InlineData("auto")] [InlineData("notify")] [InlineData("off")]
    public async Task UpdatePolicySendsOnlyThePolicy(string policy)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(AdminPluginsTests.Installation.Replace("\"update_policy\":\"notify\"", "\"update_policy\":\"" + policy + "\"")));
        await new AdminPluginsApi(kit.Client).SetUpdatePolicyAsync(lease, "7", policy);
        using var document = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.Equal(policy, Assert.Single(document.RootElement.EnumerateObject()).Value.GetString());
    }

    [Fact]
    public async Task OnlyTheThreeOfferedPoliciesAreSent()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminPluginsApi(kit.Client).SetUpdatePolicyAsync(lease, "7", "manual")); Assert.Empty(kit.Http.Requests);
    }

    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("../users")] [InlineData("7/restart")] [InlineData("9223372036854775808")]
    public async Task UnsafeIdsNeverSend(string id)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminPluginsApi(kit.Client).UninstallAsync(lease, id)); Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task RepositoryToggleSendsOnlyEnabledAndAddSendsOfficialFields()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        const string row = "{\"id\":\"2\",\"display_name\":\"Synthetic\",\"url\":\"https://fixture.invalid/index.json\",\"enabled\":false,\"source_kind\":\"external\",\"managed\":false}";
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(row, request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK));
        var api = new AdminPluginsApi(kit.Client);
        await api.SetRepositoryEnabledAsync(lease, "2", false);
        await api.AddRepositoryAsync(lease, " Synthetic ", " https://fixture.invalid/index.json ");
        var bodies = kit.Http.Bodies.ToArray();
        using var toggle = JsonDocument.Parse(bodies[0]); Assert.Equal(["enabled"], toggle.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        using var add = JsonDocument.Parse(bodies[1]);
        Assert.Equal(["display_name", "enabled", "url"], add.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("Synthetic", add.RootElement.GetProperty("display_name").GetString()); Assert.True(add.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task ManagedRepositoryCannotBeChangedOrRemovedByTheController()
    {
        var server = new AdminPluginsTests.Server { Repositories = AdminPluginsTests.Page("{\"id\":\"1\",\"display_name\":\"Silo\",\"url\":\"\",\"enabled\":true,\"source_kind\":\"silo\",\"managed\":true}") };
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); kit.Http.Respond = server.Respond;
        using var controller = new AdminPluginsController(new(kit.Client), new(kit.Client), authority); await controller.OpenAsync();
        var managed = controller.State.Repositories.Single();
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.RemoveRepositoryAsync(managed));
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.SetRepositoryEnabledAsync(managed, false));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task AuthBindingBodyCarriesOnlyTheOfficialFieldsAndNeverTheStoredCallbackMode()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await new AdminPluginsApi(kit.Client).AuthBindingAsync(lease, "7", new() { CapabilityId = "oidc", Enabled = true, DisplayOrder = 1, AutoProvision = true, DefaultLogin = false });
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.Equal(["auto_provision", "capability_id", "default_login", "display_order", "enabled"], body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task TaskBindingEncodesCapabilityResendsTheTriggerAndReportsRestartRequired()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"restart_required\":true}"));
        var trigger = new Dictionary<string, JsonElement> { ["type"] = JsonSerializer.SerializeToElement("interval"), ["interval_ms"] = JsonSerializer.SerializeToElement(3600000) };
        Assert.True(await new AdminPluginsApi(kit.Client).TaskBindingAsync(lease, "7", "refresh:all", false, trigger));
        Assert.Contains("/task-bindings/refresh%3Aall", Assert.Single(kit.Http.Requests), StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.False(body.RootElement.GetProperty("enabled").GetBoolean()); Assert.False(body.RootElement.TryGetProperty("capability_id", out _));
        Assert.Equal("interval", body.RootElement.GetProperty("trigger").GetProperty("type").GetString());
        Assert.Equal(3600000, body.RootElement.GetProperty("trigger").GetProperty("interval_ms").GetInt32());
    }

    [Fact]
    public async Task ConnectionCheckFalseIsACompletedFailedCheck()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"success\":false,\"message\":\"Synthetic refusal\"}"));
        Assert.False((await new AdminPluginsApi(kit.Client).TestConfigAsync(lease, "7", new() { Key = "connection" })).Success); Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ConfigBodyIsJsonWithClearsExplicitAndNotReflectedFromTheModel()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await new AdminPluginsApi(kit.Client).SaveConfigAsync(lease, "7", new() { Key = "connection", Value = new() { ["host"] = JsonSerializer.SerializeToElement<string?>(null), ["port"] = JsonSerializer.SerializeToElement(8080) }, ClearSecrets = ["old_key"] });
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.Equal(["clear_secrets", "key", "value"], body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("value").GetProperty("host").ValueKind);
        Assert.Equal(8080, body.RootElement.GetProperty("value").GetProperty("port").GetInt32());
        Assert.Equal("old_key", body.RootElement.GetProperty("clear_secrets")[0].GetString());
    }

    [Fact]
    public async Task UnauthorizedMutationDoesNotRefreshOrReplay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => new AdminPluginsApi(kit.Client).UninstallAsync(lease, "7")); Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task InventoryWalkStopsAtExplicitPageCapWithoutPublishingPartialRows()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var page = 0;
        kit.Http.Respond = (_, _) =>
        {
            var offset = page++ * 100;
            var rows = string.Join(',', Enumerable.Range(1, 100).Select(i => AdminPluginsTests.Installation.Replace("\"id\":\"7\"", "\"id\":\"" + (offset + i) + "\"").Replace("fixture.plugin", "fixture.plugin" + (offset + i))));
            return Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + rows + "],\"page\":{\"has_more\":true,\"next_cursor\":\"page" + page + "\"}}"));
        };
        var error = await Assert.ThrowsAsync<AdminPluginsInventoryLimitException>(() => new AdminPluginsApi(kit.Client).InstallationsAsync(lease));
        Assert.Equal(AdminPluginsApi.MaxPages * AdminPluginsApi.PageSize, error.Limit); Assert.Equal(AdminPluginsApi.MaxPages, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task OversizedReadFailsBeforeInventoryPublication()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(new string(' ', AdminPluginsApi.MaxResponseBytes + 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginsApi(kit.Client).CatalogAsync(lease));
    }

    [Theory]
    [InlineData("a.zip", 0, AdminPluginUploadCheck.Empty)]
    [InlineData("a.zip", 1, AdminPluginUploadCheck.Accepted)]
    [InlineData("a.zip", 268435456, AdminPluginUploadCheck.Accepted)]
    [InlineData("a.zip", 268435457, AdminPluginUploadCheck.TooLarge)]
    [InlineData("", 10, AdminPluginUploadCheck.InvalidName)]
    [InlineData("bad\nname.zip", 10, AdminPluginUploadCheck.InvalidName)]
    public void UploadTypeAndSizeAreCheckedBeforeAnythingIsRead(string name, long size, AdminPluginUploadCheck expected) =>
        Assert.Equal(expected, AdminPluginsApi.CheckUpload(name, size));

    [Fact]
    public async Task OversizedUploadNeverReadsItsStreamOrSends()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await using var source = new ThrowingStream();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "big.zip", AdminPluginsApi.MaxUploadBytes + 1));
        Assert.Empty(kit.Http.Requests);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The stream must not be read.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task UploadChunksAndCompletionHaveIndependentSingleSendReceipts()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var calls = new List<string>(); const int chunk = 512 * 1024; const int size = chunk + 123;
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath; calls.Add(request.Method + " " + path);
            if (path.EndsWith("/complete")) return Task.FromResult(AdminMonitorKit.Json(AdminPluginsTests.Installation, HttpStatusCode.Created));
            var received = request.Method == HttpMethod.Post ? 0 : path.EndsWith("/0") ? 1 : 2;
            return Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new { upload_id = "synthetic-session", filename = "fixture.zip", size_bytes = size, chunk_size = chunk, total_chunks = 2, received_chunks = received, received_bytes = received == 0 ? 0 : received == 1 ? chunk : size, complete = false }),
                request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK));
        };
        await using var source = new MemoryStream(new byte[size]); var progress = new List<int>();
        await new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "fixture.zip", size, new Progress<int>(progress.Add));
        Assert.Equal(4, calls.Count); Assert.EndsWith("/chunks/0", calls[1]); Assert.EndsWith("/chunks/1", calls[2]); Assert.EndsWith("/complete", calls[3]);
    }

    [Fact]
    public async Task SmallUploadIsOneMultipartRequestNamedArchive()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(AdminPluginsTests.Installation, HttpStatusCode.Created));
        await using var source = new MemoryStream(new byte[100]);
        await new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "fixture.zip", 100);
        Assert.Equal("POST /api/v2/admin/plugins/uploads", Assert.Single(kit.Http.Requests));
        var body = Assert.Single(kit.Http.Bodies); Assert.Contains("name=archive", body); Assert.Contains("filename=fixture.zip", body);
    }

    [Fact]
    public async Task LostUploadCompletionIsNeverReplayedOrReportedAsSuccess()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        const int chunk = 512 * 1024; const int size = chunk + 1; var completes = 0;
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/complete")) { completes++; throw new HttpRequestException("synthetic uncertain completion"); }
            var received = request.Method == HttpMethod.Post ? 0 : path.EndsWith("/0") ? 1 : 2;
            return Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new { upload_id = "synthetic-session", size_bytes = size, chunk_size = chunk, total_chunks = 2, received_chunks = received, received_bytes = received == 0 ? 0 : received == 1 ? chunk : size }),
                request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK));
        };
        await using var source = new MemoryStream(new byte[size]); await Assert.ThrowsAsync<HttpRequestException>(() => new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "fixture.zip", size));
        Assert.Equal(1, completes); Assert.Equal(4, kit.Http.Requests.Count);
    }

    // ── D6: a large upload on a slow link ──────────────────────────────────

    private const int Chunk = 512 * 1024;

    private static HttpResponseMessage UploadReceipt(HttpRequestMessage request, long size)
    {
        var path = request.RequestUri!.AbsolutePath; var total = (int)((size + Chunk - 1) / Chunk);
        if (path.EndsWith("/complete")) return AdminMonitorKit.Json(AdminPluginsTests.Installation, HttpStatusCode.Created);
        var received = request.Method == HttpMethod.Post ? 0 : int.Parse(path[(path.LastIndexOf('/') + 1)..]) + 1;
        return AdminMonitorKit.Json(JsonSerializer.Serialize(new { upload_id = "synthetic-session", filename = "fixture.zip", size_bytes = size, chunk_size = Chunk, total_chunks = total, received_chunks = received, received_bytes = Math.Min((long)received * Chunk, size), complete = false }),
            request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK);
    }

    [Fact]
    public async Task ASlowButSteadyUploadIsNotCutOffByAnOverallDeadline()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        const long size = Chunk * 6L;
        // Every request answers within its own limit (1 s) while the whole upload takes longer than that limit: only a deadline over the whole upload could end it.
        kit.Http.Respond = async (request, token) => { await Task.Delay(200, token); return UploadReceipt(request, size); };
        var api = new AdminPluginsApi(kit.Client) { UploadRequestLimit = TimeSpan.FromSeconds(1) };
        await using var source = new MemoryStream(new byte[size]); var watch = System.Diagnostics.Stopwatch.StartNew();
        await api.UploadAsync(lease, source, "fixture.zip", size);
        Assert.True(watch.Elapsed > TimeSpan.FromSeconds(1)); Assert.Equal(8, kit.Http.Requests.Count);
        Assert.Equal(6, kit.Http.Requests.Count(r => r.Contains("/chunks/"))); Assert.EndsWith("/complete", kit.Http.Requests.Last());
    }

    [Fact]
    public async Task AStalledChunkEndsByItsOwnLimitAndIsSentOnlyOnce()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        const long size = Chunk * 3L;
        kit.Http.Respond = async (request, token) => { if (request.Method == HttpMethod.Put) await Task.Delay(Timeout.Infinite, token); return UploadReceipt(request, size); };
        var api = new AdminPluginsApi(kit.Client) { UploadRequestLimit = TimeSpan.FromMilliseconds(300) };
        await using var source = new MemoryStream(new byte[size]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.UploadAsync(lease, source, "fixture.zip", size));
        Assert.Equal(1, kit.Http.Requests.Count(r => r.Contains("/chunks/0"))); Assert.DoesNotContain(kit.Http.Requests, r => r.Contains("/chunks/1") || r.EndsWith("/complete"));
    }

    [Fact]
    public async Task TheCallerCanCancelAnUploadInFlight()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        const long size = Chunk * 3L; using var cancel = new CancellationTokenSource(); var inChunk = new TaskCompletionSource();
        kit.Http.Respond = async (request, token) => { if (request.Method == HttpMethod.Put) { inChunk.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } return UploadReceipt(request, size); };
        await using var source = new MemoryStream(new byte[size]);
        var upload = new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "fixture.zip", size, null, cancel.Token);
        await inChunk.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task AnUploadEndsWhenTheAuthorityIsRetiredWhateverTheLinkIsDoing()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        const long size = Chunk * 3L; var inChunk = new TaskCompletionSource();
        kit.Http.Respond = async (request, token) => { if (request.Method == HttpMethod.Put) { inChunk.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } return UploadReceipt(request, size); };
        await using var source = new MemoryStream(new byte[size]);
        var upload = new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "fixture.zip", size);
        await inChunk.Task.WaitAsync(TimeSpan.FromSeconds(10)); kit.SetRole("user");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, kit.Http.Requests.Count(r => r.Contains("/chunks/0")));
    }
}
