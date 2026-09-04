namespace SiloPlayer.Core.Services;

/// <summary>
/// Tracks the logical media identity whose auto-skip marker latches are active.
/// Transport reloads keep the same identity; a new playback or content item resets it.
/// </summary>
public sealed class AutoSkipMarkerIdentity
{
    private readonly object _gate = new();
    private string? _contentId;

    /// <summary>
    /// Returns true when marker latches must be reset for <paramref name="contentId"/>.
    /// Repeated publication for the same content is treated as a transport reload.
    /// </summary>
    public bool ShouldResetFor(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return false;

        lock (_gate)
        {
            if (string.Equals(_contentId, contentId, StringComparison.Ordinal))
                return false;

            _contentId = contentId;
            return true;
        }
    }

    /// <summary>Clears the active identity so replaying the same item starts fresh.</summary>
    public void Clear()
    {
        lock (_gate)
            _contentId = null;
    }
}
