using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// One derived reading. Nothing invents a value: an unmeasured reading is a
/// muted dash, and Fill is null both when unmeasured and when there is no ceiling.
/// </summary>
public sealed record DashboardMetric(string Label, string Value, string Detail, string Title, bool Muted, bool Warning, int? Fill);

public enum DashboardNodeState { Healthy, Draining, Drained, Unhealthy, Disabled }

public sealed record DashboardResourceSample(
    bool Available,
    string UnavailableTitle,
    DashboardMetric? Cpu,
    DashboardMetric? Memory,
    DashboardMetric? Gpu,
    DateTimeOffset? SampledAt,
    bool Stale,
    string? InstanceId,
    DashboardMetric? ProcessMemory,
    DashboardMetric? HeapMemory);

public sealed record DashboardNodeSystem(bool Reported, string Title, DashboardMetric? Cpu, DashboardMetric? Memory, DashboardMetric? Disk);

public sealed record DashboardNodeGroup(string Name, string Label, DashboardNodeState State, int Rank, IReadOnlyList<DashboardNode> Nodes);

public sealed record DashboardRouteNode(string Key, string Kind, string Label, string Name);

public sealed record DashboardScanGroup(string LibraryId, DashboardActiveScan Primary, int Count);

/// <summary>A settings health item; State is ok, warn, off or info.</summary>
public sealed record DashboardHealthItem(string Id, string Label, string State, string StateText, string Detail, string? ActionLabel, string? ActionPage);

public static class DashboardPresentation
{
    private static string T(string key, params (string, object?)[] values) => DashboardText.T(key, values);

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    private static DashboardMetric Muted(string label, string title) => new(label, DashboardFormat.Dash, "", title, true, false, null);

    // ---- Resources -----------------------------------------------------

    public static DashboardResourceSample DescribeResourceSample(DashboardSystemResources? resources, DateTimeOffset now, bool includeAttribution = true)
    {
        var system = resources?.System;
        if (resources == null || !resources.Available || system == null)
            return new DashboardResourceSample(false, T("web.admin.nodes.resources.notSampled"), null, null, null, null, false, null, null, null);
        var attribution = includeAttribution ? resources.Attribution : null;
        var (cpu, memory) = SystemReadings(system, attribution);
        if (memory.Fill is { } fill) memory = memory with { Value = fill + "%", Detail = memory.Value };
        var stale = resources.Stale || (attribution != null && SampleIsOld(resources.SampledAt, attribution.SampleIntervalSeconds, now));
        return new DashboardResourceSample(true, "", cpu, memory, GpuBusy(resources.Gpu ?? []), resources.SampledAt, stale,
            string.IsNullOrWhiteSpace(attribution?.InstanceId) ? null : attribution!.InstanceId!.Trim(),
            attribution != null ? ProcessMemory(attribution, false) : null,
            attribution != null ? ProcessMemory(attribution, true) : null);
    }

    private static bool SampleIsOld(DateTimeOffset? sampledAt, double? interval, DateTimeOffset now)
        => sampledAt is not { } sampled || (now - sampled).TotalMilliseconds > (interval ?? 5) * 3000;

    private static (DashboardMetric Cpu, DashboardMetric Memory) SystemReadings(DashboardSystemStats system, DashboardResourceAttribution? attribution)
    {
        var cpu = attribution?.Cpu?.Available == false
            ? Muted(T("web.admin.nodes.metric.cpu"), T("web.admin.nodes.system.cpuUnavailable"))
            : WithScope(Cpu(system), attribution?.Cpu?.Scope);
        var memory = attribution?.Memory?.Available == false
            ? Muted(T("web.admin.nodes.metric.memory"), T("web.admin.nodes.system.memoryUnavailable"))
            : WithScope(Memory(system), attribution?.Memory?.Scope);
        return (cpu, memory);
    }

    private static DashboardMetric WithScope(DashboardMetric metric, string? scope)
    {
        var key = scope switch
        {
            "cgroup" => "web.admin.nodes.metric.cgroupScope",
            "host" => "web.admin.nodes.metric.hostScope",
            "virtualized_host" => "web.admin.nodes.metric.containerHostScope",
            _ => null,
        };
        return key == null ? metric : metric with { Label = T(key, ("metric", metric.Label)) };
    }

    private static DashboardMetric Cpu(DashboardSystemStats system)
    {
        var cpu = Finite(system.CpuPct);
        if (cpu == null) return Muted(T("web.admin.nodes.metric.cpu"), T("web.admin.nodes.cpu.noReading"));
        var percent = DashboardFormat.ClampPercent(cpu.Value);
        var cores = Finite(system.Cores);
        var load = Finite(system.Load1);
        var detail = string.Join(" · ", new[]
        {
            cores > 0 ? T("web.admin.nodes.cpu.cores", DashboardText.Count((long)cores!.Value)) : null,
            load != null ? T("web.admin.nodes.cpu.load", ("load", DashboardFormat.Decimal(load.Value, 2))) : null,
        }.Where(part => part != null));
        var title = string.Join(" ", new[]
        {
            T("web.admin.nodes.cpu.busyTitle", ("percent", percent)),
            cores > 0 ? T("web.admin.nodes.cpu.coresTitle", DashboardText.Count((long)cores!.Value)) : null,
            load != null ? T("web.admin.nodes.cpu.loadTitle", ("load", DashboardFormat.Decimal(load.Value, 2))) : null,
        }.Where(part => part != null));
        return new DashboardMetric(T("web.admin.nodes.metric.cpu"), percent + "%", detail, title, false, false, percent);
    }

