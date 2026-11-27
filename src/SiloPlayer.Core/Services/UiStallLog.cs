using System.Diagnostics;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Says where an interface-thread stall happened, for ui_lag.txt: the page (and Admin page), the
/// last navigation or input and how long before, whether playback is on, the longest measured
/// interface operation in the stalled window, and the last named operation (breadcrumb). Every
/// value is a code name (a page type, an operation name, an input kind), never a title, id, URL
/// or account value. At most once per ten minutes with stalls it adds a summary line.
/// Interface thread only. A quiet tick allocates nothing; a stall allocates its line.
/// </summary>
public sealed class UiStallLog
{
    /// <summary>A tick this late means the interface thread was blocked at least this long.</summary>
    public const long StallMilliseconds = 500;
    public const long SummaryMilliseconds = 10 * 60 * 1000;

    private readonly Func<long> _clock;
    private string? _actionKind, _actionName;
    private long _actionAt;
    private string? _crumbKind, _crumbDetail;
    private long _crumbAt;
    private string? _longestOperation;
    private long _longestOperationMs;
    private long _sample;
    private int _windowStalls;
    private long _windowStart, _windowWorst;
    private readonly Dictionary<string, long> _stallMsByPage = new(StringComparer.Ordinal);

    /// <param name="clock">Monotonic milliseconds (Environment.TickCount64 in the app).</param>
    public UiStallLog(Func<long> clock) => _clock = clock;

    public static UiStallLog Shared { get; } = new(() => Environment.TickCount64);

    /// <summary>The last named interface operation, for crash reports.</summary>
    public string Breadcrumb => _crumbKind is null ? "" : _crumbDetail is null ? _crumbKind : _crumbKind + ":" + _crumbDetail;

    /// <summary>A navigation or an input: <paramref name="kind"/> is "navigate", "back", "click" or "key"; <paramref name="name"/> a page or control type.</summary>
    public void NoteAction(string kind, string? name)
    {
        _actionKind = kind; _actionName = name; _actionAt = _clock();
    }

    public void SetBreadcrumb(string kind, string? detail = null)
    {
        _crumbKind = kind; _crumbDetail = detail; _crumbAt = _clock();
    }

    /// <summary>Measures one synchronous interface-thread operation. Never wrap an await.</summary>
    public Operation Measure(string name)
    {
        SetBreadcrumb(name);
        return new Operation(this, name, Stopwatch.GetTimestamp());
    }

    public readonly struct Operation : IDisposable
    {
        private readonly UiStallLog? _log;
        private readonly string _name;
        private readonly long _started;

        internal Operation(UiStallLog log, string name, long started) { _log = log; _name = name; _started = started; }

        public void Dispose() => _log?.RecordOperation(_name, (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
    }

    /// <summary>Records an operation's duration; the longest in the current tick window is reported with a stall.</summary>
    public void RecordOperation(string name, long milliseconds)
    {
        if (milliseconds <= _longestOperationMs) return;
        _longestOperation = name; _longestOperationMs = milliseconds;
    }

    /// <summary>A tick that came on time: the window had no stall, so its operations are not reported.</summary>
    public void Quiet()
    {
        _longestOperation = null; _longestOperationMs = 0;
    }

    /// <summary>The ui_lag.txt line for a stall of <paramref name="delayMs"/>; counts it toward the summary.</summary>
    public string Stall(long delayMs, long managedMb, string page, string? adminPage, string playback)
    {
        var now = _clock();
        var line = $"sample={++_sample} | delay_ms={delayMs} | managed_mb={managedMb} | page={page} | admin={adminPage ?? "-"} | " +
            (_actionKind is null ? "last=none" : $"last={_actionKind}:{_actionName ?? "-"} {now - _actionAt}ms ago") +
            $" | playback={playback} | " +
            (_longestOperation is null ? "longest_op=none" : $"longest_op={_longestOperation} {_longestOperationMs}ms") +
            " | " + (_crumbKind is null ? "breadcrumb=" : $"breadcrumb={_crumbKind}{(_crumbDetail is null ? "" : ":")}{_crumbDetail} {now - _crumbAt}ms ago");
        if (_windowStalls++ == 0) _windowStart = now;
        if (delayMs > _windowWorst) _windowWorst = delayMs;
        _stallMsByPage[page] = _stallMsByPage.GetValueOrDefault(page) + delayMs;
        Quiet();
        return line;
    }

    /// <summary>True once ten minutes have passed since the first stall of the current summary window.</summary>
    public bool SummaryDue => _windowStalls > 0 && _clock() - _windowStart >= SummaryMilliseconds;

    /// <summary>The summary line (stall count, worst, the three pages with the most stall time); starts a new window.</summary>
    public string? TakeSummary()
    {
        if (!SummaryDue) return null;
        var top = string.Join(",", _stallMsByPage.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(3).Select(entry => $"{entry.Key}:{entry.Value}ms"));
        var line = $"summary | minutes={(_clock() - _windowStart) / 60000} | stalls={_windowStalls} | worst_ms={_windowWorst} | most_stall_time={top}";
        _windowStalls = 0; _windowWorst = 0; _stallMsByPage.Clear();
        return line;
    }
}
