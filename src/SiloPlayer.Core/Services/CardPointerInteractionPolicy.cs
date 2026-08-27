namespace SiloPlayer.Core.Services;

/// <summary>
/// Keeps hover-only card affordances out of touch interactions. Touch users
/// receive the native long-press context menu instead of a transient overlay.
/// </summary>
public static class CardPointerInteractionPolicy
{
    public static bool ShouldRevealHoverActions(bool isTouchPointer)
        => !isTouchPointer;
}
