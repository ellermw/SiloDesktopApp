using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.System;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

internal sealed record AdminInsightsTabRegistration(AdminInsightsTab Tab, Func<AdminInsightsPage, IAdminInsightsTabContent> Create);

internal interface IAdminInsightsTabContent
{
    FrameworkElement View { get; }
    void ApplyFilters(AdminInsightsFilterState filters);
    Task RefreshAsync();
}

internal enum AdminInsightsPanelKind { Title, Person }

internal sealed record AdminInsightsPanelRegistration(AdminInsightsPanelKind Kind, Func<AdminInsightsPage, IAdminInsightsPanelContent> Create);

internal interface IAdminInsightsPanelContent
{
    AdminInsightsPanelPresentation ApplyState(AdminInsightsDetailState detail, AdminInsightsFilterState filters);
    Task RefreshAsync();
}

internal sealed record AdminInsightsPanelPresentation(string Title, string Description, FrameworkElement Body, FrameworkElement? Footer = null);

public sealed partial class AdminInsightsPage : Page, IAdminGutterScrollPage
{
    private static readonly TimeSpan MinimumRefreshTime = TimeSpan.FromMilliseconds(600);
    private static readonly string LocalTimezone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)
        ? iana : TimeZoneInfo.Local.Id;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly StackPanel _body = new() { Spacing = 20, Padding = new(0, 0, 0, 24) };
    private readonly ContentControl _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly ContentControl _toolbar = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly ScrollViewer _tabScroll;
    private readonly Dictionary<AdminInsightsTab, Button> _tabs = [];
    private readonly Button _reset;
    private readonly Button _refresh;
    private readonly AdminMonitorAuthority? _authority = App.Services.GetService<AdminMonitorAuthority>();
    private readonly AdminInsightsRefreshGate? _refreshGate = App.Services.GetService<AdminInsightsRefreshGate>();
    private readonly Dictionary<AdminInsightsTab, AdminInsightsTabRegistration> _tabFactories;
    private readonly Dictionary<AdminInsightsPanelKind, AdminInsightsPanelRegistration> _panelFactories;
    private readonly LogsDetailSheet _detailSheet;
    private IAdminInsightsTabContent? _tab;
    private AdminInsightsTab? _mountedTab;
    private AdminInsightsPanelKind? _mountedPanel;
    private IAdminInsightsPanelContent? _panel;
    private bool _active;
    private bool _closingPanel;
    private bool _refreshing;

    public AdminInsightsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _tabFactories = App.Services.GetServices<AdminInsightsTabRegistration>().ToDictionary(item => item.Tab);
        _panelFactories = App.Services.GetServices<AdminInsightsPanelRegistration>().ToDictionary(item => item.Kind);
        _scroll.Content = _body;
        _root.Children.Add(_scroll);
        Content = _root;
        _detailSheet = new LogsDetailSheet(_scroll, 576);
        _detailSheet.Closed += OnDetailClosed;
        _root.Children.Add(_detailSheet);

        var actions = new LogsWrapPanel { Spacing = 8 };
        var history = ActionButton("web.admin.insights.action.history", "history", "AdminInsightsHistory", ghost: true);
        history.Click += (_, _) => AdminShellPage.Open(new AdminRouteRequest(AdminRoute.PlaybackHistory));
        _reset = ActionButton("web.admin.insights.filter.reset", "rotate-ccw", "AdminInsightsReset", ghost: true);
        _reset.Click += (_, _) => ReplaceLocation(AdminInsightsFilters.Reset(Location, Filters.Tab));
        _refresh = ActionButton("web.admin.insights.filter.refresh", "refresh-cw", "AdminInsightsRefresh");
        _refresh.MinWidth = 120;
        _refresh.Click += (_, _) => _ = RefreshAsync();
        actions.Children.Add(history);
        actions.Children.Add(_reset);
        actions.Children.Add(_refresh);
        _body.Children.Add(new LogsPageHeader(AdminInsightsText.T("web.admin.insights.title"), AdminInsightsText.T("web.admin.insights.subtitle"), actions)
        {
            Margin = new(0, 0, 0, 4),
        });
        _tabScroll = BuildTabs();
        _body.Children.Add(new Border
        {
            Child = _tabScroll, BorderThickness = new(0, 0, 0, 1), BorderBrush = A.Solid(A.Border, 0.7),
        });
        _body.Children.Add(_toolbar);
        _body.Children.Add(_content);
        Loaded += (_, _) => { if (_active) { RevealSelectedTab(); RenderDetail(); } };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!IsLoaded) SuspendMonitor(clearVisuals: false); });
    }

    FrameworkElement IAdminGutterScrollPage.ScrollContent => _body;
    public AdminRouteRequest Location { get; private set; } = new(AdminRoute.Insights);
    public AdminInsightsFilterState Filters => AdminInsightsFilterState.Parse(Location, LocalTimezone, Today);
    internal AdminActivityPage? Activity => _content.Content as AdminActivityPage;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Location = e.Parameter as AdminRouteRequest ?? new(AdminRoute.Insights);
        _active = true;
        if (_authority != null) _authority.AvailabilityChanged += OnAuthorityChanged;
        RenderLocation();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SuspendMonitor();
        base.OnNavigatedFrom(e);
    }

    internal void SuspendMonitor(bool clearVisuals = true)
    {
        if (!_active) return;
        _active = false;
        if (_authority != null) _authority.AvailabilityChanged -= OnAuthorityChanged;
        ClearOptions();
        // WinUI owns visual teardown when the window drops the XAML root.
        Activity?.SuspendMonitor(clearVisuals);
        if (clearVisuals) CloseDetailSheet();
        else
        {
            _panel = null;
            _mountedPanel = null;
        }
        _tab = null;
        _mountedTab = null;
    }

    internal void CloseRetiredSheets()
    {
        if (_active) return;
        Activity?.CloseCardDetails(restoreFocus: false);
        CloseDetailSheet(restoreFocus: false);
    }

    internal void ReplaceLocation(AdminRouteRequest request)
    {
        if (!_active || request.Route != AdminRoute.Insights || request.Equals(Location)) return;
        if (!AdminShellPage.TryReplace(this, request)) return;
        Location = request;
        RenderLocation();
    }

    internal void OpenTitle(string contentId) => ReplaceLocation(AdminInsightsFilters.WithTitle(Location, contentId));
    internal void OpenPerson(AdminInsightsPersonScope person) => ReplaceLocation(AdminInsightsFilters.WithPerson(Location, person));

    private void RenderLocation()
    {
        var filters = Filters;
        _reset.Visibility = filters.HasNonDefaultFilters(LocalTimezone) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (tab, button) in _tabs)
        {
            var selected = tab == filters.Tab;
            var underline = (Border)button.Content;
            ((TextBlock)underline.Child).Foreground = A.Solid(selected ? A.Foreground : A.Muted);
            underline.BorderBrush = A.Solid(A.Foreground, selected ? 1 : 0);
            AutomationProperties.SetItemStatus(button, selected ? "selected" : "");
            button.IsTabStop = selected;
        }
        DispatcherQueue.TryEnqueue(RevealSelectedTab);
        RenderToolbar(filters);
        if (filters.Tab.ShowsLibraryFilter() && filters.Tab.ShowsToolbar() && _optionsLease == null)
            _ = ReadOptionsAsync();
        if (_mountedTab != filters.Tab)
        {
            LeaveTab();
            _mountedTab = filters.Tab;
            if (filters.Tab == AdminInsightsTab.Activity)
            {
                var activity = new AdminActivityPage();
                _content.Content = activity;
                activity.HostInInsights(_root, _scroll, RefreshAsync);
            }
            else if (_tabFactories.TryGetValue(filters.Tab, out var factory))
            {
                _tab = factory.Create(this);
                _content.Content = _tab.View;
            }
            else _content.Content = A.Text(AdminInsightsText.T("web.admin.insights.detail.noPlays"), 14, A.Muted, trim: false);
        }
        _tab?.ApplyFilters(filters);
        RenderDetail();
    }

    private void LeaveTab()
    {
        Activity?.DetachFromInsights();
        _content.Content = null;
        _tab = null;
        _mountedTab = null;
    }

    private async Task RefreshAsync()
    {
        if (!_active || _refreshing) return;
        _refreshing = true;
        SetRefreshing(true);
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (Activity is { } activity) await activity.RefreshAsync();
            else
            {
                _refreshGate?.Mark();
                await Task.WhenAll(_tab?.RefreshAsync() ?? Task.CompletedTask, _panel?.RefreshAsync() ?? Task.CompletedTask);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            var remaining = MinimumRefreshTime - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            _refreshing = false;
            SetRefreshing(false);
        }
    }

    private void SetRefreshing(bool refreshing)
    {
        var text = AdminInsightsText.T(refreshing ? "web.admin.insights.filter.refreshing" : "web.admin.insights.filter.refresh");
        _refresh.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { refreshing ? A.Spinner(14, A.Foreground) : A.Icon("refresh-cw", 14, A.Foreground), A.Text(text, 12, A.Foreground, FontWeights.Medium) },
        };
        AutomationProperties.SetName(_refresh, text);
        _refresh.IsEnabled = !refreshing;
    }

    private ScrollViewer BuildTabs()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var tab in AdminInsightsTabs.Ordered)
        {
            var label = tab == AdminInsightsTab.Activity ? AdminText.T(tab.LabelKey()) : AdminInsightsText.T(tab.LabelKey());
            var underline = new Border
            {
                Child = A.Text(label, 14, A.Foreground, FontWeights.Medium),
                BorderThickness = new(0, 0, 0, 2), Padding = new(10, 7, 10, 7),
            };
            var button = A.Plain(underline, label, new Thickness(0));
            AutomationProperties.SetAutomationId(button, "AdminInsightsTab_" + tab.Wire());
            button.Click += (_, _) => ReplaceLocation(AdminInsightsFilters.WithTab(Location, tab));
            button.KeyDown += (_, args) =>
            {
                var index = (int)tab;
                var last = AdminInsightsTabs.Ordered.Count - 1;
                var next = args.Key switch
                {
                    VirtualKey.Left => index == 0 ? last : index - 1,
                    VirtualKey.Right => index == last ? 0 : index + 1,
                    VirtualKey.Home => 0,
                    VirtualKey.End => last,
                    _ => -1,
                };
                if (next < 0) return;
                args.Handled = true;
                var target = AdminInsightsTabs.Ordered[next];
                ReplaceLocation(AdminInsightsFilters.WithTab(Location, target));
                _tabs[target].Focus(FocusState.Keyboard);
            };
            _tabs.Add(tab, button);
            strip.Children.Add(button);
        }
        return new ScrollViewer
        {
            Content = strip, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = false,
        };
    }

    private void RevealSelectedTab()
    {
        var button = _tabs[Filters.Tab];
        if (!_active || !IsLoaded || !button.IsLoaded || _tabScroll.ViewportWidth <= 0) return;
        var x = button.TransformToVisual((UIElement)_tabScroll.Content).TransformPoint(default).X;
        var offset = _tabScroll.HorizontalOffset;
        if (x < offset) _tabScroll.ChangeView(x, null, null, true);
        else if (x + button.ActualWidth > offset + _tabScroll.ViewportWidth)
            _tabScroll.ChangeView(x + button.ActualWidth - _tabScroll.ViewportWidth, null, null, true);
    }

    private void RenderDetail()
    {
        if (!IsLoaded || !_active) return;
        var detail = AdminInsightsFilters.ParseDetail(Location);
        if (!detail.IsOpen) { CloseDetailSheet(); return; }
        var kind = detail.TitleId != null ? AdminInsightsPanelKind.Title : AdminInsightsPanelKind.Person;
        if (kind != _mountedPanel)
        {
            _panel = _panelFactories.TryGetValue(kind, out var factory) ? factory.Create(this) : null;
            _mountedPanel = kind;
        }
        var panel = _panel?.ApplyState(detail, Filters.ForDetail()) ??
            new AdminInsightsPanelPresentation(AdminInsightsText.T(kind == AdminInsightsPanelKind.Title ? "web.admin.insights.tabs.titles" : "web.admin.insights.tabs.people"), "", new ContentPresenter());
        if (_detailSheet.Visibility == Visibility.Visible) _detailSheet.Update(panel.Title, panel.Description, panel.Body, footer: panel.Footer);
        else _detailSheet.Show(panel.Title, panel.Description, panel.Body, footer: panel.Footer);
    }

    private void CloseDetailSheet(bool restoreFocus = true)
    {
        _closingPanel = true;
        try { _detailSheet.Close(restoreFocus); }
        finally { _closingPanel = false; _panel = null; _mountedPanel = null; }
    }

    private void OnDetailClosed()
    {
        _panel = null;
        _mountedPanel = null;
        if (_closingPanel || !_active) return;
        ReplaceLocation(AdminInsightsFilters.CloseForDetail(Location));
    }

    private static Button ActionButton(string key, string icon, string id, bool ghost = false)
    {
        var label = AdminInsightsText.T(key);
        var button = A.Outline(label, icon);
        AutomationProperties.SetName(button, label);
        if (ghost)
        {
            button.Background = A.Solid(A.Surface, 0);
            button.BorderThickness = new(0);
        }
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }
}
