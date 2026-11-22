namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Display text for Configure › Network access › Direct connections. The <c>web.admin.settings.network.direct.*</c> entries are
/// the English patterns of the official catalog (<c>web/src/locales/en.json</c> at Silo Server
/// c90e406db71ee468bffdfa5733cd97d1d50b4019, compiled message ASTs rendered back to ICU), keys kept verbatim. The
/// <c>configure.native.direct.*</c> entries are native wording for states the WebUI shows from a server message. Any other key is
/// the shared admin catalog.
/// </summary>
public static class AdminDirectConnectionsText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.admin.settings.network.direct.algorithm"] = "Algorithm: {algorithm}",
        ["web.admin.settings.network.direct.algorithmRequestedActive"] = "requested {requested}; active {active}",
        ["web.admin.settings.network.direct.allowed"] = "Allowed: {values}",
        ["web.admin.settings.network.direct.congestionControl"] = "Congestion control: {state}",
        ["web.admin.settings.network.direct.defaultValue"] = "Default: {value}",
        ["web.admin.settings.network.direct.description"] = "Check this server’s readiness for direct connections.",
        ["web.admin.settings.network.direct.effective"] = "Effective: {value}",
        ["web.admin.settings.network.direct.enable"] = "Enable direct connections",
        ["web.admin.settings.network.direct.feature.cap_net_admin"] = "NET_ADMIN capability",
        ["web.admin.settings.network.direct.feature.nat_hairpin"] = "NAT hairpin",
        ["web.admin.settings.network.direct.feature.nat_mapping"] = "NAT mapping",
        ["web.admin.settings.network.direct.feature.pmtu_fallback"] = "PMTU fallback",
        ["web.admin.settings.network.direct.feature.reachability"] = "Reachability",
        ["web.admin.settings.network.direct.feature.socket_receive_buffer"] = "Socket receive buffer",
        ["web.admin.settings.network.direct.feature.socket_send_buffer"] = "Socket send buffer",
        ["web.admin.settings.network.direct.hostTuning"] = "Host tuning",
        ["web.admin.settings.network.direct.knob.tcpCongestion"] = "TCP congestion control",
        ["web.admin.settings.network.direct.knob.tcpSendBuffer"] = "TCP send buffer",
        ["web.admin.settings.network.direct.lastChecked"] = "Last checked {time}",
        ["web.admin.settings.network.direct.listener.proxy"] = "Proxy",
        ["web.admin.settings.network.direct.listeners"] = "Listeners",
        ["web.admin.settings.network.direct.observedAddress"] = "Observed public address: {address}",
        ["web.admin.settings.network.direct.portMapping.description"] = "Check the router for UPnP, NAT-PMP, or PCP support.",
        ["web.admin.settings.network.direct.portMapping.label"] = "Port mapping",
        ["web.admin.settings.network.direct.readinessNote"] = "Readiness checks do not start direct playback.",
        ["web.admin.settings.network.direct.reason.admin_off"] = "Disabled by administrator",
        ["web.admin.settings.network.direct.reason.disabled"] = "Disabled",
        ["web.admin.settings.network.direct.reason.force_failed"] = "Could not increase buffer",
        ["web.admin.settings.network.direct.reason.gateway_unsupported"] = "Router does not support port mapping",
        ["web.admin.settings.network.direct.reason.granted_below_request"] = "Buffer is smaller than requested",
        ["web.admin.settings.network.direct.reason.helper_unavailable"] = "Network tuning helper unavailable",
        ["web.admin.settings.network.direct.reason.invalid"] = "Invalid value; using off",
        ["web.admin.settings.network.direct.reason.kernel_error"] = "System could not apply or read this setting",
        ["web.admin.settings.network.direct.reason.kernel_unsupported"] = "Not supported by this kernel",
        ["web.admin.settings.network.direct.reason.limit"] = "Host buffer limit reached",
        ["web.admin.settings.network.direct.reason.limits_unknown"] = "Could not read host buffer limits",
        ["web.admin.settings.network.direct.reason.mapping_varies"] = "Public address varies by destination",
        ["web.admin.settings.network.direct.reason.module_missing"] = "BBR is not available",
        ["web.admin.settings.network.direct.reason.module_not_loaded"] = "BBR is not loaded or allowed",
        ["web.admin.settings.network.direct.reason.net_admin_dropped"] = "NET_ADMIN capability dropped",
        ["web.admin.settings.network.direct.reason.no_cap_net_admin"] = "NET_ADMIN capability missing",
        ["web.admin.settings.network.direct.reason.no_gateway"] = "No router found",
        ["web.admin.settings.network.direct.reason.no_net_admin"] = "NET_ADMIN capability missing",
        ["web.admin.settings.network.direct.reason.not_allowed"] = "Permission denied",
        ["web.admin.settings.network.direct.reason.not_available"] = "Not available",
        ["web.admin.settings.network.direct.reason.not_measured"] = "Not measured",
        ["web.admin.settings.network.direct.reason.not_needed"] = "No increase needed",
        ["web.admin.settings.network.direct.reason.overridden"] = "Host policy selected another algorithm",
        ["web.admin.settings.network.direct.reason.probe_failed"] = "Check failed",
        ["web.admin.settings.network.direct.reason.unsupported_platform"] = "Not supported on this platform",
        ["web.admin.settings.network.direct.reasonWithErrno"] = "{reason} ({errno})",
        ["web.admin.settings.network.direct.requested"] = "Requested: {value}",
        ["web.admin.settings.network.direct.requestedGranted"] = "Requested: {requested}; granted: {granted}",
        ["web.admin.settings.network.direct.runCheck"] = "Run check",
        ["web.admin.settings.network.direct.sendBuffer"] = "Send buffer: {state}",
        ["web.admin.settings.network.direct.state.capped"] = "Capped",
        ["web.admin.settings.network.direct.state.degraded"] = "Degraded",
        ["web.admin.settings.network.direct.state.fallback"] = "Fallback",
        ["web.admin.settings.network.direct.state.notNeeded"] = "Not needed",
        ["web.admin.settings.network.direct.state.off"] = "Off",
        ["web.admin.settings.network.direct.state.on"] = "On",
        ["web.admin.settings.network.direct.state.unknown"] = "Unknown",
        ["web.admin.settings.network.direct.stunServers.description"] = "A comma-separated list of up to eight host:port entries. Leave it empty to use the defaults.",
        ["web.admin.settings.network.direct.stunServers.label"] = "STUN servers",
        ["web.admin.settings.network.direct.title"] = "Direct connections",

        // Native wording: the WebUI shows the server's own message for these.
        ["configure.native.direct.loadingStatus"] = "Loading direct connection status",
        ["configure.native.direct.statusFailed"] = "Could not read direct connection status.",
        ["configure.native.direct.refreshFailed"] = "Could not refresh direct connection status. The last result is shown.",
        ["configure.native.direct.checkRefused"] = "The server did not run the check.",
        ["configure.native.direct.checkUnknown"] = "The check request did not finish, so the check may or may not have run. The server’s latest result is being read; run the check again if it is out of date.",
        ["configure.native.direct.checking"] = "Checking",
        ["configure.native.direct.settingsFailed"] = "Could not read the direct connection settings.",
        ["configure.native.direct.reload"] = "Reload",
        ["configure.native.direct.conflict"] = "Settings changed on the server. Reload and review your changes.",
        ["configure.native.direct.outcomeUnknown"] = "The save outcome is unknown. Reload and review before saving again.",
        ["configure.native.direct.saveFailed"] = "Failed to update settings.",
        ["configure.native.direct.stun.tooMany"] = "direct_connections.stun_servers accepts at most 8 entries",
        ["configure.native.direct.stun.hostPort"] = "direct_connections.stun_servers requires host:port entries: {reason}",
        ["configure.native.direct.stun.port"] = "direct_connections.stun_servers requires ports between 1 and 65535",
        ["configure.native.direct.stun.host"] = "direct_connections.stun_servers contains an invalid IP address or DNS host",
    };

    public static bool Has(string key) => English.ContainsKey(key);
    public static IReadOnlyCollection<string> Keys => English.Keys.ToArray();

    /// <summary>The pattern for <paramref name="key"/> from this catalog, else the shared admin catalog.</summary>
    public static string T(string key, object? values = null)
    {
        if (!English.TryGetValue(key, out var pattern)) return AdminText.T(key, values);
        if (values is null) return AdminText.Format(pattern, new Dictionary<string, object?>());
        if (values is IReadOnlyDictionary<string, object?> ready) return AdminText.Format(pattern, ready);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in values.GetType().GetProperties()) result[property.Name] = property.GetValue(values);
        return AdminText.Format(pattern, result);
    }
}
