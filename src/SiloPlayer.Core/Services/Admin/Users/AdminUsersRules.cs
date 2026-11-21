using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

public static class UserJson
{
    public static string Text(this JsonNode? node, string key, string fallback = "") => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;
    public static bool Flag(this JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
    public static long Count(this JsonNode? node, string key) => TryInteger(node?[key], out var count) ? count : 0;
    public static bool TryInteger(JsonNode? node, out long value)
    {
        value = 0;
        return node?.GetValueKind() == JsonValueKind.Number && long.TryParse(node.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
    public static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        return node?.GetValueKind() == JsonValueKind.Number && double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
    public static JsonObject Copy(this JsonObject node) => node.DeepClone().AsObject();
    public static JsonArray Items(this JsonNode? node) => node?["items"] as JsonArray ?? new JsonArray();
    public static string Display(this JsonNode? node) => node switch
    {
        null => "Inherit",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString()
    };
}

public static class AdminUsersRules
{
    public static bool Available(JsonObject? capability) => capability != null && capability.Text("state") == "available" &&
        (!capability.ContainsKey("available") || capability.Flag("available")) && (!capability.ContainsKey("allowed") || capability.Flag("allowed"));
    public static bool Feature(JsonObject? capability, string feature) => Available(capability) && capability.Flag(feature);
    public static bool CanManage(JsonObject target, string viewer, bool owner) => target.Text("id") == viewer ||
        !target.Flag("is_owner") && (target.Text("role") != "admin" || owner);
    public static bool CanChangePolicy(JsonObject target, string viewer, bool owner) => CanManage(target, viewer, owner) && (target.Text("role") != "admin" || owner);
    public static bool CanManageProfiles(JsonObject target, string viewer, bool owner) => CanManage(target, viewer, owner) && (target.Text("role") != "admin" || owner);
    public static bool CanTransfer(JsonObject target, string viewer, bool owner) => owner && target.Text("id") != viewer &&
        !target.Flag("is_owner") && target.Text("role") == "admin" && target.Flag("enabled");
    public static bool CanViewAs(JsonObject target, string viewer, bool owner) => target.Flag("enabled") && !target.Flag("is_owner") &&
        target.Text("id") != viewer && (target.Text("role") != "admin" || owner);

    public static void ValidateQuota(JsonObject body)
    {
        AdminUsersContract.Validate(AdminUsersContract.Schema("AdminRequestLimitBody"), body);
        if (body.Text("limit_mode") == "custom" && (body["max_requests"] == null || body["window_days"] == null))
            throw new ArgumentException("A custom quota requires max_requests and window_days; zero requests is explicit, not inheritance.");
        if (body.Text("limit_mode") != "custom" && (body["max_requests"] != null || body["window_days"] != null))
            throw new ArgumentException("Inherited, blocked and unlimited quotas require null counts, not a custom quota.");
    }

    public static void ValidateProfile(JsonObject body, bool primary)
    {
        if (body.TryGetPropertyValue("pin", out var pin) && pin != null && (pin.Display().Length == 0 || Encoding.UTF8.GetByteCount(pin.Display()) > 72))
            throw new ArgumentException("PIN must contain 1..72 UTF-8 bytes; use Remove PIN to send null.");
        if (primary && (body.Flag("library_restrictions_enabled") || body["allowed_library_ids"] is JsonArray { Count: > 0 }))
            throw new ArgumentException("A primary profile cannot have library restrictions.");
    }
}

public sealed record AdminUserSettingIdentity(string Scope, string? ProfileId = null, string? ClientFamily = null,
    string? DeviceId = null, string? LibraryId = null, string? SeriesId = null)
{
    public static IReadOnlyList<string> Families { get; } = Array.AsReadOnly(new[] { "tv", "mobile", "tablet", "desktop", "web" });
    public void Validate()
    {
        bool Present(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 256;
        var valid = Scope switch
        {
            "account" => ProfileId == null && ClientFamily == null && DeviceId == null && LibraryId == null && SeriesId == null,
            "profile" => Present(ProfileId) && ClientFamily == null && DeviceId == null && LibraryId == null && SeriesId == null,
            "profile_client" => Present(ProfileId) && Families.Contains(ClientFamily) && DeviceId == null && LibraryId == null && SeriesId == null,
            "profile_device" => Present(ProfileId) && Present(DeviceId) && ClientFamily == null && LibraryId == null && SeriesId == null,
            "profile_library" => Present(ProfileId) && Present(LibraryId) && ClientFamily == null && DeviceId == null && SeriesId == null,
            "profile_series" => Present(ProfileId) && Present(SeriesId) && ClientFamily == null && DeviceId == null && LibraryId == null,
            _ => false
        };
        if (!valid) throw new ArgumentException("Select exactly one complete storage identity. No reset may widen its scope.");
    }
    public JsonObject ToRow() => new()
    {
        ["scope"] = Scope, ["profile_id"] = ProfileId, ["client_family"] = ClientFamily,
        ["device_id"] = DeviceId, ["library_id"] = LibraryId, ["series_id"] = SeriesId
    };
    public string Query()
    {
        Validate();
        return "?" + string.Join("&", ToRow().Where(pair => pair.Value != null)
            .Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value!.GetValue<string>())));
    }
    public static AdminUserSettingIdentity FromRow(JsonObject row)
    {
        string? Optional(string key) => string.IsNullOrEmpty(row.Text(key)) ? null : row.Text(key);
        var identity = new AdminUserSettingIdentity(row.Text("scope"), Optional("profile_id"), Optional("client_family"), Optional("device_id"), Optional("library_id"), Optional("series_id"));
        identity.Validate();
        return identity;
    }
    public override string ToString() => string.Join(" · ", ToRow().Where(pair => pair.Value != null).Select(pair => pair.Value!.GetValue<string>()));
}
