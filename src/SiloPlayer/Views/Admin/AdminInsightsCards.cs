using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.Foundation;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Tailwind's breakpoint grids (<c>grid-cols-1 sm:grid-cols-2 xl:grid-cols-4</c>): the column count follows the window width, as a
/// viewport query does, and children flow row by row. Steps are ascending (minimum width, columns); below the first it is one column.
/// </summary>
internal sealed class AdminInsightsGrid : Grid
{
    internal const double Sm = 640, Xl = 1280;
    private readonly (double MinWidth, int Columns)[] _steps;

    internal AdminInsightsGrid(double gap, params (double MinWidth, int Columns)[] steps)
    {
        _steps = steps;
        ColumnSpacing = gap;
        RowSpacing = gap;
        SizeChanged += (_, _) => Reflow();
        Loaded += (_, _) => Reflow();
    }

    internal int Columns
    {
        get
        {
            var width = XamlRoot?.Size.Width ?? ActualWidth;
            var columns = 1;
            foreach (var step in _steps)
                if (width >= step.MinWidth) columns = step.Columns;
            return columns;
        }
    }

    internal AdminInsightsGrid Add(FrameworkElement child)
    {
        Children.Add(child);
        Reflow();
        return this;
    }

    private void Reflow()
    {
        var columns = Columns;
        var rows = Math.Max(1, (Children.Count + columns - 1) / columns);
        if (ColumnDefinitions.Count != columns || RowDefinitions.Count != rows)
        {
            ColumnDefinitions.Clear();
            RowDefinitions.Clear();
            for (var column = 0; column < columns; column++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var row = 0; row < rows; row++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < Children.Count; index++)
        {
            var child = (FrameworkElement)Children[index];
            SetColumn(child, index % columns);
            SetRow(child, index / columns);
        }
    }
}

/// <summary>
/// A card header: the title and subtitle at the start, the actions at the end, the actions wrapping under the title when both do not fit
/// (the official <c>flex flex-row flex-wrap justify-between gap-2</c>, where each part keeps its content width until it must wrap).
/// </summary>
internal sealed class AdminInsightsCardHeader : Panel
{
    private readonly FrameworkElement _titles;
    private readonly FrameworkElement? _actions;
    private readonly bool _centered;
    private bool _wrapped;
    private double _titlesWidth;

    internal AdminInsightsCardHeader(FrameworkElement titles, FrameworkElement? actions, bool centered)
    {
        _titles = titles;
        _actions = actions;
        _centered = centered;
        Children.Add(titles);
        if (actions != null) Children.Add(actions);
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsFinite(available.Width) ? available.Width : 1000;
        var none = new Size(double.PositiveInfinity, double.PositiveInfinity);
        _titles.Measure(none);
        _actions?.Measure(none);
        var titlesWidth = _titles.DesiredSize.Width;
        var actionsWidth = _actions?.DesiredSize.Width ?? 0;
        _wrapped = actionsWidth > 0 && titlesWidth + 8 + actionsWidth > width + SiloPlayer.Controls.LayoutTolerance.Fit;
        _titlesWidth = _wrapped || actionsWidth == 0 ? Math.Min(width, titlesWidth) : titlesWidth;
        _titles.Measure(new Size(_titlesWidth, double.PositiveInfinity));
        _actions?.Measure(new Size(width, double.PositiveInfinity));
        var titlesHeight = _titles.DesiredSize.Height;
        var actionsHeight = _actions?.DesiredSize.Height ?? 0;
        return new Size(width, _wrapped ? titlesHeight + 8 + actionsHeight : Math.Max(titlesHeight, actionsHeight));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var titlesHeight = _titles.DesiredSize.Height;
        _titles.Arrange(new Rect(0, _centered && !_wrapped ? Math.Max(0, (final.Height - titlesHeight) / 2) : 0, _titlesWidth, titlesHeight));
        if (_actions == null) return final;
        var actionsHeight = _actions.DesiredSize.Height;
        var actionsWidth = Math.Min(final.Width, _actions.DesiredSize.Width);
        _actions.Arrange(_wrapped
            ? new Rect(0, titlesHeight + 8, actionsWidth, actionsHeight)
            : new Rect(final.Width - actionsWidth, _centered ? Math.Max(0, (final.Height - actionsHeight) / 2) : 0, actionsWidth, actionsHeight));
        return final;
    }
}

/// <summary>
/// An Insights card (<c>Card className="surface-panel rounded-2xl border-0"</c>): padding 24, a bold 14px title with a 12px muted
/// subtitle, optional actions at the end of the header, and the content 24px below. A toggle changes what the card shows, so the card
/// rebuilds its actions and body from their builders instead of the whole tab.
/// </summary>
internal sealed class AdminInsightsCard : Grid
{
    private readonly string _title;
    private readonly string? _subtitle;
    private readonly Func<IEnumerable<FrameworkElement?>>? _actions;
    private readonly Func<FrameworkElement> _body;
    private readonly bool _centered;

