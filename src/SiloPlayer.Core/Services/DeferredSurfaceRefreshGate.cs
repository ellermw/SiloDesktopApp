namespace SiloPlayer.Core.Services;

/// <summary>
/// Remembers a refresh requested while a cached surface is hidden, then lets
/// the next activation consume exactly one refresh request.
/// </summary>
public sealed class DeferredSurfaceRefreshGate
{
    public bool HasPendingRefresh { get; private set; }

    public bool Request(bool isActive)
    {
        HasPendingRefresh = !isActive;
        return isActive;
    }

    public bool Activate()
    {
        if (!HasPendingRefresh)
            return false;

        HasPendingRefresh = false;
        return true;
    }

    public void Complete(bool succeeded)
    {
        if (!succeeded)
            HasPendingRefresh = true;
    }

    public void Reset() => HasPendingRefresh = false;
}
