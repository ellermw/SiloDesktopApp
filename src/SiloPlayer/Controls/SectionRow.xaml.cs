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
    private bool _isPinned;
    private int? _libraryId;

    private static readonly HashSet<string> BrowseableSectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "collection",
        "custom_filter",
        "genre",
        "random",
        "recently_added",
    };

    /// <summary>The containing library. Home sections intentionally leave this null.</summary>
    public int? LibraryId
    {
        get => _libraryId;
        set
        {
            if (_libraryId == value) return;
            _libraryId = value;
            UpdatePinAvailability();
            SyncPinState();
        }
    }

    public static bool IsBrowseSupported(string? sectionType) =>
        !string.IsNullOrWhiteSpace(sectionType) && BrowseableSectionTypes.Contains(sectionType);

    /// <summary>
    /// Optional callback invoked when the "Explore all" button is clicked.
    /// When set, the button becomes visible in the title row.
    /// </summary>
    private Action? _onViewAll;
    public Action? OnViewAll
    {
        get => _onViewAll;
        set
        {
            _onViewAll = value;
            if (ExploreAllBtn != null)
                ExploreAllBtn.Visibility = value != null ? Visibility.Visible : Visibility.Collapsed;
            UpdateArrowsOpacity();
        }
    }

    public Action<HomeSectionWithItems>? OnRetry { get; set; }

    public SectionRow()
    {
        this.InitializeComponent();
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
        UpdatePinAvailability();
        SyncPinState();

        // B44: swap the ItemsRepeater template by section type. Landscape for
        // continue_watching / next_up; poster for everything else.
        bool useLandscape = section.SectionType is "continue_watching" or "next_up";
        string templateKey = section.SectionType == "continue_listening"
            ? "AudiobookCardTemplate"
            : useLandscape ? "LandscapeCardTemplate" : "PosterCardTemplate";
        CardsRepeater.ItemTemplate = (DataTemplate)this.Resources[templateKey];
        CardsRepeater.ItemsSource = section.Items;

        // F6: show a skeleton row while items are still loading (empty list
        // during F12 per-section fetch). Replaced by the real cards as soon
        // as Items gets populated and UpdateSection is called again.
        bool hasItems = section.Items != null && section.Items.Count > 0;
        if (section.LoadFailed)
        {
            SkeletonPanel.Visibility = Visibility.Collapsed;
            CardsScrollViewer.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
        }
        else if (hasItems)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            SkeletonPanel.Visibility = Visibility.Collapsed;
            SkeletonPanel.Children.Clear();
            CardsScrollViewer.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
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
        if (PinSectionBtn.Visibility == Visibility.Visible)
            PinSectionBtn.Opacity = _isPinned || _isHovered ? 1.0 : 0.0;
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

    private void UpdatePinAvailability()
    {
        var canPin = _libraryId is > 0 && IsBrowseSupported(Section?.SectionType);
        PinSectionBtn.Visibility = canPin ? Visibility.Visible : Visibility.Collapsed;
        if (!canPin) _isPinned = false;
        UpdatePinVisual();
    }

    private void SyncPinState()
    {
        if (_libraryId is not > 0 || Section == null || !IsBrowseSupported(Section.SectionType))
            return;
        _isPinned = App.MainWindowInstance is MainWindow window
            && window.IsSidebarPin(_libraryId.Value, "section", Section.Id);
        UpdatePinVisual();
    }

    private async void PinSection_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryId is not > 0 || Section == null) return;
        PinSectionBtn.IsEnabled = false;
        try
        {
            if (App.MainWindowInstance is not MainWindow window) return;
            _isPinned = await window.ToggleSidebarPinAsync(
                _libraryId.Value, "section", Section.Id, Section.Title);
            UpdatePinVisual();
        }
        catch
        {
            // Pinning is a convenience action; keep the carousel usable if a
            // transient settings write fails.
        }
        finally
        {
            PinSectionBtn.IsEnabled = true;
        }
    }

    private void UpdatePinVisual()
    {
        if (PinSectionBtn == null) return;
        PinSectionIcon.Glyph = _isPinned ? "\uE841" : "\uE840";
        PinSectionBtn.Opacity = _isPinned || _isHovered ? 1.0 : 0.0;
        ToolTipService.SetToolTip(PinSectionBtn, _isPinned ? "Unpin from sidebar" : "Pin to sidebar");
    }

    // ─── Explore all ────────────────────────────────────────────────────

    private void ExploreAll_Click(object sender, RoutedEventArgs e)
    {
        OnViewAll?.Invoke();
    }

    private void RetrySection_Click(object sender, RoutedEventArgs e)
    {
        if (Section != null)
            OnRetry?.Invoke(Section);
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
