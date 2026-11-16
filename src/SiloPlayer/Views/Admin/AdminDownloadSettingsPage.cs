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
using R = SiloPlayer.Core.Services.Admin.AdminDownloadSettingsRules;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Manage > Downloads > Settings (official <c>DownloadsSettings.tsx</c>): the server-wide prepared-download
/// configuration. It reads and writes the SERVER's <c>download.*</c> settings only; it never touches the signed-in
/// user's own offline-download folder or device settings.
/// </summary>
public sealed partial class AdminDownloadSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private static readonly string StorageHref = "/admin/downloads";
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 32), Visibility = Visibility.Collapsed };
    private readonly Grid _titleRow = new() { ColumnSpacing = 16, RowSpacing = 8 };
    private readonly HyperlinkButton _storageLink = new();
    private readonly TextBlock _subtitle;
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _errorText = U.Text("", 13);
    private readonly Button _retry = U.Action(T("common.retry"), "DownloadsRetry");
    private readonly Button _reload = U.Action(T("web.admin.settings.notifications.reload"), "DownloadsReload");
    private readonly StackPanel _loading = new() { Spacing = 24 };
    private readonly Dictionary<string, FrameworkElement> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _descriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _scales = new(StringComparer.Ordinal);
    private readonly AdminSettingsGroup _group = new(T("web.admin.settings.downloads.group"));
    private readonly AdminSettingsSaveBar _saveBar = new("Downloads");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly StackPanel _advanced = new() { MinWidth = 0 };
    private readonly StackPanel _railFields = new();
    private readonly Border _rail;
    private readonly Button _disclosure;
    private readonly AdminMonitorAuthority _authority;
    private AdminDownloadSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private AdminRouteRequest? _location;
    private LoggingDisclosurePreferences? _preferences;
    private double? _restoreOffset;
    private bool _active, _syncing, _opening, _storageAvailable, _disclosureHover;
    // AdvancedSection: persisted choice, a session override that outranks the current reason to force it open, and that reason.
    private bool _persistedOpen, _forced, _advancedOpen;
    private bool? _override;

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminDownloadSettingsController? Controller => _controller;

    private static readonly Lazy<LoggingDisclosurePreferences> DefaultPreferences = new(() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "monitor-ui")));

    public AdminDownloadSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminDownloadSettingsPage");
        AutomationProperties.SetName(_scroller, T("web.admin.settings.layout.regionLabel", new { label = T("web.admin.settings.downloads.title") }));

        // Header: title with the storage link level with it (below it when narrow), then the shared settings description.
        _titleRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _titleRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _titleRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _titleRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _titleRow.Children.Add(U.Title(T("web.admin.settings.downloads.title")));
        var linkText = new TextBlock { Text = T("web.admin.settings.downloads.storageLink"), FontSize = 14, TextDecorations = Windows.UI.Text.TextDecorations.Underline, TextWrapping = TextWrapping.Wrap };
        _storageLink.Content = linkText; _storageLink.Padding = new(0, 4, 0, 4); _storageLink.VerticalAlignment = VerticalAlignment.Center;
        _storageLink.HorizontalAlignment = HorizontalAlignment.Right; _storageLink.Foreground = U.Brush("PrimaryTextBrush");
        _storageLink.UseSystemFocusVisuals = true; _storageLink.Background = new SolidColorBrush(Colors.Transparent);
        AutomationProperties.SetAutomationId(_storageLink, "DownloadsStorageLink");
        AutomationProperties.SetName(_storageLink, linkText.Text);
        _storageLink.Click += (_, _) => { if (_storageAvailable) AdminShellPage.TryOpenHref(StorageHref); };
        Grid.SetColumn(_storageLink, 1); _titleRow.Children.Add(_storageLink);
        _titleRow.SizeChanged += (_, args) => LayoutTitle(args.NewSize.Width);
        _header.Children.Add(_titleRow);
        _subtitle = U.Text(T("web.admin.settings.search.downloads.description"), 14, true);
        _subtitle.MaxWidth = 800; _subtitle.LineHeight = 22.75; _subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        _header.Children.Add(_subtitle); _body.Children.Add(_header);

        AutomationProperties.SetName(_loading, T("web.admin.settings.fields.loadingSettings"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(U.Skeleton(32, 160));
        var skeletonRows = new StackPanel { Spacing = 16 };
        for (var row = 0; row < 2; row++) skeletonRows.Children.Add(U.Skeleton(40));
        _loading.Children.Add(skeletonRows);
        AutomationProperties.SetAutomationId(_errorText, "DownloadsErrorText");
        _alerts.Children.Add(U.Alert(_errorText)); _alerts.Children.Add(_retry); _alerts.Children.Add(_reload);
        _retry.Visibility = _reload.Visibility = Visibility.Collapsed;
        _retry.HorizontalAlignment = _reload.HorizontalAlignment = HorizontalAlignment.Left;
        _retry.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: false); };
        _reload.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true); };
        _body.Children.Add(_loading); _body.Children.Add(_alerts); _body.Children.Add(_fields);
        _fields.Children.Add(_group);

        // Essential rows, always visible, in the official order.
        AddToggle(_group, R.Enabled, "enabled", "web.admin.settings.downloads.enabled.description");
        AddToggle(_group, R.TranscodeEnabled, "transcode", "web.admin.settings.downloads.transcode.description");
        // Not a limit: the server reads 0 as "use the built-in worker count" (2), never unlimited.
        AddNumber(_group, R.MaxConcurrentPrepares, "web.admin.settings.downloads.prepares.label", "web.admin.settings.downloads.prepares.description");
        AddPath();
        AddLimit(_group, R.ArtifactMaxBytes, "web.admin.settings.downloads.budget.label", "web.admin.settings.downloads.budget.hint", "web.admin.common.units.gigabytes", R.BytesPerGigabyte);
        AddNumber(_group, R.DiskCeilingPercent, "web.admin.settings.downloads.diskCeiling.label", "web.admin.settings.downloads.diskCeiling.description");
        AddLimit(_group, R.ServerBandwidth, "web.admin.settings.downloads.serverBandwidth.label", "web.admin.settings.downloads.serverBandwidth.hint", "web.admin.common.units.megabitsPerSecond");

        // Advanced: per-user caps first under their own heading, then the whole-server rows.
        _disclosure = NewDisclosure();
        _rail = new Border
        {
            Child = _railFields, BorderThickness = new(2, 0, 0, 0), Margin = new(6, 0, 0, 0), Padding = new(15, 0, 0, 0),
            BorderBrush = AdminMonitorUi.Solid(Windows.UI.Color.FromArgb(255, 167, 139, 250), .32), Visibility = Visibility.Collapsed,
        };
        _disclosure.Click += (_, _) => ToggleAdvanced();
        _advanced.Children.Add(_disclosure); _advanced.Children.Add(_rail); _group.Add(_advanced);
        _railFields.Children.Add(Subheading(T("web.admin.settings.downloads.perUser.heading")));
        AddLimit(null, R.UserBandwidth, "web.admin.settings.downloads.perUser.bandwidth.label", "", "web.admin.common.units.megabitsPerSecond");
        AddLimit(null, R.MaxConcurrentPerUser, "web.admin.settings.downloads.perUser.concurrent.label", "web.admin.settings.downloads.perUser.concurrent.hint");
        AddLimit(null, R.MaxPerPeriod, "web.admin.settings.downloads.perUser.perPeriod.label", "web.admin.settings.downloads.perUser.perPeriod.hint");
        AddText(null, R.PeriodDuration, "web.admin.settings.downloads.perUser.period.label", "web.admin.settings.downloads.perUser.period.description");
        _railFields.Children.Add(Subheading(T("web.admin.settings.downloads.server.heading")));
        AddToggle(null, R.LocalTranscodeFallback, "server.localFallback", "web.admin.settings.downloads.server.localFallback.description");
        AddNumber(null, R.CacheHours, "web.admin.settings.downloads.server.cacheHours.label", "web.admin.settings.downloads.server.cacheHours.description");
        _railFields.Children.OfType<AdminSettingsRow>().Last().BorderThickness = new(0);
        RenderDisclosure();

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

    private static string T(string key, object? args = null) => AdminText.T(key, args);

    private void LayoutTitle(double width)
    {
        var narrow = width < 560;
        Grid.SetRow(_storageLink, narrow ? 1 : 0); Grid.SetColumn(_storageLink, narrow ? 0 : 1);
        Grid.SetColumnSpan(_storageLink, narrow ? 2 : 1);
        _storageLink.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void FitScrollContent()
    {
        // Give the outer frame its viewport width so the 1120px settings column starts at the common gutter.
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    // ------------------------------------------------------------------ row builders

    private AdminSettingsRow Place(AdminSettingsGroup? group, string key, string label, string descriptionKey, FrameworkElement editor, string unit = "")
    {
        var description = descriptionKey.Length > 0 ? T(descriptionKey) : "";
        // A composite editor's inner controls carry their own names; a unit-less row must not reserve a unit slot of its own.
        var row = new AdminSettingsRow(label, description, editor, unit.Length > 0 ? unit : null, nameEditor: editor is not (LimitEditor or PathEditor));
        // Inline validation message under the description; a live region so the reason is announced.
        var error = U.Text("", 13); error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        row.AddStatus(error); _errors[key] = error;
        _controls[key] = editor; _rows[key] = row; _descriptions[key] = description;
        if (group != null) group.Add(row); else _railFields.Children.Add(row);
        return row;
    }

    private void AddToggle(AdminSettingsGroup? group, string key, string labelKey, string descriptionKey)
    {
        var label = T($"web.admin.settings.downloads.{labelKey}.label");
        var toggle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        AutomationProperties.SetAutomationId(toggle, "Downloads_" + key);
        AutomationProperties.SetName(toggle, label);
        Place(group, key, label, descriptionKey, toggle);
        toggle.Toggled += (_, _) => Set(key, toggle.IsOn ? "true" : "false");
    }

    private void AddText(AdminSettingsGroup? group, string key, string labelKey, string descriptionKey)
    {
        var label = T(labelKey);
        var input = U.Input(label, "Downloads_" + key);
        Place(group, key, label, descriptionKey, input);
        input.TextChanging += (_, _) => Set(key, input.Text);
    }

    private void AddNumber(AdminSettingsGroup? group, string key, string labelKey, string descriptionKey)
    {
        var label = T(labelKey);
        var input = U.Input(label, "Downloads_" + key, "", number: true);
        Place(group, key, label, descriptionKey, input);
        input.TextChanging += (_, _) => Set(key, input.Text);
    }

    private void AddLimit(AdminSettingsGroup? group, string key, string labelKey, string hintKey, string unitKey = "", long scale = 1)
    {
        var label = T(labelKey);
        var editor = new LimitEditor(label, key);
        _scales[key] = scale;
        var row = Place(group, key, label, hintKey, editor, unitKey.Length > 0 ? T(unitKey) : "");
        row.LayoutChanged += editor.SetWide;
        editor.Box.TextChanging += (_, _) => Set(key, R.ToStoredUnits(editor.Box.Text, scale));
        // Checked/Unchecked rather than Click so a keyboard, pointer or automation toggle all reach the same path; programmatic sync is guarded.
        void ToggleUnlimited(object? sender, RoutedEventArgs args)
        {
            if (_syncing || _controller?.State is not { Loaded: true } state) return;
            var current = state.Values.GetValueOrDefault(key, "");
            if (editor.Unlimited.IsChecked == true)
            {
                editor.LastLimit = R.IsUnlimited(current) ? "" : current;
                Set(key, R.Unlimited);
            }
            else Set(key, R.IsUnlimited(editor.LastLimit) ? "" : editor.LastLimit);
        }
        editor.Unlimited.Checked += ToggleUnlimited; editor.Unlimited.Unchecked += ToggleUnlimited;
    }

    private void AddPath()
    {
        var label = T("web.admin.settings.downloads.artifactDir.label");
        var editor = new PathEditor(label, R.ArtifactDir);
        var row = Place(_group, R.ArtifactDir, label, "web.admin.settings.downloads.artifactDir.description", editor);
        row.LayoutChanged += editor.SetWide;
        editor.Box.TextChanging += (_, _) => Set(R.ArtifactDir, editor.Box.Text);
        editor.Reset.Click += (_, _) => { if (!_syncing) _controller?.ResetArtifactDirectory(); };
    }

    private void Set(string key, string value)
    {
        if (!_syncing && _controller?.State is { Loaded: true } state && state.Values.GetValueOrDefault(key, "") != value) _controller.Set(key, value);
    }

    // ------------------------------------------------------------------ advanced disclosure (official AdvancedSection)

    private void ToggleAdvanced()
    {
        var next = !_advancedOpen;
        _override = next; _persistedOpen = next; _advancedOpen = next;
        try { (_preferences ?? DefaultPreferences.Value).Write("downloads", next); } catch { /* the disclosure still works this session */ }
        RenderDisclosure();
    }

    /// <summary>A reason to force the section open (a dirty or invalid field inside) outranks an older manual collapse.</summary>
    private void UpdateAdvanced(bool forced)
    {
        if (forced != _forced) { _forced = forced; if (forced) _override = null; }
        var open = _override ?? (_persistedOpen || forced);
        if (open == _advancedOpen && _disclosure.Content != null) return;
        _advancedOpen = open;
        RenderDisclosure();
    }

    // ------------------------------------------------------------------ lifetime

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
        _active = true; _controller = App.Services.GetRequiredService<AdminDownloadSettingsController>();
        _preferences = App.Services.GetService<LoggingDisclosurePreferences>();
        try { _persistedOpen = (_preferences ?? DefaultPreferences.Value).Read("downloads"); } catch { _persistedOpen = false; }
        _override = null; _forced = false; _advancedOpen = false;
        _storageAvailable = AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.PreparedFiles);
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
        if (_active && _controller?.State.Owner == null) { ClearUi(); _ = OpenAsync(); }
    });
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state); });
    }

    // ------------------------------------------------------------------ render

    private static bool IsAccessFailure(AdminFailureKind? kind) => kind is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
        or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;

    private void Render(AdminDownloadSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        if (!current) ClearUi();
        var accessBlocked = state.Error != null && IsAccessFailure(state.Failure);
        var loadFailed = current && state.Error != null && !state.Loaded && !state.Loading;
        _loading.Visibility = state.Loading && !state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = _loading.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var showFields = state.Loaded && current && !accessBlocked;
        _fields.Visibility = showFields ? Visibility.Visible : Visibility.Collapsed;
        // The storage link belongs to the loaded page; a failed read shows only the title and the alert.
        _storageLink.Visibility = showFields ? Visibility.Visible : Visibility.Collapsed;
        _storageLink.IsEnabled = _storageAvailable;
        ToolTipService.SetToolTip(_storageLink, _storageAvailable ? null : T("configure.native.downloads.storageUnavailable"));
        AutomationProperties.SetHelpText(_storageLink, _storageAvailable ? "" : T("configure.native.downloads.storageUnavailable"));
        // Retain the live region and the Retry/Reload control while a retry is in flight.
        _alerts.Visibility = current && (state.Error != null || state.Loading && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = loadFailed || (state.Loading && !state.Loaded && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _reload.Visibility = state.Loaded && state.Error != null ? Visibility.Visible : Visibility.Collapsed;
        _retry.IsEnabled = _reload.IsEnabled = !state.Saving && current;
        _retry.IsHitTestVisible = _reload.IsHitTestVisible = !state.Loading;
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
                var enabled = editable && !Managed(key);
                switch (control)
                {
                    case ToggleSwitch toggle:
                        // The server default for the local fallback is on; a blank value reads as on (official `value || "true"`).
                        toggle.IsOn = key == R.LocalTranscodeFallback ? value.Length == 0 || value == "true" : value == "true";
                        toggle.IsEnabled = enabled; break;
                    case TextBox text:
                        if (text.Text != value) text.Text = value;
                        text.IsEnabled = enabled; break;
                    case LimitEditor limit:
                        RenderLimit(limit, key, value, enabled); break;
                    case PathEditor path:
                        if (path.Box.Text != value) path.Box.Text = value;
                        path.Box.PlaceholderText = state.DerivedArtifactDirectory;
                        path.Box.IsEnabled = enabled;
                        // Nothing to reset while the field already runs the default, blank or typed verbatim.
                        path.Reset.Visibility = current && value.Length > 0 && value != state.DerivedArtifactDirectory ? Visibility.Visible : Visibility.Collapsed;
                        path.Reset.IsEnabled = enabled; break;
                }
            }
        }
        finally { _syncing = false; }

        foreach (var key in R.Keys)
        {
            var description = _descriptions[key];
            if (Managed(key)) description = (description.Length > 0 ? description + " " : "") + T("configure.native.settings.managedByEnvironment");
            Describe(key, description);
            SetError(key, current && state.Errors.TryGetValue(key, out var error) ? R.Describe(key, error) : "");
        }

        var allRestart = R.Keys.All(state.RestartKeys.Has);
        _group.State(R.Keys.Any(Dirty), allRestart ? T("web.admin.settings.fields.restartAll") : "");
        // The official Downloads rows carry no per-row unsaved dot; the group dot covers it.
        foreach (var key in R.Keys) _rows[key].State(false, !allRestart && state.RestartKeys.Has(key));
        UpdateAdvanced(current && R.AdvancedKeys.Any(Dirty));

        var writable = state.DirtyKeys.Any(key => !Managed(key)) && state.Errors.Keys.All(Managed);
        _saveBar.State(current ? state.DirtyKeys.Count : 0, state.Saving, current && writable && !state.Loading && !accessBlocked && !state.Conflict && !state.OutcomeUnknown);
        _saveBar.SetAvailableWidth(ActualWidth);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;
        if (state.Loaded && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); _scroller.Focus(FocusState.Programmatic); } });
        }
    }

    private void RenderLimit(LimitEditor limit, string key, string value, bool enabled)
    {
        var scale = _scales[key];
        var unlimited = R.IsUnlimited(value);
        if (limit.Unlimited.IsChecked != unlimited) limit.Unlimited.IsChecked = unlimited;
        limit.Unlimited.IsEnabled = enabled;
        var wanted = unlimited ? ""
            // Keeps what was typed while it still round-trips to the stored value, so "1." and "1.50" survive mid-keystroke.
            : R.ToStoredUnits(limit.Box.Text, scale) == value ? limit.Box.Text : R.ToDisplayUnits(value, scale);
        if (limit.Box.Text != wanted) limit.Box.Text = wanted;
        limit.Box.PlaceholderText = unlimited ? T("web.settings.limitField.unlimited") : "";
        limit.Box.IsEnabled = enabled && !unlimited;
    }

    private void SetError(string key, string text)
    {
        var block = _errors[key];
        U.UpdateLiveText(block, text);
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Describe(string key, string text)
    {
        var description = _rows[key].Description;
        if (description.Text != text) description.Text = text;
        description.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var target = _controls[key] switch { LimitEditor l => (FrameworkElement)l.Box, PathEditor p => p.Box, var other => other };
        AutomationProperties.SetHelpText(target, text);
    }

    private static string ErrorMessage(AdminDownloadSettingsState state)
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
        // An unread page gets the official sentence unless the failure names a specific recovery.
        if (!state.Loaded && !IsAccessFailure(state.Failure) && state.Failure is not (AdminFailureKind.RateLimited or AdminFailureKind.Validation))
            return T("web.admin.settings.downloads.loadError");
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
            foreach (var control in _controls.Values)
            {
                switch (control)
                {
                    case TextBox text: text.Text = ""; text.IsEnabled = false; break;
                    case ToggleSwitch toggle: toggle.IsOn = false; toggle.IsEnabled = false; break;
                    case LimitEditor limit: limit.Box.Text = ""; limit.Unlimited.IsChecked = false; limit.LastLimit = ""; limit.Box.IsEnabled = limit.Unlimited.IsEnabled = false; break;
                    case PathEditor path: path.Box.Text = ""; path.Reset.Visibility = Visibility.Collapsed; path.Box.IsEnabled = false; break;
                }
            }
            foreach (var key in _errors.Keys) SetError(key, "");
        }
        finally { _syncing = false; }
    }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetValue(_location, static _ => new ScrollPosition()).Offset = _scroller.VerticalOffset;
        _authority.AvailabilityChanged -= OnAuthorityChanged; _dialog?.Hide(); _dialog = null;
        // Disposing the controller retires its authority lifetime, cancelling an in-flight save wait.
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Dispose(); _controller = null; }
        _leave = null;
        ClearUi();
        _alerts.Visibility = Visibility.Collapsed;
        _fields.Visibility = _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
