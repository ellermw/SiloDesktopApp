using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Manage › Libraries › Libraries (official WebUI <c>pages/AdminLibraries.tsx</c> with
/// <c>components/admin/libraries/*</c> at abd85d0c): header with the active scan queue,
/// Scan all, Catalog maintenance and Add library; the sortable table from 1024 px and
/// compact cards below it; row commands; the add/edit editor with its General, Folders,
/// Metadata and Advanced sections; the five diagnostics sections; and older refresh jobs.
/// Pages, sections and editor steps swap in place (no entrance or slide transition); the
/// editor is an immediately shown modal panel, confirmations are modal dialogs.
/// </summary>
public sealed partial class AdminLibrariesPage : Page, IAdminNavigationGuard, IAdminPageSuspension
{
    /// <summary>WebUI <c>useLibrariesTableLayout</c>: Tailwind <c>lg</c>, tables from here up, cards below.</summary>
    internal const double TableBreakpoint = 1024;

    private readonly AdminLibrariesApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly IAdminRealtimeChannel? _realtime;
    private readonly ServerActivityStore? _activity;
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body = new() { Spacing = 24 };
    private readonly TextBlock _loading = Text(T("web.admin.libraries.page.loading"));
    private readonly StackPanel _content = new() { Spacing = 24 };
    private readonly ContentControl _header = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _alerts = new() { Spacing = 8 };
    private readonly ContentControl _list = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _diagnostics = new() { Spacing = 24 };
    private readonly StackPanel _jobControls = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(12) };
    private readonly TextBlock _announcer = new() { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false };
    private readonly LogsActionFeedback _feedback;
    private readonly AdminLeaveGuard _leaveGuard;
    private AdminLibrariesController? _controller;
    private MonitorAuthorityLease? _renderedHeaderOwner;

    private sealed record LibraryUiOwner(AdminLibrariesPage Page, AdminLibrariesController Controller, MonitorAuthorityLease? Lease)
    {
        public bool IsCurrent => ReferenceEquals(Page._controller, Controller) && Controller.IsOwnerCurrent(Lease);
    }

    private LibraryUiOwner RenderOwner(AdminLibrariesSnapshot state)
    {
        var controller = _controller ?? throw new InvalidOperationException("The Libraries page has no owner.");
        return new(this, controller, state.Owner);
    }
    private AdminRouteRequest _route = new(AdminRoute.Libraries);
    private bool _wide = true;
    private bool _phone;
    private string? _legacyNotice;

    /// <summary>The page owner (tests and fixtures).</summary>
    public AdminLibrariesController? Controller => _controller;

    public AdminLibrariesPage() : this(App.Services.GetRequiredService<AdminLibrariesApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>(),
        App.Services.GetService<AdminLibrariesRealtimeSource>()?.Channel, App.Services.GetRequiredService<ServerActivityStore>()) { }

    public AdminLibrariesPage(AdminLibrariesApi api, AdminMonitorAuthority authority, IAdminRealtimeChannel? realtime, ServerActivityStore? activity = null)
    {
        InitializeComponent();
        _api = api; _authority = authority; _realtime = realtime; _activity = activity;
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminLibrariesPage");
        _leaveGuard = new AdminLeaveGuard(() => _controller?.State.Owner, () => _editor?.Session.State.IsDirty == true, DiscardEditor);

        AutomationProperties.SetLiveSetting(_announcer, AutomationLiveSetting.Assertive);
        AutomationProperties.SetAutomationId(_announcer, "LibrariesAnnouncer");
        AutomationProperties.SetAutomationId(_loading, "LibrariesLoading");
        _loading.Margin = new(0, 0, 0, 32);
        _content.Children.Add(_header);
        _content.Children.Add(_alerts);
        _content.Children.Add(_list);
        _content.Children.Add(_diagnostics);
        _content.Children.Add(_jobControls);
        _content.Children.Add(_announcer);
        _body.Children.Add(_loading);
        _body.Children.Add(_content);
        _body.Padding = new(0, 0, 0, 24);
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _root.Children.Add(_scroll);
        _feedback = new LogsActionFeedback(this);
        _root.Children.Add(_feedback);
        Content = _root;
        _content.Visibility = Visibility.Collapsed;

        SizeChanged += (_, _) => ApplyWidth();
        Loaded += (_, _) => Open();
        Unloaded += (_, _) => Close();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not AdminRouteRequest route || route.Route != AdminRoute.Libraries) return;
        _route = route;
        // Autoscan was a tab here (?tab=autoscan&view=…) before it became its own topic (WebUI legacyLibrariesAutoscanTarget).
        if (route.Get("tab") == "autoscan")
        {
            // Redirected once the page is shown; an Autoscan page without a native implementation stays here with a notice.
            _pendingLegacyTarget = LegacyAutoscanTarget(route);
        }
    }

    private string? _pendingLegacyTarget;

    private void FollowLegacyTarget()
    {
        var target = _pendingLegacyTarget;
        _pendingLegacyTarget = null;
        if (target == null) return;
        EnqueueUi(() =>
        {
            if (!IsLoaded) return;
            var opened = AdminShellPage.TryOpenHref(target);
            if (opened is { Kind: AdminRouteResolutionKind.Route }) return;
            _legacyNotice = T("admin.native.libraries.autoscanMoved");
            if (AdminShellPage.TryReplace(this, new AdminRouteRequest(AdminRoute.Libraries))) _route = new AdminRouteRequest(AdminRoute.Libraries);
            if (_controller != null) Render(_controller.State);
        });
    }

    /// <summary>WebUI <c>legacyLibrariesAutoscanTarget</c>: activity, settings (connections/settings) or sources, carrying other parameters except to Settings.</summary>
    internal static string LegacyAutoscanTarget(AdminRouteRequest route)
    {
        var view = route.Get("view");
        var page = view == "activity" ? "/admin/autoscan/activity" : view is "connections" or "settings" ? "/admin/settings/autoscan" : "/admin/autoscan";
        if (page == "/admin/settings/autoscan") return page;
        var rest = (route.Query ?? new Dictionary<string, string>()).Where(pair => pair.Key is not ("tab" or "view"))
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)).ToArray();
        return rest.Length == 0 ? page : page + "?" + string.Join("&", rest);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    private void Open()
    {
        Close();
        var controller = new AdminLibrariesController(_api, _authority, _realtime, activity: _activity);
        _controller = controller;
        controller.Changed += QueueRender;
        controller.Retired += OnRetired;
        controller.Notice += OnNotice;
        controller.DiagnosticsStale += OnDiagnosticsStale;
        ApplyWidth();
        Render(controller.State);
        _ = controller.OpenAsync();
        OpenDiagnostics(controller);
        FollowLegacyTarget();
    }

    private void OnRetired()
    {
        var controller = _controller;
        EnqueueUi(() =>
        {
            if (!IsLoaded || controller == null || !ReferenceEquals(controller, _controller)) return;
            CloseTransientUi();
            CloseDiagnostics();
            Render(controller.State);
            if (_authority.IsAvailable) { _ = controller.OpenAsync(); OpenDiagnostics(controller); }
        });
    }

    /// <summary>The whole Admin area departed: retire reads, polls, dialogs and drafts; never resume from here.</summary>
    public void SuspendAdminPage() => Close();

    private void Close()
    {
        _feedback.Clear();
        CloseTransientUi();
        CloseDiagnostics();
        if (_controller != null)
        {
            _controller.Changed -= QueueRender; _controller.Retired -= OnRetired; _controller.Notice -= OnNotice; _controller.DiagnosticsStale -= OnDiagnosticsStale;
            _controller.Dispose(); _controller = null;
        }
        _renderedListKey = null; _renderedHeaderKey = null; _renderedDiagnosticsLibraries = null;
        _list.Content = null; _header.Content = null; _alerts.Children.Clear(); _jobControls.Children.Clear();
        _loading.Visibility = Visibility.Visible; _content.Visibility = Visibility.Collapsed;
    }

    /// <summary>Closes every dialog, flyout, editor and drag owned by the page (retirement or departure).</summary>
    private void CloseTransientUi()
    {
        CancelDrag();
        CloseEditor(force: true);
        _scanQueueFlyout?.Hide();
        foreach (var flyout in _openMenus.ToArray()) flyout.Hide();
        _openMenus.Clear();
        _dialog?.Hide(); _dialog = null;
    }

    public bool RequiresLeaveConfirmation => _leaveGuard.RequiresConfirmation;

    public Task<bool> CanLeaveAsync() => _leaveGuard.CanLeaveAsync(owner => AdminSettingsUi.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog));

    // Dispatcher callbacks must not escape into WinUI's native message loop.
    private void EnqueueUi(Action render)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            try { render(); }
            catch (Exception ex)
            {
                LocalLog.AppendLine("admin_monitor.txt", $"libraries render failed: {ex.GetType().Name} (0x{ex.HResult:X8})");
            }
        });
    }

    private bool _renderQueued;

    private void QueueRender()
    {
        var controller = _controller;
        if (controller == null) return;
        EnqueueUi(() =>
        {
            if (_renderQueued) return;
            _renderQueued = true;
            // Coalesce bursts (scan events, polls) into one render per dispatcher turn.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _renderQueued = false;
                try
                {
                    if (IsLoaded && ReferenceEquals(controller, _controller)) Render(controller.State);
                }
                catch (Exception ex) { LocalLog.AppendLine("admin_monitor.txt", $"libraries render failed: {ex.GetType().Name} (0x{ex.HResult:X8})"); }
            });
        });
    }

    private void OnNotice(string message, bool error)
    {
        var controller = _controller;
        EnqueueUi(() =>
        {
            if (IsLoaded && ReferenceEquals(controller, _controller)) _feedback.Show(error ? message : null, error ? null : message);
        });
    }

    /// <summary>Speaks a reorder or state announcement through the page's live region.</summary>
    internal void Announce(string message)
    {
        _announcer.Text = message;
        AutomationProperties.SetName(_announcer, message);
        FrameworkElementAutomationPeer.FromElement(_announcer)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // ── Render ───────────────────────────────────────────────────────────

    private string? _renderedHeaderKey;
    private IReadOnlyList<AdminLibrary>? _renderedDiagnosticsLibraries;

    private void Render(AdminLibrariesSnapshot state)
    {
        var ready = state.Owner != null && state.Loaded;
        _loading.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        _content.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        if (state.Owner == null) { CloseTransientUi(); return; }
        if (!ready) return;
        RenderHeader(state);
        RenderAlerts(state);
        RenderList(state);
        RenderJobControls(state);
        // Sections that name or pick libraries follow list changes.
        if (!ReferenceEquals(_renderedDiagnosticsLibraries, state.Libraries))
        {
            _renderedDiagnosticsLibraries = state.Libraries;
            _matcherView?.Render(); _ambiguousView?.Render();
        }
        _editor?.RenderOwner(state);
        UpdateScanQueueFlyout(state);
    }

    private void RenderHeader(AdminLibrariesSnapshot state)
    {
        var owner = RenderOwner(state);
        var groups = AdminLibrariesController.ScanGroups(state);
        var key = string.Join("|", _wide, _phone, state.ScanAllPending, groups.Sum(group => group.Scans.Count), AdminShellPage.CanOpen(new AdminRouteRequest(AdminRoute.Maintenance)));
        if (key == _renderedHeaderKey && ReferenceEquals(state.Owner, _renderedHeaderOwner)) return;
        _renderedHeaderOwner = state.Owner;
        _renderedHeaderKey = key;
        var focused = Focused(XamlRoot);
        var focusId = focused == null ? "" : AutomationProperties.GetAutomationId(focused);

        var scanAll = Action(T("web.admin.libraries.page.scanAll"), "LibrariesScanAll", "scan-line");
        scanAll.IsEnabled = !state.ScanAllPending;
        scanAll.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.ScanAllAsync(owner.Lease); };
        var maintenanceRequest = new AdminRouteRequest(AdminRoute.Maintenance);
        var maintenance = Action(T("web.admin.libraries.page.catalogMaintenance"), "LibrariesCatalogMaintenance", "wrench");
        if (AdminShellPage.CanOpen(maintenanceRequest)) maintenance.Click += (_, _) => { if (owner.IsCurrent) AdminShellPage.Open(maintenanceRequest); };
        else
        {
            // The target is a Configure page with no native implementation yet: say so instead of a dead link.
            maintenance.IsEnabled = false;
            ToolTipService.SetToolTip(maintenance, T("admin.native.libraries.maintenanceUnavailable"));
            AutomationProperties.SetHelpText(maintenance, T("admin.native.libraries.maintenanceUnavailable"));
        }
        var add = Action(T("web.admin.libraries.page.addLibrary"), "LibrariesAdd", "plus", "AccentButtonStyle");
        add.Click += (_, _) => OpenEditor(null, owner);
        var queue = groups.Count > 0 ? ScanQueueButton(state, groups) : null;

        FrameworkElement actions;
        if (_wide)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
            if (queue != null) row.Children.Add(queue);
            row.Children.Add(scanAll); row.Children.Add(maintenance); row.Children.Add(add);
            actions = row;
            _header.Content = LogsNativeUi.Header(T("web.admin.libraries.page.title"), T("web.admin.libraries.page.subtitle"), actions);
        }
        else
        {
            // Below lg the header buttons share rows at equal width and wrap; Add library takes its own row.
            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            var top = new List<Button>();
            if (queue != null) top.Add(queue);
            top.Add(scanAll); top.Add(maintenance);
            var perRow = _phone ? 2 : top.Count;
            for (var column = 0; column < perRow; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var rows = (top.Count + perRow - 1) / perRow + 1;
            for (var row = 0; row < rows; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var index = 0; index < top.Count; index++)
            {
                var button = top[index];
                button.Height = button.MinHeight = 44; button.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetRow(button, index / perRow); Grid.SetColumn(button, index % perRow);
                grid.Children.Add(button);
            }
            add.Height = add.MinHeight = 44; add.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(add, rows - 1); Grid.SetColumnSpan(add, perRow);
            grid.Children.Add(add);
            var stack = new StackPanel { Spacing = 16 };
            stack.Children.Add(LogsNativeUi.Header(T("web.admin.libraries.page.title"), T("web.admin.libraries.page.subtitle")));
            stack.Children.Add(grid);
            _header.Content = stack;
        }
        if (focusId.Length > 0) EnqueueUi(() => FindControl(_header, focusId)?.Focus(FocusState.Programmatic));
    }

    private void RenderAlerts(AdminLibrariesSnapshot state)
    {
        var owner = RenderOwner(state);
        _alerts.Children.Clear();
        if (_legacyNotice != null) _alerts.Children.Add(LogsNativeUi.Banner(_legacyNotice));
        if (state.Error != null)
        {
            var banner = LogsNativeUi.Banner(state.Error, error: true);
            AutomationProperties.SetLiveSetting(banner, AutomationLiveSetting.Assertive);
            AutomationProperties.SetAutomationId(banner, "LibrariesLoadError");
            _alerts.Children.Add(banner);
            var retry = Action(Core.Localization.AppStrings.Get("common.retry", fallback: "Retry"), "LibrariesRetry");
            retry.HorizontalAlignment = HorizontalAlignment.Left;
            retry.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.ReloadAsync(); };
            _alerts.Children.Add(retry);
        }
        if (state.Truncated)
            _alerts.Children.Add(LogsNativeUi.Banner(T("admin.native.libraries.truncated", AdminLibrariesText.Count(state.Libraries.Count))));
        _alerts.Visibility = _alerts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderJobControls(AdminLibrariesSnapshot state)
    {
        var owner = RenderOwner(state);
        _jobControls.Children.Clear();
        if (state.RefreshJobsFailed)
        {
            var segments = AdminLibrariesText.Segments(T("web.admin.common.jobPageControls.loadFailed"));
            var alert = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            AutomationProperties.SetLiveSetting(alert, AutomationLiveSetting.Assertive);
            foreach (var (text, tag) in segments)
            {
                if (tag == "button")
                {
                    var restart = Action(text, "LibrariesRestartJobs");
                    restart.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.RestartJobsAsync(); };
                    alert.Children.Add(restart);
                }
                else if (text.Trim().Length > 0) alert.Children.Add(Text(text.Trim()));
            }
            _jobControls.Children.Add(alert);
        }
        if (state.RefreshJobsHasMore)
        {
            var older = Action(T("web.admin.libraries.page.loadOlderRefreshJobs"), "LibrariesLoadOlderJobs");
            older.IsEnabled = !state.RefreshJobsLoadingMore;
            older.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.LoadOlderJobsAsync(); };
            _jobControls.Children.Add(older);
        }
        if (state.RefreshJobsCapped) _jobControls.Children.Add(Text(T("admin.native.libraries.pageCap", AdminLibrariesText.Count(AdminLibrariesController.MaxRefreshJobPages)), 12, true));
        _jobControls.Visibility = _jobControls.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyWidth()
    {
        var width = XamlRoot?.Size.Width ?? ActualWidth;
        if (width <= 0) return;
        var wide = width >= TableBreakpoint;
        var phone = width < 640;
        if (wide == _wide && phone == _phone) return;
        _wide = wide; _phone = phone;
        _renderedHeaderKey = null; _renderedListKey = null;
        RenderDiagnosticsLayout();
        _editor?.ApplyViewport();
        // Never replace list children from a SizeChanged/layout callback.
        QueueRender();
    }

    internal static Control? FindControl(DependencyObject root, string automationId)
    {
        if (root is Control control && control.IsEnabled && AutomationProperties.GetAutomationId(control) == automationId) return control;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindControl(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index), automationId) is { } match) return match;
        return null;
    }

    private void OpenItem(string contentId, LibraryUiOwner owner)
    {
        if (!owner.IsCurrent) return;
        App.Services.GetService<NavigationService>()?.Navigate<ItemDetailPage>(contentId);
    }
}
