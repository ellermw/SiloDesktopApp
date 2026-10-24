using System.Globalization;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public sealed record BufferingQualityPlan(string PlanId, string AdoptionId, string Quality,
    string ActiveQuality, int DeliveredKbps, IReadOnlyList<PlaybackQualityV3> Qualities,
    bool ReductionUnavailable = false);
public sealed record BufferingObservation(long AtMs, bool Waiting, bool Paused, bool Seeking,
    bool Outage, bool WatchParty, double BufferedSeconds, int? MeasuredKbps, bool Obscured = false);
public sealed record BufferingQualityAction(string Kind, PlaybackBufferingReplan? Replan = null);
public sealed class BufferingPlanSummary
{
    public long PlayedMs { get; set; }
    public int StallCount { get; set; }
    public long StallTotalMs { get; set; }
    public long StallLongestMs { get; set; }
    public string BufferingMode { get; set; } = "always";
    public string BufferingAction { get; set; } = "none";
    public int? ThroughputKbps { get; set; }
    public IReadOnlyDictionary<string, string> Diagnostics(int deliveredKbps) => new Dictionary<string,string>
    {
        ["played_ms"] = PlayedMs.ToString(CultureInfo.InvariantCulture),
        ["stall_count"] = StallCount.ToString(CultureInfo.InvariantCulture),
        ["stall_total_ms"] = StallTotalMs.ToString(CultureInfo.InvariantCulture),
        ["stall_longest_ms"] = StallLongestMs.ToString(CultureInfo.InvariantCulture),
        ["delivered_kbps"] = deliveredKbps.ToString(CultureInfo.InvariantCulture),
        ["buffering_mode"] = BufferingMode, ["buffering_action"] = BufferingAction,
    }.Concat(ThroughputKbps is int rate ? new Dictionary<string,string> { ["throughput_kbps"] = rate.ToString(CultureInfo.InvariantCulture) } : []).ToDictionary(x => x.Key, x => x.Value);
}

