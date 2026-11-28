using System.Diagnostics;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Records dispatcher lateness, the page at the tick, the last navigation or input, playback,
/// the longest measured UI operation and its location, and the last breadcrumb in ui_lag.txt.
/// Locations and actions use code names only. All state belongs to the interface thread except
/// the published crash breadcrumb, which can be read from any thread. Quiet ticks allocate nothing.
/// </summary>
public sealed class UiStallLog
{
    public const long TickMilliseconds = 250;
    public const long StallMilliseconds = 500;
    public const long SummaryMilliseconds = 10 * 60 * 1000;

    public readonly record struct Location(string Page, string? Admin = null)
    {
        public override string ToString() => Admin is null ? Page : $"{Page}/{Admin}";
    }

    public readonly record struct PointerPress(uint PointerId, ulong Timestamp);

    private readonly Func<long> _clock;
    private string? _actionKind, _actionName;
    private long _actionAt;
    private PointerPress? _lastPress;
    private string _breadcrumb = "";
    private long _crumbAt;
    private string? _longestOperation;
    private long _longestOperationMs;
    private Location? _operationOwner, _operationTarget;
    private long _sample;
    private int _windowStalls;
    private long _windowStart, _windowWorst;
    private readonly Dictionary<string, long> _latenessByLocation = new(StringComparer.Ordinal);

    /// <param name="clock">Monotonic milliseconds (Environment.TickCount64 in the app).</param>
    public UiStallLog(Func<long> clock) => _clock = clock;

    public static UiStallLog Shared { get; } = new(() => Environment.TickCount64);

    public long Now => _clock();

    /// <summary>The last named interface operation, published as a single reference for crash reports.</summary>
    public string Breadcrumb => Volatile.Read(ref _breadcrumb);

    /// <summary>Records code-only input, using the original age if the event has already bubbled.</summary>
    public void NoteInput(string kind, string? name, long? ageMs = null, PointerPress? press = null)
    {
        // If this press was already observed before dispatching navigation, ignore its
        // late observation even if clock rounding would make it look newer.
        if (press.HasValue && press == _lastPress) return;
        if (press.HasValue) _lastPress = press;
        var at = Now - (ageMs ?? 0);
        if (_actionKind is not null && at < _actionAt) return;
        SetAction(kind, name, at);
    }

    /// <summary>Records a committed navigation with the time it started, not when it finished.</summary>
    public void NoteNavigation(string kind, string? name, long? startedAt = null) =>
        SetAction(kind, name, startedAt ?? Now);

    private void SetAction(string kind, string? name, long at)
    {
        _actionKind = kind; _actionName = name; _actionAt = at;
    }

    public void SetBreadcrumb(string kind, string? detail = null)
    {
        _crumbAt = Now;
        Volatile.Write(ref _breadcrumb, detail is null ? kind : kind + ":" + detail);
    }

    /// <summary>Measures one synchronous interface-thread operation. Never wrap an await.</summary>
    public Operation Measure(string name, Location owner, Location? target = null)
    {
        SetBreadcrumb(name);
        return new Operation(this, name, owner, target, Stopwatch.GetTimestamp());
    }

    public readonly struct Operation : IDisposable
    {
        private readonly UiStallLog? _log;
        private readonly string _name;
        private readonly Location _owner;
        private readonly Location? _target;
        private readonly long _started;

        internal Operation(UiStallLog log, string name, Location owner, Location? target, long started)
        { _log = log; _name = name; _owner = owner; _target = target; _started = started; }

        public void Dispose() => _log?.RecordOperation(_name, (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds, _owner, _target);
    }

    /// <summary>Records an operation's duration and the location captured at its start.</summary>
    public void RecordOperation(string name, long milliseconds, Location owner, Location? target = null)
    {
        if (milliseconds <= _longestOperationMs) return;
        _longestOperation = name; _longestOperationMs = milliseconds;
        _operationOwner = owner; _operationTarget = target;
    }

    /// <summary>Clears only the current tick's operations, not the last action or breadcrumb.</summary>
    public void Quiet()
    {
        _longestOperation = null; _longestOperationMs = 0;
        _operationOwner = _operationTarget = null;
    }

    // Even coarse boot-clock reads should agree within two clock ticks. A scheduling pause
    // between the reads leaves no trustworthy pair; the monitor then drops that baseline.
    internal static (long Boot, long Awake)? ReadTickClocks(Func<long> bootClock, Func<long> awakeClock)
    {
        var before = bootClock();
        var awake = awakeClock();
        var span = bootClock() - before;
        if (awake < 0 || span < 0 || span > 32) return null;
        return (before + span / 2, awake);
    }

    /// <summary>Returns awake-time lateness beyond the expected tick, or null for an invalid or suspend-spanning sample.</summary>
    public static long? TickLateness(long delayMs, long awakeMs)
    {
        // The boot and awake clocks have different resolutions; ignore small differences in their deltas.
        if (delayMs < 0 || awakeMs < 0 || Math.Abs(delayMs - awakeMs) > 100) return null;
        return Math.Max(0, awakeMs - TickMilliseconds);
    }

    /// <summary>
    /// Attributes lateness to the longest operation's location (or transition), falling back to the
    /// sampled location if nothing was measured. The attribution is a clue, not proven blocked time.
    /// </summary>
    public string Stall(long delayMs, long lateMs, long managedMb, Location sampled, string playback)
    {
        var now = Now;
        var owner = _operationOwner?.ToString();
        var target = _operationTarget?.ToString();
        var bucket = (owner is not null && target is not null && owner != target ? $"{owner}->{target}" : owner) ?? sampled.ToString();
        var crumb = Breadcrumb;
        var line = $"sample={++_sample} | delay_ms={delayMs} | late_ms={lateMs} | managed_mb={managedMb} | page={sampled.Page} | admin={sampled.Admin ?? "-"} | " +
            (_actionKind is null ? "last=none" : $"last={_actionKind}:{_actionName ?? "-"} {now - _actionAt}ms ago") +
            $" | playback={playback} | " +
            (_longestOperation is null ? "longest_op=none" : $"longest_op={_longestOperation} {_longestOperationMs}ms | op_owner={owner}" +
                (target is null ? "" : $" | op_target={target}")) +
            " | " + (crumb.Length == 0 ? "breadcrumb=" : $"breadcrumb={crumb} {now - _crumbAt}ms ago");
        if (_windowStalls++ == 0) _windowStart = now;
        if (lateMs > _windowWorst) _windowWorst = lateMs;
        _latenessByLocation[bucket] = _latenessByLocation.GetValueOrDefault(bucket) + lateMs;
        Quiet();
        return line;
    }

    /// <summary>True once ten minutes have passed since the first stall of the current summary window.</summary>
    public bool SummaryDue => _windowStalls > 0 && Now - _windowStart >= SummaryMilliseconds;

    /// <summary>Summarizes the count, worst lateness, and three locations with the most lateness.</summary>
    public string? TakeSummary()
    {
        if (!SummaryDue) return null;
        var top = string.Join(",", _latenessByLocation.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(3).Select(entry => $"{entry.Key}:{entry.Value}ms"));
        var line = $"summary | minutes={(Now - _windowStart) / 60000} | stalls={_windowStalls} | worst_late_ms={_windowWorst} | most_late_time={top}";
        _windowStalls = 0; _windowWorst = 0; _latenessByLocation.Clear();
        return line;
    }
}
