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
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using F = SiloPlayer.Core.Models.Admin.AdminRateLimitFields;
using U = SiloPlayer.Controls.AdminSettingsUi;
using C = SiloPlayer.Core.Services.Admin.AdminSecuritySettingsController;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure > Sign-in &amp; Security > Security (official SecurityAccessSettings.tsx): the Local network
/// switch through the shared settings form, and Rate limiting through its own resource behind the same
/// save bar. Rate-limit rows carry no restart chips or dirty dots, as in the official page.
/// </summary>
public sealed partial class AdminSecuritySettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
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
    private readonly Button _reload = U.Action(T("web.admin.settings.notifications.reload"), "SecurityReload");
    private readonly StackPanel _loading = new() { Spacing = 24 };
    private readonly AdminSettingsGroup _localGroup = new(T("web.admin.settings.security.localNetwork.group"));
    private readonly ToggleSwitch _local = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly AdminSettingsRow _localRow;
    private readonly Border _warning = U.Warning(T("web.admin.settings.security.localNetwork.warning"), "SecurityLocalNetworkWarning");
    private readonly AdminSettingsGroup _rateGroup = new(T("web.admin.settings.security.rateLimit.group"));
    private readonly StackPanel _rateAlert = new() { Spacing = 8, Padding = new(0, 14, 0, 14), Visibility = Visibility.Collapsed };
    private readonly TextBlock _rateErrorText = U.Text("", 13);
    private readonly Button _rateReload = U.Action(T("web.admin.settings.notifications.reload"), "SecurityRateReload");
    private readonly TextBlock _rateNotice = U.Text("", 13, true);
    private readonly List<UIElement> _rateRows = [];
    private readonly ToggleSwitch _enabled = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly ComboBox _backend;
    private readonly ComboBox _quotaMode;
    private readonly AdminSettingsRow _backendRow;
    private readonly Grid _notRunning = U.Status(T("web.admin.settings.security.rateLimit.notRunning"), "SecurityNotRunning");
    private readonly Grid _drift = U.Status("", "SecurityBackendDrift");
    private readonly Dictionary<string, TextBox> _numbers = new(StringComparer.Ordinal);
    private readonly HashSet<TextBox> _composing = [];
    private readonly AdminSettingsSaveBar _saveBar = new("Security");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly AdminMonitorAuthority _authority;
    private AdminSecuritySettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _syncing, _opening;

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminSecuritySettingsController? Controller => _controller;

    public AdminSecuritySettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminSecuritySettingsPage");
        AutomationProperties.SetName(_scroller, T("web.admin.settings.layout.regionLabel", new { label = T("web.admin.settings.security.title") }));
        _header.Children.Add(U.Title(T("web.admin.settings.security.title")));
        var subtitle = U.Text(T("web.admin.settings.search.security.description"), 14, true);
        subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left; _header.Children.Add(subtitle); _body.Children.Add(_header);
        // Official loading state: a 32x192 title bar and two blocks of two 40px bars.
        AutomationProperties.SetName(_loading, T("web.admin.settings.security.loading"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(U.Skeleton(32, 192));
        for (var block = 0; block < 2; block++)
        {
            var bars = new StackPanel { Spacing = 16 };
            bars.Children.Add(U.Skeleton(40)); bars.Children.Add(U.Skeleton(40));
            _loading.Children.Add(bars);
        }
        AutomationProperties.SetAutomationId(_errorText, "SecurityErrorText");
        _alerts.Children.Add(U.Alert(_errorText)); _alerts.Children.Add(_reload);
        _reload.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true); };
        _body.Children.Add(_loading); _body.Children.Add(_alerts); _body.Children.Add(_fields);
        _fields.Children.Add(_localGroup); _fields.Children.Add(_rateGroup);

        // Local network: one shared batched key.
        _localRow = new AdminSettingsRow(T("web.admin.settings.security.localNetwork.label"), T("web.admin.settings.security.localNetwork.description"), _local);
        AutomationProperties.SetAutomationId(_local, "Security_" + C.LocalNetwork);
        AutomationProperties.SetLiveSetting(_localRow.Description, AutomationLiveSetting.Polite);
        _local.Toggled += (_, _) => { if (!_syncing && _controller?.State is { Loaded: true }) _controller.SetLocalNetwork(_local.IsOn); };
        _localGroup.Add(_localRow); _localGroup.Add(_warning);

        // Rate limiting: its own draft, never sent through the batched writer.
        AutomationProperties.SetAutomationId(_rateErrorText, "SecurityRateErrorText");
        AutomationProperties.SetAutomationId(_rateNotice, "SecurityRateNotice");
        AutomationProperties.SetLiveSetting(_rateNotice, AutomationLiveSetting.Polite);
        _rateNotice.Visibility = Visibility.Collapsed;
        _rateAlert.Children.Add(U.Alert(_rateErrorText)); _rateAlert.Children.Add(_rateReload);
        _rateReload.Click += async (_, _) => { if (_controller is { State.RateLoading: false } controller) await controller.ReloadRateAsync(); };
        _rateGroup.Fields.Children.Add(_rateAlert);
        _rateGroup.Fields.Children.Add(_rateNotice);

        var enabledRow = Rate(new AdminSettingsRow(T("web.admin.settings.security.rateLimit.enable"), "", _enabled));
        AutomationProperties.SetAutomationId(_enabled, "Security_" + F.Enabled);
        enabledRow.AddStatus(_notRunning);
        _enabled.Toggled += (_, _) => { if (!_syncing && _controller?.SetRate(F.Enabled, _enabled.IsOn) != true) QueueRender(); };

        _backend = U.UseInputEdge(U.Select(T("web.admin.settings.security.backend.label"), "Security_" + F.Backend,
            [("memory", T("web.admin.settings.security.backend.memory")), ("redis", T("web.admin.settings.security.backend.redis"))]));
        _backendRow = Rate(new AdminSettingsRow(T("web.admin.settings.security.backend.label"), T("web.admin.settings.security.backend.description"), _backend));
        _backendRow.AddStatus(_drift);
        WireSelect(_backend, F.Backend);

        var global = U.UseInputEdge(U.Input(T("web.admin.settings.security.global.label"), "Security_" + F.Global, number: true));
        global.TextAlignment = TextAlignment.Right;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(global, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);
        Rate(new AdminSettingsRow(T("web.admin.settings.security.global.label"), T("web.admin.settings.security.global.description"), global, T("web.admin.settings.security.global.unit")));
        WireNumber(global, F.Global);

        Triad("web.admin.settings.security.perAddress.label", "web.admin.settings.security.perAddress.description", F.IpPerSecond, F.IpPerMinute, F.IpBurst);

        Rate(U.Subheading(T("web.admin.settings.security.session.heading")));
        _quotaMode = U.UseInputEdge(U.Select(T("web.admin.settings.security.session.quotaLabel"), "Security_" + F.QuotaMode,
            F.QuotaModes.Select(mode => (mode, T("web.admin.settings.security.quotaMode." + mode)))));
        Rate(new AdminSettingsRow(T("web.admin.settings.security.session.quotaLabel"), T("web.admin.settings.security.session.quotaDescription"), _quotaMode));
        WireSelect(_quotaMode, F.QuotaMode);
        Boxes("web.admin.settings.security.session.sizeLabel", "web.admin.settings.security.session.sizeDescription",
            ("web.admin.settings.security.rate.perHour", F.QuotaPerHour), ("web.admin.settings.security.rate.burst", F.QuotaBurst));

        Rate(U.Subheading(T("web.admin.settings.security.tiers.heading")));
        foreach (var tier in F.Tiers)
            Triad("web.admin.settings.security.tier." + tier, "", F.Tier(tier, "requests_per_second"), F.Tier(tier, "requests_per_minute"), F.Tier(tier, "burst"));

        Rate(U.Subheading(T("web.admin.settings.security.endpoints.heading")));
        foreach (var endpoint in F.Endpoints)
            Boxes("web.admin.settings.security.endpoint." + EndpointLabel(endpoint), "",
                ("web.admin.settings.security.rate.perMinute", F.Endpoint(endpoint, "requests_per_minute")), ("web.admin.settings.security.rate.burst", F.Endpoint(endpoint, "burst")));

        _body.Children.Add(_spacer);
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
        _saveBar.Discard.Click += (_, _) => _controller?.Discard();
        Loaded += (_, _) => { FitScrollContent(); Activate(); }; Unloaded += (_, _) => Close();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); _saveBar.Margin = new(16, 0, 16, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? -8 : 8); };
    }

    private static string EndpointLabel(string endpoint) => endpoint switch
    {
        "password_change" => "passwordChange", "device_start" => "deviceStart", "device_lookup" => "deviceLookup",
        "device_poll" => "devicePoll", "autoscan_webhook" => "autoscanWebhook", _ => endpoint,
    };

    private T Rate<T>(T row) where T : UIElement
    {
        row.Visibility = Visibility.Collapsed;
        _rateRows.Add(row); _rateGroup.Add(row);
        return row;
    }

    private void Triad(string labelKey, string descriptionKey, string perSecond, string perMinute, string burst) =>
        Boxes(labelKey, descriptionKey, ("web.admin.settings.security.rate.perSecond", perSecond), ("web.admin.settings.security.rate.perMinute", perMinute),
            ("web.admin.settings.security.rate.burst", burst));

    /// <summary>A captioned number row; the unit slot stays reserved and empty so the boxes end on the global input's edge.</summary>
    private void Boxes(string labelKey, string descriptionKey, params (string Caption, string Path)[] boxes)
    {
        var label = T(labelKey);
        var description = descriptionKey.Length > 0 ? T(descriptionKey) : "";
        var flow = new AdminSettingsRateFlow();
        foreach (var (captionKey, path) in boxes)
        {
            var caption = T(captionKey);
            var (box, input) = U.RateBox(caption, $"{label} {caption}", "Security_" + path);
            if (description.Length > 0) AutomationProperties.SetHelpText(input, description);
            flow.Children.Add(box);
            WireNumber(input, path);
        }
        Rate(new AdminSettingsRow(label, description, flow, nameEditor: false));
    }

    private void WireNumber(TextBox box, string path)
    {
        _numbers[path] = box;
        box.TextChanging += (_, _) => { if (!_syncing && !_composing.Contains(box)) ApplyNumber(box, path); };
        // IME composition commits nothing until it ends (#4287).
        box.TextCompositionStarted += (_, _) => _composing.Add(box);
        box.TextCompositionEnded += (_, _) =>
        {
            _composing.Remove(box);
            DispatcherQueue.TryEnqueue(() => { if (_active && !_syncing && !_composing.Contains(box)) ApplyNumber(box, path); });
        };
        // Text that is not a valid value reverts on blur or Enter, never mid-composition.
        box.LostFocus += (_, _) => { if (!_composing.Contains(box)) RestoreNumber(box, path); };
        box.KeyDown += (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter || _composing.Contains(box)) return;
            RestoreNumber(box, path); args.Handled = true;
        };
    }

    private void ApplyNumber(TextBox box, string path)
    {
        if (_controller?.State is { RateEditable: true }) _controller.TrySetRateNumber(path, box.Text);
    }

    private void RestoreNumber(TextBox box, string path)
    {
        if (_controller?.State.RateValues is not { } values) return;
        var text = AdminRateLimitNumber.Format(values.Number(path));
        if (box.Text == text) return;
        _syncing = true;
        try { box.Text = text; box.SelectionStart = text.Length; }
        finally { _syncing = false; }
    }

    private void WireSelect(ComboBox select, string path) => select.SelectionChanged += (_, _) =>
    {
        if (_syncing || (select.SelectedItem as ComboBoxItem)?.Tag is not string value) return;
        if (_controller?.SetRate(path, value) != true) QueueRender();
    };

    private void FitScrollContent()
    {
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available <= 0) return;
        _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
        // The settings column takes its full width rather than its content's: stacked rows desire less than the
        // 520 that lets them go wide again, and an error-only rate group would otherwise narrow the page.
        _body.Width = Math.Min(_body.MaxWidth, _scrollContent.Width);
    }

    private static string T(string key, object? args = null) => AdminText.T(key, args);

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
        _active = true; _controller = App.Services.GetRequiredService<AdminSecuritySettingsController>();
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.DirtyCount > 0, () => _controller?.Discard());
        _controller.Changed += QueueRender; _controller.Saved += OnSaved; _controller.RateLimitsSaved += OnRateLimitsSaved;
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
    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_active && _controller?.State.Owner == null) _ = OpenAsync();
    });
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state); });
    }

    private void Render(AdminSecuritySettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        var accessBlocked = state.Error != null && state.Failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
            or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;
        // The skeleton stays until the settings form has loaded and the first rate read has settled.
        var initial = (state.Loading && !state.Loaded) || (state.RateLoading && !state.RateLoaded && state.RateMessage == AdminRateLimitMessage.None);
        _loading.Visibility = initial ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = initial ? Visibility.Collapsed : Visibility.Visible;
        _fields.Visibility = current && !initial && !accessBlocked && (state.Loaded || state.RateLoaded || state.RateMessage != AdminRateLimitMessage.None) ? Visibility.Visible : Visibility.Collapsed;
        _alerts.Visibility = current && (state.Error != null || state.Loading && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _reload.IsEnabled = !state.Saving && current;
        _reload.IsHitTestVisible = !state.Loading;
        if (state.Error != null) U.UpdateLiveText(_errorText, ErrorMessage(state));
        _syncing = true;
        try
        {
            RenderLocal(state, current, accessBlocked);
            RenderRate(state, current);
        }
        finally { _syncing = false; }
        _saveBar.State(current ? state.DirtyCount : 0, state.Saving, current && state.CanSave && !accessBlocked);
        _saveBar.SetAvailableWidth(ActualWidth);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;
        if ((state.Loaded || state.RateLoaded) && !initial && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); _scroller.Focus(FocusState.Programmatic); } });
        }
    }

    private void RenderLocal(AdminSecuritySettingsState state, bool current, bool accessBlocked)
    {
        _localGroup.Visibility = state.Loaded && current ? Visibility.Visible : Visibility.Collapsed;
        var managed = state.ManagedByEnv.Contains(C.LocalNetwork);
        var allowed = current && state.Values.GetValueOrDefault(C.LocalNetwork, "") == "true";
        _local.IsOn = allowed;
        _local.IsEnabled = current && state.Loaded && !state.Loading && !accessBlocked && !managed;
        // The warning follows the draft, so it appears as soon as the switch is staged on.
        var warning = allowed && state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        if (_warning.Visibility != warning) { _warning.Visibility = warning; _localGroup.RefreshRules(); }
        var description = managed ? T("configure.native.settings.managedByEnvironment") : T("web.admin.settings.security.localNetwork.description");
        if (_localRow.Description.Text != description) _localRow.Description.Text = description;
        AutomationProperties.SetHelpText(_local, description);
        var restartAll = C.Keys.All(state.RestartKeys.Has);
        _localGroup.State(false, restartAll ? T("web.admin.settings.fields.restartAll") : "");
        _localRow.State(false, !restartAll && state.RestartKeys.Has(C.LocalNetwork));
    }

    private void RenderRate(AdminSecuritySettingsState state, bool current)
    {
        var values = current ? state.RateValues : null;
        var visibility = values != null ? Visibility.Visible : Visibility.Collapsed;
        if (_rateRows[0].Visibility != visibility)
        {
            foreach (var row in _rateRows) row.Visibility = visibility;
            _rateGroup.RefreshRules();
        }
        // The group message: a failed or blocked state, with the explicit Retry/Reload that resolves it.
        var message = current ? RateMessage(state) : null;
        _rateAlert.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) U.UpdateLiveText(_rateErrorText, message);
        var reloadable = values == null || state.RateBlock != AdminRateLimitBlock.None || state.RateMessage is AdminRateLimitMessage.LoadFailed or AdminRateLimitMessage.ReloadBeforeEditing;
        _rateReload.Visibility = reloadable ? Visibility.Visible : Visibility.Collapsed;
        var reloadLabel = T(values == null ? "configure.native.rateLimits.retry" : "web.admin.settings.notifications.reload");
        if (_rateReload.Content as string != reloadLabel) { _rateReload.Content = reloadLabel; AutomationProperties.SetName(_rateReload, reloadLabel); }
        _rateReload.IsEnabled = current && !state.Saving;
        _rateReload.IsHitTestVisible = !state.RateLoading;
        var notice = current && state.RateMessage == AdminRateLimitMessage.Merged ? T("configure.native.rateLimits.merged") : "";
        U.UpdateLiveText(_rateNotice, notice);
        _rateNotice.Visibility = notice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _rateNotice.Margin = new(0, 14, 0, 0);

        var editable = values != null && state.RateEditable;
        _enabled.IsOn = values?.Bool(F.Enabled) == true;
        _enabled.IsEnabled = editable;
        var snapshot = state.RateSnapshot;
        var redisSelectable = snapshot?.RedisSelectable != false;
        SetSelected(_backend, values?.Text(F.Backend));
        ((ComboBoxItem)_backend.Items[1]).IsEnabled = redisSelectable;
        _backend.IsEnabled = editable;
        var backendDescription = T(redisSelectable ? "web.admin.settings.security.backend.description" : "web.admin.settings.security.backend.descriptionNoRedis");
        if (_backendRow.Description.Text != backendDescription) _backendRow.Description.Text = backendDescription;
        AutomationProperties.SetHelpText(_backend, backendDescription);
        SetSelected(_quotaMode, values?.Text(F.QuotaMode));
        _quotaMode.IsEnabled = editable;
        var quotaOff = values?.Text(F.QuotaMode) == "off";
        foreach (var (path, box) in _numbers)
        {
            var text = values == null ? "" : AdminRateLimitNumber.Format(values.Number(path));
            // A focused box keeps the admin's typing; blur or Enter restores the draft value.
            if (box.Text != text && (values == null || box.FocusState == FocusState.Unfocused)) box.Text = text;
            box.IsEnabled = editable && !(quotaOff && path is F.QuotaPerHour or F.QuotaBurst);
        }
        // Runtime hints come from the last server read, not the draft.
        _notRunning.Visibility = values != null && snapshot?.LimiterInactive == true ? Visibility.Visible : Visibility.Collapsed;
        var drift = values != null ? snapshot?.RunningBackendDrift : null;
        _drift.Visibility = drift != null ? Visibility.Visible : Visibility.Collapsed;
        var driftText = (TextBlock)_drift.Children[1];
        U.UpdateLiveText(driftText, drift == null ? "" : T("web.admin.settings.security.backend.drift", new { running = drift, saved = snapshot!.SavedBackend }));
    }

    private static void SetSelected(ComboBox select, string? value)
    {
        var match = select.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
        if (!ReferenceEquals(select.SelectedItem, match)) select.SelectedItem = match;
    }

    private static string? RateMessage(AdminSecuritySettingsState state) => state.RateMessage switch
    {
        AdminRateLimitMessage.LoadFailed => FailureText(state.RateFailure) ?? T("configure.native.rateLimits.loadFailed"),
        AdminRateLimitMessage.ReloadBeforeEditing => T("web.admin.common.rateLimits.reloadBeforeEditing"),
        AdminRateLimitMessage.ChangedElsewhere => T("web.admin.common.rateLimits.changedElsewhere"),
        AdminRateLimitMessage.OutcomeUnknown => T("configure.native.rateLimits.outcomeUnknown"),
        AdminRateLimitMessage.SaveFailed => state.RateFailure == AdminFailureKind.Validation && !string.IsNullOrEmpty(state.RateDetail) ? state.RateDetail
            : FailureText(state.RateFailure) ?? T("web.admin.common.rateLimits.saveFailed"),
        _ => null,
    };

    private static string? FailureText(AdminFailureKind? failure)
    {
        var key = failure switch
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
        return key == null ? null : T("configure.native.settings." + key);
    }

    private static string ErrorMessage(AdminSecuritySettingsState state) =>
        state.Conflict || state.OutcomeUnknown ? state.Error! : FailureText(state.Failure) ?? state.Error!;

    private void OnSaved(IReadOnlyCollection<string> keys) => RefreshShell(null);
    private void OnRateLimitsSaved(bool? restart) => RefreshShell(T(restart == true ? "web.admin.common.rateLimits.savedRestartRequired" : "web.admin.common.rateLimits.saved"));
    private void RefreshShell(string? toast)
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || controller == null || !ReferenceEquals(controller, _controller) || controller.State.Owner?.IsCurrent != true) return;
            if (toast != null) App.Services.GetService<ToastService>()?.Success(toast);
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
        // Disposing the controller retires its authority lifetime, cancelling in-flight reads and writes.
        if (_controller != null)
        {
            _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.RateLimitsSaved -= OnRateLimitsSaved;
            _controller.Dispose(); _controller = null;
        }
        _leave = null;
        _syncing = true;
        try
        {
            _local.IsOn = false; _local.IsEnabled = false; _enabled.IsOn = false; _enabled.IsEnabled = false;
            _backend.SelectedIndex = -1; _backend.IsEnabled = false; _quotaMode.SelectedIndex = -1; _quotaMode.IsEnabled = false;
            foreach (var box in _numbers.Values) { box.Text = ""; box.IsEnabled = false; }
            _composing.Clear();
        }
        finally { _syncing = false; }
        _alerts.Children.Clear();
        _fields.Visibility = _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
