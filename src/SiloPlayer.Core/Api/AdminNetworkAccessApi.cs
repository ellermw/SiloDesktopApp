using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>A connect/disconnect may have reached the provider, but no usable acknowledgement arrived. It is never resent.</summary>
public sealed class AdminNetworkAccessOutcomeUnknownException() : Exception("The network access command outcome is unknown.");

/// <summary>The server reported more entries than the page can show truthfully; nothing is silently dropped.</summary>
public sealed class AdminNetworkAccessLimitException(int limit) : Exception("The network access response exceeds the supported size.")
{
    public int Limit { get; } = limit;
}

/// <summary>
/// Configure › Network access wire operations (official <c>internal/apiv2/network_access.go</c>),
/// kept separate from the Monitor API. Every call is bound to a captured admin lease, has a
/// finite deadline and a response-size bound, and is sent once: there is no authentication
/// replay and no automatic retry. Responses are validated before any of their data is used.
/// </summary>
public sealed partial class AdminNetworkAccessApi(SiloApiClient client)
{
    /// <summary>Providers listed in one capability document (ordinary deployments install one or two).</summary>
    public const int MaxProviders = 32;
    /// <summary>Hosts per provider: the API server plus proxy nodes; matches the server's command <c>maxItems</c>.</summary>
    public const int MaxHosts = 256;
    public const int MaxAddresses = 64;
    public const int MaxResponseBytes = 1024 * 1024;
    private const int MaxIdLength = 256;
    private const int MaxTextLength = 2000;
    private const int MaxUrlLength = 4096;
    private static readonly TimeSpan CapabilitiesDeadline = TimeSpan.FromSeconds(15);
    // The server asks each plugin instance with a ten-second timeout per host.
    private static readonly TimeSpan StatusDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CommandDeadline = TimeSpan.FromSeconds(30);

