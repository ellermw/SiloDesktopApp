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
    private long _requested;
    private bool _opened;
    private bool _pending;
    // When the running fade ends (a Stopwatch timestamp; 0 when none runs).
    private long _fadeEnds;

    // One fade transition and one timer per UI thread, shared by every card, instead of one of each per card:
    // each is a WinRT object, and the timer an event source and a wrapper, that every full garbage collection
    // walks (about 200 of each on a built Home).
    [ThreadStatic] private static ScalarTransition? t_transition;
    [ThreadStatic] private static DispatcherQueueTimer? t_timer;
    [ThreadStatic] private static List<ArtworkReveal>? t_fading;

    // Reduced motion (Windows "Animation effects" off) reveals at once, as HeroCarousel does.
    internal static bool AnimationsEnabled => SystemMotion.AnimationsEnabled;

    internal bool IsFading => _fadeEnds != 0;

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
        _fadeEnds = 0;
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
        _fadeEnds = 0;
        var fade = !decoded && _requested != 0 &&
            Stopwatch.GetElapsedTime(_requested) > InstantReveal && AnimationsEnabled;
        image.Opacity = 1;
        wrapper.OpacityTransition = fade ? t_transition ??= new ScalarTransition { Duration = RevealDuration } : null;
        wrapper.Opacity = 1;
        if (decoded) _opened = true;
        if (!fade) { placeholder.Visibility = Visibility.Collapsed; return; }
        // Keep the blur under the artwork until the fade ends, then drop it from the tree.
        _fadeEnds = Stopwatch.GetTimestamp() + (long)((RevealDuration + TimeSpan.FromMilliseconds(50)).TotalSeconds * Stopwatch.Frequency);
        var fading = t_fading ??= new();
        if (!fading.Contains(this)) fading.Add(this);
        ScheduleFadeEnds();
    }

    // The shared timer fires when the earliest running fade ends.
    private static void ScheduleFadeEnds()
    {
        var next = long.MaxValue;
        if (t_fading is { } fading)
            foreach (var reveal in fading)
                if (reveal._fadeEnds != 0 && reveal._fadeEnds < next) next = reveal._fadeEnds;
        if (next == long.MaxValue) { t_fading?.Clear(); t_timer?.Stop(); return; }
        if (t_timer is null)
        {
            t_timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            t_timer.IsRepeating = false;
            t_timer.Tick += (_, _) => EndFades();
        }
        var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), next);
        t_timer.Stop();
        t_timer.Interval = wait > TimeSpan.FromMilliseconds(1) ? wait : TimeSpan.FromMilliseconds(1);
        t_timer.Start();
    }

    private static void EndFades()
    {
        if (t_fading is not { } fading) return;
        var now = Stopwatch.GetTimestamp();
        for (var i = fading.Count - 1; i >= 0; i--)
        {
            var reveal = fading[i];
            if (reveal._fadeEnds == 0) { fading.RemoveAt(i); continue; } // hidden or revealed again at once
            if (reveal._fadeEnds > now) continue;
            reveal._fadeEnds = 0;
            fading.RemoveAt(i);
            reveal.DropPlaceholderIfCovered();
        }
        ScheduleFadeEnds();
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
