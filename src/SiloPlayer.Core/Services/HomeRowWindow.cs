using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>The Home row's bounded first read and stable transition to its whole result.</summary>
public static class HomeRowWindow
{
    public const int InitialItemLimit = 20;

    // Only completion establishes an initial/tail boundary. An uncapped
    // initial read (including an old-server fallback) is not an expansion.
    public static bool IsExpanded(HomeSectionWithItems? row) => row?.ShownCount.HasValue == true;

    public static void RemoveAt(HomeSectionWithItems row, int index)
    {
        if (row.ShownCount is { } shown && index < shown) row.ShownCount = Math.Max(0, shown - 1);
        row.Items.RemoveAt(index);
    }

    public static void FilterRemoved(HomeSectionWithItems shown, HomeSectionWithItems current, HomeSectionWithItems incoming)
    {
        var remaining = current.Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
        var removed = shown.Items.Where(item => !remaining.Contains(item.ContentId)).Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
        for (var index = incoming.Items.Count - 1; index >= 0; index--)
            if (removed.Contains(incoming.Items[index].ContentId)) RemoveAt(incoming, index);
    }

    public static HomeSectionWithItems Complete(HomeSectionWithItems shown, HomeSectionWithItems whole,
        HomeSectionWithItems current, long completedOrder)
    {
        var result = HomeSectionDedupe.Snapshot(whole);
        // Preserve every retained card and its priority. Replace old-language
        // objects where current text is available, without dropping cards the
        // whole response omitted (for example because of server truncation).
        var prefix = current.Items.ToList();
        var initialCount = current.ShownCount ?? prefix.Count;
        if (current.ReadLanguageGeneration != whole.ReadLanguageGeneration)
        {
            var translated = whole.Items.DistinctBy(item => item.ContentId).ToDictionary(item => item.ContentId, StringComparer.Ordinal);
            prefix = current.Items.Select(item => translated.TryGetValue(item.ContentId, out var replacement) ? replacement : item).ToList();
        }
        var seen = shown.Items.Concat(current.Items).Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
        var rest = whole.Items.Where(item => seen.Add(item.ContentId));
        // The server already budgets its response. An echoed display limit
        // must not discard unseen cards, including when we retain old cards.
        result.Items = new(prefix.Concat(rest));
        result.HasMore = whole.HasMore;
        // There is no paging cursor: repeating a whole read that adds nothing
        // cannot move forward. A later refresh can discover changed cards.
        result.HasReadWhole = !whole.HasMore || result.Items.Count == prefix.Count;
        result.ShownCount = Math.Min(initialCount, result.Items.Count);
        result.CompletedOrder = current.CompletedOrder > 0 ? current.CompletedOrder : completedOrder;
        result.LoadCompleted = true;
        result.LoadFailed = false;
        return result;
    }
}
