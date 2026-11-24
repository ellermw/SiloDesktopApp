using System.Text.Json;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Which server rows a same-server Home layout import offers to hide (official
/// web/src/lib/homeLayoutBaseline.ts, silo-server 3b2b32168, #4648).
/// </summary>
public static class HomeLayoutBaseline
{
    public static string PageKey(string scope, int? libraryId)
        => $"{scope}:{(libraryId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")}";

    /// <summary>
    /// The server rows on a same-server page that were added since the export. Only an export
    /// whose tokens still match at least one row the profile sees now can tell new rows from old
    /// ones: a file without tokens, or whose tokens match nothing (the server's key changed, or
    /// every row was replaced), offers none and imports as it did before tokens existed.
    /// A new row the file already has a change for is left out.
    /// </summary>
    public static List<ProfileSectionBaselineRow> RowsAddedSinceExport(HomeLayoutPage page, ProfileSectionBaseline baseline)
    {
        var exported = new HashSet<string>(page.ServerSectionTokens ?? [], StringComparer.Ordinal);
        if (!baseline.VisibleSections.Any(row => exported.Contains(row.Token))) return [];
        var mentioned = page.Overrides.Select(row => row.SectionId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);
        return baseline.VisibleSections.Where(row => !exported.Contains(row.Token) && !mentioned.Contains(row.Id)).ToList();
    }
}

/// <summary>One server row the import preview offers to hide on a page; checked unless the viewer unchecks it.</summary>
public sealed class HomeLayoutHideOption
{
    public string Key { get; init; } = "";
    public string PageKey { get; init; } = "";
    public string PageLabel { get; init; } = "";
    public required ProfileSectionBaselineRow Row { get; init; }
    public bool Checked { get; set; } = true;
}

/// <summary>
/// The optional <c>server_section_tokens</c> of an export page. Absent stays null (an older export);
/// a malformed or oversized list, or an entry that is not a non-empty string of at most 128
/// characters, becomes an empty list so the import offers no rows to hide on that page.
/// Duplicates are dropped.
/// </summary>
public sealed class HomeLayoutTokenListConverter : JsonConverter<List<string>?>
{
    public const int MaximumTokensPerPage = 10_000;
    public const int MaximumTokenLength = 128;
    public override bool HandleNull => true;
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > MaximumTokensPerPage) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal); var tokens = new List<string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return [];
            var token = item.GetString()!;
            if (token.Length == 0 || token.Length > MaximumTokenLength) return [];
            if (seen.Add(token)) tokens.Add(token);
        }
        return tokens;
    }
    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray(); foreach (var token in value) writer.WriteStringValue(token); writer.WriteEndArray();
    }
}
