using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminPlaybackBackendState { Verified, Failed, Unverified, None }

public sealed record AdminPlaybackBackendBadge(string Label, AdminPlaybackBackendState State, string Title);
public sealed record AdminPlaybackGpuFailure(string Label, string Reason);
public sealed record AdminPlaybackLiveGpu(string Key, string Label, string Busy, int? BusyFill, bool BusyMuted, string Sessions, string Title);
public sealed record AdminPlaybackGpuBlock(bool Awaiting, string AwaitingLabel, string AwaitingTitle, AdminPlaybackBackendBadge? Backend,
    IReadOnlyList<AdminPlaybackGpuFailure> Failures, string? DeviceSummary, string? DeviceTitle, string? Stale, IReadOnlyList<AdminPlaybackLiveGpu> Live);
public sealed record AdminPlaybackNodeGroup(string Value, string Label, int Count, bool Degraded, string Title);
public sealed record AdminPlaybackDrainLine(string Phase, string Label, string Detail);
public sealed record AdminPlaybackLabelTitle(string Label, string Title);
public sealed record AdminPlaybackDeviceRow(string Path, string Description, bool Reported, bool Selected, string Title);

/// <summary>Official adminNodesPresentation.ts (8ae4f4f5a) as far as the native Nodes cards use it. Third-party text stays plain text.</summary>
public static class AdminPlaybackNodePresentation
{
    public static readonly TimeSpan CapabilityStaleAfter = TimeSpan.FromMinutes(10);
    private static string T(string key, object? arguments = null) => AdminPlaybackAdministrationText.T(key, arguments);
    private static double? Finite(double? value) => value is { } number && double.IsFinite(number) ? number : null;
    private static int Clamp(double value) => (int)Math.Min(100, Math.Max(0, Math.Round(value, MidpointRounding.AwayFromZero)));
    private static string P(string? value) => AdminPlaybackNodeText.Plain(value, 300);
    /// <summary>A backend name the node reported (a badge label, a probe title, a re-probe message): upper case, one line of plain text, at most 64 characters.</summary>
    private static string BackendLabel(string? name) => AdminPlaybackNodeText.Plain(name?.Trim().ToUpperInvariant(), 64);
    private static DateTimeOffset? Parse(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    // ---- Dashboard-shared readings --------------------------------------------------------------------------------------

    public static DashboardNode ToDashboard(AdminPlaybackNode node) => new()
    {
        Id = node.Id, Name = node.Name, Type = node.Type, Group = node.Group, Enabled = node.Enabled, Healthy = node.Healthy, ActiveJobs = node.ActiveJobs,
        MaxJobs = node.MaxJobs, EgressKbps = node.EgressKbps, MaxBandwidthKbps = node.MaxBandwidthKbps, DrainRequested = node.DrainRequested == true, DrainState = node.DrainState,
        LastHealthCheck = Parse(node.LastHealthCheck),
        LastStats = node.LastStats == null ? null : new DashboardNodeStats { SampledAt = Parse(node.LastStats.SampledAt), System = node.LastStats.System, Gpu = node.LastStats.Gpu?.ToList(), Attribution = node.LastStats.Attribution },
    };
    public static DashboardNodeState State(AdminPlaybackNode node) => DashboardPresentation.NodeState(ToDashboard(node));
    public static string StateLabel(AdminPlaybackNode node) => DashboardPresentation.NodeStateLabel(State(node));
    public static DashboardMetric Jobs(AdminPlaybackNode node) => DashboardPresentation.NodeJobs(ToDashboard(node));
    public static DashboardMetric Egress(AdminPlaybackNode node) => DashboardPresentation.NodeEgress(ToDashboard(node));
    /// <summary>Official nodeReportsAcceleration: a proxy reports no hardware, so it has no acceleration block and no re-probe.</summary>
    public static bool ReportsAcceleration(AdminPlaybackNode node) => node.Type != "proxy";

    // ---- System block (CPU, memory, disk, network, process) -------------------------------------------------------------

    public sealed record SystemBlock(bool Reported, string Title, bool Stale, IReadOnlyList<DashboardMetric> Rows);

    public static SystemBlock Host(AdminPlaybackNode node, DateTimeOffset now)
    {
        var dashboard = ToDashboard(node); var sample = DashboardPresentation.NodeSystem(dashboard, now);
        if (!sample.Reported) return new(false, sample.Title, sample.Title == T("web.admin.nodes.system.staleTitle"), []);
        var system = node.LastStats!.System!; var rows = new List<DashboardMetric> { sample.Cpu!, sample.Memory!, sample.Disk! };
        rows.Add(Network(system));
        if (node.LastStats.Attribution?.Process is { } process && process.ResidentBytes is { } resident && double.IsFinite(resident))
            rows.Add(new(T("web.admin.nodes.process.residentLabel"), DashboardFormat.FileSize(resident, "0 B", iec: true), T("web.admin.nodes.process.residentDetail"), T("web.admin.nodes.process.residentTitle"), false, false, null));
        else if (node.LastStats.Attribution != null)
            rows.Add(new(T("web.admin.nodes.process.residentLabel"), "—", "", T("web.admin.nodes.process.unmeasured"), true, false, null));
        return new(true, "", false, rows);
    }

    private static DashboardMetric Network(DashboardSystemStats system)
    {
        var rx = DashboardFormat.BitsPerSecond(system.NetRxBps); var tx = DashboardFormat.BitsPerSecond(system.NetTxBps); var label = T("web.admin.nodes.metric.network");
        if (rx == null && tx == null) return new(label, "—", "", T("web.admin.nodes.network.noReading"), true, false, null);
        return new(label, $"↓ {rx ?? "—"} · ↑ {tx ?? "—"}", T("web.admin.nodes.network.detail"), T("web.admin.nodes.network.title", new { rx = rx ?? "—", tx = tx ?? "—" }), false, false, null);
    }

    // ---- Acceleration block -------------------------------------------------------------------------------------------

    public static AdminPlaybackGpuBlock Gpu(AdminPlaybackNode node, DateTimeOffset now)
    {
        var capabilities = node.Capabilities;
        if (capabilities == null) return new(true, T("web.admin.nodes.gpu.awaitingLabel"), T("web.admin.nodes.gpu.awaitingTitle"), null, [], null, null, null, []);
        var resolved = capabilities.Resolved?.Trim().ToLowerInvariant() ?? ""; var detected = capabilities.DetectedBackends ?? [];
        var (summary, title) = RenderDevices(capabilities);
        return new(false, "", "", Backend(resolved, detected), OtherFailures(resolved, detected), summary, title, Staleness(node, now), LiveGpus(node));
    }

    private static AdminPlaybackBackendBadge Backend(string resolved, AdminPlaybackBackend[] detected)
    {
        if (resolved is "" or "none")
        {
            var software = T("web.admin.nodes.backend.softwareBadge"); var skipped = detected.Count(entry => entry.Skipped);
            return new(software, AdminPlaybackBackendState.None, T(skipped > 0 && skipped == detected.Length ? "web.admin.nodes.backend.devicesInaccessible" : "web.admin.nodes.backend.noneVerified"));
        }
        var label = BackendLabel(resolved); var entry = Find(resolved, detected);
        if (entry == null) return new(label, AdminPlaybackBackendState.Unverified, T("web.admin.nodes.backend.noProbe", new { backend = label }));
        if (!entry.Verified) return new(label, AdminPlaybackBackendState.Failed, T("web.admin.nodes.backend.probeFailed", new { backend = label, reason = Reason(entry) }));
        var device = P(entry.Device?.Trim());
        return new(label, AdminPlaybackBackendState.Verified, device.Length > 0 ? T("web.admin.nodes.backend.verifiedOnDevice", new { backend = label, device }) : T("web.admin.nodes.backend.verified", new { backend = label }));
    }

    private static AdminPlaybackBackend? Find(string resolved, AdminPlaybackBackend[] detected) => detected.FirstOrDefault(entry => entry.Backend?.Trim().ToLowerInvariant() == resolved);
    private static string Reason(AdminPlaybackBackend entry) => entry.Reason?.Trim() is { Length: > 0 } reason ? P(reason) : T("web.admin.nodes.backend.noReason");

    /// <summary>Official describeEffectiveAcceleration: prose under the override select; null before the first report.</summary>
    public static string? EffectiveAcceleration(AdminPlaybackNode node)
    {
        if (node.Capabilities == null) return null;
        var resolved = node.Capabilities.Resolved?.Trim().ToLowerInvariant() ?? "";
        if (resolved is "" or "none") return T("web.admin.nodes.effective.software");
        var label = BackendLabel(resolved); var entry = Find(resolved, node.Capabilities.DetectedBackends ?? []);
        if (entry == null) return T("web.admin.nodes.effective.unverified", new { backend = label });
        if (!entry.Verified) return T("web.admin.nodes.effective.probeFailed", new { backend = label });
        var device = P(entry.Device?.Trim());
        return device.Length > 0 ? T("web.admin.nodes.effective.verifiedOnDevice", new { backend = label, device }) : T("web.admin.nodes.effective.verified", new { backend = label });
    }

    private static IReadOnlyList<AdminPlaybackGpuFailure> OtherFailures(string resolved, AdminPlaybackBackend[] detected) => detected
        .Where(entry => !entry.Verified && !entry.Skipped && (entry.Backend?.Trim().ToLowerInvariant() ?? "") is { Length: > 0 } backend && backend != resolved)
        .Select(entry => new AdminPlaybackGpuFailure(BackendLabel(entry.Backend), Reason(entry))).ToArray();

    /// <summary>contradicted / unreported / unconfirmed, or null when the stored report can be trusted as current.</summary>
    public static string? Staleness(AdminPlaybackNode node, DateTimeOffset now)
    {
        if (!node.Healthy || Parse(node.CapabilitiesRefreshedAt) == null) return null;
        var advertised = node.AdvertisedCapabilitiesHash?.Trim();
        if (advertised != null && advertised != (node.CapabilitiesHash?.Trim() ?? "")) return advertised.Length == 0 ? "unreported" : "contradicted";
        return Parse(node.LastHealthCheck) is { } check && now - check > CapabilityStaleAfter ? "unconfirmed" : null;
    }

    private static (string? Summary, string? Title) RenderDevices(AdminPlaybackHardware capabilities)
    {
        var details = capabilities.RenderDeviceDetails ?? [];
        if (details.Length > 0)
        {
            var counted = new List<(string Label, int Count)>();
            foreach (var device in details)
            {
                var label = DeviceLabel(device); var index = counted.FindIndex(entry => entry.Label == label);
                if (index >= 0) counted[index] = (label, counted[index].Count + 1); else counted.Add((label, 1));
            }
            return (string.Join(", ", counted.Select(entry => entry.Count > 1 ? T("web.admin.nodes.gpu.deviceMultiple", new { count = entry.Count, count_text = entry.Count, device = entry.Label }) : entry.Label)),
                string.Join("\n", details.Select(DeviceLine)));
        }
        var paths = (capabilities.RenderDevices ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        return paths.Length == 0 ? (null, null) : (T("web.admin.nodes.gpu.renderDeviceCount", new { count = paths.Length, count_text = paths.Length }), string.Join("\n", paths.Select(P)));
    }

    private static string DeviceLabel(AdminPlaybackDevice device) => P(!string.IsNullOrWhiteSpace(device.Description) ? device.Description.Trim() : !string.IsNullOrWhiteSpace(device.Path) ? device.Path.Trim() : T("web.admin.nodes.gpu.genericDevice"));
    private static string DeviceLine(AdminPlaybackDevice device)
    {
        var path = !string.IsNullOrWhiteSpace(device.Path) ? device.Path.Trim() : T("web.admin.nodes.gpu.unknownPath");
        var line = string.Join(" — ", new[] { path, device.Description?.Trim() }.Where(part => !string.IsNullOrEmpty(part)));
        return P(device.PciAddress?.Trim() is { Length: > 0 } address ? $"{line} ({address})" : line);
    }

    private static IReadOnlyList<AdminPlaybackLiveGpu> LiveGpus(AdminPlaybackNode node)
    {
        if (!node.Healthy) return [];
        var details = node.Capabilities?.RenderDeviceDetails ?? [];
        return (node.LastStats?.Gpu ?? []).Select(stats => LiveGpu(stats, details)).ToArray();
    }

    private static AdminPlaybackLiveGpu LiveGpu(DashboardGpuStats stats, AdminPlaybackDevice[] details)
    {
        var device = stats.Device?.Trim() ?? "";
        var detail = details.FirstOrDefault(candidate => device != "" && candidate.Path?.Trim() == device) ?? details.FirstOrDefault(candidate => device != "" && candidate.PciAddress?.Trim() == device);
        var source = stats.Source?.Trim().ToLowerInvariant() ?? ""; var measured = source != "" && source != "unavailable";
        var video = Finite(stats.VideoBusyPct); var sessions = Math.Max(0, (int)Math.Truncate(Finite(stats.Sessions) ?? 0));
        var path = device != "" ? device : detail?.Path?.Trim() ?? "";
        var label = path == "" ? T("web.admin.nodes.gpu.genericDevice") : path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
        var lines = new List<string> { string.Join(" — ", new[] { device != "" ? device : detail?.Path?.Trim() is { Length: > 0 } p ? p : T("web.admin.nodes.gpu.unknownDevice"), detail?.Description?.Trim() }.Where(part => !string.IsNullOrEmpty(part))) };
        if (measured)
        {
            var engines = new[] { Engine("web.admin.nodes.gpu.videoEngine", stats.VideoBusyPct), Engine("web.admin.nodes.gpu.renderEngine", stats.RenderBusyPct) }.Where(part => part != null).ToArray();
            if (engines.Length > 0) lines.Add(string.Join(" · ", engines));
        }
        else lines.Add(T("web.admin.nodes.gpu.unmeasuredDevice"));
        if (Finite(stats.TotalBusyPct) is { } whole) lines.Add(T("web.admin.nodes.gpu.wholeGpu", new { percent = Clamp(whole) }));
        if (Finite(stats.VramTotalMb) is { } total && total > 0 && Finite(stats.VramUsedMb) is { } used)
            lines.Add(T("web.admin.nodes.gpu.vram", new { amount = T("web.admin.nodes.resources.usedOfTotal", new { used = DashboardFormat.FileSize(used * 1048576, "0 B", iec: true), total = DashboardFormat.FileSize(total * 1048576, iec: true) }) }));
        if (stats.Source?.Trim() is { Length: > 0 } sourceName) lines.Add(T("web.admin.nodes.gpu.source", new { source = sourceName }));
        return new(path == "" ? "gpu" : path, P(label), measured && video != null ? Clamp(video.Value) + "%" : "—", measured && video != null ? Clamp(video.Value) : null, !measured || video == null,
            sessions == 0 ? T("web.admin.nodes.gpu.idle") : T("web.admin.nodes.gpu.sessionCount", new { count = sessions, count_text = sessions }), string.Join("\n", lines.Select(P)));
        static string? Engine(string key, double? value) => Finite(value) is { } percent ? T(key, new { percent = Clamp(percent) }) : null;
    }

    // ---- Shared GPU, drift, override ------------------------------------------------------------------------------------

    public static AdminPlaybackLabelTitle? SharedGpu(AdminPlaybackNode node, IReadOnlyList<AdminPlaybackNode> all)
    {
        var keys = (node.PhysicalGpuKeys ?? []).Where(key => !string.IsNullOrEmpty(key)).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0) return null;
        var others = all.Where(candidate => candidate.Id != node.Id && (candidate.PhysicalGpuKeys ?? []).Any(keys.Contains)).Select(candidate => P(candidate.Name)).ToArray();
        return others.Length == 0 ? null : new(T("web.admin.nodes.sharedGpu.label"), T("web.admin.nodes.sharedGpu.title", new { nodes = string.Join(", ", others) }));
    }

    public static AdminPlaybackLabelTitle? Drift(AdminPlaybackNode node) => string.IsNullOrWhiteSpace(node.CapabilityDrift) ? null
        : new(T("web.admin.nodes.drift.label"), string.Join("\n", P(node.CapabilityDrift.Trim()), T("web.admin.nodes.drift.meaning"), T("web.admin.nodes.drift.clears")));

    public static AdminPlaybackLabelTitle? Override(AdminPlaybackNode node)
    {
        var accel = node.HwAccelOverride?.Trim().ToLowerInvariant() ?? ""; var devices = AdminPlaybackSettingsRules.ParseDevices(node.HwDeviceOverride ?? "");
        if (accel == "" && devices.Count == 0) return null;
        var accelLabel = accel == "none" ? T("web.admin.nodes.backend.softwareName") : P(accel);
        var deviceLabel = devices.Count == 1 ? P(devices[0]) : T("web.admin.nodes.override.deviceCount", new { count = devices.Count, count_text = devices.Count });
        var label = string.Join(" · ", new[] { accelLabel, devices.Count > 0 ? deviceLabel : "" }.Where(part => part != ""));
        var title = string.Join("\n", accel == "" ? T("web.admin.nodes.override.accelInherited") : T("web.admin.nodes.override.accelOverridden", new { backend = accelLabel }),
            devices.Count == 0 ? T("web.admin.nodes.override.devicesInherited") : T("web.admin.nodes.override.devicesOverridden", new { devices = string.Join(", ", devices.Select(P)) }), T("web.admin.nodes.override.appliesWhen"));
        return new(T("web.admin.nodes.override.label", new { policy = label }), title);
    }

    /// <summary>
    /// Official staleInventoryTitle: the two times go through the preference-aware date formatter (<paramref name="formatDateTime"/>, the app's shared one), as the official page's formatDateTime does.
    /// A time the server sent in a form that cannot be read is shown as the plain text it is.
    /// </summary>
    public static string StaleTitle(string reason, AdminPlaybackNode node, Func<DateTimeOffset, string> formatDateTime)
    {
        string Time(string? value) => Parse(value) is { } at ? formatDateTime(at) : P(value);
        return T("web.admin.nodes.staleInventory." + reason, new { refreshed = Time(node.CapabilitiesRefreshedAt), @checked = Time(node.LastHealthCheck) });
    }

    // ---- Drain ----------------------------------------------------------------------------------------------------------

    public static AdminPlaybackDrainLine? Drain(AdminPlaybackNode node)
    {
        if (node.Type != "transcode") return null; var requested = node.DrainRequested == true; var state = node.DrainState ?? "none";
        if (!node.Enabled) return requested ? new("draining", T("web.admin.nodes.drain.requestedLabel"), T("web.admin.nodes.drain.disabledDetail")) : null;
        if (!requested && state == "none") return null;
        if (!node.Healthy) return requested ? new("draining", T("web.admin.nodes.drain.notAnsweringLabel"), T("web.admin.nodes.drain.notAnsweringDetail")) : null;
        if (state == "drained") return requested ? new("drained", T("web.admin.nodes.drain.safeToStopLabel"), T("web.admin.nodes.drain.safeToStopDetail")) : new("drained", T("web.admin.nodes.drain.noWorkLabel"), T("web.admin.nodes.drain.noWorkDetail"));
        var active = Math.Max(0, node.ActiveJobs);
        return new("draining", T("web.admin.nodes.drain.drainingLabel", new { count = active, count_text = active }), T(requested ? "web.admin.nodes.drain.requestedDetail" : "web.admin.nodes.drain.stopSignalDetail"));
    }

    // ---- Groups ---------------------------------------------------------------------------------------------------------

    public static string GroupOf(AdminPlaybackNode node) => node.Group?.Trim() ?? "";

    public static IReadOnlyList<AdminPlaybackNodeGroup> Groups(IReadOnlyList<AdminPlaybackNode> nodes) => nodes.GroupBy(GroupOf).Select(bucket =>
    {
        var members = bucket.ToArray(); var proxies = members.Count(node => node.Type == "proxy"); var transcodes = members.Count(node => node.Type == "transcode");
        var degraded = bucket.Key != "" && members.Any(node => node.Enabled && !node.Healthy);
        var proxyText = proxies > 0 ? T("web.admin.nodes.groups.proxyCount", new { count = proxies, count_text = proxies }) : null;
        var transcodeText = transcodes > 0 ? T("web.admin.nodes.groups.transcodeCount", new { count = transcodes, count_text = transcodes }) : null;
        var composition = proxyText != null && transcodeText != null ? T("web.admin.nodes.groups.compositionBoth", new { proxies = proxyText, transcodes = transcodeText })
            : (proxyText ?? transcodeText) is { } only ? T("web.admin.nodes.groups.compositionOne", new { nodes = only }) : ".";
        var lines = new List<string> { composition, bucket.Key == "" ? T("web.admin.nodes.groups.ungroupedRouting") : proxies > 0 && transcodes > 0 ? T("web.admin.nodes.groups.pairedRouting")
            : transcodes > 0 ? T("web.admin.nodes.groups.noProxyRouting") : T("web.admin.nodes.groups.noTranscodeRouting") };
        if (degraded) lines.Add(T("web.admin.nodes.groups.outOfService"));
        return new AdminPlaybackNodeGroup(bucket.Key, bucket.Key == "" ? T("web.admin.nodes.groups.ungrouped") : P(bucket.Key), members.Length, degraded, string.Join("\n", lines));
    }).OrderBy(group => group.Value == "" ? 1 : 0).ThenBy(group => group.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();

    // ---- Device picker --------------------------------------------------------------------------------------------------

    private static IReadOnlyList<(string Path, string Description, string Title)> Reported(AdminPlaybackNode? node)
    {
        var details = (node?.Capabilities?.RenderDeviceDetails ?? []).Where(device => !string.IsNullOrWhiteSpace(device.Path)).ToArray();
        if (details.Length > 0) return details.Select(device => (device.Path.Trim(), DeviceLabel(device), DeviceLine(device))).ToArray();
        return (node?.Capabilities?.RenderDevices ?? []).Select(path => path.Trim()).Where(path => path != "").Select(path => (path, T("web.admin.nodes.gpu.genericDevice"), P(path))).ToArray();
    }
    public static bool HasDeviceInventory(AdminPlaybackNode? node) => Reported(node).Count > 0;
    public static IReadOnlyList<string> DevicePaths(AdminPlaybackNode? node) => Reported(node).Select(device => device.Path).ToArray();
    public static IReadOnlyList<AdminPlaybackDeviceRow> DeviceRows(AdminPlaybackNode? node, string? overrideValue)
    {
        var selected = AdminPlaybackSettingsRules.ParseDevices(overrideValue ?? "");
        var rows = Reported(node).Select(device => new AdminPlaybackDeviceRow(device.Path, device.Description, true, selected.Contains(device.Path), device.Title)).ToList();
        foreach (var path in selected.Where(path => rows.All(row => row.Path != path)))
            rows.Add(new(path, T("web.admin.nodes.devicePicker.unreportedDescription"), false, true, T("web.admin.nodes.devicePicker.unreportedTitle", new { path = P(path) })));
        return rows;
    }

    /// <summary>Official nodeUsesCUDADevices: NVENC names GPUs by CUDA identity, so the render-path picker is meaningless for it.</summary>
    public static bool UsesCudaDevices(AdminPlaybackNode? node, string? acceleration, string? cluster)
    {
        var selected = acceleration?.Trim().ToLowerInvariant() ?? "";
        if (selected == "nvenc") return true;
        if (selected is not ("" or "inherit" or "auto")) return false;
        if (selected is "" or "inherit")
        {
            var clusterValue = cluster?.Trim().ToLowerInvariant() ?? "";
            if (clusterValue != "" && clusterValue != "auto") return clusterValue == "nvenc";
        }
        return node?.Capabilities?.Resolved?.Trim().ToLowerInvariant() == "nvenc";
    }
    public static bool DeviceSyntaxChanges(AdminPlaybackNode? node, string? current, string? next, string? cluster) => UsesCudaDevices(node, current, cluster) != UsesCudaDevices(node, next, cluster);

    /// <summary>Official describeReprobeOutcome: whether the driver work landed, compared against the stored hash.</summary>
    public static (bool Ok, string Message) ReprobeOutcome(AdminPlaybackNode node, AdminPlaybackNodeCommand result)
    {
        var name = P(result.NodeName?.Trim() is { Length: > 0 } reported ? reported : node.Name);
        if (result.Status != "ok") return (false, T("web.admin.nodes.reprobe.failed", new { name, reason = P(result.Error?.Trim()) is { Length: > 0 } reason ? reason : T("web.admin.nodes.reprobe.noReason") }));
        var resolved = result.Resolved?.Trim().ToLowerInvariant() ?? "";
        var backend = resolved == "" ? "" : resolved == "none" ? T("web.admin.nodes.backend.softwareName") : BackendLabel(resolved);
        var hash = result.CapabilityHash?.Trim() ?? ""; var previous = node.CapabilitiesHash?.Trim() ?? "";
        var values = new { name, backend, pending = result.CapabilitiesRefreshed == true ? "no" : "yes" };
        var kind = hash != "" && previous != "" ? hash == previous ? "unchanged" : "changed" : "done";
        return (true, T("web.admin.nodes.reprobe." + kind + (backend != "" ? "WithBackend" : ""), values));
    }
}
