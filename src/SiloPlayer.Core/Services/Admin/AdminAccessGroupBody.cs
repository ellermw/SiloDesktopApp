using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The access group body a save sends, and what a member move changes. Official 217ff25 <c>accessGroupDraft.ts</c>,
/// <c>playback-quality.ts</c> and <c>accessGroupPolicyChanges.ts</c>. A draft starts from the raw stored values.
/// </summary>
public static class AdminAccessGroupBody
{
    public static readonly IReadOnlyList<string> Keys =
    [
        "name", "description", "library_ids", "max_playback_quality", "download_allowed", "download_transcode_allowed", "transcode_allowed",
        "audio_transcode_allowed", "max_streams", "max_transcodes", "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps",
        "max_profiles", "allowed_permissions", "requests_allowed", "is_default",
    ];
    private static readonly string[] Unordered = ["library_ids", "allowed_permissions"];
    public const string MetadataCuration = "metadata_curation";
    public const string MarkerEdit = "marker_edit";

    /// <summary>"" (any), "1080p" or "2160p": every spelling the server may hold maps to one of the three.</summary>
    public static string CanonicalQuality(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "standard" or "480p" or "720p" or "1080p" => "1080p",
        "4k" or "uhd" or "2160p" or "4320p" => "2160p",
        _ => "",
    };
    public static string QualityPreset(string? value) => CanonicalQuality(value) switch { "2160p" => "4k", "1080p" => "standard", _ => "any" };
    public static string QualityValue(string preset) => preset switch { "standard" => "1080p", "4k" => "2160p", _ => "" };

    /// <summary>The body a save would send for the group as stored, built like the editor's draft so an untouched draft compares equal.</summary>
    public static JsonObject Stored(JsonElement group)
    {
        var result = new JsonObject();
        foreach (var key in Keys) result[key] = group.Member(key) is { } child ? JsonNode.Parse(child.GetRawText()) : null;
        result["name"] = group.Text("name").Trim(); result["description"] = group.Text("description").Trim();
        result["max_playback_quality"] = QualityValue(QualityPreset(group.Text("max_playback_quality")));
        result["download_transcode_allowed"] = group.Flag("download_allowed") && group.Flag("download_transcode_allowed");
        return result;
    }

    /// <summary>What a save sends: trimmed text and a transcoded-download flag that cannot contradict a disabled download.</summary>
    public static JsonObject ForSave(JsonObject draft)
    {
        var body = (JsonObject)draft.DeepClone();
        body["name"] = (draft["name"]?.GetValue<string>() ?? "").Trim(); body["description"] = (draft["description"]?.GetValue<string>() ?? "").Trim();
        body["download_transcode_allowed"] = draft["download_allowed"]?.GetValue<bool>() == true && draft["download_transcode_allowed"]?.GetValue<bool>() == true;
        return body;
    }

    private static string Comparable(string key, JsonNode? value)
    {
        if (Unordered.Contains(key) && value is JsonArray array)
            return JsonSerializer.Serialize(array.Select(item => item?.ToJsonString() ?? "null").Order(StringComparer.Ordinal).ToArray());
        return value?.ToJsonString() ?? "null";
    }
    /// <summary>Sets, not sequences: reordering libraries or permissions alone is not an edit worth a group write.</summary>
    public static bool Same(JsonObject a, JsonObject b) => Keys.All(key => Comparable(key, a[key]) == Comparable(key, b[key]));

    // Mirrors access.NoGroupPolicy(): what an account in no group inherits, so a move out of no group lists what it loses.
    public static readonly JsonElement NoGroupPolicy = JsonDocument.Parse("""
        {"library_ids":null,"max_playback_quality":"","download_allowed":true,"download_transcode_allowed":false,"transcode_allowed":true,
         "audio_transcode_allowed":true,"max_streams":0,"max_transcodes":0,"max_remote_stream_bitrate_kbps":0,"max_local_stream_bitrate_kbps":0,
         "max_profiles":5,"allowed_permissions":null,"requests_allowed":true}
        """).RootElement.Clone();

