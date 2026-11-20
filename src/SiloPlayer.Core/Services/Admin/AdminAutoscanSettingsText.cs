namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// English text of Autoscan › Settings, copied from the official web/src/locales/en.json at
/// Silo-Server/silo-server 66b836f82 (web.admin.settings.autoscan.*, web.admin.autoscan.connections.*,
/// web.admin.common.autoscan.*). Entries marked "native" have no official counterpart.
/// </summary>
public static class AdminAutoscanSettingsText
{
    private static readonly IReadOnlyDictionary<string, string> Text = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["title"] = "Autoscan",
        ["loadingSettings"] = "Loading settings",
        ["loadError"] = "Autoscan settings could not be loaded. Reload before editing.",
        ["polling.group"] = "Polling defaults",
        ["polling.description"] = "Used by sources that don't set their own.",
        ["polling.interval"] = "Default check interval",
        ["polling.debounce"] = "Debounce",
        ["polling.debounceDescription"] = "Skips repeat reports of an unchanged file within this window. Changed or deleted files still scan.",
        ["seconds"] = "seconds",
        ["connections.title"] = "Connections",
        ["connections.description"] = "Credentials for sources that reach a server, such as Sonarr or Radarr. Webhook sources need none.",
        ["connections.add"] = "Add connection",
        ["connections.empty"] = "No connections yet.",
        ["loading"] = "Loading…",
        ["name"] = "Name", ["kind"] = "Kind", ["source"] = "Source", ["url"] = "URL",
        ["own"] = "Own", ["reuse"] = "Reused from Requests",
        ["edit"] = "Edit connection", ["delete"] = "Delete connection",
        ["dialog.description"] = "Reuse a server you already configured in Requests (Sonarr/Radarr), or enter your own credentials.",
        ["dialog.type"] = "Connection type",
        ["dialog.own"] = "Enter credentials manually", ["dialog.reuse"] = "Reuse a server from Requests",
        ["dialog.requestServer"] = "Request server", ["dialog.loadingServers"] = "Loading servers…",
        ["dialog.noServers"] = "No Sonarr or Radarr servers yet. Add one under",
        ["dialog.requests"] = "Settings › Requests", ["dialog.noServersEnd"] = "first, or enter credentials manually.",
        ["dialog.selectServer"] = "Select a server…", ["dialog.save"] = "Save changes", ["dialog.saving"] = "Saving…",
        ["key"] = "API key", ["key.keep"] = "(leave blank to keep existing)", ["key.placeholder"] = "Enter API key",
        ["baseUrl"] = "Base URL", ["name.placeholder"] = "My Sonarr",
        ["test"] = "Test connection", ["testing"] = "Testing…", ["connected"] = "Connected", ["failed"] = "Connection failed",
        ["testRequestFailed"] = "Connection test failed",
        ["delete.title"] = "Delete connection?", ["delete.confirm"] = "Delete",
        ["saved"] = "Autoscan settings saved",
        ["created"] = "Autoscan connection created", ["updated"] = "Autoscan connection updated", ["deleted"] = "Autoscan connection deleted",
        ["settingsUnconfirmed"] = "Settings persistence could not be confirmed. Reload settings before submitting again.",
        ["createUnconfirmed"] = "Connection creation could not be confirmed. Refresh connections before submitting again.",
        ["updateUnconfirmed"] = "Connection update could not be confirmed. Refresh connections before submitting again.",
        ["deleteUnconfirmed"] = "Connection deletion could not be confirmed. Refresh connections and check source bindings before submitting again.",
        ["reschedule.failed"] = "Settings saved, but poll-task rescheduling failed. Runtime may retain its previous schedule until restart.",
        ["reschedule.not_configured"] = "Settings saved; no poll-task rescheduler is configured on this server.",
        // native: recovery and local validation, which the web page expresses with toasts and server validation.
        ["reload"] = "Reload", ["reloadReview"] = "Reload and review",
        ["connectionsLoadError"] = "Connections could not be loaded. Reload before editing.",
        ["serversLoadError"] = "Request servers could not be loaded. Reload to retry.",
        ["invalidDefaults"] = "Default check interval must be 1–2147483647 seconds; Debounce must be 0–2147483647 seconds.",
        ["defaultsChanged"] = "Polling defaults changed on the server. Reload and review before saving again.",
        ["targetChanged"] = "Connection changed or was removed on the server. Reload and review before submitting again.",
        ["notSent"] = "Could not reload current server data. No write was sent. Reload before trying again.",
        ["requestsUnavailable"] = "Settings › Requests is not available in this build.",
        ["editorUnavailable"] = "Connection editor could not be opened.",
        ["deleteUnavailable"] = "Delete confirmation could not be opened.",
    };
    public static string T(string key) => Text.TryGetValue(key, out var value) ? value : throw new ArgumentException("Unknown Autoscan settings text.", nameof(key));
    public static string DeleteDescription(string name) => $"“{name}” will be removed. Deletion is blocked while scan sources reference this connection. Reconfigure those sources first.";
    public static string Connected(string? version) => version == null ? T("connected") : $"Connected (v{version})";
    /// <summary>The official "{name} ({kind})" request-server option.</summary>
    public static string RequestServerOption(string name, string kind) => $"{name} ({KindLabel(kind)})";
    public static string KindLabel(string kind) => kind == "sonarr" ? "Sonarr" : kind == "radarr" ? "Radarr" : kind;
    public static IEnumerable<string> Keys => Text.Keys;
}
