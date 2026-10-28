namespace SiloPlayer.Core.Services;

/// <summary>
/// A read kept racing profile-state mutations of the same item (watched,
/// progress, lists) past its retry budget. Callers show a retryable failure
/// rather than presenting or playing state already known to be superseded.
/// </summary>
public sealed class UserStateChangedException(string contentId)
    : Exception("This title's watched or progress state changed while it was loading. Try again.")
{
    public string ContentId { get; } = contentId;
}
