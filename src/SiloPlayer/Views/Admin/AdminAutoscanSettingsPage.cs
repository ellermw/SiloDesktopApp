using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
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
using SiloPlayer.Helpers;
using U = SiloPlayer.Controls.AdminSettingsUi;
using T = SiloPlayer.Core.Services.Admin.AdminAutoscanSettingsText;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Manage › Autoscan › Settings (official AutoscanSettings.tsx and ConnectionsPanel.tsx at Silo-Server/silo-server
/// 66b836f82): the two polling defaults saved through one guarded save bar, and the saved server connections with
/// their add/edit/test/delete dialogs. The global enable switch, the scan trigger, Sources and Activity belong to other pages.
/// </summary>
public sealed partial class AdminAutoscanSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 32) };
    private readonly StackPanel _loading = new() { Spacing = 16 };
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _errorText = U.Text("", 13);
    private readonly TextBlock _notice = U.Text("", 13);
    private readonly Button _reload = U.Action(T.T("reload"), "AutoscanSettingsReload");
    private readonly StackPanel _fields = new() { Spacing = 20, Visibility = Visibility.Collapsed };
    private readonly Button _add = AdminLibrariesUi.Action(T.T("connections.add"), "AutoscanConnectionAdd", "plus");
    private readonly TextBox _interval = U.Input(T.T("polling.interval"), "AutoscanInterval", number: true);
    private readonly TextBox _debounce = U.Input(T.T("polling.debounce"), "AutoscanDebounce", number: true);
    private readonly AdminSettingsGroup _polling = new(T.T("polling.group"), T.T("polling.description"));
    private readonly AdminSettingsGroup _connections = new(T.T("connections.title"), T.T("connections.description"));
    private readonly AdminSettingsRow _intervalRow;
    private readonly AdminSettingsRow _debounceRow;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly AdminSettingsSaveBar _saveBar = new("AutoscanSettings");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private AdminAutoscanSettingsController? _controller;
    private AdminMonitorAuthority? _authority;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private Action<AdminAutoscanSettingsState>? _renderDialog;
    private PasswordBox? _key;
    private bool _active, _syncing, _opening;
    public AdminAutoscanSettingsController? Controller => _controller;
    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _controller?.State is { Owner.IsCurrent: true } state && (state.Busy || state.Testing || state.Dirty || state.Editor != null);

    public AdminAutoscanSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminAutoscanSettingsPage");
        AutomationProperties.SetName(_scroller, AdminText.T("web.admin.settings.layout.regionLabel", new { label = T.T("title") }));
        _header.Children.Add(U.Title(T.T("title")));
        var subtitle = U.Text("How often Autoscan checks polled sources, and the servers its sources connect to.", 14, true);
        subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left; _header.Children.Add(subtitle);
        _body.Children.Add(_header);

        AutomationProperties.SetName(_loading, T.T("loadingSettings"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Visibility = Visibility.Collapsed;
        _loading.Children.Add(U.Skeleton(32, 192)); _loading.Children.Add(U.Skeleton(40)); _loading.Children.Add(U.Skeleton(40));
        _body.Children.Add(_loading);

        AutomationProperties.SetAutomationId(_errorText, "AutoscanSettingsError");
        _alerts.Children.Add(U.Alert(_errorText)); _alerts.Children.Add(_reload);
        _body.Children.Add(_alerts);
        AutomationProperties.SetAutomationId(_notice, "AutoscanSettingsNotice");
        AutomationProperties.SetLiveSetting(_notice, AutomationLiveSetting.Polite);
        _notice.Margin = new(0, 0, 0, 16); _notice.Visibility = Visibility.Collapsed; _body.Children.Add(_notice);

        _intervalRow = new(T.T("polling.interval"), "", _interval, T.T("seconds"));
        _debounceRow = new(T.T("polling.debounce"), T.T("polling.debounceDescription"), _debounce, T.T("seconds"));
        _polling.Add(_intervalRow); _polling.Add(_debounceRow);
        _fields.Children.Add(_polling);
        _connections.SetAction(_add); _connections.Add(_rows); _fields.Children.Add(_connections);
        _body.Children.Add(_fields); _body.Children.Add(_spacer);
        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent; _root.Children.Add(_scroller);
        _scroller.SizeChanged += (_, _) => FitScrollContent();
        var background = AdminMonitorUi.ColorOf("AppBackgroundBrush", Colors.Black);
        _scrim.Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0), EndPoint = new(0, 1), GradientStops =
            {
                new GradientStop { Color = AdminMonitorUi.WithAlpha(background, 0), Offset = 0 },
                new GradientStop { Color = background, Offset = 1 },
            },
        };
        _root.Children.Add(_scrim); _root.Children.Add(_saveBar); Content = _root;
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); _saveBar.Margin = new(16, 0, 16, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? -8 : 8); };
        _interval.TextChanging += (_, _) => EditDefaultFields();
        _debounce.TextChanging += (_, _) => EditDefaultFields();
        _saveBar.Save.Click += async (_, _) => { if (_controller != null) await _controller.SaveDefaultsAsync(); };
        _saveBar.Discard.Click += (_, _) => _controller?.Discard();
        _reload.Click += async (_, _) => await ReconcileAsync();
        _add.Click += async (_, _) => { _controller?.OpenEditor(); await ShowEditorAsync(); };
        Loaded += (_, _) => { FitScrollContent(); Activate(); }; Unloaded += (_, _) => Close();
    }

    private void FitScrollContent()
    {
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    /// <summary>Official <c>Number(value) || fallback</c>: an empty or non-numeric interval reads 300, an empty debounce 0.</summary>
    private void EditDefaultFields()
    {
        if (_syncing) return;
        _controller?.EditDefaults(AdminAutoscanNumber.Coerce(_interval.Text, 300), AdminAutoscanNumber.Coerce(_debounce.Text, 0));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args) { Close(); base.OnNavigatedFrom(args); }
    private void Activate()
    {
        if (_active) return; _active = true;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        _controller = App.Services.GetRequiredService<AdminAutoscanSettingsController>();
        _leave = new(() => _controller?.State.Owner, () => _controller?.State is { } state && (state.Dirty || state.Editor != null), () => { _controller?.Discard(); _controller?.CloseEditor(); });
        _controller.Changed += QueueRender; _authority.AvailabilityChanged += AuthorityChanged; _ = OpenAsync();
    }
    private async Task OpenAsync()
    {
        if (!_active || _opening || _controller == null || _authority?.IsAvailable != true) return;
        _opening = true;
        try { await _controller.OpenAsync(); }
        finally
        {
            _opening = false;
            if (_active && _authority?.IsAvailable == true && _controller?.State.Owner == null) DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); });
        }
    }
    private void AuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        if (_controller?.State.Owner == null) { ClearDialog(); Render(new()); _ = OpenAsync(); }
    });
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return; var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state); });
    }

    private void Render(AdminAutoscanSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        // Official: the whole page is a skeleton while the settings read is pending; the connections list has its own loading line.
        var pending = current && state.Loading && state.Saved == null && state.SettingsError == null;
        _loading.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
        _fields.Visibility = current && !pending ? Visibility.Visible : Visibility.Collapsed;
        _syncing = true;
        var interval = current ? state.Interval : ""; var debounce = current ? state.Debounce : "";
        if (_interval.Text != interval) _interval.Text = interval;
        if (_debounce.Text != debounce) _debounce.Text = debounce;
        _syncing = false;
        _polling.Visibility = current && state.Saved != null ? Visibility.Visible : Visibility.Collapsed;
        var saved = state.Saved;
        var intervalDirty = saved != null && state.Interval != saved.DefaultPollIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var debounceDirty = saved != null && state.Debounce != saved.DebounceSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _intervalRow.State(intervalDirty, false); _debounceRow.State(debounceDirty, false); _polling.State(intervalDirty || debounceDirty, T.T("polling.description"));
        _connections.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
        _interval.IsEnabled = _debounce.IsEnabled = state.CanWrite;
        _add.IsEnabled = state.CanWrite && state.ConnectionsLoaded && state.Editor == null && state.DeleteTarget == null;

        // Failed reads and write results stay visible and recoverable; a page-level error never hides another one.
        var dialogOpen = state.Editor != null || state.DeleteTarget != null;
        var errors = new List<string>();
        foreach (var message in new[] { state.SettingsError, state.ConnectionsError, state.ServersError, dialogOpen ? null : state.Error })
            if (!string.IsNullOrEmpty(message) && !errors.Contains(message)) errors.Add(message);
        var recovery = current && (errors.Count > 0 || state.NeedsReview);
        _alerts.Visibility = recovery && errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (errors.Count > 0) U.UpdateLiveText(_errorText, string.Join("\n", errors));
        _reload.IsEnabled = current && !state.Busy && !state.Testing && !state.Loading;
        var warning = state.RescheduleState is "failed" or "not_configured" ? T.T("reschedule." + state.RescheduleState) : null;
        var notice = current ? warning ?? (dialogOpen ? null : state.Notice) ?? "" : "";
        U.UpdateLiveText(_notice, notice);
        _notice.Foreground = AdminMonitorUi.Solid(warning != null ? AdminMonitorUi.Amber500 : Emerald500);
        _notice.Visibility = notice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _saveBar.State(current ? (intervalDirty ? 1 : 0) + (debounceDirty ? 1 : 0) : 0, state.Busy || state.Testing, state.CanWrite && state.Editor == null);
        _saveBar.SetAvailableWidth(ActualWidth);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;

        _rows.Children.Clear();
        if (!current) { ClearDialog(); return; }
        if (!state.ConnectionsLoaded)
        {
            var line = U.Text(state.ConnectionsError != null ? "" : T.T("loading"), 13, true); line.Margin = new(0, 14, 0, 14);
            AutomationProperties.SetAutomationId(line, "AutoscanConnectionsState"); _rows.Children.Add(line);
        }
        else if (state.Connections.Count == 0)
        {
            var line = U.Text(T.T("connections.empty"), 13, true); line.Margin = new(0, 14, 0, 14);
            AutomationProperties.SetAutomationId(line, "AutoscanConnectionsState"); _rows.Children.Add(line);
        }
        else
        {
            var table = new StackPanel { MinWidth = 600 };
            table.Children.Add(ConnectionRow(null, state));
            foreach (var connection in state.Connections) table.Children.Add(ConnectionRow(connection, state));
            _rows.Children.Add(new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }
        _renderDialog?.Invoke(state);
    }

    private static readonly Windows.UI.Color Emerald500 = Windows.UI.Color.FromArgb(255, 0x10, 0xB9, 0x81);

    /// <summary>One table row (official shadcn Table): Name, Kind, Source badge, URL and the edit/delete actions.</summary>
    private Grid ConnectionRow(AdminAutoscanSavedConnection? connection, AdminAutoscanSettingsState state)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new(0, 10, 0, 10), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6), BorderThickness = new(0, 0, 0, 1) };
        foreach (var width in new[] { 1.2, .7, 1.1, 1.4, .9 }) row.ColumnDefinitions.Add(new() { Width = new(width, GridUnitType.Star) });
        FrameworkElement[] cells;
        if (connection == null)
        {
            cells = [U.Text(T.T("name"), 13, true), U.Text(T.T("kind"), 13, true), U.Text(T.T("source"), 13, true), U.Text(T.T("url"), 13, true)];
            foreach (var cell in cells.OfType<TextBlock>()) cell.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        }
        else
        {
            var name = U.Text(connection.Name, 13); name.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
            var badge = AdminLibrariesUi.Badge(T.T(connection.RequestIntegrationId == null ? "own" : "reuse"),
                connection.RequestIntegrationId == null ? AdminLibrariesUi.BadgeTone.Outline : AdminLibrariesUi.BadgeTone.Secondary);
            badge.VerticalAlignment = VerticalAlignment.Center;
            cells = [name, U.Text(T.KindLabel(connection.Kind), 13), badge,
                U.Text(connection.BaseUrl is { Length: > 0 } url ? AdminAutoscanConnectionEditor.DisplayUrl(url) : "—", 13, true)];
        }
        for (var column = 0; column < cells.Length; column++)
        {
            if (cells[column] is TextBlock text) { text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap; }
            Grid.SetColumn(cells[column], column); row.Children.Add(cells[column]);
        }
        if (connection != null)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var edit = AdminLibrariesUi.IconButton("pencil", T.T("edit"), "AutoscanEdit_" + connection.Id, 28, 14);
            var delete = AdminLibrariesUi.IconButton("trash-2", T.T("delete"), "AutoscanDelete_" + connection.Id, 28, 14, AdminLibrariesUi.Solid(AdminLibrariesUi.Destructive));
            edit.IsEnabled = delete.IsEnabled = state.CanWrite && state.Editor == null && state.DeleteTarget == null;
            edit.Click += async (_, _) => { _controller?.OpenEditor(connection.Id); await ShowEditorAsync(); };
            delete.Click += async (_, _) => { _controller?.RequestDelete(connection.Id); await ShowDeleteAsync(); };
            actions.Children.Add(edit); actions.Children.Add(delete); Grid.SetColumn(actions, 4); row.Children.Add(actions);
        }
        return row;
    }

    private async Task ReconcileAsync()
    {
        if (_controller == null || _controller.State.Busy || _controller.State.Testing) return;
        ClearDialog(); _controller.CloseEditor(); await _controller.ReloadAsync();
    }
    public Task<bool> CanLeaveAsync()
    {
        if (_controller?.State is { Owner.IsCurrent: true } state && (state.Busy || state.Testing)) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }
    public void SuspendAdminPage() => Close();
    private void ClearDialog()
    {
        if (_key != null) _key.Password = "";
        var dialog = _dialog;
        _renderDialog = null; _dialog = null; _key = null;
        dialog?.Hide();
    }
    private void Close()
    {
        if (!_active) return; _active = false;
        if (_authority != null) _authority.AvailabilityChanged -= AuthorityChanged;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Dispose(); _controller = null; }
        ClearDialog(); _leave = null;
        _syncing = true; _interval.Text = _debounce.Text = ""; _syncing = false;
        _rows.Children.Clear(); _errorText.Text = ""; _notice.Text = "";
        _polling.Visibility = _connections.Visibility = _alerts.Visibility = _notice.Visibility = _loading.Visibility = Visibility.Collapsed;
        _fields.Visibility = _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
