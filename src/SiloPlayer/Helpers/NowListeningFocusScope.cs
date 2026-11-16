using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Helpers;

/// <summary>
/// Official Silo Server #4296 (385d90755) for the expanded audiobook player:
/// while Now Listening covers the app, the covered shell is taken out of the
/// keyboard order and the UI Automation control view, focus starts on its
/// Back control, and on collapse focus returns to the mini-player control that
/// opened it. WinUI has no <c>inert</c>; the covered siblings get
/// <see cref="AccessibilityView.Raw"/> and covered controls are disabled, which
/// makes them and every descendant unfocusable and reports them unavailable to
/// UI Automation. The overlay itself cycles Tab (<c>TabFocusNavigation="Cycle"</c>).
/// Every write is reversed exactly on close; no event handlers are attached.
/// </summary>
public sealed class NowListeningFocusScope
{
    private readonly ReversibleInertness _inertness = new();
    private WeakReference<DependencyObject>? _opener;
    private int _generation;

    public bool IsOpen => _inertness.IsApplied;

    /// <summary>Number of covered-shell property writes currently held.</summary>
    public int InertWrites => _inertness.Count;

    /// <summary>
    /// Makes the overlay's covered siblings in <paramref name="shell"/> inert,
    /// remembers <paramref name="opener"/> and moves focus with
    /// <paramref name="focusInitial"/>. A repeated open while open is a no-op,
    /// so it neither re-captures the opener nor steals focus again.
    /// </summary>
    public bool Open(Panel shell, UIElement overlay, DependencyObject? opener, Func<bool> focusInitial)
    {
        if (!_inertness.Begin()) return false;
        var generation = ++_generation;
        _opener = opener != null && !IsWithin(opener, overlay) ? new WeakReference<DependencyObject>(opener) : null;
        var children = shell.Children;
        var overlayIndex = children.IndexOf(overlay);
        var overlayZIndex = Canvas.GetZIndex(overlay);
        if (overlayIndex >= 0)
        {
            for (var index = 0; index < children.Count; index++)
            {
                var element = children[index];
                if (!ReversibleInertness.IsCovered(Canvas.GetZIndex(element), index, overlayZIndex, overlayIndex)) continue;
                _inertness.Set<UIElement, AccessibilityView>(element,
                    target => AutomationProperties.GetAccessibilityView(target),
                    (target, value) => AutomationProperties.SetAccessibilityView(target, value),
                    AccessibilityView.Raw);
                if (element is Control control)
                    _inertness.Set<Control, bool>(control, target => target.IsEnabled, (target, value) => target.IsEnabled = value, false);
            }
        }
        Attempt(overlay, generation, _ => focusInitial());
        return true;
    }

    /// <summary>
    /// Restores the covered shell exactly. With <paramref name="returnFocus"/>,
    /// focus goes to <paramref name="preferred"/> (the mini-player opener), else
    /// the remembered opener, else the first focusable element of
    /// <paramref name="fallbackScope"/>.
    /// </summary>
    public bool Close(UIElement anchor, bool returnFocus, Func<bool>? preferred = null, DependencyObject? fallbackScope = null)
    {
        if (!_inertness.IsApplied) return false;
        var generation = ++_generation;
        _inertness.Restore();
        DependencyObject? opener = null;
        _opener?.TryGetTarget(out opener);
        _opener = null;
        if (returnFocus)
        {
            Attempt(anchor, generation, final =>
                preferred?.Invoke() == true
                || TryFocus(opener)
                || (final && fallbackScope != null && TryFocus(FocusManager.FindFirstFocusableElement(fallbackScope))));
        }
        return true;
    }

    /// <summary>
    /// Tries focus now; a target that was only just made visible may not accept
    /// focus until layout, so one low-priority retry follows. The generation
    /// drops a retry superseded by a later open or close.
    /// </summary>
    private void Attempt(UIElement anchor, int generation, Func<bool, bool> focus)
    {
        if (focus(false)) return;
        anchor.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation == _generation) focus(true);
        });
    }

    /// <summary>
    /// Programmatic focus (the focus visual follows the last input device, like
    /// the WebUI's <c>focus()</c>). Succeeds when the element holds focus
    /// afterwards: <c>Focus</c> also returns false when it only set logical focus,
    /// for example while the window is in the background.
    /// </summary>
    public static bool TryFocus(DependencyObject? target) => target switch
    {
        UIElement { Visibility: Visibility.Visible, XamlRoot: not null } element =>
            element.Focus(FocusState.Programmatic) || element.FocusState != FocusState.Unfocused,
        Microsoft.UI.Xaml.Documents.Hyperlink link =>
            link.Focus(FocusState.Programmatic) || link.FocusState != FocusState.Unfocused,
        _ => false,
    };

    private static bool IsWithin(DependencyObject element, UIElement root)
    {
        if (element is not UIElement) return false;
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, root)) return true;
        return false;
    }
}
