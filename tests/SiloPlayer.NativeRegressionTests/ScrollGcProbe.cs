using System.Diagnostics.Tracing;
using System.Text;

/// <summary>
/// Every garbage collection while a page is measured, from the runtime's own GC events (in process,
/// Microsoft-Windows-DotNETRuntime, GC keyword, verbose). Per collection: generation, reason, blocking or
/// background, the pause (SuspendEE begin to RestartEE end), the part of it spent before the GC starts its
/// own work (GCToEEInterface::GcStartWork runs between SuspendEE end and GCStart; on a gen-2 collection that is
/// the XAML reference-tracker walk over every native object wrapper, ComWrappersNative::OnFullGCStarted), the
/// mark phases, generation sizes before and after, promoted bytes and the GC handle count. Between
/// collections: sampled allocations by type (GCAllocationTick, one per ~100 KB), memory pressure added and
/// removed (IncreaseMemoryPressure: XAML reports native memory through IReferenceTrackerHost.AddMemoryPressure)
/// and finalizer runs.
/// </summary>
internal sealed class ScrollGcProbe : EventListener
{
    internal sealed class Collection
    {
        public long Count; public int Depth, Reason, Type;
        public DateTime Start, End;
        public readonly long[] SizeAfter = new long[5], Promoted = new long[5];
        public long Handles, FinalizationPromotedCount, Pinned;
        public long[]? SizeBefore;
        public double? InfoPauseMs; public bool? Compacted, Concurrent;
        public readonly List<(int Type, long Bytes, DateTime At)> Marks = new();
        public string GlobalMechanisms = "";
    }

    internal sealed class Pause
    {
        public DateTime SuspendBegin, SuspendEnd, RestartBegin, RestartEnd;
        public int SuspendReason; public long Thread;
        public long? GcStarted; public DateTime GcStartAt;
        public double Ms => (RestartEnd - SuspendBegin).TotalMilliseconds;
        public double SuspendMs => (SuspendEnd - SuspendBegin).TotalMilliseconds;
        public double BeforeGcStartMs => GcStarted == null ? 0 : (GcStartAt - SuspendEnd).TotalMilliseconds;
    }

    private readonly object _lock = new();
    /// <summary>The GC handle count after the latest collection (each native object wrapper, each managed object a native object holds, and each reference the tracker walk found keeps one).</summary>
    internal (long Gc, long Count) LastHandles;
    private readonly Dictionary<long, Collection> _collections = new();
    private readonly List<Pause> _pauses = new();
    private Pause? _open;
    private Collection? _marking;
    private readonly List<(DateTime At, long Bytes, string Type)> _allocations = new(65536);
    private readonly List<(DateTime At, long Bytes)> _pressure = new();
    private readonly List<(DateTime At, int Count)> _finalizers = new();

    private static readonly string[] Reasons = ["alloc_soh", "induced", "lowmemory", "empty", "alloc_loh", "oos_soh", "oos_loh", "induced_noforce",
        "gcstress", "lowmemory_blocking", "induced_compacting", "lowmemory_host", "pm_full_gc", "lowmemory_host_blocking", "bgc_tuning_soh",
        "bgc_tuning_loh", "bgc_stepping", "induced_aggressive"];
    private static readonly string[] Types = ["blocking", "background", "foreground"];
    private static readonly string[] MarkTypes = ["stack", "finalize-queue", "handles", "older", "sizedref", "overflow", "dependent-handles", "new-fq", "steal", "bgc"];

    /// <summary>Verbose adds allocation sampling and memory-pressure calls; it also makes the listener allocate per event.</summary>
    internal static readonly bool Verbose = Environment.GetEnvironmentVariable("SILO_SCROLL_GC") == "alloc";

