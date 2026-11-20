using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Why a device was flagged: the numbers behind it, as the official detail pane shows them.</summary>
public sealed record AdminDeviceAnomaly(bool Outlier, bool Stale, int OverrideCount, int PlatformMedian, string Platform, int? StaleDays, string Summary, string Detail);

/// <summary>A suspect override row: <c>extreme</c>, <c>conflict</c> or <c>stale</c>, with the reason shown under it.</summary>
public sealed record AdminSettingAnomaly(string Kind, string Reason);

/// <summary>Where a device row's inherited value comes from. <paramref name="ProfileName"/> is null for the app default.</summary>
public sealed record AdminInheritedValue(string? ProfileName, string? Varies, string Display, string Raw);

/// <summary>Official 217ff25 admin devices rules: <c>AdminDevices.tsx</c>, <c>deviceOverrides.tsx</c>, <c>settingsDisplay.ts</c>, <c>inheritedSettingValue.ts</c>.</summary>
public static class AdminDevicePresentation
{
    private static readonly IReadOnlyList<DeviceSettingDefinition> All = LoadDefinitions();
    private static string T(string key, params (string Name, object? Value)[] args) => AdminUserResourceText.T(key, args);

    // The device-scoped, remotely stored settings in manifest order. Only the retired ui.theme is withheld (official ALL_DEVICE_SETTING_KEYS).
    private static IReadOnlyList<DeviceSettingDefinition> LoadDefinitions()
    {
        using var source = typeof(AdminDevicePresentation).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Admin.admin-device-settings.json")!;
        using var document = JsonDocument.Parse(source);
        return document.RootElement.EnumerateArray().Select(x => new DeviceSettingDefinition(x.Clone()))
            .Where(x => x.Data.Text("persistence") == "remote" && x.Data.Rows("allowed_scopes").Any(scope => (scope.ValueKind == JsonValueKind.String ? scope.GetString() : scope.Text("scope")) == "profile_device") && x.Key != "ui.theme").ToArray();
    }
    public static IReadOnlyList<DeviceSettingDefinition> AllDefinitions => All;
    public static DeviceSettingDefinition? Definition(string key) => All.FirstOrDefault(x => x.Key == key);
    /// <summary>Manifest position, so rows keep a stable order as overrides come and go. Unknown keys sort last.</summary>
    public static int Order(string key) { for (var i = 0; i < All.Count; i++) if (All[i].Key == key) return i; return int.MaxValue; }
    /// <summary>Settings the platform reads, plus any already stored so a stale override can still be seen and reset.</summary>
    public static IReadOnlyList<DeviceSettingDefinition> ForPlatform(string platform, IEnumerable<string>? stored = null)
    {
        var keys = (stored ?? []).ToHashSet(StringComparer.Ordinal);
        return All.Where(x => keys.Contains(x.Key) || x.AppliesTo(platform)).ToArray();
    }

    public static string Platform(string value)
    {
        var p = value.ToLowerInvariant();
        if (new[] { "tvos", "apple tv", "androidtv", "android tv", "roku", "firetv", "fire tv", "webos", "tizen" }.Any(p.Contains) || Regex.IsMatch(p, "\\btv\\b")) return "tv";
        if (p.Contains("ipad") || p.Contains("tablet")) return "tablet";
        if (new[] { "ios", "iphone", "android", "mobile", "phone" }.Any(p.Contains)) return "mobile";
        if (new[] { "mac", "win", "linux", "desktop", "chrome", "safari", "firefox", "edge", "web", "browser" }.Any(p.Contains)) return "desktop";
        return "unknown";
    }
    public static readonly IReadOnlyList<string> PlatformOrder = ["tv", "mobile", "tablet", "desktop", "unknown"];
    public static string PlatformKindLabel(string kind) => T("web.common.devicePlatform.kind." + kind);
    public static string PlatformLabel(string raw) => string.IsNullOrWhiteSpace(raw) ? T("web.common.devicePlatform.unknown") : raw;

