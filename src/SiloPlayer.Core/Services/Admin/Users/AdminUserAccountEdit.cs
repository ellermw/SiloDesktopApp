using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

/// <summary>Video transcoding as the Access cards word it: off, unlimited, or up to a number at a time (official <c>VideoTranscoding</c>).</summary>
public sealed record AdminUserVideoTranscoding(string Mode, long Max = 0)
{
    public const string Off = "off", Unlimited = "unlimited", Limit = "limit";

    /// <summary>What the account resolves to now: not allowed is off, allowed with 0 is unlimited, otherwise a limit.</summary>
    public static AdminUserVideoTranscoding FromEffective(bool allowed, long max) =>
        !allowed ? new(Off) : max == 0 ? new(Unlimited) : new(Limit, max);

    /// <summary>Off sends false and no count, unlimited true and 0, a limit true and the count; Default sends neither (null, null).</summary>
    public static (bool? Allowed, long? Max) ToFields(AdminUserVideoTranscoding? value) => value == null ? (null, null) : value.Mode switch
    {
        Off => (false, null),
        Unlimited => (true, 0),
        _ => (true, value.Max),
    };

    public static bool Same(AdminUserVideoTranscoding? a, AdminUserVideoTranscoding? b) =>
        a == null || b == null ? a == b : a.Mode == b.Mode && (a.Mode != Limit || a.Max == b.Max);
}

/// <summary>
/// The rules of the Access &amp; limits card editors on the admin user detail page (official <c>pages/admin-users/detail/access</c>): what an
/// inheriting row resolves to, which rows changed and the request body a save sends. A row is "Default" (the account does not override it, null on
/// the wire) or "Custom" (an explicit value).
/// </summary>
public static class AdminUserAccountEdit
{
    /// <summary>The wire fields that can inherit (official <c>POLICY_FIELDS</c>).</summary>
    public static readonly string[] PolicyFields =
    [
        "library_ids", "max_playback_quality", "max_streams", "max_transcodes", "transcode_allowed", "audio_transcode_allowed",
        "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps", "download_allowed", "download_transcode_allowed", "requests_allowed", "max_profiles",
    ];

    /// <summary>The playback ceiling a quality override sends: any is empty, standard 1080p, 4K 2160p.</summary>
    public static string QualityValue(string preset) => preset switch { "standard" => "1080p", "4k" => "2160p", _ => "" };

    /// <summary>The preset a stored ceiling shows as: any, standard (up to 1080p) or 4k.</summary>
    public static string QualityPreset(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "any" or "original" or "auto" => "any",
        "standard" or "480p" or "720p" or "1080p" => "standard",
        "4k" or "uhd" or "2160p" or "4320p" => "4k",
        _ => "any",
    };

    /// <summary>
    /// What a row that does not override resolves to for the group picked now (official <c>policyInheritHints</c> and <c>savedUserPolicyInheritHints</c>):
    /// an admin takes the admin defaults, an ungrouped account the no-group defaults and a grouped one its group. For the group the account is saved in, a row
    /// that already inherits shows the freshest resolved value; an overridden row shows the group-only value it would inherit once the override is cleared.
    /// </summary>
    public static UserPolicyHints InheritHints(JsonObject user, string? pickedGroupId, IEnumerable<JsonObject> groups, JsonObject? defaults)
    {
        var role = user.Text("role");
        var effectiveGroup = AdminUserCreatePolicy.EffectiveAccessGroupId(role, pickedGroupId);
        JsonObject? source = role == "admin" ? defaults?["admin"] as JsonObject
            : effectiveGroup == null ? defaults?["ungrouped"] as JsonObject
            : groups.FirstOrDefault(group => group["id"]?.ToString() == effectiveGroup);
        var savedGroup = AdminUserCreatePolicy.EffectiveAccessGroupId(role, user["access_group_id"]?.ToString());
        if (effectiveGroup != savedGroup) return UserPolicyHints.FromGroup(source ?? new JsonObject());
        var merged = new JsonObject();
        var effective = user["effective_policy"] as JsonObject;
        foreach (var field in PolicyFields)
        {
            var from = user[field] == null ? effective : source;
            if (from != null && from.ContainsKey(field)) merged[field] = from[field]?.DeepClone();
        }
        return UserPolicyHints.FromGroup(merged);
    }

