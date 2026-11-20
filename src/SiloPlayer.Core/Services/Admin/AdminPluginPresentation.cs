using SiloPlayer.Core.Models.Admin;
using System.Text.Json;

namespace SiloPlayer.Core.Services.Admin;

public static class AdminPluginPresentation
{
    public static string Name(AdminPlugin p)
    {
        if (!string.IsNullOrWhiteSpace(p.Presentation?.DisplayName)) return p.Presentation.DisplayName.Trim();
        var raw = System.Text.RegularExpressions.Regex.Replace(p.PluginId, "^silo[._-]?", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var name = string.Join(' ', raw.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
        return name.Length > 0 ? name : p.PluginId;
    }
    public static string Tier(AdminPlugin p) => p.Id.Length > 0 && p.RepositoryId == null ? "unverified" : p.SourceKind;
    public static string Source(string kind) => kind switch { "silo" => "Silo maintained", "approved_community" => "Approved community", "external" => "External source", _ => "Unverified" };
    public static bool HasUpdate(AdminPlugin p) => !p.UpdatesPaused && p.RepositoryId != null && !string.IsNullOrWhiteSpace(p.AvailableVersion) && p.AvailableVersion != p.Version;
    public static IReadOnlyList<AdminPluginSchema> Missing(AdminPlugin p) => p.GlobalConfigSchema.Where(s => s.Required && !p.GlobalConfigs.Any(c => c.Key == s.Key &&
        (c.ConfiguredSecrets.Count > 0 || c.Value.Values.Any(v => v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && (v.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(v.GetString())))))).ToArray();
    public static int Rank(AdminPlugin p) => !p.Enabled ? 4 : p.Runtime?.State == "failed" ? 0 : p.Runtime is { Resident: true, State: "backoff" } ? 1 : Missing(p).Count > 0 || p.UpdateFailure != null || p.Signature?.State == "invalid" ? 2 : 3;
    public static string Status(AdminPlugin p) => !p.Enabled ? "Off" : p.Runtime?.State == "failed" ? "Failed" : p.Runtime is { Resident: true, State: "backoff" } ? "Restarting (" + p.Runtime.RestartCount + ")" : Missing(p).Count > 0 ? "Needs setup" : p.Runtime?.Resident != true ? "Active" : p.Runtime.State == "running" ? "Running" : p.Runtime.State == "starting" ? "Starting" : "Stopped";
    public static bool Matches(AdminPlugin p, string query, string filter = "all", string job = "")
    {
        var text = string.Join(' ', Name(p), p.PluginId, p.Presentation?.Summary, p.Presentation?.PublisherName, Source(Tier(p)), p.RepositoryName,
            string.Join(' ', p.Capabilities.Select(c => c.DisplayName + " " + c.Type)));
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)) &&
            (job.Length == 0 || p.Capabilities.Any(c => c.Type == job)) && (filter switch { "attention" => p.Enabled && Rank(p) < 3, "update" => HasUpdate(p), "off" => !p.Enabled, _ => true });
    }
    // Never echo signed links or userinfo from server/plugin supplied text, including diagnostics.
    public static string SafeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var text = value.Length > 16000 ? value[..16000] + "…" : value;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"https?://[^\s<>""']+", "[link omitted]", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)\b(password|secret|token|api[_-]?key|authorization)\s*[:=]\s*[^\s,;]+", "$1=[redacted]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return text;
    }
    public static Uri? SafeLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 ? uri : null;
}
