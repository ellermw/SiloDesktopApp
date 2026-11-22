namespace SiloPlayer.Core.Services;

/// <summary>
/// Decides when a refresh of the page the viewer is on must wait because the page holds the viewer's unsaved edits (an Admin settings page with a draft).
/// An automatic refresh (reconnect catch-up, an access change, a language change) is not a departure and is not the viewer's choice, so it never asks them to
/// discard anything: it is remembered as owed and performed once the edits are gone (saved, or discarded by the viewer). Leaving the page makes it moot.
/// </summary>
public sealed class RefreshDeferral
{
    private bool _owed;

    /// <summary>
    /// True while the current page holds unsaved edits that a refresh would lose. It must be false when the viewer cannot act on the page any more
    /// (an Admin page whose authority was retired reports no edits), so that a refresh which has to take them away is never held back.
    /// </summary>
    public Func<bool>? HoldsEdits { get; set; }

    /// <summary>Whether a refresh is waiting for the edits to go.</summary>
    public bool Owed => _owed;

    /// <summary>Asked when a refresh is requested: true means do not refresh now (it is now owed); false means go ahead (nothing is owed).</summary>
    public bool ShouldDefer()
    {
        if (HoldsEdits?.Invoke() == true) { _owed = true; return true; }
        _owed = false;
        return false;
    }

    /// <summary>Asked while a refresh is owed: true once, when the edits are gone and the refresh should now run.</summary>
    public bool TakeDue()
    {
        if (!_owed || HoldsEdits?.Invoke() == true) return false;
        _owed = false;
        return true;
    }

    /// <summary>The page was left or replaced: the refresh that was owed no longer has anything to refresh.</summary>
    public void Clear() => _owed = false;
}