    internal AdminInsightsCard(string title, string? subtitle, Func<IEnumerable<FrameworkElement?>>? actions, Func<FrameworkElement> body, bool centered = false)
    {
        _title = title;
        _subtitle = subtitle;
        _actions = actions;
        _body = body;
        _centered = centered;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(24);
        Background = A.Solid(A.Surface);
        RowSpacing = 24;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AutomationProperties.SetName(this, title);
        Rebuild();
    }

    /// <summary>The links inside the card (bar-list titles, library rows): a page that keeps a result on screen while a new one loads disables them.</summary>
    internal IEnumerable<Control> Links => AdminInsightsLayout.Links(this);

    internal void Rebuild()
    {
        Children.Clear();
        var titles = new StackPanel { Spacing = 2, Children = { A.Text(_title, 14, A.Foreground, FontWeights.Bold, trim: false) } };
        if (!string.IsNullOrEmpty(_subtitle)) titles.Children.Add(A.Text(_subtitle, 12, A.Muted, trim: false));
        DashboardWrapPanel? actions = null;
        if (_actions != null)
        {
            actions = new DashboardWrapPanel { Spacing = 8, RowSpacing = 8 };
            foreach (var action in _actions())
                if (action != null) { action.VerticalAlignment = VerticalAlignment.Center; actions.Children.Add(action); }
            if (actions.Children.Count == 0) actions = null;
        }
        Children.Add(new AdminInsightsCardHeader(titles, actions, _centered));
        var body = _body();
        Grid.SetRow(body, 1);
        Children.Add(body);
    }
}

/// <summary>Layout and control factories every Insights tab and panel shares.</summary>
internal static class AdminInsightsLayout
{
    /// <summary>The tab's vertical rhythm (<c>space-y-5</c>).</summary>
    internal static StackPanel Stack(params FrameworkElement?[] children)
    {
        var stack = new StackPanel { Spacing = 20 };
        foreach (var child in children) if (child != null) stack.Children.Add(child);
        return stack;
    }

    /// <summary>KPI cards: one column, two from 640px, four from 1280px (<c>gap-3</c>).</summary>
    internal static FrameworkElement KpiGrid(IEnumerable<AdminInsightsKpi> kpis)
    {
        var grid = new AdminInsightsGrid(12, (AdminInsightsGrid.Sm, 2), (AdminInsightsGrid.Xl, 4));
        foreach (var kpi in kpis) grid.Add(Kpi(kpi));
        return grid;
    }

    /// <summary>Three KPI cards: one column, three from 640px (<c>grid-cols-1 gap-3 sm:grid-cols-3</c>, the People tab).</summary>
    internal static FrameworkElement KpiGridOfThree(IEnumerable<AdminInsightsKpi> kpis)
    {
        var grid = new AdminInsightsGrid(12, (AdminInsightsGrid.Sm, 3));
        foreach (var kpi in kpis) grid.Add(Kpi(kpi));
        return grid;
    }

    /// <summary>The title panel's KPI cards: two columns at any width (<c>grid-cols-2 gap-3</c>).</summary>
    internal static FrameworkElement PanelKpiGrid(IEnumerable<AdminInsightsKpi> kpis)
    {
        var grid = new AdminInsightsGrid(12, (0, 2));
        foreach (var kpi in kpis) grid.Add(Kpi(kpi));
        return grid;
    }

    internal static FrameworkElement Kpi(AdminInsightsKpi kpi) => AdminInsightsUi.KpiCard(kpi.Label, kpi.Value,
        kpi.Subtext == null ? null : A.Text(kpi.Subtext, 12, A.Muted, trim: false), kpi.Accent);

    /// <summary>Two cards side by side from 1280px, stacked below (<c>grid-cols-1 gap-5 xl:grid-cols-2</c>).</summary>
    internal static FrameworkElement TwoUp(FrameworkElement left, FrameworkElement right)
        => new AdminInsightsGrid(20, (AdminInsightsGrid.Xl, 2)).Add(left).Add(right);

    /// <summary>The compact pill toggle every card header uses (the dashboard's WidgetViewToggle).</summary>
    internal static FrameworkElement Toggle<T>(IReadOnlyList<(T Value, string Label, string AccessibleName)> options, T selected, Action<T> onChange)
        => DashboardUi.Segmented(options, selected, onChange);

    internal static FrameworkElement Empty(string message, double minHeight = 160) => DashboardChartChrome.EmptyState(message, null, null, DateTimeOffset.UtcNow, minHeight);

    /// <summary>
    /// The link buttons inside a result (<see cref="DashboardBarList.LinkTag"/>), found through the logical tree so it works before the
    /// result is on screen. A card's links belong to its current body.
    /// </summary>
    internal static IEnumerable<Control> Links(UIElement root)
    {
        var found = new List<Control>();
        void Walk(UIElement node)
        {
            if (node is Button { Tag: string tag } button && tag == DashboardBarList.LinkTag) found.Add(button);
            switch (node)
            {
                case Panel panel:
                    foreach (var child in panel.Children) Walk(child);
                    break;
                case Border { Child: { } child }:
                    Walk(child);
                    break;
                case ContentControl { Content: UIElement child }:
                    Walk(child);
                    break;
            }
        }
        Walk(root);
        return found;
    }

