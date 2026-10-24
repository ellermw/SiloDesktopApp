using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public partial class PlaybackApi
{
    public ApiIdentityContext CaptureConnectionOwner() => client.CaptureIdentityContext();
    public bool IsCurrentConnectionOwner(ApiIdentityContext owner) => client.IsCurrentIdentityContext(owner);
    public async Task<PlaybackCapabilityV3> GetConnectionCapabilityAsync(ApiIdentityContext owner, CancellationToken ct = default)
    {
        var value = await client.SendIdentityBoundRequestAsync<PlaybackCapabilityV3>(owner, HttpMethod.Get, "/api/v2/playback/capabilities", null, ct).ConfigureAwait(false);
        EnsureAuthority(owner); return value;
    }
    public async Task<PlaybackConnectionState> GetConnectionAsync(ApiIdentityContext owner, CancellationToken ct = default)
    {
        var result = await client.SendIdentityBoundRequestAsync<PlaybackConnectionState>(owner, HttpMethod.Get, "/api/v2/playback/connection", null, ct).ConfigureAwait(false);
        EnsureAuthority(owner); return result;
    }
    public async Task ResetLearnedConnectionLimitAsync(ApiIdentityContext owner, CancellationToken ct = default)
    {
        await client.SendIdentityBoundNoContentAsync(owner, HttpMethod.Delete, "/api/v2/playback/connection/learned-limit", null, ct).ConfigureAwait(false);
        EnsureAuthority(owner);
    }
    public async Task ReportConnectionProbeAsync(ApiIdentityContext owner, PlaybackProbeResult result, CancellationToken ct = default)
    {
        await client.SendIdentityBoundNoContentAsync(owner, HttpMethod.Post, "/api/v2/playback/bandwidth-probe/results", new Dictionary<string, object?>
        {
            ["kbps"] = result.Kbps, ["bytes"] = result.Bytes, ["duration_ms"] = result.DurationMs,
            ["transport"] = result.Transport, ["lower_bound"] = result.LowerBound, ["manual"] = result.Manual,
        }, ct).ConfigureAwait(false);
        EnsureAuthority(owner);
    }
    public Task<PlaybackProbeResult?> MeasureConnectionAsync(ApiIdentityContext owner, PlaybackCapabilityV3 capability,
        bool manual, string transport, CancellationToken ct = default)
        => client.MeasurePlaybackConnectionAsync(owner, capability, manual, transport, ct);
}