    public static double? Age(JsonElement device, DateTimeOffset now) => DateTimeOffset.TryParse(device.Text("last_updated"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? (now - at).TotalDays : null;
    public static string Recency(JsonElement row, DateTimeOffset now) => Age(row, now) is { } age ? age < 1 ? "<24h" : age < 7 ? "<7d" : age < 30 ? "<30d" : ">30d" : ">30d";
    public static string OverrideRange(JsonElement row) => row.Number("override_count") switch { 0 => "none", <= 2 => "1-2", <= 5 => "3-5", _ => "6+" };
    public static string DeviceKey(JsonElement row) => row.Text("user_id") + "/" + row.Text("device_id");
    public static int ProfileCount(JsonElement row) => row.Rows("profiles").Count;

    public static string FormatDecimal(double value, int maximum, int minimum = 0)
    {
        var text = Math.Round(value, maximum, MidpointRounding.AwayFromZero).ToString("0." + new string('0', minimum) + new string('#', Math.Max(0, maximum - minimum)), CultureInfo.CurrentCulture);
        return text.EndsWith('.') ? text[..^1] : text;
    }

    /// <summary>Flags devices whose override pattern is unusual among their platform peers: far above the median, or dormant for 30+ days yet still overriding.</summary>
    public static IReadOnlyDictionary<string, AdminDeviceAnomaly> Anomalies(IReadOnlyList<JsonElement> devices, DateTimeOffset now)
    {
        var result = new Dictionary<string, AdminDeviceAnomaly>(StringComparer.Ordinal);
        if (devices.Count == 0) return result;
        var medians = devices.GroupBy(row => Platform(row.Text("device_platform"))).ToDictionary(group => group.Key,
            group => { var counts = group.Select(row => row.Number("override_count")).Order().ToArray(); return counts[counts.Length / 2]; });
        foreach (var row in devices)
        {
            var platform = Platform(row.Text("device_platform")); var count = row.Number("override_count"); var median = medians[platform];
            var outlier = count >= 6 || median > 0 && count >= median * 2 && count >= 4;
            var days = Age(row, now); var stale = (days ?? 0) > 30 && count > 0;
            if (!outlier && !stale) continue;
            var platformText = PlatformKindLabel(platform).ToLowerInvariant();
            var summary = new List<string>(); var detail = new List<string>();
            if (outlier)
            {
                double? ratio = median > 0 ? Math.Round(count / (double)median * 10, MidpointRounding.AwayFromZero) / 10 : null;
                summary.Add(ratio is >= 1.2 ? T("web.admin.devices.anomaly.summaryRatio", ("count", count), ("count_text", count), ("ratio", FormatDecimal(ratio.Value, 1)), ("platform", platformText))
                    : T("web.admin.devices.anomaly.summaryCount", ("count", count), ("count_text", count)));
                detail.Add(median > 0 ? T("web.admin.devices.anomaly.detailMedian", ("count", count), ("count_text", count), ("ratio", FormatDecimal(ratio ?? 0, 1)), ("platform", platformText), ("median", FormatDecimal(median, 2)))
                    : T("web.admin.devices.anomaly.detailTypical", ("count", count), ("count_text", count), ("platform", platformText)));
            }
            if (stale)
            {
                var floor = (int)Math.Floor(days ?? 0);
                summary.Add(T("web.admin.devices.anomaly.summaryStale", ("days", floor)));
                detail.Add(T("web.admin.devices.anomaly.detailStale", ("days", floor), ("days_text", floor)));
            }
            result[DeviceKey(row)] = new(outlier, stale, count, median, platform, stale ? (int)Math.Floor(days ?? 0) : null,
                summary.Count > 1 ? T("web.admin.devices.anomaly.summarySeparator", ("first", summary[0]), ("second", summary[1])) : summary[0],
                detail.Count > 1 ? T("web.admin.devices.anomaly.detailJoin", ("first", detail[0]), ("second", detail[1])) : detail[0]);
        }
        return result;
    }

    public static bool SavedView(JsonElement row, string view, IReadOnlySet<string> anomalies, DateTimeOffset now) => view switch
    {
        "anomalies" => anomalies.Contains(DeviceKey(row)),
        "recent" => (Age(row, now) ?? double.PositiveInfinity) < 7,
        "hdr" => Regex.IsMatch((row.Text("device_platform") + " " + row.Text("device_name")).ToLowerInvariant(), "tv|appletv|tvos|shield"),
        "subtitle" => row.Number("override_count") >= 3,
        "dormant" => (Age(row, now) ?? 0) > 30,
        _ => true,
    };

    // ----- per-setting rows -----
    /// <summary>The string form the admin controls edit: null and absent are empty, scalars print as themselves, structures as JSON.</summary>
    public static string ValueString(JsonElement? value) => value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? ""
        : v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText();

    /// <summary>Re-types an edited string through the manifest. Throws <see cref="ArgumentException"/> with the official message for a bad number or JSON.</summary>
    public static JsonElement ValueFromString(string key, string raw)
    {
        var definition = Definition(key); var trimmed = raw.Trim();
        if (definition?.Data.Member("value_schema") is { } schema && schema.Flag("nullable") && trimmed.Length == 0) return JsonSerializer.SerializeToElement((object?)null);
        switch (definition?.Type)
        {
            case "boolean": return JsonSerializer.SerializeToElement(trimmed == "true");
            case "integer" or "number":
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
                    throw new ArgumentException(T("web.common.settingValue.notANumber", ("key", key), ("raw", raw)));
                return definition.Type == "integer" ? JsonSerializer.SerializeToElement((long)Math.Truncate(parsed)) : JsonSerializer.SerializeToElement(parsed);
            case "object":
                try { using var document = JsonDocument.Parse(trimmed); return document.RootElement.Clone(); }
                catch (JsonException) { throw new ArgumentException(T("web.common.settingValue.invalidJson", ("key", key))); }
            default: return JsonSerializer.SerializeToElement(raw);
        }
    }

    public static string ControlKind(DeviceSettingDefinition definition)
    {
        if (definition.Data.TryGetProperty("recommended_control", out var recommended) && recommended.ValueKind == JsonValueKind.String)
        {
            var control = recommended.GetString();
            if (control is "switch" or "select" or "slider" or "stepper" or "panel" or "text") return control;
        }
        if (definition.Type == "boolean") return "switch";
        if (definition.Type is "enum" or "language_tag") return "select";
        return definition.Type is "integer" or "number" ? "slider" : "text";
    }
    /// <summary>Object-valued settings and panels have no inline widget: they open a bespoke panel or raw JSON.</summary>
    public static bool IsStructured(DeviceSettingDefinition? definition) => definition == null || definition.Type == "object" || ControlKind(definition) == "panel";
    public static string DefaultString(DeviceSettingDefinition definition) => ValueString(definition.Data.Member("default_value"));
    public static string Label(DeviceSettingDefinition definition) => definition.Label;
    private static JsonElement? Bound(DeviceSettingDefinition definition, string name) => definition.Schema.Member(name);

    /// <summary>The select entries: enum members in manifest order, a leading unset entry for a nullable value, languages from the catalog, and the bandwidth ladder for a numeric cap.</summary>
    public static IReadOnlyList<(string Value, string Label)> Options(DeviceSettingDefinition definition, string current)
    {
        if (definition.Type == "language_tag")
        {
            var unset = definition.Data.Text("unset_label");
            var rows = new List<(string, string)> { ("", unset.Length > 0 ? unset : T("web.settings.value.unset")) };
            rows.AddRange(MediaLanguageCatalog.All.Select(language => (language.Code, language.Label)));
            if (current.Length > 0 && rows.All(row => row.Item1 != current)) rows.Add((current, current));
            return rows;
        }
        var members = definition.Options.ToList();
        if (definition.Schema.Flag("nullable")) members.Insert(0, ("", T("web.settings.value.unset")));
        var numeric = definition.Type is "integer" or "number";
        if (!numeric || members.Any(x => x.Value.Length > 0)) return members;
        var min = Bound(definition, "minimum") is { ValueKind: JsonValueKind.Number } lo ? lo.GetDouble() : 0; var max = Bound(definition, "maximum") is { ValueKind: JsonValueKind.Number } hi ? hi.GetDouble() : double.MaxValue;
        var ladder = new[] { 1500, 2000, 4000, 6000, 10000, 15000, 20000, 40000, 60000, 100000, 200000 }.Where(kbps => kbps >= min && kbps <= max)
            .Select(kbps => (kbps.ToString(CultureInfo.InvariantCulture), FormatBitrate(kbps))).ToList();
        if (current.Length > 0 && ladder.All(x => x.Item1 != current) && double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
        { ladder.Add((current, FormatBitrate(parsed))); ladder.Sort((a, b) => double.Parse(a.Item1, CultureInfo.InvariantCulture).CompareTo(double.Parse(b.Item1, CultureInfo.InvariantCulture))); }
        ladder.Insert(0, ("", definition.Data.Text("unit") == "kbps" ? T("web.settings.value.noLimit") : T("web.settings.value.unset")));
        return ladder;
    }
    public static string FormatBitrate(double kbps) => kbps >= 1000
        ? T("web.common.format.bitrate.mbps", ("rate", FormatDecimal(kbps / 1000, 1))) : T("web.common.format.bitrate.kbps", ("rate", FormatDecimal(kbps, 0)));

    /// <summary>A stored value as the official rows print it.</summary>
    public static string FormatValue(string key, string? value)
    {
        var definition = Definition(key);
        if (definition == null) return value ?? T("web.settings.value.unset");
        if (ControlKind(definition) == "switch") return value == "true" ? T("web.settings.value.enabled") : T("web.settings.value.disabled");
        var unit = definition.Data.Text("unit");
        if (definition.Type == "language_tag") return Options(definition, value ?? "").FirstOrDefault(x => x.Value == (value ?? "")).Label ?? (string.IsNullOrEmpty(value) ? T("web.settings.value.unset") : value);
        if (definition.Options.Length > 0)
        {
            var fallback = value ?? DefaultString(definition);
            foreach (var option in definition.Options) if (option.Value == fallback) return option.Label;
            return fallback.Length > 0 ? fallback : T("web.settings.value.unset");
        }
        if (unit == "kbps")
        {
            var raw = value is null ? "" : value.Length > 0 ? value : DefaultString(definition);
            return raw.Length > 0 && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var kbps) && double.IsFinite(kbps) ? FormatBitrate(kbps) : T("web.settings.value.noLimit");
        }
        if (unit.Length > 0) return FormatWithUnit(value ?? (DefaultString(definition) is { Length: > 0 } d ? d : "0"), unit);
        if (definition.Type == "object") return string.IsNullOrEmpty(value) ? T("web.settings.value.usingFallback") : T("web.settings.value.custom");
        return !string.IsNullOrEmpty(value) ? value : DefaultString(definition) is { Length: > 0 } fallbackValue ? fallbackValue : T("web.settings.value.unset");
    }
    private static string FormatWithUnit(string value, string unit)
    {
        var key = unit switch { "milliseconds" => "web.settings.value.milliseconds", "seconds" => "web.settings.value.seconds", "minutes" => "web.settings.value.minutes", "episodes" => "web.settings.value.episodes", _ => "" };
        if (key.Length == 0 || value.Length == 0 || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return value + " " + unit;
        return T(key, ("count", count), ("value", value));
    }

    /// <summary>What a setting resolves to for a profile on a device that stores no value of its own, following the key's resolution order.</summary>
    public static AdminInheritedValue Inherited(DeviceSettingDefinition definition, string profileId, string profileName, IReadOnlyList<JsonElement> values)
    {
        string? varies = null;
        foreach (var scopeValue in definition.Data.Rows("resolution_order"))
        {
            var scope = scopeValue.GetString();
            if (scope == "profile_device") continue;
            if (scope == "profile")
            {
                var stored = values.FirstOrDefault(x => x.Text("key") == definition.Key && x.Text("scope") == "profile" && x.Text("profile_id") == profileId);
                if (stored.ValueKind == JsonValueKind.Object) { var raw = ValueString(stored.Member("value")); return new(profileName, varies, FormatValue(definition.Key, raw), raw); }
            }
            var scopeKind = scope switch { "profile_client" => "app family", "profile_library" => "library", "profile_series" => "series", _ => null };
            if (scopeKind != null && varies == null && values.Any(x => x.Text("key") == definition.Key && x.Text("scope") == scope && x.Text("profile_id") == profileId)) varies = scopeKind;
            if (scope == "default") break;
        }
        var fallback = DefaultString(definition);
        return new(null, varies, definition.Type == "object" ? "" : FormatValue(definition.Key, fallback), fallback);
    }
    public static string InheritedSource(AdminInheritedValue inherited)
    {
        var source = inherited.ProfileName != null ? T("web.admin.common.deviceOverrides.source.profile", ("name", inherited.ProfileName)) : T("web.admin.common.deviceOverrides.source.appDefault");
        return inherited.Varies == null ? source : T("web.admin.common.deviceOverrides.source.orVaries", ("source", source), ("scope", inherited.Varies.Replace(' ', '_')));
    }

    /// <summary>Pairwise inconsistencies among one profile's overrides on a device, keyed by setting key.</summary>
    public static IReadOnlyDictionary<string, AdminSettingAnomaly> Conflicts(IReadOnlyDictionary<string, string> overrides)
    {
        var result = new Dictionary<string, AdminSettingAnomaly>(StringComparer.Ordinal);
        string? Get(string key) => overrides.TryGetValue(key, out var value) ? value : null;
        if (Get("player.hdr_enabled") == "false" && Get("player.dv_profile7_hdr10_fallback") == "true")
        {
            result["player.dv_profile7_hdr10_fallback"] = new("conflict", T("web.admin.common.deviceOverrides.conflict.dvFallbackWithoutHdr"));
            result["player.hdr_enabled"] = new("conflict", T("web.admin.common.deviceOverrides.conflict.hdrWithDvFallback"));
        }
        if (Get("playback.auto_skip_credits") == "true" && Get("playback.auto_play_next") == "false")
            result["playback.auto_skip_credits"] = new("conflict", T("web.admin.common.deviceOverrides.conflict.skipCreditsWithoutAutoPlay"));
        return result;
    }

    /// <summary>The anomaly, if any, of one override row. Rows synthesized from a default are never flagged.</summary>
    public static AdminSettingAnomaly? RowAnomaly(string key, string value, bool isOverride, IReadOnlyDictionary<string, AdminSettingAnomaly> conflicts, int? deviceStaleDays)
    {
        if (!isOverride) return null;
        if (conflicts.TryGetValue(key, out var conflict)) return conflict;
        var definition = Definition(key);
        if (definition != null && ControlKind(definition) == "slider" && Bound(definition, "minimum") is { ValueKind: JsonValueKind.Number } lo && Bound(definition, "maximum") is { ValueKind: JsonValueKind.Number } hi
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.TryParse(DefaultString(definition).Length == 0 ? "0" : DefaultString(definition), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && double.IsFinite(v) && double.IsFinite(d))
        {
            var range = Math.Abs(hi.GetDouble() - lo.GetDouble());
            if (range > 0 && Math.Abs(v - d) / range >= 0.4)
            {
                var unit = definition.Data.Text("unit") is { Length: > 0 } u ? " " + u : "";
                return new("extreme", T("web.admin.common.deviceOverrides.anomaly.extreme", ("value", FormatDecimal(v, 4) + unit), ("default_value", FormatDecimal(d, 4) + unit)));
            }
        }
        if (deviceStaleDays is > 30) return new("stale", T("web.admin.common.deviceOverrides.anomaly.stale", ("count", deviceStaleDays), ("count_text", deviceStaleDays)));
        return null;
    }
}
