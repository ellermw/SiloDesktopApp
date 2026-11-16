namespace SiloPlayer.Core.Models.Admin;

/// <summary>One installed overlay-network provider from the capability document (plugin manifest; no health implied).</summary>
public sealed record AdminNetworkAccessProvider(string Provider, string DisplayName, string InstallationId);

/// <summary>GET /api/v2/network-access/capabilities. Providers keep the server's order.</summary>
public sealed record AdminNetworkAccessCapabilities(bool Allowed, string State, string Revision, IReadOnlyList<AdminNetworkAccessProvider> Providers);

/// <summary>One process running the provider: <c>api</c> for the API server, <c>node:&lt;id&gt;</c> for a proxy node.</summary>
public sealed record AdminNetworkAccessHost(string Id, string Role, string Name);

/// <summary>
/// The provider's state on one host. <see cref="AuthUrl"/> is untrusted plugin-provided
/// enrollment data: it is held only in memory for the page owner and is never part of
/// <see cref="ToString"/>, logs, receipts or persisted state.
/// </summary>
public sealed record AdminNetworkAccessHostStatus(
    AdminNetworkAccessHost Host,
    string State,
    string? RawState,
    string? Hostname,
    string? Origin,
    IReadOnlyList<string> Addresses,
    string? AuthUrl,
    string? Error,
    string? ProviderVersion,
    DateTimeOffset? UpdatedAt)
{
    public const string Disconnected = "disconnected";
    public const string AwaitingAuthorization = "awaiting_authorization";
    public const string Connecting = "connecting";
    public const string Connected = "connected";
    public const string ErrorState = "error";
    public const string Unavailable = "unavailable";

    /// <summary>Connected, connecting or waiting for enrollment: the row offers Disconnect.</summary>
    public bool ShowsDisconnect => State is Connected or Connecting or AwaitingAuthorization;

    public override string ToString() => $"{nameof(AdminNetworkAccessHostStatus)} {{ Host = {Host.Id}, State = {State} }}";

    /// <summary>Value equality that also compares the address list's contents (for in-place row updates).</summary>
    public bool SameAs(AdminNetworkAccessHostStatus? other) => other != null && this with { Addresses = Array.Empty<string>() } == other with { Addresses = Array.Empty<string>() } &&
        Addresses.SequenceEqual(other.Addresses, StringComparer.Ordinal);
}

/// <summary>GET /api/v2/admin/network-access/{provider}/status and the connect/disconnect acknowledgement.</summary>
public sealed record AdminNetworkAccessStatus(string Provider, IReadOnlyList<AdminNetworkAccessHostStatus> Hosts)
{
    public override string ToString() => $"{nameof(AdminNetworkAccessStatus)} {{ Provider = {Provider}, Hosts = {Hosts.Count} }}";
}
