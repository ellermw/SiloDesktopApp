using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>The Home row's bounded first read and stable transition to its whole result.</summary>
public static class HomeRowWindow
{
    public const int InitialItemLimit = 20;

    public static bool IsExpanded(HomeSectionWithItems? row) => row is { HasMore: false }
        && (row.ShownCount.HasValue || row.Items.Count > InitialItemLimit);

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
        var seen = shown.Items.Concat(current.Items).Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
        var rest = whole.Items.Where(item => seen.Add(item.ContentId));
        result.Items = new(current.Items.Concat(rest).Take(Math.Max(whole.ItemLimit, whole.Items.Count)));
        result.HasMore = false;
        result.ShownCount = Math.Min(current.Items.Count, result.Items.Count);
        result.CompletedOrder = completedOrder;
        result.LoadCompleted = true;
        result.LoadFailed = false;
        return result;
    }
}
