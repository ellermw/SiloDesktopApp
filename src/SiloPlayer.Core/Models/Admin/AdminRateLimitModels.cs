using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Models.Admin;

public enum AdminRateLimitFieldKind { Toggle, Choice, Rate, Burst }

/// <summary>One editable rate-limit member, addressed by its JSON path (official <c>RateLimitConfig</c>).</summary>
public sealed record AdminRateLimitField(string Path, AdminRateLimitFieldKind Kind, double Max = 0)
{
    public bool IsNumber => Kind is AdminRateLimitFieldKind.Rate or AdminRateLimitFieldKind.Burst;
}

/// <summary>
/// The 31 values the Security page edits (official SecurityAccessSettings.tsx at c11b0a96). The server
/// holds 13 sign-in endpoints; only the eight shown here are read, compared or sent, and the five
/// hidden ones (invitation, password reset, reset request, Apple/Android push retirement) are kept by
/// the PATCH merge. Bounds are the server's <c>validateRateLimitConfig</c> limits.
/// </summary>
public static class AdminRateLimitFields
{
    public const double MaxRate = 2_147_483_647;
    public const double MaxGlobalRate = 35_791_394;
    public const double MaxBurst = 2_147_483_647;
    public const string Enabled = "enabled", Backend = "backend", Global = "global_requests_per_second";
    public const string IpPerSecond = "ip_requests_per_second", IpPerMinute = "ip_requests_per_minute", IpBurst = "ip_burst";
    public const string QuotaMode = "session_quota.mode", QuotaPerHour = "session_quota.requests_per_hour", QuotaBurst = "session_quota.burst";
    public static IReadOnlyList<string> Tiers { get; } = Array.AsReadOnly(new[] { "standard", "elevated" });
    public static IReadOnlyList<string> Endpoints { get; } = Array.AsReadOnly(new[]
        { "login", "signup", "setup", "password_change", "device_start", "device_lookup", "device_poll", "autoscan_webhook" });
    public static IReadOnlyList<string> Backends { get; } = Array.AsReadOnly(new[] { "memory", "redis" });
    public static IReadOnlyList<string> QuotaModes { get; } = Array.AsReadOnly(new[] { "off", "log", "enforce" });
    public static string Tier(string tier, string member) => $"tiers.{tier}.{member}";
    public static string Endpoint(string endpoint, string member) => $"auth_endpoints.{endpoint}.{member}";

    public static IReadOnlyList<AdminRateLimitField> All { get; } = Build();
    private static readonly Dictionary<string, AdminRateLimitField> ByPath = All.ToDictionary(field => field.Path, StringComparer.Ordinal);

    public static AdminRateLimitField Get(string path) =>
        ByPath.TryGetValue(path, out var field) ? field : throw new ArgumentException("Unknown rate-limit field.", nameof(path));
    public static bool Contains(string path) => ByPath.ContainsKey(path);

    private static ReadOnlyCollection<AdminRateLimitField> Build()
    {
        var fields = new List<AdminRateLimitField>
        {
            new(Enabled, AdminRateLimitFieldKind.Toggle), new(Backend, AdminRateLimitFieldKind.Choice),
            new(Global, AdminRateLimitFieldKind.Rate, MaxGlobalRate),
            new(IpPerSecond, AdminRateLimitFieldKind.Rate, MaxRate), new(IpPerMinute, AdminRateLimitFieldKind.Rate, MaxRate),
            new(IpBurst, AdminRateLimitFieldKind.Burst, MaxBurst),
            new(QuotaMode, AdminRateLimitFieldKind.Choice), new(QuotaPerHour, AdminRateLimitFieldKind.Rate, MaxRate),
            new(QuotaBurst, AdminRateLimitFieldKind.Burst, MaxBurst),
        };
        foreach (var tier in Tiers)
        {
            fields.Add(new(Tier(tier, "requests_per_second"), AdminRateLimitFieldKind.Rate, MaxRate));
            fields.Add(new(Tier(tier, "requests_per_minute"), AdminRateLimitFieldKind.Rate, MaxRate));
            fields.Add(new(Tier(tier, "burst"), AdminRateLimitFieldKind.Burst, MaxBurst));
        }
        foreach (var endpoint in Endpoints)
        {
            fields.Add(new(Endpoint(endpoint, "requests_per_minute"), AdminRateLimitFieldKind.Rate, MaxRate));
            fields.Add(new(Endpoint(endpoint, "burst"), AdminRateLimitFieldKind.Burst, MaxBurst));
        }
        return fields.AsReadOnly();
    }
}

