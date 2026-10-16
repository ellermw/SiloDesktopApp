using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class HomeLayoutMerge
{
    public static List<SectionOverride> Build(HomeLayoutPage page, IReadOnlyList<RawSectionOverride> existing, bool sameServer)
    {
        var saved = existing.Select(HomeSectionWritePolicy.FromRaw).ToList();
        if (!sameServer) return saved.Where(row => !string.IsNullOrEmpty(row.SectionId)).Concat(page.Overrides).ToList();
        var bySection = new Dictionary<string, SectionOverride>();
        foreach (var row in saved) if (!string.IsNullOrEmpty(row.SectionId)) bySection[row.SectionId] = row;
        var imported = new HashSet<string>(); var result = new List<SectionOverride>();
        foreach (var row in page.Overrides)
        {
            if (string.IsNullOrEmpty(row.SectionId)) { result.Add(row); continue; }
            if (!imported.Add(row.SectionId)) continue;
            bySection.TryGetValue(row.SectionId, out var previous);
            row.Id = previous?.Id ?? Guid.NewGuid().ToString(); result.Add(row);
        }
        return result;
    }
}
