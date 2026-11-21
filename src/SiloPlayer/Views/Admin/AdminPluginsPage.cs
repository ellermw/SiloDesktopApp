using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using T = SiloPlayer.Core.Services.Admin.AdminPluginsText;
using Ui = SiloPlayer.Views.Admin.AdminPluginsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure › Integrations › Plugins: the board (/admin/plugins) and one plugin's page (/admin/plugins/:pluginId), as the
/// official AdminPlugins and AdminPluginDetail pages at 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e. Plugin-contributed app
/// pages (Plugin Apps) are another family; their entries here are disabled. Every write goes through the controller.
/// </summary>
public sealed partial class AdminPluginsPage : Page, IAdminNavigationGuard, IAdminPageSuspension, IAdminGutterScrollPage
{
    private const int TilePage = 60;
    private const int MaxPollTicks = 60;
    /// <summary>The board's search and filter state, handed to the plugin page so its back link restores it (official router state).</summary>
    private static AdminRouteRequest? _boardReturn;

    private readonly AdminPluginsApi _api;
    private readonly AdminTasksApi _tasks;
    private readonly AdminMonitorAuthority _authority;
    private readonly StackPanel _body = Stack(20);
    private readonly StackPanel _banner = Stack(8);
    private readonly StackPanel _content = Stack(20);
    private readonly ProgressBar _loading = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly FrameworkElement _header;
    private readonly Button _check;
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<string, AdminPluginConfigView> _forms = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedForms = new(StringComparer.Ordinal);
    private AdminPluginsController? _controller;
    private AdminPluginsState? _rendered;
    private AdminRouteRequest _route = new(AdminRoute.Plugins);
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private bool _closed = true;
    private bool _syncing;
    private int _pollTicks;
    private int _generation;
    private string _tab = "installed";

    /// <summary>Opens a link; the fixture replaces it. Only links that passed the safe-link policy reach it.</summary>
    public Func<Uri, Task<bool>> Launcher { get; set; } = async uri => await Windows.System.Launcher.LaunchUriAsync(uri);
    public AdminPluginsController? Controller => _controller;
    FrameworkElement IAdminGutterScrollPage.ScrollContent => _body;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;

    public AdminPluginsPage() : this(App.Services.GetRequiredService<AdminPluginsApi>(), App.Services.GetRequiredService<AdminTasksApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>()) { }

    public AdminPluginsPage(AdminPluginsApi api, AdminTasksApi tasks, AdminMonitorAuthority authority)
    {
        InitializeComponent(); _api = api; _tasks = tasks; _authority = authority; NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminPluginsPage");
        _check = AdminSettingsUi.Action(T.T("web.admin.plugins.page.checkUpdates"), "PluginsCheckUpdates");
        _check.Click += async (_, _) => { if (_controller is { } controller) { await controller.CheckUpdatesAsync(); StartPollIfNeeded(); } };
        _header = Header(T.T("web.admin.plugins.page.title"), T.T("web.admin.plugins.page.subtitle"), _check);
        _body.Children.Add(_header); _body.Children.Add(_loading); _body.Children.Add(_banner); _body.Children.Add(_content);
        _body.Padding = new(0, 0, 0, 28);
        _scroll = new() { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Content = _scroll;
        _poll.Tick += async (_, _) =>
        {
            if (++_pollTicks >= MaxPollTicks) { _poll.Stop(); return; }
            if (_controller is { } controller) await controller.PollUpdateCheckAsync(!_forms.Values.Any(form => form.IsDirty) && _dialog == null);
        };
        Loaded += (_, _) => Open(); Unloaded += (_, _) => Close();
        _body.SizeChanged += (_, args) => { LayoutTiles(args.NewSize.Width); LayoutDetail(args.NewSize.Width); LayoutSources(args.NewSize.Width); };
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not AdminRouteRequest { Route: AdminRoute.Plugins } route) return;
        _route = route;
        // Old links named the plugin in ?configure=<id>; each plugin has its own page now.
        if (route.Get("configure") is { Length: > 0 } legacy) _route = route with { Segments = [legacy], Query = null };
        _tab = _route.Get("tab") == "catalog" ? "catalog" : "installed";
    }

    private void Open()
    {
        if (!_closed) return;
        _closed = false; var controller = new AdminPluginsController(_api, _tasks, _authority); _controller = controller; var generation = ++_generation;
        _header.Visibility = _route.Segments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AdminShellPage.TryReplace(this, _route); // canonicalizes an old ?configure= link to its plugin's page
        _leave = new(() => controller.Owner, () => _forms.Values.Any(form => form.IsDirty), DiscardForms);
        controller.Changed += QueueRender; controller.Retired += Retired;
        Render();
        _ = RunAsync(async () => { await controller.OpenAsync(_route.Segment(0)); if (generation == _generation) StartPollIfNeeded(); });
    }

