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
        IEnumerable<string> removedIds, Dictionary<string, string> stableIds, string? changedSectionId = null)
    {
        var sections = source.ToList(); var raw = stored.ToList();
        var saved = raw.Where(row => !string.IsNullOrEmpty(row.SectionId) && !string.IsNullOrEmpty(row.Id))
            .GroupBy(row => row.SectionId).ToDictionary(group => group.Key, group => group.Last().Id);
        string Id(string sectionId)
        {
            if (saved.TryGetValue(sectionId, out var existing)) return existing;
            if (!stableIds.TryGetValue(sectionId, out var created)) stableIds[sectionId] = created = Guid.NewGuid().ToString();
            return created;
        }
        bool LeftOut(SettingsSectionEntry row) => !row.IsCustom && !row.Hidden && !saved.ContainsKey(row.Id) && row.Id != changedSectionId && IsTrakt(row.Config);
        var held = sections.Where(LeftOut).Select(row => row.Position).ToHashSet();
        var result = new List<SectionOverride>(); var position = 0;
        foreach (var section in sections)
        {
            if (LeftOut(section)) { position = Math.Max(position, section.Position + 1); continue; }
            while (held.Contains(position)) position++;
            var original = raw.LastOrDefault(row => row.SectionId == section.Id || (string.IsNullOrEmpty(row.SectionId) && row.Id == section.Id));
            var row = original == null ? new SectionOverride() : FromRaw(original);
            row.Id = section.IsCustom ? section.Id : Id(section.Id); row.SectionId = section.IsCustom ? null : section.Id;
            row.Position = position++; row.Hidden = section.Hidden; row.Title = section.Title; row.Featured = section.Featured;
            row.ItemLimit = section.ItemLimit; row.Config = section.Config; row.Removed = false;
            if (section.IsCustom)
            { row.IsUserAdded = true; row.SectionType = section.SectionType; row.UserSectionType = section.SectionType; row.UserConfig = section.Config; row.UserTitle = section.Title; }
            result.Add(row);
        }
        foreach (var id in removedIds.Distinct()) result.Add(new() { Id = Id(id), SectionId = id, Removed = true });
        return result;
    }
}
