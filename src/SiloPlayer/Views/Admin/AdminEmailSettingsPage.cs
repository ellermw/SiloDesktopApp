using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using U = SiloPlayer.Controls.AdminSettingsUi;
using C = SiloPlayer.Core.Services.Admin.AdminEmailSettingsController;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure > Integrations > Email (official EmailSettings.tsx). One Mail server group; every
/// row stays visible whatever the enable toggle says. The test row sends through SAVED settings.
/// </summary>
public sealed partial class AdminEmailSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private static readonly Windows.UI.Color Emerald500 = Windows.UI.Color.FromArgb(255, 0x10, 0xB9, 0x81);
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 32), Visibility = Visibility.Collapsed };
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _errorText = U.Text("", 13);
    private readonly Button _reload = U.Action(T("web.admin.settings.notifications.reload"), "EmailReload");
    private readonly StackPanel _loading = new() { Spacing = 24 };
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly AdminSettingsGroup _group = new(T("web.admin.settings.email.mailServer"), T("web.admin.settings.email.mailServerDescription"));
    private readonly AdminSettingsSaveBar _saveBar = new("Email");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly ComboBox _security;
    private readonly PasswordBox _password = U.Secret(T("web.admin.settings.email.password"), "Email_" + C.Password);
    private readonly Button _secretAction = U.Action(T("web.settings.secretField.clear"), "EmailPasswordAction");
    private readonly Button _retryStatus = U.Action(T("configure.native.email.retryPasswordStatus"), "EmailRetryPasswordStatus");
    private readonly TextBox _recipient = U.Input(T("web.admin.common.emailTest.recipientLabel"), "EmailTestRecipient", T("web.admin.common.emailTest.recipientPlaceholder"));
    private readonly Button _send = U.Action(T("web.admin.common.emailTest.send"), "EmailSendTest");
    private readonly Border _sendIcon = new() { Width = 16, Height = 16 };
    private readonly TextBlock _testResult = U.Text("", 12);
    private readonly AdminMonitorAuthority _authority;
    private AdminEmailSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _syncing, _opening, _spinning;
    /// <summary>The password text last exchanged with the controller. PasswordChanged arrives asynchronously,
    /// so a box that differs from it holds typing the controller has not seen yet; a render must not overwrite it.</summary>
    private string _passwordSynced = "";

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminEmailSettingsController? Controller => _controller;

    public AdminEmailSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminEmailSettingsPage");
        AutomationProperties.SetName(_scroller, T("web.admin.settings.layout.regionLabel", new { label = T("web.admin.settings.email.title") }));
        _header.Children.Add(U.Title(T("web.admin.settings.email.title")));
        var subtitle = U.Text(T("web.admin.settings.search.email.description"), 14, true);
        subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left; _header.Children.Add(subtitle); _body.Children.Add(_header);
        AutomationProperties.SetName(_loading, T("web.admin.settings.email.loading"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(U.Skeleton(32, 192));
        var skeletonRows = new StackPanel { Spacing = 16 };
        for (var row = 0; row < 4; row++) skeletonRows.Children.Add(U.Skeleton(40));
        _loading.Children.Add(skeletonRows);
        AutomationProperties.SetAutomationId(_errorText, "EmailErrorText");
        _alerts.Children.Add(U.Alert(_errorText)); _alerts.Children.Add(_reload);
        _reload.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true); };
        _body.Children.Add(_loading); _body.Children.Add(_alerts); _body.Children.Add(_fields);
        _fields.Children.Add(_group);

        var enabled = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        Add("email.enabled", T("web.admin.settings.email.enabled"), enabled);
        enabled.Toggled += (_, _) => Set("email.enabled", enabled.IsOn ? "true" : "false");
        AddText("email.from_address", "fromAddress", hint: "silo@example.com");
        AddText("email.from_name", "fromName", hint: "Silo");
        AddText("email.smtp_host", "host", hint: "smtp.example.com");
        AddText("email.smtp_port", "port", T("web.admin.settings.email.portDescription"), number: true);
        // STARTTLS is a protocol name and is not translated.
        _security = U.Select(T("web.admin.settings.email.encryption"), "Email_" + C.Security,
            [("starttls", "STARTTLS"), ("tls", T("web.admin.settings.email.encryptionTls")), ("none", T("web.admin.settings.email.encryptionNone"))]);
        Add(C.Security, T("web.admin.settings.email.encryption"), _security, T("web.admin.settings.email.encryptionDescription"));
        _security.SelectionChanged += (_, _) => { if ((_security.SelectedItem as ComboBoxItem)?.Tag is string value) Set(C.Security, value); };
        AddText("email.smtp_username", "username", T("web.admin.settings.email.usernameDescription"));

        // SecretField: the action sits before the input; there is no Replace step.
        var secret = new Grid { ColumnSpacing = 12 };
        secret.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        secret.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        secret.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _secretAction.VerticalAlignment = _retryStatus.VerticalAlignment = VerticalAlignment.Center;
        _secretAction.Visibility = _retryStatus.Visibility = Visibility.Collapsed;
        secret.Children.Add(_secretAction); Grid.SetColumn(_retryStatus, 1); secret.Children.Add(_retryStatus);
        Grid.SetColumn(_password, 2); secret.Children.Add(_password);
        var passwordRow = new AdminSettingsRow(T("web.admin.settings.email.password"), "", secret);
        AutomationProperties.SetName(_password, T("web.admin.settings.email.password"));
        AutomationProperties.SetName(secret, "");
        AutomationProperties.SetLiveSetting(passwordRow.Description, AutomationLiveSetting.Polite);
        passwordRow.LayoutChanged += wide => _password.Width = wide ? 240 : double.NaN;
        _rows[C.Password] = passwordRow; _controls[C.Password] = _password; _group.Add(passwordRow);
        _password.PasswordChanged += (_, _) =>
        {
            if (_syncing || _controller?.State is not { Loaded: true } state) return;
            _passwordSynced = _password.Password;
            // PasswordChanged also arrives later for programmatic updates; ignore that echo.
            if (_password.Password == state.Values.GetValueOrDefault(C.Password, "")) return;
            _controller.SetPassword(_password.Password);
        };
        _secretAction.Click += (_, _) =>
        {
            if (_controller?.State is not { Loaded: true } state) return;
            if (state.DirtyKeys.Contains(C.Password)) _controller.KeepPassword(); else _controller.ClearPassword();
        };
        _retryStatus.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true); };

        _group.Add(TestRow());
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

    /// <summary>Official EmailTestRow: recipient, outline Send test, result line and the saved-settings hint.</summary>
    private Border TestRow()
    {
        // max-w-md: the input fills the row up to 448px, left-aligned (a capped Stretch element would center).
        var line = new Grid { ColumnSpacing = 8, MaxWidth = 448, HorizontalAlignment = HorizontalAlignment.Left };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _recipient.Width = double.NaN; _recipient.HorizontalAlignment = HorizontalAlignment.Stretch;
        _recipient.InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } };
        _recipient.IsSpellCheckEnabled = false; _recipient.IsTextPredictionEnabled = false;
        line.Children.Add(_recipient);
        _recipient.TextChanged += (_, _) => RenderSendEnabled();
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _sendIcon.Child = AdminMonitorUi.Icon("send", 16, U.Brush("PrimaryTextBrush"));
        content.Children.Add(_sendIcon);
        content.Children.Add(new TextBlock { Text = T("web.admin.common.emailTest.send"), VerticalAlignment = VerticalAlignment.Center });
        _send.Content = content; _send.Height = 36; _send.MinHeight = 36; _send.Padding = new(16, 0, 16, 0); _send.IsEnabled = false;
        Grid.SetColumn(_send, 1); line.Children.Add(_send);
        _send.Click += async (_, _) => { if (_controller != null) await _controller.SendTestAsync(_recipient.Text); };
        AutomationProperties.SetAutomationId(_testResult, "EmailTestResult");
        AutomationProperties.SetLiveSetting(_testResult, AutomationLiveSetting.Polite);
        _testResult.Visibility = Visibility.Collapsed;
        var hint = U.Text(T("web.admin.common.emailTest.saveFirst"), 12, true);
        AutomationProperties.SetAutomationId(hint, "EmailTestHint");
        AutomationProperties.SetHelpText(_recipient, hint.Text);
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(line); stack.Children.Add(_testResult); stack.Children.Add(hint);
        var row = new Border { Child = stack, Padding = new(0, 12, 0, 12) };
        row.SizeChanged += (_, args) => line.Width = Math.Min(448, Math.Max(0, args.NewSize.Width));
        return row;
    }

    private void FitScrollContent()
    {
        // Give the outer frame its viewport width so the 1120px settings column starts at the common gutter.
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    private static string T(string key, object? args = null) => AdminText.T(key, args);
    private void AddText(string key, string labelKey, string description = "", string hint = "", bool number = false)
    {
        var label = T($"web.admin.settings.email.{labelKey}");
        var input = U.Input(label, "Email_" + key, hint, number);
        Add(key, label, input, description);
        input.TextChanging += (_, _) => Set(key, input.Text);
    }
    private void Add(string key, string label, Control input, string description = "")
    {
        AutomationProperties.SetAutomationId(input, "Email_" + key);
        var row = new AdminSettingsRow(label, description, input);
        _controls[key] = input; _rows[key] = row; _group.Add(row);
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
        _active = true; _controller = App.Services.GetRequiredService<AdminEmailSettingsController>();
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
    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        // A replaced authority must not inherit the previous recipient or result.
        if (_controller?.State.Owner == null) { ClearTestDraft(); _ = OpenAsync(); }
    });
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state); });
    }

    private void Render(AdminEmailSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        if (!current) ClearTestDraft();
        var accessBlocked = state.Error != null && state.Failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
            or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;
        _loading.Visibility = state.Loading && !state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = _loading.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        _fields.Visibility = state.Loaded && current && !accessBlocked ? Visibility.Visible : Visibility.Collapsed;
        // Retain the live region and Reload control while the retry is in flight.
        _alerts.Visibility = current && (state.Error != null || state.Loading && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _reload.IsEnabled = !state.Saving && current;
        _reload.IsHitTestVisible = !state.Loading;
        if (state.Error != null) U.UpdateLiveText(_errorText, ErrorMessage(state));
        bool Dirty(string key) => state.DirtyKeys.Contains(key);
        bool Managed(string key) => state.ManagedByEnv.Contains(key);
        var editable = current && state.Loaded && !state.Loading && !accessBlocked;
        _syncing = true;
        try
        {
            foreach (var (key, control) in _controls)
            {
                var value = current ? state.Values.GetValueOrDefault(key, "") : "";
                if (control is TextBox text && text.Text != value) text.Text = value;
                else if (control is PasswordBox secret)
                {
                    // Retirement always clears private text, including a PasswordChanged event still in flight.
                    if (secret.Password != value && (!current || secret.Password == _passwordSynced)) { secret.Password = value; _passwordSynced = value; }
                }
                else if (control is ToggleSwitch toggle) toggle.IsOn = value == "true";
                else if (control is ComboBox select) SetSelected(select, C.DisplaySecurity(value));
                control.IsEnabled = editable && !Managed(key) && (key != C.Password || (!state.Saving && !state.SensitiveStatusError));
            }
        }
        finally { _syncing = false; }

        // Password: presence comes only from sensitive status; the saved value is never loaded.
        var passwordValue = state.Values.GetValueOrDefault(C.Password, "");
        var clearStaged = Dirty(C.Password) && passwordValue.Length == 0;
        var passwordEditable = _password.IsEnabled;
        var showAction = current && state.PasswordConfigured && passwordEditable && passwordValue.Length == 0;
        _secretAction.Visibility = showAction ? Visibility.Visible : Visibility.Collapsed;
        var actionLabel = T(clearStaged ? "web.settings.secretField.keep" : "web.settings.secretField.clear");
        if (_secretAction.Content as string != actionLabel) { _secretAction.Content = actionLabel; AutomationProperties.SetName(_secretAction, actionLabel); }
        var statusFailed = current && state.Loaded && state.SensitiveStatusError && !Managed(C.Password);
        _retryStatus.Visibility = statusFailed ? Visibility.Visible : Visibility.Collapsed;
        _retryStatus.IsEnabled = statusFailed && !state.Loading && !state.Saving;
        _password.PlaceholderText = state.SensitiveStatusError ? "" : clearStaged ? T("web.settings.secretField.willClear")
            : state.PasswordConfigured ? "••••••••••••" : T("web.settings.secretField.notConfigured");
        var passwordDescription = Managed(C.Password) ? T("configure.native.settings.managedByEnvironment")
            : state.SensitiveStatusError ? T("configure.native.email.passwordStatusUnavailable")
            : clearStaged ? T("web.settings.secretField.clearedDescription")
            : state.PasswordConfigured ? T("web.settings.secretField.configuredDescription") : "";
        Describe(C.Password, passwordDescription);
        Describe("email.smtp_port", Managed("email.smtp_port") ? T("configure.native.settings.managedByEnvironment") : T("web.admin.settings.email.portDescription"));
        Describe(C.Security, Managed(C.Security) ? T("configure.native.settings.managedByEnvironment") : T("web.admin.settings.email.encryptionDescription"));
        Describe("email.smtp_username", Managed("email.smtp_username") ? T("configure.native.settings.managedByEnvironment") : T("web.admin.settings.email.usernameDescription"));
        foreach (var key in new[] { "email.enabled", "email.from_address", "email.from_name", "email.smtp_host" })
            Describe(key, Managed(key) ? T("configure.native.settings.managedByEnvironment") : "");

        var allRestart = C.Keys.All(state.RestartKeys.Has);
        var groupDescription = T("web.admin.settings.email.mailServerDescription");
        _group.State(C.Keys.Any(Dirty), allRestart ? T("web.admin.settings.fields.restartAllWithDescription", new { description = groupDescription }) : groupDescription);
        // SecretField carries no per-field dirty dot; the group dot covers it.
        foreach (var key in C.Keys) _rows[key].State(key != C.Password && Dirty(key), !allRestart && state.RestartKeys.Has(key));

        RenderTest(state, current);
        var writable = state.DirtyKeys.Any(key => !Managed(key) && (key != C.Password || !state.SensitiveStatusError));
        _saveBar.State(current ? state.DirtyKeys.Count : 0, state.Saving, current && writable && !state.Loading && !accessBlocked && !state.Conflict && !state.OutcomeUnknown);
        _saveBar.SetAvailableWidth(ActualWidth);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;
        if (state.Loaded && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); _scroller.Focus(FocusState.Programmatic); } });
        }
    }

    private void Describe(string key, string text)
    {
        var description = _rows[key].Description;
        if (description.Text != text) description.Text = text;
        description.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(_controls[key], text);
        if (key == C.Password) AutomationProperties.SetName(description, text);
    }

    private static void SetSelected(ComboBox select, string value)
    {
        var match = select.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
        if (match == null) { match = new() { Content = value, Tag = value, CornerRadius = new(6), MinHeight = 32 }; select.Items.Add(match); }
        if (!ReferenceEquals(select.SelectedItem, match)) select.SelectedItem = match;
    }

    private void RenderTest(AdminEmailSettingsState state, bool current)
    {
        if (_spinning != state.TestPending)
        {
            _spinning = state.TestPending;
            // Swap only the icon; the focused Send button itself is never rebuilt.
            _sendIcon.Child = _spinning ? AdminMonitorUi.Spinner(16, AdminMonitorUi.ColorOf("PrimaryTextBrush", Colors.White))
                : AdminMonitorUi.Icon("send", 16, U.Brush("PrimaryTextBrush"));
        }
        _recipient.IsEnabled = current && state.Loaded;
        RenderSendEnabled();
        var feedback = current ? state.TestResult : null;
        var text = feedback == null ? "" : TestMessage(feedback);
        U.UpdateLiveText(_testResult, text);
        _testResult.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _testResult.Foreground = AdminMonitorUi.Solid(feedback?.Outcome switch
        {
            AdminEmailTestOutcome.Delivered => Emerald500,
            AdminEmailTestOutcome.Rejected or AdminEmailTestOutcome.Unknown => AdminMonitorUi.Amber500,
            _ => AdminMonitorUi.Destructive,
        });
    }

    private void RenderSendEnabled()
    {
        var state = _controller?.State;
        _send.IsEnabled = state is { Loaded: true, TestPending: false } && state.Owner?.IsCurrent == true && AdminEmailAddress.Trim(_recipient.Text).Length > 0;
    }

    internal static string TestMessage(AdminEmailTestFeedback feedback) => feedback.Outcome switch
    {
        AdminEmailTestOutcome.Delivered => T("web.admin.common.emailTest.delivered", new { duration_ms = feedback.DurationMs }),
        AdminEmailTestOutcome.Rejected => string.IsNullOrEmpty(feedback.Message) ? T("web.admin.common.emailTest.failed") : feedback.Message,
        AdminEmailTestOutcome.InvalidRecipient => T("web.auth.invalidEmail"),
        AdminEmailTestOutcome.Unknown => T("configure.native.emailTest.unknown"),
        _ => feedback.ErrorCode == "dependency_unavailable" ? T("configure.native.emailTest.unavailable") : feedback.Failure switch
        {
            AdminFailureKind.Unauthorized => T("configure.native.emailTest.authenticationRequired"),
            AdminFailureKind.Forbidden or AdminFailureKind.DemoRestricted => T("configure.native.emailTest.forbidden"),
            AdminFailureKind.ProfileVerificationRequired => T("configure.native.emailTest.profileVerification"),
            AdminFailureKind.CapabilityUnavailable => T("configure.native.emailTest.unavailable"),
            AdminFailureKind.RateLimited => T("configure.native.emailTest.rateLimited"),
            AdminFailureKind.Validation => T("configure.native.emailTest.recipientRejected"),
            _ => T("web.admin.common.emailTest.requestFailed"),
        },
    };

    private static string ErrorMessage(AdminEmailSettingsState state)
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
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || controller == null || !ReferenceEquals(controller, _controller) || controller.State.Owner?.IsCurrent != true) return;
            if (App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell) shell.RefreshSettingsStatus();
        });
    }

    public Task<bool> CanLeaveAsync()
    {
        if (_controller?.State is { Saving: true, Owner.IsCurrent: true }) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }

    public void SuspendAdminPage() => Close();

    private void ClearTestDraft()
    {
        if (_recipient.Text.Length > 0) _recipient.Text = "";
        _testResult.Text = ""; _testResult.Visibility = Visibility.Collapsed;
    }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetValue(_location, static _ => new ScrollPosition()).Offset = _scroller.VerticalOffset;
        _authority.AvailabilityChanged -= OnAuthorityChanged; _dialog?.Hide(); _dialog = null;
        // Disposing the controller retires its authority lifetime, cancelling an in-flight test or save wait.
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Dispose(); _controller = null; }
        _leave = null;
        _syncing = true;
        foreach (var control in _controls.Values)
        {
            control.IsEnabled = false;
            if (control is TextBox text) text.Text = ""; else if (control is PasswordBox secret) { secret.Password = ""; _passwordSynced = ""; }
            else if (control is ToggleSwitch toggle) toggle.IsOn = false; else if (control is ComboBox select) select.SelectedIndex = -1;
        }
        _syncing = false;
        ClearTestDraft(); _send.IsEnabled = false; _recipient.IsEnabled = false;
        _sendIcon.Child = null; _spinning = false;
        _alerts.Children.Clear();
        _fields.Visibility = _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
