using Microsoft.UI.Xaml.Media.Animation;

namespace SiloPlayer.Controls;

/// <summary>
/// Reusable slide-in sheet primitive (F8). Hosts arbitrary content that
/// animates in from the right edge with a dimmed scrim. Dismisses on scrim
/// tap, close button click, or <see cref="IsOpen"/> = false. Place at the
/// top level of a page's root Grid so the scrim covers the whole page.
///
/// Use cases: catalog filter editor, subtitle-pick panel, quality picker,
/// anything that wants a modal-ish side panel without the heft of a
/// ContentDialog (which centers + blurs aggressively).
/// </summary>
public sealed partial class SlideSheet : UserControl
{
    private const double SheetWidth = 420;
    private static readonly TimeSpan AnimDuration = TimeSpan.FromMilliseconds(220);
    private int _animationGeneration;
    private Storyboard? _activeStoryboard;

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(
            nameof(IsOpen),
            typeof(bool),
            typeof(SlideSheet),
            new PropertyMetadata(false, OnIsOpenChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(SlideSheet),
            new PropertyMetadata("", OnTitleChanged));

    public static readonly DependencyProperty SheetContentProperty =
        DependencyProperty.Register(
            nameof(SheetContent),
            typeof(object),
            typeof(SlideSheet),
            new PropertyMetadata(null));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? SheetContent
    {
        get => GetValue(SheetContentProperty);
        set => SetValue(SheetContentProperty, value);
    }

    /// <summary>Fires after the sheet has fully animated closed. Useful for
    /// cleaning up content or resetting form state.</summary>
    public event Action? Closed;

    public SlideSheet()
    {
        this.InitializeComponent();
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SlideSheet s)
        {
            if ((bool)e.NewValue) s.Open();
            else s.Close();
        }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SlideSheet s) s.TitleText.Text = (string)e.NewValue;
    }

    private void Open()
    {
        var generation = ++_animationGeneration;
        _activeStoryboard?.Stop();
        this.Visibility = Visibility.Visible;

        var sb = new Storyboard();

        var scrimAnim = new DoubleAnimation
        {
            From = Scrim.Opacity, To = 1, Duration = new Duration(AnimDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(scrimAnim, Scrim);
        Storyboard.SetTargetProperty(scrimAnim, "Opacity");
        sb.Children.Add(scrimAnim);

        var slideAnim = new DoubleAnimation
        {
            From = SheetTransform.X, To = 0, Duration = new Duration(AnimDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(slideAnim, SheetTransform);
        Storyboard.SetTargetProperty(slideAnim, "X");
        sb.Children.Add(slideAnim);

        _activeStoryboard = sb;
        sb.Completed += (_, _) =>
        {
            if (generation == _animationGeneration)
                _activeStoryboard = null;
        };
        sb.Begin();
    }

    private void Close()
    {
        var generation = ++_animationGeneration;
        _activeStoryboard?.Stop();
        var sb = new Storyboard();

        var scrimAnim = new DoubleAnimation
        {
            From = Scrim.Opacity, To = 0, Duration = new Duration(AnimDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(scrimAnim, Scrim);
        Storyboard.SetTargetProperty(scrimAnim, "Opacity");
        sb.Children.Add(scrimAnim);

        var slideAnim = new DoubleAnimation
        {
            From = SheetTransform.X, To = SheetWidth, Duration = new Duration(AnimDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(slideAnim, SheetTransform);
        Storyboard.SetTargetProperty(slideAnim, "X");
        sb.Children.Add(slideAnim);

        sb.Completed += (_, _) =>
        {
            if (generation != _animationGeneration || IsOpen)
                return;

            _activeStoryboard = null;
            this.Visibility = Visibility.Collapsed;
            try { Closed?.Invoke(); } catch { }
        };
        _activeStoryboard = sb;
        sb.Begin();
    }

    private void Scrim_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        IsOpen = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        IsOpen = false;
    }
}
