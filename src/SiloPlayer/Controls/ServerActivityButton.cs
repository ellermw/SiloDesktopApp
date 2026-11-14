using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Views.Admin;
using Windows.UI;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Controls;

/// <summary>
/// The top-right Server Activity indicator (WebUI <c>ServerActivity.tsx</c>).
/// Every instance renders the one app-lifetime <see cref="ServerActivityStore"/>;
/// none opens its own subscription. The host decides context:
/// <see cref="Eligible"/> (acting-admin context where Layout/AdminLayout would
/// mount it; drives reads), <see cref="Suppressed"/> (temporarily covered, e.g.
/// player overlay; hides without releasing data) and <see cref="HideWhenEmpty"/>
/// (main shell: hidden at zero unless the popover is open; admin shell: always shown).
/// </summary>
public sealed class ServerActivityButton : UserControl
{
    private readonly ServerActivityStore? _store;
    private readonly NavigationService? _navigation;
    private readonly Button _trigger;
    private readonly Viewbox _idleIcon;
    private readonly Viewbox _activeIcon;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private readonly Border _problemDot;
    private readonly Flyout _flyout;
    private readonly StackPanel _popover = new();
    private readonly Storyboard _pulse = new();
    private IDisposable? _reads;
    private ServerActivitySnapshot _snapshot = ServerActivitySnapshot.Empty;
    private ServerActivityView _view;
    private bool _eligible;
    private bool _suppressed;
    private bool _hideWhenEmpty;
    private bool _open;
    private bool _loaded;

