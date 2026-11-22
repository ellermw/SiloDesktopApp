namespace SiloPlayer.Core.Models.Admin;

/// <summary>One readiness row (official <c>DirectConnectionsFeature</c>). Every text is bounded server text, shown as plain text.</summary>
public sealed record AdminDirectConnectionsFeature(string Feature, string State, string? Reason, string? Requested, string? Granted, string? Detail);

/// <summary>A listener's send buffer (official <c>DirectConnectionsSendBuffer</c>); byte counts are whole numbers.</summary>
public sealed record AdminDirectConnectionsSendBuffer(string State, string Reason, long Requested, long Granted, string? Errno);

/// <summary>A bound TCP listener's congestion result (official <c>DirectConnectionsListener</c>).</summary>
public sealed record AdminDirectConnectionsListener(string Listener, string Requested, string Granted, string State, string Reason, string? Errno,
    AdminDirectConnectionsSendBuffer SendBuffer);

/// <summary>A host tuning variable (official <c>DirectConnectionsKnob</c>).</summary>
public sealed record AdminDirectConnectionsKnob(string Env, string Default, string Effective, string? Requested, string? Reason, IReadOnlyList<string> Allowed);

/// <summary>One host's last completed check. The server sends exactly one, the API host.</summary>
public sealed record AdminDirectConnectionsHost(string Id, string Role, string Name, DateTimeOffset CheckedAt,
    IReadOnlyList<AdminDirectConnectionsFeature> Features, IReadOnlyList<AdminDirectConnectionsListener> Listeners, IReadOnlyList<AdminDirectConnectionsKnob> Knobs);

/// <summary>The status document (official <c>DirectConnectionsStatus</c>).</summary>
public sealed record AdminDirectConnectionsStatus(bool Enabled, IReadOnlyList<AdminDirectConnectionsHost> Hosts)
{
    public override string ToString() => nameof(AdminDirectConnectionsStatus);
}
