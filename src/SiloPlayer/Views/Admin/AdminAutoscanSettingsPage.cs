using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using U = SiloPlayer.Controls.AdminSettingsUi;
using T = SiloPlayer.Core.Services.Admin.AdminAutoscanSettingsText;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAutoscanSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Grid _scrollContent = new();
    private readonly StackPanel _body = new() { Spacing = 20, MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 112) };
    private readonly StackPanel _alerts = new() { Spacing = 8 };
    private readonly TextBlock _status = U.Text("", 13);
    private readonly Button _reload = U.Action("Reload", "AutoscanSettingsReload");
    private readonly Button _add = U.Action(T.T("connections.add"), "AutoscanConnectionAdd");
    private readonly TextBox _interval = U.Input(T.T("polling.interval"), "AutoscanInterval", number: true);
    private readonly TextBox _debounce = U.Input(T.T("polling.debounce"), "AutoscanDebounce", number: true);
    private readonly AdminSettingsGroup _polling = new(T.T("polling.group"), T.T("polling.description"));
    private readonly AdminSettingsGroup _connections = new(T.T("connections.title"), T.T("connections.description"));
    private readonly AdminSettingsRow _intervalRow;
    private readonly AdminSettingsRow _debounceRow;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly AdminSettingsSaveBar _saveBar = new("AutoscanSettings");
    private AdminAutoscanSettingsController? _controller;
    private AdminMonitorAuthority? _authority;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private PasswordBox? _key;
    private Action<AdminAutoscanSettingsState>? _renderDialog;
    private bool _active, _syncing, _opening;
    public AdminAutoscanSettingsController? Controller => _controller;
    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _controller?.State is { Owner.IsCurrent: true } state && (state.Busy || state.Testing || state.Dirty || state.Editor != null);

    public AdminAutoscanSettingsPage()
    {
        InitializeComponent();
        var root = new Grid(); _body.Children.Add(U.Title(T.T("title")));
        _alerts.Children.Add(_status); _alerts.Children.Add(_reload); _body.Children.Add(_alerts);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _intervalRow = new(T.T("polling.interval"), "", _interval, T.T("seconds"));
        _debounceRow = new(T.T("polling.debounce"), T.T("polling.debounceDescription"), _debounce, T.T("seconds"));
        _polling.Add(_intervalRow); _polling.Add(_debounceRow);
        _body.Children.Add(_polling); _connections.Add(_add); _connections.Add(_rows); _body.Children.Add(_connections);
        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent;
        _scroller.SizeChanged += (_, _) => FitScrollContent();
        root.Children.Add(_scroller); root.Children.Add(_saveBar); Content = root;
        SizeChanged += (_, _) => _saveBar.SetAvailableWidth(ActualWidth);
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
    private void EditDefaultFields()
    {
        if (_syncing) return;
        _controller?.EditDefaults(NumericValue(_interval.Text, 300), NumericValue(_debounce.Text, 0));
    }
    private static string NumericValue(string text, int fallback)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || value == 0) value = fallback;
        return value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
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
        _syncing = true; _interval.Text = current ? state.Interval : ""; _debounce.Text = current ? state.Debounce : ""; _syncing = false;
        _polling.Visibility = current && state.Saved != null ? Visibility.Visible : Visibility.Collapsed;
        var intervalDirty = state.Dirty && state.Interval != state.Saved?.DefaultPollIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var debounceDirty = state.Dirty && state.Debounce != state.Saved?.DebounceSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _intervalRow.State(intervalDirty, false); _debounceRow.State(debounceDirty, false); _polling.State(state.Dirty, T.T("polling.description"));
        _connections.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
        _interval.IsEnabled = _debounce.IsEnabled = state.CanWrite;
        _add.IsEnabled = state.CanWrite && state.ConnectionsLoaded && state.Editor == null && state.DeleteTarget == null;
        _reload.IsEnabled = current && !state.Busy && !state.Testing && !state.Loading;
        var warning = state.RescheduleState is "failed" or "not_configured" ? T.T("reschedule." + state.RescheduleState) : null;
        var message = state.Loading ? T.T("loading") : state.Error ?? state.ConnectionsError ?? state.ServersError ?? warning ?? state.Notice ?? "";
        U.UpdateLiveText(_status, message); _alerts.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _saveBar.State((intervalDirty ? 1 : 0) + (debounceDirty ? 1 : 0), state.Busy || state.Testing, state.CanWrite && state.Editor == null);
        _rows.Children.Clear();
        if (!current) { ClearDialog(); return; }
        if (!state.ConnectionsLoaded) _rows.Children.Add(U.Text(state.Loading ? T.T("loading") : state.ConnectionsError ?? "", 13, true));
        else if (state.Connections.Count == 0) _rows.Children.Add(U.Text(T.T("connections.empty"), 13, true));
        else
        {
            var table = new StackPanel { Spacing = 4, MinWidth = 600 };
            table.Children.Add(ConnectionRow(null, state));
            foreach (var connection in state.Connections) table.Children.Add(ConnectionRow(connection, state));
            _rows.Children.Add(new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }
        _renderDialog?.Invoke(state);
    }
    private Grid ConnectionRow(AdminAutoscanSavedConnection? connection, AdminAutoscanSettingsState state)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new(0, 10, 0, 10) };
        foreach (var width in new[] { 1.2, .7, 1.1, 1.4, .9 }) row.ColumnDefinitions.Add(new() { Width = new(width, GridUnitType.Star) });
        var values = connection == null ? new[] { T.T("name"), T.T("kind"), T.T("source"), T.T("url") }
            : new[] { connection.Name, Kind(connection.Kind), T.T(connection.RequestIntegrationId == null ? "own" : "reuse"), connection.BaseUrl ?? "—" };
        for (var column = 0; column < values.Length; column++)
        {
            var text = U.Text(values[column], 13, connection != null && column == 3); text.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(text, column); row.Children.Add(text);
        }
        if (connection != null)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var edit = U.Action("✎", "AutoscanEdit_" + connection.Id); AutomationProperties.SetName(edit, T.T("edit")); ToolTipService.SetToolTip(edit, T.T("edit"));
            var delete = U.Action("×", "AutoscanDelete_" + connection.Id); AutomationProperties.SetName(delete, T.T("delete")); ToolTipService.SetToolTip(delete, T.T("delete"));
            edit.IsEnabled = delete.IsEnabled = state.CanWrite && state.Editor == null && state.DeleteTarget == null;
            edit.Click += async (_, _) => { _controller?.OpenEditor(connection.Id); await ShowEditorAsync(); };
            delete.Click += async (_, _) => { _controller?.RequestDelete(connection.Id); await ShowDeleteAsync(); };
            actions.Children.Add(edit); actions.Children.Add(delete); Grid.SetColumn(actions, 4); row.Children.Add(actions);
        }
        return row;
    }
    private static string Kind(string kind) => kind == "sonarr" ? "Sonarr" : kind == "radarr" ? "Radarr" : kind;
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
        _renderDialog = null; _dialog?.Hide(); _dialog = null; _key = null;
    }
    private void Close()
    {
        if (!_active) return; _active = false;
        if (_authority != null) _authority.AvailabilityChanged -= AuthorityChanged;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Dispose(); _controller = null; }
        ClearDialog(); _leave = null; _interval.Text = _debounce.Text = ""; _rows.Children.Clear(); _status.Text = "";
        _polling.Visibility = _connections.Visibility = _alerts.Visibility = _saveBar.Visibility = Visibility.Collapsed;
    }
}

