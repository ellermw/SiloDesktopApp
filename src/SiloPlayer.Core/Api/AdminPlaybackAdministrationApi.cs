using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>An action the page does not offer for this node (a deployment-managed node's key or deletion, an environment-managed field's change). Thrown before anything is sent.</summary>
public sealed class AdminPlaybackNodeActionNotOfferedException(string reason) : InvalidOperationException(reason);

/// <summary>Only server administration; never touches the installed player's decoder or preferences.</summary>
public sealed class AdminPlaybackAdministrationApi(SiloApiClient client)
{
    public const int MaxPages = 8;
    public const int PageSize = 200;
    private const string NodesPath = "/api/v2/admin/nodes";
    private static string NodePath(AdminPlaybackNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 1024) throw new ArgumentException("Invalid node identity.");
        return NodesPath + "/" + Uri.EscapeDataString(node.Id);
    }
    private async Task<ApiResponse<T>> Send<T>(MonitorAuthorityLease owner, HttpMethod method, string path, object? body, string? tag, CancellationToken ct)
    {
        var reprobe = path.EndsWith("/reprobe", StringComparison.Ordinal);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, ct); timeout.CancelAfter(TimeSpan.FromSeconds(reprobe ? 180 : 45));
        try
        {
            // The shared client's own 100-second timeout would end a re-probe before this deadline, so it must be the only limit.
            return await (reprobe ? client.AdminPlaybackLongSendAsync<T>(owner, method, path, body, tag, timeout.Token, 2 * 1024 * 1024)
                : client.MonitorLogsSendAsync<T>(owner, method, path, body, tag, timeout.Token, 2 * 1024 * 1024)).ConfigureAwait(false);
        }
        catch (LoggingSettingsConflictException) { throw new AdminSettingsConflictException(); }
        catch (ApiException error) { throw Reasoned(error); }
    }
    /// <summary>The settings sanitizer keeps only safe codes. A node refusal also carries the server's own reason (bounded, plain, credential-redacted at display time) so a refused write can say why.</summary>
    private static ApiException Reasoned(ApiException error)
    {
        var safe = AdminServerSettingsApi.Sanitize(error);
        return new(safe.ErrorCode, safe.Message, safe.StatusCode)
        {
            ProblemType = safe.ProblemType, ProblemTitle = Bound(error.ProblemTitle), ProblemDetail = Bound(error.ProblemDetail),
            FieldErrors = error.FieldErrors.Take(8).Select(field => new ApiFieldError(Bound(field.Location, 128), Bound(field.Code, 64) ?? "", Bound(field.Detail))).ToArray(),
        };
    }
    private static string? Bound(string? text, int max = 600) => text == null ? null : text.Length > max ? text[..max] : text;
    /// <summary>Official validation of the two fields added since the pinned page: a boolean and a list of field names. Absent means not managed (an older server).</summary>
    internal static void ValidateManaged(AdminPlaybackNode node)
    {
        if (node.EnvManaged == null) return;
        if (node.EnvManaged.Length > 16 || node.EnvManaged.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl))) throw new InvalidDataException("Invalid node environment declaration.");
    }
    public async Task<AdminPlaybackNodeWindow> ListNodesAsync(MonitorAuthorityLease owner, CancellationToken ct)
    {
        var items = new List<AdminPlaybackNode>(); var cursors = new HashSet<string>(StringComparer.Ordinal); string? cursor = null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < MaxPages; page++)
        {
            var path = NodesPath + "?limit=200" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var response = (await Send<NodePage>(owner, HttpMethod.Get, path, null, null, ct).ConfigureAwait(false)).Body;
            if (response.Items == null || response.Items.Length > PageSize) throw new InvalidDataException("Invalid node page.");
            foreach (var node in response.Items)
            {
                if (node == null || string.IsNullOrEmpty(node.Id) || !ids.Add(node.Id) || node.Type is not ("proxy" or "transcode")) throw new InvalidDataException("Invalid node identity.");
                if (node.Capabilities != null && !ValidHardware(node.Capabilities) || node.NetworkAccess?.Any(pair => pair.Value == null) == true
                    || node.LastStats?.Gpu?.Any(gpu => gpu == null) == true || node.LastStats?.System?.Disks?.Any(disk => disk == null) == true) throw new InvalidDataException("Invalid node observations.");
                ValidateManaged(node);
                node.PhysicalGpuKeys ??= []; // A JSON null (a Go nil slice) replaces the empty default; a node without a GPU report shares nothing.
                items.Add(node);
            }
            cursor = response.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor)) return new(items.AsReadOnly(), false);
            if (cursor.Length > 8192 || !cursors.Add(cursor)) throw new InvalidDataException("Invalid node pagination.");
        }
        return new(items.AsReadOnly(), true);
    }
    public async Task<AdminPlaybackNodeCapabilities> CapabilitiesAsync(MonitorAuthorityLease owner, CancellationToken ct) =>
        (await Send<AdminPlaybackNodeCapabilities>(owner, HttpMethod.Get, NodesPath + "/capabilities", null, null, ct).ConfigureAwait(false)).Body;
    public async Task<AdminPlaybackNode> CreateNodeAsync(MonitorAuthorityLease owner, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        ValidateBody(values, true);
        var response = await Send<AdminPlaybackNode>(owner, HttpMethod.Post, NodesPath, values, null, ct).ConfigureAwait(false);
        ValidateAck(response, null); return response.Body;
    }
    public async Task<AdminPlaybackNode> UpdateNodeAsync(MonitorAuthorityLease owner, AdminPlaybackNode node, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        AdminSettingsForm.RequireStrongETag(node.ConfigETag); ValidateBody(values, false);
        // A field the node declares from its environment is the node's: the server refuses a change and nothing here restates it, so it is never sent.
        if (values.Keys.FirstOrDefault(node.IsEnvManaged) is { } locked) throw new AdminPlaybackNodeActionNotOfferedException("env_managed:" + locked);
        var response = await Send<AdminPlaybackNode>(owner, HttpMethod.Put, NodePath(node), values, node.ConfigETag, ct).ConfigureAwait(false);
        ValidateAck(response, node.Id); return response.Body;
    }
    public Task DeleteNodeAsync(MonitorAuthorityLease owner, AdminPlaybackNode node, CancellationToken ct)
    {
        AdminSettingsForm.RequireStrongETag(node.ConfigETag);
        if (node.IsDeploymentManaged) throw new AdminPlaybackNodeActionNotOfferedException("deployment_managed");
        return client.AdminPlaybackDeleteAsync(owner, NodePath(node), node.ConfigETag!, ct);
    }
    public async Task<AdminPlaybackNodeCommand> CommandAsync(MonitorAuthorityLease owner, AdminPlaybackNode node, string command, CancellationToken ct)
    {
        if (command is not ("check" or "reprobe" or "drain" or "resume")) throw new ArgumentException("Unsupported node command.");
        if (command is "drain" or "resume" && node.Type != "transcode") throw new ArgumentException("Only transcode nodes drain.");
        var result = (await Send<AdminPlaybackNodeCommand>(owner, HttpMethod.Post, NodePath(node) + "/" + command, null, null, ct).ConfigureAwait(false)).Body;
        var valid = command switch
        {
            "check" => result.Healthy != null && result.HealthPersisted != null && result.ActiveJobs >= 0 && result.EgressKbps >= 0,
            "reprobe" => result.NodeId == node.Id && result.Status is "ok" or "error" && result.CapabilitiesRefreshed != null,
            _ => result.NodeId == node.Id && result.DrainRequested == (command == "drain") && result.DrainState is "none" or "draining" or "drained" && result.ActiveJobs >= 0,
        };
        if (!valid) throw new InvalidDataException("Invalid node command acknowledgement.");
        return result;
    }
    /// <summary>Never cache, return or log the issued key. The caller receives it only while its dialog owns authority.</summary>
    public async Task IssueKeyAsync(MonitorAuthorityLease owner, AdminPlaybackNode node, Action<string> reveal, CancellationToken ct)
    {
        if (node.Type != "transcode") throw new ArgumentException("Only transcode nodes have keys.");
        if (node.IsDeploymentManaged) throw new AdminPlaybackNodeActionNotOfferedException("deployment_managed");
        var result = (await Send<KeyReply>(owner, HttpMethod.Post, NodePath(node) + "/key", null, null, ct).ConfigureAwait(false)).Body;
        owner.ThrowIfRetired();
        if (string.IsNullOrWhiteSpace(result.NodeKey) || result.NodeKey.Length > 8192) throw new InvalidDataException("Invalid key acknowledgement.");
        try { reveal(result.NodeKey); } finally { result.NodeKey = ""; }
    }
    private static void ValidateAck(ApiResponse<AdminPlaybackNode> response, string? expectedId)
    {
        AdminSettingsForm.RequireStrongETag(response.ETag);
        if (string.IsNullOrEmpty(response.Body.Id) || expectedId != null && response.Body.Id != expectedId || response.Body.ConfigETag != response.ETag) throw new InvalidDataException("Invalid node configuration acknowledgement.");
    }
    private static void ValidateBody(IReadOnlyDictionary<string, object?> values, bool create)
    {
        string[] common = ["name", "url", "group", "max_jobs", "max_bandwidth_kbps", "public_url", "source_access"];
        var allowed = common.Concat(create ? new[] { "type" } : new[] { "enabled", "hw_accel_override", "hw_device_override" }).ToHashSet(StringComparer.Ordinal);
        if (values.Count == 0 || values.Keys.Any(key => !allowed.Contains(key))) throw new ArgumentException("Invalid node configuration.");
        if (create && (!values.ContainsKey("name") || !values.ContainsKey("url") || !values.ContainsKey("type"))) throw new ArgumentException("Missing node configuration.");
    }
    public async Task<AdminPlaybackSupport> SupportAsync(MonitorAuthorityLease owner, CancellationToken ct, bool detectHardware = true)
    {
        // Independent optional reads fail closed for capabilities, unknown for node availability/remake count.
        async Task<T?> Optional<T>(string path) where T : class
        {
            try { return (await Send<T>(owner, HttpMethod.Get, path, null, null, ct).ConfigureAwait(false)).Body; }
            catch (OperationCanceledException) when (!owner.IsCurrent || ct.IsCancellationRequested) { throw; }
            catch { return null; }
        }
        var library = Optional<LibraryCaps>("/api/v2/libraries/capabilities");
        var markers = Optional<MarkerCaps>("/api/v2/admin/markers/capabilities");
        var hardware = detectHardware ? Optional<AdminPlaybackHardware>("/api/v2/admin/system/hw-accel") : Task.FromResult<AdminPlaybackHardware?>(null);
        var tasks = Optional<TaskPage>("/api/v2/admin/tasks");
        AdminPlaybackNodeWindow? nodes = null;
        try { nodes = await ListNodesAsync(owner, ct).ConfigureAwait(false); } catch { }
        await Task.WhenAll(library, markers, hardware, tasks).ConfigureAwait(false);
        owner.ThrowIfRetired(); ct.ThrowIfCancellationRequested();
        var lib = await library.ConfigureAwait(false); var marker = await markers.ConfigureAwait(false);
        long? count = null;
        if (lib?.Trickplay == true)
        {
            var previews = await Optional<PreviewPage>("/api/v2/admin/trickplay/libraries").ConfigureAwait(false);
            if (previews?.Items is { Length: <= 2000 } rows && rows.All(row => row != null && row.Ready >= 0 && row.Running >= 0 && row.Pending >= 0))
                try { count = rows.Aggregate(0L, (total, row) => checked(total + row.Ready + row.Running + row.Pending)); } catch (OverflowException) { }
        }
        // An unread or oversized task list is reported as such, so the page cannot take the empty list for a reviewed one.
        var taskRows = (await tasks.ConfigureAwait(false))?.Items; var tasksRead = taskRows is { Length: <= 1000 };
        if (taskRows == null || !tasksRead) taskRows = [];
        var detection = await hardware.ConfigureAwait(false);
        if (detection != null && (string.IsNullOrWhiteSpace(detection.Resolved) || !ValidHardware(detection))) detection = null;
        return new(lib?.Trickplay == true, marker is { Allowed: true, State: "available", DetectionKindSettings: true }, detection, nodes, count,
            Array.AsReadOnly(taskRows.Where(task => task != null && MarkerTasks.Contains(task.Key)).DistinctBy(task => task.Key).ToArray())) { TasksRead = tasksRead, TrickplayRead = lib != null, DetectionKindsRead = marker != null };
    }
    public static IReadOnlyList<string> MarkerTasks { get; } = Array.AsReadOnly(new[] { "sync_markers", "detect_intro_markers", "contribute_markers" });
    public async Task<IReadOnlyList<AdminPlaybackTask>> ListMarkerTasksAsync(MonitorAuthorityLease owner, CancellationToken ct)
    {
        var response = await Send<TaskPage>(owner, HttpMethod.Get, "/api/v2/admin/tasks", null, null, ct).ConfigureAwait(false);
        if (response.Body.Items is not { Length: <= 1000 } items) throw new InvalidDataException("Invalid task collection.");
        return Array.AsReadOnly(items.Where(task => task != null && MarkerTasks.Contains(task.Key)).DistinctBy(task => task.Key).ToArray());
    }
    private static bool ValidHardware(AdminPlaybackHardware hardware) =>
        (hardware.RenderDevices == null || hardware.RenderDevices.Length <= 64 && hardware.RenderDevices.All(path => !string.IsNullOrWhiteSpace(path)))
        && (hardware.RenderDeviceDetails == null || hardware.RenderDeviceDetails.Length <= 64 && hardware.RenderDeviceDetails.All(device => device != null && !string.IsNullOrWhiteSpace(device.Path)))
        && (hardware.DetectedBackends == null || hardware.DetectedBackends.Length <= 64 && hardware.DetectedBackends.All(backend => backend != null && backend.Backend != null))
        && (hardware.Nodes == null || hardware.Nodes.Length <= 1600 && hardware.Nodes.All(node => node != null && node.NodeName != null && node.NodeUrl != null && (node.RenderDevices == null || node.RenderDevices.All(path => !string.IsNullOrWhiteSpace(path)))));
    public async Task RunMarkerTaskAsync(MonitorAuthorityLease owner, string key, CancellationToken ct)
    {
        if (!MarkerTasks.Contains(key)) throw new ArgumentException("Unsupported marker task.");
        var response = await Send<AdminPlaybackTask>(owner, HttpMethod.Post, "/api/v2/admin/tasks/" + key + "/run", null, null, ct).ConfigureAwait(false);
        if (response.Body.Key != key || response.Body.State is not ("idle" or "running" or "cancelling")) throw new InvalidDataException("Invalid task acknowledgement.");
    }
    private sealed class NodePage { public AdminPlaybackNode[]? Items { get; set; } public CursorPage? Page { get; set; } }
    private sealed class CursorPage { public string? NextCursor { get; set; } }
    private sealed class KeyReply { public string NodeKey { get; set; } = ""; public override string ToString() => nameof(KeyReply); }
    private sealed class LibraryCaps { public bool Trickplay { get; set; } }
    private sealed class MarkerCaps { public bool Allowed { get; set; } public string? State { get; set; } public bool DetectionKindSettings { get; set; } }
    private sealed class TaskPage { public AdminPlaybackTask[]? Items { get; set; } }
    private sealed class PreviewPage { public Preview[]? Items { get; set; } }
    private sealed class Preview { public long Ready { get; set; } public long Pending { get; set; } public long Running { get; set; } }
}
