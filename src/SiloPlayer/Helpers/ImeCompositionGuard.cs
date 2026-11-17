using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>
/// Official #4287: binds a TextBox's IME composition events to one shared
/// <see cref="ImeCompositionState"/>, so its custom key handlers can leave
/// candidate Enter, arrows and Escape to the input method.
/// </summary>
public static class ImeCompositionGuard
{
    private static readonly ConditionalWeakTable<TextBox, ImeCompositionState> States = new();

    /// <summary>Tracks <paramref name="box"/>'s composition. Repeated calls return the same state.</summary>
    public static ImeCompositionState Attach(TextBox box)
    {
        if (States.TryGetValue(box, out var existing)) return existing;
        var state = new ImeCompositionState();
        States.Add(box, state);
        box.TextCompositionStarted += (_, _) => state.Start();
        box.TextCompositionEnded += (_, _) => state.End();
        // A composition cannot outlive focus; never leave Enter blocked.
        box.LostFocus += (_, _) => state.Reset();
        return state;
    }

    /// <summary>The state attached to <paramref name="box"/>, if any.</summary>
    public static ImeCompositionState? For(TextBox box) => States.TryGetValue(box, out var state) ? state : null;

    /// <summary>
    /// Whether this key event belongs to the IME and the handler must not act on
    /// it. The TextBox and IME have already seen the key; an IME Enter or Escape
    /// is marked handled so an ancestor ContentDialog's default or close action
    /// does not take it either.
    /// </summary>
    public static bool ShouldIgnore(this ImeCompositionState state, KeyRoutedEventArgs e)
    {
        if (!state.ShouldIgnoreKey((int)e.Key, (int)e.OriginalKey)) return false;
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Escape) e.Handled = true;
        return true;
    }
}