    private static string G(string key) => AdminUserResourceText.T("web.admin.access.groups." + key);
    private static string Limit(int value) => value > 0 ? value.ToString(System.Globalization.CultureInfo.CurrentCulture) : G("changes.unlimited");
    private static string Bitrate(int kbps) => kbps <= 0 ? G("changes.unlimited") : AdminStreamBitrate.Format(kbps);
    private static string Allowed(bool value) => G(value ? "changes.allowed" : "changes.notAllowed");
    public static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "", 1 => items[0], 2 => items[0] + " and " + items[1], _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1],
    };
    private static string Libraries(JsonElement ids, IReadOnlyDictionary<string, string> names)
    {
        if (ids.ValueKind != JsonValueKind.Array) return G("card.allLibraries");
        var rows = ids.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToArray();
        if (rows.Length == 0) return G("changes.noLibraries");
        return JoinList(rows.Select(id => names.TryGetValue(id, out var name) ? name : AdminUserResourceText.T("web.admin.access.groups.changes.libraryId", ("id", id))).ToArray());
    }
    public static string PermissionLabel(string value) => value switch { MetadataCuration => G("permissions.metadataCuration"), MarkerEdit => G("permissions.markerEdit"), _ => value };
    private static string Permissions(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array) return G("changes.allPermissions");
        var rows = values.EnumerateArray().Select(x => PermissionLabel(x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "")).Order(StringComparer.CurrentCulture).ToArray();
        return rows.Length == 0 ? G("changes.noPermissions") : JoinList(rows);
    }
    public static string QualityLabel(string? value) => QualityPreset(value) switch
    {
        "4k" => "4K", "standard" => AdminUserResourceText.T("web.profiles.helpers.quality.standard"),
        _ => AdminUserResourceText.T("web.profiles.helpers.quality.any"),
    };
    private static bool SameIds(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != JsonValueKind.Array || b.ValueKind != JsonValueKind.Array) return a.ValueKind == b.ValueKind;
        var left = a.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet(StringComparer.Ordinal);
        var right = b.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet(StringComparer.Ordinal);
        return left.SetEquals(right) && a.GetArrayLength() == b.GetArrayLength();
    }

    /// <summary>The group policies that differ between two groups, as members inheriting them see it.</summary>
    public static IReadOnlyList<AdminPolicyChange> PolicyChanges(JsonElement before, JsonElement after, IReadOnlyDictionary<string, string> libraryNames)
    {
        var changes = new List<AdminPolicyChange>();
        void Add(string label, string a, string b) { if (a != b) changes.Add(new(G("changes." + label), a, b)); }
        JsonElement Value(JsonElement item, string key) => item.Member(key) ?? default;
        if (!SameIds(Value(before, "library_ids"), Value(after, "library_ids"))) changes.Add(new(G("changes.libraries"), Libraries(Value(before, "library_ids"), libraryNames), Libraries(Value(after, "library_ids"), libraryNames)));
        Add("playbackQuality", QualityLabel(before.Text("max_playback_quality")), QualityLabel(after.Text("max_playback_quality")));
        Add("downloads", Allowed(before.Flag("download_allowed")), Allowed(after.Flag("download_allowed")));
        Add("convertedDownloads", Allowed(before.Flag("download_transcode_allowed")), Allowed(after.Flag("download_transcode_allowed")));
        Add("videoTranscoding", Allowed(before.Flag("transcode_allowed")), Allowed(after.Flag("transcode_allowed")));
        Add("audioTranscoding", Allowed(before.Flag("audio_transcode_allowed")), Allowed(after.Flag("audio_transcode_allowed")));
        Add("concurrentStreams", Limit(before.Number("max_streams")), Limit(after.Number("max_streams")));
        Add("concurrentTranscodes", Limit(before.Number("max_transcodes")), Limit(after.Number("max_transcodes")));
        Add("householdProfiles", before.Number("max_profiles").ToString(System.Globalization.CultureInfo.CurrentCulture), after.Number("max_profiles").ToString(System.Globalization.CultureInfo.CurrentCulture));
        Add("remoteBitrate", Bitrate(before.Number("max_remote_stream_bitrate_kbps")), Bitrate(after.Number("max_remote_stream_bitrate_kbps")));
        Add("localBitrate", Bitrate(before.Number("max_local_stream_bitrate_kbps")), Bitrate(after.Number("max_local_stream_bitrate_kbps")));
        Add("permissions", Permissions(Value(before, "allowed_permissions")), Permissions(Value(after, "allowed_permissions")));
        Add("requests", Allowed(before.Flag("requests_allowed")), Allowed(after.Flag("requests_allowed")));
        return changes;
    }
}
