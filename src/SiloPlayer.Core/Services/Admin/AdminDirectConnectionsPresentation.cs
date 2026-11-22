using System.Globalization;
using SiloPlayer.Core.Models.Admin;
using T = SiloPlayer.Core.Services.Admin.AdminDirectConnectionsText;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One row of a host's lists: the label (and, for a tuning variable, the variable name under it), the value on the label line and the detail lines under it.</summary>
public sealed record AdminDirectConnectionsRowView(string Key, string Label, string? Variable, string Value, IReadOnlyList<string> Details);

/// <summary>A host block as the section shows it. Every string is plain text.</summary>
public sealed record AdminDirectConnectionsHostView(string Id, string Heading, string LastChecked,
    IReadOnlyList<AdminDirectConnectionsRowView> Features, string? ListenersHeading, IReadOnlyList<AdminDirectConnectionsRowView> Listeners,
    string TuningHeading, IReadOnlyList<AdminDirectConnectionsRowView> Knobs);

/// <summary>
/// State, reason, size and tuning wording of the Direct connections section (official <c>DirectConnectionsSection.tsx</c>):
/// a value this build does not know is shown as the server sent it, with underscores read as spaces.
/// </summary>
public static class AdminDirectConnectionsPresentation
{
    private const string Prefix = "web.admin.settings.network.direct.";
    private const string EnvTcpCongestion = "SILO_NET_TCP_CONGESTION";
    private const string EnvTcpSendBuffer = "SILO_NET_TCP_SEND_BUFFER";
    private const string NotNeededReason = "not_needed";
    private const string ListenerNetAdminReason = "no_net_admin";
    private static readonly string[] NetAdminReasons = ["no_cap_net_admin", "net_admin_dropped"];

    private static readonly string[] StateNames = ["on", "off", "capped", "fallback", "degraded", "unknown"];
    private static readonly string[] ReasonNames =
    [
        "disabled", "admin_off", "kernel_unsupported", "limit", "force_failed", "no_cap_net_admin", "net_admin_dropped", "unsupported_platform",
        "probe_failed", "not_available", "no_gateway", "gateway_unsupported", "mapping_varies", "not_measured", "module_missing", "module_not_loaded",
        "overridden", "kernel_error", "not_allowed", "limits_unknown", "no_net_admin", "helper_unavailable", "granted_below_request", "not_needed", "invalid",
    ];
    // A catalog key, or a name that stays as written (a protocol or product name).
    private static readonly Dictionary<string, string> FeatureLabels = new(StringComparer.Ordinal)
    {
        ["direct"] = Prefix + "title", ["nat_mapping"] = Prefix + "feature.nat_mapping", ["nat_hairpin"] = Prefix + "feature.nat_hairpin",
        ["port_mapping"] = Prefix + "portMapping.label", ["reachability"] = Prefix + "feature.reachability",
        ["socket_receive_buffer"] = Prefix + "feature.socket_receive_buffer", ["socket_send_buffer"] = Prefix + "feature.socket_send_buffer",
        ["pmtu_fallback"] = Prefix + "feature.pmtu_fallback", ["cap_net_admin"] = Prefix + "feature.cap_net_admin",
    };
    private static readonly Dictionary<string, string> FeatureNames = new(StringComparer.Ordinal) { ["ipv4"] = "IPv4", ["ipv6"] = "IPv6", ["gso"] = "GSO", ["gro"] = "GRO" };
    private static readonly Dictionary<string, string> PortMappingLabels = new(StringComparer.Ordinal) { ["upnp"] = "UPnP", ["pmp"] = "NAT-PMP", ["pcp"] = "PCP" };
    private static readonly Dictionary<string, string> ListenerNames = new(StringComparer.Ordinal) { ["api"] = "API", ["jellyfin"] = "Jellyfin", ["abs"] = "Audiobookshelf" };
    private static readonly Dictionary<string, string> KnobLabels = new(StringComparer.Ordinal)
    {
        [EnvTcpCongestion] = Prefix + "knob.tcpCongestion", [EnvTcpSendBuffer] = Prefix + "knob.tcpSendBuffer",
    };

