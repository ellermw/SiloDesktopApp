using System.Text.Json;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class HomeSectionWritePolicy
{
    public static bool IsTrakt(Dictionary<string, object>? config)
        => config != null && new[] { "source", "source_provider" }.Any(key => config.TryGetValue(key, out var value) && value?.ToString() == "trakt");
    public static SectionOverride FromRaw(RawSectionOverride row) => new()
    {
        Id = Empty(row.Id), SectionId = Empty(row.SectionId), Position = row.Position, Hidden = row.Hidden, Removed = row.Removed,
        SectionType = Empty(row.SectionType), Title = Empty(row.Title), Featured = row.Featured, ItemLimit = row.ItemLimit,
        Config = Config(row.Config), IsUserAdded = row.IsUserAdded, UserSectionType = Empty(row.UserSectionType), UserTitle = Empty(row.UserTitle), UserConfig = Config(row.UserConfig)
    };
    private static string? Empty(string value) => string.IsNullOrEmpty(value) ? null : value;
    private static Dictionary<string, object>? Config(string json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(json);
    public static List<SectionOverride> Build(IEnumerable<SettingsSectionEntry> source, IEnumerable<RawSectionOverride> stored,
        IEnumerable<string> removedIds, Dictionary<string, string> stableIds, string? changedSectionId = null,
        IEnumerable<SettingsSectionEntry>? baseline = null, IReadOnlySet<string>? changedSectionIds = null)
    {
        var sections = source.ToList(); var raw = stored.ToList();
        var savedRows = raw.Where(row => !string.IsNullOrEmpty(row.SectionId))
            .GroupBy(row => row.SectionId).ToDictionary(group => group.Key, group => group.Last());
        var saved = raw.Where(row => !string.IsNullOrEmpty(row.SectionId) && !string.IsNullOrEmpty(row.Id))
            .GroupBy(row => row.SectionId).ToDictionary(group => group.Key, group => group.Last().Id);
        var bases = baseline?.ToList();
        var baseById = bases?.ToDictionary(row => row.Id);
        var known = bases?.Select(row => row.Id).ToHashSet() ?? [];
        var firstNew = sections.FindIndex(row => !known.Contains(row.Id));
        var newTail = firstNew < 0 ? new List<SettingsSectionEntry>() : sections.Skip(firstNew).ToList();
        var movedNew = newTail.Where((row, index) => known.Contains(row.Id) || index > 0 && row.Position < newTail[index - 1].Position).Any();
        var currentIds = sections.Select(row => row.Id).ToHashSet();
        var ordered = bases == null || raw.Any(row => !string.IsNullOrEmpty(row.SectionId) && row.Position.HasValue)
            || !sections.Where(row => known.Contains(row.Id)).Select(row => row.Id)
                .SequenceEqual(bases.Where(row => currentIds.Contains(row.Id)).Select(row => row.Id)) || movedNew;
        string Id(string sectionId)
        {
            if (saved.TryGetValue(sectionId, out var existing)) return existing;
            if (!stableIds.TryGetValue(sectionId, out var created)) stableIds[sectionId] = created = Guid.NewGuid().ToString();
            return created;
        }
        bool LeftOut(SettingsSectionEntry row) => !row.IsCustom && !row.Hidden && !saved.ContainsKey(row.Id) && row.Id != changedSectionId
            && changedSectionIds?.Contains(row.Id) != true && IsTrakt(row.Config);
        var held = sections.Where(LeftOut).Select(row => row.Position).ToHashSet();
        var result = new List<SectionOverride>(); var position = 0;
        foreach (var section in sections)
        {
            if (LeftOut(section)) { position = Math.Max(position, section.Position + 1); continue; }
            while (held.Contains(position)) position++;
            var original = raw.LastOrDefault(row => row.SectionId == section.Id || (string.IsNullOrEmpty(row.SectionId) && row.Id == section.Id));
            var row = original == null ? new SectionOverride() : FromRaw(original);
            row.Id = section.IsCustom ? section.Id : Id(section.Id); row.SectionId = section.IsCustom ? null : section.Id;
            var listPosition = position++;
            row.Position = ordered ? listPosition : section.IsCustom ? section.Position : null;
            row.Hidden = section.Hidden; row.Title = section.Title; row.Featured = section.Featured;
            row.ItemLimit = section.ItemLimit; row.Config = section.Config; row.Removed = false;
            if (section.IsCustom)
            { row.IsUserAdded = true; row.SectionType = section.SectionType; row.UserSectionType = section.SectionType; row.UserConfig = section.Config; row.UserTitle = section.Title; }
            else if (bases != null)
            {
                var before = baseById!.GetValueOrDefault(section.Id);
                savedRows.TryGetValue(section.Id, out var previous);
                row.Title = before == null || section.Title != before.Title
                    ? section.Title == section.DefaultTitle ? null : section.Title
                    : string.IsNullOrEmpty(previous?.Title) ? null : previous.Title;
                row.Featured = before == null || section.Featured != before.Featured ? section.Featured : previous?.Featured;
                row.ItemLimit = before == null || section.ItemLimit != before.ItemLimit ? section.ItemLimit : previous?.ItemLimit;
                row.Config = before == null || !EqualConfig(section.Config, before.Config) ? section.Config : Config(previous?.Config ?? "");
                if (!ordered && previous == null && !section.Hidden && row.Title == null && row.Featured == null && row.ItemLimit == null && row.Config == null) continue;
            }
            result.Add(row);
        }
        foreach (var id in removedIds.Distinct()) result.Add(new() { Id = Id(id), SectionId = id, Removed = true });
        return result;
    }

    public static SettingsSectionEntry Snapshot(SettingsSectionEntry row) => new()
    {
        Id = row.Id, SectionType = row.SectionType, Title = row.Title, DefaultTitle = row.DefaultTitle,
        Featured = row.Featured, ItemLimit = row.ItemLimit, Hidden = row.Hidden, IsCustom = row.IsCustom,
        Customized = row.Customized, Position = row.Position,
        Config = row.Config == null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(row.Config))
    };

    public static bool EqualConfig(Dictionary<string, object>? a, Dictionary<string, object>? b)
        => Canonical(JsonSerializer.SerializeToElement(a)) == Canonical(JsonSerializer.SerializeToElement(b));
    private static string Canonical(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(Canonical)) + "]",
        _ => element.GetRawText()
    };
}
