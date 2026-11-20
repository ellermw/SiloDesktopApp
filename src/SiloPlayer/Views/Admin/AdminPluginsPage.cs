using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>/admin/plugins and /admin/plugins/:pluginId. XAML-backed for real Frame activation; no plugin-app host/navigation ownership.</summary>
public sealed partial class AdminPluginsPage : Page, IAdminNavigationGuard, IAdminPageSuspension, IAdminGutterScrollPage
{
    private readonly AdminPluginsApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly StackPanel _body = Stack(20);
    private readonly StackPanel _alerts = Stack(8);
    private readonly StackPanel _content = Stack(20);
    private readonly TextBlock _notice = Text("");
    private readonly ProgressBar _loading = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly Button _refresh;
    private readonly Button _check;
    private readonly FrameworkElement _header;
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _taskPoll = new() { Interval = TimeSpan.FromSeconds(2) };
    private int _pollCount;
    private bool _pollExhausted;
    private AdminPluginsController? _controller;
    private AdminPluginsState? _rendered;
    private AdminRouteRequest _route = new(AdminRoute.Plugins);
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private readonly List<AdminPluginConfigView> _forms = [];
    private string _tab = "installed";
    private int _page;
    private int _columns = 1;
    private int _pageSize = 25;
    private TextBox? _search;
    private ComboBox? _filter;
    private ComboBox? _job;
    private StackPanel? _listHost;
    private LogsWrapPanel? _chipHost;
    private bool _syncing;
    private bool _closed = true;
    public AdminPluginsController? Controller => _controller;
    public FrameworkElement ScrollContent => _body;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminPluginsPage() : this(App.Services.GetRequiredService<AdminPluginsApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>()) { }
    public AdminPluginsPage(AdminPluginsApi api, AdminMonitorAuthority authority)
    {
        InitializeComponent(); _api = api; _authority = authority; NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminPluginsPage");
        _refresh = U.Action("Refresh", "PluginsRefresh");
        _refresh.Click += async (_, _) => { if (_controller is { } c && await CanLeaveAsync()) { _pollExhausted = false; await c.ReloadAsync(reviewed: true); } };
        _check = U.Action("Check for updates", "PluginsCheckUpdates"); _check.Click += async (_, _) => { if (_controller is { } c && await CanLeaveAsync()) await c.CheckUpdatesAsync(); };
        var actions = new LogsWrapPanel { Spacing = 8 }; actions.Children.Add(_refresh); actions.Children.Add(_check);
        _header = Header("Plugins", "Add metadata sources, playback markers, request routing, and watch sync to Silo.", actions); _body.Children.Add(_header);
        AutomationProperties.SetLiveSetting(_notice, AutomationLiveSetting.Polite); AutomationProperties.SetAutomationId(_notice, "PluginsNotice");
        _body.Children.Add(_loading); _body.Children.Add(_notice); _body.Children.Add(_alerts); _body.Children.Add(_content);
        _body.Padding = new(0, 0, 0, 28);
        _scroll = new() { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Content = _scroll;
        _taskPoll.Tick += async (_, _) =>
        {
            if (++_pollCount >= 60) { _taskPoll.Stop(); _pollExhausted = true; _notice.Text = "The update check is still pending. Refresh to review its current state."; return; }
            if (_controller is { } controller) await controller.PollUpdateCheckAsync(!_forms.Any(f => f.IsDirty) && _dialog == null);
        };
        Loaded += (_, _) => Open(); Unloaded += (_, _) => Close();
        SizeChanged += (_, args) =>
        {
            var columns = args.NewSize.Width > 1120 ? 3 : args.NewSize.Width > 700 ? 2 : 1;
            if (columns != _columns) { _columns = columns; if (_listHost != null) RenderList(); }
            LayoutDetail(args.NewSize.Width);
        };
    }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is AdminRouteRequest { Route: AdminRoute.Plugins } route)
        {
            _route = route;
            if (route.Get("configure") is { Length: > 0 } legacy) _route = route with { Segments = [legacy], Query = null };
            _tab = _route.Get("tab") == "catalog" ? "catalog" : "installed";
        }
    }
    private void Open()
    {
        if (!_closed) return;
        _closed = false; var controller = new AdminPluginsController(_api, _authority); _controller = controller;
        _header.Visibility = _route.Segments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AdminShellPage.TryReplace(this, _route); // Canonicalizes old ?configure= links to their management descendant.
        _leave = new(() => controller.Owner, () => _forms.Any(f => f.IsDirty), ClearForms);
        controller.Changed += QueueRender; controller.Retired += Retired;
        Render(); _ = controller.OpenAsync(_route.Segment(0));
    }
    private void QueueRender()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() => { if (!_closed && ReferenceEquals(_controller, controller)) Render(); });
    }
    private void Retired()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !ReferenceEquals(controller, _controller)) return;
            _dialog?.Hide(); ClearForms(); _content.Children.Clear(); _rendered = null; _uploadFile = null; _taskPoll.Stop();
            _notice.Text = "The administrator session changed. Reopen Plugins to continue.";
        });
    }
    private void Render()
    {
        if (_controller == null) return;
        if (_controller.Owner is not { IsCurrent: true }) { ClearForms(); _content.Children.Clear(); _content.IsEnabled = false; _check.IsEnabled = _refresh.IsEnabled = false; _loading.Visibility = Visibility.Collapsed; _taskPoll.Stop(); return; }
        var state = _controller.State;
        _loading.Visibility = state.Loading ? Visibility.Visible : Visibility.Collapsed;
        _content.IsHitTestVisible = !state.Busy && !state.Loading;
        _content.IsEnabled = !state.Busy && !state.Loading && _controller.Owner is { IsCurrent: true };
        _refresh.IsEnabled = !state.Busy && !state.Loading && _controller.Owner is { IsCurrent: true };
        _check.Content = state.TaskState is "running" or "cancelling" ? "Checking updates..." : "Check for updates";
        _check.IsEnabled = _refresh.IsEnabled && state.TaskState == "idle";
        if ((state.TaskState is "running" or "cancelling") && !_pollExhausted)
        { if (!_taskPoll.IsEnabled) { _pollCount = 0; _taskPoll.Start(); } }
        else _taskPoll.Stop();
        U.UpdateLiveText(_notice, state.Notice);
        if (!state.Loaded || state.Loading) return;
        _alerts.Children.Clear();
        foreach (var error in state.Errors) _alerts.Children.Add(Text(error.Key + ": " + error.Value));
        if (state.CatalogReviewRequired) _alerts.Children.Add(Text("Catalog settings changed. Refresh and review them before submitting another edit."));
        var rebuild = _rendered == null || !ReferenceEquals(_rendered.Installations, state.Installations) || !ReferenceEquals(_rendered.Catalog, state.Catalog) ||
            !ReferenceEquals(_rendered.Repositories, state.Repositories) || !ReferenceEquals(_rendered.CatalogSettings, state.CatalogSettings) || _rendered.CatalogReviewRequired != state.CatalogReviewRequired;
        if (!rebuild) return;
        _rendered = state; ClearForms(); _content.Children.Clear(); _listHost = null;
        if (_route.Segments.Count > 0) BuildDetail(state); else BuildBoard(state);
    }
    private async Task GoAsync(AdminRouteRequest route)
    {
        if (!await CanLeaveAsync() || _controller?.Owner is not { IsCurrent: true }) return;
        // Same native page supports every management descendant. The shell updates its normal in-place Frame route.
        if (AdminShellPage.CanOpen(route)) AdminShellPage.Open(route);
    }
    private void ReplaceQuery(string key, string? value)
    {
        _route = _route.WithQuery(key, value); AdminShellPage.TryReplace(this, _route);
    }
    private void ClearForms() { foreach (var form in _forms) form.Dispose(); _forms.Clear(); }
    public Task<bool> CanLeaveAsync()
    {
        if (_dialog != null || _controller?.State.Busy == true) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }
    private async Task<bool> ConfirmAsync(string title, string message, string confirm, Func<bool> current)
    {
        var controller = _controller; var owner = controller?.Owner;
        if (_dialog != null || owner is not { IsCurrent: true } || !current()) return false;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = Text(message), PrimaryButtonText = confirm, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        AutomationProperties.SetAutomationId(dialog, "PluginsConfirmation"); _dialog = dialog;
        using var retirement = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try { var result = await dialog.ShowAsync(); return result == ContentDialogResult.Primary && !_closed && ReferenceEquals(_controller, controller) && ReferenceEquals(owner, controller?.Owner) && owner.IsCurrent && current(); }
        catch (System.Runtime.InteropServices.COMException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    public void SuspendAdminPage() => Close();
    private void Close()
    {
        if (_closed) return; _closed = true; _dialog?.Hide(); _dialog = null; _taskPoll.Stop();
        if (_controller is { } c) { c.Changed -= QueueRender; c.Retired -= Retired; c.Dispose(); }
        _controller = null; _rendered = null; ClearForms(); _content.Children.Clear(); _alerts.Children.Clear(); _notice.Text = "";
        _search = null; _filter = null; _job = null; _listHost = null; _chipHost = null; _leave = null; _uploadFile = null; _detailGrid = null; _detailMain = null; _detailRail = null;
    }
    private static TextBlock Heading(string title, int level = 2)
    {
        var result = Text(P.SafeText(title), level == 1 ? 30 : 16); result.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(result, level == 1 ? AutomationHeadingLevel.Level1 : AutomationHeadingLevel.Level2); return result;
    }
    private static StackPanel Section(StackPanel parent, string title, string description = "")
    {
        var body = Stack(12); body.Children.Add(Heading(title)); if (description.Length > 0) body.Children.Add(Text(P.SafeText(description), 13, true));
        parent.Children.Add(Panel(body, 16, 20)); return body;
    }
    private static Button Command(string text, string id, Action action)
    {
        var button = U.Action(text, id); button.Click += (_, _) => action(); return button;
    }
    private void UiError() => _notice.Text = "Review the highlighted fields and try again.";
}