    /// <summary>The host heading. The WebUI always says "API server" for the one host the server sends.</summary>
    public static string HostHeading() => AdminText.T("web.admin.settings.network.host.apiServer");

    public static string FeatureLabel(string feature) =>
        FeatureNames.TryGetValue(feature, out var name) ? name : FeatureLabels.TryGetValue(feature, out var key) ? T.T(key) : feature;

    public static string ListenerLabel(string listener) =>
        ListenerNames.TryGetValue(listener, out var name) ? name : listener == "proxy" ? T.T(Prefix + "listener.proxy") : listener;

    /// <summary>The label for a state; a state this build does not know shows as sent with underscores read as spaces.</summary>
    public static string StateLabel(string state) => Array.IndexOf(StateNames, state) >= 0 ? T.T(Prefix + "state." + state) : Readable(state);

    public static string ReasonLabel(string reason) => Array.IndexOf(ReasonNames, reason) >= 0 ? T.T(Prefix + "reason." + reason) : Readable(reason);

    private static string Readable(string value) => value.Replace('_', ' ');

    /// <summary>"Off" for off, else the algorithm in capitals ("BBR"); an empty name reads Unknown.</summary>
    public static string CongestionLabel(string value) => value == "off" ? T.T(Prefix + "state.off") : value.ToUpperInvariant() is { Length: > 0 } upper ? upper : T.T(Prefix + "state.unknown");

