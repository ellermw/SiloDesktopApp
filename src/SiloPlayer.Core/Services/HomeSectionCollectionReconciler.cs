using System.Collections.ObjectModel;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Reconciles a refreshed ordered section surface without replacing mounted
/// section objects. This keeps native rows, carousel position, and focus alive
/// while their server-defined metadata, items, membership, and order update.
/// </summary>
public static class HomeSectionCollectionReconciler
{
    public static bool Apply(
        ObservableCollection<HomeSectionWithItems> current,
        IReadOnlyList<HomeSectionWithItems> incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        var changed = false;
        for (var targetIndex = 0; targetIndex < incoming.Count; targetIndex++)
        {
            var next = incoming[targetIndex];
            if (targetIndex < current.Count
                && string.Equals(current[targetIndex].Id, next.Id, StringComparison.Ordinal))
            {
                changed |= HomeSectionReconciler.Apply(current[targetIndex], next) != HomeSectionChange.None;
                continue;
            }

            var existingIndex = FindSection(current, next.Id, targetIndex + 1);
            if (existingIndex >= 0)
            {
                current.Move(existingIndex, targetIndex);
                changed = true;
                changed |= HomeSectionReconciler.Apply(current[targetIndex], next) != HomeSectionChange.None;
                continue;
            }

            current.Insert(targetIndex, next);
            changed = true;
        }

        while (current.Count > incoming.Count)
        {
            current.RemoveAt(current.Count - 1);
            changed = true;
        }

        return changed;
    }

    private static int FindSection(
        IReadOnlyList<HomeSectionWithItems> sections,
        string id,
        int startIndex)
    {
        for (var index = Math.Max(0, startIndex); index < sections.Count; index++)
            if (string.Equals(sections[index].Id, id, StringComparison.Ordinal))
                return index;
        return -1;
    }
}
