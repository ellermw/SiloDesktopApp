using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public static class AdminDevicePresentation
{
    private static readonly IReadOnlyList<DeviceSettingDefinition> AllDefinitions = LoadDefinitions();
    public static DeviceSettingDefinition? Definition(string key) => AllDefinitions.FirstOrDefault(x => x.Key == key);
    private static IReadOnlyList<DeviceSettingDefinition> LoadDefinitions()
    {
        using var source = typeof(AdminDevicePresentation).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Admin.admin-device-settings.json")!;
        using var document = JsonDocument.Parse(source);
        return document.RootElement.EnumerateArray().Select(x => new DeviceSettingDefinition(x.Clone())).ToArray();
    }
    public static IReadOnlyList<DeviceSettingDefinition> Definitions(int revision, string platform, IEnumerable<string> stored)
    {
        var storedKeys = stored.ToHashSet(StringComparer.Ordinal);
        string[] hidden = ["ui.app_language", "ui.theme", "ui.text_scale", "ui.text_weight", "ui.high_contrast", "ui.library_page_state", "ui.remember_library_page_state", "nav.primary_menu", "ui.card_presentation"];
        return AllDefinitions.Where(x => x.Data.Number("introduced_in", 1) <= revision && !hidden.Contains(x.Key) && !(x.Key == "playback.auto_skip_intro" && revision >= 7) && (x.AppliesTo(platform) || storedKeys.Contains(x.Key))).ToArray();
    }
    public static string Platform(string value)
    {
        var p = value.ToLowerInvariant();
        if (new[] { "tvos", "apple tv", "androidtv", "android tv", "roku", "firetv", "fire tv", "webos", "tizen" }.Any(p.Contains) || System.Text.RegularExpressions.Regex.IsMatch(p, "\\btv\\b")) return "tv";
        if (p.Contains("ipad") || p.Contains("tablet")) return "tablet";
        if (new[] { "ios", "iphone", "android", "mobile", "phone" }.Any(p.Contains)) return "mobile";
        if (new[] { "mac", "win", "linux", "desktop", "chrome", "safari", "firefox", "edge", "web", "browser" }.Any(p.Contains)) return "desktop";
        return "unknown";
    }
    public static double? Age(JsonElement device, DateTimeOffset now) => DateTimeOffset.TryParse(device.Text("last_updated"), out var at) ? (now - at).TotalDays : null;
    public static string Recency(JsonElement row, DateTimeOffset now) => Age(row, now) is { } age ? age < 1 ? "<24h" : age < 7 ? "<7d" : age < 30 ? "<30d" : ">30d" : ">30d";
    public static string OverrideRange(JsonElement row) => row.Number("override_count") switch { 0 => "none", <= 2 => "1-2", <= 5 => "3-5", _ => "6+" };
    public static bool Anomaly(JsonElement row, IReadOnlyList<JsonElement> devices, DateTimeOffset now)
    {
        var peers = devices.Where(x => Platform(x.Text("device_platform")) == Platform(row.Text("device_platform"))).Select(x => x.Number("override_count")).Order().ToArray();
        var median = peers.Length == 0 ? 0 : peers[peers.Length / 2]; var count = row.Number("override_count");
        return count >= 6 || median > 0 && count >= median * 2 && count >= 4 || Age(row, now) > 30 && count > 0;
    }
    public static string DeviceKey(JsonElement row) => row.Text("user_id") + "/" + row.Text("device_id");
    public static IReadOnlySet<string> Anomalies(IReadOnlyList<JsonElement> devices, DateTimeOffset now)
    {
        var medians = devices.GroupBy(row => Platform(row.Text("device_platform"))).ToDictionary(group => group.Key,
            group => { var counts = group.Select(row => row.Number("override_count")).Order().ToArray(); return counts[counts.Length / 2]; });
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in devices)
        {
            var count = row.Number("override_count"); var median = medians[Platform(row.Text("device_platform"))];
            if (count >= 6 || median > 0 && count >= median * 2 && count >= 4 || Age(row, now) > 30 && count > 0) result.Add(DeviceKey(row));
        }
        return result;
    }
    public static bool SavedView(JsonElement row, string view, IReadOnlyList<JsonElement> fleet, DateTimeOffset now) => view switch
    {
        "anomalies" => Anomaly(row, fleet, now), "recent" => (Age(row, now) ?? double.PositiveInfinity) < 7,
        "hdr" => System.Text.RegularExpressions.Regex.IsMatch(row.Text("device_platform") + " " + row.Text("device_name"), "tv|appletv|tvos|shield", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        "subtitle" => row.Number("override_count") >= 3, "dormant" => (Age(row, now) ?? 0) > 30, _ => true,
    };
    public static (JsonElement Value, string Source) Inherited(DeviceSettingDefinition definition, string profile, IReadOnlyList<JsonElement> values)
    {
        var varies = "";
        foreach (var scopeValue in definition.Data.Rows("resolution_order"))
        {
            var scope = scopeValue.GetString();
            if (scope == "profile")
            {
                var stored = values.FirstOrDefault(x => x.Text("key") == definition.Key && x.Text("scope") == "profile" && x.Text("profile_id") == profile);
                if (stored.Member("value") is { } value) return (value, "From profile" + varies);
            }
            if ((scope is "profile_client" or "profile_library" or "profile_series") && values.Any(x => x.Text("key") == definition.Key && x.Text("scope") == scope && x.Text("profile_id") == profile))
                varies = "; other scopes may apply";
        }
        return (definition.Data.Member("default_value") ?? JsonSerializer.SerializeToElement((object?)null), "App default" + varies);
    }
}