/// <summary>Replayable native adapter of official buffering-quality v1. Owns no player or timers.</summary>
public sealed class BufferingQualityController(string mode)
{
    public string Mode { get; set; } = NormalizeMode(mode);
    public static string NormalizeMode(string? mode) => mode is "ask" or "never" ? mode : "always";
    public string? Pill { get; private set; }
    public long PillRemainingMs { get; private set; }
    public int? KeptEstimateKbps { get; private set; }
    public BufferingPlanSummary Summary { get; private set; } = new();
    public BufferingQualityPlan? Plan { get; private set; }
    private long _last, _grace, _blocked, _reoffer;
    private long? _stall, _pendingAt;
    private bool _played, _answered, _paused, _waiting, _outage, _room, _rendererWait;
    private long _pendingBitrate;
    private bool _pendingUndo, _pendingRestore;
    private bool _cantLowerShown;
    private BufferingObservation? _lastObservation;
    private string? _lastQuality;
    private long? _outageEndedAt;
    private long? _longEnded;
    private readonly Queue<long> _stalls = new();
    public void Adopt(BufferingQualityPlan plan, long atMs)
    {
        if (plan.AdoptionId == Plan?.AdoptionId) return;
        var pending = _pendingAt != null;
        if (!pending && _lastQuality != plan.Quality) { _answered = false; KeptEstimateKbps = null; }
        _lastQuality = plan.Quality;
        Plan = plan; Summary = new() { BufferingMode = Mode };
        _last = atMs; _grace = atMs + 10000; _stall = null; _stalls.Clear(); _longEnded = null;
        _paused = _waiting = _rendererWait = false; Pill = null; _blocked = 0;
        if (pending && !_pendingRestore)
        {
            if (plan.DeliveredKbps >= _pendingBitrate || plan.ReductionUnavailable)
            { Summary.BufferingAction = "cant_lower"; _blocked = atMs + 300000; Pill = "cant_lower"; PillRemainingMs = 15000; }
            else if (_pendingUndo) { Pill = "undo"; PillRemainingMs = 15000; }
            else { Pill = "lowered"; PillRemainingMs = 15000; }
        }
        _pendingAt = null;
    }
    public BufferingQualityAction? Observe(BufferingObservation sample)
    {
        _lastObservation = sample;
        var now = sample.AtMs; var delta = Math.Max(0, now - _last); _last = now;
        Summary.BufferingMode = Mode;
        if (!_paused && !_waiting && _played) Summary.PlayedMs += delta;
        if (sample.MeasuredKbps is >= 100 and <= 1000000 && !sample.Paused && !sample.Seeking && !sample.Outage)
            Summary.ThroughputKbps = sample.MeasuredKbps;
        if (Pill != null && !sample.Paused && !sample.Obscured)
        {
            PillRemainingMs -= delta;
            if (PillRemainingMs <= 0)
            {
                if (Pill == "prompt") { Summary.BufferingAction = "timed_out"; _reoffer = now + 105000; ResetHistory(now); }
                Pill = null;
            }
        }
        if (_pendingAt is long sent && now - sent >= 60000) { _pendingAt = null; ResetHistory(now); }
        if (_room != sample.WatchParty)
        {
            _room = sample.WatchParty; ResetHistory(now); Pill = null; _pendingAt = null;
            if (_room) { KeptEstimateKbps = null; Summary.BufferingAction = "none"; }
        }
        _paused = sample.Paused;
        if (sample.Paused || sample.Seeking || sample.Outage || sample.WatchParty)
        { FinishStall(now, false); _waiting = false; _outage = sample.Outage; return null; }
        if (_outage) { _grace = Math.Max(_grace, now + 6000); _outageEndedAt = now; _outage = false; }
        if (sample.Waiting)
        {
            if (sample.BufferedSeconds >= 5 || (!_waiting && sample.BufferedSeconds > 1))
            { FinishStall(now, false); _rendererWait = true; }
            if (_rendererWait && sample.BufferedSeconds <= 1) _rendererWait = false;
            if (!_rendererWait && _stall == null) _stall = Math.Max(now, _grace);
            _waiting = true;
        }
        else
        {
            FinishStall(now, true); _waiting = _rendererWait = false; _played = true;
        }
        while (_stalls.TryPeek(out var ended) && now - ended >= 120000) _stalls.Dequeue();
        if (Plan == null || _room || _answered || _pendingAt != null || Pill != null || now < _blocked ||
            (!_played && (sample.MeasuredKbps == null || sample.MeasuredKbps >= Plan.DeliveredKbps))) return null;
        if (_stalls.Count < 2 && !(_stall is long start && now - start >= 6000) && !(_longEnded is long l && now - l < 120000)) return null;
        if (Mode == "never") { Summary.BufferingAction = "ignored"; return null; }
        if (LowerRung() == null || (Plan.Quality == "auto" && Plan.DeliveredKbps * 7 / 10 < 100))
        { _blocked = now + 300000; Summary.BufferingAction = "cant_lower"; if (_cantLowerShown) return null; _cantLowerShown = true; return new("cant_lower"); }
        if (Mode == "always" && Plan.Quality == "auto") return Lower(now, true);
        if (now < _reoffer) return null;
        Pill = "prompt"; PillRemainingMs = 15000; return new("prompt");
    }
    public BufferingQualityAction? Accept(long now) => Pill == "prompt" ? Lower(now, false) : null;
    public void Keep() { if (Pill == "prompt") { _answered = true; Summary.BufferingAction = "kept"; } Pill = null; }
    public BufferingQualityAction? Restore(long now)
    {
        if (Pill != "undo" || Plan == null) return null;
        Pill = null; _answered = true; KeptEstimateKbps = null; Summary.BufferingAction = "restored";
        _pendingAt = now; _pendingRestore = true;
        return new("replan", new("auto", LearnedLimit: "clear"));
    }
    public void QualityPicked() { Pill = null; _answered = false; KeptEstimateKbps = null; _pendingAt = null; ResetHistory(_last); }
    public BufferingQualityAction? StartDeadlinePassed(long now)
    {
        if (Plan?.Quality != "auto" || Mode != "always" || _answered || _pendingAt != null || _room || now < _blocked ||
            (_outageEndedAt is long recovered && now - recovered < 6000) ||
            _lastObservation is not { Paused:false, Seeking:false, Outage:false, WatchParty:false, MeasuredKbps: int rate } || rate >= Plan.DeliveredKbps || LowerRung() == null) return null;
        return Lower(now, true);
    }
    public void ReplanFailed(bool unsent) { if (unsent) { _pendingAt = null; KeptEstimateKbps = null; } }
    public BufferingPlanSummary Stop(long now) { FinishStall(now, true); var result = Summary; Plan = null; Pill = null; return result; }
    private PlaybackQualityV3? LowerRung()
    {
        var named = Plan?.Qualities.FirstOrDefault(q => q.Label == Plan.ActiveQuality)?.BitrateKbps;
        var ceiling = Math.Min(named is > 0 ? named.Value : int.MaxValue, Plan?.DeliveredKbps ?? 0);
        return Plan?.Qualities.Where(q => !q.PreservesSource && q.BitrateKbps > 0 && q.BitrateKbps < ceiling).OrderByDescending(q => q.BitrateKbps).FirstOrDefault();
    }
    private BufferingQualityAction? Lower(long now, bool undo)
    {
        var rung = LowerRung(); if (Plan == null || rung == null) return null;
        Pill = null; _pendingAt = now; _pendingBitrate = Plan.DeliveredKbps; _pendingUndo = undo; _pendingRestore = false;
        Summary.BufferingAction = undo ? "lowered" : "accepted";
        if (Plan.Quality != "auto") return new("replan", new(rung.Label));
        KeptEstimateKbps = Plan.DeliveredKbps * 7 / 10;
        return new("replan", new("auto", KeptEstimateKbps, "observed_rebuffer", Summary.ThroughputKbps));
    }
    private void ResetHistory(long now) { _stalls.Clear(); _longEnded = null; _stall = _waiting ? Math.Max(now, _grace) : null; }
    private void FinishStall(long now, bool count)
    {
        if (_stall is not long start) return;
        var duration = Math.Max(0, now - start);
        Summary.StallTotalMs += duration; Summary.StallLongestMs = Math.Max(Summary.StallLongestMs, duration);
        if (duration >= 1000 && count) { Summary.StallCount++; _stalls.Enqueue(now); }
        if (count && duration >= 6000) _longEnded = now;
        _stall = null;
    }
}
