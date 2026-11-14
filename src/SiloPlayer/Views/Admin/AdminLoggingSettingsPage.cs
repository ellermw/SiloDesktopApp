using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

public interface IAdminLoggingNavigationGuard
{
    Task<bool> CanLeaveAsync();
}

public sealed record LoggingSettingsHealth(bool Loaded, bool LoadFailed, bool Conflict, int DirtyCount, bool RestartRequired);

public sealed partial class AdminLoggingSettingsPage : Page, IAdminLoggingNavigationGuard
{
    private readonly AdminMonitorApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly StackPanel _body = Stack(20);
    private readonly StackPanel _alerts = Stack(8);
    private readonly StackPanel _fields = Stack(20);
    private readonly StackPanel _serverAdvanced = Stack();
    private readonly StackPanel _retentionAdvanced = Stack();
    private readonly Dictionary<string, Control> _controls = new();
    private readonly Dictionary<string, FrameworkElement> _dots = new();
    private readonly Dictionary<string, FrameworkElement> _restarts = new();
    private readonly List<(string[] Keys, TextBlock Title, TextBlock Description, string? Original)> _groups = [];
    private readonly Border _saveBar;
    private readonly TextBlock _saveCount = Text("", 13);
    private readonly Button _save;
    private readonly Button _discard;
    private readonly Border _spacer = new() { Height = 112 };
    private readonly Button _serverDisclosure;
    private readonly Button _retentionDisclosure;
    private LoggingController? _controller;
    private ContentDialog? _dialog;
    private bool _syncing;
    private bool _allowNavigation;
    private bool _leaving;
    private LoggingSettingsHealth _health = new(false, false, false, 0, false);
    public LoggingController? Controller => _controller;
    public StackPanel MonitorScrollContent => _body;
    public LoggingSettingsHealth SettingsHealth => _health;
    public event Action<LoggingSettingsHealth>? SettingsHealthChanged;

