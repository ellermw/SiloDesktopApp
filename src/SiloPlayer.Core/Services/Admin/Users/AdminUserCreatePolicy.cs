using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin.Users;

/// <summary>Where a policy field that is not overridden takes its value from (official <c>PolicyDefaultSource</c>).</summary>
public enum UserPolicyDefaultSource { Group, Admin, Server }

/// <summary>
/// The per-user policy overrides of the Create user form (official <c>UserPolicyState</c>). A null field is not overridden, so the account
/// takes the value of its group (or the admin or server default); a value is an explicit override in either direction.
/// </summary>
public sealed class UserPolicyState
{
    public List<string>? LibraryIds { get; set; }
    public string? MaxPlaybackQuality { get; set; }
    public long? MaxStreams { get; set; }
    public long? MaxTranscodes { get; set; }
    public long? MaxRemoteStreamBitrateKbps { get; set; }
    public long? MaxLocalStreamBitrateKbps { get; set; }
    public long? MaxProfiles { get; set; }
    public bool? TranscodeAllowed { get; set; }
    public bool? AudioTranscodeAllowed { get; set; }
    public bool? DownloadAllowed { get; set; }
    public bool? DownloadTranscodeAllowed { get; set; }
    public bool? RequestsAllowed { get; set; }

    /// <summary>Create payload: only overridden fields are sent; absent fields inherit.</summary>
    public void WriteCreateFields(JsonObject body, bool includeMaxProfiles = true)
    {
        if (LibraryIds != null) body["library_ids"] = new JsonArray(LibraryIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        if (MaxPlaybackQuality != null) body["max_playback_quality"] = MaxPlaybackQuality;
        if (MaxStreams != null) body["max_streams"] = MaxStreams;
        if (MaxTranscodes != null) body["max_transcodes"] = MaxTranscodes;
        if (MaxRemoteStreamBitrateKbps != null) body["max_remote_stream_bitrate_kbps"] = MaxRemoteStreamBitrateKbps;
        if (MaxLocalStreamBitrateKbps != null) body["max_local_stream_bitrate_kbps"] = MaxLocalStreamBitrateKbps;
        if (includeMaxProfiles && MaxProfiles != null) body["max_profiles"] = MaxProfiles;
        if (TranscodeAllowed != null) body["transcode_allowed"] = TranscodeAllowed;
        if (AudioTranscodeAllowed != null) body["audio_transcode_allowed"] = AudioTranscodeAllowed;
        if (DownloadAllowed != null) body["download_allowed"] = DownloadAllowed;
        if (DownloadTranscodeAllowed != null) body["download_transcode_allowed"] = DownloadTranscodeAllowed;
        if (RequestsAllowed != null) body["requests_allowed"] = RequestsAllowed;
    }
}

/// <summary>What an inheriting field currently resolves to (official <c>PolicyInheritHints</c>); a null member is unknown.</summary>
public sealed class UserPolicyHints
{
    /// <summary>Null is "all libraries"; <see cref="LibraryIdsKnown"/> says whether the value is known at all.</summary>
    public List<string>? LibraryIds { get; init; }
    public bool LibraryIdsKnown { get; init; }
    public string? MaxPlaybackQuality { get; init; }
    public bool MaxPlaybackQualityKnown { get; init; }
    public long? MaxStreams { get; init; }
    public long? MaxTranscodes { get; init; }
    public long? MaxRemoteStreamBitrateKbps { get; init; }
    public long? MaxLocalStreamBitrateKbps { get; init; }
    public long? MaxProfiles { get; init; }
    public bool? TranscodeAllowed { get; init; }
    public bool? AudioTranscodeAllowed { get; init; }
    public bool? DownloadAllowed { get; init; }
    public bool? DownloadTranscodeAllowed { get; init; }
    public bool? RequestsAllowed { get; init; }

    public static UserPolicyHints FromGroup(JsonObject group) => new()
    {
        LibraryIdsKnown = group.ContainsKey("library_ids"),
        LibraryIds = group["library_ids"] is JsonArray ids ? ids.Select(node => node?.ToString() ?? "").ToList() : null,
        MaxPlaybackQualityKnown = group.ContainsKey("max_playback_quality"),
        MaxPlaybackQuality = group["max_playback_quality"]?.GetValue<string>(),
        MaxStreams = Long(group, "max_streams"), MaxTranscodes = Long(group, "max_transcodes"),
        MaxRemoteStreamBitrateKbps = Long(group, "max_remote_stream_bitrate_kbps"), MaxLocalStreamBitrateKbps = Long(group, "max_local_stream_bitrate_kbps"),
        MaxProfiles = Long(group, "max_profiles"),
        TranscodeAllowed = Flag(group, "transcode_allowed"), AudioTranscodeAllowed = Flag(group, "audio_transcode_allowed"),
        DownloadAllowed = Flag(group, "download_allowed"), DownloadTranscodeAllowed = Flag(group, "download_transcode_allowed"),
        RequestsAllowed = Flag(group, "requests_allowed"),
    };

    private static long? Long(JsonObject node, string key) => node[key] is JsonValue value && UserJson.TryInteger(value, out var number) ? number : null;
    private static bool? Flag(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
}

/// <summary>The rules of the official Create user form (<c>CreateUserForm.tsx</c>, <c>UserPolicyFields.tsx</c>) that do not depend on a control.</summary>
public static class AdminUserCreatePolicy
{
    /// <summary>The permission an account gets by default (official <c>PERMISSION_MARKER_EDIT</c>).</summary>
    public const string MarkerEdit = "marker_edit";
    public const string MetadataCuration = "metadata_curation";

