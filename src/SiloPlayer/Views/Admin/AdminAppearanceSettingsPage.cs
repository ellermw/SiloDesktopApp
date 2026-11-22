using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;
using A = SiloPlayer.Controls.AdminMonitorUi;
using C = SiloPlayer.Core.Services.Admin.AdminAppearanceSettingsController;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceText;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Admin Configure &gt; Server &gt; Appearance, official web/src/pages/admin-settings/AppearanceSettings.tsx
/// at Silo Server 66b836f82: logos and icons, accent color, card overlay defaults with the
/// live preview, the badge-image library and the title-art default. The four settings save
/// as one batch through the save bar; images keep their own single-shot writes.
/// </summary>
public sealed partial class AdminAppearanceSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private static readonly string[] AccentPresets = ["#4f46e5", "#0ea5e9", "#10b981", "#f59e0b", "#ef4444", "#ec4899", "#8b5cf6", "#64748b"];
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new() { Padding = new(0, 0, 0, 112) };
    private readonly StackPanel _body = new() { Spacing = 0, MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _error = U.Text("", 13);
    private readonly TextBlock _loading = U.Text(T.T("web.admin.settings.appearance.loading"));
    private readonly StackPanel _alert = new() { Spacing = 8, Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 16) };
    private readonly Button _reload = U.Action(AdminText.T("web.admin.settings.notifications.reload"), "AppearanceReload");
    private readonly AdminSettingsSaveBar _saveBar = new("Appearance");
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToggleButton> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToggleSwitch _overlaysEnabled = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly ToggleSwitch _titleArt = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly ColorPicker _accentPicker = new() { IsAlphaEnabled = false, IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false, IsMoreButtonVisible = false };
    private readonly Flyout _accentFlyout = new();
    private readonly ToggleButton _customAccent = new() { Height = 32, Padding = new(10, 0, 10, 0), CornerRadius = new(8) };
    private readonly Button _restore = new() { Padding = new(6, 2, 6, 2), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
    private readonly AdminMonitorAuthority _authority;
    private readonly CardOverlayService _overlays;
    private readonly AdminAppearanceOverlayEditor _overlayEditor;
    private readonly AdminSettingsGroup _logos;
    private readonly AdminSettingsGroup _colors;
    private readonly AdminSettingsGroup _overlayGroup;
    private readonly AdminSettingsGroup _badgeImages;
    private readonly AdminSettingsGroup _titles;
    private readonly Border _storageWarning;
    private AdminAppearanceSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private bool _active;
    private bool _navigated;
    private bool _opening;
    private AdminAppearanceSettingsController? _openingController;
    private bool _syncing;
    private bool _accentOpen;

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminAppearanceSettingsController? Controller => _controller;
    internal AdminAppearanceOverlayEditor OverlayEditor => _overlayEditor;

    public AdminAppearanceSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        _overlays = App.Services.GetRequiredService<CardOverlayService>();
        _overlayEditor = new(_overlays);
        AutomationProperties.SetAutomationId(this, "AdminAppearanceSettingsPage");
        _scrollContent.Children.Add(_body);
        _scroller.Content = _scrollContent;
        _root.Children.Add(_scroller);
        _root.Children.Add(_saveBar);
        Content = _root;
        _body.Children.Add(U.Title(T.T("web.admin.settings.appearance.title")));
        AutomationProperties.SetAutomationId(_loading, "AppearanceLoading");
        _body.Children.Add(_loading);
        AutomationProperties.SetAutomationId(_error, "AppearanceError");
        _alert.Children.Add(U.Alert(_error));
        _alert.Children.Add(_reload);
        _body.Children.Add(_alert);
        _reload.Click += async (_, _) => { if (_controller is { } controller) await controller.ReloadAsync(); };

        _logos = new(T.T("web.admin.settings.appearance.logos.group"));
        _storageWarning = U.Warning(T.T("web.admin.settings.appearance.logos.storageNeeded"), "AppearanceStorageWarning");
        _logos.Add(_storageWarning);
        AddAsset("wordmark", "wordmark", "wordmarkGuidance", 640, 8, "silo-wordmark-sidebar.png");
        AddAsset("mark", "mark", "markGuidance", 512, 8, "silo-icon-1024.png");
        AddAsset("favicon", "favicon", "faviconGuidance", 0, 1, "app.ico");
        AddAsset("login_bg", "loginBackground", "loginBackgroundGuidance", 2560, 12, null);
        _body.Children.Add(_logos);

        _colors = new(T.T("web.admin.settings.appearance.colors.group"));
        BuildColors();
        _body.Children.Add(_colors);

        _overlayGroup = new(T.T("web.admin.settings.appearance.overlays.group"));
        var restore = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        restore.Children.Add(A.Icon("rotate-ccw", 12, A.Muted));
        restore.Children.Add(U.Text(T.T("web.admin.settings.appearance.overlays.restoreDefaults"), 12, true));
        _restore.Content = restore;
        AutomationProperties.SetName(_restore, T.T("web.admin.settings.appearance.overlays.restoreDefaults"));
        AutomationProperties.SetAutomationId(_restore, "AppearanceRestoreOverlays");
        _restore.Click += async (_, _) => await RestoreDefaultsAsync();
        _overlayGroup.SetAction(_restore);
        var enabledRow = new AdminSettingsRow(T.T("web.admin.settings.appearance.overlays.enabled.label"),
            T.T("web.admin.settings.appearance.overlays.enabled.description"), _overlaysEnabled);
        _rows[C.OverlaysEnabledKey] = enabledRow;
        AutomationProperties.SetAutomationId(_overlaysEnabled, "AppearanceOverlaysEnabled");
        _overlaysEnabled.Toggled += (_, _) =>
        {
            if (_syncing || _controller?.State is not { } state) return;
            var value = _overlaysEnabled.IsOn ? "true" : "false";
            // The control shows "on" for an unset value; only a real change stages.
            if ((state.Values.GetValueOrDefault(C.OverlaysEnabledKey, "") != "false") != _overlaysEnabled.IsOn) Stage(C.OverlaysEnabledKey, value);
        };
        _overlayGroup.Add(enabledRow);
        _overlayGroup.Add(new Border { Child = _overlayEditor, Padding = new(0, 8, 0, 8) });
        _overlayEditor.Changed += document => Stage(C.OverlayDefaultsKey, document);
        _overlayEditor.ChooseImageRequested += () => _ = PickBadgeImageAsync();
        _overlayEditor.UploadRequested += name => _ = UploadBadgeImageAsync(name);
        _overlayEditor.DeleteRequested += reference => _ = DeleteBadgeImageAsync(reference);
        _body.Children.Add(_overlayGroup);

        _badgeImages = new(T.T("web.admin.settings.appearance.badgeImages.group"));
        _badgeImages.Add(new Border { Child = _overlayEditor.LibraryPanel, Padding = new(0, 12, 0, 12) });
        _body.Children.Add(_badgeImages);

        _titles = new(T.T("web.admin.settings.appearance.titlePages.group"));
        var titleRow = new AdminSettingsRow(T.T("settingsManifest.ui.title_art.label"),
            T.T("web.admin.settings.appearance.titlePages.titleArt.description"), _titleArt);
        _rows[C.TitleArtDefaultKey] = titleRow;
        AutomationProperties.SetAutomationId(_titleArt, "AppearanceTitleArt");
        _titleArt.Toggled += (_, _) =>
        {
            if (_syncing || _controller?.State is not { } state) return;
            if ((state.Values.GetValueOrDefault(C.TitleArtDefaultKey, "") != "false") != _titleArt.IsOn) Stage(C.TitleArtDefaultKey, _titleArt.IsOn ? "true" : "false");
        };
        _titles.Add(titleRow);
        _body.Children.Add(_titles);

        // Nothing but the title and the loading line shows before the first snapshot.
        foreach (var group in new[] { _logos, _colors, _overlayGroup, _badgeImages, _titles }) group.Visibility = Visibility.Collapsed;
        _saveBar.Save.Click += async (_, _) => { if (_controller is { } controller) await controller.SaveAsync(); };
        _saveBar.Discard.Click += (_, _) => _controller?.Discard();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); QueueRender(); };
        Loaded += (_, _) => { if (_navigated) { Activate(); QueueRender(); } };
        Unloaded += (_, _) => { if (!IsLoaded) Close(); };
    }

    private void BuildColors()
    {
        var choices = new LogsWrapPanel { Spacing = 8, MaxWidth = 300 };
        foreach (var hex in new[] { "" }.Concat(AccentPresets))
        {
            var swatch = new ToggleButton { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(16), Margin = new(0, 0, 0, 8) };
            if (hex == "")
            {
                swatch.Content = A.Icon("rotate-ccw", 14, A.Muted);
                // The default swatch keeps its muted fill; selection is the ring.
                var muted = A.Solid(A.Muted, .18);
                swatch.Background = muted;
                foreach (var key in new[] { "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed" }) swatch.Resources[key] = muted;
                foreach (var key in new[] { "ToggleButtonBorderBrushChecked", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed" }) swatch.Resources[key] = A.Solid(Microsoft.UI.Colors.White);
                swatch.BorderThickness = new(2);
                ToolTipService.SetToolTip(swatch, T.T("web.admin.settings.appearance.colors.accent.themeDefault"));
            }
            else
            {
                // Keep the swatch's own color in every state; selection shows as a ring and a check.
                var fill = A.Solid(ParseColor(hex));
                swatch.Background = fill;
                foreach (var key in new[] { "ToggleButtonBackgroundPointerOver", "ToggleButtonBackgroundPressed", "ToggleButtonBackgroundChecked",
                    "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed", "ToggleButtonBackgroundDisabled", "ToggleButtonBackgroundCheckedDisabled" })
                    swatch.Resources[key] = fill;
                foreach (var key in new[] { "ToggleButtonBorderBrushChecked", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed" })
                    swatch.Resources[key] = A.Solid(Microsoft.UI.Colors.White);
                swatch.BorderThickness = new(2);
            }
            AutomationProperties.SetName(swatch, hex == "" ? T.T("web.admin.settings.appearance.colors.accent.useDefault")
                : T.T("web.admin.settings.appearance.colors.accent.usePreset", ("hex", hex)));
            AutomationProperties.SetAutomationId(swatch, "AppearanceAccent" + (hex == "" ? "Default" : hex[1..]));
            // Radio semantics on a toggle: any user toggle picks this swatch, and the render re-selects the stored one.
            void Pick(object sender, RoutedEventArgs args)
            {
                if (_syncing) return;
                Stage(C.AccentKey, hex);
                QueueRender();
            }
            swatch.Checked += Pick;
            swatch.Unchecked += Pick;
            _swatches[hex] = swatch;
            choices.Children.Add(swatch);
        }
        // Exactly one control is selected: Default when unset, a matching preset, or Custom.
        var custom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        custom.Children.Add(U.Text(T.T("web.admin.settings.appearance.colors.accent.custom"), 12));
        _customAccent.Content = custom;
        AdminAppearanceOverlayEditor.TintWhenChecked(_customAccent);
        AutomationProperties.SetName(_customAccent, T.T("web.admin.settings.appearance.colors.accent.customLabel"));
        AutomationProperties.SetAutomationId(_customAccent, "AppearanceCustomAccent");
        _accentFlyout.Content = _accentPicker;
        void OpenCustom(object sender, RoutedEventArgs args)
        {
            if (_syncing) return;
            QueueRender();
            if (_customAccent.IsEnabled) _accentFlyout.ShowAt(_customAccent);
        }
        _customAccent.Checked += OpenCustom;
        _customAccent.Unchecked += OpenCustom;
        _accentFlyout.Opened += (_, _) => _accentOpen = true;
        _accentFlyout.Closed += (_, _) => _accentOpen = false;
        AutomationProperties.SetName(_accentPicker, T.T("web.admin.settings.appearance.colors.accent.customLabel"));
        AutomationProperties.SetAutomationId(_accentPicker, "AppearanceAccentPicker");
        // A ColorPicker reports its loaded value as a change; only the admin's own pick
        // while the picker is open stages, and only when it differs.
        _accentPicker.ColorChanged += (_, args) =>
        {
            if (_syncing || !_accentOpen || _controller?.State is not { } state) return;
            var value = $"#{args.NewColor.R:x2}{args.NewColor.G:x2}{args.NewColor.B:x2}";
            if (!string.Equals(state.Values.GetValueOrDefault(C.AccentKey, ""), value, StringComparison.OrdinalIgnoreCase)) Stage(C.AccentKey, value);
        };
        choices.Children.Add(_customAccent);
        var row = new AdminSettingsRow(T.T("web.admin.settings.appearance.colors.accent.label"),
            T.T("web.admin.settings.appearance.colors.accent.description"), choices, nameEditor: false);
        _rows[C.AccentKey] = row;
        _colors.Add(row);
    }

    private static Windows.UI.Color ParseColor(string value)
        => Windows.UI.Color.FromArgb(255, Convert.ToByte(value.Substring(1, 2), 16), Convert.ToByte(value.Substring(3, 2), 16), Convert.ToByte(value.Substring(5, 2), 16));

    private static bool IsHex(string value) => value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private void Stage(string key, string value) { if (!_syncing && _active) _controller?.Set(key, value); }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        _navigated = true;
        Activate();
        base.OnNavigatedTo(args);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        _navigated = false;
        Close();
        base.OnNavigatedFrom(args);
    }

    private void Activate()
    {
        if (_active) return;
        _active = true;
        _controller = App.Services.GetRequiredService<AdminAppearanceSettingsController>();
        _controller.Changed += QueueRender;
        _controller.Saved += OnSaved;
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.DirtyKeys.Count > 0, () => _controller?.Discard());
        _authority.AvailabilityChanged += OnAuthorityChanged;
        _ = OpenAsync();
    }

    private async Task OpenAsync()
    {
        if (!_active || _controller == null || !_authority.IsAvailable || _opening && ReferenceEquals(_openingController, _controller)) return;
        _opening = true;
        var controller = _controller;
        _openingController = controller;
        try
        {
            await controller.OpenAsync();
            if (!_active || !ReferenceEquals(controller, _controller)) return;
            // Badge images in the preview and library decode through the overlay service.
            using var action = controller.CaptureAction();
            try { await _overlays.EnsureLoadedAsync(action.Token); }
            catch (OperationCanceledException) { throw; }
            catch { /* The preview draws text badges without it. */ }
            if (_active && action.IsCurrent) QueueRender();
        }
        catch (OperationCanceledException) { }
        catch { QueueRender(); }
        finally
        {
            if (ReferenceEquals(_openingController, controller)) { _opening = false; _openingController = null; }
            if (_active && _authority.IsAvailable && ReferenceEquals(controller, _controller) && controller.State.Owner == null)
                DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); });
        }
    }

    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        HideDialog();
        DropPendingBadge();
        if (_active && _controller?.State.Owner == null) _ = OpenAsync();
        QueueRender();
    });

    private void QueueRender()
    {
        var controller = _controller;
        if (controller == null) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && IsLoaded && ReferenceEquals(controller, _controller)) Render(controller.State);
        });
    }

    private void Render(AdminAppearanceSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        var shown = current && state.Loaded;
        var editable = shown && !state.Loading && !state.Saving;
        _loading.Visibility = current && !state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        var message = state.Error ?? state.AssetError;
        _alert.Visibility = current && message != null && !(state.Loading && !state.Loaded) ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) U.UpdateLiveText(_error, state.Error != null ? ErrorMessage(state) : message);
        _reload.Visibility = state.Error != null || state.AssetsUnknown ? Visibility.Visible : Visibility.Collapsed;
        _reload.IsEnabled = current && !state.Loading && !state.Saving && state.AssetBusy == null;
        foreach (var group in new[] { _logos, _colors, _overlayGroup, _titles }) group.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _badgeImages.Visibility = shown && state.SupportsStyles ? Visibility.Visible : Visibility.Collapsed;
        _syncing = true;
        try
        {
            var accent = state.Values.GetValueOrDefault(C.AccentKey, "");
            var preset = AccentPresets.FirstOrDefault(hex => string.Equals(hex, accent, StringComparison.OrdinalIgnoreCase));
            foreach (var (hex, swatch) in _swatches)
            {
                swatch.IsChecked = hex == "" ? accent == "" : preset != null && string.Equals(hex, preset, StringComparison.OrdinalIgnoreCase);
                if (hex != "") swatch.Content = swatch.IsChecked == true ? new FontIcon { Glyph = "", FontSize = 14, Foreground = A.Solid(Microsoft.UI.Colors.White) } : null;
                swatch.IsEnabled = editable;
            }
            var custom = accent.Length > 0 && preset == null;
            _customAccent.IsChecked = custom;
            _customAccent.IsEnabled = editable;
            if (!_accentOpen) _accentPicker.Color = ParseColor(IsHex(accent) ? accent : "#4f46e5");
            var enabled = state.Values.GetValueOrDefault(C.OverlaysEnabledKey, "") != "false";
            _overlaysEnabled.IsOn = enabled;
            _overlaysEnabled.IsEnabled = editable;
            _titleArt.IsOn = state.Values.GetValueOrDefault(C.TitleArtDefaultKey, "") != "false";
            _titleArt.IsEnabled = editable;
            var colorsRestart = state.RestartKeys.Has(C.AccentKey);
            var overlaysRestart = state.RestartKeys.Has(C.OverlaysEnabledKey) && state.RestartKeys.Has(C.OverlayDefaultsKey);
            var titlesRestart = state.RestartKeys.Has(C.TitleArtDefaultKey);
            _colors.State(false, colorsRestart ? AdminText.T("web.admin.settings.fields.restartAll") : "");
            _overlayGroup.State(false, overlaysRestart ? AdminText.T("web.admin.settings.fields.restartAll") : "");
            _titles.State(false, titlesRestart ? AdminText.T("web.admin.settings.fields.restartAll") : "");
            _rows[C.AccentKey].State(false, false);
            _rows[C.OverlaysEnabledKey].State(false, !overlaysRestart && state.RestartKeys.Has(C.OverlaysEnabledKey));
            _rows[C.TitleArtDefaultKey].State(false, !titlesRestart && state.RestartKeys.Has(C.TitleArtDefaultKey));
            var document = state.Values.GetValueOrDefault(C.OverlayDefaultsKey, "");
            _restore.IsEnabled = editable && !CardOverlayService.IsBuiltInEditorDocument(document);
            _restore.Opacity = _restore.IsEnabled ? 1 : .4;
            _overlayEditor.Bind(new(document, enabled, editable, state.SupportsStyles, state.OverlayConfig?.Images ?? [],
                current && state.OverlayConfig?.ImageUploadsAvailable == true, state.ShownRatingSources,
                state.CanWriteImages && state.OverlayConfig != null));
            _overlayEditor.SetPreviewPresentation(state.PosterSize, XamlRoot?.Size.Width ?? ActualWidth);
            RenderAssets(state);
        }
        finally { _syncing = false; }
        _saveBar.State(state.DirtyKeys.Count, state.Saving, state.CanSave);
    }

    private static string ErrorMessage(AdminAppearanceSettingsState state)
    {
        if (state.Conflict || state.OutcomeUnknown || state.AssetsUnknown) return state.Error!;
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
        return key == null ? state.Error! : AdminText.T("configure.native.settings." + key);
    }

    private void OnSaved() => DispatcherQueue.TryEnqueue(async () => await RefreshSavedAppearanceAsync());

    /// <summary>After an acknowledged change the app's own accent and card badges follow the new defaults.</summary>
    private async Task RefreshSavedAppearanceAsync()
    {
        var controller = _controller;
        if (controller == null) return;
        try
        {
            using var action = controller.CaptureAction();
            App.Services.GetService<AdminServerSettingsStore>()?.Invalidate();
            await App.Services.GetRequiredService<ThemeService>().SyncFromServerAsync(action.Token);
            if (!_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
            // Announces the refresh: an Invalidate alone resets every loaded badge image to text and nothing reloads them.
            // The overlay service is the whole app's, so leaving this page must not cancel its reload once it has invalidated.
            await _overlays.RefreshPreferencesAsync(CancellationToken.None);
            QueueRender();
        }
        catch { /* The saved values are already acknowledged; a refresh failure only delays the app's look. */ }
    }

    private async Task RestoreDefaultsAsync()
    {
        if (_controller is not { } controller || _dialog != null || XamlRoot == null) return;
        ContentDialog? dialog = null;
        try
        {
            using var action = controller.CaptureAction();
            dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = T.T("web.admin.settings.appearance.restoreDialog.title"),
                Content = U.Text(T.T("web.admin.settings.appearance.restoreDialog.description")),
                PrimaryButtonText = T.T("web.admin.settings.appearance.restoreDialog.confirm"),
                CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Close,
            };
            AutomationProperties.SetAutomationId(dialog, "AppearanceRestoreDialog");
            _dialog = dialog;
            // Restoring stages the built-in document like any other edit; Save writes it and Discard undoes it.
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && _active && action.IsCurrent && ReferenceEquals(controller, _controller))
                controller.Set(C.OverlayDefaultsKey, AdminAppearanceOverlayEditor.BuiltInDefaults);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    public Task<bool> CanLeaveAsync() => _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner,
        () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);

    public void SuspendAdminPage() { _navigated = false; Close(); }

    private void HideDialog()
    {
        var dialog = _dialog;
        _dialog = null;
        dialog?.Hide();
    }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        _authority.AvailabilityChanged -= OnAuthorityChanged;
        HideDialog();
        _accentFlyout.Hide();
        DropPendingBadge();
        _overlayEditor.ShowLibraryError(null);
        if (_controller is { } controller)
        {
            controller.Changed -= QueueRender;
            controller.Saved -= OnSaved;
            controller.Dispose();
        }
        _controller = null;
        _leave = null;
    }
}
