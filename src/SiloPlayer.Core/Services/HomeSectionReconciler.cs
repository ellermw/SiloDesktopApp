using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

[Flags]
public enum HomeSectionChange
{
    None = 0,
    Metadata = 1,
    Items = 2,
}

/// <summary>
/// Applies a refreshed Home response without replacing unchanged card objects
/// or the ObservableCollection mounted by the ItemsRepeater.
/// </summary>
public static class HomeSectionReconciler
{
    /// <summary>
    /// Returns whether the hero's materialized item snapshot still matches the
    /// visible prefix of the reconciled featured section.
    /// </summary>
    public static bool IsHeroSnapshotCurrent(
        IList<MediaItem>? snapshot,
        IList<MediaItem>? source,
        int itemLimit)
    {
        var sourceCount = source?.Count ?? 0;
        var visibleCount = itemLimit > 0
            ? Math.Min(itemLimit, sourceCount)
            : sourceCount;
        if ((snapshot?.Count ?? 0) != visibleCount)
            return false;

        for (var index = 0; index < visibleCount; index++)
        {
            if (!ReferenceEquals(snapshot![index], source![index]))
                return false;
        }

        return true;
    }

    public static HomeSectionChange Apply(HomeSectionWithItems current, HomeSectionWithItems incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        var change = ApplyMetadata(current, incoming)
            ? HomeSectionChange.Metadata
            : HomeSectionChange.None;

        incoming.Items ??= [];
        if (MediaItemCollectionReconciler.Apply(current.Items, incoming.Items))
            change |= HomeSectionChange.Items;

        return change;
    }

    private static bool ApplyMetadata(HomeSectionWithItems current, HomeSectionWithItems incoming)
    {
        var changed = current.Id != incoming.Id
            || current.SectionType != incoming.SectionType
            || current.Title != incoming.Title
            || current.Featured != incoming.Featured
            || current.ItemLimit != incoming.ItemLimit
            || current.TotalCount != incoming.TotalCount
            || current.IsCustom != incoming.IsCustom
            || current.Customized != incoming.Customized
            || current.LoadFailed != incoming.LoadFailed
            || current.LoadCompleted != incoming.LoadCompleted;

        current.Id = incoming.Id;
        current.SectionType = incoming.SectionType;
        current.Title = incoming.Title;
        current.Featured = incoming.Featured;
        current.ItemLimit = incoming.ItemLimit;
        current.TotalCount = incoming.TotalCount;
        current.IsCustom = incoming.IsCustom;
        current.Customized = incoming.Customized;
        current.LoadFailed = incoming.LoadFailed;
        current.LoadCompleted = incoming.LoadCompleted;
        return changed;
    }

}