    public static IReadOnlyList<string> DefaultPermissions => [MarkerEdit];

    /// <summary>Admins are never grouped: the server clears the group for the admin role, so the form never shows or sends one.</summary>
    public static string? EffectiveAccessGroupId(string role, string? accessGroupId) => role == "admin" ? null : accessGroupId;

    public static UserPolicyDefaultSource DefaultSource(string role, string? accessGroupId) =>
        role == "admin" ? UserPolicyDefaultSource.Admin : accessGroupId is null ? UserPolicyDefaultSource.Server : UserPolicyDefaultSource.Group;

    /// <summary>What the fields inherit for the role and group selected now; null while unknown (no group row, or the admin/server defaults were not read).</summary>
    public static UserPolicyHints? InheritHints(string role, string? accessGroupId, IEnumerable<JsonObject> groups)
    {
        if (role == "admin" || accessGroupId is null) return null;
        var group = groups.FirstOrDefault(candidate => candidate["id"]?.ToString() == accessGroupId);
        return group == null ? null : UserPolicyHints.FromGroup(group);
    }

    /// <summary>The group the picker shows: the one the admin picked, else the default group.</summary>
    public static string? DefaultGroupId(IEnumerable<JsonObject> groups) =>
        groups.FirstOrDefault(group => group["is_default"] is JsonValue flag && flag.TryGetValue<bool>(out var isDefault) && isDefault)?["id"]?.ToString();

    /// <summary>
    /// The Create user request body: the account fields, <c>require_password_change</c> only when it is on, the permissions, the overridden
    /// policy fields, and the group when one applies.
    /// </summary>
    public static JsonObject BuildBody(string username, string email, string password, bool requirePasswordChange, string role,
        IEnumerable<string> permissions, bool createDefaultProfile, UserPolicyState policy, string? accessGroupId, bool sendGroup = true, bool sendMaxProfiles = true)
    {
        var body = new JsonObject
        {
            ["username"] = username, ["email"] = email, ["password"] = password,
        };
        if (requirePasswordChange) body["require_password_change"] = true;
        body["role"] = role;
        body["permissions"] = new JsonArray(permissions.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        body["create_default_profile"] = createDefaultProfile;
        policy.WriteCreateFields(body, sendMaxProfiles);
        if (sendGroup && EffectiveAccessGroupId(role, accessGroupId) is { } group) body["access_group_id"] = group;
        return body;
    }

    public static IEnumerable<string> SetPermission(IEnumerable<string> current, string name, bool granted)
    {
        var rest = current.Where(entry => entry != name);
        return granted ? rest.Append(name) : rest;
    }

    // ── Limit fields ────────────────────────────────────────────────────────────────────────────

    /// <summary>A whole number at or above <paramref name="min"/>, else null (an empty or half-typed box is an unsaved edit, never 0).</summary>
    public static long? ParseLimit(string draft, long min)
    {
        if (string.IsNullOrWhiteSpace(draft)) return null;
        return long.TryParse(draft.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= min ? value : null;
    }

    /// <summary>Preset per-stream caps offered ahead of a custom value, highest first (kbps).</summary>
    public static readonly long[] StreamBitratePresetsKbps = [40000, 20000, 10000, 8000, 5000, 3000, 2000, 1000];

    public const long LowStreamBitrateKbps = 1000;

    private static readonly Regex MbpsText = new(@"^\s*(\d+([.,]\d{0,3})?|[.,]\d{1,3})\s*$", RegexOptions.CultureInvariant);

    /// <summary>A typed custom cap in Mbps as kbps: a positive number the API can store exactly (three decimals at most), else null. 0 means unlimited and has its own choice.</summary>
    public static long? ParseStreamBitrateMbps(string text)
    {
        if (!MbpsText.IsMatch(text)) return null;
        var mbps = double.Parse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        var kbps = (long)Math.Round(mbps * 1000, MidpointRounding.AwayFromZero);
        return kbps > 0 ? kbps : null;
    }

    public static bool IsLowStreamBitrate(long kbps) => kbps > 0 && kbps < LowStreamBitrateKbps;

    /// <summary>kbps as an exact Mbps number text: 8000 is "8", 1500 is "1.5", 250 is "0.25".</summary>
    public static string StreamBitrateMbpsText(long kbps) => (kbps / 1000d).ToString("0.###", CultureInfo.InvariantCulture);

    // ── Email ───────────────────────────────────────────────────────────────────────────────────

    private static readonly Regex Ipv4 = new(@"^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$", RegexOptions.CultureInvariant);
    private static readonly Regex Ipv6 = new(@"^[0-9a-fA-F:]*:[0-9a-fA-F:.]*$", RegexOptions.CultureInvariant);

    /// <summary>One real mailbox: something before a single "@" and a domain with a dot that has text on both sides (official <c>isValidEmail</c>).</summary>
    public static bool IsValidEmail(string? value)
    {
        var trimmed = (value ?? "").Trim();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at != trimmed.IndexOf('@') || trimmed.Any(char.IsWhiteSpace)) return false;
        var domain = trimmed[(at + 1)..];
        if (domain.StartsWith('[')) return IsDomainLiteral(domain);
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }

    private static bool IsDomainLiteral(string domain)
    {
        if (!domain.EndsWith(']')) return false;
        var inner = domain[1..^1];
        return Ipv4.IsMatch(inner) || inner.Length > 1 && Ipv6.IsMatch(inner);
    }
}
