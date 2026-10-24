using System.Runtime.CompilerServices;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>One connection observer per authenticated client, shared by settings and playback.</summary>
public sealed class PlaybackConnectionService
{
    private static readonly ConditionalWeakTable<PlaybackApi, PlaybackConnectionService> Instances = new();
    public static PlaybackConnectionService For(PlaybackApi api) => Instances.GetValue(api, a => new(a));
    private readonly PlaybackApi _api;
    private readonly SemaphoreSlim _gate = new(1,1);
    private CancellationTokenSource? _pending;
    private ApiIdentityContext? _owner;
    private string _network = "";
    private bool _mediaActive, _metered;
    private DateTimeOffset _notBefore;
    private PlaybackConnectionService(PlaybackApi api) => _api = api;
    public PlaybackConnectionState? State { get; private set; }
    public PlaybackCapabilityV3? Capability { get; private set; }
    public void SetMediaActive(bool active)
    {
        lock (this) { _mediaActive = active; if (active) _pending?.Cancel(); }
    }
    public void Retire()
    {
        lock (this) { _pending?.Cancel(); State = null; Capability = null; _owner = null; }
    }
    public async Task RefreshAsync(string network, bool metered, string transport, bool allowAutomatic, CancellationToken ct = default)
    {
        var owner = _api.CaptureConnectionOwner();
        CancellationTokenSource pending;
        lock (this)
        {
            _pending?.Cancel();
            if (_owner != owner || _network != network) { State = null; Capability = null; }
            _owner = owner; _network = network; _metered = metered;
            pending = CancellationTokenSource.CreateLinkedTokenSource(ct); _pending = pending;
        }
        try
        {
            var cap = await _api.GetConnectionCapabilityAsync(owner, pending.Token).ConfigureAwait(false);
            if (!cap.Features.Contains("connection_speed_v1")) { Install(owner, network, cap, null); return; }
            var state = await _api.GetConnectionAsync(owner, pending.Token).ConfigureAwait(false);
            if (!Install(owner, network, cap, state)) return;
            if (!allowAutomatic || !AutomaticAllowed() || cap.BandwidthProbeAutomatic == false || ProvesConnection(state, cap, DateTimeOffset.UtcNow)) return;
            await Task.Delay(5000, pending.Token).ConfigureAwait(false);
            if (!AutomaticAllowed() || !Current(owner, network)) return;
            await RunProbeAsync(owner, network, cap, false, transport, pending.Token).ConfigureAwait(false);
        }
        finally { lock(this) { if (ReferenceEquals(_pending, pending)) _pending = null; } pending.Dispose(); }
    }
    public async Task<PlaybackProbeResult> TestAsync(string network, bool metered, string transport, CancellationToken ct = default)
    {
        var owner = _api.CaptureConnectionOwner();
        CancellationTokenSource pending;
        lock(this)
        {
            if (_mediaActive) throw new InvalidOperationException("Stop playback before testing connection speed.");
            if (metered) throw new InvalidOperationException("Connection tests are unavailable on a metered connection.");
            _pending?.Cancel();
            if (_owner != owner || _network != network) State = null;
            _owner = owner; _network = network; _metered = metered;
            pending = CancellationTokenSource.CreateLinkedTokenSource(ct); _pending = pending;
        }
        try
        {
            var cap = await _api.GetConnectionCapabilityAsync(owner, pending.Token).ConfigureAwait(false);
            if (!Install(owner, network, cap, State)) throw new OperationCanceledException();
            return await RunProbeAsync(owner, network, cap, true, transport, pending.Token).ConfigureAwait(false);
        }
        finally { lock(this) { if (ReferenceEquals(_pending, pending)) _pending = null; } pending.Dispose(); }
    }
    public async Task ResetAsync(CancellationToken ct = default)
    {
        var owner = _api.CaptureConnectionOwner();
        string network; lock(this) network = _network;
        var cap = await _api.GetConnectionCapabilityAsync(owner, ct).ConfigureAwait(false);
        if (!cap.Features.Contains("buffering_quality_v1")) throw new NotSupportedException("This server does not support learned connection limits.");
        await _api.ResetLearnedConnectionLimitAsync(owner, ct).ConfigureAwait(false);
        var state = await _api.GetConnectionAsync(owner, ct).ConfigureAwait(false);
        Install(owner, network, cap, state);
    }
    public static bool ProvesConnection(PlaybackConnectionState state, PlaybackCapabilityV3 cap, DateTimeOffset now)
    {
        var max = TimeSpan.FromSeconds(cap.BandwidthProbeMaxAgeSeconds is > 0 ? cap.BandwidthProbeMaxAgeSeconds.Value : 604800);
        bool Current(PlaybackConnectionSample s) => s.Kbps is >=100 and <=1000000 && now - s.MeasuredAt <= max;
        bool Enough(int rate) => cap.BandwidthProbeTargetKbps is not int target || rate >= target;
        return state.Probe is { } p && Current(p) && (!p.LowerBound || Enough(p.Kbps)) ||
            state.PlaybackSample is { } s && Current(s) && Enough(s.Kbps);
    }
    private bool Current(ApiIdentityContext owner, string network) { lock(this) return _owner == owner && _network == network && _api.IsCurrentConnectionOwner(owner); }
    private bool Install(ApiIdentityContext owner, string network, PlaybackCapabilityV3 cap, PlaybackConnectionState? state)
    { lock(this) { if (!Current(owner, network)) return false; Capability = cap; State = state; return true; } }
    private bool AutomaticAllowed() { lock(this) return !_mediaActive && !_metered && DateTimeOffset.UtcNow >= _notBefore; }
    private async Task<PlaybackProbeResult> RunProbeAsync(ApiIdentityContext owner, string network, PlaybackCapabilityV3 cap, bool manual, string transport, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock(this) { if (_mediaActive || _metered || !Current(owner, network)) throw new OperationCanceledException("Connection changed or playback began."); }
            var result = await _api.MeasureConnectionAsync(owner, cap, manual, transport, ct).ConfigureAwait(false)
                ?? throw new IOException("The connection speed could not be measured.");
            if (!Current(owner, network)) throw new OperationCanceledException();
            await _api.ReportConnectionProbeAsync(owner, result, ct).ConfigureAwait(false);
            var state = await _api.GetConnectionAsync(owner, ct).ConfigureAwait(false);
            Install(owner, network, cap, state);
            return result;
        }
        catch (ApiException ex) when (ex.StatusCode == 429)
        { lock(this) _notBefore = DateTimeOffset.UtcNow.AddSeconds(ex.RetryAfterSeconds ?? 30); throw; }
        finally { _gate.Release(); }
    }
}
