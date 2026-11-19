namespace SiloPlayer.Helpers;

/// <summary>
/// Records and reverses the property writes that make covered UI inert while a
/// full-window surface is open (official Silo Server #4296, Now Listening).
/// Framework-free so the apply/restore contract is unit tested; the WinUI
/// wiring lives in <c>NowListeningFocusScope</c>.
/// </summary>
public sealed class ReversibleInertness
{
    private readonly List<Func<bool>> _restores = [];

    /// <summary>True between <see cref="Begin"/> and <see cref="Restore"/>.</summary>
    public bool IsApplied { get; private set; }

    /// <summary>Number of property writes that <see cref="Restore"/> will reverse.</summary>
    public int Count => _restores.Count;

    /// <summary>Starts a scope. Returns false (and changes nothing) while one is already applied.</summary>
    public bool Begin()
    {
        if (IsApplied) return false;
        IsApplied = true;
        return true;
    }

    /// <summary>
    /// Writes <paramref name="inert"/> and records the replaced value. A target
    /// that already holds the inert value belongs to another owner (upstream
    /// skips elements that are already <c>inert</c>), so it is left alone and
    /// not restored.
    /// </summary>
    public bool Set<TTarget, TValue>(TTarget target, Func<TTarget, TValue> get, Action<TTarget, TValue> set, TValue inert)
    {
        if (!IsApplied) throw new InvalidOperationException("Begin an inert scope before making targets inert.");
        var comparer = EqualityComparer<TValue>.Default;
        var original = get(target);
        if (comparer.Equals(original, inert)) return false;
        set(target, inert);
        _restores.Add(() =>
        {
            // Restore exactly what this scope replaced, never a value another
            // owner wrote while the scope was open.
            if (!comparer.Equals(get(target), inert)) return false;
            set(target, original);
            return true;
        });
        return true;
    }

    /// <summary>
    /// Reverses every recorded write (newest first) and ends the scope. A failing
    /// write does not stop the others. Returns the number of writes that threw.
    /// </summary>
    public int Restore()
    {
        if (!IsApplied) return 0;
        var failures = 0;
        for (var index = _restores.Count - 1; index >= 0; index--)
        {
            try { _restores[index](); }
            catch (Exception) { failures++; }
        }
        _restores.Clear();
        IsApplied = false;
        return failures;
    }

    /// <summary>
    /// Whether a sibling paints beneath the overlay in a XAML panel: a lower
    /// Canvas.ZIndex, or an equal ZIndex earlier in document order. Siblings
    /// drawn above the overlay (the mini player, the buffering pill) stay live.
    /// </summary>
    public static bool IsCovered(int zIndex, int index, int overlayZIndex, int overlayIndex) =>
        index != overlayIndex && (zIndex < overlayZIndex || (zIndex == overlayZIndex && index < overlayIndex));
}
