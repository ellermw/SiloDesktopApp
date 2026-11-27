using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin.Users;

/// <summary>What the server accepts on a profile (<c>max_advisory_age_supported</c> and the other flags of the profile list): a member it does not know is never sent.</summary>
public sealed record AdminProfileSupport(bool AdvisoryAge, bool RequireAdvisoryAge, bool RequestsAllowed, bool Avatars);

/// <summary>The profile editor's draft (official <c>ProfileDraft</c>, <c>profile-management.ts</c>, <c>ProfileEditorDialog</c>).</summary>
public sealed partial record AdminProfileDraft(
    string Name, string Avatar, string Pin, bool ClearPin, bool IsChild, string MaxContentRating, long? MaxAdvisoryAge, bool RequireAdvisoryAge,
    string Quality, bool LibraryRestrictions, IReadOnlyList<string> AllowedLibraries, bool RequestsAllowed)
{
    /// <summary>The ratings a profile can be capped at, lowest first; "" is no ceiling.</summary>
    public static readonly (string Value, string Label)[] ContentRatings =
    [
        ("", ""), ("G", "G / TV-G / U"), ("6", "6 / FSK 6"), ("PG", "PG / TV-PG / TV-Y7"), ("12", "12 / 12A / FSK 12"), ("PG-13", "PG-13 / TV-14"),
        ("15", "15 / MA 15+"), ("16", "16 / FSK 16"), ("R", "R / TV-MA / NC-17 / 18"),
    ];

    public const long MinAdvisoryAge = 1, MaxAdvisoryAge21 = 21;

    private static IReadOnlyList<string> SortedIds(IEnumerable<string>? ids) => (ids ?? []).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal)
        .OrderBy(id => long.TryParse(id, out var number) ? number : long.MaxValue).ThenBy(id => id, StringComparer.Ordinal).ToArray();

    /// <summary>A new profile starts empty; an edited one from what the server read.</summary>
    public static AdminProfileDraft From(JsonObject? profile) => new(
        profile?.Text("name") ?? "", profile?["avatar"]?.ToString() ?? "", "", false, profile?.Flag("is_child") ?? false,
        profile?.Text("max_content_rating") ?? "",
        profile?["max_advisory_age"] is JsonValue age && UserJson.TryInteger(age, out var years) ? years : null, profile?.Flag("require_advisory_age") ?? false,
        AdminUserAccountEdit.QualityPreset(profile?.Text("max_playback_quality")), profile?.Flag("library_restrictions_enabled") ?? false,
        SortedIds((profile?["allowed_library_ids"] as JsonArray)?.Select(node => node?.ToString() ?? "")),
        profile == null || profile["requests_allowed"] is not JsonValue flag || !flag.TryGetValue<bool>(out var allowed) || allowed);

    [GeneratedRegex(@"^\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex FourDigits();

    public static bool PinOk(string pin) => FourDigits().IsMatch(pin.Trim());

    /// <summary>Official <c>validate</c>: a name, a four-digit PIN when one is typed, and at least one library when libraries are restricted.</summary>
    public (bool Name, bool Pin, bool Libraries) Problems(bool isPrimary) =>
        (Name.Trim().Length == 0, Pin.Trim().Length > 0 && !PinOk(Pin), !isPrimary && LibraryRestrictions && AllowedLibraries.Count == 0);

    /// <summary>The kids switch (official <c>applyKidsPreset</c>/<c>clearKidsPreset</c>): on seeds PG, a library limit and no requests unless the admin set those by hand; off clears them.</summary>
    public AdminProfileDraft WithKids(bool kids, bool ratingTouched, bool librariesTouched, bool requestsTouched, bool isPrimary)
    {
        if (!kids) return this with { IsChild = false, MaxContentRating = "", MaxAdvisoryAge = null, RequireAdvisoryAge = false, Quality = "any", LibraryRestrictions = false, AllowedLibraries = [], RequestsAllowed = true };
        var next = this with { IsChild = true };
        if (!ratingTouched && next.MaxContentRating == "") next = next with { MaxContentRating = "PG" };
        if (!librariesTouched && !isPrimary && !next.LibraryRestrictions) next = next with { LibraryRestrictions = true, AllowedLibraries = [] };
        if (!requestsTouched) next = next with { RequestsAllowed = false };
        return next;
    }

    /// <summary>The create body: a member with nothing to set is left out (there is nothing to clear on a profile that does not exist yet).</summary>
    public JsonObject CreateBody(AdminProfileSupport support, bool isPrimary)
    {
        var body = new JsonObject
        {
            ["name"] = Name.Trim(), ["is_child"] = IsChild, ["library_restrictions_enabled"] = !isPrimary && LibraryRestrictions,
            ["allowed_library_ids"] = new JsonArray((!isPrimary && LibraryRestrictions ? AllowedLibraries : []).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        };
        if (Avatar.Length > 0) body["avatar"] = Avatar;
        if (MaxContentRating != "") body["max_content_rating"] = MaxContentRating;
        if (support.AdvisoryAge && MaxAdvisoryAge != null)
        {
            body["max_advisory_age"] = MaxAdvisoryAge;
            if (support.RequireAdvisoryAge && RequireAdvisoryAge) body["require_advisory_age"] = true;
        }
        if (AdminUserAccountEdit.QualityValue(Quality) is { Length: > 0 } quality) body["max_playback_quality"] = quality;
        if (support.RequestsAllowed && !RequestsAllowed) body["requests_allowed"] = false;
        if (!ClearPin && Pin.Trim().Length > 0) body["pin"] = Pin.Trim();
        return body;
    }

    /// <summary>
    /// The edit body. The official editor sends every member it owns so the profile matches the draft; this sends the members that differ from what was read, which
    /// leaves the same profile and never overwrites a member the server did not report.
    /// </summary>
    public JsonObject UpdateBody(JsonObject saved, AdminProfileSupport support, bool isPrimary)
    {
        var next = new JsonObject
        {
            ["name"] = Name.Trim(), ["avatar"] = Avatar.Length > 0 ? Avatar : null, ["is_child"] = IsChild,
            ["max_content_rating"] = MaxContentRating == "" ? null : MaxContentRating,
            ["max_playback_quality"] = AdminUserAccountEdit.QualityValue(Quality) is { Length: > 0 } quality ? quality : null,
            ["library_restrictions_enabled"] = !isPrimary && LibraryRestrictions,
            ["allowed_library_ids"] = new JsonArray((!isPrimary && LibraryRestrictions ? AllowedLibraries : []).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        };
        if (support.AdvisoryAge)
        {
            next["max_advisory_age"] = MaxAdvisoryAge;
            if (support.RequireAdvisoryAge) next["require_advisory_age"] = MaxAdvisoryAge != null && RequireAdvisoryAge;
        }
        if (support.RequestsAllowed) next["requests_allowed"] = RequestsAllowed;
        var body = new JsonObject();
        foreach (var pair in next)
        {
            var before = saved[pair.Key];
            if (pair.Key == "allowed_library_ids" ? SameIds(before, pair.Value) : pair.Key == "avatar" ? (before?.ToString() ?? "") == (pair.Value?.ToString() ?? "")
                : pair.Key == "max_playback_quality" ? AdminUserAccountEdit.QualityPreset(before?.ToString()) == Quality : JsonNode.DeepEquals(before, pair.Value)) continue;
            // A member the server did not report is only sent once the admin has moved it off its default.
            if (!saved.ContainsKey(pair.Key) && IsDefault(pair.Key, pair.Value)) continue;
            body[pair.Key] = pair.Value?.DeepClone();
        }
        if (ClearPin) body["pin"] = null; else if (Pin.Trim().Length > 0) body["pin"] = Pin.Trim();
        return body;
    }

    private static bool SameIds(JsonNode? a, JsonNode? b) => SortedIds((a as JsonArray)?.Select(node => node?.ToString() ?? "")).SequenceEqual(SortedIds((b as JsonArray)?.Select(node => node?.ToString() ?? "")));

    private static bool IsDefault(string key, JsonNode? value) => key switch
    {
        "is_child" or "library_restrictions_enabled" => value is JsonValue flag && flag.TryGetValue<bool>(out var on) && !on,
        "requests_allowed" => value is JsonValue allowed && allowed.TryGetValue<bool>(out var yes) && yes,
        "allowed_library_ids" => value is JsonArray { Count: 0 },
        "require_advisory_age" => value is JsonValue require && require.TryGetValue<bool>(out var r) && !r,
        _ => value == null,
    };
}
