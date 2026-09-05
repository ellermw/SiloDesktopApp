using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class VirtualGridSlotAllocatorTests
{
    [Fact]
    public void GrowingRangeBeforeRetainedCardsDoesNotExhaustSmallCreationBatch()
    {
        var slots = Enumerable.Range(0, 44).Select(_ => new object()).ToArray();
        var current = Enumerable.Range(96, 40).ToDictionary(index => index, index => slots[index - 96]);
        var result = VirtualGridSlotAllocator.Assign(slots, current, 76, 209);
        Assert.Equal(44, result.Count);
        Assert.Equal(44, result.Values.Distinct().Count());
        foreach (var (index, slot) in current) Assert.Same(slot, result[index]);
        Assert.Equal(new[] { 76, 77, 78, 79 }, result.Keys.Where(index => index < 96).Order().ToArray());
    }

    [Fact]
    public void DisjointWindowReusesOnlyExistingSlots()
    {
        var slots = Enumerable.Range(0, 4).Select(_ => new object()).ToArray();
        var current = Enumerable.Range(0, 4).ToDictionary(index => index, index => slots[index]);
        var result = VirtualGridSlotAllocator.Assign(slots, current, 100, 199);
        Assert.Equal(new[] { 100, 101, 102, 103 }, result.Keys.ToArray());
        Assert.Equal(slots, result.Values.ToArray());
    }

    [Fact]
    public void SmallerWindowRetainsIdentityWithoutAssigningHiddenSlots()
    {
        var slots = Enumerable.Range(0, 8).Select(_ => new object()).ToArray();
        var current = Enumerable.Range(0, 8).ToDictionary(index => index, index => slots[index]);
        var result = VirtualGridSlotAllocator.Assign(slots, current, 3, 5);
        Assert.Equal(3, result.Count);
        foreach (var (index, slot) in result) Assert.Same(slots[index], slot);
    }
}
