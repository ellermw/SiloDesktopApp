using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ContinuumPlayer.Controls;

/// <summary>
/// F6: Shimmer-style placeholder used in place of <see cref="ProgressRing"/>
/// spinners while content is loading. Rendered as a rounded border filled
/// with <c>SurfaceRaisedBrush</c> that pulses Opacity 1 → 0.5 → 1 every
/// 1.6 s. Mirrors the webui <c>&lt;Skeleton /&gt;</c> primitive used across
/// home sections, item detail, library grids, and cast carousels.
///
/// Derives from <see cref="Grid"/> (Border is sealed in WinUI 3) and hosts
/// a single Border as its visual. The pulse animation targets this outer
/// Grid's Opacity so it affects the Border child uniformly.
/// </summary>
public class SkeletonBox : Grid
{
    private Storyboard? _pulseStoryboard;
    private readonly Border _fill;

    public SkeletonBox()
    {
        _fill = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(8),
        };
        this.Children.Add(_fill);
        Opacity = 1.0;
        Loaded += (_, _) => StartPulse();
        Unloaded += (_, _) => StopPulse();
    }

    /// <summary>Override the fill's corner radius (defaults to 8 px).</summary>
    public new CornerRadius CornerRadius
    {
        get => _fill.CornerRadius;
        set => _fill.CornerRadius = value;
    }

    private void StartPulse()
    {
        if (_pulseStoryboard != null) return;

        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(1600)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
            Value = 1.0,
        });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800)),
            Value = 0.45,
        });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600)),
            Value = 1.0,
        });
        Storyboard.SetTarget(anim, this);
        Storyboard.SetTargetProperty(anim, "Opacity");
        _pulseStoryboard = new Storyboard();
        _pulseStoryboard.Children.Add(anim);
        _pulseStoryboard.Begin();
    }

    private void StopPulse()
    {
        _pulseStoryboard?.Stop();
        _pulseStoryboard = null;
    }
}

/// <summary>
/// Poster card skeleton — matches <see cref="PosterCard"/> dimensions from
/// the shared theme tokens (178×267 by default).
/// </summary>
public class SkeletonPoster : SkeletonBox
{
    public SkeletonPoster()
    {
        double width = (double)Application.Current.Resources["PosterCardWidth"];
        double height = (double)Application.Current.Resources["PosterCardHeight"];
        Width = width;
        Height = height;
        CornerRadius = new CornerRadius(12);
    }
}

/// <summary>
/// 16:9 backdrop skeleton — for hero banners, episode stills, etc.
/// </summary>
public class SkeletonBackdrop : SkeletonBox
{
    public SkeletonBackdrop()
    {
        CornerRadius = new CornerRadius(12);
    }
}

/// <summary>
/// Horizontal text-line placeholder. Height defaults to 14px (body line),
/// width is configurable for title vs caption variants.
/// </summary>
public class SkeletonText : SkeletonBox
{
    public SkeletonText()
    {
        Height = 14;
        CornerRadius = new CornerRadius(4);
    }

    public SkeletonText(double width, double height) : this()
    {
        Width = width;
        Height = height;
    }
}