    /// <summary>The WebUI's formatFileSize with the "Unknown" fallback for a value that is absent, not a number or not positive.</summary>
    public static string Size(string? bytes, string fallback)
    {
        if (bytes != null && double.TryParse(bytes.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
            return DashboardFormat.FileSize(value, fallback);
        return fallback;
    }

    /// <summary>A reason text, "{reason} ({errno})" when the server sent an errno.</summary>
    private static string ReasonWithErrno(string reason, string? errno) =>
        string.IsNullOrEmpty(errno) ? ReasonLabel(reason) : T.T(Prefix + "reasonWithErrno", new { reason = ReasonLabel(reason), errno });

    /// <summary>The state and the reason a feature row shows. NET_ADMIN never repeats its own name as a reason.</summary>
    public static (string State, string? Reason) FeatureDisplay(AdminDirectConnectionsFeature feature, AdminDirectConnectionsHost host)
    {
        var reason = string.IsNullOrEmpty(feature.Reason) ? null : feature.Reason;
        if (feature.Feature == "cap_net_admin" && feature.State == "off" && reason != null && Array.IndexOf(NetAdminReasons, reason) >= 0)
        {
            var needed = host.Features.Any(other => other.Feature != "cap_net_admin" && other.Reason != null && Array.IndexOf(NetAdminReasons, other.Reason) >= 0)
                || host.Listeners.Any(listener => listener.SendBuffer.Reason == ListenerNetAdminReason);
            return (needed ? StateLabel(feature.State) : T.T(Prefix + "state.notNeeded"), null);
        }
        return (StateLabel(feature.State), reason);
    }

    /// <summary>A tuning value as people read it: an algorithm in capitals, a byte size with its unit, off as Off. Anything else shows as sent.</summary>
    public static string KnobValue(AdminDirectConnectionsKnob knob, string value)
    {
        if (knob.Env == EnvTcpCongestion) return value == "off" || knob.Allowed.Contains(value) ? CongestionLabel(value) : value;
        if (knob.Env == EnvTcpSendBuffer)
        {
            if (value == "off") return T.T(Prefix + "state.off");
            return value.Length > 0 && value.All(char.IsAsciiDigit) ? Size(value, value) : value;
        }
        return value;
    }

    /// <summary>A plain list in the interface language's separator ("A, B").</summary>
    public static string JoinList(IEnumerable<string> items) => string.Join(", ", items);

    public static AdminDirectConnectionsHostView Present(AdminDirectConnectionsHost host, Func<DateTimeOffset, string> formatTime)
    {
        var unknown = T.T(Prefix + "state.unknown");
        var features = host.Features.Select(feature =>
        {
            var (state, reason) = FeatureDisplay(feature, host);
            var details = new List<string>();
            if (reason != null) details.Add(ReasonLabel(reason));
            if (feature.Requested != null || feature.Granted != null)
                details.Add(T.T(Prefix + "requestedGranted", new { requested = Size(feature.Requested, unknown), granted = Size(feature.Granted, unknown) }));
            if (!string.IsNullOrEmpty(feature.Detail))
                details.Add(feature.Feature is "reachability" or "nat_mapping" ? T.T(Prefix + "observedAddress", new { address = feature.Detail })
                    : feature.Feature == "port_mapping" ? JoinList(feature.Detail.Split(',').Select(protocol => PortMappingLabels.TryGetValue(protocol, out var label) ? label : protocol.ToUpperInvariant()))
                    : feature.Detail);
            return new AdminDirectConnectionsRowView(feature.Feature, FeatureLabel(feature.Feature), null, state, details);
        }).ToArray();

        var listeners = host.Listeners.Select(listener =>
        {
            var details = new List<string>();
            if (listener.Requested != "off")
                details.Add(T.T(Prefix + "algorithm", new
                {
                    algorithm = listener.Requested == listener.Granted ? CongestionLabel(listener.Granted)
                        : T.T(Prefix + "algorithmRequestedActive", new { requested = CongestionLabel(listener.Requested), active = CongestionLabel(listener.Granted) }),
                }));
            if (!string.IsNullOrEmpty(listener.Reason)) details.Add(ReasonWithErrno(listener.Reason, listener.Errno));
            var buffer = listener.SendBuffer;
            details.Add(T.T(Prefix + "sendBuffer", new { state = buffer.Reason == NotNeededReason ? T.T(Prefix + "state.notNeeded") : StateLabel(buffer.State) }));
            if (buffer.Requested > 0)
                details.Add(T.T(Prefix + "requestedGranted", new { requested = DashboardFormat.FileSize(buffer.Requested), granted = DashboardFormat.FileSize(buffer.Granted, unknown) }));
            if (!string.IsNullOrEmpty(buffer.Reason) && buffer.Reason != NotNeededReason) details.Add(ReasonWithErrno(buffer.Reason, buffer.Errno));
            var state = listener.Requested == "off" ? T.T(Prefix + "state.off") : StateLabel(listener.State);
            return new AdminDirectConnectionsRowView(listener.Listener, ListenerLabel(listener.Listener), null, T.T(Prefix + "congestionControl", new { state }), details);
        }).ToArray();

        var knobs = host.Knobs.Select(knob =>
        {
            var details = new List<string> { T.T(Prefix + "defaultValue", new { value = KnobValue(knob, knob.Default) }) };
            if (knob.Requested != null) details.Add(T.T(Prefix + "requested", new { value = KnobValue(knob, knob.Requested) }));
            if (knob.Allowed.Count > 0) details.Add(T.T(Prefix + "allowed", new { values = JoinList(knob.Allowed.Select(value => KnobValue(knob, value))) }));
            if (!string.IsNullOrEmpty(knob.Reason)) details.Add(ReasonLabel(knob.Reason));
            var label = KnobLabels.TryGetValue(knob.Env, out var key) ? T.T(key) : "";
            return new AdminDirectConnectionsRowView(knob.Env, label, knob.Env, T.T(Prefix + "effective", new { value = KnobValue(knob, knob.Effective) }), details);
        }).ToArray();

        return new(host.Id, HostHeading(), T.T(Prefix + "lastChecked", new { time = formatTime(host.CheckedAt) }), features,
            listeners.Length > 0 ? T.T(Prefix + "listeners") : null, listeners, T.T(Prefix + "hostTuning"), knobs);
    }
}
