using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminAutoscanPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>Official /admin/autoscan Sources. Activity and polling Settings are separate registrations.</summary>
public sealed partial class AdminAutoscanSourcesPage : Page, IAdminGutterScrollPage, IAdminPageSuspension, IAdminNavigationGuard
{
    private readonly Grid _root = new();
    private readonly StackPanel _body = new() { Spacing = 24, Padding = new(0, 0, 0, 32) };
    private readonly StackPanel _scrollContent = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _loading = U.Text(T("sources.loading"), 14, true);
    private readonly TextBlock _feedback = U.Text("", 13);
    private readonly StackPanel _rows = new();
    private readonly Grid _header = new() { ColumnSpacing = 16, RowSpacing = 12 };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Top };
    private readonly ToggleSwitch _enabled = new() { OnContent = "", OffContent = "", MinWidth = 0 };
    private readonly TextBlock _badge = U.Text("", 12, true);
    private readonly Button _run = U.Action(T("page.runNow"), "AutoscanRunNow");
    private readonly Button _add = U.Action(T("sources.add"), "AutoscanAdd");
    private readonly Button _retry = U.Action("Reload", "AutoscanRetry");
    private readonly AdminMonitorAuthority _authority;
    private AdminAutoscanController? _controller;
    private AdminAutoscanSourceEditor? _editor;
    private Panel? _editorHost;
    private ContentDialog? _dialog;
    private bool _active, _syncing, _wide = true;
    private MonitorAuthorityLease? _renderedOwner;
    public AdminAutoscanController? Controller => _controller;
    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _editor is { Dirty: true } && _controller?.IsCurrent(_renderedOwner) == true;

    public AdminAutoscanSourcesPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminAutoscanSourcesPage");
        AutomationProperties.SetName(_scroll, T("page.title"));
        var titles = new StackPanel { Spacing = 8 };
        titles.Children.Add(U.Title(T("page.title"))); titles.Children.Add(U.Text(T("page.subtitle"), 14, true));
        _header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _header.RowDefinitions.Add(new() { Height = GridLength.Auto }); _header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _header.Children.Add(titles);
        _actions.Children.Add(_badge);
        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        toggleRow.Children.Add(U.Text(T("page.switchLabel"), 14, true)); toggleRow.Children.Add(_enabled); _actions.Children.Add(toggleRow); _actions.Children.Add(_run);
        AutomationProperties.SetName(_enabled, T("page.enableAria")); AutomationProperties.SetAutomationId(_enabled, "AutoscanEnabled");
        _header.Children.Add(_actions); _body.Children.Add(_header);
        AutomationProperties.SetAutomationId(_feedback, "AutoscanFeedback"); AutomationProperties.SetLiveSetting(_feedback, AutomationLiveSetting.Polite);
        _body.Children.Add(_feedback); _body.Children.Add(_retry); _body.Children.Add(_loading);
        var toolbar = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto }); toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var plugins = PluginsHint();
        toolbar.Children.Add(plugins); toolbar.Children.Add(_add); Grid.SetColumn(_add, 1);
        toolbar.SizeChanged += (_, args) => { var narrow = args.NewSize.Width < 560; Grid.SetRow(_add, narrow ? 1 : 0); Grid.SetColumn(_add, narrow ? 0 : 1); _add.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right; };
        _body.Children.Add(toolbar); _body.Children.Add(_rows);
        _scrollContent.Children.Add(_body); _scroll.Content = _scrollContent; _root.Children.Add(_scroll); Content = _root;
        _enabled.Toggled += async (_, _) => { var c = _controller; var owner = _renderedOwner; if (!_syncing && c != null && owner != null) await c.SetEnabledAsync(_enabled.IsOn, owner); };
        _run.Click += async (_, _) => { var c = _controller; var owner = _renderedOwner; if (c != null && owner != null) await c.TriggerAsync(owner); };
        _retry.Click += async (_, _) => { if (_controller != null) await _controller.ReloadAsync(); };
        _add.Click += (_, _) => OpenEditor(null);
        SizeChanged += (_, _) => Fit(); _scroll.SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => Activate(); Unloaded += (_, _) => Close();
    }
    private static string T(string key, params (string Name, object? Value)[] values) => AdminAutoscanText.T(key, values);
    /// <summary>The hint with its Plugins link; the link is plain text with a tooltip while no Plugins page is in this build.</summary>
    private static TextBlock PluginsHint()
    {
        var hint = U.Text("", 12, true); var openable = AdminShellPage.CanOpen(new(AdminRoute.Plugins));
        foreach (var (text, tag) in AdminAutoscanText.Rich("sources.pluginsHint"))
        {
            if (tag != "link") { hint.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text }); continue; }
            if (!openable) { hint.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text }); continue; }
            var link = new Microsoft.UI.Xaml.Documents.Hyperlink(); link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text });
            link.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/plugins"); hint.Inlines.Add(link);
        }
        if (!openable) ToolTipService.SetToolTip(hint, T("sources.pluginsUnavailable"));
        return hint;
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }
    private void Activate()
    {
        if (_active) return; _active = true;
        _controller = App.Services.GetRequiredService<AdminAutoscanController>();
        _controller.Changed += QueueRender; _authority.AvailabilityChanged += AuthorityChanged;
        Fit(); _ = _controller.OpenAsync();
    }
    private void AuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        _dialog?.Hide(); CloseEditor(true);
        if (_authority.IsAvailable && _controller != null) _ = _controller.OpenAsync(); else Render();
    });
    private void QueueRender()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() => { if (_active && ReferenceEquals(controller, _controller)) Render(); });
    }
    private void Fit()
    {
        var width = _scroll.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (width > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, width);
        var wide = width >= 672;
        _actions.Orientation = width < 430 ? Orientation.Vertical : Orientation.Horizontal;
        Grid.SetRow(_actions, width < 1000 ? 1 : 0); Grid.SetColumn(_actions, width < 1000 ? 0 : 1);
        Grid.SetColumnSpan(_actions, width < 1000 ? 2 : 1);
        if (_wide != wide) { _wide = wide; Render(); }
    }
    private void Render()
    {
        var c = _controller; if (c == null) return;
        var state = c.State; _renderedOwner = state.Owner;
        var current = c.IsCurrent(state.Owner);
        _syncing = true; _enabled.IsOn = state.Settings?.Enabled == true; _syncing = false;
        var writable = current && state.Loaded && !state.Stale && !state.Loading && !state.Busy && !state.UnknownOutcome;
        _enabled.IsEnabled = writable && state.Settings != null;
        _badge.Text = state.Settings == null ? "" : T(state.Settings.Enabled ? "page.enabled" : "page.disabled");
        _run.Visibility = AdminAutoscanRunNow.IsVisible(state.Settings?.Enabled, state.Loaded && state.SourcesComplete ? state.Sources.Select(s => new AdminAutoscanRunNowSource(s.Enabled, s.DeliveryMode)).ToArray() : null) ? Visibility.Visible : Visibility.Collapsed;
        _run.IsEnabled = writable; _run.Content = T(state.Busy ? "page.triggering" : "page.runNow");
        _add.IsEnabled = writable && state.OptionsReady;
        _loading.Visibility = state.Loading ? Visibility.Visible : Visibility.Collapsed;
        _feedback.Text = string.Join("\n", new[] { state.Error, state.Notice, state.OptionsError, state.Loaded && !state.SourcesComplete ? "Showing at most 1,000 sources. Reload or reduce the configured list before editing." : null }.Where(s => s != null));
        _feedback.Visibility = _feedback.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Recovery is offered whenever a read failed (the page, the source types or servers, the library paths) or the list is stale; no standing button.
        var failed = state.Error != null || state.OptionsError != null || state.Stale;
        _retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        _retry.Content = T("common.tryAgain");
        _retry.IsEnabled = current && !state.Loading && !state.Busy;
        _rows.Children.Clear();
        if (!current) { CloseEditor(true); return; }
        if (!state.Loaded) return;
        if (state.Sources.Count == 0) { var empty = U.Text(T("sources.empty"), 14, true); empty.Margin = new(24); _rows.Children.Add(empty); }
        else
        {
            if (_wide) _rows.Children.Add(Columns([U.Text(T("sources.list.columnSource"), 12, true), U.Text(T("sources.list.columnFeeds"), 12, true), U.Text(T("sources.list.columnStatus"), 12, true), U.Text(T("sources.list.columnOn"), 12, true), U.Text("")], true));
            foreach (var source in state.Sources) AddRow(source, state, writable && state.SourcesComplete, c);
        }
        _editor?.SyncState();
    }
    private Grid Columns(IReadOnlyList<FrameworkElement> children, bool header = false)
    {
        var row = new Grid { ColumnSpacing = 16, RowSpacing = 12, Padding = new(16, header ? 10 : 14, 16, header ? 10 : 14), BorderThickness = new(0, 0, 0, 1), BorderBrush = U.Brush("BorderBrush") };
        foreach (var width in new GridLength[] { new(1.7, GridUnitType.Star), new(0.9, GridUnitType.Star), new(1.3, GridUnitType.Star), new(48), new(148) }) row.ColumnDefinitions.Add(new() { Width = width });
        for (var i = 0; i < children.Count; i++) { Grid.SetColumn(children[i], i); row.Children.Add(children[i]); }
        return row;
    }
    private void AddRow(AdminAutoscanSource source, AdminAutoscanState state, bool writable, AdminAutoscanController c)
    {
        var owner = state.Owner!; var title = P.Title(source, state.Available);
        var identity = new StackPanel { Spacing = 4 }; identity.Children.Add(U.Text(title, 14)); identity.Children.Add(U.Text(P.Subtitle(source, state), 12, true));
        var descriptor = P.Descriptor(state.Available.FirstOrDefault(p => p.PluginId == source.PluginId && p.CapabilityId == source.CapabilityId));
        var targets = P.Targets(source, descriptor, state.Libraries);
        var feeds = U.Text(state.OptionsReady ? P.TargetsText(targets) : T("sources.targets.unknown"), 12, true);
        var status = new StackPanel { Spacing = 4 }; var health = P.Health(source); var healthText = U.Text(health.Text, 13);
        if (health.Error) healthText.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); status.Children.Add(healthText);
        if (source.Enabled && source.DeliveryMode == "webhook" && !source.WebhookConfigured) healthText.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning);
        ToolTipService.SetToolTip(healthText, source.DeliveryMode == "webhook" ? source.WebhookLastErrorAt ?? source.WebhookLastReceivedAt ?? "" : source.LastRunAt ?? "");
        if (health.Error && !(source.ConnectionId == null && AdminAutoscanSourceDraft.ConnectionRequired(descriptor, source.DeliveryMode)))
        { var detail = P.ErrorDetail(source, descriptor); if (detail.Length > 0) { var text = U.Text(detail, 12); text.MaxLines = 2; text.TextTrimming = TextTrimming.CharacterEllipsis; text.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); ToolTipService.SetToolTip(text, detail); status.Children.Add(text); } }
        void Warning(string key) { var warning = U.Text(T(key), 12); warning.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning); status.Children.Add(warning); }
        if (source.ConnectionId == null && AdminAutoscanSourceDraft.ConnectionRequired(descriptor, source.DeliveryMode)) Warning("sources.row.noServer");
        if (state.OptionsReady) { if (targets.Unresolvable) Warning("sources.row.noPaths"); else if (!targets.Unknown && targets.Libraries.Count == 0) Warning("sources.row.pathsUnmatched"); }
        var toggle = new ToggleSwitch { IsOn = source.Enabled, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = writable };
        AutomationProperties.SetName(toggle, T("sources.row.enabledAria", ("title", title))); AutomationProperties.SetAutomationId(toggle, "AutoscanOn_" + source.Id);
        toggle.Toggled += async (_, _) => await c.SetSourceEnabledAsync(source.Id, toggle.IsOn, owner);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (source.DeliveryMode == "webhook" && source.WebhookConfigured)
        { var copy = U.Action(T("webhook.copyButton"), "AutoscanCopy_" + source.Id); copy.IsEnabled = c.SafeWebhookUrl(source.Id, owner) != null; copy.Click += (_, _) => CopyWebhook(source.Id, owner); actions.Children.Add(copy); }
        var edit = U.Action(T("sources.row.edit"), "AutoscanEdit_" + source.Id); edit.IsEnabled = writable && state.OptionsReady; edit.Click += (_, _) => { if (c.IsCurrent(owner)) OpenEditor(source); }; actions.Children.Add(edit);
        var menu = new MenuFlyout();
        if (source.DeliveryMode == "webhook") { var rotate = new MenuFlyoutItem { Text = T("sources.row.rotate"), IsEnabled = writable && source.WebhookConfigured }; rotate.Click += async (_, _) => await RotateAsync(source.Id, owner); menu.Items.Add(rotate); }
        var delete = new MenuFlyoutItem { Text = T("sources.row.delete"), IsEnabled = writable }; delete.Click += async (_, _) => await DeleteAsync(source.Id, owner); menu.Items.Add(delete);
        var more = U.Action("…", "AutoscanMore_" + source.Id); AutomationProperties.SetName(more, T("sources.row.moreAria", ("title", title))); more.Flyout = menu; actions.Children.Add(more);
        FrameworkElement row;
        if (_wide) row = Columns([identity, feeds, status, toggle, actions]);
        else
        {
            var card = new StackPanel { Spacing = 12, Padding = new(16) }; var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            top.Children.Add(identity); Grid.SetColumn(toggle, 1); top.Children.Add(toggle); card.Children.Add(top); card.Children.Add(feeds); card.Children.Add(status); card.Children.Add(actions);
            row = new Border { Child = card, BorderBrush = U.Brush("BorderBrush"), BorderThickness = new(1), CornerRadius = new(8), Margin = new(0, 0, 0, 8) };
        }
        AutomationProperties.SetAutomationId(row, "AutoscanSource_" + source.Id); _rows.Children.Add(row);
    }
    private void OpenEditor(AdminAutoscanSource? source)
    {
        if (_editor != null || _dialog != null || _controller is not { } c || c.State is not { Owner: { } owner, OptionsReady: true, Stale: false, Loading: false, Busy: false } || !c.IsCurrent(owner)) return;
        var editor = new AdminAutoscanSourceEditor(this, c, owner, source);
        _editor = editor; _editorHost = OverlayHost(); Grid.SetRowSpan(editor, Math.Max(1, (_editorHost as Grid)?.RowDefinitions.Count ?? 1)); Grid.SetColumnSpan(editor, Math.Max(1, (_editorHost as Grid)?.ColumnDefinitions.Count ?? 1)); Canvas.SetZIndex(editor, 1000); _editorHost.Children.Add(editor); _scroll.IsEnabled = false;
        editor.CloseRequested += () => CloseEditor(false); editor.FocusFirst();
    }
    private Panel OverlayHost()
    { for (DependencyObject? node = this; node != null; node = VisualTreeHelper.GetParent(node)) if (node is AdminShellPage shell) return shell.MonitorOverlayRoot; return _root; }
    private void CloseEditor(bool force)
    { if (_editor == null || !force && _editor.Busy) return; var editor = _editor; _editor = null; _editorHost?.Children.Remove(editor); _editorHost = null; editor.Detach(); _scroll.IsEnabled = true; if (_add.IsLoaded) _add.Focus(FocusState.Programmatic); }
    internal async Task<bool> ConfirmAsync(string title, string description, string accept, MonitorAuthorityLease owner)
    {
        var c = _controller; if (c == null || !c.IsCurrent(owner) || _dialog != null || XamlRoot == null) return false;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = U.Text(description, 14), PrimaryButtonText = accept, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        AutomationProperties.SetAutomationId(dialog, "AutoscanConfirmation"); _dialog = dialog;
        // A lambda, not the method group: a delegate whose target is the dialog itself is unwrapped as a WinRT object and fails to marshal.
        using var cancel = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && c.IsCurrent(owner); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    /// <summary>Null when nothing was asked of the server (cancelled or no longer current); otherwise the write's outcome for the caller to show.</summary>
    internal async Task<AdminAutoscanResult<object>?> DeleteAsync(string id, MonitorAuthorityLease owner)
    {
        var c = _controller; var source = c?.State.Sources.FirstOrDefault(s => s.Id == id); if (source == null || c == null) return null;
        if (!await ConfirmAsync(T("sources.deleteTitle"), T("sources.deleteDescription", ("name", P.Title(source, c.State.Available))), T("sources.deleteConfirm"), owner)) return null;
        var result = await c.DeleteAsync(id, owner);
        if (result.Succeeded) CloseEditor(true);
        return result;
    }
    internal async Task<AdminAutoscanResult<AdminAutoscanSource>?> RotateAsync(string id, MonitorAuthorityLease owner)
    {
        var c = _controller; if (c == null) return null;
        return await ConfirmAsync(T("webhook.rotate.title"), T("webhook.rotate.description"), T("webhook.rotate.confirm"), owner) ? await c.ConfigureWebhookAsync(id, true, owner) : null;
    }
    internal void CopyWebhook(string id, MonitorAuthorityLease owner)
    {
        if (_controller?.SafeWebhookUrl(id, owner) is not { } url) return;
        try { var package = new Windows.ApplicationModel.DataTransfer.DataPackage(); package.SetText(url); owner.ThrowIfRetired(); if (_controller?.IsCurrent(owner) != true) return; Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); _feedback.Text = T("webhook.copy.copied"); _feedback.Visibility = Visibility.Visible; }
        catch { _feedback.Text = T("webhook.copy.failed"); _feedback.Visibility = Visibility.Visible; }
    }
    public async Task<bool> CanLeaveAsync()
    {
        if (_editor?.Busy == true) return false;
        if (!RequiresLeaveConfirmation || _renderedOwner is not { } owner) return true;
        if (!await ConfirmAsync("Discard source changes?", "Your unsaved source changes will be lost.", "Discard", owner)) return !owner.IsCurrent;
        CloseEditor(true); return true;
    }
    public void SuspendAdminPage() => Close();
    private void Close()
    {
        if (!_active) return; _active = false; _authority.AvailabilityChanged -= AuthorityChanged; _dialog?.Hide(); CloseEditor(true);
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Dispose(); _controller = null; }
        _renderedOwner = null; _rows.Children.Clear(); _feedback.Text = "";
    }
}