    /// <summary>A title or library list with the rows' links wired through <paramref name="open"/>.</summary>
    internal static FrameworkElement RowList(IReadOnlyList<AdminInsightsBarRow> rows, Func<double, string> formatValue, string empty, int seriesIndex,
        Func<AdminInsightsBarRow, Action?>? open = null, double? max = null)
        => DashboardBarList.Create(rows.Select(row => new DashboardBarItem(row.Id, row.Label, row.Value, row.Secondary, open?.Invoke(row))).ToArray(),
            formatValue, empty, max, seriesIndex);

    /// <summary>A card skeleton of the given height (<c>Skeleton className="h-64 rounded-2xl"/>).</summary>
    internal static FrameworkElement CardSkeleton(double height) => DashboardUi.Skeleton(height, 16);

    /// <summary>KPI row skeleton: four <c>h-24</c> cards.</summary>
    internal static FrameworkElement KpiSkeleton(int count = 4)
    {
        var grid = new AdminInsightsGrid(12, (AdminInsightsGrid.Sm, 2), (AdminInsightsGrid.Xl, 4));
        for (var index = 0; index < count; index++) grid.Add(DashboardUi.Skeleton(96, 16));
        return grid;
    }
}

/// <summary>
/// "Add to dashboard" for one card (official <c>AddToDashboardButton</c>): nothing until the page's layout owner has loaded and is able to
/// save, "Add to dashboard" while the widget is not placed, "On dashboard" (a link there) once it is. The page raises one event for any
/// change of the layout, so every button on screen follows it.
/// </summary>
internal sealed class AdminInsightsPinButton : ContentControl
{
    private readonly AdminInsightsPage _page;
    private readonly string _widgetId;
    private readonly string _chartName;
    private AdminInsightsPinState _state = (AdminInsightsPinState)(-1);

    internal AdminInsightsPinButton(AdminInsightsPage page, string widgetId, string chartName)
    {
        _page = page;
        _widgetId = widgetId;
        _chartName = chartName;
        IsTabStop = false;
        Padding = new Thickness(0);
        MinWidth = 0;
        MinHeight = 0;
        VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) =>
        {
            _page.PinsChanged += OnPinsChanged;
            Render();
        };
        Unloaded += (_, _) => _page.PinsChanged -= OnPinsChanged;
        Render();
    }

    internal AdminInsightsPinState State => _state;

    private void OnPinsChanged() => DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        var state = _page.PinState(_widgetId);
        if (state == _state) return;
        _state = state;
        switch (state)
        {
            case AdminInsightsPinState.Add:
                Content = Build(add: true);
                Visibility = Visibility.Visible;
                break;
            case AdminInsightsPinState.OnDashboard:
                Content = Build(add: false);
                Visibility = Visibility.Visible;
                break;
            default:
                Content = null;
                Visibility = Visibility.Collapsed;
                break;
        }
        // The wrapping panels around a button cache the sizes of what they measured; ask each of them to measure again.
        InvalidateMeasure();
        for (var node = VisualTreeHelper.GetParent(this); node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement element) element.InvalidateMeasure();
            if (node is AdminInsightsCard or ScrollViewer) break;
        }
    }

    private Button Build(bool add)
    {
        var brush = A.Solid(A.Muted);
        var text = add ? AdminInsightsText.T("web.admin.insights.addToDashboard.add") : AdminInsightsText.T("web.admin.insights.addToDashboard.onDashboard");
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                A.Icon(add ? "layout-dashboard" : "check", 14, brush),
                new TextBlock { Text = text, FontSize = 12, Foreground = brush, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var button = A.Plain(row, null, new Thickness(8, 0, 8, 0));
        button.Height = 28;
        button.CornerRadius = new CornerRadius(6);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        void Tint(bool hot) => brush.Color = hot ? A.Primary : A.Muted;
        if (add)
        {
            AutomationProperties.SetName(button, AdminInsightsText.T("web.admin.insights.addToDashboard.addLabel", ("chart", _chartName)));
            ToolTipService.SetToolTip(button, AdminInsightsText.T("web.admin.insights.addToDashboard.hint"));
            AutomationProperties.SetAutomationId(button, "AdminInsightsAdd_" + _widgetId);
            button.Click += (_, _) => _page.AddToDashboard(_widgetId, _chartName);
        }
        else
        {
            AutomationProperties.SetName(button, AdminInsightsText.T("web.admin.insights.addToDashboard.onDashboardLabel", ("chart", _chartName)));
            AutomationProperties.SetAutomationId(button, "AdminInsightsOnDashboard_" + _widgetId);
            button.Click += (_, _) => _page.OpenDashboard();
        }
        button.PointerEntered += (_, _) => Tint(true);
        button.PointerExited += (_, _) => Tint(false);
        button.GotFocus += (_, _) => Tint(true);
        button.LostFocus += (_, _) => Tint(false);
        return button;
    }
}
