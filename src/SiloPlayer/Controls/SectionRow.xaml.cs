using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Controls;

public sealed partial class SectionRow : UserControl
{
    public static readonly DependencyProperty SectionProperty =
        DependencyProperty.Register(
            nameof(Section),
            typeof(HomeSectionWithItems),
            typeof(SectionRow),
            new PropertyMetadata(null, OnSectionChanged));

    public HomeSectionWithItems? Section
    {
        get => (HomeSectionWithItems?)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    // B45: hover-reveal arrows. Arrows fade in only when the pointer is over
    // the row AND the content is wider than the viewport. Each arrow is
    // independently enabled based on scroll position (can-scroll-prev / next).
    private bool _isHovered;
    private bool _canScrollPrev;
    private bool _canScrollNext;

    /// <summary>
    /// Optional callback invoked when the "Explore all" button is clicked.
    /// When set, the button becomes visible in the title row.
    /// </summary>
    public Action? OnViewAll { get; set; }

    /// <summary>
    /// Optional callback invoked when the per-section refresh button is
    /// clicked. When set, the parent should re-fetch this section's items
    /// from the server and rebind. Webui parity with retrySection().
    /// </summary>
    public Action<HomeSectionWithItems>? OnRefresh { get; set; }

    public SectionRow()
    {
        this.InitializeComponent();
    }

    private void RefreshSection_Click(object sender, RoutedEventArgs e)
    {
        if (Section == null) return;
        OnRefresh?.Invoke(Section);
    }

    private static void OnSectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SectionRow row && e.NewValue is HomeSectionWithItems section)
        {
            row.UpdateSection(section);
        }
    }

    private void UpdateSection(HomeSectionWithItems section)
    {
        SectionTitle.Text = section.Title;

        // Show the "Explore all" button only when a navigation callback is set.
        ExploreAllBtn.Visibility = OnViewAll != null ? Visibility.Visible : Visibility.Collapsed;

        // B44: swap the ItemsRepeater template by section type. Landscape for
        // continue_watching / next_up; poster for everything else.
        bool useLandscape = section.SectionType is "continue_watching" or "next_up";
        string templateKey = useLandscape ? "LandscapeCardTemplate" : "PosterCardTemplate";
        CardsRepeater.ItemTemplate = (DataTemplate)this.Resources[templateKey];
        CardsRepeater.ItemsSource = section.Items;

        // F6: show a skeleton row while items are still loading (empty list
        // during F12 per-section fetch). Replaced by the real cards as soon
        // as Items gets populated and UpdateSection is called again.
        bool hasItems = section.Items != null && section.Items.Count > 0;
        if (hasItems)
        {
            SkeletonPanel.Visibility = Visibility.Collapsed;
            SkeletonPanel.Children.Clear();
            CardsScrollViewer.Visibility = Visibility.Visible;
        }
        else
        {
            BuildSkeletonRow();
            SkeletonPanel.Visibility = Visibility.Visible;
            CardsScrollViewer.Visibility = Visibility.Collapsed;
        }

        // Reset scroll position on re-bind so the first item is always visible.
        CardsScrollViewer.ChangeView(0, null, null, disableAnimation: true);
        UpdateScrollBounds();
    }

    /// <summary>
    /// Populate the skeleton row with 7 placeholder cards (matching the
    /// webui <c>SectionLoadingRow</c>). Cards are lightweight
    /// <see cref="SkeletonPoster"/> instances pulsing opacity.
    /// </summary>
    private void BuildSkeletonRow()
    {
        if (SkeletonPanel.Children.Count > 0) return;
        for (int i = 0; i < 7; i++)
        {
            SkeletonPanel.Children.Add(new SkeletonPoster());
        }
    }

    // ─── Scroll bounds tracking ─────────────────────────────────────────

    private void CardsScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateScrollBounds();
    }

    private void CardsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateScrollBounds();
    }

    private void UpdateScrollBounds()
    {
        double extent = CardsScrollViewer.ExtentWidth;
        double viewport = CardsScrollViewer.ViewportWidth;
        double offset = CardsScrollViewer.HorizontalOffset;

        _canScrollPrev = offset > 1;
        _canScrollNext = offset < extent - viewport - 1 && extent > viewport + 1;

        ScrollLeftBtn.IsEnabled = _canScrollPrev;
        ScrollRightBtn.IsEnabled = _canScrollNext;
        UpdateArrowsOpacity();

        // Edge fade gradients match the scroll-button enabled state — fade in
        // only when there's more content in that direction.
        LeftFadeGradient.Opacity = _canScrollPrev ? 1.0 : 0.0;
        RightFadeGradient.Opacity = _canScrollNext ? 1.0 : 0.0;
    }

    // ─── Hover-reveal ───────────────────────────────────────────────────

    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = true;
        UpdateArrowsOpacity();
    }

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = false;
        UpdateArrowsOpacity();
    }

    private void UpdateArrowsOpacity()
    {
        // Panel is visible when hovered AND either there's something to scroll
        // or the "Explore all" button is available.
        bool hasExploreAll = OnViewAll != null;
        bool show = _isHovered && (_canScrollPrev || _canScrollNext || hasExploreAll);
        double target = show ? 1.0 : 0.0;
        if (Math.Abs(ArrowsPanel.Opacity - target) < 0.01) return;

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(140)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, ArrowsPanel);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    // ─── Explore all ────────────────────────────────────────────────────

    private void ExploreAll_Click(object sender, RoutedEventArgs e)
    {
        OnViewAll?.Invoke();
    }

    // ─── Scroll actions ─────────────────────────────────────────────────

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        CardsScrollViewer.ChangeView(
            Math.Max(0, CardsScrollViewer.HorizontalOffset - 500), null, null);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        CardsScrollViewer.ChangeView(
            CardsScrollViewer.HorizontalOffset + 500, null, null);
    }
}
