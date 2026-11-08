using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace SiloPlayer.Helpers;

/// <summary>
/// Shows a card's artwork over its blurred placeholder, as the library grid does: artwork that arrives
/// within <see cref="InstantReveal"/> of the request shows at once; later artwork fades in over the
/// blur (only with Windows animation effects on), and the blur leaves the tree after the fade. The fade
/// sits on <c>wrapper</c>: WinUI refuses an OpacityTransition on the image, whose hover zoom uses its
/// RenderTransform. UI thread only.
/// </summary>
internal sealed class ArtworkReveal(UIElement wrapper, Image image, UIElement placeholder)
{
    internal static readonly TimeSpan InstantReveal = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(250);
    private static readonly Windows.UI.ViewManagement.UISettings? s_uiSettings = CreateUISettings();

    private long _requested;
    private bool _opened;
    private bool _pending;
    private ScalarTransition? _transition;
    private DispatcherQueueTimer? _placeholderTimer;

    private static Windows.UI.ViewManagement.UISettings? CreateUISettings()
    {
        try { return new Windows.UI.ViewManagement.UISettings(); } catch { return null; }
    }

    // Reduced motion (Windows "Animation effects" off) reveals at once, as HeroCarousel does.
    internal static bool AnimationsEnabled
    {
        get { try { return s_uiSettings?.AnimationsEnabled ?? true; } catch { return true; } }
    }

    internal bool IsFading => _placeholderTimer?.IsRunning == true;

    /// <summary>A new artwork request: a later reveal measures how long the card waited from here.</summary>
    internal void Begin()
    {
        _requested = Stopwatch.GetTimestamp();
        _opened = false;
        _pending = false;
    }

    /// <summary>Clears at once: a recycled card never fades the previous artwork out.</summary>
    internal void Hide()
    {
        _placeholderTimer?.Stop();
        _requested = 0;
        _opened = false;
        _pending = false;
        wrapper.OpacityTransition = null;
        wrapper.Opacity = 0;
        image.Opacity = 0;
    }

    /// <summary>
    /// Shows the artwork just set on the image. A <paramref name="decoded"/> bitmap (reused from
    /// <see cref="DecodedArtworkCache"/>) is already on screen material: it shows at once and the
    /// placeholder leaves now. Otherwise revealing waits for ImageOpened, so decoding cannot consume
    /// the fade before the artwork can draw.
    /// </summary>
    internal void Reveal(bool decoded = false)
    {
        if (image.Source is null) return;
        if (!decoded && !_opened) { _pending = true; return; }
        _pending = false;
        _placeholderTimer?.Stop();
        var fade = !decoded && _requested != 0 &&
            Stopwatch.GetElapsedTime(_requested) > InstantReveal && AnimationsEnabled;
        image.Opacity = 1;
        wrapper.OpacityTransition = fade ? _transition ??= new ScalarTransition { Duration = RevealDuration } : null;
        wrapper.Opacity = 1;
        if (decoded) _opened = true;
        if (!fade) { placeholder.Visibility = Visibility.Collapsed; return; }
        // Keep the blur under the artwork until the fade ends, then drop it from the tree.
        if (_placeholderTimer is null)
        {
            _placeholderTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _placeholderTimer.Interval = RevealDuration + TimeSpan.FromMilliseconds(50);
            _placeholderTimer.IsRepeating = false;
            _placeholderTimer.Tick += (_, _) => DropPlaceholderIfCovered();
        }
        _placeholderTimer.Stop();
        _placeholderTimer.Start();
    }

    /// <summary>The image's ImageOpened: the artwork is decoded and can cover the placeholder.</summary>
    internal void Opened()
    {
        _opened = true;
        // ImageOpened can also arrive synchronously while Source is assigned, before Reveal().
        if (_pending) Reveal();
    }

    // Only while revealed artwork still covers it: a rebind meanwhile owns the placeholder.
    private void DropPlaceholderIfCovered()
    {
        if (_opened && image.Source != null && image.Opacity >= 1 && wrapper.Opacity >= 1)
            placeholder.Visibility = Visibility.Collapsed;
    }
}
