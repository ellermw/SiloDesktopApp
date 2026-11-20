using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Official prepared-file administration wire contracts at Server 66b836f82.</summary>
public sealed class AdminPreparedFilesApiTests
{
    private static readonly string File = """{"id":"art-1","state":"cached","format":"transcode","location":"node:9","location_name":"gpu-1","media_file_id":"42","title":"Dune","media_type":"movie","container":"mp4","video_codec":"h264","audio_codec":"aac","bytes":1200,"created_at":"2026-10-08T09:30:00Z","last_used_at":"2026-10-08T09:30:00Z","waiting":0,"downloading":0,"finished":1,"stale_waiting":0}""";
    private static readonly string Device = """{"user_id":"7","username":"maya","profile_id":"p1","profile_name":"Maya","device_id":"phone","device_name":"Pixel","platform":"android","stale":true,"copies":2,"finished":1,"waiting":1,"failed":0,"revoked":0,"bytes_on_device":1200,"monitors":1}""";
    private static readonly string Event = """{"id":"e1","reason":"cache_expired","location":"server","location_name":"Server","occurred_at":"2026-10-08T09:30:00Z","count":1,"bytes":1200,"titles":["Dune"]}""";

    [Fact]
    public async Task ReadsSendOfficialBoundedFiltersSortAndCursor()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/downloads/storage/files" => $$$"""{"items":[{{{File}}}],"page":{"has_more":false}}""",
            "/api/v2/admin/downloads/devices" => $$$"""{"items":[{{{Device}}}],"page":{"has_more":false}}""",
            "/api/v2/admin/downloads/storage/events" => $$$"""{"items":[{{{Event}}}],"page":{"has_more":false}}""",
            _ => "{}",
        }));
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);

        await api.ListFilesAsync(lease, new AdminPreparedFileQuery
        {
            Location = "node:9", State = "cached", Format = "transcode", Search = "  Dune &  ", Sort = "last_used",
        }, "a/b+c");
        Assert.Equal("GET /api/v2/admin/downloads/storage/files?location=node%3A9&state=cached&format=transcode&q=Dune%20%26&sort=last_used&limit=50&cursor=a%2Fb%2Bc", kit.Http.Requests.Last());

        await api.ListDevicesAsync(lease, new AdminDownloadDeviceQuery { Search = " Maya ", Stale = true, Sort = "size" }, "d2");
        Assert.Equal("GET /api/v2/admin/downloads/devices?q=Maya&stale=true&sort=size&limit=50&cursor=d2", kit.Http.Requests.Last());

        await api.ListHistoryAsync(lease, new AdminDownloadHistoryQuery { Reason = "revoked", Location = "device", Days = 0 }, "h2");
        Assert.Equal("GET /api/v2/admin/downloads/storage/events?reason=revoked&location=device&days=0&limit=50&cursor=h2", kit.Http.Requests.Last());
    }

    [Fact]
    public async Task StorageFilesystemAcronymsBindToTheOfficialSnakeCaseMembers()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"locations":[{"key":"server","kind":"server","name":"Server","enabled":true,"online":true,"dir":"/prepared","dir_source":"setting","usage":{"measured_at":"2026-10-08T09:30:00Z","files":1,"bytes":10,"partial_files":0,"partial_bytes":0,"other_bytes":0,"fs_used_bytes":60,"fs_total_bytes":100,"shares_scratch":false,"ephemeral":false,"stale":false},"in_use_files":1,"in_use_bytes":10,"cached_files":0,"cached_bytes":0,"waiting_downloads":0,"stale_waiting_bytes":0,"untracked_files":0,"untracked_bytes":0,"budget_bytes":1000,"budget_source":"setting","cleanup_backlog":0,"storage_full":false,"replicas_disagree":false}],"cache_hours":72,"disk_ceiling_percent":85,"default_budget_bytes":1000,"stale_device_days":14,"preparing_jobs":0,"freed_last_30_days_bytes":0}"""));
        using var lease = authority.Capture();

        var storage = await new AdminPreparedFilesApi(kit.Client).GetStorageAsync(lease);

        var usage = Assert.Single(storage.Locations).Usage!;
        Assert.Equal(60, usage.FSUsedBytes);
        Assert.Equal(100, usage.FSTotalBytes);
    }

    [Theory]
    [InlineData("storage", "{}")]
    [InlineData("preparations", "{}")]
    [InlineData("history", "{\"items\":[{\"id\":\"e1\",\"reason\":\"revoked\",\"location\":\"device\",\"occurred_at\":\"2026-10-08T09:30:00Z\",\"count\":1,\"bytes\":1}],\"page\":{\"has_more\":false}}")]
    [InlineData("preparation-action", "{}")]
    [InlineData("file-delete", "{}")]
    [InlineData("revoke", "{\"revoked\":1,\"bytes\":1,\"paused_monitors\":0}")]
    public async Task MissingRequiredWireCollectionsFailClosed(string operation, string response)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(response));
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);

        Func<Task> run = operation switch
        {
            "storage" => () => api.GetStorageAsync(lease),
            "preparations" => () => api.ListPreparationsAsync(lease),
            "history" => () => api.ListHistoryAsync(lease, new(), null),
            "preparation-action" => () => api.RunPreparationActionAsync(lease, "pause", ["job-1"]),
            "file-delete" => () => api.DeleteFilesAsync(lease, ["file-1"], includeInUse: false),
            _ => () => api.RevokeAsync(lease, AdminDownloadRevokeTarget.ForIds(["download-1"]), ""),
        };

        await Assert.ThrowsAsync<InvalidDataException>(run);
    }

    [Theory]
    [InlineData("files", """{"items":[FILE],"page":{"has_more":true}}""")]
    [InlineData("files", """{"items":[FILE,FILE],"page":{"has_more":false}}""")]
    [InlineData("devices", """{"items":[DEVICE,DEVICE],"page":{"has_more":false}}""")]
    [InlineData("history", """{"items":[EVENT,EVENT],"page":{"has_more":false}}""")]
    public async Task InconsistentCursorPagesAndDuplicateIdentitiesFailClosed(string kind, string template)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(template.Replace("FILE", File).Replace("DEVICE", Device).Replace("EVENT", Event)));
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);

        Func<Task> run = kind switch
        {
            "files" => () => (Task)api.ListFilesAsync(lease, new(), null),
            "devices" => () => (Task)api.ListDevicesAsync(lease, new(), null),
            _ => () => (Task)api.ListHistoryAsync(lease, new(), null),
        };
        var error = await Assert.ThrowsAsync<AdminListInconsistentException>(run);
        Assert.Equal(AdminFailureKind.InvalidResponse, AdminFailures.Classify(error));
    }

    [Fact]
    public async Task ActionsUseExactOfficialEndpointsBodiesAndNeverBatchOverFiveHundred()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/downloads/preparations/pause" => ActionResults(kit.Http.Bodies.Last(), "applied"),
            "/api/v2/admin/downloads/storage/files/delete" => DeleteResults(kit.Http.Bodies.Last()),
            "/api/v2/admin/downloads/revoke" => "{\"revoked\":1,\"bytes\":2,\"paused_monitors\":0,\"download_ids\":[\"d1\"]}",
            "/api/v2/admin/downloads/storage/locations/node:9/cleanup" => "{\"freed_bytes\":3}",
            "/api/v2/admin/downloads/storage/locations/node:9/untracked/delete" => "{\"files\":1,\"bytes\":4,\"failed_files\":0}",
            _ => "{}",
        }));
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);
        var ids = Enumerable.Range(1, 501).Select(i => "id-" + i).ToArray();

        await api.RunPreparationActionAsync(lease, "pause", ids);
        Assert.Equal(2, kit.Http.Requests.Count(r => r == "POST /api/v2/admin/downloads/preparations/pause"));
        Assert.Contains("\"id-500\"", kit.Http.Bodies.ToArray()[^2]);
        Assert.DoesNotContain("\"id-501\"", kit.Http.Bodies.ToArray()[^2]);
        Assert.Equal("{\"ids\":[\"id-501\"]}", kit.Http.Bodies.ToArray()[^1]);

        await api.DeleteFilesAsync(lease, ids, includeInUse: true);
        Assert.Equal(2, kit.Http.Requests.Count(r => r == "POST /api/v2/admin/downloads/storage/files/delete"));
        Assert.Contains("\"include_in_use\":true", kit.Http.Bodies.Last());

        await api.RevokeAsync(lease, AdminDownloadRevokeTarget.ForIds(["d1"]), " Lost phone ");
        Assert.Equal("POST /api/v2/admin/downloads/revoke", kit.Http.Requests.Last());
        Assert.Equal("{\"ids\":[\"d1\"],\"reason\":\"Lost phone\"}", kit.Http.Bodies.Last());

        await api.CleanupLocationAsync(lease, "node:9");
        await api.DeleteUntrackedAsync(lease, "node:9");
        Assert.EndsWith("POST /api/v2/admin/downloads/storage/locations/node:9/untracked/delete".Replace("node:9", "node%3A9"), kit.Http.Requests.Last());
        Assert.All(kit.Http.Requests.Where(r => r.StartsWith("POST ")), request => Assert.DoesNotContain("C:\\", request));
    }

    [Fact]
    public async Task ActionsRejectDuplicateIdsAndAcknowledgementsForUnrequestedRows()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"results\":[{\"id\":\"other\",\"outcome\":\"applied\"}]}"));
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);

        await Assert.ThrowsAsync<ArgumentException>(() => api.RunPreparationActionAsync(lease, "pause", ["same", "same"]));
        Assert.Empty(kit.Http.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => api.DeleteFilesAsync(lease, ["../etc"], includeInUse: false));
        Assert.Empty(kit.Http.Requests);
        await Assert.ThrowsAsync<InvalidDataException>(() => api.RunPreparationActionAsync(lease, "pause", ["requested"]));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ActionsRejectTruncatedAcknowledgements()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"results\":[]}"));
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);

        await Assert.ThrowsAsync<InvalidDataException>(() => api.RunPreparationActionAsync(lease, "pause", ["job-1"]));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.DeleteFilesAsync(lease, ["file-1"], includeInUse: false));
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task NodeLocationUpdateRequiresAndReturnsTheSameStrongConfigurationValidator()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        string? match = null;
        kit.Http.Respond = (request, _) =>
        {
            match = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            var response = AdminMonitorKit.Json("{\"id\":\"9\",\"name\":\"gpu-1\",\"type\":\"transcode\",\"url\":\"https://node\",\"enabled\":true,\"healthy\":true,\"active_jobs\":0,\"egress_kbps\":0,\"created_at\":\"2026-01-01T00:00:00Z\",\"config_etag\":\"\\\"v2\\\"\",\"download_artifact_dir_override\":\"/prepared\",\"download_artifact_max_bytes_override\":1500000000}");
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            return Task.FromResult(response);
        };
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);
        var node = new AdminPreparedFilesNode { Id = "9", ConfigETag = "\"v1\"" };

        var saved = await api.UpdateNodeLocationAsync(lease, node, new AdminNodeLocationChange(true, "/prepared", true, 1_500_000_000));
        Assert.Equal("PUT /api/v2/admin/nodes/9", kit.Http.Requests.Single());
        Assert.Equal("\"v1\"", match);
        Assert.Equal("\"v2\"", saved.ETag);
        Assert.Equal("\"v2\"", saved.Value.ConfigETag);
        Assert.Equal("{\"download_artifact_dir_override\":\"/prepared\",\"download_artifact_max_bytes_override\":1500000000}", kit.Http.Bodies.Single());

        node.ConfigETag = "W/\"weak\"";
        await Assert.ThrowsAsync<AdminValidatorException>(() => api.UpdateNodeLocationAsync(lease, node, new AdminNodeLocationChange(true, null, false, null)));
    }

    [Fact]
    public async Task NodeLocationUpdateSendsOnlyTheChangedFieldsAndAnExplicitNullToInherit()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) =>
        {
            var response = AdminMonitorKit.Json("{\"id\":\"9\",\"name\":\"gpu-1\",\"type\":\"transcode\",\"url\":\"https://node\",\"enabled\":true,\"healthy\":true,\"active_jobs\":0,\"egress_kbps\":0,\"created_at\":\"2026-01-01T00:00:00Z\",\"config_etag\":\"\\\"v2\\\"\"}");
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            return Task.FromResult(response);
        };
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);
        var node = new AdminPreparedFilesNode { Id = "9", ConfigETag = "\"v1\"" };

        await api.UpdateNodeLocationAsync(lease, node, new AdminNodeLocationChange(true, "  ", false, null));
        Assert.Equal("{\"download_artifact_dir_override\":null}", kit.Http.Bodies.Last());
        await api.UpdateNodeLocationAsync(lease, node, new AdminNodeLocationChange(false, null, true, null));
        Assert.Equal("{\"download_artifact_max_bytes_override\":null}", kit.Http.Bodies.Last());
        await api.UpdateNodeLocationAsync(lease, node, new AdminNodeLocationChange(false, null, true, 0));
        Assert.Equal("{\"download_artifact_max_bytes_override\":0}", kit.Http.Bodies.Last());

        var sent = kit.Http.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => api.UpdateNodeLocationAsync(lease, node, new AdminNodeLocationChange(false, null, false, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => api.UpdateNodeLocationAsync(lease, node, new AdminNodeLocationChange(true, "relative/path", false, null)));
        Assert.Equal(sent, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task TransportFailureAfterSendLogsOnlyAFixedCategoryAndNeverEchoesTitlesPathsOrNames()
    {
        var directory = Path.Combine(TestRepository.Root, ".codex-tmp", "prepared-files-transport", Guid.NewGuid().ToString("N"));
        using var operation = LocalLog.BeginOperation("prepared-files-transport-fixture", directory);
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("echoed Alien <Directors> /srv/prepared/maya-phone host.internal:8443"));
        using var lease = authority.Capture();
        var api = new AdminPreparedFilesApi(kit.Client);

        await Assert.ThrowsAnyAsync<Exception>(() => api.RevokeAsync(lease, AdminDownloadRevokeTarget.ForIds(["d1"]), "Lost phone of maya"));
        await Assert.ThrowsAnyAsync<Exception>(() => api.ListFilesAsync(lease, new AdminPreparedFileQuery { Search = "Alien" }, null));
        // The node save carries the directory override in its body, so it records only the same category.
        await Assert.ThrowsAnyAsync<Exception>(() => api.UpdateNodeLocationAsync(lease, new AdminPreparedFilesNode { Id = "9", ConfigETag = "\"v1\"" }, new AdminNodeLocationChange(true, "/srv/prepared/maya-private", false, null)));
        await Assert.ThrowsAnyAsync<Exception>(() => api.ListNodesAsync(lease));

        var diagnostics = System.IO.File.ReadAllText(Path.Combine(directory, "api_diagnostics.txt"));
        Assert.Contains("error=admin_downloads_nodes_transport_failed", diagnostics);
        Assert.DoesNotContain("maya-private", diagnostics);
        Assert.DoesNotContain("Alien", diagnostics);
        Assert.DoesNotContain("/srv/prepared", diagnostics);
        Assert.DoesNotContain("maya", diagnostics);
        Assert.DoesNotContain("host.internal", diagnostics);
        Assert.DoesNotContain("HttpRequestException", diagnostics);
        Assert.Single(kit.Http.Requests, request => request.StartsWith("POST /api/v2/admin/downloads/revoke", StringComparison.Ordinal));
    }

    private static string ActionResults(string requestBody, string outcome)
    {
        using var request = System.Text.Json.JsonDocument.Parse(requestBody);
        var rows = request.RootElement.GetProperty("ids").EnumerateArray()
            .Select(id => "{\"id\":" + System.Text.Json.JsonSerializer.Serialize(id.GetString()) + ",\"outcome\":\"" + outcome + "\"}");
        return "{\"results\":[" + string.Join(',', rows) + "]}";
    }

    private static string DeleteResults(string requestBody)
    {
        using var request = System.Text.Json.JsonDocument.Parse(requestBody);
        var rows = request.RootElement.GetProperty("ids").EnumerateArray()
            .Select(id => "{\"id\":" + System.Text.Json.JsonSerializer.Serialize(id.GetString()) + ",\"outcome\":\"deleted\",\"bytes\":1}");
        return "{\"results\":[" + string.Join(',', rows) + "]}";
    }
}
