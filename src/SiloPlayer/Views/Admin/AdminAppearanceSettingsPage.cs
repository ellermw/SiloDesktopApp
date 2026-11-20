using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;
using A = SiloPlayer.Controls.AdminMonitorUi;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceText;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAppearanceSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private static readonly string[] AccentPresets = ["#4f46e5", "#0ea5e9", "#10b981", "#f59e0b", "#ef4444", "#ec4899", "#8b5cf6", "#64748b"];
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new() { Padding = new(0, 0, 0, 112) };
    private readonly StackPanel _body = new() { Spacing = 20, MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _error = U.Text("", 13);
    private readonly TextBlock _loading = U.Text(T.T("web.admin.settings.appearance.loading"));
    private readonly StackPanel _alert = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly Button _reload = U.Action(AdminText.T("web.admin.settings.notifications.reload"), "AppearanceReload");
    private readonly AdminSettingsSaveBar _saveBar = new("Appearance");
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToggleButton> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToggleSwitch _overlaysEnabled = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly ToggleSwitch _titleArt = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly ColorPicker _accentPicker = new() { IsAlphaEnabled = false, IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false };
    private readonly Button _customAccent = U.Action(T.T("web.admin.settings.appearance.colors.accent.custom"), "AppearanceCustomAccent");
    private readonly Button _restore = U.Action(T.T("web.admin.settings.appearance.overlays.restoreDefaults"), "AppearanceRestoreOverlays");
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminAppearanceApi _api;
    private readonly CardOverlayService _overlays;
    private readonly AdminAppearanceOverlayEditor _overlayEditor;
    private readonly AdminSettingsGroup _logos;
    private readonly Border _storageWarning;
    private AdminAppearanceSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private bool _active;
    private bool _navigated;
    private bool _opening;
    private AdminAppearanceSettingsController? _openingController;
    private bool _syncing;
    private IReadOnlySet<string> _shownRatingSources = new HashSet<string>(StringComparer.Ordinal);
    private string _posterSize = "standard";

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminAppearanceSettingsController? Controller => _controller;

    public AdminAppearanceSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        _api = App.Services.GetRequiredService<AdminAppearanceApi>();
        _overlays = App.Services.GetRequiredService<CardOverlayService>();
        _overlayEditor = new(_overlays);
        AutomationProperties.SetAutomationId(this, "AdminAppearanceSettingsPage");
        _scrollContent.Children.Add(_body);
        _scroller.Content = _scrollContent;
        _root.Children.Add(_scroller);
        _root.Children.Add(_saveBar);
        Content = _root;
        _body.Children.Add(U.Title(T.T("web.admin.settings.appearance.title")));
        _body.Children.Add(_loading);
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
        BuildColors();
        var overlayGroup = new AdminSettingsGroup(T.T("web.admin.settings.appearance.overlays.group"));
        var enabledRow = new AdminSettingsRow(T.T("web.admin.settings.appearance.overlays.enabled.label"),
            T.T("web.admin.settings.appearance.overlays.enabled.description"), _overlaysEnabled);
        _rows["overlays.enabled"] = enabledRow;
        AutomationProperties.SetAutomationId(_overlaysEnabled, "AppearanceOverlaysEnabled");
        _overlaysEnabled.Toggled += (_, _) => Stage("overlays.enabled", _overlaysEnabled.IsOn ? "true" : "false");
        overlayGroup.Add(enabledRow);
        overlayGroup.Add(_restore);
        overlayGroup.Add(_overlayEditor);
        _restore.Click += async (_, _) => await RestoreDefaultsAsync();
        _overlayEditor.Changed += document => Stage("defaults.card_overlays", document);
        _overlayEditor.UploadRequested += () => _ = UploadBadgeImageAsync();
        _overlayEditor.DeleteRequested += reference => _ = DeleteBadgeImageAsync(reference);
        _body.Children.Add(overlayGroup);
        var titles = new AdminSettingsGroup(T.T("web.admin.settings.appearance.titlePages.group"));
        var titleRow = new AdminSettingsRow(T.T("settingsManifest.ui.title_art.label"),
            T.T("web.admin.settings.appearance.titlePages.titleArt.description"), _titleArt);
        _rows["defaults.ui.title_art"] = titleRow;
        AutomationProperties.SetAutomationId(_titleArt, "AppearanceTitleArt");
        _titleArt.Toggled += (_, _) => Stage("defaults.ui.title_art", _titleArt.IsOn ? "true" : "false");
        titles.Add(titleRow);
        _body.Children.Add(titles);
        _saveBar.Save.Click += async (_, _) => { if (_controller is { } controller) await controller.SaveAsync(); };
        _saveBar.Discard.Click += (_, _) => _controller?.Discard();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); QueueRender(); };
        Loaded += (_, _) => { if (_navigated) { Activate(); QueueRender(); } };
        Unloaded += (_, _) => { if (!IsLoaded) Close(); };
    }

    private void BuildColors()
    {
        var colors = new AdminSettingsGroup(T.T("web.admin.settings.appearance.colors.group"));
        var choices = new LogsWrapPanel { Spacing = 8, MaxWidth = 340 };
        foreach (var hex in new[] { "" }.Concat(AccentPresets))
        {
            var swatch = new ToggleButton { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(16) };
            if (hex == "") swatch.Content = A.Icon("rotate-ccw", 14, A.Muted);
            else swatch.Background = A.Solid(ParseColor(hex));
            AutomationProperties.SetName(swatch, hex == "" ? T.T("web.admin.settings.appearance.colors.accent.useDefault")
                : T.T("web.admin.settings.appearance.colors.accent.usePreset", ("hex", hex)));
            AutomationProperties.SetAutomationId(swatch, "AppearanceAccent" + (hex == "" ? "Default" : hex[1..]));
            swatch.Click += (_, _) => { Stage("branding.accent_color", hex); QueueRender(); };
            _swatches[hex] = swatch;
            choices.Children.Add(swatch);
        }
        var flyout = new Flyout { Content = _accentPicker };
        _customAccent.Flyout = flyout;
        AutomationProperties.SetName(_accentPicker, T.T("web.admin.settings.appearance.colors.accent.customLabel"));
        _accentPicker.ColorChanged += (_, args) => Stage("branding.accent_color", $"#{args.NewColor.R:x2}{args.NewColor.G:x2}{args.NewColor.B:x2}");
        choices.Children.Add(_customAccent);
        var row = new AdminSettingsRow(T.T("web.admin.settings.appearance.colors.accent.label"),
            T.T("web.admin.settings.appearance.colors.accent.description"), choices);
        _rows["branding.accent_color"] = row;
        colors.Add(row);
        _body.Children.Add(colors);
    }

    private static Windows.UI.Color ParseColor(string value)
        => Windows.UI.Color.FromArgb(255, Convert.ToByte(value.Substring(1, 2), 16), Convert.ToByte(value.Substring(3, 2), 16), Convert.ToByte(value.Substring(5, 2), 16));

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
            using var action = controller.CaptureAction();
            await _overlays.EnsureLoadedAsync(action.Token);
            try
            {
                action.ThrowIfRetired();
                var catalog = App.Services.GetService<CatalogApi>();
                var ratings = catalog == null ? new HashSet<string>(StringComparer.Ordinal)
                    : await catalog.GetShownRatingSourcesAsync(action.Token);
                action.ThrowIfRetired();
                if (_active && ReferenceEquals(controller, _controller)) _shownRatingSources = ratings;
            }
            catch (OperationCanceledException) { throw; }
            catch { _shownRatingSources = new HashSet<string>(StringComparer.Ordinal); }
            try
            {
                var settingsApi = App.Services.GetRequiredService<SettingsApi>();
                var effective = await settingsApi.GetContractEffectiveSettingsAsync(action.Context, ["ui.card_presentation"], ct: action.Token);
                action.ThrowIfRetired();
                var value = effective.Settings.FirstOrDefault(setting => setting.Key == "ui.card_presentation");
                if (_active && ReferenceEquals(controller, _controller) && value != null && value.Value.ValueKind == JsonValueKind.Object &&
                    value.Value.TryGetProperty("poster_size", out var size) && size.ValueKind == JsonValueKind.String && size.GetString() is "compact" or "standard" or "large")
                    _posterSize = size.GetString()!;
            }
            catch (OperationCanceledException) { throw; }
            catch { }
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
        _dialog?.Hide();
        _shownRatingSources = new HashSet<string>(StringComparer.Ordinal);
        _posterSize = "standard";
        _overlays.Invalidate();
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
        var editable = current && state.Loaded && !state.Loading && !state.StatusUnavailable;
        _loading.Visibility = state.Loading ? Visibility.Visible : Visibility.Collapsed;
        _alert.Visibility = current && state.Error != null ? Visibility.Visible : Visibility.Collapsed;
        if (state.Error != null) U.UpdateLiveText(_error, state.Error);
        _reload.IsEnabled = current && !state.Loading && !state.Saving && state.AssetBusy == null;
        foreach (var group in _body.Children.OfType<AdminSettingsGroup>()) group.Visibility = current && state.Loaded && !state.Loading ? Visibility.Visible : Visibility.Collapsed;
        _syncing = true;
        try
        {
            var accent = state.Values.GetValueOrDefault("branding.accent_color", "");
            foreach (var (hex, swatch) in _swatches)
            {
                swatch.IsChecked = string.Equals(hex, accent, StringComparison.OrdinalIgnoreCase);
                swatch.IsEnabled = editable && !state.ManagedKeys.Contains("branding.accent_color");
            }
            _customAccent.IsEnabled = editable && !state.ManagedKeys.Contains("branding.accent_color");
            AutomationProperties.SetItemStatus(_customAccent, accent.Length > 0 && !AccentPresets.Contains(accent, StringComparer.OrdinalIgnoreCase) ? "selected" : "");
            if (accent.Length == 7 && accent[0] == '#' && accent[1..].All(Uri.IsHexDigit)) _accentPicker.Color = ParseColor(accent);
            _overlaysEnabled.IsOn = state.Values.GetValueOrDefault("overlays.enabled", "") != "false";
            _overlaysEnabled.IsEnabled = editable && !state.ManagedKeys.Contains("overlays.enabled");
            _titleArt.IsOn = state.Values.GetValueOrDefault("defaults.ui.title_art", "") != "false";
            _titleArt.IsEnabled = editable && !state.ManagedKeys.Contains("defaults.ui.title_art");
            foreach (var (key, row) in _rows) row.State(state.DirtyKeys.Contains(key), state.RestartKeys.Has(key));
            var document = state.Values.GetValueOrDefault("defaults.card_overlays", "");
            _restore.IsEnabled = editable && !state.ManagedKeys.Contains("defaults.card_overlays") &&
                document.Length > 0 && document != AdminAppearanceOverlayEditor.BuiltInDefaults;
            _overlayEditor.Bind(document, _overlaysEnabled.IsOn, editable && !state.ManagedKeys.Contains("defaults.card_overlays"),
                _overlays.ManifestRevision, state.OverlayConfig?.Images ?? []);
            _overlayEditor.SetCapabilities(current && state.OverlayConfig?.ImageUploadsAvailable == true,
                current ? _shownRatingSources : new HashSet<string>(StringComparer.Ordinal));
            _overlayEditor.SetImageLibraryEditable(current && state.Loaded && !state.Loading && !state.Saving && state.AssetBusy == null && !state.OutcomeUnknown);
            _overlayEditor.SetPreviewPresentation(_posterSize, XamlRoot?.Size.Width ?? ActualWidth);
            RenderAssets(state);
        }
        finally { _syncing = false; }
        _saveBar.State(state.DirtyKeys.Count, state.Saving, editable && state.AssetBusy == null && !state.Conflict && !state.OutcomeUnknown);
    }

    private void OnSaved() => DispatcherQueue.TryEnqueue(async () => await RefreshSavedAppearanceAsync());

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
            _overlays.Invalidate();
            await _overlays.EnsureLoadedAsync(action.Token);
            QueueRender();
        }
        catch { }
    }

    private async Task RestoreDefaultsAsync()
    {
        if (_controller is not { } controller || _dialog != null) return;
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
            _dialog = dialog;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && _active && action.IsCurrent && ReferenceEquals(controller, _controller))
                controller.Set("defaults.card_overlays", AdminAppearanceOverlayEditor.DefaultsForRevision(_overlays.ManifestRevision));
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    public Task<bool> CanLeaveAsync() => _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner,
        () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);

    public void SuspendAdminPage() { _navigated = false; Close(); }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        _authority.AvailabilityChanged -= OnAuthorityChanged;
        _dialog?.Hide();
        _dialog = null;
        if (_controller is { } controller)
        {
            controller.Changed -= QueueRender;
            controller.Saved -= OnSaved;
            controller.Dispose();
        }
        _controller = null;
        _leave = null;
        _shownRatingSources = new HashSet<string>(StringComparer.Ordinal);
        _posterSize = "standard";
    }
}
