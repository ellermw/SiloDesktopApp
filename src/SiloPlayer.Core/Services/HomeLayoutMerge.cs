using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class HomeLayoutMerge
{
    /// <summary>
    /// The override set to save for one imported page (official mergeImportedPage). On another server the
    /// file says nothing about this server's own sections, so the profile's saved changes to them stay and
    /// only its profile-built sections are replaced. On the same server the file describes the whole page
    /// and replaces what the profile saved; <paramref name="hideSectionIds"/> are server rows to hide too.
    /// A page that replaces nothing (<see cref="HomeLayoutPage.ReplaceExisting"/> false) keeps every saved
    /// override and only hides the rows: a hidden row keeps its saved changes, and a removed row stays removed.
    /// </summary>
    public static List<SectionOverride> Build(HomeLayoutPage page, IReadOnlyList<RawSectionOverride> existing, bool sameServer, IReadOnlyCollection<string>? hideSectionIds = null)
    {
        var saved = existing.Select(HomeSectionWritePolicy.FromRaw).ToList();
        if (!sameServer) return saved.Where(row => !string.IsNullOrEmpty(row.SectionId)).Concat(page.Overrides).ToList();
        var hide = (hideSectionIds ?? []).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var bySection = new Dictionary<string, SectionOverride>();
        foreach (var row in saved) if (!string.IsNullOrEmpty(row.SectionId)) bySection[row.SectionId] = row;
        if (!page.ReplaceExisting)
        {
            var hideSet = hide.ToHashSet(); var kept = new List<SectionOverride>();
            foreach (var row in saved)
            {
                if (!string.IsNullOrEmpty(row.SectionId) && hideSet.Contains(row.SectionId) && row.Removed != true) row.Hidden = true;
                kept.Add(row);
            }
            foreach (var id in hide) if (!bySection.ContainsKey(id)) kept.Add(new() { Id = Guid.NewGuid().ToString(), SectionId = id, Hidden = true });
            return kept;
        }
        var imported = new HashSet<string>(); var result = new List<SectionOverride>();
        foreach (var row in page.Overrides)
        {
            if (string.IsNullOrEmpty(row.SectionId)) { result.Add(row); continue; }
            if (!imported.Add(row.SectionId)) continue;
            bySection.TryGetValue(row.SectionId, out var previous);
            row.Id = previous?.Id ?? Guid.NewGuid().ToString(); result.Add(row);
        }
        foreach (var id in hide)
        {
            if (!imported.Add(id)) continue;
            bySection.TryGetValue(id, out var previous);
            result.Add(new() { Id = previous?.Id ?? Guid.NewGuid().ToString(), SectionId = id, Hidden = true });
        }
        return result;
    }
}
