using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class CardPointerInteractionPolicyTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void HoverActionsAreNeverRevealedByTouch(
        bool isTouchPointer,
        bool expected)
    {
        Assert.Equal(
            expected,
            CardPointerInteractionPolicy.ShouldRevealHoverActions(isTouchPointer));
    }
}