    public AdminLoggingSettingsPage() : this(App.Services.GetRequiredService<AdminMonitorApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>()) { }
    public AdminLoggingSettingsPage(AdminMonitorApi api, AdminMonitorAuthority authority)
    {
        InitializeComponent();
        _api = api; _authority = authority; NavigationCacheMode = NavigationCacheMode.Disabled; AutomationProperties.SetAutomationId(this, "AdminLoggingSettingsPage");
        _body.MaxWidth = 1120; _body.HorizontalAlignment = HorizontalAlignment.Left;
        _body.Children.Add(Header(L("title"), T("web.admin.settings.search.logging.description"))); _body.Children.Add(_alerts); _body.Children.Add(_fields);
        var server = Stack(); server.Children.Add(Field("server.log_level", L("server.level.label"), L("server.level.description"), options: new[] { "debug", "info", "warn", "error" }.Select(value => (value, L("server.level." + value)))));
        _serverDisclosure = Disclosure("server", 1); server.Children.Add(_serverDisclosure);
        _serverAdvanced.Children.Add(Field("server.log_quiet", L("server.quiet.label"), L("server.quiet.description"), hint: "metadata, scanner")); server.Children.Add(AdvancedRail(_serverAdvanced));
        _fields.Children.Add(Group(L("server.group"), null, ["server.log_level", "server.log_quiet"], server));
        var retention = Stack(); retention.Children.Add(Field("opslog.retention_days", L("retention.days.label"), numeric: true, unit: L("retention.unitDays"))); retention.Children.Add(Field("opslog.max_size_mb", L("retention.maxSize.label"), numeric: true, unit: "MB"));
        _retentionDisclosure = Disclosure("retention", 3); retention.Children.Add(_retentionDisclosure);
        _retentionAdvanced.Children.Add(Field("opslog.max_rows", L("retention.maxRows.label"), numeric: true)); var subheading = Text(L("retention.permissions.heading"), 11, true); subheading.CharacterSpacing = 80; subheading.Margin = new(0, 20, 0, 6); _retentionAdvanced.Children.Add(subheading);
        _retentionAdvanced.Children.Add(Field("policy.decision_log_retention_days", L("retention.permissions.days.label"), numeric: true, unit: L("retention.unitDays")));
        _retentionAdvanced.Children.Add(Field("policy.decision_log_verbosity", L("retention.permissions.verbosity.label"), L("retention.permissions.verbosity.description"), options: new[] { ("digest", L("retention.permissions.verbosity.digest")), ("verbose", L("retention.permissions.verbosity.verbose")) })); retention.Children.Add(AdvancedRail(_retentionAdvanced));
        _fields.Children.Add(Group(L("retention.group"), L("retention.description"), ["opslog.retention_days", "opslog.max_size_mb", "opslog.max_rows", "policy.decision_log_retention_days", "policy.decision_log_verbosity"], retention));
        _body.Children.Add(_spacer);
        var root = new Grid(); var scroll = new ScrollViewer { Content = _body, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; scroll.SizeChanged += (_, args) => _body.Width = Math.Min(1120, args.NewSize.Width); root.Children.Add(scroll);
        var bar = Inline(16); bar.Children.Add(_saveCount); _discard = Button(T("web.shell.unsavedChanges.discard"), "LoggingDiscard", "GhostButtonStyle"); _discard.CornerRadius = new(999); _discard.MinHeight = 32; _discard.Click += (_, _) => _controller?.Discard(); bar.Children.Add(_discard);
        _save = Button(T("common.save"), "LoggingSave"); _save.CornerRadius = new(999); _save.MinHeight = 32; _save.Background = Violet; _save.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 21, 21, 26)); _save.Click += async (_, _) => await SaveAsync(); bar.Children.Add(_save);
        _saveBar = Panel(bar, 999, 8, true); _saveBar.Padding = new(20, 8, 8, 8); _saveBar.HorizontalAlignment = HorizontalAlignment.Center; _saveBar.VerticalAlignment = VerticalAlignment.Bottom; _saveBar.Margin = new(0, 0, 0, 24); _saveBar.Visibility = Visibility.Collapsed; AutomationProperties.SetName(_saveBar, T("web.shell.saveBar.regionLabel")); root.Children.Add(_saveBar); Content = root;
        Loaded += (_, _) => Open(); Unloaded += (_, _) => Close();
    }
    private FrameworkElement Field(string key, string label, string? description = null, bool numeric = false, string? unit = null, string? hint = null, IEnumerable<(string Value, string Label)>? options = null)
    {
        var row = new Grid { Padding = new(0, 14, 0, 14), ColumnSpacing = 24, RowSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var labels = Stack(5); var title = new LogsWrapPanel { Spacing = 6 }; var dot = new Border { Width = 6, Height = 6, Background = Violet, CornerRadius = new(3), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center }; _dots[key] = dot; title.Children.Add(dot); var fieldLabel = Text(label); fieldLabel.FontWeight = FontWeights.SemiBold; title.Children.Add(fieldLabel);
        var restart = Panel(Text(T("web.settings.restartBadge.label"), 11), 4, 4, true); ToolTipService.SetToolTip(restart, T("web.settings.restartBadge.title")); restart.Visibility = Visibility.Collapsed; _restarts[key] = restart; title.Children.Add(restart); labels.Children.Add(title); if (description != null) labels.Children.Add(Text(description, 13, true)); row.Children.Add(labels);
        Control control;
        if (options != null)
        {
            var select = Select("LoggingField_" + key, options, 240); AutomationProperties.SetName(select, label); select.SelectionChanged += (_, _) => { if (!_syncing) Set(key, Selected(select)); }; control = select;
        }
        else
        {
            var input = Input(hint ?? "", "LoggingField_" + key, numeric ? 160 : 240); AutomationProperties.SetName(input, label);
            // TextChanging is synchronous: rendering cannot queue a later draft edit,
            // and a user edit retires older queued model renders immediately.
            input.TextChanging += (_, _) => { if (!_syncing) Set(key, input.Text); }; control = input;
        }
        _controls[key] = control; var trailing = new Grid { ColumnSpacing = 12 }; trailing.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); trailing.ColumnDefinitions.Add(new() { Width = new(64) }); trailing.Children.Add(control); var units = Text(unit ?? "", 13, true); Grid.SetColumn(units, 1); trailing.Children.Add(units); Grid.SetColumn(trailing, 1); row.Children.Add(trailing);
        row.SizeChanged += (_, args) => { var stacked = args.NewSize.Width < 560; row.RowSpacing = stacked ? 12 : 0; Grid.SetColumn(trailing, stacked ? 0 : 1); Grid.SetRow(trailing, stacked ? 1 : 0); Grid.SetColumnSpan(labels, stacked ? 2 : 1); trailing.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right; };
        return new Border { Child = row, BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
    }
    private FrameworkElement Group(string label, string? description, string[] keys, StackPanel fields)
    {
        var section = new Grid { ColumnSpacing = 48, RowSpacing = 20, Padding = new(0, 24, 0, 0), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 1, 0, 0) }; section.ColumnDefinitions.Add(new() { Width = new(256) }); section.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); section.RowDefinitions.Add(new() { Height = GridLength.Auto }); section.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = Stack(6); var title = Text(label, 15); title.FontWeight = FontWeights.SemiBold; heading.Children.Add(title); var subtitle = Text(description ?? "", 13, true); subtitle.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible; heading.Children.Add(subtitle); _groups.Add((keys, title, subtitle, description)); section.Children.Add(heading); section.Children.Add(fields);
        void Arrange()
        {
            var wide = (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 && section.ActualWidth >= 832; section.RowSpacing = wide ? 0 : 20; section.ColumnDefinitions[0].Width = wide ? new(256) : new(1, GridUnitType.Star); Grid.SetColumn(fields, wide ? 1 : 0); Grid.SetRow(fields, wide ? 0 : 1); Grid.SetColumnSpan(fields, wide ? 1 : 2); Grid.SetColumnSpan(heading, wide ? 1 : 2); fields.Margin = wide ? new(0, -14, 0, 0) : new(0); section.Padding = new(0, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? 28 : 24, 0, 24);
        }
        section.SizeChanged += (_, _) => Arrange(); section.Loaded += (_, _) => Arrange(); return section;
    }
    private Button Disclosure(string section, int count)
    {
        var button = Button(T("web.settings.advancedSection.title") + " · " + count, "LoggingAdvanced_" + section, "GhostButtonStyle", "chevron-right"); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new(0, 12, 0, 12); button.Foreground = Violet;
        AutomationProperties.SetName(button, T("web.settings.advancedSection.accessibleLabel", ("title", T("web.settings.advancedSection.title")), ("count", count), ("count_text", count)));
        button.Click += (_, _) => { if (section == "server") _controller?.ToggleServerAdvanced(); else _controller?.ToggleRetentionAdvanced(); }; return button;
    }
    private static Border AdvancedRail(StackPanel content) => new() { Child = content, BorderBrush = VioletLine, BorderThickness = new(2, 0, 0, 0), Margin = new(6, 0, 0, 0), Padding = new(15, 0, 0, 0) };
    private void Set(string key, string value)
    {
        var controller = _controller;
        var state = controller?.State;
        if (state?.Loaded != true) return;
        // Ignore model echoes, including the displayed default for an unset value.
        var current = state.Values.GetValueOrDefault(key, "");
        if (key == "policy.decision_log_verbosity" && current.Length == 0) current = "digest";
        if (current == value) return;
        try { controller!.Set(key, value); } catch (OperationCanceledException) { }
    }
    private static readonly LoggingDisclosurePreferences DefaultDisclosurePreferences = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "monitor-ui"));
    private void Open() { Close(); _controller = new(_api, _authority, App.Services.GetService<LoggingDisclosurePreferences>() ?? DefaultDisclosurePreferences); _controller.Changed += QueueRender; _ = _controller.OpenAsync(); }
    internal void SuspendMonitor() => Close();

    private void Close()
    {
        _dialog?.Hide(); _dialog = null; if (_controller != null) { _controller.Changed -= QueueRender; _controller.Dispose(); _controller = null; }
        _syncing = true; foreach (var control in _controls.Values) { control.IsEnabled = false; if (control is TextBox input) input.Text = ""; else if (control is ComboBox select) select.SelectedIndex = -1; } _syncing = false;
        _alerts.Children.Clear(); _saveBar.Visibility = Visibility.Collapsed; _spacer.Visibility = Visibility.Collapsed; SetHealth(new(false, false, false, 0, false));
    }
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return; var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (IsLoaded && ReferenceEquals(controller, _controller) && controller.IsCurrent(state)) Render(state); });
    }
    private void Render(LoggingPageSnapshot state)
    {
        _alerts.Children.Clear(); _alerts.Visibility = Visibility.Collapsed; _syncing = true;
        try
        {
            foreach (var (key, control) in _controls)
            {
                control.IsEnabled = state.Loaded; var value = state.Values.GetValueOrDefault(key, "");
                if (key == "policy.decision_log_verbosity" && value.Length == 0) value = "digest";
                if (control is TextBox input && input.Text != value) input.Text = value; else if (control is ComboBox select && (select.SelectedIndex < 0 || Selected(select) != value)) SetSelected(select, value);
                _dots[key].Visibility = state.DirtyKeys.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
                var groupAll = _groups.First(group => group.Keys.Contains(key)).Keys.All(state.RestartKeys.Has); _restarts[key].Visibility = state.RestartKeys.Has(key) && !groupAll ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        finally { _syncing = false; }
        _fields.Visibility = state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        if (state.Owner == null) { _dialog?.Hide(); _saveBar.Visibility = Visibility.Collapsed; _spacer.Visibility = Visibility.Collapsed; SetHealth(new(false, false, false, 0, false)); return; }
        if (state.Loading) _alerts.Children.Add(Text(T("web.admin.settings.fields.loadingSettings"), 14, true));
        if (state.Error != null) _alerts.Children.Add(Banner(state.Error, true));
        if (!state.Loading && (!state.Loaded || state.Conflict)) { var retry = Button(state.Conflict ? "Reload server changes" : "Retry", "LoggingReload"); retry.Click += async (_, _) => await ReconcileAsync(); _alerts.Children.Add(retry); }
        if (state.Notice != null) _alerts.Children.Add(Text(state.Notice, 13, true));
        if (state.RestartRequired) _alerts.Children.Add(Banner(T("web.admin.settings.fields.restartAll")));
        foreach (var group in _groups)
        {
            var dirty = group.Keys.Any(state.DirtyKeys.Contains); group.Title.Text = group.Title.Text.TrimEnd(' ', '•') + (dirty ? "  •" : "");
            group.Description.Text = group.Keys.All(state.RestartKeys.Has) ? (group.Original == null ? T("web.admin.settings.fields.restartAll") : T("web.admin.settings.fields.restartAllWithDescription", ("description", group.Original))) : group.Original ?? "";
        }
        _alerts.Visibility = _alerts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var group in _groups) group.Description.Visibility = string.IsNullOrEmpty(group.Description.Text) ? Visibility.Collapsed : Visibility.Visible;
        _serverAdvanced.Visibility = state.ServerAdvancedOpen ? Visibility.Visible : Visibility.Collapsed; ((FrameworkElement)_serverAdvanced.Parent).Visibility = _serverAdvanced.Visibility;
        _retentionAdvanced.Visibility = state.RetentionAdvancedOpen ? Visibility.Visible : Visibility.Collapsed; ((FrameworkElement)_retentionAdvanced.Parent).Visibility = _retentionAdvanced.Visibility;
        _serverDisclosure.Content = DisclosureContent(state.ServerAdvancedOpen, 1); _retentionDisclosure.Content = DisclosureContent(state.RetentionAdvancedOpen, 3);
        _saveCount.Text = T("web.shell.saveBar.count", ("count", state.DirtyKeys.Count), ("shown", state.DirtyKeys.Count)); _save.Content = T(state.Saving ? "web.ui.saving" : "common.save"); _save.IsEnabled = !state.Saving && !state.Conflict; _discard.IsEnabled = true;
        _saveBar.Visibility = state.DirtyKeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed; _spacer.Visibility = _saveBar.Visibility;
        SetHealth(new(state.Loaded, !state.Loaded && state.Error != null, state.Conflict, state.DirtyKeys.Count, state.RestartRequired));
    }
    private static Grid DisclosureContent(bool open, int count) { var content = new Grid { ColumnSpacing = 10 }; content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.Children.Add(Icon(open ? "chevron-down" : "chevron-right", 14)); var label = Text(T("web.settings.advancedSection.title"), 13, !open); Grid.SetColumn(label, 1); content.Children.Add(label); var number = Text(count.ToString(), 12, true); Grid.SetColumn(number, 2); content.Children.Add(number); return content; }
    private void SetHealth(LoggingSettingsHealth health) { if (_health == health) return; _health = health; SettingsHealthChanged?.Invoke(health); }
    private async Task SaveAsync()
    {
        var controller = _controller; if (controller == null) return;
        try { using var action = controller.CaptureAction(); await controller.SaveAsync(action); }
        catch (OperationCanceledException) { }
    }
    private async Task ReconcileAsync()
    {
        var controller = _controller; if (controller == null) return;
        try
        {
            using var action = controller.CaptureAction(); var latest = await controller.PrepareReconciliationAsync(action); if (latest == null || !controller.IsActionCurrent(action)) return;
            if (!controller.State.Loaded || controller.State.DirtyKeys.Count == 0) { controller.AcceptReconciliation(action, latest, false); return; }
            var result = await ConfirmAsync(action, T("logs.native.logging.conflictTitle"), T("logs.native.logging.conflictDescription"), T("logs.native.logging.keepEdits"), T("logs.native.logging.discardEdits"));
            if (ReferenceEquals(controller, _controller) && controller.IsActionCurrent(action) && result != ContentDialogResult.None) controller.AcceptReconciliation(action, latest, result == ContentDialogResult.Primary);
        }
        catch (OperationCanceledException) { }
    }
    public async Task<bool> CanLeaveAsync()
    {
        var controller = _controller; if (controller == null || controller.State.DirtyKeys.Count == 0) return true; if (_leaving || _dialog != null) return false; _leaving = true;
        try
        {
            using var action = controller.CaptureAction(); var result = await ConfirmAsync(action, T("web.shell.unsavedChanges.title"), T("web.shell.unsavedChanges.description"), T("web.shell.unsavedChanges.discard"));
            if (!ReferenceEquals(controller, _controller) || !controller.IsActionCurrent(action) || result != ContentDialogResult.Primary) return false; controller.Discard(); return true;
        }
        catch (OperationCanceledException) { return false; }
        finally { _leaving = false; }
    }
    private async Task<ContentDialogResult> ConfirmAsync(MonitorAuthorityLease action, string title, string description, string primary, string? secondary = null)
    {
        if (_dialog != null || !action.IsCurrent) return ContentDialogResult.None;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = description, PrimaryButtonText = primary, SecondaryButtonText = secondary ?? "", CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close }; _dialog = dialog;
        using var cancellation = action.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try { var result = await dialog.ShowAsync(); action.ThrowIfRetired(); return result; }
        finally { if (ReferenceEquals(dialog, _dialog)) _dialog = null; }
    }
    protected override void OnNavigatingFrom(NavigatingCancelEventArgs args)
    {
        base.OnNavigatingFrom(args); if (_allowNavigation) { _allowNavigation = false; return; } if (_controller?.State.DirtyKeys.Count is not > 0) return;
        args.Cancel = true; var frame = Frame; var type = args.SourcePageType; var parameter = args.Parameter; var mode = args.NavigationMode;
        _ = ResumeAsync();
        async Task ResumeAsync()
        {
            if (!await CanLeaveAsync() || Frame != frame || !IsLoaded) return; _allowNavigation = true;
            try { if (mode == NavigationMode.Back && frame.CanGoBack) frame.GoBack(new SuppressNavigationTransitionInfo()); else if (mode == NavigationMode.Forward && frame.CanGoForward) frame.GoForward(); else frame.Navigate(type, parameter, new SuppressNavigationTransitionInfo()); }
            finally { _allowNavigation = false; }
        }
    }
    private static string L(string key) => T("web.admin.settings.logging." + key);
}
