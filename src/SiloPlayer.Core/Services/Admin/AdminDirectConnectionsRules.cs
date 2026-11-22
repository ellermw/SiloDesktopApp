using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminStunProblem { None, TooMany, HostPort, Port, Host }

/// <summary>The result of checking a STUN list: <see cref="Problem"/> None, or the server's message for the first fault.</summary>
public sealed record AdminStunCheck(AdminStunProblem Problem, string Message)
{
    public static AdminStunCheck Valid { get; } = new(AdminStunProblem.None, "");
    public bool IsValid => Problem == AdminStunProblem.None;
}

/// <summary>
/// The pure rules of Configure › Network access › Direct connections (official <c>DirectConnectionsSection.tsx</c>,
/// <c>directConnections.ts</c> and the server's <c>normalizeSTUNServers</c> in <c>internal/config/admin_settings.go</c>).
/// </summary>
public static class AdminDirectConnectionsRules
{
    public const string Enabled = "direct_connections.enabled";
    public const string PortMapping = "direct_connections.port_mapping";
    public const string StunServers = "direct_connections.stun_servers";
    public static IReadOnlyList<string> AdvancedKeys { get; } = Array.AsReadOnly(new[] { PortMapping, StunServers });
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { Enabled, PortMapping, StunServers });
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(Array.Empty<string>());

    /// <summary>Status poll period (official <c>STATUS_POLL_INTERVAL</c>, also its stale time).</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    /// <summary>The longest STUN list the server accepts, counted before it drops duplicates.</summary>
    public const int MaxStunServers = 8;

    /// <summary>
    /// The section exists only while the saved value is <c>"true"</c>, or while any of its three keys has an unsaved edit so the
    /// draft can still be saved or discarded. Official: <c>getPersistedValue(ENABLED) !== "true" &amp;&amp; dirtyCount === 0</c> renders nothing.
    /// </summary>
    public static bool SectionVisible(string? savedEnabled, int dirtyCount) => savedEnabled == "true" || dirtyCount > 0;

    /// <summary>The advanced disclosure is forced open while either advanced key is dirty (a hidden field must never block Save).</summary>
    public static bool AdvancedForcedOpen(IEnumerable<string> dirtyKeys) => dirtyKeys.Any(AdvancedKeys.Contains);

    /// <summary>
    /// Request order decides, not the server's clock: a poll that started before a refresh result was published is older than the
    /// cache and is dropped (official <c>structuralSharing</c> with <c>publicationCount</c>).
    /// </summary>
    public static bool PollIsCurrent(long publicationsAtStart, long publicationsNow) => publicationsAtStart == publicationsNow;

    /// <summary>A poll is due when the last read is at least one interval old.</summary>
    public static bool PollDue(DateTimeOffset lastRead, DateTimeOffset now) => now - lastRead >= PollInterval;

    // ── STUN list (server rules, in the server's order) ───────────────────────────

    /// <summary>
    /// Checks a STUN list as the server does before it stores it. An empty list is valid (it means "use the defaults"). At most
    /// 8 comma-separated entries (counted before duplicates are dropped); each is <c>host:port</c> (an IPv6 literal in brackets),
    /// the port 1–65535, the host an IP literal or a lower-case DNS name. The server remains the authority; this only avoids
    /// sending what it would refuse.
    /// </summary>
    public static AdminStunCheck CheckStunServers(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return AdminStunCheck.Valid;
        var entries = text.Split(',');
        if (entries.Length > MaxStunServers) return new(AdminStunProblem.TooMany, AdminDirectConnectionsText.T("configure.native.direct.stun.tooMany"));
        foreach (var raw in entries)
        {
            var entry = raw.Trim().ToLowerInvariant();
            if (!TrySplitHostPort(entry, out var host, out var port, out var why))
                return new(AdminStunProblem.HostPort, AdminDirectConnectionsText.T("configure.native.direct.stun.hostPort", new { reason = entry.Length > 0 ? $"address {entry}: {why}" : why }));
            if (!ushort.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0 || port.Any(c => c is < '0' or > '9'))
                return new(AdminStunProblem.Port, AdminDirectConnectionsText.T("configure.native.direct.stun.port"));
            if (!IsIpLiteral(host) && !IsDnsName(host))
                return new(AdminStunProblem.Host, AdminDirectConnectionsText.T("configure.native.direct.stun.host"));
        }
        return AdminStunCheck.Valid;
    }

    /// <summary>Go's <c>net.SplitHostPort</c>.</summary>
    private static bool TrySplitHostPort(string hostPort, out string host, out string port, out string why)
    {
        host = port = why = "";
        var last = hostPort.LastIndexOf(':');
        if (last < 0) { why = "missing port in address"; return false; }
        int j = 0, k = 0;
        if (hostPort[0] == '[')
        {
            var end = hostPort.IndexOf(']');
            if (end < 0) { why = "missing ']' in address"; return false; }
            if (end + 1 == hostPort.Length) { why = "missing port in address"; return false; }
            if (end + 1 != last)
            {
                why = hostPort[end + 1] == ':' ? "too many colons in address" : "missing port in address";
                return false;
            }
            host = hostPort[1..end];
            j = 1; k = end + 1;
        }
        else
        {
            host = hostPort[..last];
            if (host.Contains(':')) { why = "too many colons in address"; return false; }
        }
        if (hostPort[j..].Contains('[')) { why = "unexpected '[' in address"; return false; }
        if (hostPort[k..].Contains(']')) { why = "unexpected ']' in address"; return false; }
        port = hostPort[(last + 1)..];
        return true;
    }

    /// <summary>Go's <c>net.ParseIP</c>: a dotted-quad IPv4 without leading zeros, or an IPv6 literal without a zone.</summary>
    private static bool IsIpLiteral(string host)
    {
        if (host.Contains(':'))
            return host.All(c => Uri.IsHexDigit(c) || c is ':' or '.') && IPAddress.TryParse(host, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6;
        var parts = host.Split('.');
        return parts.Length == 4 && parts.All(part => part.Length is >= 1 and <= 3 && part.All(char.IsAsciiDigit) &&
            (part.Length == 1 || part[0] != '0') && int.Parse(part, CultureInfo.InvariantCulture) <= 255);
    }

    /// <summary>The server's <c>validSTUNHostName</c>: a lower-case DNS name whose last label is not all digits.</summary>
    private static bool IsDnsName(string host)
    {
        if (host.Length is 0 or > 253) return false;
        var labels = (host.EndsWith('.') ? host[..^1] : host).Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-') return false;
            if (label.Any(c => !(c is >= 'a' and <= 'z') && !(c is >= '0' and <= '9') && c != '-')) return false;
        }
        return labels[^1].Trim('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length != 0;
    }
}
