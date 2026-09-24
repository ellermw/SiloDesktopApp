using System.Collections.Concurrent;
using System.Globalization;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public partial class PlaybackApi
{
    // Keep the authority and installation of each attempt, including superseded
    // sessions still awaiting stop. Never relabel an old session after a server switch.
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private readonly ConcurrentQueue<string> _retiredSessions = new();
    private const int RetainedStopAttempts = 64;

    private sealed class SessionState(string installationId, ApiRequestContext context)
    {
        public string InstallationId { get; } = installationId;
        public ApiRequestContext Context { get; } = context;
        public object Gate { get; } = new();
        public long Sequence;
        public PlaybackAcceptedSample? LastSample;
        public Dictionary<string, object?>? StopBody;
    }

    private async Task<PlaybackDecisionResponseV3> StartV2Async(PlaybackStartRequestV3 request, CancellationToken ct)
    {
        var authority = client.CaptureContext();
        foreach (var entry in _sessions)
            if (entry.Value.Context != authority) _sessions.TryRemove(entry.Key, out _);
        for (var attempt = 0; ; attempt++)
        {
            var capability = await GetPlaybackCapabilityAsync(ct).ConfigureAwait(false);
            EnsureAuthority(authority);
            if (!capability.Allowed || capability.State != "available" ||
                !capability.ProtocolVersions.Contains(3) || string.IsNullOrEmpty(capability.InstallationId))
                throw new InvalidOperationException("This server does not currently support native playback API v2.");
            request.InstallationId = capability.InstallationId;
            // Preserve the viewer's source/timeline through seek and recovery.
            request.AllowAlternateVersions = capability.Features.Contains("fixed_media_file_v1") ? false : null;
            var body = StartBody(request);
            try
            {
                var decision = await RetryDeliveryAsync(
                    token => client.PostAsync<PlaybackDecisionResponseV3>("/api/v2/playback/start", body, token),
                    authority, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);
                EnsureAuthority(authority);
                var session = decision.PlaybackPlan?.SessionId ?? decision.SessionId;
                if (decision.Outcome == "playable" && !string.IsNullOrEmpty(session))
                    _sessions.TryAdd(session, new SessionState(capability.InstallationId, authority));
                return decision;
            }
            catch (ApiException ex) when (ex.ErrorCode == "installation_changed" && attempt == 0)
            {
                EnsureAuthority(authority);
                request.PlaybackAttemptId = Guid.NewGuid().ToString();
            }
        }
    }

    private static Dictionary<string, object?> StartBody(PlaybackStartRequestV3 request)
    {
        var body = new Dictionary<string, object?>
        {
            ["installation_id"] = request.InstallationId,
            ["protocol_version"] = request.ProtocolVersion,
            ["client_features"] = request.ClientFeatures,
            ["file_id"] = request.FileId.ToString(CultureInfo.InvariantCulture),
            ["profile_id"] = request.ProfileId,
            ["playback_attempt_id"] = request.PlaybackAttemptId,
            ["quality_preference"] = request.QualityPreference,
            ["subtitle_fidelity_preference"] = request.SubtitleFidelityPreference,
            ["metered"] = request.Metered,
            ["client_capabilities"] = request.ClientCapabilities,
            ["client_playback_context"] = request.ClientPlaybackContext,
        };
        void Optional(string key, object? value) { if (value != null) body[key] = value; }
        Optional("allow_alternate_versions", request.AllowAlternateVersions);
        Optional("start_position", request.StartPosition);
        Optional("progress_persistence", request.ProgressPersistence);
        Optional("audio_track_id", request.AudioTrackId);
        Optional("audio_track_index", request.AudioTrackIndex);
        Optional("subtitle_track_id", request.SubtitleTrackId);
        Optional("subtitle_track_index", request.SubtitleTrackIndex);
        Optional("bandwidth_estimate_kbps", request.BandwidthEstimateKbps);
        Optional("bandwidth_cap_kbps", request.BandwidthCapKbps);
        return body;
    }

    private async Task<PlaybackDecisionResponseV3> ReplanV2Async(string sessionId, PlaybackReplanRequestV3 request, CancellationToken ct)
    {
        var state = Session(sessionId);
        request.InstallationId = state.InstallationId;
        var decision = await client.PostAsync<PlaybackDecisionResponseV3>(
            $"/api/v2/playback/{Uri.EscapeDataString(sessionId)}/replan", request, ct).ConfigureAwait(false);
        EnsureAuthority(state.Context);
        return decision;
    }

    private async Task<PlaybackMutationReceipt> ReportProgressV2Async(string sessionId, double position, bool isPaused, CancellationToken ct)
    {
        var state = Session(sessionId);
        PlaybackAcceptedSample sample;
        lock (state.Gate)
        {
            if (state.StopBody != null) throw new InvalidOperationException("Playback is already stopping.");
            sample = new() { Sequence = checked(++state.Sequence), Position = position, IsPaused = isPaused };
            state.LastSample = sample;
        }
        var receipt = await client.PostAsync<PlaybackMutationReceipt>($"/api/v2/playback/{Uri.EscapeDataString(sessionId)}/progress",
            new Dictionary<string, object?>
            {
                ["installation_id"] = state.InstallationId,
                ["sequence"] = sample.Sequence,
                ["position"] = sample.Position,
                ["is_paused"] = sample.IsPaused,
            }, ct).ConfigureAwait(false);
        EnsureAuthority(state.Context);
        if (receipt.Outcome is not ("applied" or "replayed" or "stale_sample"))
            throw new InvalidOperationException($"Unexpected playback progress outcome: {receipt.Outcome}");
        lock (state.Gate)
            state.Sequence = Math.Max(state.Sequence, receipt.Accepted?.Sequence ?? 0);
        return receipt;
    }

    private async Task<PlaybackMutationReceipt> StopV2Async(string sessionId, CancellationToken ct)
    {
        var state = Session(sessionId);
        Dictionary<string, object?> body;
        var newlyStopping = false;
        lock (state.Gate)
        {
            if (state.StopBody == null)
            {
                newlyStopping = true;
                state.StopBody = new()
                {
                    ["installation_id"] = state.InstallationId,
                    ["stop_id"] = Guid.NewGuid().ToString(),
                };
                if (state.LastSample is { } sample)
                {
                    state.StopBody["sequence"] = checked(++state.Sequence);
                    state.StopBody["position"] = sample.Position;
                    state.StopBody["is_paused"] = sample.IsPaused;
                }
            }
            body = state.StopBody;
        }
        if (newlyStopping)
        {
            // Exact stop bodies survive lost responses and teardown retries, but
            // completed viewing history must not grow this singleton forever.
            _retiredSessions.Enqueue(sessionId);
            while (_retiredSessions.Count > RetainedStopAttempts && _retiredSessions.TryDequeue(out var expired))
                _sessions.TryRemove(expired, out _);
        }
        var receipt = await RetryDeliveryAsync(token => client.DeleteWithBodyAsync<PlaybackMutationReceipt>(
            $"/api/v2/playback/{Uri.EscapeDataString(sessionId)}", body, token),
            state.Context, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        EnsureAuthority(state.Context);
        if (receipt.Outcome is not ("stopped" or "replayed"))
            throw new InvalidOperationException($"Unexpected playback stop outcome: {receipt.Outcome}");
        return receipt;
    }

    public async Task<PlaybackControlTicket> CreateControlTicketAsync(string sessionId, CancellationToken ct = default)
    {
        var state = Session(sessionId);
        var ticket = await client.PostAsync<PlaybackControlTicket>(
            $"/api/v2/playback/sessions/{Uri.EscapeDataString(sessionId)}/control/ws-ticket",
            new Dictionary<string, object?> { ["installation_id"] = state.InstallationId }, ct).ConfigureAwait(false);
        EnsureAuthority(state.Context);
        if (ticket.Protocol != "silo.playback-control.v2" || string.IsNullOrEmpty(ticket.Ticket))
            throw new InvalidOperationException("The server returned an invalid playback control ticket.");
        return ticket;
    }

    public async Task ReportRouteEventAsync(PlaybackStartResponse session, string eventName, CancellationToken ct = default)
    {
        var state = Session(session.SessionId);
        // Best effort telemetry is sent once; 202 is queue acknowledgement, and
        // retrying a dropped event must never delay playback or teardown.
        await client.PostNoContentAsync("/api/v2/playback/route-events", new Dictionary<string, object?>
        {
            ["installation_id"] = state.InstallationId,
            ["event_id"] = Guid.NewGuid().ToString(),
            ["protocol_version"] = 3,
            ["playback_attempt_id"] = session.PlaybackAttemptId,
            ["session_id"] = session.SessionId,
            ["plan_id"] = session.PlanId,
            ["plan_attempt_key"] = session.PlanAttemptKey,
            ["event"] = eventName,
            ["diagnostics"] = new Dictionary<string, string>(),
        }, ct).ConfigureAwait(false);
        EnsureAuthority(state.Context);
    }

    private SessionState Session(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
            throw new InvalidOperationException("Playback session installation is unavailable. Start a new attempt.");
        EnsureAuthority(state.Context);
        return state;
    }

    private void EnsureAuthority(ApiRequestContext authority)
    {
        if (!client.IsCurrentContext(authority))
            throw new OperationCanceledException("The account, profile or server changed during playback.");
    }

    private async Task<T> RetryDeliveryAsync<T>(Func<CancellationToken, Task<T>> deliver, ApiRequestContext authority,
        TimeSpan budget, TimeSpan perRequest, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);
        for (var attempt = 0; ; attempt++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            EnsureAuthority(authority);
            using var request = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            request.CancelAfter(perRequest);
            try { return await deliver(request.Token).ConfigureAwait(false); }
            catch (Exception ex) when (attempt < 2 && !deadline.IsCancellationRequested &&
                ex is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
            {
                EnsureAuthority(authority);
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), deadline.Token).ConfigureAwait(false);
            }
        }
    }
}