    /// <summary>Rows that differ from the saved account: the saved override of each field is compared with the draft (null is Default).</summary>
    public static JsonObject Diff(JsonObject user, JsonObject draft)
    {
        var body = new JsonObject();
        foreach (var pair in draft)
            if (!JsonNode.DeepEquals(user[pair.Key], pair.Value)) body[pair.Key] = pair.Value?.DeepClone();
        return body;
    }

    /// <summary>The permission list with one assignment set or cleared, sorted so two lists with the same entries compare equal.</summary>
    public static JsonArray SetPermission(JsonNode? current, string permission, bool granted)
    {
        var entries = (current as JsonArray)?.Select(node => node?.ToString() ?? "").Where(name => name.Length > 0).Where(name => name != permission).ToList() ?? [];
        if (granted) entries.Add(permission);
        entries.Sort(StringComparer.Ordinal);
        return new JsonArray(entries.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
    }

    public static bool HasPermission(JsonNode? list, string permission) => (list as JsonArray)?.Any(node => node?.ToString() == permission) == true;

    /// <summary>
    /// The server intersects an account's permissions with its group's allowed set, so an assigned permission can have no effect. A group with no
    /// allowed-permissions list allows everything; an admin or an ungrouped account is never locked. Returns the group's name when it blocks the permission.
    /// </summary>
    public static (bool Locked, string? GroupName) PermissionLock(JsonObject user, string? groupId, IEnumerable<JsonObject> groups, string permission)
    {
        if (user.Text("role") == "admin" || groupId == null) return (false, null);
        var group = groups.FirstOrDefault(candidate => candidate["id"]?.ToString() == groupId);
        if (group == null) return (false, null);
        var locked = group["allowed_permissions"] is JsonArray allowed && !allowed.Any(node => node?.ToString() == permission);
        return (locked, group.Text("name"));
    }

    /// <summary>The same libraries in any order (ids compare as sets; null is "all libraries").</summary>
    public static bool SameLibraries(JsonNode? a, JsonNode? b)
    {
        if (a is not JsonArray left || b is not JsonArray right) return a == null && b == null;
        return left.Count == right.Count && left.Select(node => node?.ToString()).ToHashSet().SetEquals(right.Select(node => node?.ToString()));
    }

    /// <summary>
    /// The Sign-in and role card body: only the rows that changed. An admin is never grouped, so a role change to admin also clears the group.
    /// A profile limit is sent only when it changed, as the override or null for Default.
    /// </summary>
    public static JsonObject SignInBody(JsonObject user, string username, string email, string role, bool enabled, bool profileLimitValid, long? profileLimit)
    {
        var body = new JsonObject();
        if (username != user.Text("username")) body["username"] = username;
        if (email != user.Text("email")) body["email"] = email;
        if (role != user.Text("role"))
        {
            body["role"] = role;
            if (role == "admin") body["access_group_id"] = null;
        }
        if (enabled != user.Flag("enabled")) body["enabled"] = enabled;
        if (profileLimitValid)
        {
            JsonNode? next = profileLimit == null ? null : JsonValue.Create(profileLimit.Value);
            if (!JsonNode.DeepEquals(user["max_profiles"], next)) body["max_profiles"] = next;
        }
        return body;
    }

    /// <summary>The Library access card body: the group (not for an admin), the library list and the permission list, each only when it changed.</summary>
    public static JsonObject LibraryBody(JsonObject user, string? groupId, JsonNode? libraryIds, JsonNode? permissions)
    {
        var body = new JsonObject();
        if (user.Text("role") != "admin" && groupId != user["access_group_id"]?.ToString()) body["access_group_id"] = groupId;
        if (!SameLibraries(libraryIds, user["library_ids"])) body["library_ids"] = libraryIds?.DeepClone();
        var saved = SetPermission(user["permissions"], "", false);
        var next = SetPermission(permissions, "", false);
        if (!JsonNode.DeepEquals(saved, next)) body["permissions"] = next;
        return body;
    }

    /// <summary>A Custom row that holds nothing valid cannot replace a saved override; one over Default simply stays on Default.</summary>
    public static bool Incomplete(bool custom, bool valid, bool savedIsOverride) => custom && !valid && savedIsOverride;
}
