using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class HomeSectionDedupe
{
    public static Dictionary<string, HomeSectionWithItems> Project(
        IReadOnlyList<HomeSection> layout,
        IReadOnlyDictionary<string, HomeSectionWithItems> raw,
        IReadOnlySet<string> avoidTypes)
    {
        var result = new Dictionary<string, HomeSectionWithItems>(raw);
        if (avoidTypes.Count == 0) return result;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in layout)
        {
            if (!raw.TryGetValue(entry.Id, out var section)) continue;
            var visible = section;
            if (avoidTypes.Contains(section.SectionType))
            {
                var kept = section.Items.Where(item => string.IsNullOrEmpty(item.ContentId) || !seen.Contains(item.ContentId)).ToList();
                var dropped = section.Items.Count - kept.Count;
                if (dropped > 0)
                {
                    visible = Snapshot(section);
                    visible.Items = new(kept);
                    visible.TotalCount = Math.Max(kept.Count, section.TotalCount - dropped);
                    result[entry.Id] = visible;
                }
            }
            foreach (var item in visible.Items)
                if (!string.IsNullOrEmpty(item.ContentId)) seen.Add(item.ContentId);
        }
        return result;
    }

    public static HashSet<string> AvoidTypes(RecipeCatalogResponse catalog) => catalog.Categories.Values
        .SelectMany(recipes => recipes).Where(recipe => recipe.AvoidDuplicates)
        .Select(recipe => recipe.Type).ToHashSet(StringComparer.Ordinal);

    // Mounted rows own their collections. Reconciliation must never mutate the
    // retained raw response, including when it initially had nothing to drop.
    public static HomeSectionWithItems Snapshot(HomeSectionWithItems source) => new()
    {
        Id = source.Id, SectionType = source.SectionType, Title = source.Title,
        Featured = source.Featured, ItemLimit = source.ItemLimit, TotalCount = source.TotalCount,
        IsCustom = source.IsCustom, Customized = source.Customized,
        LoadFailed = source.LoadFailed, LoadCompleted = source.LoadCompleted,
        Items = new(source.Items),
    };
}
