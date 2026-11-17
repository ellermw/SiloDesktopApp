using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

public static class AdminUserPreferences
{
    public static JsonObject ValueDefinition(JsonObject definition)
    {
        var wire = definition["value_schema"]?.DeepClone().AsObject() ?? throw new InvalidDataException("Missing setting value schema.");
        var type = wire.Text("type");
        if (type == "enum")
        {
            var values = new JsonArray((wire["values"] as JsonArray ?? []).OfType<JsonObject>().Select(row => row["value"]?.DeepClone()).ToArray());
            wire["enum"] = values;
            var example = values.FirstOrDefault(value => value != null);
            type = example is JsonValue scalar && scalar.TryGetValue<string>(out _) ? "string" : "number";
        }
        if (type == "language_tag") type = "string";
        // Settings-contract objects are JSON documents (objects or arrays), unlike mutation schema objects.
        if (type == "object")
            wire["type"] = wire.Flag("nullable") ? new JsonArray("object", "array", "null") : new JsonArray("object", "array");
        else
            wire["type"] = wire.Flag("nullable") ? new JsonArray(type, "null") : JsonValue.Create(type);
        return wire;
    }
    public static bool Supports(JsonObject definition, AdminUserSettingIdentity identity) => definition.Text("persistence") == "remote" &&
        definition["allowed_scopes"] is JsonArray scopes && scopes.Any(scope => scope?.GetValue<string>() == identity.Scope);
    public static string Inherited(JsonObject definition, AdminUserSettingIdentity identity, IEnumerable<JsonObject> rows)
    {
        var order = (definition["resolution_order"] as JsonArray ?? []).Select(node => node!.GetValue<string>()).ToArray();
        var index = Array.IndexOf(order, identity.Scope);
        var varies = new List<string>();
        foreach (var scope in order.Skip(index >= 0 ? index + 1 : 0))
        {
            if (scope == "default") break;
            var candidates = rows.Where(row => row.Text("key") == definition.Text("key") && row.Text("scope") == scope &&
                (scope == "account" || row.Text("profile_id") == identity.ProfileId)).ToList();
            if (scope is "profile" or "account" && candidates.Count > 0)
                return scope + ": " + (candidates[0]["value"]?.Display() ?? "null (explicit)") + (varies.Count > 0 ? " · may vary by " + string.Join(", ", varies) : "");
            if (candidates.Count > 0) varies.Add(scope);
        }
        return "Default: " + (definition["default_value"]?.Display() ?? "null") + (varies.Count > 0 ? " · may vary by " + string.Join(", ", varies) : "");
    }
    private static string Part(string? value) => Uri.EscapeDataString(value ?? "").Replace(".", "%2E", StringComparison.Ordinal);
    public static string LevelId(AdminUserSettingIdentity identity) => identity.Scope switch
    {
        "account" => "account",
        "profile" => "profile." + Part(identity.ProfileId),
        "profile_device" => "device." + Part(identity.ProfileId) + "." + Part(identity.DeviceId),
        "profile_client" => "client." + Part(identity.ProfileId) + "." + Part(identity.ClientFamily),
        "profile_library" => "library." + Part(identity.ProfileId) + "." + Part(identity.LibraryId),
        "profile_series" => "series." + Part(identity.ProfileId) + "." + Part(identity.SeriesId),
        _ => throw new ArgumentException("Unsupported level.")
    };
}
