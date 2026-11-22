namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The selected look of the account-resource pages' segmented controls, saved views and profile tabs: the official hover fill under the foreground
/// label, whatever the accent colour is. In a Windows contrast theme the app maps that fill and that text to the window and window text colours, which
/// would make a selected tab identical to the page; the platform's own highlight pair (highlight fill, highlight text) is what tells the selected option
/// apart there, so nothing is replaced.
/// </summary>
public static class AdminSelectionLook
{
    public static readonly IReadOnlyList<string> CheckedBackgroundKeys = ["ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed"];
    public static readonly IReadOnlyList<string> CheckedForegroundKeys = ["ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed"];
    public static readonly IReadOnlyList<string> CheckedBorderKeys = ["ToggleButtonBorderBrushChecked", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed"];

    /// <summary>Whether the checked state's fill, text and border are replaced with the official look: never in a Windows contrast theme.</summary>
    public static bool ReplacesCheckedLook(bool systemContrast) => !systemContrast;

    /// <summary>The resource keys written onto a toggle button for its checked state under the given theme.</summary>
    public static IReadOnlyList<string> KeysReplaced(bool systemContrast) =>
        ReplacesCheckedLook(systemContrast) ? [.. CheckedBackgroundKeys, .. CheckedForegroundKeys, .. CheckedBorderKeys] : [];

    /// <summary>
    /// The resource keys to remove from a toggle button that was given the official look earlier, when the theme is now <paramref name="systemContrast"/>: all of
    /// them in a contrast theme (so the platform's highlight pair shows again), none otherwise. Together with <see cref="KeysReplaced"/> this is the whole rule
    /// for re-applying the look when the setting changes while a page is open.
    /// </summary>
    public static IReadOnlyList<string> KeysRemoved(bool systemContrast) =>
        ReplacesCheckedLook(systemContrast) ? [] : [.. CheckedBackgroundKeys, .. CheckedForegroundKeys, .. CheckedBorderKeys];
}

/// <summary>
/// One shared signal for "the system contrast setting changed", so every control that looks at it is told while its page is open and none is told after it
/// has left. The source (the accessibility setting's change event) is attached only while somebody listens. Subscribe from the UI thread; the source may
/// raise from any thread, so a subscriber moves to its own thread before it touches a control.
/// </summary>
public sealed class AdminContrastSignal
{
    private readonly Func<bool> _read; private readonly Action<Action> _attach, _detach;
    private readonly object _gate = new(); private readonly List<Action<bool>> _subscribers = [];
    private readonly Action _raise;

    /// <param name="read">Whether a Windows contrast theme is on now.</param>
    /// <param name="attach">Starts listening to the source and calls the given action on each change.</param>
    /// <param name="detach">Stops listening with the same action.</param>
    public AdminContrastSignal(Func<bool> read, Action<Action> attach, Action<Action> detach)
    {
        _read = read; _attach = attach; _detach = detach; _raise = Raise;
    }

    /// <summary>Whether a contrast theme is on now.</summary>
    public bool Current => _read();
    /// <summary>How many controls are listening.</summary>
    public int Listeners { get { lock (_gate) return _subscribers.Count; } }

    /// <summary>Starts telling <paramref name="changed"/> the new setting; dispose to stop.</summary>
    public IDisposable Subscribe(Action<bool> changed)
    {
        lock (_gate) { _subscribers.Add(changed); if (_subscribers.Count == 1) _attach(_raise); }
        return new Subscription(this, changed);
    }

    private void Raise()
    {
        Action<bool>[] now; lock (_gate) now = [.. _subscribers];
        if (now.Length == 0) return;
        var value = _read();
        foreach (var subscriber in now) { try { subscriber(value); } catch { /* one control's failure does not stop the others */ } }
    }

    private void Remove(Action<bool> changed)
    {
        lock (_gate) { if (!_subscribers.Remove(changed)) return; if (_subscribers.Count == 0) _detach(_raise); }
    }

    private sealed class Subscription(AdminContrastSignal owner, Action<bool> changed) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) owner.Remove(changed); }
    }
}
