using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Services;
using Windows.System;

namespace SiloPlayer.Controls;

public sealed partial class SectionRow : UserControl
{
    private readonly UICustomizationService _uiCustomizationService;
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

    // B45: hover/focus-reveal arrows. The WebUI exposes carousel arrows on
    // pointer hover and focus-visible, and each arrow is independently enabled
    // based on scroll position (can-scroll-prev / next).
    private bool _isHovered;
    private bool _hasKeyboardFocus;
    private bool _canScrollPrev;
    private bool _canScrollNext;
    private bool _isPinned;
    private int? _libraryId;
    private bool _useTitleViewLink;
    private double _posterWidth = 178;
    private double _landscapeWidth = 315;
    private uint? _dragPointerId;
    private double _dragStartX;
    private double _dragStartOffset;
    private bool _isDragging;
    private System.Collections.ObjectModel.ObservableCollection<MediaItem>? _observedItems;
    private string? _lastRenderedSectionId;
    private string? _lastRenderedTemplateKey;
    private const double DragThreshold = 7;

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
            UpdateNavigationPresentation();
            UpdateArrowsOpacity();
        }
    }

    /// <summary>
    /// Uses the current WebUI recommendation-section treatment: the title is
    /// the navigation target and carries a small "View" suffix.
    /// </summary>
    public bool UseTitleViewLink
    {
        get => _useTitleViewLink;
        set
        {
            if (_useTitleViewLink == value) return;
            _useTitleViewLink = value;
            UpdateNavigationPresentation();
        }
    }

    public Action<HomeSectionWithItems>? OnRetry { get; set; }

    public SectionRow()
    {
        this.InitializeComponent();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        UpdateThemeFadeColors();
        Loaded += (_, _) =>
        {
            ObserveItems(Section?.Items);
            _uiCustomizationService.Changed += UICustomization_Changed;
            ApplyResponsiveCardWidths(ActualWidth);
        };
        Unloaded += (_, _) =>
        {
            ObserveItems(null);
            _uiCustomizationService.Changed -= UICustomization_Changed;
        };
    }

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => ApplyResponsiveCardWidths(ActualWidth));

    private static void OnSectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SectionRow row) return;

        if (e.NewValue is HomeSectionWithItems section)
        {
            row.ObserveItems(section.Items);
            row.UpdateSection(section);
        }
        else
        {
            row.ObserveItems(null);
        }
    }

    private void ObserveItems(System.Collections.ObjectModel.ObservableCollection<MediaItem>? items)
    {
        if (ReferenceEquals(_observedItems, items)) return;
        if (_observedItems != null)
            _observedItems.CollectionChanged -= OnSectionItemsChanged;
        _observedItems = items;
        if (_observedItems != null)
            _observedItems.CollectionChanged += OnSectionItemsChanged;
    }

    private void OnSectionItemsChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (Section == null) return;
        // A removal can change Continue Watching from a mixed episode row to
        // an all-cover row (or vice versa), so recompute both visibility and
        // the WebUI card variant instead of only hiding an emptied row.
        DispatcherQueue.TryEnqueue(() => UpdateSection(Section));
    }

    private void UpdateSection(HomeSectionWithItems section)
    {
        var previousOffset = CardsScrollViewer.HorizontalOffset;
        var isSameSection = string.Equals(_lastRenderedSectionId, section.Id, StringComparison.Ordinal);
        SectionTitle.Text = section.Title;
        TitleLinkTitle.Text = section.Title;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            TitleLinkBtn,
            $"View {section.Title}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            ExploreAllBtn,
            $"Explore all {section.Title}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            CardsScrollViewer,
            $"{section.Title} carousel");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            ScrollLeftBtn,
            $"Previous items in {section.Title}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            ScrollRightBtn,
            $"Next items in {section.Title}");

        UpdateNavigationPresentation();
        UpdatePinAvailability();
        SyncPinState();

        // Current WebUI keeps episode-based Continue Watching / Next Up rows
        // wide, but switches Continue Watching to upright covers when every
        // item is a movie, audiobook, or ebook. A pure audiobook row remains
        // square like its web counterpart.
        var isContinueWatching = section.SectionType == "continue_watching";
        if (section.SectionType is "continue_watching" or "next_up")
        {
            // The row type is authoritative. Some section-item payloads omit
            // item_source even though the full /home/sections response carries
            // it; normalize both API paths so card menus and Next Up behavior
            // do not silently lose their surface-specific actions.
            foreach (var item in section.Items)
                item.ItemSource = section.SectionType;
        }
        var allCoverMedia = isContinueWatching && section.Items.Count > 0 &&
            section.Items.All(item => item.Type is "movie" or "audiobook" or "ebook");
        var useLandscape = section.SectionType == "next_up" ||
            (isContinueWatching && !allCoverMedia);
        string templateKey = section.SectionType == "continue_listening"
            ? "AudiobookCardTemplate"
            : allCoverMedia ? "ContinuePosterCardTemplate"
            : useLandscape ? "LandscapeCardTemplate"
            : "PosterCardTemplate";
        var templateChanged = !string.Equals(_lastRenderedTemplateKey, templateKey, StringComparison.Ordinal);
        CardsRepeater.ItemTemplate = (DataTemplate)this.Resources[templateKey];
        CardsRepeater.ItemsSource = section.Items;
        _lastRenderedSectionId = section.Id;
        _lastRenderedTemplateKey = templateKey;

        // F6: show a skeleton row while items are still loading (empty list
        // during F12 per-section fetch). Replaced by the real cards as soon
        // as Items gets populated and UpdateSection is called again.
        bool hasItems = section.Items != null && section.Items.Count > 0;
        var compactStatusHeader = section.LoadFailed || (!hasItems && !section.LoadCompleted);
        SectionTitle.FontSize = compactStatusHeader ? 14 : 20;
        TitleLinkTitle.FontSize = compactStatusHeader ? 14 : 20;
        SectionHeader.Margin = new Thickness(
            SectionHeader.Margin.Left,
            SectionHeader.Margin.Top,
            SectionHeader.Margin.Right,
            compactStatusHeader ? 12 : 20);
        UpdateSectionVisibility(section);
        if (section.LoadFailed)
        {
            SkeletonPanel.Visibility = Visibility.Collapsed;
            CardsScrollViewer.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
        }
        else if (hasItems || section.LoadCompleted)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            SkeletonPanel.Visibility = Visibility.Collapsed;
            SkeletonPanel.Children.Clear();
            CardsScrollViewer.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            BuildSkeletonRow();
            SkeletonPanel.Visibility = Visibility.Visible;
            CardsScrollViewer.Visibility = Visibility.Collapsed;
        }

        // The current WebUI keeps carousel state mounted while section items
        // update. Preserve horizontal position for the same section/template
        // so background refreshes, progress updates, and item dismissals don't
        // snap the user back to the first card. New sections or card-shape
        // changes still reset to the beginning.
        RestoreScrollPositionAfterRebind(isSameSection && !templateChanged, previousOffset);
        UpdateScrollBounds();
    }

    private void RestoreScrollPositionAfterRebind(bool preserveOffset, double previousOffset)
    {
        if (!preserveOffset)
        {
            CardsScrollViewer.ChangeView(0, null, null, disableAnimation: true);
            return;
        }

        var targetOffset = Math.Max(0, previousOffset);
        if (targetOffset <= 1)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            var clamped = Math.Clamp(targetOffset, 0, CardsScrollViewer.ScrollableWidth);
            CardsScrollViewer.ChangeView(clamped, null, null, disableAnimation: true);
            UpdateScrollBounds();
        });
    }

    private void UpdateSectionVisibility(HomeSectionWithItems section)
    {
        var completedEmpty = section.LoadCompleted
            && !section.LoadFailed
            && section.Items.Count == 0;
        Visibility = completedEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateNavigationPresentation()
    {
        if (ExploreAllBtn == null || TitleLinkBtn == null || SectionTitle == null) return;
        var hasTarget = OnViewAll != null;
        TitleLinkBtn.Visibility = hasTarget && UseTitleViewLink
            ? Visibility.Visible : Visibility.Collapsed;
        SectionTitle.Visibility = hasTarget && UseTitleViewLink
            ? Visibility.Collapsed : Visibility.Visible;
        ExploreAllBtn.Visibility = hasTarget && !UseTitleViewLink
            ? Visibility.Visible : Visibility.Collapsed;
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
            var skeleton = new SkeletonPoster();
            skeleton.SetResponsiveWidth(GetSkeletonWidth(ActualWidth));
            SkeletonPanel.Children.Add(skeleton);
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

    private void CardsScrollViewer_GotFocus(object sender, RoutedEventArgs e)
    {
        _hasKeyboardFocus = true;
        UpdateArrowsOpacity();
    }

    private void CardsScrollViewer_LostFocus(object sender, RoutedEventArgs e)
    {
        _hasKeyboardFocus = false;
        UpdateArrowsOpacity();
    }

    private void CardsScrollViewer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                ScrollByPage(-1);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                ScrollByPage(1);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                CardsScrollViewer.ChangeView(0, null, null);
                e.Handled = true;
                break;
            case VirtualKey.End:
                CardsScrollViewer.ChangeView(CardsScrollViewer.ScrollableWidth, null, null);
                e.Handled = true;
                break;
        }
    }

    private void CardsScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CardsScrollViewer);
        if (!point.Properties.IsLeftButtonPressed && !point.IsInContact)
            return;

        _dragPointerId = e.Pointer.PointerId;
        _dragStartX = point.Position.X;
        _dragStartOffset = CardsScrollViewer.HorizontalOffset;
        _isDragging = false;
    }

    private void CardsScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId)
            return;

        var point = e.GetCurrentPoint(CardsScrollViewer);
        var delta = point.Position.X - _dragStartX;
        if (!_isDragging)
        {
            if (Math.Abs(delta) < DragThreshold)
                return;

            _isDragging = CardsScrollViewer.CapturePointer(e.Pointer);
            if (!_isDragging)
            {
                ResetDragState();
                return;
            }
        }

        var target = Math.Clamp(
            _dragStartOffset - delta,
            0,
            CardsScrollViewer.ScrollableWidth);
        CardsScrollViewer.ChangeView(target, null, null, disableAnimation: true);
        e.Handled = true;
    }

    private void CardsScrollViewer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId)
            return;

        var handled = _isDragging;
        if (_isDragging)
            CardsScrollViewer.ReleasePointerCapture(e.Pointer);
        ResetDragState();
        e.Handled = handled;
    }

    private void CardsScrollViewer_PointerCanceled(object sender, PointerRoutedEventArgs e)
        => ResetDragState();

    private void CardsScrollViewer_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        => ResetDragState();

    private void ResetDragState()
    {
        _dragPointerId = null;
        _isDragging = false;
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
        // WebUI reveals carousel edge arrows on hover/focus-visible. The
        // optional Explore all header action is controlled separately by the
        // page that owns the section.
        bool show = (_isHovered || _hasKeyboardFocus) && (_canScrollPrev || _canScrollNext);
        double target = show ? 1.0 : 0.0;
        ArrowsPanel.IsHitTestVisible = show;
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
        var label = _isPinned ? "Unpin from sidebar" : "Pin to sidebar";
        ToolTipService.SetToolTip(PinSectionBtn, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PinSectionBtn, label);
    }

    // ─── Explore all ────────────────────────────────────────────────────

    private void ExploreAll_Click(object sender, RoutedEventArgs e)
    {
        OnViewAll?.Invoke();
    }

    private void TitleLink_Click(object sender, RoutedEventArgs e)
    {
        OnViewAll?.Invoke();
    }

    private void SectionRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThemeFadeColors();
        ApplyResponsiveCardWidths(e.NewSize.Width);
    }

    private void ApplyResponsiveCardWidths(double width)
    {
        if (width <= 0) return;

        var gutter = width < 640 ? 16d
            : width < 1024 ? 24d
            : width < 1280 ? 40d
            : 48d;
        var posterWidth = _uiCustomizationService.CardPresentation.PosterSize switch
        {
            "compact" => width < 640 ? 120d : width < 1024 ? 140d : 160d,
            "large" => width < 640 ? 170d : width < 1024 ? 195d : 220d,
            _ => width < 640 ? 140d : width < 1024 ? 160d : 185d,
        };
        var landscapeWidth = width < 640 ? 260d : 315d;

        var edgeMargin = new Thickness(gutter, 0, gutter, 0);
        SectionHeader.Margin = new Thickness(gutter, 0, gutter, 20);
        CardsRepeater.Margin = edgeMargin;
        SkeletonPanel.Margin = edgeMargin;
        ErrorPanel.Margin = edgeMargin;
        CardsLayout.Spacing = width < 1024 ? 16 : 20;

        foreach (var child in SkeletonPanel.Children.OfType<SkeletonPoster>())
            child.SetResponsiveWidth(GetSkeletonWidth(width));

        var posterChanged = Math.Abs(_posterWidth - posterWidth) >= 0.1;
        var landscapeChanged = Math.Abs(_landscapeWidth - landscapeWidth) >= 0.1;
        if (!posterChanged && !landscapeChanged) return;
        _posterWidth = posterWidth;
        _landscapeWidth = landscapeWidth;
        if (Section == null) return;
        for (var i = 0; i < Section.Items.Count; i++)
        {
            if (CardsRepeater.TryGetElement(i) is PosterCard card)
                card.SetCatalogGridLayout(_posterWidth);
            else if (CardsRepeater.TryGetElement(i) is LandscapeCard landscape)
                landscape.SetCardWidth(landscape.UsePosterAspect ? _posterWidth : _landscapeWidth);
        }
    }

    private void CardsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is PosterCard card)
            card.SetCatalogGridLayout(_posterWidth);
        else if (args.Element is LandscapeCard landscape)
            landscape.SetCardWidth(landscape.UsePosterAspect ? _posterWidth : _landscapeWidth);
    }

    private static double GetSkeletonWidth(double width) => width < 640 ? 130d
        : width < 1024 ? 150d
        : 178d;

    private void UpdateThemeFadeColors()
    {
        if (Application.Current.Resources["AppBackgroundColor"] is not Windows.UI.Color background)
            return;
        var solid = Microsoft.UI.ColorHelper.FromArgb(255, background.R, background.G, background.B);
        var clear = Microsoft.UI.ColorHelper.FromArgb(0, background.R, background.G, background.B);
        LeftFadeSolid.Color = RightFadeSolid.Color = solid;
        LeftFadeClear.Color = RightFadeClear.Color = clear;
    }

    private void RetrySection_Click(object sender, RoutedEventArgs e)
    {
        if (Section != null)
            OnRetry?.Invoke(Section);
    }


    // ─── Scroll actions ─────────────────────────────────────────────────

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        ScrollByPage(-1);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        ScrollByPage(1);
    }

    private void ScrollByPage(int direction)
    {
        var page = Math.Max(240, CardsScrollViewer.ViewportWidth * 0.82);
        var target = Math.Clamp(
            CardsScrollViewer.HorizontalOffset + (page * direction),
            0,
            CardsScrollViewer.ScrollableWidth);
        CardsScrollViewer.ChangeView(target, null, null);
    }
}