    private static DashboardMetric Memory(DashboardSystemStats system)
    {
        var used = Finite(system.MemUsedMb);
        var total = Finite(system.MemTotalMb);
        if (used == null || total == null || total <= 0)
            return Muted(T("web.admin.nodes.metric.memory"), T("web.admin.nodes.memory.noReading"));
        var value = UsedOfTotal(used.Value * 1024 * 1024, total.Value * 1024 * 1024) ?? DashboardFormat.Dash;
        var percent = DashboardFormat.ClampPercent(used.Value / total.Value * 100);
        return new DashboardMetric(T("web.admin.nodes.metric.memory"), value,
            T("web.admin.nodes.memory.percentUsed", ("percent", percent)),
            T("web.admin.nodes.memory.title", ("amount", value), ("percent", percent)), false, false, percent);
    }

    private static string? UsedOfTotal(double used, double total)
        => total <= 0 ? null : T("web.admin.nodes.resources.usedOfTotal",
            ("used", DashboardFormat.FileSize(used, "0 B", iec: true)), ("total", DashboardFormat.FileSize(total, iec: true)));

    private static DashboardMetric ProcessMemory(DashboardResourceAttribution attribution, bool heap)
    {
        var value = heap ? attribution.Process?.HeapLiveBytes : attribution.Process?.ResidentBytes;
        var label = heap ? T("web.admin.nodes.process.heapLabel") : T("web.admin.nodes.process.residentLabel");
        if (value == null) return Muted(label, T("web.admin.nodes.process.unmeasured"));
        return new DashboardMetric(label, DashboardFormat.FileSize(value, "0 B", iec: true),
            heap ? T("web.admin.nodes.process.heapDetail") : T("web.admin.nodes.process.residentDetail"),
            heap ? T("web.admin.nodes.process.heapTitle") : T("web.admin.nodes.process.residentTitle"), false, false, null);
    }

    private static bool IsMeasuredGpuSource(string? source)
    {
        var normalized = source?.Trim().ToLowerInvariant() ?? "";
        return normalized != "" && normalized != "unavailable";
    }

    /// <summary>The busiest GPU's video engine; null when the host reports no GPU at all.</summary>
    public static DashboardMetric? GpuBusy(IReadOnlyList<DashboardGpuStats> gpu)
    {
        if (gpu.Count == 0) return null;
        var sessions = (long)gpu.Sum(stats => Finite(stats.Sessions) ?? 0);
        var sessionLabel = T("web.admin.nodes.gpu.sessionCount", DashboardText.Count(sessions));
        var title = string.Join("\n", gpu.Select(stats =>
        {
            var device = string.IsNullOrWhiteSpace(stats.Device) ? T("web.admin.nodes.gpu.unknownDevice") : stats.Device!.Trim();
            var video = Finite(stats.VideoBusyPct);
            var reading = IsMeasuredGpuSource(stats.Source) && video != null
                ? T("web.admin.nodes.gpu.videoEngine", ("percent", DashboardFormat.ClampPercent(video.Value)))
                : T("web.admin.nodes.gpu.notMeasured");
            var count = Math.Max(0, (long)Math.Truncate(Finite(stats.Sessions) ?? 0));
            return $"{device} — {reading} · {T("web.admin.nodes.gpu.sessionCount", DashboardText.Count(count))}";
        }));
        double? busiest = null;
        foreach (var stats in gpu)
            if (IsMeasuredGpuSource(stats.Source) && Finite(stats.VideoBusyPct) is { } value && (busiest == null || value > busiest))
                busiest = value;
        if (busiest == null)
            return Muted(T("web.admin.nodes.metric.gpu"), title == "" ? T("web.admin.nodes.gpu.noReading") : title);
        var label = gpu.Count == 1 ? T("web.admin.nodes.gpu.singleEngine") : T("web.admin.nodes.gpu.busiestOf", ("count", (long)gpu.Count));
        var percent = DashboardFormat.ClampPercent(busiest.Value);
        return new DashboardMetric(T("web.admin.nodes.metric.gpu"), percent + "%", $"{label} · {sessionLabel}", title, false, false, percent);
    }

    // ---- Nodes ---------------------------------------------------------

    public static string NodeGroupOf(DashboardNode node) => node.Group?.Trim() ?? "";

    public static bool NodeReportsAcceleration(DashboardNode node) => node.Type != "proxy";

    public static long? PositiveCap(long? value) => value is > 0 ? value : null;

    public static (string Phase, string Label)? NodeDrain(DashboardNode node)
    {
        if (node.Type != "transcode") return null;
        var requested = node.DrainRequested;
        var state = node.DrainState ?? "none";
        if (!node.Enabled) return requested ? ("draining", T("web.admin.nodes.drain.requestedLabel")) : null;
        if (!requested && state == "none") return null;
        if (!node.Healthy) return requested ? ("draining", T("web.admin.nodes.drain.notAnsweringLabel")) : null;
        if (state == "drained")
            return ("drained", requested ? T("web.admin.nodes.drain.safeToStopLabel") : T("web.admin.nodes.drain.noWorkLabel"));
        var active = Math.Max(0, node.ActiveJobs);
        return ("draining", T("web.admin.nodes.drain.drainingLabel", DashboardText.Count(active)));
    }

