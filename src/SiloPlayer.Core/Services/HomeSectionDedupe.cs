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
        var keptById = new Dictionary<string, List<MediaItem>>(StringComparer.Ordinal);
        var firstCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        List<MediaItem> Keep(HomeSectionWithItems section, IEnumerable<MediaItem> items)
        {
            var kept = items.Where(item => !avoidTypes.Contains(section.SectionType)
                || string.IsNullOrEmpty(item.ContentId) || !seen.Contains(item.ContentId)).ToList();
            foreach (var item in kept)
                if (!string.IsNullOrEmpty(item.ContentId)) seen.Add(item.ContentId);
            return kept;
        }
        foreach (var entry in layout)
        {
            if (!raw.TryGetValue(entry.Id, out var section)) continue;
            keptById[entry.Id] = Keep(section, section.Items.Take(section.ShownCount ?? section.Items.Count));
            firstCounts[entry.Id] = keptById[entry.Id].Count;
        }
        // Every initial prefix has priority over later cards. Completed rows
        // then contribute their tails in completion order, including on refresh.
        foreach (var section in layout.Where(entry => raw.ContainsKey(entry.Id)).Select(entry => raw[entry.Id])
            .Where(section => section.ShownCount.HasValue).OrderBy(section => section.CompletedOrder))
            keptById[section.Id].AddRange(Keep(section, section.Items.Skip(section.ShownCount!.Value)));
        foreach (var entry in layout)
        {
            if (!raw.TryGetValue(entry.Id, out var section)) continue;
            var kept = keptById[entry.Id];
            var dropped = section.Items.Count - kept.Count;
            if (dropped > 0)
            {
                var visible = Snapshot(section);
                visible.Items = new(kept);
                if (visible.ShownCount.HasValue) visible.ShownCount = firstCounts[entry.Id];
                visible.TotalCount = Math.Max(kept.Count, section.TotalCount - dropped);
                result[entry.Id] = visible;
            }
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
        HasMore = source.HasMore, ShownCount = source.ShownCount, CompletedOrder = source.CompletedOrder,
        Items = new(source.Items),
    };
}