    /// <summary>The UI thread's OS id: a collection it starts was asked for by code running on it (XAML, or the app).</summary>
    internal static long UiThread;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, Verbose ? EventLevel.Verbose : EventLevel.Informational, (EventKeywords)0x1);
    }

    // Types the listener itself allocates for every event it receives.
    private static readonly string[] ListenerTypes = ["System.Diagnostics.Tracing.", "MoreEventInfo", "System.Object[]", "System.Collections.ObjectModel.ReadOnlyCollection", "System.UInt16", "System.UInt32", "System.UInt64", "System.Int64", "System.IntPtr", "System.Guid", "System.String"];

    private static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);

    private static long L(EventWrittenEventArgs e, string name)
    {
        var i = e.PayloadNames?.IndexOf(name) ?? -1;
        return i < 0 || e.Payload == null ? 0 : Convert.ToInt64(e.Payload[i]);
    }

    private static string S(EventWrittenEventArgs e, string name)
    {
        var i = e.PayloadNames?.IndexOf(name) ?? -1;
        return i < 0 || e.Payload == null ? "" : e.Payload[i]?.ToString() ?? "";
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        var name = e.EventName ?? "";
        var cut = name.IndexOf("_V", StringComparison.Ordinal);
        if (cut > 0) name = name[..cut];
        var at = Utc(e.TimeStamp);
        lock (_lock)
        {
            switch (name)
            {
                case "GCSuspendEEBegin":
                    _open = new Pause { SuspendBegin = at, SuspendReason = (int)L(e, "Reason"), Thread = e.OSThreadId };
                    break;
                case "GCSuspendEEEnd":
                    if (_open != null) _open.SuspendEnd = at;
                    break;
                case "GCStart":
                {
                    var c = new Collection { Count = L(e, "Count"), Depth = (int)L(e, "Depth"), Reason = (int)L(e, "Reason"), Type = (int)L(e, "Type"), Start = at };
                    _collections[c.Count] = c;
                    _marking = c;
                    if (_open is { GcStarted: null }) { _open.GcStarted = c.Count; _open.GcStartAt = at; }
                    break;
                }
                case "GCMarkWithType":
                    _marking?.Marks.Add(((int)L(e, "Type"), L(e, "Bytes"), at));
                    break;
                case "GCGlobalHeapHistory":
                    if (_marking != null) _marking.GlobalMechanisms = $"mechanisms=0x{L(e, "GlobalMechanisms"):x} pauseMode={L(e, "PauseMode")} memoryPressure={L(e, "MemoryPressure")}";
                    break;
                case "GCHeapStats":
                {
                    if (_marking == null) break;
                    var c = _marking;
                    for (var g = 0; g < 5; g++) { c.SizeAfter[g] = L(e, $"GenerationSize{g}"); c.Promoted[g] = L(e, $"TotalPromotedSize{g}"); }
                    c.Handles = L(e, "GCHandleCount"); LastHandles = (c.Count, c.Handles); c.FinalizationPromotedCount = L(e, "FinalizationPromotedCount"); c.Pinned = L(e, "PinnedObjectCount");
                    var info = GC.GetGCMemoryInfo(GCKind.Any);
                    if (info.Index == c.Count)
                    {
                        c.SizeBefore = info.GenerationInfo.ToArray().Select(g => g.SizeBeforeBytes).ToArray();
                        c.InfoPauseMs = info.PauseDurations.ToArray().Sum(p => p.TotalMilliseconds);
                        c.Compacted = info.Compacted; c.Concurrent = info.Concurrent;
                    }
                    break;
                }
                case "GCEnd":
                    if (_collections.TryGetValue(L(e, "Count"), out var ended)) ended.End = at;
                    break;
                case "GCRestartEEBegin":
                    if (_open != null) _open.RestartBegin = at;
                    break;
                case "GCRestartEEEnd":
                    if (_open != null) { _open.RestartEnd = at; _pauses.Add(_open); _open = null; }
                    break;
                case "GCAllocationTick":
                    _allocations.Add((at, L(e, "AllocationAmount64") is var big && big > 0 ? big : L(e, "AllocationAmount"), S(e, "TypeName")));
                    break;
                case "IncreaseMemoryPressure":
                    _pressure.Add((at, L(e, "BytesAllocated")));
                    break;
                case "DecreaseMemoryPressure":
                    _pressure.Add((at, -L(e, "BytesFreed")));
                    break;
                case "GCFinalizersEnd":
                    _finalizers.Add((at, (int)L(e, "Count")));
                    break;
            }
        }
    }

    /// <summary>Every collection and pause between <paramref name="from"/> and <paramref name="to"/> (UTC), with the allocation and memory pressure behind them.</summary>
    internal (string Summary, string Detail, double WorstPauseMs) Describe(string segment, DateTime from, DateTime to)
    {
        lock (_lock)
        {
            var pauses = _pauses.Where(p => p.SuspendBegin >= from && p.SuspendBegin <= to).ToList();
            var collections = _collections.Values.Where(c => c.Start >= from && c.Start <= to).OrderBy(c => c.Count).ToList();
            var allocations = _allocations.Where(a => a.At >= from && a.At <= to && !ListenerTypes.Any(l => a.Type.StartsWith(l, StringComparison.Ordinal))).ToList();
            var pressure = _pressure.Where(p => p.At >= from && p.At <= to).ToList();
            var finalized = _finalizers.Where(f => f.At >= from && f.At <= to).Sum(f => f.Count);
            var detail = new StringBuilder();
            detail.AppendLine($"== {segment} ({(to - from).TotalMilliseconds:0} ms)");
            foreach (var c in collections)
            {
                var pause = pauses.FirstOrDefault(p => p.GcStarted == c.Count);
                var marks = new List<string>();
                var previous = c.Start;
                foreach (var (type, bytes, markAt) in c.Marks)
                {
                    marks.Add($"{(type >= 0 && type < MarkTypes.Length ? MarkTypes[type] : type.ToString())} {(markAt - previous).TotalMilliseconds:0.0}ms/{bytes / 1024}KB");
                    previous = markAt;
                }
                string Mb(long b) => (b / 1048576.0).ToString("0.0");
                detail.AppendLine($"  gc#{c.Count} gen{c.Depth} {(c.Type >= 0 && c.Type < Types.Length ? Types[c.Type] : c.Type.ToString())} reason={(c.Reason >= 0 && c.Reason < Reasons.Length ? Reasons[c.Reason] : c.Reason.ToString())}"
                    + $" at +{(c.Start - from).TotalMilliseconds:0}ms thread={(pause == null ? "?" : pause.Thread == UiThread ? "UI" : pause.Thread.ToString())}"
                    + $" pause={(pause?.Ms ?? 0):0.0}ms (suspend {(pause?.SuspendMs ?? 0):0.0}, before GC work {(pause?.BeforeGcStartMs ?? 0):0.0}, GC {(c.End > c.Start ? (c.End - c.Start).TotalMilliseconds : 0):0.0}) infoPause={c.InfoPauseMs:0.0}ms compacted={c.Compacted} concurrent={c.Concurrent}");
                detail.AppendLine($"    gen0/1/2/LOH/POH MB before {(c.SizeBefore == null ? "?" : string.Join("/", c.SizeBefore.Take(5).Select(Mb)))} after {string.Join("/", c.SizeAfter.Select(Mb))}"
                    + $" promoted {string.Join("/", c.Promoted.Select(Mb))} handles={c.Handles} finalizationPromoted={c.FinalizationPromotedCount} pinned={c.Pinned} {c.GlobalMechanisms}");
                if (marks.Count > 0) detail.AppendLine("    mark: " + string.Join(", ", marks));
            }
            foreach (var p in pauses.Where(p => p.GcStarted == null || collections.All(c => c.Count != p.GcStarted)))
                detail.AppendLine($"  pause {(p.Ms):0.0}ms (suspend reason {p.SuspendReason}, GC started {(p.GcStarted?.ToString() ?? "none")}) at +{(p.SuspendBegin - from).TotalMilliseconds:0}ms");
            var byType = allocations.GroupBy(a => a.Type).Select(g => (Type: g.Key, Bytes: g.Sum(a => a.Bytes), Samples: g.Count())).OrderByDescending(t => t.Bytes).Take(12).ToList();
            var sampled = allocations.Sum(a => a.Bytes);
            detail.AppendLine($"  sampled allocation (listener's own types left out) {sampled / 1048576.0:0.0}MB in {allocations.Count} ticks; top types: "
                + string.Join("; ", byType.Select(t => $"{t.Type} {t.Bytes / 1024}KB x{t.Samples}")));
            detail.AppendLine($"  memory pressure added {pressure.Where(p => p.Bytes > 0).Sum(p => p.Bytes) / 1048576.0:0.0}MB in {pressure.Count(p => p.Bytes > 0)} calls, removed {-pressure.Where(p => p.Bytes < 0).Sum(p => p.Bytes) / 1048576.0:0.0}MB; finalized {finalized} objects");
            var worst = pauses.Count == 0 ? 0 : pauses.Max(p => p.Ms);
            var gen2 = collections.Where(c => c.Depth >= 2).ToList();
            var summary = $"collections {collections.Count} (gen0 {collections.Count(c => c.Depth == 0)}, gen1 {collections.Count(c => c.Depth == 1)}, gen2 {gen2.Count}: "
                + string.Join(" ", gen2.Select(c => $"{(c.Type >= 0 && c.Type < Types.Length ? Types[c.Type] : "?")}/{(c.Reason >= 0 && c.Reason < Reasons.Length ? Reasons[c.Reason] : "?")}/"
                    + $"{(pauses.FirstOrDefault(p => p.GcStarted == c.Count)?.Ms ?? 0):0}ms(walk+setup {(pauses.FirstOrDefault(p => p.GcStarted == c.Count)?.BeforeGcStartMs ?? 0):0})"))
                + $") worst pause {worst:0.0}ms; sampled alloc {sampled / 1048576.0:0.0}MB; pressure +{pressure.Where(p => p.Bytes > 0).Sum(p => p.Bytes) / 1048576.0:0.0}MB";
            return (summary, detail.ToString(), worst);
        }
    }

    /// <summary>The pause of collection <paramref name="count"/>: total, the part before the GC's own work (the reference-tracker walk on a gen-2 collection) and the GC's work.</summary>
    internal string PauseOf(long count)
    {
        lock (_lock)
        {
            if (!_collections.TryGetValue(count, out var c)) return "not seen";
            var pause = _pauses.FirstOrDefault(p => p.GcStarted == count);
            return $"gen{c.Depth} pause {(pause?.Ms ?? 0):0.0} ms (walk and setup {(pause?.BeforeGcStartMs ?? 0):0.0} ms, GC work {(c.End > c.Start ? (c.End - c.Start).TotalMilliseconds : 0):0.0} ms)";
        }
    }

    /// <summary>Wall-clock pause of every gen-0/1 and gen-2 collection seen so far (for whole-run statistics).</summary>
    internal string PauseStatistics()
    {
        lock (_lock)
        {
            string Line(int depth)
            {
                var list = _pauses.Where(p => p.GcStarted is { } n && _collections.TryGetValue(n, out var c) && (depth == 2 ? c.Depth >= 2 : c.Depth == depth))
                    .Select(p => (p.Ms, p.BeforeGcStartMs)).OrderBy(p => p.Ms).ToList();
                return list.Count == 0 ? $"gen{depth}: none" : $"gen{depth}: {list.Count} pauses, median {list[list.Count / 2].Ms:0.0}ms, max {list[^1].Ms:0.0}ms, before-GC-work median {list.OrderBy(p => p.BeforeGcStartMs).ElementAt(list.Count / 2).BeforeGcStartMs:0.0}ms max {list.Max(p => p.BeforeGcStartMs):0.0}ms";
            }
            return $"{Line(0)}; {Line(1)}; {Line(2)}";
        }
    }
}
