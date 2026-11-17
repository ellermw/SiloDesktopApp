using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

public static class AdminUserRequestTerms
{
    public static string Describe(JsonObject user, JsonObject? account, JsonObject? group, JsonObject? defaults, bool inherited = false)
    {
        if (defaults == null || account == null) return "Request terms unavailable; no defaults were assumed.";
        var grouped = user.Text("role") != "admin" && user.Text("access_group_id").Length > 0;
        if (grouped && group == null) return "Group request terms unavailable; no server fallback was assumed.";
        var layers = new List<(string Source, JsonObject Layer)>();
        if (!inherited) layers.Add(("Account", account));
        if (grouped) layers.Add(("Group #" + user.Text("access_group_id"), group!));
        if (!inherited)
        {
            if (!defaults.Flag("requests_enabled")) return "Requests off server-wide.";
            if (!user["effective_policy"].Flag("requests_allowed")) return "Requests disallowed by the resolved account/group switch.";
            if (account.Text("limit_mode") == "blocked" || account.Text("approval_mode") == "blocked") return "Legacy blocked request terms; use Clear legacy block or choose inherited terms.";
        }
        var limit = layers.FirstOrDefault(row => row.Layer.Text("limit_mode") is "custom" or "unlimited");
        var quotaSource = limit.Layer == null ? "Server default" : limit.Source;
        var window = Math.Max(1, defaults.Count("global_window_days") > 0 ? defaults.Count("global_window_days") : 7);
        var maximum = defaults.Count("global_max_requests");
        if (limit.Layer != null)
        {
            if (limit.Layer.Text("limit_mode") == "unlimited") maximum = -1;
            else
            {
                if (limit.Layer["max_requests"] != null) maximum = limit.Layer.Count("max_requests");
                if (limit.Layer.Count("window_days") > 0) window = limit.Layer.Count("window_days");
            }
        }
        var approval = layers.FirstOrDefault(row => row.Layer.Text("approval_mode") is "manual" or "auto");
        var automatic = approval.Layer == null ? defaults.Flag("global_auto_approval_enabled") : approval.Layer.Text("approval_mode") == "auto";
        var quota = maximum < 0 ? "Unlimited" : maximum == 0 ? "No new requests (0 per " + window + " days; not an account block)" : maximum + " per " + window + " days";
        return quota + " · " + quotaSource + "; " + (automatic ? "Automatic approval" : "Manual approval") + " · " + (approval.Layer == null ? "Server default" : approval.Source);
    }
    public static JsonObject EditorSchema()
    {
        var schema = AdminUsersContract.Schema("AdminRequestLimitBody");
        schema["properties"]!["limit_mode"]!["enum"] = new JsonArray("inherit", "custom", "unlimited");
        schema["properties"]!["approval_mode"]!["enum"] = new JsonArray("inherit", "manual", "auto");
        return schema;
    }
    public static JsonObject ClearLegacy(JsonObject saved)
    {
        var custom = saved.Text("limit_mode") == "custom";
        return new() { ["limit_mode"] = saved.Text("limit_mode") == "blocked" ? "inherit" : saved.Text("limit_mode"),
            ["approval_mode"] = saved.Text("approval_mode") == "blocked" ? "inherit" : saved.Text("approval_mode"),
            ["max_requests"] = custom ? saved["max_requests"]?.DeepClone() : null, ["window_days"] = custom ? saved["window_days"]?.DeepClone() : null };
    }
}
