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
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminStorageRules;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure › Server › Storage (official StorageSettings.tsx): artwork backend, local path and provider artwork,
/// public and private S3 storage with connection checks and asset-link authorization, the managed storage transition
/// review, and the active/failed/restart/recovery transition panels. Credentials are write-only. Polling runs only
/// while this page and its window are in front.
/// </summary>
public sealed partial class AdminStorageSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private static readonly Windows.UI.Color Green600 = Windows.UI.Color.FromArgb(255, 0x16, 0xA3, 0x4A);
    private static readonly Windows.UI.Color Red600 = Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26);
    private static readonly Lazy<LoggingDisclosurePreferences> DefaultPreferences = new(() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "monitor-ui")));

    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 32), Visibility = Visibility.Collapsed };
    private readonly StackPanel _loading = new() { Spacing = 24, Visibility = Visibility.Collapsed };
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _errorText = U.Text("", 13);
    private readonly Button _reload = U.Action(T("web.admin.settings.notifications.reload"), "StorageReload");
    // Official fail-closed page alerts: credential status or storage lock status unavailable.
    private readonly StackPanel _blocked = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _blockedTitle = U.Text("", 14);
    private readonly Button _blockedReload = U.Action(T("web.admin.settings.notifications.reload"), "StorageBlockedReload");
    private readonly StackPanel _fields = new() { Spacing = 20, Visibility = Visibility.Collapsed };
    private readonly Border _lockAlert;
    private readonly Button _retryStatus = U.Action(T("web.admin.settings.storage.retryStatus"), "StorageRetryStatus");
    private readonly Border _unknownAlert;
    private readonly TextBlock _unknownText = U.Text("", 13);
    private readonly Button _unknownRefresh = U.Action(T("web.admin.settings.storage.active.refresh"), "StorageCommandUnknownRefresh");
    private readonly AdminSettingsGroup _storageGroup = new(T("web.admin.settings.storage.group"));
    private readonly ComboBox _backend;
    private readonly TextBox _localPath;
    private readonly ToggleSwitch _cacheImages = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly Dictionary<string, AdminSettingsRow> _storageRows = new(StringComparer.Ordinal);
    private readonly S3View _public;
    private readonly S3View _private;
    private readonly AdminSettingsSaveBar _saveBar = new("Storage");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly AdminMonitorAuthority _authority;
    private AdminStorageSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private LoggingDisclosurePreferences? _preferences;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _navigated, _syncing, _opening, _hostVisible = true, _hostFocused = true;

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true ||
        _controller?.State is { Owner.IsCurrent: true } state && (state.Saving || state.Queuing);
    public AdminStorageSettingsController? Controller => _controller;
    /// <summary>The open transition review dialog, if any (native fixtures await its Closed event).</summary>
    public ContentDialog? ReviewDialog => _review;

    public AdminStorageSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminStorageSettingsPage");
        AutomationProperties.SetName(_scroller, T("web.admin.settings.layout.regionLabel", new { label = T("web.admin.settings.storage.title") }));
        _header.Children.Add(U.Title(T("web.admin.settings.storage.title")));
        var subtitle = U.Text(T("web.admin.settings.search.storage.description"), 14, true);
        subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        _header.Children.Add(subtitle); _body.Children.Add(_header);

        // Official skeleton: a 32×192 title bar then six full-width 40 DIP bars.
        AutomationProperties.SetName(_loading, T("web.admin.settings.storage.loading"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(U.Skeleton(32, 192));
        var bars = new StackPanel { Spacing = 16 };
        for (var row = 0; row < 6; row++) bars.Children.Add(U.Skeleton(40));
        _loading.Children.Add(bars);
        _body.Children.Add(_loading);

        AutomationProperties.SetAutomationId(_errorText, "StorageErrorText");
        _alerts.Children.Add(U.Alert(_errorText)); _alerts.Children.Add(_reload);
        _reload.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: true); };
        _body.Children.Add(_alerts);

        AutomationProperties.SetAutomationId(_blockedTitle, "StorageBlockedText");
        _blocked.Children.Add(U.Alert(_blockedTitle, T("web.admin.settings.storage.reloadBeforeEditing")));
        _blocked.Children.Add(_blockedReload);
        _blockedReload.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(preserveDraft: false); };
        _body.Children.Add(_blocked);
        _body.Children.Add(_fields);

        // Lock status unknown: other settings remain editable; location fields wait for a successful retry.
        var lockText = U.Text(T("web.admin.settings.storage.lockStatusUnavailable"), 14); lockText.FontWeight = FontWeights.Medium;
        AutomationProperties.SetAutomationId(lockText, "StorageLockStatusText");
        var lockContent = new StackPanel { Spacing = 4 };
        lockContent.Children.Add(lockText); lockContent.Children.Add(U.Text(T("web.admin.settings.storage.lockStatusRetryHint"), 12, true));
        _lockAlert = Card(lockContent, _retryStatus, AdminMonitorUi.Red500, .2, .05, "alert");
        _retryStatus.Click += (_, _) => _controller?.RetryStatus();
        _fields.Children.Add(_lockAlert);
        AutomationProperties.SetAutomationId(_unknownText, "StorageCommandUnknownText");
        // Ambiguous commands retain an explicit discovery action even when no active job is known.
        _unknownAlert = Card(_unknownText, _unknownRefresh, AdminMonitorUi.Amber500, .25, .05, "alert");
        _unknownRefresh.Click += (_, _) => _controller?.RefreshTransitions();
        _fields.Children.Add(_unknownAlert);

        // Storage group.
        _backend = U.Select(T("web.admin.settings.storage.backend"), "Storage_" + R.Backend,
            [("auto", T("web.admin.settings.storage.backendAuto")), ("local", T("web.admin.settings.storage.backendLocal")), ("s3", "S3")]);
        AddStorageRow(R.Backend, T("web.admin.settings.storage.backend"), _backend, T("web.admin.settings.storage.backendDescription"));
        _backend.SelectionChanged += (_, _) =>
        {
            if (_syncing || (_backend.SelectedItem as ComboBoxItem)?.Tag is not string value || _controller?.State is not { Loaded: true } state) return;
            if (state.ArtworkStorage != null && value != R.SelectedBackend(state, state.ArtworkStorage)) _controller.ChangeBackend(value);
        };
        _localPath = U.Input(T("web.admin.settings.storage.localPath"), "Storage_" + R.LocalPath, R.LocalPathExample);
        AddStorageRow(R.LocalPath, T("web.admin.settings.storage.localPath"), _localPath, T("web.admin.settings.storage.localPathDescription"));
        _localPath.TextChanging += (_, _) => SetText(R.LocalPath, _localPath.Text);
        AutomationProperties.SetAutomationId(_cacheImages, "Storage_" + R.CacheImages);
        AddStorageRow(R.CacheImages, T("web.admin.settings.storage.cacheImages"), _cacheImages, T("web.admin.settings.storage.cacheImagesDescription"));
        _cacheImages.Toggled += (_, _) => SetText(R.CacheImages, _cacheImages.IsOn ? "true" : "false");
        _fields.Children.Add(_storageGroup);

        BuildTransitionPanels();

        _public = new S3View(this, isPublic: true);
        _private = new S3View(this, isPublic: false);
        _fields.Children.Add(_public.Group); _fields.Children.Add(_private.Group);
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
        Loaded += (_, _) => { FitScrollContent(); if (_navigated) Activate(); };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!IsLoaded) Close(); });
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); _saveBar.Margin = new(16, 0, 16, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? -8 : 8); };
    }

    private static string T(string key, object? args = null) => AdminText.T(key, args);

    private void FitScrollContent()
    {
        // Give the outer frame its viewport width so the 1120 DIP settings column starts at the common gutter.
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available <= 0) return;
        _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
        _body.Width = Math.Min(1120, _scrollContent.Width);
    }

    private void AddStorageRow(string key, string label, Control editor, string description)
    {
        AutomationProperties.SetAutomationId(editor, "Storage_" + key);
        var row = new AdminSettingsRow(label, description, editor);
        _storageRows[key] = row; _storageGroup.Add(row);
    }

    /// <summary>Forwards a text or toggle edit unless it is the render echo of the current value.</summary>
    private void SetText(string key, string value)
    {
        if (_syncing || _controller?.State is not { Loaded: true } state) return;
        if (state.Get(key) == value || DisplayValue(key, state.Get(key)) == value) return;
        _controller.Set(key, value);
    }

    private static string DisplayValue(string key, string value) => key switch
    {
        R.UrlAuth => R.DisplayUrlAuth(value),
        R.TokenParam => R.DisplayTokenParam(value),
        R.TokenTtl => R.DisplayTokenTtl(value),
        _ => value,
    };

    /// <summary>A bordered panel (official <c>rounded-xl border p-4</c>) with an optional trailing action.</summary>
    private static Border Card(UIElement content, FrameworkElement? action, Windows.UI.Color? tone, double borderAlpha, double fillAlpha, string? role)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(content);
        if (action != null) { action.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(action, 1); grid.Children.Add(action); }
        var card = new Border
        {
            Child = grid, Padding = new(16), CornerRadius = new(16), BorderThickness = new(1), Visibility = Visibility.Collapsed,
            BorderBrush = tone is { } color ? AdminMonitorUi.Solid(color, borderAlpha) : AdminMonitorUi.Solid(AdminMonitorUi.Border, .6),
            Background = tone is { } fill ? AdminMonitorUi.Solid(fill, fillAlpha) : AdminMonitorUi.Solid(AdminMonitorUi.Surface, .4),
        };
        if (role == "alert") AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Assertive);
        else if (role == "status") AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Polite);
        // Narrow layouts stack the action under the text (official flex-col → sm:flex-row).
        card.SizeChanged += (_, args) =>
        {
            if (action == null) return;
            var narrow = args.NewSize.Width < 640;
            Grid.SetColumn(action, narrow ? 0 : 1); Grid.SetRow(action, narrow ? 1 : 0);
            if (grid.RowDefinitions.Count == 0) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            action.HorizontalAlignment = HorizontalAlignment.Left;
        };
        return card;
    }

    // ── Navigation and activity ──────────────────────────────

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _location = e.Parameter as AdminRouteRequest;
        _restoreOffset = e.NavigationMode == NavigationMode.Back && _location != null && ScrollPositions.TryGetValue(_location, out var position) ? position.Offset : 0;
        _navigated = true;
        base.OnNavigatedTo(e);
        if (IsLoaded) Activate();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); _navigated = false; base.OnNavigatedFrom(e); }

    /// <summary>Outer Admin departure: retire reads, polls, commands, dialogs and drafts.</summary>
    public void SuspendAdminPage() => Close();

    private void Activate()
    {
        if (_active) return;
        _active = true;
        var player = App.Services.GetService<PlayerService>();
        _controller = new AdminStorageSettingsController(App.Services.GetRequiredService<AdminServerSettingsApi>(), App.Services.GetRequiredService<AdminServerSettingsStore>(),
            App.Services.GetRequiredService<AdminStorageApi>(), _authority, deferBackground: () => player?.IsLoading == true);
        _preferences = App.Services.GetService<LoggingDisclosurePreferences>();
        _public.LoadDisclosure(_preferences ?? DefaultPreferences.Value);
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.DirtyKeys.Count > 0, () => _controller?.Discard());
        _controller.Changed += QueueRender; _controller.Saved += OnSaved; _controller.Notice += OnNotice;
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
        if (!_active) return;
        // A replaced authority never inherits a review, typed credentials or command state.
        if (_controller?.State.Owner == null) { HideReview(); ClearSecrets(); _ = OpenAsync(); }
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
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller)) Render(controller.State); });
    }

    private void OnNotice(AdminStorageNotice notice)
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || !ReferenceEquals(controller, _controller) || controller?.State.Owner?.IsCurrent != true) return;
            var text = notice.JobId != null ? T(notice.Key, new { id = notice.JobId }) : T(notice.Key);
            LastNotice = text;
            var toasts = App.Services.GetService<ToastService>();
            if (notice.Success) toasts?.Success(text); else toasts?.Error(text);
        });
    }

    /// <summary>The most recent toast text, for native fixtures (toasts carry no credentials or locations).</summary>
    public string? LastNotice { get; private set; }

    private void OnSaved(IReadOnlyCollection<string> keys)
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || controller == null || !ReferenceEquals(controller, _controller) || controller.State.Owner?.IsCurrent != true) return;
            if (App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell) shell.RefreshSettingsStatus();
        });
    }

    // ── Render ───────────────────────────────────────────────

    private void Render(AdminStorageSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        if (!current) { ClearSecrets(); HideReview(); }
        var accessBlocked = state.Error != null && state.Failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
            or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;
        // Official order: credential status failure, then lock status unavailable, then the skeleton.
        var blockedKey = !current || !state.Loaded ? null
            : state.SensitiveStatusError ? "web.admin.settings.storage.credentialStatusUnavailable"
            : state.StatusUnavailable ? "web.admin.settings.storage.lockStatusUnavailable" : null;
        var loading = current && blockedKey == null && state.Error == null && (!state.Loaded || state.ArtworkStorage == null);
        _loading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        _blocked.Visibility = blockedKey != null ? Visibility.Visible : Visibility.Collapsed;
        if (blockedKey != null) U.UpdateLiveText(_blockedTitle, T(blockedKey));
        _blockedReload.IsEnabled = current && !state.Saving;
        _blockedReload.IsHitTestVisible = !state.Loading;
        _alerts.Visibility = current && (state.Error != null || state.Loading && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _reload.IsEnabled = current && !state.Saving && !state.Queuing;
        _reload.IsHitTestVisible = !state.Loading;
        if (state.Error != null) U.UpdateLiveText(_errorText, ErrorMessage(state));
        var show = current && state.Loaded && !accessBlocked && blockedKey == null && state.ArtworkStorage != null;
        _fields.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) RenderFields(state, state.ArtworkStorage!);
        else _saveBar.State(0, false, false);
        RenderReview(state);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;
        if (state.Loaded && show && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); _scroller.Focus(FocusState.Programmatic); } });
        }
    }

    private void RenderFields(AdminStorageSettingsState state, AdminArtworkStorageStatus status)
    {
        bool Managed(string key) => state.ManagedByEnv.Contains(key);
        var known = state.StatusKnown;
        var editable = !state.Loading && !state.SensitiveStatusError && state.Review == null && !state.Queuing;
        _lockAlert.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
        _retryStatus.IsEnabled = !state.StatusLoading;
        _unknownAlert.Visibility = state.CommandUnknownKey != null ? Visibility.Visible : Visibility.Collapsed;
        if (state.CommandUnknownKey != null) U.UpdateLiveText(_unknownText, T(state.CommandUnknownKey));

        var selected = R.SelectedBackend(state, status);
        _syncing = true;
        try
        {
            SetSelected(_backend, selected);
            _backend.IsEnabled = editable && known && !Managed(R.Backend);
            var path = state.Get(R.LocalPath);
            if (_localPath.Text != path) _localPath.Text = path;
            _localPath.IsEnabled = editable && known && !(status.Locked && R.CurrentSourceIsS3(status)) && !state.Saving && !Managed(R.LocalPath);
            _cacheImages.IsOn = state.Get(R.CacheImages) == "true";
            _cacheImages.IsEnabled = editable && !Managed(R.CacheImages);
        }
        finally { _syncing = false; }
        Describe(_storageRows[R.Backend], _backend, Managed(R.Backend) ? T("configure.native.settings.managedByEnvironment")
            : status.Locked ? T(status.Backend == "s3" ? "web.admin.settings.storage.backendLockedS3" : "web.admin.settings.storage.backendLockedLocal")
            : T("web.admin.settings.storage.backendDescription"));
        Describe(_storageRows[R.LocalPath], _localPath, Managed(R.LocalPath) ? T("configure.native.settings.managedByEnvironment")
            : status.Locked ? T(status.Backend == "s3" ? "web.admin.settings.storage.localPathLockedS3" : "web.admin.settings.storage.localPathLockedLocal")
            : T("web.admin.settings.storage.localPathDescription"));
        Describe(_storageRows[R.CacheImages], _cacheImages, Managed(R.CacheImages) ? T("configure.native.settings.managedByEnvironment") : T("web.admin.settings.storage.cacheImagesDescription"));
        var storageRestartAll = R.StorageGroupKeys.All(state.RestartKeys.Has);
        _storageGroup.State(R.StorageGroupKeys.Any(state.IsDirty), storageRestartAll ? T("web.admin.settings.fields.restartAll") : "");
        foreach (var key in R.StorageGroupKeys) _storageRows[key].State(state.IsDirty(key), !storageRestartAll && state.RestartKeys.Has(key));

        RenderTransitionPanels(state);

        var publicPending = R.PublicBackendChangePending(state, status, state.LatestTransition);
        _public.Render(state, status, R.ShowPublicStorage(state, selected), status.Locked ? status.Backend : null, publicPending, editable);
        _private.Render(state, status, R.ShowPrivateStorage(state, selected), status.Locked || status.PrivateLocked ? status.Backend : null, false, editable);

        var pending = known && R.LocationChangePending(state, status, state.LatestTransition);
        var writable = state.DirtyKeys.Any(key => !Managed(key));
        _saveBar.State(state.DirtyKeys.Count, state.Saving, writable && editable && !state.Conflict && !state.OutcomeUnknown,
            pending ? T("web.admin.settings.storage.reviewTransition") : null);
        _saveBar.SetAvailableWidth(ActualWidth);
    }

    private static void Describe(AdminSettingsRow row, Control control, string text)
    {
        if (row.Description.Text != text) row.Description.Text = text;
        row.Description.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(control, text);
    }

    private static void SetSelected(ComboBox select, string value)
    {
        var match = select.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
        if (match == null) { match = new() { Content = value, Tag = value, CornerRadius = new(6), MinHeight = 32 }; select.Items.Add(match); }
        if (!ReferenceEquals(select.SelectedItem, match)) select.SelectedItem = match;
    }

    private static string ErrorMessage(AdminStorageSettingsState state)
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

    // ── Guard and teardown ───────────────────────────────────

    public Task<bool> CanLeaveAsync()
    {
        if (_controller?.State is { Owner.IsCurrent: true } state && (state.Saving || state.Queuing)) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog ?? _review, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }

    private void ClearSecrets() { _public.ClearSecrets(); _private.ClearSecrets(); }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetValue(_location, static _ => new ScrollPosition()).Offset = _scroller.VerticalOffset;
        if (App.MainWindowInstance is { } window) { window.Activated -= OnWindowActivated; window.VisibilityChanged -= OnWindowVisibilityChanged; }
        if (XamlRoot != null) XamlRoot.Changed -= OnXamlRootChanged;
        _authority.AvailabilityChanged -= OnAuthorityChanged;
        _dialog?.Hide(); _dialog = null;
        HideReview();
        // Disposing retires the owner: in-flight reads, polls, saves and commands are cancelled and their late results discarded.
        if (_controller != null)
        {
            _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Notice -= OnNotice;
            _controller.Dispose(); _controller = null;
        }
        _leave = null; LastNotice = null;
        _syncing = true;
        try
        {
            _backend.SelectedIndex = -1; _backend.IsEnabled = false;
            _localPath.Text = ""; _localPath.IsEnabled = false;
            _cacheImages.IsOn = false; _cacheImages.IsEnabled = false;
            _public.Clear(); _private.Clear();
        }
        finally { _syncing = false; }
        _alerts.Visibility = _blocked.Visibility = _fields.Visibility = Visibility.Collapsed;
        _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
