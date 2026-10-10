using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PersonalCollectionOwnershipTests
{
    [Fact]
    public void SplitRetainsListedOrderAndGroupsByAccountProfileOrderWithMissingOwnersLast()
    {
        Collection Item(string id, string owner) => new() { Id = id, CreatorProfileId = owner };
        var result = PersonalCollectionOwnership.Split([Item("missing", "removed"), Item("b2", "b"), Item("own2", "a"), Item("c", "c"), Item("b1", "b"), Item("own1", "a")],
            "a", [new Profile { Id = "c", Name = "Third" }, new Profile { Id = "a", Name = "Mine" }, new Profile { Id = "b", Name = "Second" }]);
        Assert.Equal(new[] { "own2", "own1" }, result.Own.Select(item => item.Id));
        Assert.Equal(new[] { "c", "b", "removed" }, result.Shared.Select(group => group.Id));
        Assert.Equal(new[] { "b2", "b1" }, result.Shared[1].Collections.Select(item => item.Id));
        Assert.Equal("Another profile", result.Shared[2].Name);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("other")]
    public void MissingOrDifferentProfileNeverOwnsACollection(string? profileId)
        => Assert.False(PersonalCollectionOwnership.IsOwn(new() { CreatorProfileId = "creator" }, profileId));
}