    public static DashboardNodeState NodeState(DashboardNode node)
    {
        if (!node.Enabled) return DashboardNodeState.Disabled;
        if (!node.Healthy) return DashboardNodeState.Unhealthy;
        return NodeDrain(node)?.Phase switch
        {
            "draining" => DashboardNodeState.Draining,
            "drained" => DashboardNodeState.Drained,
            _ => DashboardNodeState.Healthy,
        };
    }

    public static string NodeStateLabel(DashboardNodeState state) => T("web.admin.nodes.state." + state switch
    {
        DashboardNodeState.Draining => "draining",
        DashboardNodeState.Drained => "drained",
        DashboardNodeState.Unhealthy => "unhealthy",
        DashboardNodeState.Disabled => "disabled",
        _ => "healthy",
    });

    public static DashboardMetric NodeJobs(DashboardNode node)
    {
        var label = node.Type == "proxy" ? T("web.admin.nodes.capacity.streams") : T("web.admin.nodes.capacity.transcodes");
        var active = Math.Max(0, node.ActiveJobs);
        var cap = PositiveCap(node.MaxJobs);
        return new DashboardMetric(label, cap == null ? $"{active}" : $"{active} / {cap}",
            cap == null ? T("web.admin.nodes.capacity.noCap") : T("web.admin.nodes.capacity.inUse"),
            cap == null
                ? T("web.admin.nodes.capacity.jobsUncappedTitle", DashboardText.Count(active))
                : T("web.admin.nodes.capacity.jobsCappedTitle", DashboardText.Count(active, ("cap", cap.Value))),
            false, cap != null && active >= cap, cap is { } c ? DashboardFormat.ClampPercent(active / (double)c * 100) : null);
    }

    public static DashboardMetric NodeEgress(DashboardNode node)
    {
        var kbps = Math.Max(0, node.EgressKbps);
        var cap = PositiveCap(node.MaxBandwidthKbps);
        static string Mbps(double value) => DashboardFormat.Decimal(Math.Round(value / 100, MidpointRounding.AwayFromZero) / 10, 1, 0);
        var used = Mbps(kbps);
        return new DashboardMetric(T("web.admin.nodes.capacity.egress"),
            cap == null ? $"{used} Mbps" : $"{used} / {Mbps(cap.Value)} Mbps",
            cap == null ? T("web.admin.nodes.capacity.noCap") : T("web.admin.nodes.capacity.ofCap"),
            cap == null ? T("web.admin.nodes.capacity.egressUncappedTitle", ("used", used))
                : T("web.admin.nodes.capacity.egressCappedTitle", ("used", used), ("cap", Mbps(cap.Value))),
            false, cap != null && kbps >= cap, cap is { } c ? DashboardFormat.ClampPercent(kbps / (double)c * 100) : null);
    }

    private static bool NodeSampleIsStale(DashboardNode node, DateTimeOffset now)
    {
        if (node.LastStats?.SampledAt is not { } sampled || node.LastHealthCheck is not { } checkedAt) return false;
        var interval = node.LastStats.Attribution?.SampleIntervalSeconds ?? 5;
        return (checkedAt - sampled).TotalMilliseconds > interval * 3000 || (now - checkedAt).TotalMilliseconds > 90_000;
    }

    public static DashboardNodeSystem NodeSystem(DashboardNode node, DateTimeOffset now)
    {
        var system = node.LastStats?.System;
        if (system == null)
            return new(false, node.Healthy ? T("web.admin.nodes.system.noSample") : T("web.admin.nodes.system.notAnswering"), null, null, null);
        if (!node.Healthy) return new(false, T("web.admin.nodes.system.lastCheckFailed"), null, null, null);
        if (NodeSampleIsStale(node, now)) return new(false, T("web.admin.nodes.system.staleTitle"), null, null, null);
        var (cpu, memory) = SystemReadings(system, node.LastStats?.Attribution);
        return new(true, "", cpu, memory, WorstDisk(system.Disks ?? []));
    }

    public static DashboardMetric? NodeGpuBusy(DashboardNode node, DateTimeOffset now)
        => !node.Healthy || NodeSampleIsStale(node, now) ? null : GpuBusy(node.LastStats?.Gpu ?? []);

    private static int? DiskFill(DashboardDiskStats disk)
    {
        if (disk.Unavailable == true) return null;
        if (Finite(disk.UsedGb) is not { } used || Finite(disk.TotalGb) is not { } total || total <= 0) return null;
        return DashboardFormat.ClampPercent(used / total * 100);
    }

    private static string DiskName(DashboardDiskStats disk) => disk.Path?.Trim() is { Length: > 0 } path ? path : disk.Role?.Trim() ?? "";

    private static string DiskLine(DashboardDiskStats disk)
    {
        var path = DiskName(disk) is { Length: > 0 } name ? name : T("web.admin.nodes.disk.unknownMount");
        if (disk.Unavailable == true) return T("web.admin.nodes.disk.lineUnavailable", ("path", path));
        if (DiskFill(disk) is not { } fill) return T("web.admin.nodes.disk.lineNoCapacity", ("path", path));
        var capacity = disk.UsedGb is { } u && disk.TotalGb is { } tot ? UsedOfTotal(u * 1024 * 1024 * 1024, tot * 1024 * 1024 * 1024) : null;
        var stale = disk.Stale == true ? "yes" : "no";
        return capacity != null
            ? T("web.admin.nodes.disk.lineWithCapacity", ("path", path), ("percent", fill.ToString()), ("stale", stale), ("capacity", capacity))
            : T("web.admin.nodes.disk.line", ("path", path), ("percent", fill.ToString()), ("stale", stale));
    }

