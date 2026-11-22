using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
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
using R = SiloPlayer.Core.Services.Admin.AdminSearchSettingsRules;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure &gt; Server &gt; Search (official <c>SearchSettings.tsx</c>): the search engine, the Meilisearch connection and
/// matching choices, the Advanced tuning rows and the read-only search status with the index maintenance actions.
/// </summary>
public sealed partial class AdminSearchSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private static readonly Windows.UI.Color Emerald600 = Windows.UI.Color.FromArgb(255, 0x16, 0xA3, 0x4A);
    private static readonly Windows.UI.Color Red600 = Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26);
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 32), Visibility = Visibility.Collapsed };
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _errorText = U.Text("", 13);
    private readonly Button _retry = U.Action(T("common.retry"), "SearchRetry");
    private readonly Button _reload = U.Action(T("web.admin.settings.notifications.reload"), "SearchReload");
    private readonly StackPanel _loading = new() { Spacing = 24 };
    private readonly Dictionary<string, FrameworkElement> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly AdminSettingsGroup _group = new(T("web.admin.settings.search.group"), T("web.admin.settings.search.description"));
    private readonly AdminSettingsSaveBar _saveBar = new("Search");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly ComboBox _provider;
    private readonly ComboBox _matching;
    private readonly ToggleSwitch _semantic = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly TextBlock _summary = U.Text("", 13, true);
    private readonly Grid _semanticWarning = U.Status(T("web.admin.settings.search.meili.semantic.enablingWarning"), "SearchSemanticWarning");
    private readonly Border _credentialRow;
    private readonly Grid _credentialStatus = U.Status(T("web.admin.settings.search.credentialStatusError"), "SearchCredentialStatus");
    private readonly Button _credentialRetry = U.Action(T("common.retry"), "SearchRetryCredentialStatus");
    private readonly Border _degradedRow;
    private readonly TextBlock _degradedText = U.Text("", 13);
    private readonly PasswordBox _apiKey = U.Secret(T("web.admin.settings.search.meili.apiKey.label"), "Search_" + R.MeilisearchApiKey);
    private readonly Button _secretAction = U.Action(T("web.settings.secretField.clear"), "SearchApiKeyAction");
    private readonly Button _check = U.Action(T("web.admin.common.connectionCheck.check"), "SearchCheckConnection", secondary: true);
    private readonly TextBlock _checkResult = U.Text("", 14);
    private readonly Border _checkRow;
    private readonly AdminMonitorAuthority _authority;
    private AdminSearchSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private AdminRouteRequest? _location;
    private LoggingDisclosurePreferences? _preferences;
    private double? _restoreOffset;
    private bool _active, _syncing, _opening, _navigated, _hostVisible = true, _hostFocused = true, _tasksAvailable;
    /// <summary>The key text last exchanged with the controller; PasswordChanged arrives asynchronously, so a render must not overwrite newer typing.</summary>
    private string _apiKeySynced = "";
    private string _degradedSignature = "";
    /// <summary>What the admin last typed into each text box, so a render never rewrites a box mid-edit.</summary>
    private readonly Dictionary<string, string> _typed = new(StringComparer.Ordinal);

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminSearchSettingsController? Controller => _controller;

    private static readonly Lazy<LoggingDisclosurePreferences> DefaultPreferences = new(() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "monitor-ui")));

    public AdminSearchSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminSearchSettingsPage");
        AutomationProperties.SetName(_scroller, T("web.admin.settings.layout.regionLabel", new { label = T("web.admin.settings.search.title") }));
        _header.Children.Add(U.Title(T("web.admin.settings.search.title")));
        var subtitle = U.Text(T("web.admin.settings.search.search.description"), 14, true);
        subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left; _header.Children.Add(subtitle); _body.Children.Add(_header);
        AutomationProperties.SetName(_loading, T("web.admin.settings.fields.loadingSettings"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(U.Skeleton(32, 192));
        _loading.Children.Add(U.Skeleton(128));
        AutomationProperties.SetAutomationId(_errorText, "SearchErrorText");
        _alerts.Children.Add(U.Alert(_errorText)); _alerts.Children.Add(_retry); _alerts.Children.Add(_reload);
        _retry.Visibility = _reload.Visibility = Visibility.Collapsed;
        _retry.HorizontalAlignment = _reload.HorizontalAlignment = HorizontalAlignment.Left;
        _retry.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: false); };
        _reload.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true); };
        _body.Children.Add(_loading); _body.Children.Add(_alerts); _body.Children.Add(_fields);
        _fields.Children.Add(_group);

        // Search engine: the status summary sits under the description while Meilisearch answers.
        _provider = U.Select(T("web.admin.settings.search.provider.label"), "Search_" + R.Provider,
            [(R.Postgres, T("web.admin.settings.search.provider.postgres")), (R.Meilisearch, "Meilisearch")]);
        var providerRow = Place(_group, R.Provider, T("web.admin.settings.search.provider.label"), T("web.admin.settings.search.provider.description"), _provider);
        _summary.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_summary, "SearchStatusSummary");
        AutomationProperties.SetLiveSetting(_summary, AutomationLiveSetting.Polite);
        providerRow.AddStatus(_summary);
        _provider.SelectionChanged += (_, _) => { if ((_provider.SelectedItem as ComboBoxItem)?.Tag is string value) Set(R.Provider, value); };

        // The two page-level notes between the engine row and the Meilisearch rows.
        var credentialContent = new StackPanel { Spacing = 8 };
        credentialContent.Children.Add(_credentialStatus); _credentialStatus.Visibility = Visibility.Visible;
        _credentialRetry.HorizontalAlignment = HorizontalAlignment.Left;
        credentialContent.Children.Add(_credentialRetry);
        _credentialRow = new Border { Child = credentialContent, Padding = new(0, 14, 0, 14), Visibility = Visibility.Collapsed };
        _credentialRetry.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true); };
        _group.Add(_credentialRow);
        AutomationProperties.SetLiveSetting(_degradedText, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_degradedText, "SearchDegraded");
        var degradedIcon = AdminMonitorUi.Icon("alert-triangle", 14, AdminMonitorUi.Amber400); degradedIcon.VerticalAlignment = VerticalAlignment.Top; degradedIcon.Margin = new(0, 2, 0, 0);
        _degradedText.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber400); _degradedText.LineHeight = 17.875;
        var degraded = new Grid { ColumnSpacing = 6 };
        degraded.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); degraded.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        degraded.Children.Add(degradedIcon); Grid.SetColumn(_degradedText, 1); degraded.Children.Add(_degradedText);
        _degradedRow = new Border { Child = degraded, Padding = new(0, 14, 0, 14), Visibility = Visibility.Collapsed };
        _group.Add(_degradedRow);

        // Meilisearch rows, shown once it is selected or while any of its edits is staged.
        AddText(R.MeilisearchUrl, "web.admin.settings.search.meili.url.label", "", hint: "http://localhost:7700");
        var secret = new Grid { ColumnSpacing = 12 };
        secret.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        secret.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _secretAction.VerticalAlignment = VerticalAlignment.Center; _secretAction.Visibility = Visibility.Collapsed;
        secret.Children.Add(_secretAction);
        Grid.SetColumn(_apiKey, 1); secret.Children.Add(_apiKey);
        var apiKeyRow = new AdminSettingsRow(T("web.admin.settings.search.meili.apiKey.label"), T("web.admin.settings.search.meili.apiKey.hint"), secret);
        AutomationProperties.SetName(_apiKey, T("web.admin.settings.search.meili.apiKey.label"));
        AutomationProperties.SetName(secret, "");
        AutomationProperties.SetLiveSetting(apiKeyRow.Description, AutomationLiveSetting.Polite);
        apiKeyRow.LayoutChanged += wide => _apiKey.Width = wide ? 240 : double.NaN;
        _rows[R.MeilisearchApiKey] = apiKeyRow; _controls[R.MeilisearchApiKey] = _apiKey; _group.Add(apiKeyRow);
        _apiKey.PasswordChanged += (_, _) =>
        {
            if (_syncing || _controller?.State is not { Loaded: true } state) return;
            _apiKeySynced = _apiKey.Password;
            // PasswordChanged also arrives later for programmatic updates; ignore that echo.
            if (_apiKey.Password == state.Values.GetValueOrDefault(R.MeilisearchApiKey, "")) return;
            _controller.SetApiKey(_apiKey.Password);
        };
        _secretAction.Click += (_, _) =>
        {
            if (_controller?.State is not { Loaded: true } state) return;
            if (state.DirtyKeys.Contains(R.MeilisearchApiKey)) _controller.KeepApiKey(); else _controller.ClearApiKey();
        };
        var checkContent = new AdminSettingsActionContent(_check, _checkResult);
        _check.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetLiveSetting(_checkResult, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_checkResult, "SearchCheckResult");
        _checkRow = new Border { Child = checkContent, Padding = new(0, 14, 0, 14), Visibility = Visibility.Collapsed };
        _group.Add(_checkRow);
        _check.Click += async (_, _) => { if (_controller != null) await _controller.CheckConnectionAsync(); };
        _matching = U.Select(T("web.admin.settings.search.meili.matching.label"), "Search_" + R.MatchingStrategy,
            [("last", T("web.admin.settings.search.meili.matching.last")), ("all", T("web.admin.settings.search.meili.matching.all"))]);
        Place(_group, R.MatchingStrategy, T("web.admin.settings.search.meili.matching.label"), "", _matching);
        _matching.SelectionChanged += (_, _) => { if ((_matching.SelectedItem as ComboBoxItem)?.Tag is string value) Set(R.MatchingStrategy, value); };
        AutomationProperties.SetAutomationId(_semantic, "Search_" + R.SemanticEnabled);
        var semanticRow = Place(_group, R.SemanticEnabled, T("web.admin.settings.search.meili.semantic.label"), T("web.admin.settings.search.meili.semantic.description"), _semantic);
        _semanticWarning.Visibility = Visibility.Collapsed;
        semanticRow.AddStatus(_semanticWarning);
        _semantic.Toggled += (_, _) => Set(R.SemanticEnabled, _semantic.IsOn ? "true" : "false");

        BuildAdvanced();

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
        Loaded += (_, _) => { FitScrollContent(); if (_navigated) Activate(); }; Unloaded += (_, _) => Close();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); _saveBar.Margin = new(16, 0, 16, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? -8 : 8); };
    }

    private static string T(string key, object? args = null) => AdminText.T(key, args);

    // ------------------------------------------------------------------ row builders

    private AdminSettingsRow Place(AdminSettingsGroup group, string key, string label, string description, FrameworkElement editor, string unit = "")
    {
        AutomationProperties.SetAutomationId(editor, "Search_" + key);
        var row = new AdminSettingsRow(label, description, editor, unit.Length > 0 ? unit : null);
        _controls[key] = editor; _rows[key] = row; group.Add(row);
        return row;
    }

    private AdminSettingsRow PlaceInRail(string key, string label, string description, FrameworkElement editor, string unit = "")
    {
        AutomationProperties.SetAutomationId(editor, "Search_" + key);
        var row = new AdminSettingsRow(label, description, editor, unit.Length > 0 ? unit : null);
        _controls[key] = editor; _rows[key] = row; _railFields.Children.Add(row);
        return row;
    }

    private void AddText(string key, string labelKey, string description, string hint = "", bool number = false, bool rail = false, string unitKey = "")
    {
        var label = T(labelKey);
        var input = U.Input(label, "Search_" + key, hint, number);
        var unit = unitKey.Length > 0 ? T(unitKey) : "";
        if (rail) PlaceInRail(key, label, description, input, unit); else Place(_group, key, label, description, input, unit);
        input.TextChanging += (_, _) => { if (!_syncing) _typed[key] = input.Text; Set(key, input.Text); };
    }

    private void Set(string key, string value)
    {
        if (!_syncing && _controller?.State is { Loaded: true } state && state.Values.GetValueOrDefault(key, "") != value) _controller.Set(key, value);
    }

    private void FitScrollContent()
    {
        // Give the outer frame its viewport width so the 1120px settings column starts at the common gutter.
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    // ------------------------------------------------------------------ lifetime

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _navigated = true;
        _location = e.Parameter as AdminRouteRequest;
        _restoreOffset = e.NavigationMode == NavigationMode.Back && _location != null && ScrollPositions.TryGetValue(_location, out var position) ? position.Offset : 0;
        base.OnNavigatedTo(e);
        if (IsLoaded) Activate();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); _navigated = false; base.OnNavigatedFrom(e); }

    private void Activate()
    {
        if (_active) return;
        _active = true; _controller = App.Services.GetRequiredService<AdminSearchSettingsController>();
        _preferences = App.Services.GetService<LoggingDisclosurePreferences>();
        try { _persistedOpen = (_preferences ?? DefaultPreferences.Value).Read("search"); } catch { _persistedOpen = false; }
        _override = null; _forced = false; _advancedOpen = false; _statusKey = "";
        _tasksAvailable = AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.ScheduledTasks);
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.DirtyKeys.Count > 0, () => _controller?.Discard());
        _controller.Changed += QueueRender; _controller.Saved += OnSaved;
        _authority.AvailabilityChanged += OnAuthorityChanged;
        if (App.MainWindowInstance is { } window) { window.Activated += OnWindowActivated; window.VisibilityChanged += OnWindowVisibilityChanged; _hostVisible = window.Visible; }
        if (XamlRoot != null) { XamlRoot.Changed += OnXamlRootChanged; _hostVisible &= XamlRoot.IsHostVisible; }
        _controller.SetActive(_hostVisible && _hostFocused);
        _ = OpenAsync();
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
        if (_active && _controller?.State.Owner == null) { ClearUi(); _ = OpenAsync(); }
    });

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _hostFocused = args.WindowActivationState != WindowActivationState.Deactivated;
        _controller?.SetActive(_active && _hostVisible && _hostFocused);
    }
    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        _hostVisible = args.Visible && (XamlRoot?.IsHostVisible ?? true);
        _controller?.SetActive(_active && _hostVisible && _hostFocused);
    }
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _hostVisible = sender.IsHostVisible && (App.MainWindowInstance?.Visible ?? true);
        _controller?.SetActive(_active && _hostVisible && _hostFocused);
    }
    /// <summary>Window activation and visibility as the page observes them; native fixtures drive this directly.</summary>
    public void SetHostActivity(bool visible, bool focused)
    {
        _hostVisible = visible; _hostFocused = focused;
        _controller?.SetActive(_active && visible && focused);
    }

    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state); });
    }

    // ------------------------------------------------------------------ render

    private static bool IsAccessFailure(AdminFailureKind? kind) => kind is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
        or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;

    private void Render(AdminSearchSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        if (!current) ClearUi();
        var accessBlocked = state.Error != null && IsAccessFailure(state.Failure);
        var loadFailed = current && state.Error != null && !state.Loaded && !state.Loading;
        _loading.Visibility = state.Loading && !state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = _loading.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var showFields = state.Loaded && current && !accessBlocked;
        _fields.Visibility = showFields ? Visibility.Visible : Visibility.Collapsed;
        // Retain the live region and the Retry/Reload control while a retry is in flight.
        _alerts.Visibility = current && (state.Error != null || state.Loading && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = loadFailed || (state.Loading && !state.Loaded && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _reload.Visibility = state.Loaded && state.Error != null ? Visibility.Visible : Visibility.Collapsed;
        _retry.IsEnabled = _reload.IsEnabled = !state.Saving && current;
        _retry.IsHitTestVisible = _reload.IsHitTestVisible = !state.Loading;
        if (state.Error != null) U.UpdateLiveText(_errorText, ErrorMessage(state));

        bool Dirty(string key) => state.DirtyKeys.Contains(key);
        bool Managed(string key) => state.ManagedByEnv.Contains(key);
        var meili = state.MeilisearchSelected;
        var showMeili = meili || R.AnyMeilisearchDirty(state.DirtyKeys);
        var editable = current && state.Loaded && !state.Loading && !accessBlocked;
        var unknown = state.SensitiveStatusError;
        _syncing = true;
        try
        {
            foreach (var (key, control) in _controls)
            {
                var value = current ? state.Values.GetValueOrDefault(key, "") : "";
                var enabled = editable && (key == R.Provider || meili) && !Managed(key) && (key is not (R.Provider or R.MeilisearchUrl or R.MeilisearchApiKey) || !unknown)
                    && (key != R.MeilisearchApiKey || !state.Saving);
                switch (control)
                {
                    case TextBox text:
                        // A text or number box shows what it holds, empty included: no default appears once it is emptied (official #4816).
                        var shown = current ? R.Display(key, value) : "";
                        // The box being typed in keeps the admin's text.
                        var typing = current && text.FocusState != FocusState.Unfocused && _typed.TryGetValue(key, out var typed) && typed == text.Text && typed == value;
                        if (text.Text != shown && !typing) text.Text = shown;
                        text.IsEnabled = enabled; break;
                    case PasswordBox secret:
                        // Retirement always clears private text, including a PasswordChanged event still in flight.
                        if (secret.Password != value && (!current || secret.Password == _apiKeySynced)) { secret.Password = value; _apiKeySynced = value; }
                        secret.IsEnabled = enabled; break;
                    case ToggleSwitch toggle:
                        toggle.IsOn = current && R.Display(key, value) == "true"; toggle.IsEnabled = enabled; break;
                    case ComboBox select:
                        SetSelected(select, current ? R.Display(key, value) : ""); select.IsEnabled = enabled; break;
                }
            }
        }
        finally { _syncing = false; }

        // The API key: presence comes only from sensitive status; the saved value is never loaded.
        var keyValue = state.Values.GetValueOrDefault(R.MeilisearchApiKey, "");
        var clearStaged = Dirty(R.MeilisearchApiKey) && keyValue.Length == 0;
        var keyEditable = _apiKey.IsEnabled;
        _secretAction.Visibility = current && state.ApiKeyConfigured && keyEditable && keyValue.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var actionLabel = T(clearStaged ? "web.settings.secretField.keep" : "web.settings.secretField.clear");
        if (_secretAction.Content as string != actionLabel) { _secretAction.Content = actionLabel; AutomationProperties.SetName(_secretAction, actionLabel); }
        _apiKey.PlaceholderText = clearStaged ? T("web.settings.secretField.willClear") : state.ApiKeyConfigured ? "••••••••••••" : T("web.settings.secretField.notConfigured");
        // Official SecretField: the page's hint is the description (also while the environment sets the key); only the "saved value" line is
        // withheld for a managed secret, and this page passes a hint, so that line never shows.
        Describe(R.MeilisearchApiKey, clearStaged ? T("web.settings.secretField.clearedDescription") : T("web.admin.settings.search.meili.apiKey.hint"));
        Describe(R.Provider, T("web.admin.settings.search.provider.description"));
        Describe(R.MeilisearchUrl, "");
        Describe(R.MatchingStrategy, "");
        Describe(R.SemanticEnabled, T("web.admin.settings.search.meili.semantic.description"));
        Describe(R.Index, T("web.admin.settings.search.meili.index.description"));
        Describe(R.TimeoutMs, T("web.admin.settings.search.meili.timeout.description"));
        Describe(R.SyncBatchSize, T("web.admin.settings.search.meili.batch.description"));
        Describe(R.SemanticRatio, T("web.admin.settings.search.meili.ratio.description"));
        // Every row keeps its description; a setting the server's environment sets adds the official note naming the variable.
        // The server's own name for the variable wins; a server that named none keeps a fallback only where exactly one variable can set the key
        // (the provider and the URL). The API key has none: it may be read from a file variable, so the unnamed note is shown instead.
        foreach (var key in R.Keys)
            if (_rows.TryGetValue(key, out var envRow))
                envRow.SetEnvironmentNote(Managed(key) ? AdminEnvManagedSettings.NoteFor(state.EnvManaged.VariableName(key) ?? R.EnvironmentVariable(key)) : null);

        // Rows that exist only for Meilisearch, kept reachable while any of its edits is staged.
        foreach (var key in R.ShownKeys) _rows[key].Visibility = showFields && showMeili ? Visibility.Visible : Visibility.Collapsed;
        foreach (var key in R.AdvancedKeys) _rows[key].Visibility = showFields && showMeili ? Visibility.Visible : Visibility.Collapsed;
        _checkRow.Visibility = showFields && showMeili ? Visibility.Visible : Visibility.Collapsed;
        _credentialRow.Visibility = current && state.Loaded && unknown ? Visibility.Visible : Visibility.Collapsed;
        _credentialRetry.IsEnabled = current && !state.Loading && !state.Saving;
        _group.RefreshRules();
        var enabling = current && R.IsEnablingSemanticSearch(state.Values, state.PersistedValues, state.DirtyKeys);
        _semanticWarning.Visibility = enabling ? Visibility.Visible : Visibility.Collapsed;

        _check.Content = T(state.Checking ? "web.admin.common.connectionCheck.checking" : "web.admin.common.connectionCheck.check");
        AutomationProperties.SetName(_check, _check.Content.ToString());
        _check.IsEnabled = editable && meili && !state.Saving && !state.Checking;
        U.UpdateLiveText(_checkResult, current ? state.ConnectionResult?.Message ?? "" : "");
        _checkResult.Foreground = AdminMonitorUi.Solid(state.ConnectionResult?.Success == true ? Emerald600 : Red600);

        var allRestart = R.Keys.All(state.RestartKeys.Has);
        var groupDescription = T("web.admin.settings.search.description");
        _group.State(R.Keys.Any(Dirty), allRestart ? T("web.admin.settings.fields.restartAllWithDescription", new { description = groupDescription }) : groupDescription);
        // SecretField carries no per-field dirty dot; the group dot covers it.
        foreach (var key in R.Keys) _rows[key].State(key != R.MeilisearchApiKey && Dirty(key), !allRestart && state.RestartKeys.Has(key));
        UpdateAdvanced(current && showMeili && R.AnyAdvancedDirty(state.DirtyKeys), showMeili);
        // Restoring Advanced may not raise Changed when status is already fresh.
        RenderStatusParts(state, current, meili);

        // Provider, URL and key cannot be told apart from environment-managed ones without the sensitive status.
        bool Writable(string key) => !Managed(key) && (!unknown || key is not (R.Provider or R.MeilisearchUrl or R.MeilisearchApiKey));
        _saveBar.State(current ? state.DirtyKeys.Count : 0, state.Saving, current && state.DirtyKeys.Any(Writable) && !state.Loading && !accessBlocked && !state.Conflict && !state.OutcomeUnknown);
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
        _rows[key].HelpTarget = _controls[key];
        _rows[key].RefreshHelp();
    }

    private static void SetSelected(ComboBox select, string value)
    {
        if (value.Length == 0) { if (select.SelectedIndex != -1) select.SelectedIndex = -1; return; }
        var match = select.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
        if (match == null) { match = new() { Content = value, Tag = value, CornerRadius = new(6), MinHeight = 32 }; select.Items.Add(match); }
        if (!ReferenceEquals(select.SelectedItem, match)) select.SelectedItem = match;
    }

    private static string ErrorMessage(AdminSearchSettingsState state)
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

    /// <summary>Retirement clears private values and every draft control; nothing from the previous authority remains.</summary>
    private void ClearUi()
    {
        _syncing = true;
        try
        {
            _typed.Clear();
            foreach (var control in _controls.Values)
            {
                switch (control)
                {
                    case TextBox text: text.Text = ""; text.IsEnabled = false; break;
                    case PasswordBox secret: secret.Password = ""; _apiKeySynced = ""; secret.IsEnabled = false; break;
                    case ToggleSwitch toggle: toggle.IsOn = false; toggle.IsEnabled = false; break;
                    case ComboBox select: select.SelectedIndex = -1; select.IsEnabled = false; break;
                }
            }
            U.UpdateLiveText(_checkResult, ""); _summary.Text = ""; _summary.Visibility = Visibility.Collapsed;
            _secretAction.Visibility = Visibility.Collapsed;
        }
        finally { _syncing = false; }
        ClearStatusUi();
    }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetValue(_location, static _ => new ScrollPosition()).Offset = _scroller.VerticalOffset;
        _authority.AvailabilityChanged -= OnAuthorityChanged; _dialog?.Hide(); _dialog = null;
        if (App.MainWindowInstance is { } window) { window.Activated -= OnWindowActivated; window.VisibilityChanged -= OnWindowVisibilityChanged; }
        if (XamlRoot != null) XamlRoot.Changed -= OnXamlRootChanged;
        // Disposing the controller retires its authority lifetime: pending reads, checks, rebuilds and saves are cancelled and the poll stops.
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Dispose(); _controller = null; }
        _leave = null;
        ClearUi();
        _alerts.Visibility = Visibility.Collapsed;
        _fields.Visibility = _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