/// <summary>
/// An immutable set of the 31 visible rate-limit values: bool, string or double. Equality is structural
/// (numeric for numbers), so returning a field to its saved value is clean (#4309).
/// </summary>
public sealed class AdminRateLimitValues : IEquatable<AdminRateLimitValues>
{
    private readonly IReadOnlyDictionary<string, object> _values;
    internal AdminRateLimitValues(IDictionary<string, object> values)
    {
        if (values.Count != AdminRateLimitFields.All.Count || AdminRateLimitFields.All.Any(field => !values.ContainsKey(field.Path)))
            throw new ArgumentException("Every rate-limit field needs a value.", nameof(values));
        foreach (var field in AdminRateLimitFields.All) RequireType(field, values[field.Path]);
        _values = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(values, StringComparer.Ordinal));
    }

    /// <summary>The official WebUI <c>DEFAULT_CONFIG</c>, used only to fill members a successful read omits.</summary>
    public static AdminRateLimitValues Defaults { get; } = new(DefaultMap());

    public object this[string path] => _values[AdminRateLimitFields.Get(path).Path];
    public bool Bool(string path) => (bool)this[path];
    public string Text(string path) => (string)this[path];
    public double Number(string path) => (double)this[path];
    public IEnumerable<string> Paths => AdminRateLimitFields.All.Select(entry => entry.Path);

    public AdminRateLimitValues With(string path, object value)
    {
        var field = AdminRateLimitFields.Get(path);
        RequireType(field, value);
        var copy = new Dictionary<string, object>(_values, StringComparer.Ordinal) { [field.Path] = value };
        return new(copy);
    }

    /// <summary>Paths whose values differ from <paramref name="other"/>, in field order.</summary>
    public IReadOnlyList<string> Differences(AdminRateLimitValues other) =>
        AdminRateLimitFields.All.Where(field => !Equals(_values[field.Path], other._values[field.Path])).Select(field => field.Path).ToArray();

    /// <summary>
    /// The PATCH body: every visible member (the WebUI sends its whole hydrated draft), exact snake-case
    /// keys, no null tokens and integer bursts. Hidden endpoints are omitted so the server merge keeps them.
    /// </summary>
    public Dictionary<string, object> ToPatchBody()
    {
        object Value(string path) => AdminRateLimitFields.Get(path).Kind == AdminRateLimitFieldKind.Burst ? (object)checked((long)Number(path)) : _values[path];
        Dictionary<string, object> Group(string prefix, params string[] members) =>
            members.ToDictionary(member => member, member => Value(prefix + member), StringComparer.Ordinal);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["enabled"] = Bool(AdminRateLimitFields.Enabled),
            ["backend"] = Text(AdminRateLimitFields.Backend),
            ["global_requests_per_second"] = Value(AdminRateLimitFields.Global),
            ["tiers"] = AdminRateLimitFields.Tiers.ToDictionary(tier => tier,
                tier => (object)Group($"tiers.{tier}.", "requests_per_second", "requests_per_minute", "burst"), StringComparer.Ordinal),
            ["ip_requests_per_second"] = Value(AdminRateLimitFields.IpPerSecond),
            ["ip_requests_per_minute"] = Value(AdminRateLimitFields.IpPerMinute),
            ["ip_burst"] = Value(AdminRateLimitFields.IpBurst),
            ["auth_endpoints"] = AdminRateLimitFields.Endpoints.ToDictionary(endpoint => endpoint,
                endpoint => (object)Group($"auth_endpoints.{endpoint}.", "requests_per_minute", "burst"), StringComparer.Ordinal),
            ["session_quota"] = Group("session_quota.", "mode", "requests_per_hour", "burst"),
        };
    }

    /// <summary>
    /// Hydrates a GET config body as the WebUI does: absent tiers, endpoints, IP members or session quota
    /// take the official defaults; members that are present must be well-formed or the read fails.
    /// </summary>
    public static AdminRateLimitValues FromConfig(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw Invalid();
        var map = DefaultMap();
        map[AdminRateLimitFields.Enabled] = root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? enabled.GetBoolean() : throw Invalid();
        var backend = OptionalText(root, "backend");
        map[AdminRateLimitFields.Backend] = string.IsNullOrEmpty(backend) ? "memory"
            : AdminRateLimitFields.Backends.Contains(backend) ? backend : throw Invalid();
        map[AdminRateLimitFields.Global] = Rate(Required(root, "global_requests_per_second"));
        ReadOptional(root, "ip_requests_per_second", map, AdminRateLimitFields.IpPerSecond);
        ReadOptional(root, "ip_requests_per_minute", map, AdminRateLimitFields.IpPerMinute);
        ReadOptional(root, "ip_burst", map, AdminRateLimitFields.IpBurst);
        if (root.TryGetProperty("tiers", out var tiers) && tiers.ValueKind != JsonValueKind.Null)
        {
            if (tiers.ValueKind != JsonValueKind.Object) throw Invalid();
            foreach (var tier in AdminRateLimitFields.Tiers)
                if (tiers.TryGetProperty(tier, out var value) && value.ValueKind != JsonValueKind.Null)
                    foreach (var member in new[] { "requests_per_second", "requests_per_minute", "burst" })
                        map[AdminRateLimitFields.Tier(tier, member)] = Member(Required(value, member), AdminRateLimitFields.Tier(tier, member));
        }
        if (root.TryGetProperty("auth_endpoints", out var endpoints) && endpoints.ValueKind != JsonValueKind.Null)
        {
            if (endpoints.ValueKind != JsonValueKind.Object) throw Invalid();
            foreach (var endpoint in AdminRateLimitFields.Endpoints)
                if (endpoints.TryGetProperty(endpoint, out var value) && value.ValueKind != JsonValueKind.Null)
                    foreach (var member in new[] { "requests_per_minute", "burst" })
                        map[AdminRateLimitFields.Endpoint(endpoint, member)] = Member(Required(value, member), AdminRateLimitFields.Endpoint(endpoint, member));
        }
        if (root.TryGetProperty("session_quota", out var quota) && quota.ValueKind != JsonValueKind.Null)
        {
            var mode = OptionalText(quota, "mode");
            map[AdminRateLimitFields.QuotaMode] = mode != null && AdminRateLimitFields.QuotaModes.Contains(mode) ? mode : throw Invalid();
            map[AdminRateLimitFields.QuotaPerHour] = Rate(Required(quota, "requests_per_hour"));
            map[AdminRateLimitFields.QuotaBurst] = Burst(Required(quota, "burst"));
        }
        return new(map);
    }

    private static void ReadOptional(JsonElement root, string name, Dictionary<string, object> map, string path)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null) map[path] = Member(value, path);
    }
    private static double Member(JsonElement value, string path) =>
        AdminRateLimitFields.Get(path).Kind == AdminRateLimitFieldKind.Burst ? Burst(value) : Rate(value);
    private static JsonElement Required(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : throw Invalid();
    private static string? OptionalText(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid();
    private static double Rate(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : throw Invalid();
    private static double Burst(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : throw Invalid();
    private static InvalidDataException Invalid() => new("Invalid rate limit settings response.");

    private static void RequireType(AdminRateLimitField field, object value)
    {
        var valid = field.Kind switch
        {
            AdminRateLimitFieldKind.Toggle => value is bool,
            AdminRateLimitFieldKind.Choice => value is string,
            AdminRateLimitFieldKind.Burst => value is double burst && double.IsFinite(burst) && Math.Floor(burst) == burst,
            _ => value is double rate && double.IsFinite(rate),
        };
        if (!valid) throw new ArgumentException("Invalid rate-limit value.", nameof(value));
    }

    private static Dictionary<string, object> DefaultMap()
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [AdminRateLimitFields.Enabled] = true, [AdminRateLimitFields.Backend] = "memory", [AdminRateLimitFields.Global] = 1000d,
            [AdminRateLimitFields.IpPerSecond] = 120d, [AdminRateLimitFields.IpPerMinute] = 6000d, [AdminRateLimitFields.IpBurst] = 120d,
            [AdminRateLimitFields.QuotaMode] = "log", [AdminRateLimitFields.QuotaPerHour] = 10000d, [AdminRateLimitFields.QuotaBurst] = 30000d,
        };
        void Tier(string tier, double second, double minute, double burst)
        {
            map[AdminRateLimitFields.Tier(tier, "requests_per_second")] = second;
            map[AdminRateLimitFields.Tier(tier, "requests_per_minute")] = minute;
            map[AdminRateLimitFields.Tier(tier, "burst")] = burst;
        }
        void Endpoint(string endpoint, double minute, double burst)
        {
            map[AdminRateLimitFields.Endpoint(endpoint, "requests_per_minute")] = minute;
            map[AdminRateLimitFields.Endpoint(endpoint, "burst")] = burst;
        }
        Tier("standard", 20, 1200, 20); Tier("elevated", 100, 6000, 100);
        Endpoint("login", 20, 10); Endpoint("signup", 10, 6); Endpoint("setup", 10, 6); Endpoint("password_change", 10, 5);
        Endpoint("device_start", 20, 10); Endpoint("device_lookup", 60, 20); Endpoint("device_poll", 120, 30); Endpoint("autoscan_webhook", 60, 30);
        return map;
    }

    public bool Equals(AdminRateLimitValues? other) => other != null && Differences(other).Count == 0;
    public override bool Equals(object? obj) => Equals(obj as AdminRateLimitValues);
    public override int GetHashCode() => AdminRateLimitFields.All.Count;
    public override string ToString() => nameof(AdminRateLimitValues);
}

/// <summary>A strongly validated config read with the runtime status read beside it. Runtime fields never affect the validator.</summary>
public sealed record AdminRateLimitSnapshot(AdminRateLimitValues Config, string ETag, bool Active, string? ActiveBackend, bool? RedisAvailable)
{
    /// <summary>Redis can be chosen unless the server says it is not configured; an older server without the field leaves it enabled.</summary>
    public bool RedisSelectable => RedisAvailable != false;
    public string SavedBackend => Config.Text(AdminRateLimitFields.Backend);
    public bool LimiterInactive => Config.Bool(AdminRateLimitFields.Enabled) && !Active;
    /// <summary>The running backend when it differs from the saved one, from server data (never the draft).</summary>
    public string? RunningBackendDrift => Active && !string.IsNullOrEmpty(ActiveBackend) && ActiveBackend != SavedBackend ? ActiveBackend : null;
    public override string ToString() => nameof(AdminRateLimitSnapshot);
}

/// <summary>
/// Number entry for rate-limit boxes. Accepts only a plain ASCII integer (surrounding white space and a
/// leading '+' allowed) within 1..the server bound; anything else leaves the draft unchanged. Values
/// are shown invariantly with no separators, so an untouched stored fraction such as 0.5 round-trips.
/// </summary>
public static class AdminRateLimitNumber
{
    public static bool TryParse(string? raw, double max, out double value)
    {
        value = 0;
        var text = (raw ?? "").Trim().Trim('﻿').Trim();
        if (text.StartsWith('+')) text = text[1..];
        if (text.Length == 0 || text.Length > 12 || text.Any(c => c is < '0' or > '9')) return false;
        var parsed = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        if (parsed < 1 || parsed > max) return false;
        value = parsed;
        return true;
    }

    public static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
