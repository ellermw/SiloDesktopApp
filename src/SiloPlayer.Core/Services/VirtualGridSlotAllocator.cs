namespace SiloPlayer.Core.Services;

public static class VirtualGridSlotAllocator
{
    /// <summary>
    /// Preserves already-realized items while a bounded card pool grows in batches.
    /// Unassigned indices remain placeholders until the next creation batch.
    /// </summary>
    public static Dictionary<int, T> Assign<T>(IReadOnlyList<T> slots,
        IReadOnlyDictionary<int, T> current, int startIndex, int endIndex) where T : class
    {
        var retained = current.Where(pair => pair.Key >= startIndex && pair.Key <= endIndex)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var retainedSet = retained.Values.ToHashSet();
        var available = new Queue<T>(slots.Where(slot => !retainedSet.Contains(slot)));
        var result = new Dictionary<int, T>();
        for (var index = startIndex; index <= endIndex && result.Count < slots.Count; index++)
        {
            if (retained.TryGetValue(index, out var slot) || available.TryDequeue(out slot))
                result[index] = slot;
        }
        return result;
    }
}
