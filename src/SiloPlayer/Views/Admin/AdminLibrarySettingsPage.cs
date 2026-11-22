using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminLibrarySettingsRules;

namespace SiloPlayer.Views.Admin;

/// <summary>Manage &gt; Libraries &gt; Settings, matching official <c>LibraryMetadataSettings.tsx</c>.</summary>
public sealed partial class AdminLibrarySettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private static readonly TimeSpan RatingPolicySettle = TimeSpan.FromSeconds(11);
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private static readonly Lazy<LoggingDisclosurePreferences> DefaultPreferences = new(() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "monitor-ui")));

    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _header = new() { Margin = new(0, 0, 0, 32), Visibility = Visibility.Collapsed };
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly StackPanel _loading = new() { Spacing = 24 };
    private readonly TextBlock _errorText = U.Text("", 13);
    private readonly Button _retry = U.Action(T("common.retry"), "LibrarySettingsRetry");
    private readonly Button _reload = U.Action(T("web.admin.settings.notifications.reload"), "LibrarySettingsReload");
    private readonly Dictionary<string, FrameworkElement> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _descriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToggleSwitch> _ratingToggles = new(StringComparer.Ordinal);
    private readonly AdminSettingsGroup _general = new(T("web.admin.settings.libraryMetadata.general.group"));
    private readonly AdminSettingsGroup _scanning = new(T("web.admin.settings.libraryMetadata.scanning.group"));
    private readonly AdminSettingsGroup _maturity = new(T("web.admin.settings.libraryMetadata.maturity.group"), T("web.admin.settings.libraryMetadata.maturity.description"));
    private readonly AdminSettingsGroup _ratings = new(T("web.admin.settings.libraryMetadata.ratings.group"), T("web.admin.settings.libraryMetadata.ratings.description"));
    private readonly AdminSettingsGroup _subtitle = new(T("web.admin.settings.libraryMetadata.subtitleSync.group"), T("web.admin.settings.libraryMetadata.subtitleSync.description"));
    private readonly AdminSettingsSaveBar _saveBar = new("LibrarySettings");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Button _restoreDefaults;
    private readonly Border _syncNoNodesWarning;
    private readonly AdvancedSection _scanningAdvanced;
    private readonly AdvancedSection _subtitleAdvanced;
    private readonly AdminMonitorAuthority _authority;
    private AdminLibrarySettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private AdminRouteRequest? _location;
    private LoggingDisclosurePreferences? _preferences;
    private double? _restoreOffset;
    private string _ratingLayout = "";
    private bool _active, _opening, _syncing;

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminLibrarySettingsController? Controller => _controller;

    public AdminLibrarySettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminLibrarySettingsPage");
        AutomationProperties.SetName(_scroller, T("web.admin.settings.layout.regionLabel", new { label = T("web.admin.settings.libraryMetadata.title") }));

        _header.Children.Add(U.Title(T("web.admin.settings.libraryMetadata.title")));
        _body.Children.Add(_header);
        AutomationProperties.SetName(_loading, T("web.admin.settings.fields.loadingSettings"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(U.Skeleton(32, 210));
        for (var index = 0; index < 3; index++) _loading.Children.Add(U.Skeleton(44));
        AutomationProperties.SetAutomationId(_errorText, "LibrarySettingsErrorText");
        _alerts.Children.Add(U.Alert(_errorText));
        _alerts.Children.Add(_retry); _alerts.Children.Add(_reload);
        _retry.Visibility = _reload.Visibility = Visibility.Collapsed;
        _retry.HorizontalAlignment = _reload.HorizontalAlignment = HorizontalAlignment.Left;
        _retry.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(false); };
        _reload.Click += async (_, _) => { if (_controller is { State.Loading: false } controller) await controller.ReloadAsync(true); };
        _body.Children.Add(_loading); _body.Children.Add(_alerts); _body.Children.Add(_fields);
        foreach (var group in new[] { _general, _scanning, _maturity, _ratings, _subtitle }) _fields.Children.Add(group);

        AddToggle(_general, R.ScopeVersionsToLibrary, "web.admin.settings.libraryMetadata.general.allVersions.label",
            "web.admin.settings.libraryMetadata.general.allVersions.description", inverse: true);
        AddSelect(_general, R.FallbackLanguage, "web.admin.settings.libraryMetadata.general.fallbackLanguage.label",
            "web.admin.settings.libraryMetadata.general.fallbackLanguage.description",
            AdminLibraryPresentation.MetadataLanguages().Select(option => (option.Code, option.Label))
                .Append(("original", T("web.admin.settings.libraryMetadata.general.fallbackLanguage.original"))));

        AddToggle(_scanning, R.RealtimeMonitoring, "web.admin.settings.libraryMetadata.scanning.realtime.label",
            "web.admin.settings.libraryMetadata.scanning.realtime.description");
        AddNumber(_scanning, R.ScannerWorkers, "web.admin.settings.libraryMetadata.scanning.scannerWorkers.label",
            "web.admin.settings.libraryMetadata.scanning.scannerWorkers.description");
        AddNumber(_scanning, R.MatcherWorkers, "web.admin.settings.libraryMetadata.scanning.matcherWorkers.label",
            "web.admin.settings.libraryMetadata.scanning.matcherWorkers.description");
        AddNumber(_scanning, R.ImageWorkers, "web.admin.settings.libraryMetadata.scanning.imageWorkers.label",
            "web.admin.settings.libraryMetadata.scanning.imageWorkers.description");
        _scanningAdvanced = new("library.scanning", "LibrarySettingsScanningAdvanced");
        _scanning.Add(_scanningAdvanced);
        AddNumber(_scanningAdvanced.Fields, R.PersonRefreshPerMinute, "web.admin.settings.libraryMetadata.scanning.personRefresh.label",
            "web.admin.settings.libraryMetadata.scanning.personRefresh.description");
        AddNumber(_scanningAdvanced.Fields, R.MatcherBatchSize, "web.admin.settings.libraryMetadata.scanning.matcherBatch.label",
            "web.admin.settings.libraryMetadata.scanning.matcherBatch.description");
        _restoreDefaults = U.Action(T("web.admin.settings.libraryMetadata.scanning.restoreDefaults"), "LibrarySettingsRestoreDefaults");
        _restoreDefaults.Height = 28; _restoreDefaults.MinHeight = 28; _restoreDefaults.FontSize = 12; _restoreDefaults.Padding = new(8, 0, 8, 0);
        _restoreDefaults.BorderThickness = new(0); _restoreDefaults.Foreground = U.Brush("SecondaryTextBrush");
        var restoreContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        restoreContent.Children.Add(AdminMonitorUi.Icon("rotate-ccw", 13, U.Brush("SecondaryTextBrush")));
        restoreContent.Children.Add(U.Text(T("web.admin.settings.libraryMetadata.scanning.restoreDefaults"), 12, true));
        _restoreDefaults.Content = restoreContent;
        _restoreDefaults.Click += (_, _) => _controller?.RestoreWorkerDefaults();
        _scanning.SetAction(_restoreDefaults);

        AddSelect(_maturity, R.UnratedContent, "web.admin.settings.libraryMetadata.maturity.unrated.label",
            "web.admin.settings.libraryMetadata.maturity.unrated.description",
            [("hide", T("web.admin.settings.libraryMetadata.maturity.unrated.hide")), ("allow", T("web.admin.settings.libraryMetadata.maturity.unrated.allow"))]);

        AddToggle(_subtitle, R.SubtitleAutoSync, "web.admin.settings.libraryMetadata.subtitleSync.auto.label",
            "web.admin.settings.libraryMetadata.subtitleSync.auto.description");
        _subtitleAdvanced = new("library.subtitle_sync", "LibrarySettingsSubtitleAdvanced");
        _subtitle.Add(_subtitleAdvanced);
        AddSelect(_subtitleAdvanced.Fields, R.SubtitleSyncExecution, "web.admin.settings.libraryMetadata.subtitleSync.execution.label",
            "web.admin.settings.libraryMetadata.subtitleSync.execution.description",
            [("local", T("web.admin.settings.playback.execution.localServer")),
             ("prefer_transcode_nodes", T("web.admin.settings.playback.execution.preferNodes")),
             ("transcode_nodes_only", T("web.admin.settings.playback.execution.nodesOnly"))]);
        _syncNoNodesWarning = U.Warning(T("web.admin.settings.libraryMetadata.subtitleSync.execution.noNodes"), "LibrarySettingsNoNodes");
        _rows[R.SubtitleSyncExecution].AddStatus(_syncNoNodesWarning);
        AddNumber(_subtitleAdvanced.Fields, R.SubtitleSyncNodeCapacity, "web.admin.settings.libraryMetadata.subtitleSync.capacity.label",
            "web.admin.settings.libraryMetadata.subtitleSync.capacity.description");

        _body.Children.Add(_spacer);
        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent; _root.Children.Add(_scroller);
        _scroller.SizeChanged += (_, _) => FitScrollContent();
        var background = AdminMonitorUi.ColorOf("AppBackgroundBrush", Colors.Black);
        _scrim.Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0), EndPoint = new(0, 1),
            GradientStops = { new GradientStop { Color = AdminMonitorUi.WithAlpha(background, 0), Offset = 0 }, new GradientStop { Color = background, Offset = 1 } },
        };
        _root.Children.Add(_scrim); _root.Children.Add(_saveBar); Content = _root;
        _saveBar.Save.Click += async (_, _) => { if (_controller != null) await _controller.SaveAsync(); };
        _saveBar.Discard.Click += (_, _) => _controller?.Discard();
        Loaded += (_, _) => { FitScrollContent(); Activate(); };
        Unloaded += (_, _) => Close();
        SizeChanged += (_, _) =>
        {
            _saveBar.SetAvailableWidth(ActualWidth);
            _saveBar.Margin = new(16, 0, 16, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? -8 : 8);
        };
    }

    private static string T(string key, object? args = null) => AdminText.T(key, args);

    private void FitScrollContent()
    {
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    private AdminSettingsRow Place(StackPanel fields, string key, string labelKey, string descriptionKey, FrameworkElement editor)
    {
        var label = T(labelKey); var description = T(descriptionKey);
        var row = new AdminSettingsRow(label, description, editor);
        var error = U.Text("", 13); error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        row.AddStatus(error); fields.Children.Add(row);
        _controls[key] = editor; _rows[key] = row; _errors[key] = error; _descriptions[key] = description;
        return row;
    }

    private AdminSettingsRow Place(AdminSettingsGroup group, string key, string labelKey, string descriptionKey, FrameworkElement editor)
    {
        var row = Place(group.Fields, key, labelKey, descriptionKey, editor); group.RefreshRules(); return row;
    }

    private void AddToggle(AdminSettingsGroup group, string key, string labelKey, string descriptionKey, bool inverse = false)
    {
        var toggle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        AutomationProperties.SetAutomationId(toggle, "LibrarySettings_" + key);
        Place(group, key, labelKey, descriptionKey, toggle);
        toggle.Toggled += (_, _) =>
        {
            var value = toggle.IsOn ? "true" : "false";
            Set(key, inverse ? value == "true" ? "false" : "true" : value);
        };
    }

    private void AddNumber(AdminSettingsGroup group, string key, string labelKey, string descriptionKey) => AddNumber(group.Fields, key, labelKey, descriptionKey, group);
    private void AddNumber(StackPanel fields, string key, string labelKey, string descriptionKey, AdminSettingsGroup? group = null)
    {
        var input = U.Input(T(labelKey), "LibrarySettings_" + key, number: true);
        if (group == null) Place(fields, key, labelKey, descriptionKey, input); else Place(group, key, labelKey, descriptionKey, input);
        input.TextChanging += (_, _) => Set(key, input.Text);
    }

    private void AddSelect(AdminSettingsGroup group, string key, string labelKey, string descriptionKey, IEnumerable<(string Value, string Label)> options) =>
        AddSelect(group.Fields, key, labelKey, descriptionKey, options, group);
    private void AddSelect(StackPanel fields, string key, string labelKey, string descriptionKey,
        IEnumerable<(string Value, string Label)> options, AdminSettingsGroup? group = null)
    {
        var select = U.Select(T(labelKey), "LibrarySettings_" + key, options);
        if (group == null) Place(fields, key, labelKey, descriptionKey, select); else Place(group, key, labelKey, descriptionKey, select);
        select.SelectionChanged += (_, _) =>
        {
            if (select.SelectedItem is ComboBoxItem { Tag: string value }) Set(key, value);
        };
    }

    private void Set(string key, string value)
    {
        if (!_syncing && _controller?.State is { Loaded: true } state && state.Values.GetValueOrDefault(key, "") != value)
            _controller.Set(key, value);
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
        _active = true;
        _controller = App.Services.GetRequiredService<AdminLibrarySettingsController>();
        _preferences = App.Services.GetService<LoggingDisclosurePreferences>();
        _scanningAdvanced.Initialize(_preferences ?? DefaultPreferences.Value);
        _subtitleAdvanced.Initialize(_preferences ?? DefaultPreferences.Value);
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.DirtyKeys.Count > 0, () => _controller?.Discard());
        _controller.Changed += QueueRender; _controller.Saved += OnSaved; _authority.AvailabilityChanged += OnAuthorityChanged;
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

    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state);
        });
    }

    private static bool IsAccessFailure(AdminFailureKind? kind) => kind is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
        or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;

    private void Render(AdminLibrarySettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        if (!current) ClearUi();
        var accessBlocked = state.Error != null && IsAccessFailure(state.Failure);
        var loadFailed = current && state.Error != null && !state.Loaded && !state.Loading;
        _loading.Visibility = state.Loading && !state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = _loading.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var showFields = current && state.Loaded && !accessBlocked;
        _fields.Visibility = showFields ? Visibility.Visible : Visibility.Collapsed;
        // Without the environment-managed status no key is known to be writable: read-only until Reload succeeds.
        var statusUnknown = current && state.Loaded && state.SensitiveStatusError;
        var notice = state.Error != null || statusUnknown;
        _alerts.Visibility = current && (notice || state.Loading && _alerts.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = loadFailed || state.Loading && !state.Loaded && _alerts.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        _reload.Visibility = state.Loaded && notice ? Visibility.Visible : Visibility.Collapsed;
        _retry.IsEnabled = _reload.IsEnabled = current && !state.Saving;
        _retry.IsHitTestVisible = _reload.IsHitTestVisible = !state.Loading;
        if (state.Error != null) U.UpdateLiveText(_errorText, ErrorMessage(state));
        else if (statusUnknown) U.UpdateLiveText(_errorText, T("configure.native.settings.managedStatusUnavailable"));

        bool Dirty(string key) => state.DirtyKeys.Contains(key);
        bool Managed(string key) => state.ManagedByEnv.Contains(key);
        var editable = current && state.Loaded && !state.Loading && !accessBlocked && !state.SensitiveStatusError;
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
                        toggle.IsOn = key switch
                        {
                            R.ScopeVersionsToLibrary => value != "true",
                            R.RealtimeMonitoring or R.SubtitleAutoSync => value.Length == 0 || value == "true",
                            _ => value == "true",
                        };
                        toggle.IsEnabled = enabled;
                        break;
                    case TextBox input:
                        var text = key == R.SubtitleSyncNodeCapacity && value.Length == 0 ? "1" : value;
                        if (input.Text != text) input.Text = text;
                        input.IsEnabled = enabled;
                        break;
                    case ComboBox select:
                        var selected = key switch
                        {
                            R.FallbackLanguage when value.Length == 0 => "en",
                            R.UnratedContent when value.Length == 0 => "hide",
                            R.SubtitleSyncExecution when value.Length == 0 => R.DefaultSubtitleSyncExecution,
                            _ => value,
                        };
                        select.SelectedValuePath = "Tag";
                        if (!Equals(select.SelectedValue, selected)) select.SelectedValue = selected;
                        select.IsEnabled = enabled;
                        break;
                }
            }
        }
        finally { _syncing = false; }

        foreach (var key in R.Keys.Where(_rows.ContainsKey))
        {
            // The description stays; a setting the server's environment sets adds the official note naming the variable.
            Describe(key, _descriptions[key]);
            _rows[key].SetEnvironmentNote(state.EnvManaged.Note(key));
            SetError(key, current && state.Errors.TryGetValue(key, out var error) ? R.Describe(error) : "");
        }

        _rows[R.RealtimeMonitoring].Visibility = current && R.ShowRealtimeMonitoring(
            state.PersistedValues.GetValueOrDefault(R.RealtimeMonitoring, "true"), Dirty(R.RealtimeMonitoring)) ? Visibility.Visible : Visibility.Collapsed;
        _scanning.RefreshRules();
        var execution = state.Values.GetValueOrDefault(R.SubtitleSyncExecution, "");
        if (execution.Length == 0) execution = R.DefaultSubtitleSyncExecution;
        var capacityVisible = R.ShowSubtitleCapacity(execution, Dirty(R.SubtitleSyncNodeCapacity));
        _rows[R.SubtitleSyncNodeCapacity].Visibility = capacityVisible ? Visibility.Visible : Visibility.Collapsed;
        var noNodes = state.NodesKnown && !state.TranscodeNodeAvailable && R.IsNodeBackedExecution(execution);
        _syncNoNodesWarning.Visibility = noNodes ? Visibility.Visible : Visibility.Collapsed;
        if (_controls[R.SubtitleSyncExecution] is ComboBox executionSelect)
            foreach (var item in executionSelect.Items.OfType<ComboBoxItem>())
                item.IsEnabled = !state.NodesKnown || state.TranscodeNodeAvailable || item.Tag?.ToString() == execution || !R.IsNodeBackedExecution(item.Tag?.ToString() ?? "");

        _restoreDefaults.Visibility = current && R.HasWorkerOverrides(key => state.Values.GetValueOrDefault(key, "")) ? Visibility.Visible : Visibility.Collapsed;
        // The controller skips environment-managed keys, so one managed worker does not block restoring the others.
        _restoreDefaults.IsEnabled = editable && R.WorkerDefaults.Keys.Any(key => !Managed(key));
        _scanningAdvanced.Update(R.ScannerAdvancedKeys.Any(Dirty), R.ScannerAdvancedKeys.Count);
        _subtitleAdvanced.Update(R.SubtitleAdvancedKeys.Any(Dirty) || noNodes && execution == "transcode_nodes_only", capacityVisible ? 2 : 1);
        _subtitleAdvanced.RefreshRules();

        RenderRatings(state, editable && !Managed(R.ExtraRatingSources));

        SetGroup(_general, R.GeneralKeys, "", state);
        SetGroup(_scanning, R.ScanningKeys, "", state);
        SetGroup(_maturity, R.MaturityKeys, T("web.admin.settings.libraryMetadata.maturity.description"), state, showDirty: false);
        SetGroup(_ratings, R.RatingsKeys, T("web.admin.settings.libraryMetadata.ratings.description"), state, showDirty: false);
        SetGroup(_subtitle, R.SubtitleKeys, T("web.admin.settings.libraryMetadata.subtitleSync.description"), state);
        foreach (var key in R.Keys.Where(_rows.ContainsKey))
        {
            var groupKeys = key == R.ExtraRatingSources ? R.RatingsKeys : key is R.SubtitleAutoSync or R.SubtitleSyncExecution or R.SubtitleSyncNodeCapacity ? R.SubtitleKeys
                : key == R.UnratedContent ? R.MaturityKeys : R.ScanningKeys.Contains(key) ? R.ScanningKeys : R.GeneralKeys;
            var allRestart = groupKeys.All(state.RestartKeys.Has);
            _rows[key].State((key is R.SubtitleAutoSync or R.SubtitleSyncExecution or R.SubtitleSyncNodeCapacity) && Dirty(key),
                !allRestart && state.RestartKeys.Has(key));
        }

        var writable = !state.SensitiveStatusError && state.DirtyKeys.Any(key => !Managed(key)) && state.Errors.Keys.All(Managed);
        _saveBar.State(current ? state.DirtyKeys.Count : 0, state.Saving,
            current && writable && !state.Loading && !accessBlocked && !state.Conflict && !state.OutcomeUnknown);
        _saveBar.SetAvailableWidth(ActualWidth);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;
        if (state.Loaded && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); _scroller.Focus(FocusState.Programmatic); }
            });
        }
    }

    private void SetGroup(AdminSettingsGroup group, IReadOnlyList<string> keys, string description, AdminLibrarySettingsState state, bool showDirty = true)
    {
        var allRestart = keys.All(state.RestartKeys.Has);
        var shownDescription = allRestart
            ? description.Length == 0
                ? T("web.admin.settings.fields.restartAll")
                : T("web.admin.settings.fields.restartAllWithDescription", new { description })
            : description;
        group.State(showDirty && keys.Any(state.DirtyKeys.Contains), shownDescription);
    }

    private void RenderRatings(AdminLibrarySettingsState state, bool editable)
    {
        _ratings.Visibility = state.RatingSourcesSupported ? Visibility.Visible : Visibility.Collapsed;
        if (!state.RatingSourcesSupported) { _ratingLayout = ""; _ratingToggles.Clear(); _ratings.Fields.Children.Clear(); return; }
        var current = R.ParseRatingSources(state.Values.GetValueOrDefault(R.ExtraRatingSources, ""));
        var retained = R.ParseRatingSources(state.PersistedValues.GetValueOrDefault(R.ExtraRatingSources, "")).Concat(current);
        IReadOnlyList<string> undeclared = state.RatingSourcesLoaded
            ? R.UndeclaredRatingSources(retained, state.RatingSources) : Array.Empty<string>();
        var groups = R.GroupRatingSources(state.RatingSources);
        var signature = string.Join('|', groups.SelectMany(group => group.Sources.Select(source => group.Provider + ":" + source.Source + ":" + source.Label))) +
            "#" + string.Join(',', undeclared) + "#" + state.RatingSourcesLoading + ":" + state.RatingSourcesError;
        // The list has no row of its own, so a value the server would refuse is explained on the group.
        var invalid = state.Errors.TryGetValue(R.ExtraRatingSources, out var listError) ? R.Describe(listError) : "";
        signature += "#" + invalid;
        if (_ratingLayout != signature)
        {
            _ratingLayout = signature; _ratingToggles.Clear(); _ratings.Fields.Children.Clear();
            if (invalid.Length > 0)
            {
                var problem = U.Warning(invalid, "LibrarySettingsRatingsInvalid");
                problem.Visibility = Visibility.Visible;
                _ratings.Fields.Children.Add(problem);
            }
            if (state.RatingSourcesError)
            {
                var warning = U.Warning(T("web.admin.settings.libraryMetadata.ratings.loadError"), "LibrarySettingsRatingsError");
                warning.Visibility = Visibility.Visible;
                _ratings.Fields.Children.Add(warning);
            }
            else if (!state.RatingSourcesLoading && groups.Count == 0 && undeclared.Count == 0)
                _ratings.Fields.Children.Add(new Border { Padding = new(0, 14, 0, 14), Child = U.Text(T("web.admin.settings.libraryMetadata.ratings.empty"), 13, true) });
            foreach (var group in groups)
            {
                _ratings.Fields.Children.Add(Subheading(T("web.admin.settings.libraryMetadata.ratings.fromProvider", new { provider = group.Provider })));
                foreach (var source in group.Sources) AddRatingToggle(source.Source, source.Label);
            }
            if (undeclared.Count > 0)
            {
                var heading = Subheading(T("web.admin.settings.libraryMetadata.ratings.undeclared.heading"));
                var stack = (StackPanel)((Border)heading).Child;
                stack.Children.Add(U.Text(T("web.admin.settings.libraryMetadata.ratings.undeclared.description"), 12, true));
                _ratings.Fields.Children.Add(heading);
                foreach (var source in undeclared) AddRatingToggle(source, source);
            }
            _ratings.RefreshRules();
        }
        _syncing = true;
        try { foreach (var (source, toggle) in _ratingToggles) { toggle.IsOn = current.Contains(source); toggle.IsEnabled = editable; } }
        finally { _syncing = false; }
    }

    private void AddRatingToggle(string source, string label)
    {
        var toggle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        AutomationProperties.SetAutomationId(toggle, "LibrarySettingsRating_" + source);
        var row = new AdminSettingsRow(label, "", toggle); _ratingToggles[source] = toggle;
        toggle.Toggled += (_, _) => { if (!_syncing) _controller?.SetRatingSource(source, toggle.IsOn); };
        _ratings.Fields.Children.Add(row);
    }

    private static Border Subheading(string text)
    {
        var stack = new StackPanel { Spacing = 4 };
        var heading = U.Text(text, 12, true); heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3); stack.Children.Add(heading);
        return new Border { Child = stack, Padding = new(0, 14, 0, 6), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6) };
    }

    private void SetError(string key, string text)
    {
        var block = _errors[key]; U.UpdateLiveText(block, text); block.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Describe(string key, string text)
    {
        var block = _rows[key].Description; if (block.Text != text) block.Text = text;
        block.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _rows[key].HelpTarget = _controls[key];
        _rows[key].RefreshHelp();
    }

    private static string ErrorMessage(AdminLibrarySettingsState state)
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
        if (!state.Loaded && !IsAccessFailure(state.Failure)) return T("configure.native.librarySettings.loadError");
        return key == null ? state.Error! : T("configure.native.settings." + key);
    }

    private void OnSaved(IReadOnlyCollection<string> keys)
    {
        if (keys.Contains(R.ExtraRatingSources) && App.Services.GetService<CatalogApi>() is { } catalog)
        {
            var context = catalog.CaptureContext();
            var details = App.Services.GetService<ItemDetailPrefetchCache>();
            catalog.InvalidateRatingCapability(); details?.Clear();
            _ = InvalidateRatingCachesAfterSettleAsync(catalog, details, context);
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && _controller?.State.Owner?.IsCurrent == true && App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell)
                shell.RefreshSettingsStatus();
        });
    }

    private static async Task InvalidateRatingCachesAfterSettleAsync(CatalogApi catalog, ItemDetailPrefetchCache? details, ApiRequestContext context)
    {
        await Task.Delay(RatingPolicySettle).ConfigureAwait(false);
        if (!catalog.IsCurrentContext(context)) return;
        catalog.InvalidateRatingCapability(); details?.Clear();
    }

    public Task<bool> CanLeaveAsync()
    {
        if (_controller?.State is { Saving: true, Owner.IsCurrent: true }) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }

    public void SuspendAdminPage() => Close();

    private void ClearUi()
    {
        _syncing = true;
        try
        {
            foreach (var control in _controls.Values)
            {
                switch (control)
                {
                    case TextBox input: input.Text = ""; input.IsEnabled = false; break;
                    case ToggleSwitch toggle: toggle.IsOn = false; toggle.IsEnabled = false; break;
                    case ComboBox select: select.SelectedIndex = -1; select.IsEnabled = false; break;
                }
            }
            foreach (var toggle in _ratingToggles.Values) { toggle.IsOn = false; toggle.IsEnabled = false; }
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
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Dispose(); _controller = null; }
        _leave = null; ClearUi();
        _alerts.Visibility = _fields.Visibility = _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    /// <summary>Official AdvancedSection: persisted disclosure, violet rail, exact visible count and dirty/warning force-open.</summary>
    private sealed class AdvancedSection : StackPanel
    {
        private readonly string _preferenceKey;
        private readonly Button _button;
        private readonly Border _rail;
        private LoggingDisclosurePreferences? _preferences;
        private bool _persisted, _forced, _open, _hover;
        private bool? _override;
        private int _count;
        internal StackPanel Fields { get; } = new();

        internal AdvancedSection(string preferenceKey, string automationId)
        {
            _preferenceKey = preferenceKey;
            _button = CreateButton(); AutomationProperties.SetAutomationId(_button, automationId);
            _button.Click += (_, _) => Toggle();
            _button.PointerEntered += (_, _) => { _hover = true; Render(); };
            _button.PointerExited += (_, _) => { _hover = false; Render(); };
            _button.GotFocus += (_, _) => Render();
            _rail = new Border
            {
                Child = Fields, BorderThickness = new(2, 0, 0, 0), Margin = new(6, 0, 0, 0), Padding = new(15, 0, 0, 0),
                BorderBrush = AdminMonitorUi.Solid(Windows.UI.Color.FromArgb(255, 167, 139, 250), .32), Visibility = Visibility.Collapsed,
            };
            Children.Add(_button); Children.Add(_rail); Render();
        }

        internal void Initialize(LoggingDisclosurePreferences preferences)
        {
            _preferences = preferences;
            try { _persisted = preferences.Read(_preferenceKey); } catch { _persisted = false; }
            _override = null; _forced = false; _open = _persisted; Render();
        }

        internal void Update(bool forced, int count)
        {
            if (forced != _forced) { _forced = forced; if (forced) _override = null; }
            _count = count; _open = _override ?? (_persisted || forced); Render();
        }

        internal void RefreshRules()
        {
            var visible = Fields.Children.Where(child => child.Visibility == Visibility.Visible).ToArray();
            for (var index = 0; index < visible.Length; index++)
                if (visible[index] is Grid grid) grid.BorderThickness = new(0, 0, 0, index == visible.Length - 1 ? 0 : 1);
        }

        private void Toggle()
        {
            _open = !_open; _override = _open; _persisted = _open;
            try { _preferences?.Write(_preferenceKey, _open); } catch { }
            Render();
        }

        private void Render()
        {
            var title = T("web.settings.advancedSection.title");
            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.Children.Add(AdminMonitorUi.Icon(_open ? "chevron-down" : "chevron-right", 14, _open ? U.Accent : U.Brush("SecondaryTextBrush")));
            var label = U.Text(title, 13); label.FontWeight = FontWeights.Medium; label.TextWrapping = TextWrapping.NoWrap;
            label.Foreground = _open || _hover ? U.Brush("PrimaryTextBrush") : U.Brush("SecondaryTextBrush");
            Grid.SetColumn(label, 1); content.Children.Add(label);
            var count = U.Text(AdminText.FormatNumber(_count), 12, true); count.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(count, 2); content.Children.Add(count); _button.Content = content;
            AutomationProperties.SetName(_button, T("web.settings.advancedSection.accessibleLabel",
                new Dictionary<string, object?> { ["title"] = title, ["count"] = (long)_count, ["count_text"] = AdminText.FormatNumber(_count) }));
            AutomationProperties.SetItemStatus(_button, _open ? "expanded" : "collapsed");
            _rail.Visibility = _open ? Visibility.Visible : Visibility.Collapsed;
        }

        private static Button CreateButton()
        {
            var template = (ControlTemplate)XamlReader.Load("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
                  <Border Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter Content="{TemplateBinding Content}" HorizontalContentAlignment="Stretch" VerticalAlignment="Center"/></Border>
                </ControlTemplate>
                """);
            return new Button
            {
                Template = template, MinWidth = 0, MinHeight = 0, Padding = new(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = true, CornerRadius = new(6),
                FocusVisualPrimaryBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("FocusVisualPrimaryBrush", Colors.White), .5),
                FocusVisualPrimaryThickness = new(3), FocusVisualSecondaryBrush = new SolidColorBrush(Colors.Transparent),
            };
        }
    }
}