    public ServerActivityButton()
    {
        try
        {
            _store = App.Services.GetService<ServerActivityStore>();
            _navigation = App.Services.GetService<NavigationService>();
        }
        catch (InvalidOperationException) { }
        _view = ServerActivityPresentation.Build(_snapshot);

        _idleIcon = A.Icon("activity", 18, A.Muted);
        _activeIcon = A.Icon("activity", 18, A.Primary);
        _activeIcon.Visibility = Visibility.Collapsed;
        _badgeText = new TextBlock
        {
            FontSize = 10, FontWeight = FontWeights.Bold, Foreground = A.Solid(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _badge = new Border
        {
            Height = 18, MinWidth = 18, Padding = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(9),
            Background = A.Solid(A.Red500), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -2, -2, 0), Child = _badgeText, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        _problemDot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = A.Solid(A.Red500),
            BorderBrush = A.Brush("AppBackgroundBrush"), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -2, -2), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        var face = new Grid { Children = { _idleIcon, _activeIcon } };
        _trigger = new Button
        {
            Width = 36, Height = 36, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Content = face,
        };
        if (Application.Current.Resources.TryGetValue("GhostButtonStyle", out var ghost) && ghost is Style style)
        {
            _trigger.Style = style;
            _trigger.Padding = new Thickness(0);
            _trigger.MinHeight = 0;
            _trigger.CornerRadius = new CornerRadius(12);
        }
        // The activity glyph owns its colour, so the generic ghost template's
        // accent foreground inversion would leave it pale on a white hover.
        // Match the WebUI's subtle foreground tint without obscuring the glyph.
        var hover = A.Solid(A.Foreground, 0.05).Color;
        _trigger.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load($@"
            <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                             xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
                <Border x:Name='Root' Background='{{TemplateBinding Background}}' CornerRadius='{{TemplateBinding CornerRadius}}'>
                    <ContentPresenter Content='{{TemplateBinding Content}}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name='CommonStates'>
                        <VisualState x:Name='Normal'/>
                        <VisualState x:Name='PointerOver'><VisualState.Setters><Setter Target='Root.Background' Value='{hover}'/></VisualState.Setters></VisualState>
                        <VisualState x:Name='Pressed'><VisualState.Setters><Setter Target='Root.Opacity' Value='0.7'/></VisualState.Setters></VisualState>
                        <VisualState x:Name='Disabled'><VisualState.Setters><Setter Target='Root.Opacity' Value='0.5'/></VisualState.Setters></VisualState>
                    </VisualStateGroup></VisualStateManager.VisualStateGroups>
                </Border>
            </ControlTemplate>");
        // No tooltip by design; the accessible name carries the count.
        ToolTipService.SetToolTip(_trigger, null);

        _flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            Content = _popover,
            FlyoutPresenterStyle = PresenterStyle(),
        };
        _flyout.Opening += (_, _) => { _open = true; RenderPopover(); };
        _flyout.Closed += (_, _) => { _open = false; ApplyVisibility(); };
        _trigger.Flyout = _flyout;

        var pulse = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, EnableDependentAnimation = false };
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1)), Value = 0.5, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)), Value = 1, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(pulse, _badge);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        _pulse.Children.Add(pulse);

        Content = new Grid { Children = { _trigger, _badge, _problemDot } };
        Visibility = Visibility.Collapsed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Main shell: hidden while idle unless the popover is open. Admin shell: false.</summary>
    public bool HideWhenEmpty
    {
        get => _hideWhenEmpty;
        set { _hideWhenEmpty = value; ApplyVisibility(); }
    }

    /// <summary>36 (desktop) or 44 (admin phone header and main narrow header use their own size).</summary>
    public double ButtonSize
    {
        get => _trigger.Width;
        set { _trigger.Width = value; _trigger.Height = value; }
    }

    /// <summary>The host's mount rule (acting admin, critical content, shell context). Drives reads and visibility.</summary>
    public bool Eligible
    {
        get => _eligible;
        set
        {
            if (_eligible == value) return;
            _eligible = value;
            SynchronizeReads();
            ApplyVisibility();
        }
    }

    /// <summary>Covered by a full-window player/overlay: hidden, data kept.</summary>
    public bool Suppressed
    {
        get => _suppressed;
        set
        {
            if (_suppressed == value) return;
            _suppressed = value;
            if (value) ClosePopover();
            ApplyVisibility();
        }
    }

    /// <summary>True while the trigger is shown (tests and hosts).</summary>
    public bool IsTriggerShown => Visibility == Visibility.Visible;

    public ServerActivityView CurrentView => _view;

    /// <summary>The popover is keyed by route: any navigation closes it.</summary>
    public void ClosePopover()
    {
        if (_open) _flyout.Hide();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        if (_store != null)
        {
            _store.Changed += OnStoreChanged;
            Render(_store.Current);
        }
        if (_navigation != null) _navigation.Navigated += OnNavigated;
        AdminShellPage.RouteChanged += OnAdminRouteChanged;
        SynchronizeReads();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _loaded = false;
        if (_store != null) _store.Changed -= OnStoreChanged;
        if (_navigation != null) _navigation.Navigated -= OnNavigated;
        AdminShellPage.RouteChanged -= OnAdminRouteChanged;
        _reads?.Dispose();
        _reads = null;
        _pulse.Stop();
    }

    private void OnNavigated(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e) => ClosePopover();
    private void OnAdminRouteChanged(AdminRoute route) => ClosePopover();

    private void SynchronizeReads()
    {
        var want = _loaded && _eligible && _store != null;
        if (want && _reads == null) _reads = _store!.AcquireReads();
        else if (!want && _reads != null)
        {
            _reads.Dispose();
            _reads = null;
        }
    }

    private void OnStoreChanged(ServerActivitySnapshot snapshot) =>
        DispatcherQueue?.TryEnqueue(() => { if (_loaded) Render(_store?.Current ?? ServerActivitySnapshot.Empty); });

    private void Render(ServerActivitySnapshot snapshot)
    {
        // A snapshot from a superseded epoch never overrides a newer one.
        if (snapshot.Epoch < _snapshot.Epoch ||
            (snapshot.Epoch == _snapshot.Epoch && snapshot.Version < _snapshot.Version)) return;
        _snapshot = snapshot;
        _view = ServerActivityPresentation.Build(snapshot);
        var active = _view.TotalActive > 0;
        _idleIcon.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        _activeIcon.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _badgeText.Text = _view.BadgeText;
        _badge.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active) _pulse.Begin(); else _pulse.Stop();
        _problemDot.Visibility = snapshot.IsActingAdmin && snapshot.ConnectionProblem ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_trigger, _view.AccessibleName);
        if (!snapshot.IsActingAdmin) ClosePopover();
        if (_open) RenderPopover();
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        var show = _eligible && !_suppressed && _snapshot.IsActingAdmin &&
                   (!_hideWhenEmpty || _view.TotalActive > 0 || _open);
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Style PresenterStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(WidthProperty, 320d));
        style.Setters.Add(new Setter(MaxWidthProperty, 320d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(16)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, A.Solid(A.Surface, 0.95)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, A.Solid(A.Border, 0.72)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        return style;
    }

    // ── Popover ──────────────────────────────────────────────

    private void RenderPopover()
    {
        _popover.Children.Clear();
        var view = _view;

        var header = new Grid { Padding = new Thickness(16, 12, 16, 12) };
        header.Children.Add(A.Text(AdminText.T("web.admin.common.serverActivity.title"), 13, A.Foreground, FontWeights.Bold));
        if (view.ConnectionText is { } connection)
        {
            var status = A.Text(connection, 10, A.Warning, FontWeights.Medium);
            status.HorizontalAlignment = HorizontalAlignment.Right;
            header.Children.Add(status);
        }
        _popover.Children.Add(new Border
        {
            BorderBrush = A.Solid(A.Border, 0.5), BorderThickness = new Thickness(0, 0, 0, 1), Child = header,
        });

        var body = new StackPanel();
        if (view.ShowEmptyState)
        {
            var empty = A.Text(AdminText.T("web.admin.common.serverActivity.empty"), 14, A.Muted);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(new Border { Padding = new Thickness(16, 32, 16, 32), Child = empty });
        }
        else
        {
            var lastIsScans = view.Preparations == null;
            body.Children.Add(Section(AdminText.T("web.admin.common.serverActivity.streams"), view.StreamCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                view.StreamCount > 0, StreamsContent(view), OpenActivity, last: false));
            body.Children.Add(Section(AdminText.T("web.admin.common.serverActivity.tasks"), view.Tasks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                view.Tasks.Count > 0, TasksContent(view), null, last: false));
            body.Children.Add(Section(AdminText.T("web.admin.common.serverActivity.scans"), view.ScanChipText,
                view.ScanCount > 0, ScansContent(view), null, last: lastIsScans));
            if (view.Preparations is { } preparations)
                body.Children.Add(Section(AdminText.T("web.admin.common.serverActivity.preparing"),
                    ServerActivityPresentation.FormatBadgeCount(preparations.ActiveCount), preparations.ActiveCount > 0,
                    PreparationsContent(preparations), null, last: true));
        }
        _popover.Children.Add(new ScrollViewer
        {
            MaxHeight = 400, Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
    }

    private void OpenActivity()
    {
        ClosePopover();
        AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Activity));
    }

    /// <summary>
    /// Section frame. "View all" navigates only for in-scope Activity; the Tasks,
    /// Scans and Preparing destinations are outside the Monitor scope, so they
    /// render as the same text without hover or pointer (recorded deviation).
    /// </summary>
    private static Border Section(string title, string countText, bool showCount, UIElement content, Action? viewAll, bool last)
    {
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(A.Eyebrow(title, A.Muted));
        if (showCount)
            titleRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), Background = A.Solid(A.Primary, 0.10), Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock { Text = countText, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = A.Solid(A.Primary) },
            });

        var viewAllContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        viewAllContent.Children.Add(A.Text(AdminText.T("web.admin.common.serverActivity.viewAll"), 10, A.Muted, FontWeights.Medium));
        viewAllContent.Children.Add(A.Icon("chevron-right", 12, A.Muted));
        UIElement viewAllElement = viewAllContent;
        if (viewAll != null)
        {
            var link = A.Plain(viewAllContent, AdminText.T("web.admin.common.serverActivity.viewAll") + " " + title);
            link.Click += (_, _) => viewAll();
            viewAllElement = link;
        }

        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.Children.Add(titleRow);
        Grid.SetColumn((FrameworkElement)viewAllElement, 1);
        headerRow.Children.Add(viewAllElement);

        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = A.Solid(A.Border, 0.40),
            BorderThickness = last ? new Thickness(0) : new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Children = { headerRow, content } },
        };
    }

    private static TextBlock EmptyRow(string text)
    {
        var row = A.Text(text, 11, A.Muted);
        row.Margin = new Thickness(0, 4, 0, 4);
        return row;
    }

    private static UIElement StreamsContent(ServerActivityView view)
    {
        if (view.StreamCount == 0) return EmptyRow(AdminText.T("web.admin.common.serverActivity.noStreams"));
        var list = new StackPanel { Spacing = 6 };
        foreach (var stream in view.Streams)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = A.Solid(A.ToneColor(stream.Meta.Tone)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(A.Text(stream.Text, 12, A.Foreground, FontWeights.Medium));
            list.Children.Add(row);
        }
        return list;
    }

    private static UIElement Progress(double percent) => new Grid
    {
        Height = 6,
        CornerRadius = new CornerRadius(3),
        Background = A.Solid(A.Muted, 0.20),
        Children =
        {
            new Border
            {
                CornerRadius = new CornerRadius(3), Background = A.Solid(A.Primary), HorizontalAlignment = HorizontalAlignment.Left,
                Width = double.NaN,
                Tag = percent,
            }.WithProportionalWidth(percent),
        },
    };

    private static UIElement Running(string label)
    {
        var running = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        running.Children.Add(A.Spinner(12, A.Primary));
        running.Children.Add(A.Text(label, 10, A.Primary, FontWeights.SemiBold));
        return running;
    }

    private static Grid TitleRow(string title, UIElement trailing)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(A.Text(title, 12, A.Foreground, FontWeights.Medium));
        if (trailing is FrameworkElement element) element.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn((FrameworkElement)trailing, 1);
        grid.Children.Add(trailing);
        return grid;
    }

    private static UIElement TasksContent(ServerActivityView view)
    {
        if (view.Tasks.Count == 0) return EmptyRow(AdminText.T("web.admin.common.serverActivity.noTasks"));
        var list = new StackPanel { Spacing = 8 };
        foreach (var task in view.Tasks)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(TitleRow(task.Name, task.Percent is null
                ? Running(AdminText.T("web.admin.common.serverActivity.running"))
                : A.Text(task.PercentText!, 10, A.Muted, FontWeights.SemiBold)));
            if (task.Percent is { } percent) row.Children.Add(Progress(percent));
            list.Children.Add(row);
        }
        return list;
    }

    private static UIElement ScansContent(ServerActivityView view)
    {
        if (view.ScanCount == 0) return EmptyRow(AdminText.T("web.admin.common.serverActivity.noScans"));
        var list = new StackPanel { Spacing = 6 };
        foreach (var scan in view.Scans)
        {
            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = scan.Running ? A.Spinner(14, A.Primary) : A.Icon("scan-line", 14, A.Muted);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            grid.Children.Add(icon);
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(TitleRow(scan.LibraryName, A.Text(scan.StatusText, 10, A.Muted)));
            text.Children.Add(A.Text(scan.DetailText, 10, A.Muted));
            if (scan.ProgressText is { } progress) text.Children.Add(A.Text(progress, 10, A.WithAlpha(A.Muted, 0.8)));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            list.Children.Add(grid);
        }
        if (view.MoreScansText is { } more)
            list.Children.Add(new Border
            {
                BorderBrush = A.Solid(A.Border, 0.35), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0),
                Child = A.Text(more, 11, A.Muted),
            });
        return list;
    }

    private static UIElement PreparationsContent(ServerActivityPreparationsView preparations)
    {
        if (preparations.EmptyText is { } empty) return EmptyRow(empty);
        var list = new StackPanel { Spacing = 10 };
        foreach (var prep in preparations.Running)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(TitleRow(prep.Title, prep.Percent is null
                ? Running(prep.RunningLabel)
                : A.Text(prep.PercentText!, 10, A.Muted, FontWeights.SemiBold)));
            if (prep.Percent is { } percent)
            {
                var bar = Progress(percent);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, prep.ProgressLabel);
                row.Children.Add(bar);
            }
            row.Children.Add(A.Text(prep.DetailText, 10, A.Muted));
            list.Children.Add(row);
        }
        if (preparations.WaitingText is { } waiting)
            list.Children.Add(new Border
            {
                BorderBrush = A.Solid(A.Border, 0.35),
                BorderThickness = preparations.Running.Count > 0 ? new Thickness(0, 1, 0, 0) : new Thickness(0),
                Padding = preparations.Running.Count > 0 ? new Thickness(0, 6, 0, 0) : new Thickness(0),
                Child = IconLine("clock", waiting, A.Muted, FontWeights.Normal),
            });
        if (preparations.PausedText is { } paused) list.Children.Add(IconLine("pause", paused, A.Muted, FontWeights.Normal));
        if (preparations.FailedText is { } failed) list.Children.Add(IconLine("alert-triangle", failed, A.Destructive, FontWeights.Medium));
        return list;
    }

    private static StackPanel IconLine(string icon, string text, Color color, Windows.UI.Text.FontWeight weight)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(A.Icon(icon, 12, color));
        line.Children.Add(A.Text(text, 11, color, weight));
        return line;
    }
}

internal static class AdminProgressExtensions
{
    /// <summary>Sizes a progress fill to <paramref name="percent"/> of its parent track.</summary>
    public static Border WithProportionalWidth(this Border fill, double percent)
    {
        void Size(FrameworkElement parent) => fill.Width = Math.Max(0, parent.ActualWidth * Math.Clamp(percent, 0, 100) / 100);
        fill.Loaded += (_, _) =>
        {
            if (fill.Parent is FrameworkElement parent)
            {
                Size(parent);
                parent.SizeChanged += (_, _) => Size(parent);
            }
        };
        return fill;
    }
}
