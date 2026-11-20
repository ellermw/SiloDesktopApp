using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Manage > Playback > Settings. Server configuration only; does not alter installed-player/libmpv behavior.</summary>
public sealed partial class AdminPlaybackSettingsPage : Page, IAdminGutterScrollPage, IAdminPageSuspension, IAdminNavigationGuard
{
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _body = new() { Spacing = 24 };
    private readonly AdminMonitorAuthority _authority;
    private AdminPlaybackSettingsController? _controller;
    private AdminPlaybackSettingsPanel? _panel;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _leaveDialog;
    private bool _active;
    private long _supportSequence;
    private CancellationTokenSource? _supportCancellation;
    private readonly DispatcherTimer _taskPoll = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _taskRefreshing, _focused = true, _visible = true;
    public FrameworkElement ScrollContent => _body;
    public AdminPlaybackSettingsController? Controller => _controller;
    public bool RequiresLeaveConfirmation => _panel?.IsDialogOpen == true || _leave?.RequiresConfirmation == true;
    public AdminPlaybackSettingsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminPlaybackSettingsPage");
        _body.Children.Add(U.Title(AdminPlaybackAdministrationText.T("web.admin.settings.playback.title")));
        _scroll.Content = _body; _root.Children.Add(_scroll); Content = _root;
        _scroll.SizeChanged += (_, _) => Fit(); Loaded += async (_, _) => { Fit(); await Activate(); }; Unloaded += (_, _) => SuspendAdminPage();
        _taskPoll.Tick += async (_, _) => await RefreshTasks();
    }
    private void Fit() { var width = _scroll.ActualWidth - _body.Margin.Left - _body.Margin.Right; if (width > 0) _body.Width = width; }
    private async Task Activate()
    {
        if (_active) return; _active = true;
        _controller = new(App.Services.GetRequiredService<AdminServerSettingsApi>(), App.Services.GetRequiredService<AdminServerSettingsStore>(), _authority, false);
        _panel = new(_controller, false, this); _body.Children.Add(_panel); _root.Children.Add(_panel.SaveBar); _panel.RunTask += RunTask; _panel.RefreshSupport += ReviewSupport;
        var controller = _controller;
        _leave = new(() => controller.State.Owner, () => controller.State.DirtyKeys.Count > 0, controller.Discard);
        _authority.AvailabilityChanged += AuthorityChanged;
        if (App.MainWindowInstance is { } window) { window.Activated += WindowActivated; window.VisibilityChanged += WindowVisible; _visible = window.Visible; }
        _taskPoll.Start();
        await Open();
    }
    private async Task Open()
    {
        var controller = _controller; if (!_active || controller == null || !_authority.IsAvailable) return;
        await controller.OpenAsync(); await RefreshSupport();
    }
    private async Task RefreshSupport()
    {
        var controller = _controller; var owner = controller?.State.Owner; var sequence = ++_supportSequence;
        if (owner?.IsCurrent != true) return;
        _supportCancellation?.Cancel(); _supportCancellation?.Dispose();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); _supportCancellation = cancellation;
        try
        {
            var support = await App.Services.GetRequiredService<AdminPlaybackAdministrationApi>().SupportAsync(owner, cancellation.Token, AdminPlaybackSettingsRules.Get(controller!.State.Values, "playback.hw_accel") != "none");
            if (_active && owner.IsCurrent && ReferenceEquals(controller, _controller) && ReferenceEquals(owner, controller!.State.Owner) && sequence == _supportSequence) _panel?.SetSupport(support);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_supportCancellation, cancellation)) _supportCancellation = null; }
    }
    private async Task ReviewSupport() { await RefreshSupport(); if (_active) _panel?.ReviewTaskOutcomes(); }
    private void WindowActivated(object sender, WindowActivatedEventArgs args) => _focused = args.WindowActivationState != WindowActivationState.Deactivated;
    private void WindowVisible(object sender, WindowVisibilityChangedEventArgs args) => _visible = args.Visible;
    private async Task RefreshTasks()
    {
        var controller = _controller; var owner = controller?.State.Owner;
        if (!_active || !_focused || !_visible || _taskRefreshing || _supportCancellation != null || _panel?.IsDialogOpen == true || owner?.IsCurrent != true || App.Services.GetService<SiloPlayer.Services.PlayerService>()?.IsLoading == true) return;
        _taskRefreshing = true;
        try
        {
            var tasks = await App.Services.GetRequiredService<AdminPlaybackAdministrationApi>().ListMarkerTasksAsync(owner, owner.Token);
            if (_active && owner.IsCurrent && ReferenceEquals(controller, _controller) && ReferenceEquals(owner, controller!.State.Owner)) _panel?.SetTasks(tasks);
        }
        catch { /* Retain last observations; manual Refresh remains available. No mutation is replayed. */ }
        finally { _taskRefreshing = false; }
    }
    private async Task RunTask(string key)
    {
        var owner = _controller?.State.Owner; if (owner?.IsCurrent != true) return;
        try { await App.Services.GetRequiredService<AdminPlaybackAdministrationApi>().RunMarkerTaskAsync(owner, key, owner.Token); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { if (owner.IsCurrent) { _panel?.MarkTaskUncertain(key); App.Services.GetService<SiloPlayer.Services.ToastService>()?.Error("The task outcome is unconfirmed. Review task history before running it again."); } return; }
        await RefreshSupport();
    }
    private void AuthorityChanged() => DispatcherQueue.TryEnqueue(() => { if (!_active) return; _supportSequence++; _leaveDialog?.Hide(); if (_authority.IsAvailable) _ = Open(); });
    public async Task<bool> CanLeaveAsync()
    {
        if (_panel?.IsDialogOpen == true || _controller?.State.Saving == true) return false;
        return _leave == null || await _leave.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _leaveDialog, dialog => _leaveDialog = dialog));
    }
    public void SuspendAdminPage()
    {
        if (!_active) return; _active = false; _supportSequence++; _supportCancellation?.Cancel(); _supportCancellation?.Dispose(); _supportCancellation = null; _authority.AvailabilityChanged -= AuthorityChanged; _leaveDialog?.Hide(); _leaveDialog = null;
        _taskPoll.Stop(); if (App.MainWindowInstance is { } window) { window.Activated -= WindowActivated; window.VisibilityChanged -= WindowVisible; }
        if (_panel != null) { _panel.RunTask -= RunTask; _panel.RefreshSupport -= ReviewSupport; _panel.Dispose(); _root.Children.Remove(_panel.SaveBar); _body.Children.Remove(_panel); _panel = null; }
        _controller?.Dispose(); _controller = null;
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { SuspendAdminPage(); base.OnNavigatedFrom(args); }
}