    public static DashboardMetric WorstDisk(IReadOnlyList<DashboardDiskStats> disks)
    {
        var title = disks.Count > 0 ? string.Join("\n", disks.Select(DiskLine)) : "";
        var measured = disks.Select(disk => (disk, fill: DiskFill(disk))).Where(entry => entry.fill != null).ToList();
        if (measured.Count == 0) return Muted(T("web.admin.nodes.metric.disk"), title == "" ? T("web.admin.nodes.disk.noReading") : title);
        var worst = measured.Aggregate((a, b) => b.fill > a.fill ? b : a);
        var name = DiskName(worst.disk);
        return new DashboardMetric(T("web.admin.nodes.metric.disk"), worst.fill + "%",
            name == "" ? T("web.admin.nodes.disk.fullDetail") : name, title, false, worst.fill >= 85, worst.fill);
    }

    /// <summary>A nearly full (over 90%) transcode directory, or null.</summary>
    public static DashboardMetric? TranscodeDirectoryWarning(DashboardNode node, DateTimeOffset now)
    {
        if (node.Type != "transcode") return null;
        var system = NodeSystem(node, now);
        return system.Reported && system.Disk is { Fill: > 90 } disk ? disk : null;
    }

    public static bool BuildDiffers(DashboardNode node, string? serverRevision)
    {
        var build = node.LastStats?.Build;
        return build is { Available: true } && !string.IsNullOrEmpty(build.Revision) && !string.IsNullOrEmpty(serverRevision)
               && build.Revision != serverRevision;
    }

    public static int AttentionRank(DashboardNode node, string? serverRevision, DateTimeOffset now)
    {
        var state = NodeState(node);
        if (state == DashboardNodeState.Unhealthy) return 0;
        if (state == DashboardNodeState.Draining) return 1;
        if (state == DashboardNodeState.Drained) return 2;
        if (state == DashboardNodeState.Disabled) return 5;
        var flagged = NodeJobs(node).Warning || NodeEgress(node).Warning || TranscodeDirectoryWarning(node, now) != null || BuildDiffers(node, serverRevision);
        return flagged ? 3 : 4;
    }

