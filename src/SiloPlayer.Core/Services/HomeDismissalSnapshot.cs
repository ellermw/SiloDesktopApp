using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public sealed class HomeDismissalSnapshot
{
    private readonly List<(string SectionId, int Index, MediaItem Item)> _items;
    private HomeDismissalSnapshot(List<(string, int, MediaItem)> items) => _items = items;
    public static HomeDismissalSnapshot Capture(IEnumerable<HomeSectionWithItems> sections, string contentId, string? seriesId)
        => new(sections.Where(s => s.SectionType is "continue_watching" or "next_up")
            .SelectMany(s => s.Items.Select((item, index) => (s.Id, index, item)))
            .Where(row => row.item.ContentId == contentId || (!string.IsNullOrEmpty(seriesId) &&
                (row.item.SeriesId == seriesId || row.item.ContentId == seriesId))).ToList());
    public void Restore(IEnumerable<HomeSectionWithItems> sections)
    {
        var byId = sections.ToDictionary(s => s.Id);
        foreach (var (sectionId, index, item) in _items.OrderBy(row => row.Index))
            if (byId.TryGetValue(sectionId, out var section) && !section.Items.Any(existing => existing.ContentId == item.ContentId))
                section.Items.Insert(Math.Min(index, section.Items.Count), item);
    }
}
