using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>Run check may have started a check on the server, but no usable answer arrived. It is never sent again; the state is re-read.</summary>
public sealed class AdminDirectConnectionsOutcomeUnknownException() : Exception("The direct connection check outcome is unknown.");

/// <summary>
/// Configure › Network access › Direct connections wire operations (official <c>internal/apiv2/direct_connections.go</c>).
/// Both requests are bound to a captured admin lease, have a finite deadline and a response-size bound, and are sent once:
/// there is no authentication replay and no automatic retry. Responses are validated before any of their data is used and an
/// over-long list is reported, never silently trimmed. Server text is bounded and shown as plain text only.
/// </summary>
public sealed class AdminDirectConnectionsApi(SiloApiClient client)
{
    public const string StatusPath = "/api/v2/admin/direct-connections/status";
    public const int MaxHosts = 16, MaxFeatures = 64, MaxListeners = 16, MaxKnobs = 32, MaxAllowed = 32;
    public const int MaxResponseBytes = 1024 * 1024;
    private const int MaxIdLength = 256, MaxLabelLength = 200, MaxDetailLength = 1000;
    // The server's settings read and its check each take up to 15 s; a refresh may take about 30 s.
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan RefreshDeadline = TimeSpan.FromSeconds(45);

    /// <summary>Reads the last completed check. Polling uses this; it starts no probe.</summary>
    public async Task<AdminDirectConnectionsStatus> GetStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        StatusWire body;
        try { body = (await client.MonitorLogsSendAsync<StatusWire>(lease, HttpMethod.Get, StatusPath, null, ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw Sanitize(error, keepDetail: false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !lease.IsCurrent) { throw; }
        // Out of time while the owner is still current: a transport failure, not a user cancellation.
        catch (OperationCanceledException) { throw new OperationCanceledException("The direct connection status read timed out.", new TimeoutException()); }
        return Validate(body);
    }

    /// <summary>
    /// Runs one check (<c>?refresh=true</c>). A 4xx answer other than 408 is a refusal and keeps the server's problem detail
    /// (bounded and redacted). A timeout, 408, 5xx, transport failure or unusable answer is an unknown outcome.
    /// </summary>
    public async Task<AdminDirectConnectionsStatus> RefreshAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, RefreshDeadline);
        StatusWire body;
        try { body = (await client.MonitorLogsSendAsync<StatusWire>(lease, HttpMethod.Get, StatusPath + "?refresh=true", null, ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !lease.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminDirectConnectionsOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error, keepDetail: true); }
        catch (Exception) { throw new AdminDirectConnectionsOutcomeUnknownException(); }
        try { return Validate(body); }
        catch (Exception error) when (error is InvalidDataException or AdminNetworkAccessLimitException) { throw new AdminDirectConnectionsOutcomeUnknownException(); }
    }

    private static AdminDirectConnectionsStatus Validate(StatusWire body)
    {
        if (body.Enabled == null || body.Hosts == null) throw new InvalidDataException("Invalid direct connection status.");
        if (body.Hosts.Length > MaxHosts) throw new AdminNetworkAccessLimitException(MaxHosts);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hosts = new List<AdminDirectConnectionsHost>(body.Hosts.Length);
        foreach (var host in body.Hosts)
        {
            if (host?.Host?.Id is not { Length: > 0 and <= MaxIdLength } id || string.IsNullOrWhiteSpace(id) || !seen.Add(id) ||
                host.CheckedAt is not { } checkedAt || host.Features == null || host.Knobs == null)
                throw new InvalidDataException("Invalid direct connection host.");
            var listeners = host.Listeners ?? [];
            if (host.Features.Length > MaxFeatures) throw new AdminNetworkAccessLimitException(MaxFeatures);
            if (listeners.Length > MaxListeners) throw new AdminNetworkAccessLimitException(MaxListeners);
            if (host.Knobs.Length > MaxKnobs) throw new AdminNetworkAccessLimitException(MaxKnobs);
            hosts.Add(new(id, host.Host.Role ?? "", Bound(host.Host.Name, MaxLabelLength) ?? "", checkedAt,
                Array.AsReadOnly(host.Features.Select(Feature).ToArray()),
                Array.AsReadOnly(listeners.Select(Listener).ToArray()),
                Array.AsReadOnly(host.Knobs.Select(Knob).ToArray())));
        }
        return new(body.Enabled.Value, hosts.AsReadOnly());
    }

    private static AdminDirectConnectionsFeature Feature(FeatureWire? wire)
    {
        if (wire?.Feature is not { Length: > 0 } feature || wire.State is not { Length: > 0 }) throw new InvalidDataException("Invalid direct connection feature.");
        return new(Bound(feature, MaxLabelLength)!, Bound(wire.State, MaxLabelLength)!, Bound(wire.Reason, MaxLabelLength), Bound(Text(wire.Requested), MaxLabelLength),
            Bound(Text(wire.Granted), MaxLabelLength), Bound(wire.Detail, MaxDetailLength));
    }

    private static AdminDirectConnectionsListener Listener(ListenerWire? wire)
    {
        if (wire?.Listener is not { Length: > 0 } name || wire.SendBuffer is not { } buffer || wire.State == null) throw new InvalidDataException("Invalid direct connection listener.");
        return new(Bound(name, MaxLabelLength)!, Bound(wire.Requested, MaxLabelLength) ?? "", Bound(wire.Granted, MaxLabelLength) ?? "", Bound(wire.State, MaxLabelLength)!,
            Bound(wire.Reason, MaxLabelLength) ?? "", Bound(wire.Errno, MaxLabelLength),
            new(Bound(buffer.State, MaxLabelLength) ?? "", Bound(buffer.Reason, MaxLabelLength) ?? "", buffer.Requested ?? 0, buffer.Granted ?? 0, Bound(buffer.Errno, MaxLabelLength)));
    }

    private static AdminDirectConnectionsKnob Knob(KnobWire? wire)
    {
        if (wire?.Env is not { Length: > 0 } env || wire.Default == null || wire.Effective == null) throw new InvalidDataException("Invalid direct connection knob.");
        var allowed = wire.Allowed ?? [];
        if (allowed.Length > MaxAllowed) throw new AdminNetworkAccessLimitException(MaxAllowed);
        return new(Bound(env, MaxLabelLength)!, Bound(wire.Default, MaxLabelLength)!, Bound(wire.Effective, MaxLabelLength)!, Bound(wire.Requested, MaxLabelLength), Bound(wire.Reason, MaxLabelLength),
            Array.AsReadOnly(allowed.Where(value => value != null).Select(value => Bound(value, MaxLabelLength)!).ToArray()));
    }

    /// <summary>A byte count the server sends as a string (a number is accepted); anything else is absent.</summary>
    private static string? Text(JsonElement? value) => value?.ValueKind switch
    {
        JsonValueKind.String => value.Value.GetString(),
        JsonValueKind.Number => value.Value.GetRawText(),
        _ => null,
    };

    /// <summary>Bounded display text: an over-long diagnostic string ends with a visible ellipsis.</summary>
    private static string? Bound(string? value, int max)
    {
        if (value == null) return null;
        return value.Length <= max ? value : value[..max] + "…";
    }

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline);
        return linked;
    }

    private static string SafeCode(string? code) => AdminServerSettingsApi.SafeCode(code);

    /// <summary>A refusal keeps the server's one-line problem detail (bounded, redacted), as the official section shows the error's message.</summary>
    private static ApiException Sanitize(ApiException error, bool keepDetail)
    {
        var safe = new ApiException(SafeCode(error.ErrorCode), "Direct connection request failed.", error.StatusCode)
        {
            ProblemType = SafeCode(error.ProblemType) is var code && code != "unknown" ? code : null,
        };
        if (!keepDetail || string.IsNullOrWhiteSpace(error.ProblemDetail)) return safe;
        var detail = LocalLog.RedactDiagnosticText(error.ProblemDetail.ReplaceLineEndings(" ").Trim());
        if (detail.Length > MaxDetailLength) detail = detail[..MaxDetailLength].TrimEnd() + "…";
        return new(safe.ErrorCode, safe.Message, safe.StatusCode) { ProblemType = safe.ProblemType, ProblemDetail = detail };
    }

    private sealed record StatusWire { public bool? Enabled { get; init; } public HostWire?[]? Hosts { get; init; } }
    private sealed record HostRefWire { public string? Id { get; init; } public string? Role { get; init; } public string? Name { get; init; } }
    private sealed record HostWire
    {
        public HostRefWire? Host { get; init; }
        public DateTimeOffset? CheckedAt { get; init; }
        public FeatureWire?[]? Features { get; init; }
        public ListenerWire?[]? Listeners { get; init; }
        public KnobWire?[]? Knobs { get; init; }
    }
    private sealed record FeatureWire
    {
        public string? Feature { get; init; }
        public string? State { get; init; }
        public string? Reason { get; init; }
        public JsonElement? Requested { get; init; }
        public JsonElement? Granted { get; init; }
        public string? Detail { get; init; }
    }
    private sealed record SendBufferWire { public string? State { get; init; } public string? Reason { get; init; } public long? Requested { get; init; } public long? Granted { get; init; } public string? Errno { get; init; } }
    private sealed record ListenerWire
    {
        public string? Listener { get; init; }
        public string? Requested { get; init; }
        public string? Granted { get; init; }
        public string? State { get; init; }
        public string? Reason { get; init; }
        public string? Errno { get; init; }
        public SendBufferWire? SendBuffer { get; init; }
    }
    private sealed record KnobWire { public string? Env { get; init; } public string? Default { get; init; } public string? Effective { get; init; } public string? Requested { get; init; } public string? Reason { get; init; } public string?[]? Allowed { get; init; } }
}