    private static int CompareNatural(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                var cmp = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (cmp != 0) return cmp;
                continue;
            }
            var c = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.CurrentCultureIgnoreCase);
            if (c != 0) return c;
            i++; j++;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Nodes under their group, worst group first; proxies before transcoders within a group.</summary>
    public static IReadOnlyList<DashboardNodeGroup> NodeGroups(IReadOnlyList<DashboardNode> nodes, string? serverRevision, DateTimeOffset now)
    {
        var ranks = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (var node in nodes) ranks[node] = AttentionRank(node, serverRevision, now);
        int RankOf(DashboardNode node) => ranks[node];
        var byName = new Dictionary<string, List<DashboardNode>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var node in nodes)
        {
            var name = NodeGroupOf(node);
            if (!byName.TryGetValue(name, out var members)) { members = []; byName[name] = members; order.Add(name); }
            members.Add(node);
        }
        var labels = GroupLabels(order.Where(name => name != "").ToList());
        var ungrouped = T("web.admin.nodes.groups.ungrouped");
        return order.Select(name =>
        {
            var members = byName[name];
            var worst = members.Aggregate((a, b) => RankOf(b) < RankOf(a) ? b : a);
            var sorted = members.ToList();
            sorted.Sort((a, b) =>
            {
                var c = string.Compare(a.Type, b.Type, StringComparison.CurrentCulture);
                if (c != 0) return c;
                c = RankOf(a) - RankOf(b);
                if (c != 0) return c;
                c = CompareNatural(a.Name, b.Name);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return new DashboardNodeGroup(name, labels.GetValueOrDefault(name, ungrouped), NodeState(worst), RankOf(worst), sorted);
        }).OrderBy(group => group, Comparer<DashboardNodeGroup>.Create((a, b) =>
        {
            var c = a.Rank - b.Rank;
            if (c != 0) return c;
            c = (a.Name == "" ? 1 : 0) - (b.Name == "" ? 1 : 0);
            return c != 0 ? c : CompareNatural(a.Name, b.Name);
        })).ToArray();
    }

    public static string SharedGroupPrefix(IReadOnlyList<string> names)
    {
        if (names.Count < 2) return "";
        var prefix = names[0];
        foreach (var name in names)
            while (!name.StartsWith(prefix, StringComparison.Ordinal)) prefix = prefix[..^1];
        return prefix[..(prefix.LastIndexOf('-') + 1)];
    }

    public static Dictionary<string, string> GroupLabels(IReadOnlyList<string> names)
    {
        var prefix = SharedGroupPrefix(names);
        var labels = names.ToDictionary(name => name, name => GroupLabel(name, prefix), StringComparer.Ordinal);
        var counts = labels.Values.GroupBy(label => label).ToDictionary(group => group.Key, group => group.Count());
        foreach (var name in names)
            if (counts[labels[name]] > 1) labels[name] = name;
        return labels;
    }

    private static string GroupLabel(string name, string prefix)
    {
        var words = name.Split('-');
        var kept = name[prefix.Length..].Split('-').Length;
        while (kept < words.Length && string.Join("-", words[^kept..]).Length < 8) kept++;
        return string.Join("-", words[^kept..]);
    }

    /// <summary>"2 groups · 3/4 healthy · 5 streams · 2 transcodes · 120 Mbps"; counts only current nodes.</summary>
    public static string FleetSummary(IReadOnlyList<DashboardNode> nodes)
    {
        var enabled = nodes.Where(node => node.Enabled).ToList();
        var live = enabled.Where(node => node.Healthy).ToHashSet(ReferenceEqualityComparer.Instance);
        var egress = live.Cast<DashboardNode>().Sum(node => node.EgressKbps);
        string Work(string key, IEnumerable<DashboardNode> list)
        {
            var active = list.Where(node => live.Contains(node)).Sum(node => node.ActiveJobs);
            return T(key, DashboardText.Count(active));
        }
        var proxies = nodes.Where(node => node.Type == "proxy").ToList();
        var others = nodes.Where(node => node.Type != "proxy").ToList();
        var groups = nodes.Select(NodeGroupOf).Where(name => name != "").Distinct().Count();
        var parts = new List<string?>
        {
            groups > 0 ? T("web.admin.dashboard.widgets.transcodeNodes.groupCount", ("count", (long)groups)) : null,
            enabled.Count > 0 ? T("web.admin.dashboard.widgets.transcodeNodes.healthyCount", ("healthy", (long)live.Count), ("enabled", (long)enabled.Count)) : null,
            proxies.Count > 0 ? Work("web.admin.dashboard.widgets.transcodeNodes.streamCount", proxies) : null,
            others.Count > 0 ? Work("web.admin.dashboard.widgets.transcodeNodes.transcodeCount", others) : null,
            DashboardFormat.Mbps(egress / 1000d),
        };
        return string.Join(" · ", parts.Where(part => part != null));
    }

    // ---- Sessions ------------------------------------------------------

    private static string NormalizeStreamDecision(string? decision) => decision?.Trim() switch
    {
        "direct" => "direct",
        "copy" or "remux" => "copy",
        "transcode" => "transcode",
        _ => "",
    };

    /// <summary>The server's effective method when present; audio-only transcodes are direct_stream, not transcode.</summary>
    public static string ClassifyMethod(DashboardSession session)
    {
        if (!string.IsNullOrEmpty(session.EffectivePlayMethod)) return session.EffectivePlayMethod;
        var video = NormalizeStreamDecision(!string.IsNullOrEmpty(session.VideoDecision) ? session.VideoDecision : session.PlayMethod);
        var audio = NormalizeStreamDecision(!string.IsNullOrEmpty(session.AudioDecision) ? session.AudioDecision
            : session.TranscodeAudio ? "transcode" : session.PlayMethod);
        if (video == "transcode") return "transcode";
        if (audio == "transcode" && video != "") return "direct_stream";
        if (video == "direct" && audio == "direct") return "direct";
        if (video == "copy" || audio == "copy") return "remux";
        return "unknown";
    }

    /// <summary>Label and tone (success/info/warning/destructive/muted) of a method bucket.</summary>
    public static (string Label, string Tone) MethodMeta(string method) => method switch
    {
        "direct" => (T("web.common.activity.method.direct.label"), "success"),
        "remux" => (T("web.common.activity.method.remux.label"), "info"),
        "direct_stream" => (T("web.common.activity.method.directStream.label"), "warning"),
        "transcode" => (T("web.common.activity.method.transcode.label"), "destructive"),
        _ => (T("web.common.activity.method.unknown.label"), "muted"),
    };

    public static string ClientLabel(DashboardSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.ClientLabel)) return session.ClientLabel.Trim();
        var name = session.ClientName?.Trim();
        var version = session.ClientVersion?.Trim();
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version)) return name + " " + version;
        return name ?? "";
    }

    public static bool IsEpisode(DashboardSession session)
        => !string.IsNullOrEmpty(session.SeriesName) && session.SeasonNumber != null && session.EpisodeNumber != null;

    public static string SessionTitle(DashboardSession session, string widgetKey)
        => IsEpisode(session)
            ? !string.IsNullOrEmpty(session.EpisodeName) ? session.EpisodeName
                : T($"web.admin.dashboard.widgets.{widgetKey}.episodeFallback", ("season", session.SeasonNumber.ToString()), ("episode", session.EpisodeNumber.ToString()))
            : !string.IsNullOrEmpty(session.MediaTitle) ? session.MediaTitle
                : T($"web.admin.dashboard.widgets.{widgetKey}.fileFallback", ("id", session.MediaFileId));

    public static string SessionUser(DashboardSession session, string widgetKey)
        => !string.IsNullOrEmpty(session.Username) ? session.Username : T($"web.admin.dashboard.widgets.{widgetKey}.userFallback", ("id", session.UserId));

    public static IReadOnlyList<DashboardRouteNode> RouteNodes(DashboardSession session)
    {
        var execution = session.RoutingExecution?.Trim();
        var egress = session.RoutingEgress?.Trim();
        var resolved = !string.IsNullOrEmpty(session.RoutingWorkload?.Trim()) || !string.IsNullOrEmpty(execution) || !string.IsNullOrEmpty(egress);
        if (!resolved)
        {
            var reported = (session.NodeDisplayName?.Trim() is { Length: > 0 } display ? display : session.ReportingNode?.Trim()) ?? "";
            var local = reported == "" || reported.Equals("local", StringComparison.OrdinalIgnoreCase);
            return [new DashboardRouteNode(local ? "legacy:local" : "legacy:" + reported, "legacy", T("web.admin.activity.route.node"),
                local ? T("web.admin.activity.route.localServer") : reported)];
        }
        var nodes = new List<DashboardRouteNode>();
        var executionName = session.RoutingExecutionNodeName?.Trim() is { Length: > 0 } en ? en : session.NodeDisplayName;
        if (execution is "transcode" or "proxy") nodes.Add(NamedRouteNode(execution, executionName, session.RoutingExecutionNodeId));
        if (egress == "proxy")
        {
            var node = NamedRouteNode("proxy", session.RoutingEgressNodeName, session.RoutingEgressNodeId);
            if (nodes.All(existing => existing.Key != node.Key)) nodes.Add(node);
        }
        if (egress == "api" || (execution == "api" && nodes.Count == 0)) nodes.Add(ServerRouteNode(session));
        return nodes.Count > 0 ? nodes : [ServerRouteNode(session)];
    }

    private static DashboardRouteNode NamedRouteNode(string kind, string? name, string? id)
    {
        var label = T(kind == "transcode" ? "web.admin.activity.route.transcode" : "web.admin.activity.route.proxy");
        var trimmedName = name?.Trim();
        var trimmedId = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        var unknown = T(kind == "transcode" ? "web.admin.activity.route.unknownTranscodeNode" : "web.admin.activity.route.unknownProxyNode");
        return new DashboardRouteNode(kind + ":" + (trimmedId ?? trimmedName ?? "unknown"), kind, label,
            !string.IsNullOrEmpty(trimmedName) ? trimmedName
                : trimmedId != null ? T("web.admin.activity.route.nodeId", ("id", trimmedId)) : unknown);
    }

    private static DashboardRouteNode ServerRouteNode(DashboardSession session)
    {
        var reported = session.ReportingNode?.Trim();
        var label = T("web.admin.activity.route.server");
        return string.IsNullOrEmpty(reported) || reported.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? new DashboardRouteNode("server:local", "server", label, T("web.admin.activity.route.localServer"))
            : new DashboardRouteNode("server:" + reported, "server", label, reported);
    }

    // ---- Scans ---------------------------------------------------------

    private static int ScanModeRank(string mode) => mode switch { "library" => 0, "file" => 1, "subtree" => 2, _ => 3 };

    public static int CompareActiveScans(DashboardActiveScan left, DashboardActiveScan right)
    {
        var l = left.Status == "running" ? 0 : 1;
        var r = right.Status == "running" ? 0 : 1;
        if (l != r) return l - r;
        var mode = ScanModeRank(left.Mode) - ScanModeRank(right.Mode);
        if (mode == 0) mode = string.CompareOrdinal(left.Mode, right.Mode);
        if (mode != 0) return mode;
        return string.Compare(left.Path ?? "", right.Path ?? "", StringComparison.CurrentCulture);
    }

    /// <summary>Active scans per library with the one worth showing first.</summary>
    public static IReadOnlyList<DashboardScanGroup> GroupActiveScans(IEnumerable<DashboardActiveScan> scans)
    {
        var grouped = new Dictionary<string, List<DashboardActiveScan>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var scan in scans)
        {
            if (!scan.IsActive) continue;
            if (!grouped.TryGetValue(scan.LibraryId, out var list)) { list = []; grouped[scan.LibraryId] = list; order.Add(scan.LibraryId); }
            list.Add(scan);
        }
        return order.Select(id =>
        {
            var list = grouped[id];
            list.Sort(CompareActiveScans);
            return new DashboardScanGroup(id, list[0], list.Count);
        }).ToArray();
    }

    public static string ScanMode(string mode) => mode switch
    {
        "library" or "subtree" or "file" => T("web.admin.common.scans.mode." + mode),
        _ => mode,
    };

    public static string ScanTrigger(string trigger) => trigger switch
    {
        "autoscan" => T("web.admin.common.scans.trigger.autoscan"),
        "library_created" => T("web.admin.common.scans.trigger.created"),
        "library_paths_changed" or "library_updated" => T("web.admin.common.scans.trigger.updated"),
        "library_id" or "manual" => T("web.admin.common.scans.trigger.manual"),
        "task:scan_libraries" or "task_scan_libraries" => T("web.admin.common.scans.trigger.scheduled"),
        "realtime_monitor" => T("web.admin.common.scans.trigger.fileChange"),
        _ => trigger.Replace('_', ' '),
    };

    public static string ScanProgress(DashboardActiveScan scan)
    {
        if (scan.TotalFiles is > 0 && scan.FilesProcessed is > 0)
        {
            var percent = Math.Max(0, Math.Min(100, (int)Math.Round(scan.FilesProcessed.Value / (double)scan.TotalFiles.Value * 100, MidpointRounding.AwayFromZero)));
            return T("web.admin.common.scans.progress",
                ("message", scan.Message ?? T("web.admin.common.scans.processingFiles")),
                ("processed", DashboardFormat.Integer(scan.FilesProcessed.Value)),
                ("total", DashboardFormat.Integer(scan.TotalFiles.Value)),
                ("percent", (long)percent));
        }
        return scan.Message ?? "";
    }

    public static string LibraryScanProgress(DashboardActiveScan scan, int activeScanCount)
    {
        var running = scan.Status == "running";
        var progress = ScanProgress(scan);
        var detail = progress != "" ? progress : running ? ScanMode(scan.Mode) : T("web.admin.dashboard.format.scanWaitingForCapacity");
        if (activeScanCount > 1)
            return T(running ? "web.admin.dashboard.format.scanRunningWithMore" : "web.admin.dashboard.format.scanQueuedWithMore",
                ("detail", detail), ("more", (long)activeScanCount - 1), ("more_text", (long)activeScanCount - 1));
        return T(running ? "web.admin.dashboard.format.scanRunning" : "web.admin.dashboard.format.scanQueued", ("detail", detail));
    }

    /// <summary>Normalizes a scans-channel frame; returns null for anything without an id and status.</summary>
    public static DashboardActiveScan? ParseActiveScan(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        string? Text(string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var id = Text("id");
        var status = Text("status");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(status)) return null;
        string? libraryId = null;
        if (element.TryGetProperty("library_id", out var library))
        {
            if (library.ValueKind == JsonValueKind.String) libraryId = library.GetString();
            else if (library.ValueKind == JsonValueKind.Number && library.TryGetInt64(out var numeric)) libraryId = numeric.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (string.IsNullOrEmpty(libraryId)) return null;
        DateTimeOffset? started = Text("started_at") is { } s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
        long? total = null, processed = null;
        string? message = null;
        if (element.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            if (result.TryGetProperty("total_files", out var t) && t.TryGetInt64(out var tv)) total = tv;
            if (result.TryGetProperty("files_processed", out var p) && p.TryGetInt64(out var pv)) processed = pv;
            if (result.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString();
        }
        return new DashboardActiveScan(id, libraryId, Text("mode") ?? "", Text("path"), Text("trigger") ?? "", status, started, total, processed, message);
    }

    // ---- Users and libraries -------------------------------------------

    /// <summary>Most recently active first; never-active accounts sink, ties by username.</summary>
    public static IReadOnlyList<DashboardUser> SortUsersByLastActive(IEnumerable<DashboardUser> users)
        => users.OrderByDescending(user => user.LastActiveAt?.ToUnixTimeMilliseconds() ?? 0)
            .ThenBy(user => user.Username, StringComparer.CurrentCulture).ToArray();

    public static string AccountRole(DashboardUser user) => T("web.admin.common.accounts.role", ("role", user.IsOwner ? "owner" : user.Role));

    public static string LibraryTypeLabel(string type)
        => DashboardText.Has("web.common.libraryType." + type) ? T("web.common.libraryType." + type) : type;

    // ---- Settings health / attention ------------------------------------

    private static readonly HashSet<string> TrueValues = new(StringComparer.Ordinal) { "true", "1", "yes", "on" };

    /// <summary>Health items from already-fetched inputs; attention items are the ones carrying an action.</summary>
    public static IReadOnlyList<DashboardHealthItem> BuildSettingsHealth(IReadOnlyDictionary<string, string> settings,
        IReadOnlyCollection<string>? sensitiveConfigured, bool storageAvailable, DashboardServerStatus? status, DashboardSearchStatus? search)
    {
        string Text(string key) => settings.TryGetValue(key, out var value) ? value.Trim() : "";
        bool Bool(string key) => TrueValues.Contains(Text(key).ToLowerInvariant());
        (string?, string?) Action(string state, string page) => state switch
        {
            "warn" => (T("web.admin.settings.health.action.fix"), page),
            "off" => (T("web.admin.settings.health.action.setUp"), page),
            _ => (null, null),
        };
        var configured = new HashSet<string>(sensitiveConfigured ?? [], StringComparer.Ordinal);

        var publicBucket = Text("s3.public_bucket");
        var privateBucket = Text("s3.private_bucket");
        var bucketDetail = privateBucket != "" ? T("web.admin.settings.health.storage.publicAndPrivate")
            : publicBucket != "" ? T("web.admin.settings.health.storage.publicBucket", ("bucket", publicBucket))
            : T("web.admin.settings.health.storage.noBucket");
        var storageRestartPending = !storageAvailable && publicBucket != "" && RestartPending(status, ["s3."]);
        var storageState = storageAvailable ? "ok" : storageRestartPending ? "warn" : "off";

        var maxConnections = int.TryParse(Text("database.max_connections"), out var max) ? max : (int?)null;
        var redisConfigured = configured.Contains("redis.url") || Text("redis.url") != "";

        var transcodeEnabled = Bool("playback.transcode_enabled");
        var hwAccel = Text("playback.hw_accel") is { Length: > 0 } mode ? mode : "auto";
        var transcodeMode = hwAccel switch
        {
            "auto" => T("web.admin.settings.health.hwAccel.auto"),
            "none" => T("web.admin.settings.health.hwAccel.software"),
            "qsv" => "Quick Sync",
            "vaapi" => "VA-API",
            "nvenc" => "NVENC",
            "videotoolbox" => "VideoToolbox",
            _ => hwAccel.ToUpperInvariant(),
        };
        var playbackRestartPending = RestartPending(status, ["playback."]);
        var transcodeState = playbackRestartPending ? "warn" : transcodeEnabled ? "ok" : "off";

        var activeSearch = search?.ActiveProvider is { Length: > 0 } active ? active
            : Text("catalog.search.provider") is { Length: > 0 } provider ? provider : "postgres";
        var resolved = search != null;
        var meiliConfigured = search?.Meilisearch?.Configured ?? false;
        var meiliHealthy = search?.Meilisearch?.Healthy ?? false;
        var degraded = search?.Degraded ?? false;
        var searchState = activeSearch == "meilisearch" ? !resolved ? "info" : meiliHealthy && !degraded ? "ok" : "warn"
            : meiliConfigured ? "warn" : "info";
        var searchLabel = activeSearch switch
        {
            "postgres" => "Postgres",
            "meilisearch" => "Meilisearch",
            "" => "",
            _ => char.ToUpperInvariant(activeSearch[0]) + activeSearch[1..],
        };

        var emailHost = Text("email.smtp_host");
        var mailReady = Bool("email.enabled") && emailHost != "" && Text("email.from_address") != "";
        var emailState = mailReady ? "ok" : "off";
        var publicUrl = Text("server.public_url");
        var publicUrlState = publicUrl != "" ? "ok" : "off";

        DashboardHealthItem Item(string id, string labelKey, string state, string stateText, string detail, string? page)
        {
            var (actionLabel, actionPage) = page == null ? (null, null) : Action(state, page);
            return new DashboardHealthItem(id, T(labelKey), state, stateText, detail, actionLabel, actionPage);
        }

        return
        [
            Item("storage", "web.admin.settings.health.storage.label", storageState,
                storageAvailable ? T("web.admin.settings.health.state.healthy")
                    : storageRestartPending ? T("web.admin.settings.health.state.restartPending") : T("web.admin.settings.health.state.notSetUp"),
                storageAvailable ? bucketDetail
                    : storageRestartPending ? T("web.admin.settings.health.storage.appliesAfterRestart", ("bucket", bucketDetail))
                    : T("web.admin.settings.health.storage.notSetUp"), "storage"),
            Item("database", "web.admin.settings.health.database.label", "ok", T("web.admin.settings.health.state.healthy"),
                string.Join(" · ", new[]
                {
                    "Postgres",
                    maxConnections is > 0 ? T("web.admin.settings.health.database.maxConnections", DashboardText.Count(maxConnections.Value)) : null,
                    redisConfigured ? "Redis" : null,
                }.Where(part => !string.IsNullOrWhiteSpace(part))), null),
            Item("transcoding", "web.admin.settings.health.transcoding.label", transcodeState,
                playbackRestartPending ? T("web.admin.settings.health.state.restartPending")
                    : transcodeEnabled ? T("web.admin.settings.health.state.ready") : T("web.admin.settings.health.state.off"),
                playbackRestartPending ? T("web.admin.settings.health.transcoding.appliesAfterRestart")
                    : transcodeEnabled ? transcodeMode : T("web.admin.settings.health.transcoding.off"), "playback"),
            Item("search", "web.admin.settings.health.search.label", searchState, searchLabel,
                activeSearch == "meilisearch"
                    ? !resolved ? T("web.admin.settings.health.search.checking")
                        : degraded ? search!.DegradedReason ?? T("web.admin.settings.health.search.degraded")
                        : meiliHealthy ? T("web.admin.settings.health.search.meiliConnected") : T("web.admin.settings.health.search.meiliUnreachable")
                    : meiliConfigured ? T("web.admin.settings.health.search.meiliInactive") : T("web.admin.settings.health.search.meiliNotConnected"),
                "search"),
            Item("public-url", "web.admin.settings.health.publicUrl.label", publicUrlState,
                publicUrl != "" ? T("web.admin.settings.health.state.set") : T("web.admin.settings.health.state.notSet"),
                publicUrl != "" ? publicUrl : T("web.admin.settings.health.publicUrl.notSet"), "general"),
            Item("email", "web.admin.settings.health.email.label", emailState,
                mailReady ? T("web.admin.settings.health.state.ready") : T("web.admin.settings.health.state.notSetUp"),
                mailReady ? T("web.admin.settings.health.email.ready", ("host", emailHost)) : T("web.admin.settings.health.email.notSetUp"), "email"),
        ];
    }

    private static readonly HashSet<string> NonSettingsRestartReasons = new(StringComparer.Ordinal)
        { "jellyfin_compat", "ratelimit_backend", "plugin_auth_binding", "plugin_task_binding" };

    private static bool RestartPending(DashboardServerStatus? status, IReadOnlyList<string> prefixes)
    {
        if (status?.RestartRequired != true) return false;
        if (status.RestartRequiredReasons == null)
        {
            var reason = status.RestartRequiredReason?.Trim() ?? "";
            return reason == "" || !NonSettingsRestartReasons.Contains(reason);
        }
        return status.RestartRequiredReasons.Any(reason => reason.StartsWith("setting:", StringComparison.Ordinal)
            && prefixes.Any(prefix => reason["setting:".Length..].StartsWith(prefix, StringComparison.Ordinal)));
    }

    /// <summary>Only setup not yet done ("off") may be hidden; a broken service or pending restart stays.</summary>
    public static bool IsAttentionDismissible(DashboardHealthItem item) => item.State == "off";

    public static IReadOnlyList<string> ParseHiddenAttention(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return [];
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            return document.RootElement.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string EncodeHiddenAttention(IReadOnlyList<string> ids) => ids.Count == 0 ? "" : JsonSerializer.Serialize(ids);
}
