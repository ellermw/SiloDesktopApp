using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure › Server › Scheduled Tasks (official <c>AdminTasks.tsx</c> at <c>/admin/tasks</c> and
/// <c>AdminTaskDetail.tsx</c> at <c>/admin/tasks/:key</c>). One registered page shows the list or one
/// task, depending on the location. Each shown location owns its own controller and authority owner;
/// realtime frames only invalidate, and the metrics/clock tick runs only while this page and its
/// window are in front.
/// </summary>
public sealed partial class AdminScheduledTasksPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private const double NarrowWidth = 640;
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { Spacing = 24, MaxWidth = 1280, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(0, 0, 0, 24) };
    private readonly AdminMonitorAuthority _authority;
    private AdminTasksController? _list;
    private AdminTaskDetailController? _detail;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _navigated, _hostVisible = true, _hostFocused = true, _opening, _narrow;

    public FrameworkElement ScrollContent => _scrollContent;
    public AdminTasksController? ListController => _list;
    public AdminTaskDetailController? DetailController => _detail;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;

    public AdminScheduledTasksPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminScheduledTasksPage");
        AutomationProperties.SetName(_scroller, AdminTasksText.Message("web.admin.tasks.page.title"));
        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent; _root.Children.Add(_scroller);
        _scroller.SizeChanged += (_, _) => FitScrollContent();
        Content = _root;
        SizeChanged += (_, args) => ApplyWidth(args.NewSize.Width);
        Loaded += (_, _) => { FitScrollContent(); if (_navigated) Activate(); };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!IsLoaded) Close(); });
    }

    private void FitScrollContent()
    {
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    private void ApplyWidth(double width)
    {
        var narrow = width > 0 && width < NarrowWidth;
        if (narrow == _narrow) return;
        _narrow = narrow;
        RelayoutList();
        RelayoutDetail();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _location = e.Parameter as AdminRouteRequest;
        _restoreOffset = e.NavigationMode == NavigationMode.Back && _location != null && ScrollPositions.TryGetValue(_location, out var position) ? position.Offset : 0;
        _navigated = true;
        base.OnNavigatedTo(e);
        if (IsLoaded) Activate();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); _navigated = false; base.OnNavigatedFrom(e); }

    /// <summary>Outer Admin departure: retire reads, commands, timers, dialogs and any schedule draft.</summary>
    public void SuspendAdminPage() => Close();

    /// <summary>The key of the shown task, or null on the list.</summary>
    private string? TaskKey => _location?.Segment(0);

    private void Activate()
    {
        if (_active) return;
        _active = true;
        var channel = RealtimeChannel();
        var api = App.Services.GetRequiredService<AdminTasksApi>();
        if (TaskKey is { } key)
        {
            _detail = new AdminTaskDetailController(api, _authority, key, channel);
            _detail.Changed += QueueRender;
            _detail.AuthorityRetired += OnAuthorityRetired;
            _detail.Notice += OnNotice;
            _leave = new AdminLeaveGuard(() => _detail?.State.Owner, () => _detail?.State.Dirty == true, () => _detail?.CancelEdit());
            BuildDetail();
        }
        else
        {
            _list = new AdminTasksController(api, _authority, channel);
            _list.Changed += QueueRender;
            _list.AuthorityRetired += OnAuthorityRetired;
            _list.Notice += OnNotice;
            BuildList();
        }
        if (App.MainWindowInstance is { } window) { window.Activated += OnWindowActivated; window.VisibilityChanged += OnWindowVisibilityChanged; _hostVisible = window.Visible; }
        if (XamlRoot != null) { XamlRoot.Changed += OnXamlRootChanged; _hostVisible &= XamlRoot.IsHostVisible; }
        ApplyWidth(ActualWidth);
        PushActivity();
        OpenController();
        Render();
    }

    /// <summary>The app's one shared event client; a native fixture may register its own synthetic channel instead.</summary>
    private static IAdminRealtimeChannel? RealtimeChannel() =>
        App.Services.GetService<IAdminRealtimeChannel>() ??
        (App.Services.GetService<EventChannelClient>() is { } events ? new EventChannelAdminRealtime(events) : null);

    private void OpenController()
    {
        if (_opening || !_active || !_authority.IsAvailable) return;
        _opening = true;
        try { if (_detail != null) _detail.Open(); else _list?.Open(); }
        finally { _opening = false; }
    }

    private void OnAuthorityRetired() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        _dialog?.Hide();
        ClearList(); ClearDetail();
        OpenController();
        Render();
    });

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _hostFocused = args.WindowActivationState != WindowActivationState.Deactivated;
        PushActivity();
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        _hostVisible = args.Visible && (XamlRoot?.IsHostVisible ?? true);
        PushActivity();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _hostVisible = sender.IsHostVisible && (App.MainWindowInstance?.Visible ?? true);
        PushActivity();
    }

    /// <summary>Window activation and visibility as the page observes them; native fixtures drive this directly.</summary>
    public void SetHostActivity(bool visible, bool focused)
    {
        _hostVisible = visible; _hostFocused = focused;
        PushActivity();
    }

    private void PushActivity()
    {
        var active = _active && _hostVisible && _hostFocused;
        _list?.SetActive(active);
        _detail?.SetActive(active);
    }

    private void QueueRender()
    {
        var list = _list;
        var detail = _detail;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && ReferenceEquals(list, _list) && ReferenceEquals(detail, _detail)) Render();
        });
    }

    private void Render()
    {
        if (!_active) return;
        if (_list != null) RenderList(_list.State);
        if (_detail != null) RenderDetail(_detail.State);
        if (_restoreOffset is { } offset && (_list?.State.Loaded == true || _detail?.State.Task != null))
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); } });
        }
    }

    private void OnNotice(AdminTaskNotice notice)
    {
        var list = _list;
        var detail = _detail;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || !ReferenceEquals(list, _list) || !ReferenceEquals(detail, _detail)) return;
            var owner = _list?.State.Owner ?? _detail?.State.Owner;
            if (owner is not { IsCurrent: true } || !ReferenceEquals(owner, notice.Owner)) return;
            var toasts = App.Services.GetService<ToastService>();
            if (notice.Kind is AdminTaskNoticeKind.Started or AdminTaskNoticeKind.CancelRequested or AdminTaskNoticeKind.ScheduleSaved) toasts?.Success(notice.Message);
            else toasts?.Error(notice.Message);
        });
    }

    /// <summary>Opens another task-page location (a task row, the breadcrumb, All tasks) through the shell, which asks the leave guard.</summary>
    private static void OpenLocation(AdminRouteRequest request) => AdminShellPage.Open(request);

    private static AdminRouteRequest TaskLocation(string key) => new(AdminRoute.ScheduledTasks) { Segments = [key] };

    public Task<bool> CanLeaveAsync()
    {
        if (_detail?.State is { Saving: true, Owner.IsCurrent: true }) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => AdminSettingsUi.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetValue(_location, static _ => new ScrollPosition()).Offset = _scroller.VerticalOffset;
        if (App.MainWindowInstance is { } window) { window.Activated -= OnWindowActivated; window.VisibilityChanged -= OnWindowVisibilityChanged; }
        if (XamlRoot != null) XamlRoot.Changed -= OnXamlRootChanged;
        _dialog?.Hide(); _dialog = null;
        if (_list != null)
        {
            _list.Changed -= QueueRender; _list.AuthorityRetired -= OnAuthorityRetired; _list.Notice -= OnNotice;
            _list.Dispose(); _list = null;
        }
        if (_detail != null)
        {
            _detail.Changed -= QueueRender; _detail.AuthorityRetired -= OnAuthorityRetired; _detail.Notice -= OnNotice;
            _detail.Dispose(); _detail = null;
        }
        _leave = null;
        ClearList(); ClearDetail();
        _body.Children.Clear();
    }
}
