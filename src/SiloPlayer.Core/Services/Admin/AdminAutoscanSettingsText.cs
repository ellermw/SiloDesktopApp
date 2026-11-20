namespace SiloPlayer.Core.Services.Admin;

public static class AdminAutoscanSettingsText
{
    private static readonly IReadOnlyDictionary<string, string> Text = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["title"] = "Autoscan",
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
        ["delete.title"] = "Delete connection?",
        ["reschedule.failed"] = "Settings saved, but poll-task rescheduling failed. Runtime may retain its previous schedule until restart.",
        ["reschedule.not_configured"] = "Settings saved; no poll-task rescheduler is configured on this server."
    };
    public static string T(string key) => Text.TryGetValue(key, out var value) ? value : throw new ArgumentException("Unknown Autoscan settings text.", nameof(key));
    public static string DeleteDescription(string name) => $"“{name}” will be removed. Deletion is blocked while scan sources reference this connection. Reconfigure those sources first.";
    public static string Connected(string? version) => version == null ? T("connected") : $"Connected (v{version})";
}

