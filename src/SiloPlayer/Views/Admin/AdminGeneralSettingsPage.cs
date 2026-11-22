using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminGeneralSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 32), Visibility = Visibility.Collapsed };
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _errorText = U.Text("", 13);
    private readonly Button _reload = U.Action(T("web.admin.settings.notifications.reload"), "GeneralReload");
    private readonly StackPanel _loading = new() { Spacing = 24 };
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly List<(AdminSettingsGroup Group, string[] Keys)> _groups = [];
    private readonly AdminSettingsSaveBar _saveBar = new();
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly ToggleSwitch _redisToggle = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly PasswordBox _redisUrl;
    private readonly Button _check = U.Action(T("web.admin.common.connectionCheck.check"), "GeneralCheckRedis", secondary: true);
    private readonly TextBlock _checkResult = U.Text("", 14);
    private readonly AdminSettingsGroup _redisGroup;
    private readonly Border _redisUnavailable;
    private readonly Border _checkRow;
    private readonly AdminMonitorAuthority _authority;
    private AdminGeneralSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _syncing, _opening;
    private bool? _redisEnabledOverride;
    private bool _redisShown;
    private int _lastDirtyCount;

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminGeneralSettingsController? Controller => _controller;

    public AdminGeneralSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminGeneralSettingsPage");
        AutomationProperties.SetName(_scroller, T("web.admin.settings.layout.regionLabel", new { label = T("web.admin.settings.general.title") }));
        _header.Children.Add(U.Title(T("web.admin.settings.general.title")));
        var subtitle = U.Text(T("web.admin.settings.search.general.description"), 14, true);
        subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left; _header.Children.Add(subtitle); _body.Children.Add(_header);
        AutomationProperties.SetName(_loading, T("web.admin.settings.fields.loadingSettings"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(Skeleton(32, 192));
        for (var group = 0; group < 2; group++)
        {
            var block = new StackPanel { Spacing = 16 };
            block.Children.Add(Skeleton(40)); block.Children.Add(Skeleton(40)); _loading.Children.Add(block);
        }
        AutomationProperties.SetAutomationId(_errorText, "GeneralErrorText");
        _alerts.Children.Add(Alert(_errorText)); _alerts.Children.Add(_reload);
        _reload.Click += async (_, _) =>
        {
            if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true);
        };
        _body.Children.Add(_loading); _body.Children.Add(_alerts); _body.Children.Add(_fields);
        var identity = Group("identity", ["branding.server_name", "branding.login_subtitle"]);
        AddText(identity, "branding.server_name", "identity.serverName", hint: "Silo");
        AddText(identity, "branding.login_subtitle", "identity.loginSubtitle", T("web.admin.settings.general.identity.loginSubtitle.description"), T("web.admin.settings.general.identity.loginSubtitle.placeholder"));
        var network = Group("network", ["server.public_url", "server.lan_discovery", "server.local_url", "clientip.trusted_proxies"]);
        AddText(network, "server.public_url", "network.publicUrl", T("web.admin.settings.general.network.publicUrl.description"), "https://silo.example.com");
        var lan = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        Add(network, "server.lan_discovery", "network.lanDiscovery", lan, T("web.admin.settings.general.network.lanDiscovery.description"));
        lan.Toggled += (_, _) => Set("server.lan_discovery", lan.IsOn ? "true" : "false");
        AddText(network, "server.local_url", "network.localUrl", T("web.admin.settings.general.network.localUrl.description"), "http://192.168.1.20:8080");
        AddText(network, "clientip.trusted_proxies", "network.trustedProxies", T("web.admin.settings.general.network.trustedProxies.description"), "172.16.0.0/12, 203.0.113.7/32");
        var database = Group("database", ["database.max_connections"]);
        AddText(database, "database.max_connections", "database.maxConnections", T("web.admin.settings.general.database.maxConnections.description"), number: true);
        _redisGroup = new("Redis"); _groups.Add((_redisGroup, ["redis.url", "redis.db"])); _fields.Children.Add(_redisGroup);
        AutomationProperties.SetName(_redisToggle, T("web.admin.settings.redis.enabled.label"));
        AutomationProperties.SetAutomationId(_redisToggle, "GeneralRedisEnabled");
        var enabledRow = new AdminSettingsRow(T("web.admin.settings.redis.enabled.label"), T("web.admin.settings.redis.enabled.description"), _redisToggle);
        _rows["redis.enabled"] = enabledRow; _redisGroup.Add(enabledRow);
        _redisToggle.Toggled += (_, _) =>
        {
            if (_syncing || _controller?.State is not { Loaded: true, Saving: false }) return;
            // A switch reports its state again after it is realized or set in code; only a state that differs from the one shown is the admin's choice.
            if (_redisToggle.IsOn == _redisShown || !_redisToggle.IsEnabled) return;
            _redisEnabledOverride = _redisToggle.IsOn;
            // The state shown is now the one the admin chose, so a second click before the queued render (Off, then On) is a change from this one, not an echo.
            _redisShown = _redisToggle.IsOn;
            if (_redisToggle.IsOn) _controller.EnableRedis(); else _controller.DisableRedis();
        };
        _redisUrl = U.Secret(T("web.admin.settings.redis.url.label"), "General_redis.url");
        var redisUrlRow = new AdminSettingsRow(T("web.admin.settings.redis.url.label"), "redis://host:6379", _redisUrl);
        _rows["redis.url"] = redisUrlRow; _controls["redis.url"] = _redisUrl; _redisGroup.Add(redisUrlRow);
        _redisUrl.PasswordChanged += (_, _) =>
        {
            if (_syncing || _controller?.State is not { Loaded: true, Saving: false } state) return;
            // PasswordChanged is delivered asynchronously for programmatic updates too.
            // A model echo must not withdraw a newly staged clear or replacement.
            if (_redisUrl.Password == state.Values.GetValueOrDefault("redis.url", "")) return;
            if (_redisUrl.Password.Length == 0 && state.Configured.Contains("redis.url")) _controller.KeepSavedRedisUrl();
            else _controller.StageRedisUrl(_redisUrl.Password);
        };
        var redisDb = U.Input(T("web.admin.settings.redis.db.label"), "General_redis.db", number: true);
        var redisDbRow = new AdminSettingsRow(T("web.admin.settings.redis.db.label"), T("web.admin.settings.redis.db.description"), redisDb);
        _rows["redis.db"] = redisDbRow; _controls["redis.db"] = redisDb; _redisGroup.Add(redisDbRow);
        redisDb.TextChanging += (_, _) => Set("redis.db", redisDb.Text);
        var checkContent = new AdminSettingsActionContent(_check, _checkResult);
        _check.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetLiveSetting(_checkResult, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_checkResult, "GeneralRedisResult");
        _checkRow = new Border { Child = checkContent, Padding = new(0, 14, 0, 14) }; _redisGroup.Add(_checkRow);
        _check.Click += async (_, _) => { if (_controller != null) await _controller.CheckRedisAsync(); };
        _redisUnavailable = Alert(T("web.admin.settings.redis.unavailable.title"), T("web.admin.settings.redis.unavailable.description"));
        _fields.Children.Add(_redisUnavailable); _body.Children.Add(_spacer);
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
        _saveBar.Save.Click += async (_, _) => { if (_controller != null) await _controller.SaveAsync(); };
        _saveBar.Discard.Click += (_, _) => { _redisEnabledOverride = null; _controller?.Discard(); };
        Loaded += (_, _) => { FitScrollContent(); Activate(); }; Unloaded += (_, _) => Close();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); _saveBar.Margin = new(16, 0, 16, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? -8 : 8); };
    }

    private void FitScrollContent()
    {
        // A capped child gives ScrollContentPresenter a narrower desired width.
        // Give the outer frame its viewport width so the 1120px settings column
        // starts at the common gutter rather than centering on wide windows.
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    private static string T(string key, object? args = null) => AdminText.T(key, args);
    private static Border Skeleton(double height, double width = double.NaN) => U.Skeleton(height, width);
    private static Border Alert(string message, string? description = null) => U.Alert(message, description);
    private static Border Alert(TextBlock text, string? description = null) => U.Alert(text, description);

    private AdminSettingsGroup Group(string section, string[] keys)
    {
        var group = new AdminSettingsGroup(T($"web.admin.settings.general.{section}.group"));
        _groups.Add((group, keys)); _fields.Children.Add(group); return group;
    }
    private void AddText(AdminSettingsGroup group, string key, string labelKey, string description = "", string hint = "", bool number = false)
    {
        var input = U.Input(T($"web.admin.settings.general.{labelKey}.label"), "General_" + key, hint, number);
        Add(group, key, labelKey, input, description);
        input.TextChanging += (_, _) => Set(key, input.Text);
    }
    private void Add(AdminSettingsGroup group, string key, string labelKey, Control input, string description)
    {
        var label = T($"web.admin.settings.general.{labelKey}.label");
        AutomationProperties.SetAutomationId(input, "General_" + key);
        var row = new AdminSettingsRow(label, description, input);
        _controls[key] = input; _rows[key] = row; group.Add(row);
    }
    private void Set(string key, string value)
    {
        if (!_syncing && _controller?.State is { Loaded: true } state && state.Values.GetValueOrDefault(key, "") != value) _controller.Set(key, value);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _location = e.Parameter as AdminRouteRequest;
        _restoreOffset = e.NavigationMode == NavigationMode.Back && _location != null && ScrollPositions.TryGetValue(_location, out var position) ? position.Offset : 0;
        base.OnNavigatedTo(e);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }
    private void Activate()
    {
        if (_active) return;
        _active = true; _controller = App.Services.GetRequiredService<AdminGeneralSettingsController>();
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.DirtyKeys.Count > 0, () => _controller?.Discard());
        _controller.Changed += QueueRender; _controller.Saved += OnSaved;
        _authority.AvailabilityChanged += OnAuthorityChanged; _ = OpenAsync();
    }
    private async Task OpenAsync()
    {
        if (_opening || !_active || _controller == null || !_authority.IsAvailable) return;
        _opening = true;
        try { await _controller.OpenAsync(); }
        finally
        {
            _opening = false;
            if (_active && _authority.IsAvailable && _controller?.State.Owner == null)
                DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); });
        }
    }
    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() => { if (_active && _controller?.State.Owner == null) _ = OpenAsync(); });
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state); });
    }
    private void Render(AdminGeneralSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        var accessBlocked = state.Error != null && state.Failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
            or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;
        _loading.Visibility = state.Loading ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = state.Loading ? Visibility.Collapsed : Visibility.Visible;
        _fields.Visibility = state.Loaded && !state.Loading && current && !accessBlocked ? Visibility.Visible : Visibility.Collapsed;
        // Retain the live region and Reload control while the retry is in flight.
        // A pending retry ignores duplicate activation without disabling keyboard focus.
        _alerts.Visibility = current && (state.Error != null || state.Loading && _alerts.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
        _reload.IsEnabled = !state.Saving && current;
        _reload.IsHitTestVisible = !state.Loading;
        if (state.Error != null) U.UpdateLiveText(_errorText, ErrorMessage(state));
        if (state.DirtyKeys.Count != _lastDirtyCount)
        {
            _lastDirtyCount = state.DirtyKeys.Count;
            if (_lastDirtyCount == 0) _redisEnabledOverride = null;
        }
        bool Dirty(string key) => state.DirtyKeys.Contains(key);
        bool Managed(string key) => state.ManagedByEnv.Contains(key);
        bool StatusUnknown(string key) => state.SensitiveStatusError && AdminGeneralSettingsController.SensitiveStatusKeys.Contains(key);
        var redisManaged = Managed("redis.url") || Managed("redis.db");
        // Once the environment sets the connection the switch is locked, so a choice made before it took over no longer applies,
        // whatever else is still staged (official RedisGroup.tsx: `if (urlEnv && enabledOverride !== null) setEnabledOverride(null)`).
        if (redisManaged) _redisEnabledOverride = null;
        var redisEnabled = _redisEnabledOverride ?? (state.Configured.Contains("redis.url") || !string.IsNullOrWhiteSpace(state.Values.GetValueOrDefault("redis.url", "")));
        _syncing = true;
        try
        {
            foreach (var (key, control) in _controls)
            {
                var value = current ? state.Values.GetValueOrDefault(key, "") : "";
                if (control is TextBox text && text.Text != value) text.Text = value;
                else if (control is PasswordBox secret && secret.Password != value) secret.Password = value;
                else if (control is ToggleSwitch toggle) toggle.IsOn = value == "true";
                control.IsEnabled = current && state.Loaded && !state.Loading && !accessBlocked && !Managed(key) && !StatusUnknown(key) && (!key.StartsWith("redis.", StringComparison.Ordinal) || (!state.Saving && !redisManaged && !state.SensitiveStatusError));
            }
            _redisToggle.IsOn = redisEnabled; _redisShown = redisEnabled;
            _redisToggle.IsEnabled = current && state.Loaded && !state.Loading && !accessBlocked && !state.Saving && !redisManaged && !state.SensitiveStatusError;
        }
        finally { _syncing = false; }
        _redisUrl.PlaceholderText = state.Configured.Contains("redis.url") ? "••••••••••••" : T("web.settings.secretField.notConfigured");
        _rows["clientip.trusted_proxies"].Description.Text = T(StatusUnknown("clientip.trusted_proxies") ? "configure.native.settings.managedStatusUnavailable"
            : "web.admin.settings.general.network.trustedProxies.description");
        // Every row keeps its own description; a setting the server's environment sets adds the official note naming the variable.
        // The Redis switch is set by the same variable as the connection URL.
        foreach (var (key, row) in _rows) row.SetEnvironmentNote(state.EnvManaged.Note(key == "redis.enabled" ? "redis.url" : key));
        foreach (var (group, keys) in _groups)
        {
            var allRestart = keys.All(state.RestartKeys.Has);
            group.State(keys.Any(Dirty), allRestart ? T("web.admin.settings.fields.restartAll") : "");
            foreach (var key in keys) _rows[key].State(key != "redis.url" && Dirty(key), !allRestart && state.RestartKeys.Has(key));
        }
        _rows["redis.enabled"].State(false, state.RestartKeys.Has("redis.url") && !new[] { "redis.url", "redis.db" }.All(state.RestartKeys.Has));
        foreach (var key in new[] { "redis.url", "redis.db" }) _rows[key].Visibility = redisEnabled ? Visibility.Visible : Visibility.Collapsed;
        _checkRow.Visibility = redisEnabled ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (group, _) in _groups) group.RefreshRules();
        _redisGroup.Visibility = state.SensitiveStatusError ? Visibility.Collapsed : Visibility.Visible;
        _redisUnavailable.Visibility = state.SensitiveStatusError ? Visibility.Visible : Visibility.Collapsed;
        _check.Content = T(state.Checking ? "web.admin.common.connectionCheck.checking" : "web.admin.common.connectionCheck.check");
        AutomationProperties.SetName(_check, _check.Content.ToString());
        _check.IsEnabled = current && state.Loaded && !state.Loading && !accessBlocked && !state.Saving && !state.Checking && !state.SensitiveStatusError;
        U.UpdateLiveText(_checkResult, state.ConnectionResult?.Message ?? "");
        _checkResult.Foreground = AdminMonitorUi.Solid(state.ConnectionResult?.Success == true
            ? Windows.UI.Color.FromArgb(255, 0x16, 0xA3, 0x4A) : Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26));
        foreach (var row in _rows.Values) row.RefreshHelp();
        // An emptied Redis database number is the one draft the server still accepts under REDIS_URL, so it can still be saved.
        var writable = state.DirtyKeys.Any(key => (!Managed(key) || key == "redis.db" && state.Values.GetValueOrDefault(key, "").Trim().Length == 0)
            && !StatusUnknown(key) && (!key.StartsWith("redis.", StringComparison.Ordinal) || !redisManaged || key == "redis.db" && state.Values.GetValueOrDefault(key, "").Trim().Length == 0));
        _saveBar.State(current ? state.DirtyKeys.Count : 0, state.Saving, current && writable && !state.Loading && !accessBlocked && !state.Conflict && !state.OutcomeUnknown);
        _saveBar.SetAvailableWidth(ActualWidth);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;
        if (state.Loaded && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); _scroller.Focus(FocusState.Programmatic); } });
        }
    }
    private static string ErrorMessage(AdminGeneralSettingsState state)
    {
        if (state.Conflict || state.OutcomeUnknown) return state.Error!;
        var key = state.Failure switch
        {
            AdminFailureKind.Unauthorized => "authenticationRequired",
            AdminFailureKind.Forbidden or AdminFailureKind.DemoRestricted => "forbidden",
            AdminFailureKind.ProfileVerificationRequired => "profileVerification",
            AdminFailureKind.CapabilityUnavailable => "capabilityUnavailable",
            AdminFailureKind.RateLimited => "rateLimited",
            AdminFailureKind.Validation => "validationFailed",
            AdminFailureKind.InvalidResponse => "invalidResponse",
            AdminFailureKind.Transport or AdminFailureKind.Cancelled => "transportFailed",
            AdminFailureKind.Server => "serverFailed",
            _ => null,
        };
        return key == null ? state.Error! : T("configure.native.settings." + key);
    }
    private void OnSaved(IReadOnlyCollection<string> keys)
    {
        var controller = _controller; var state = controller?.State;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || controller == null || !ReferenceEquals(controller, _controller) || state?.Owner?.IsCurrent != true) return;
            if (keys.Contains("branding.server_name"))
            {
                DocumentTitle.SetServerName(state.PersistedValues.GetValueOrDefault("branding.server_name", "Silo"));
                if (_location != null) App.MainWindowInstance?.SetDynamicTitle(AdminNavigation.TabTitle(_location));
            }
            if (App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell) shell.RefreshSettingsStatus();
        });
    }
    public Task<bool> CanLeaveAsync()
    {
        if (_controller?.State is { Saving: true, Owner.IsCurrent: true }) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }

    public void SuspendAdminPage() => Close();
    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetValue(_location, static _ => new ScrollPosition()).Offset = _scroller.VerticalOffset;
        _authority.AvailabilityChanged -= OnAuthorityChanged; _dialog?.Hide(); _dialog = null;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Dispose(); _controller = null; }
        _leave = null; _redisEnabledOverride = null; _lastDirtyCount = 0;
        _syncing = true;
        foreach (var control in _controls.Values) { control.IsEnabled = false; if (control is TextBox text) text.Text = ""; else if (control is PasswordBox secret) secret.Password = ""; else if (control is ToggleSwitch toggle) toggle.IsOn = false; }
        _redisToggle.IsOn = false; _redisToggle.IsEnabled = false;
        _syncing = false; _alerts.Children.Clear(); _checkResult.Text = "";
        _fields.Visibility = _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
