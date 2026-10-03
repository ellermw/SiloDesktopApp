using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class HomeLayoutMerge
{
    public static List<SectionOverride> Build(HomeLayoutPage page, IReadOnlyList<RawSectionOverride> existing, bool sameServer, IReadOnlySet<string> keepSavedSectionIds)
    {
        var saved = existing.Select(HomeSectionWritePolicy.FromRaw).ToList();
        var trakt = saved.Where(row => string.IsNullOrEmpty(row.SectionId) && HomeSectionWritePolicy.IsTrakt(row.UserConfig ?? row.Config)).ToList();
        if (!sameServer) return saved.Where(row => !string.IsNullOrEmpty(row.SectionId)).Concat(trakt).Concat(page.Overrides).ToList();
        var bySection = new Dictionary<string, SectionOverride>();
        foreach (var row in saved) if (!string.IsNullOrEmpty(row.SectionId)) bySection[row.SectionId] = row;
        var imported = new HashSet<string>(); var result = new List<SectionOverride>();
        foreach (var row in page.Overrides)
        {
            if (string.IsNullOrEmpty(row.SectionId)) { result.Add(row); continue; }
            if (!imported.Add(row.SectionId)) continue;
            bySection.TryGetValue(row.SectionId, out var previous);
            if (keepSavedSectionIds.Contains(row.SectionId))
            {
                if (previous != null) result.Add(previous);
                else if (row.Hidden == true || row.Removed == true) { row.Id = Guid.NewGuid().ToString(); result.Add(row); }
                continue;
            }
            row.Id = previous?.Id ?? Guid.NewGuid().ToString(); result.Add(row);
        }
        foreach (var (id, row) in bySection) if (!imported.Contains(id) && keepSavedSectionIds.Contains(id)) result.Add(row);
        return result.Concat(trakt).ToList();
    }
}
