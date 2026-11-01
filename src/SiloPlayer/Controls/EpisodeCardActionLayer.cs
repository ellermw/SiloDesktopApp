using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>Keeps still actions visible while either the pointer or focus is within their card.</summary>
internal sealed class EpisodeCardActionLayer : Grid
{
    private readonly FrameworkElement _owner;
    private bool _pointerWithin, _focusWithin;

    internal EpisodeCardActionLayer(FrameworkElement owner)
    {
        _owner = owner;
        owner.PointerEntered += OnOwnerPointerEntered;
        owner.PointerExited += OnOwnerPointerExited;
        owner.GotFocus += (_, _) => { _focusWithin = true; Refresh(); };
        owner.LostFocus += (_, _) => owner.DispatcherQueue.TryEnqueue(() =>
        {
            var focused = owner.XamlRoot == null ? null : FocusManager.GetFocusedElement(owner.XamlRoot) as DependencyObject;
            _focusWithin = false;
            for (var element = focused; element != null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, _owner)) { _focusWithin = true; break; }
            Refresh();
        });
        owner.Unloaded += (_, _) => { _pointerWithin = _focusWithin = false; Refresh(); };
        Refresh();
    }

    private void OnOwnerPointerEntered(object sender, PointerRoutedEventArgs args) { _pointerWithin = true; Refresh(); }
    private void OnOwnerPointerExited(object sender, PointerRoutedEventArgs args) { _pointerWithin = false; Refresh(); }
    private void Refresh()
    {
        var reveal = _pointerWithin || _focusWithin;
        Opacity = reveal ? 1 : 0;
        IsHitTestVisible = reveal;
    }
}