    [GeneratedRegex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderSlug();

    public static bool IsProviderSlug(string? provider) => provider is { Length: > 0 and <= 64 } && ProviderSlug().IsMatch(provider);

    public async Task<AdminNetworkAccessCapabilities> GetCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, CapabilitiesDeadline);
        var body = await ReadAsync<CapabilitiesWire>(lease, HttpMethod.Get, "/api/v2/network-access/capabilities", null, timeout.Token).ConfigureAwait(false);
        if (body.Providers == null || body.State is not { Length: > 0 and <= 64 }) throw new InvalidDataException("Invalid network access capabilities.");
        if (body.Providers.Length > MaxProviders) throw new AdminNetworkAccessLimitException(MaxProviders);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var providers = new List<AdminNetworkAccessProvider>(body.Providers.Length);
        foreach (var provider in body.Providers)
        {
            if (provider == null || !IsProviderSlug(provider.Provider) || !seen.Add(provider.Provider!) || string.IsNullOrWhiteSpace(provider.DisplayName))
                throw new InvalidDataException("Invalid network access provider.");
            providers.Add(new(provider.Provider!, Bound(provider.DisplayName, 200)!, Bound(IdText(provider.InstallationId), 64) ?? ""));
        }
        return new(body.Allowed == true, body.State, Bound(body.Revision, 200) ?? "", providers.AsReadOnly());
    }

    public async Task<AdminNetworkAccessStatus> GetStatusAsync(MonitorAuthorityLease lease, string provider, CancellationToken ct = default)
    {
        var path = ProviderPath(provider) + "/status";
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, StatusDeadline);
        var body = await ReadAsync<StatusWire>(lease, HttpMethod.Get, path, null, timeout.Token).ConfigureAwait(false);
        return Validate(body, provider);
    }

    /// <summary>Asks the provider to connect exactly <paramref name="hostId"/>. 202 answers every host's current state.</summary>
    public Task<AdminNetworkAccessStatus> ConnectAsync(MonitorAuthorityLease lease, string provider, string hostId, CancellationToken ct = default) =>
        CommandAsync(lease, provider, "/connect", hostId, ct);

    /// <summary>Asks the provider to disconnect exactly <paramref name="hostId"/>. 202 answers every host's current state.</summary>
    public Task<AdminNetworkAccessStatus> DisconnectAsync(MonitorAuthorityLease lease, string provider, string hostId, CancellationToken ct = default) =>
        CommandAsync(lease, provider, "/disconnect", hostId, ct);

    private async Task<AdminNetworkAccessStatus> CommandAsync(MonitorAuthorityLease lease, string provider, string command, string hostId, CancellationToken ct)
    {
        var path = ProviderPath(provider) + command;
        // An omitted or empty list would act on every host; only one captured, named host is ever sent.
        if (string.IsNullOrWhiteSpace(hostId) || hostId.Length > MaxIdLength) throw new ArgumentException("A single host is required.", nameof(hostId));
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, CommandDeadline);
        StatusWire body;
        try
        {
            body = (await client.MonitorLogsSendAsync<StatusWire>(lease, HttpMethod.Post, path, new CommandWire([hostId]), ct: timeout.Token,
                maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminNetworkAccessOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminNetworkAccessOutcomeUnknownException(); }
        AdminNetworkAccessStatus status;
        try { status = Validate(body, provider); }
        catch (Exception error) when (error is InvalidDataException or AdminNetworkAccessLimitException) { throw new AdminNetworkAccessOutcomeUnknownException(); }
        if (!status.Hosts.Any(host => string.Equals(host.Host.Id, hostId, StringComparison.Ordinal))) throw new AdminNetworkAccessOutcomeUnknownException();
        return status;
    }

    private async Task<T> ReadAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try { return (await client.MonitorLogsSendAsync<T>(lease, method, path, body, ct: ct, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw Sanitize(error); }
    }

    private static string ProviderPath(string provider)
    {
        if (!IsProviderSlug(provider)) throw new ArgumentException("Invalid network access provider.", nameof(provider));
        return "/api/v2/admin/network-access/" + Uri.EscapeDataString(provider);
    }

    private static AdminNetworkAccessStatus Validate(StatusWire body, string provider)
    {
        if (!string.Equals(body.Provider, provider, StringComparison.Ordinal) || body.Hosts == null) throw new InvalidDataException("Invalid network access status.");
        if (body.Hosts.Length > MaxHosts) throw new AdminNetworkAccessLimitException(MaxHosts);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hosts = new List<AdminNetworkAccessHostStatus>(body.Hosts.Length);
        foreach (var entry in body.Hosts)
        {
            if (entry?.Host is not { } host || host.Id is not { Length: > 0 and <= MaxIdLength } id || string.IsNullOrWhiteSpace(id) || !seen.Add(id) ||
                entry.State is not { Length: > 0 and <= 64 } state)
                throw new InvalidDataException("Invalid network access host.");
            var addresses = entry.Addresses ?? [];
            if (addresses.Length > MaxAddresses) throw new AdminNetworkAccessLimitException(MaxAddresses);
            hosts.Add(new(new(id, host.Role == "api" ? "api" : "proxy", Bound(host.Name, 200) ?? ""), state, Bound(entry.RawState, 64),
                Bound(entry.Hostname, 253), Bound(entry.Origin, MaxUrlLength), Array.AsReadOnly(addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => Bound(a, 64)!).ToArray()),
                entry.AuthUrl is { Length: > 0 and <= MaxUrlLength } url ? url : null,
                Bound(entry.Error, MaxTextLength), Bound(entry.ProviderVersion, 128), entry.UpdatedAt));
        }
        return new(provider, hosts.AsReadOnly());
    }

    /// <summary>Bounded display text: an over-long diagnostic string ends with a visible ellipsis.</summary>
    private static string? Bound(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= max ? value : value[..max] + "…";
    }

    private static string? IdText(JsonElement? value) => value?.ValueKind switch
    {
        JsonValueKind.String => value.Value.GetString(),
        JsonValueKind.Number => value.Value.GetRawText(),
        _ => null,
    };

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline);
        return linked;
    }

    private static string SafeCode(string? code) => code is "permission_denied" or "profile_unverified" or "profile_verification_required"
        or "demo_restricted" or "dependency_unavailable" or "validation_failed" or "not_found" or "capability_disabled"
        or "capability_not_configured" or "capability_unsupported" or "rate_limited" or "authentication_required"
        ? code : "unknown";
    // Problem detail is plugin/server text; the page shows its own localized message instead.
    private static ApiException Sanitize(ApiException error) => new(SafeCode(error.ErrorCode), "Network access request failed.", error.StatusCode)
    {
        ProblemType = SafeCode(error.ProblemType) is var code && code != "unknown" ? code : null
    };

    private sealed record CommandWire(string[] Hosts);
    private sealed record CapabilitiesWire { public bool? Allowed { get; init; } public string? State { get; init; } public string? Revision { get; init; } public ProviderWire?[]? Providers { get; init; } }
    private sealed record ProviderWire { public string? Provider { get; init; } public string? DisplayName { get; init; } public JsonElement? InstallationId { get; init; } }
    private sealed record StatusWire { public string? Provider { get; init; } public HostStatusWire?[]? Hosts { get; init; } }
    private sealed record HostWire { public string? Id { get; init; } public string? Role { get; init; } public string? Name { get; init; } }
    private sealed record HostStatusWire
    {
        public HostWire? Host { get; init; }
        public string? State { get; init; }
        public string? RawState { get; init; }
        public string? Hostname { get; init; }
        public string? Origin { get; init; }
        public string[]? Addresses { get; init; }
        public string? AuthUrl { get; init; }
        public string? Error { get; init; }
        public string? ProviderVersion { get; init; }
        public DateTimeOffset? UpdatedAt { get; init; }
        // Never printed: auth_url is enrollment data.
        public override string ToString() => nameof(HostStatusWire);
    }
}