    private async Task RunAsync(Func<Task> work)
    {
        try { await work(); }
        catch (OperationCanceledException) { /* the page or its authority ended */ }
        catch (ObjectDisposedException) { /* the page closed while work was in flight */ }
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
            var dialog = _dialog; dialog?.Hide(); DiscardForms(); _content.Children.Clear(); _rendered = null; _poll.Stop(); _upload = null;
            Banner(T.T("web.admin.plugins.hooks.plugins.settings.selectProfile"), true);
            _content.Children.Add(Ui.Command(T.T("common.retry"), "PluginsReopen", () => Reopen()));
        });
    }

    /// <summary>After an authority change the admin reopens the page; nothing resumes by itself.</summary>
    private void Reopen()
    {
        if (_closed) return;
        var route = _route; Close(); _route = route; Open();
    }

    private static string Signature(AdminPluginsState state) =>
        string.Join('|', state.Installations.GetHashCode(), state.Catalog.GetHashCode(), state.Repositories.GetHashCode(), state.Provisioning.GetHashCode(), state.CatalogSettings?.GetHashCode(), state.CatalogStatus?.GetHashCode(),
            state.Failures.GetHashCode(), state.Loaded.GetHashCode(), state.Busy, state.BusyKey, state.Unconfirmed.GetHashCode(), state.CatalogReviewRequired, state.Loading, state.TaskState);

    private string _renderedSignature = "";

    private void Render()
    {
        var controller = _controller;
        if (controller == null) return;
        if (controller.Owner is not { IsCurrent: true }) { _loading.Visibility = Visibility.Collapsed; return; }
        var state = controller.State;
        _loading.Visibility = state.Loading || state.Busy ? Visibility.Visible : Visibility.Collapsed;
        var checking = state.TaskState is "running" or "cancelling";
        _check.Content = checking ? T.T("web.admin.plugins.page.checkingUpdates") : T.T("web.admin.plugins.page.checkUpdates");
        _check.IsEnabled = !state.Busy && !state.Loading && !checking;
        if (checking) StartPollIfNeeded(); else _poll.Stop();
        ShowNotice(state);
        var signature = Signature(state);
        if (signature == _renderedSignature && _rendered != null) { foreach (var form in _forms.Values) form.SetBusy(state.Busy); return; }
        _renderedSignature = signature; _rendered = state;
        Rebuild(state);
        foreach (var form in _forms.Values) form.SetBusy(state.Busy);
    }

    /// <summary>Draws the page again from scratch, e.g. so a control that a refused or unknown write left flipped shows the saved value.</summary>
    private void RebuildNow()
    {
        if (_closed || _controller == null) return;
        _renderedSignature = ""; Render();
    }

    private void Rebuild(AdminPluginsState state)
    {
        _syncing = true;
        var searchText = _search?.Text; var searchHadFocus = _search != null && _search.FocusState != FocusState.Unfocused;
        try
        {
            // Forms keep their drafts across a rebuild, so each one leaves its old parent before the old tree is dropped.
            foreach (var form in _forms.Values) (form.Parent as Microsoft.UI.Xaml.Controls.Panel)?.Children.Remove(form);
            _usedForms.Clear(); _content.Children.Clear(); _boardList = null; _detailGrid = null; _detailMain = null; _detailRail = null;
            _header.Visibility = _route.Segments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_route.Segments.Count == 0) BuildBoard(state); else BuildDetail(state);
            foreach (var stale in _forms.Keys.Where(key => !_usedForms.Contains(key)).ToArray()) { _forms[stale].Dispose(); _forms.Remove(stale); }
        }
        finally { _syncing = false; }
        // A redraw (a poll finishing, a write completing) never takes the admin out of the search box they are typing in.
        if (searchHadFocus && _search != null && _search.Text == searchText) { _search.Focus(FocusState.Programmatic); _search.SelectionStart = _search.Text.Length; }
    }

    private void ShowNotice(AdminPluginsState state)
    {
        _banner.Children.Clear();
        if (string.IsNullOrEmpty(state.Notice)) return;
        var text = Text(state.Notice, 13); text.IsTextSelectionEnabled = true;
        AutomationProperties.SetAutomationId(text, "PluginsNotice");
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(text);
        var dismiss = AdminSettingsUi.Action(T.T("common.done"), "PluginsNoticeDismiss"); Grid.SetColumn(dismiss, 1);
        dismiss.Click += (_, _) => _controller?.Dismiss(); row.Children.Add(dismiss);
        _banner.Children.Add(Ui.Banner(row, state.NoticeIsError));
    }

    private void Banner(string message, bool warning)
    {
        _banner.Children.Clear(); _banner.Children.Add(Ui.Banner(Text(message, 13), warning));
    }

    // ── navigation, leaving, dialogs ───────────────────────────────────────
    private async Task GoAsync(AdminRouteRequest route)

    {
        if (!await CanLeaveAsync() || _controller?.Owner is not { IsCurrent: true }) return;
        if (AdminShellPage.CanOpen(route)) AdminShellPage.Open(route);
    }

    private void ReplaceQuery(string key, string? value)
    {
        _route = _route.WithQuery(key, value); AdminShellPage.TryReplace(this, _route);
    }

    private void DiscardForms() { foreach (var form in _forms.Values) form.Dispose(); _forms.Clear(); _usedForms.Clear(); }

    public Task<bool> CanLeaveAsync()
    {
        if (_dialog != null) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => AdminSettingsUi.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }

    /// <summary>
    /// A confirmation under the page's owner. The caller captured the row it describes first; the write re-checks it again at
    /// the moment of sending, so a list that changed while the dialog was open sends nothing.
    /// </summary>
    private async Task<bool> ConfirmAsync(string title, UIElement content, string primary, string id, bool destructive = false)
    {
        var controller = _controller; var owner = controller?.Owner;
        if (_dialog != null || owner is not { IsCurrent: true } || XamlRoot == null) return false;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = T.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        if (destructive) dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"];
        AutomationProperties.SetAutomationId(dialog, id); _dialog = dialog;
        using var retirement = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try
        {
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary && !_closed && ReferenceEquals(_controller, controller) && ReferenceEquals(owner, controller?.Owner) && owner.IsCurrent;
        }
        catch (System.Runtime.InteropServices.COMException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private void StartPollIfNeeded()
    {
        if (_closed || _controller == null || _route.Segments.Count > 0) return;
        if (_controller.State.TaskState is "running" or "cancelling" && !_poll.IsEnabled) { _pollTicks = 0; _poll.Start(); }
    }

    public void SuspendAdminPage() => Close();

    private void Close()
    {
        if (_closed) return;
        _closed = true; ++_generation; _poll.Stop(); var dialog = _dialog; _dialog = null; dialog?.Hide();
        if (_controller is { } controller) { controller.Changed -= QueueRender; controller.Retired -= Retired; controller.Dispose(); }
        _controller = null; _rendered = null; _renderedSignature = ""; DiscardForms(); _content.Children.Clear(); _banner.Children.Clear();
        _boardList = null; _search = null; _detailGrid = null; _detailMain = null; _detailRail = null; _leave = null;
        ClearUpload();
    }

    // ── shared pieces ──────────────────────────────────────────────────────

    private FrameworkElement Unavailable(string title, string description, string retryId, Action retry, bool back = true)
    {
        var body = Stack(10);
        body.Children.Add(Ui.Heading(title)); body.Children.Add(Text(description, 13, true));
        var actions = new LogsWrapPanel { Spacing = 8 };
        actions.Children.Add(Ui.Command(T.T("common.retry"), retryId, retry));
        if (back) actions.Children.Add(Ui.Command(T.T("web.admin.plugins.detail.allPlugins"), "PluginsAll", () => _ = GoAsync(new(AdminRoute.Plugins))));
        body.Children.Add(actions);
        return Panel(body, 16, 20, border: true);
    }

    private void ReloadAll() { if (_controller is { } controller) _ = RunAsync(async () => { if (await CanLeaveAsync()) await controller.ReloadAsync(); }); }
    private void ReloadSection(AdminPluginsSection section) { if (_controller is { } controller) _ = RunAsync(() => controller.ReloadAsync([section])); }

    private FrameworkElement ProvisioningBlock(AdminPluginsState state, string? onlyPlugin = null)
    {
        var host = Stack(8);
        if (state.Failures.ContainsKey(AdminPluginsSection.Provisioning))
        {
            var row = new LogsWrapPanel { Spacing = 8 };
            var message = Text(T.T("web.admin.plugins.provisioning.loadError"), 13, true); AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite); row.Children.Add(message);
            var retry = Ui.Command(T.T("common.retry"), "PluginsProvisioningRetry", () => ReloadSection(AdminPluginsSection.Provisioning)); retry.IsEnabled = !state.Loading; row.Children.Add(retry);
            host.Children.Add(row);
        }
        var items = state.Provisioning.Where(item => onlyPlugin == null || item.PluginId == onlyPlugin && item.State != "applied").ToArray();
        if (items.Length > 0)
        {
            var list = Stack(8); list.Children.Add(Ui.Heading(T.T("web.admin.plugins.provisioning.title"), 2, 14));
            foreach (var item in items)
            {
                var line = Stack(2); var head = new LogsWrapPanel { Spacing = 12 };
                var name = Text(item.PluginId, 14); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; head.Children.Add(name);
                if (item.Version.Length > 0) head.Children.Add(Text(T.T("web.admin.plugins.detail.version", new { version = item.Version }), 13, true));
                head.Children.Add(Text(item.State switch
                {
                    "pending" => T.T("web.admin.plugins.provisioning.pending"), "refused" => T.T("web.admin.plugins.provisioning.refused"),
                    "held" => T.T("web.admin.plugins.provisioning.held"), "applied" => T.T("web.admin.plugins.provisioning.applied"), _ => item.State,
                }, 13));
                line.Children.Add(head);
                if (item.Detail.Length > 0) line.Children.Add(Text(P.Redact(item.Detail, 600), 12, true));
                list.Children.Add(line);
            }
            var panel = Panel(list, 12, 16, border: true); AutomationProperties.SetAutomationId(panel, "PluginsProvisioning"); host.Children.Add(panel);
        }
        return host;
    }

}
