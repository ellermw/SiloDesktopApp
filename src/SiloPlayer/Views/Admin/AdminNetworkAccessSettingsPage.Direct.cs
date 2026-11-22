using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Windows.Foundation;
using R = SiloPlayer.Core.Services.Admin.AdminDirectConnectionsRules;
using T = SiloPlayer.Core.Services.Admin.AdminDirectConnectionsText;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The Direct connections section of Network access (official <c>DirectConnectionsSection.tsx</c>): the enable switch, the
/// readiness diagnostics with Run check, and the advanced Port mapping and STUN servers settings. It exists only while the saved
/// switch is on or a key is staged; the page-wide save bar and leave guard cover its three settings. Every server string is shown
/// as plain text, left to right.
/// </summary>
internal sealed class NetworkAccessDirectSection : StackPanel
{
    private readonly Func<AdminDirectConnectionsController?> _controller;
    private readonly Func<LoggingDisclosurePreferences?> _preferences;
    private readonly StackPanel _loadAlert = new() { Spacing = 8, Margin = new(0, 24, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _loadText = U.Text("", 14);
    private readonly Button _loadReload = U.Action(T.T("configure.native.direct.reload"), "NetworkAccessDirectLoadReload");
    private readonly AdminSettingsGroup _group = new(T.T("web.admin.settings.network.direct.title"), T.T("web.admin.settings.network.direct.description"));
    private readonly StackPanel _saveAlert = new() { Spacing = 8, Padding = new(0, 14, 0, 14), Visibility = Visibility.Collapsed };
    private readonly TextBlock _saveText = U.Text("", 14);
    private readonly Button _saveReload = U.Action(T.T("configure.native.direct.reload"), "NetworkAccessDirectSaveReload");
    private readonly ToggleSwitch _enabled = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly AdminSettingsRow _enabledRow;
    private readonly StackPanel _diagnostics = new() { Spacing = 16, Padding = new(0, 16, 0, 16) };
    private readonly DirectBetweenLine _top;
    private readonly (Button Button, Border Icon, TextBlock Label) _run;
    private readonly TextBlock _checkMessage = U.Text("", 14);
    private readonly StackPanel _statusAlert = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _statusText = U.Text("", 14);
    private readonly Button _retry = U.Action(AdminDirectConnectionsText.T("configure.native.network.retry"), "NetworkAccessDirectRetry");
    private readonly StackPanel _skeleton = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    private readonly StackPanel _hosts = new() { Spacing = 0 };
    private readonly StackPanel _advanced = new();
    private readonly Button _disclosure;
    private readonly Border _rail;
    private readonly ToggleSwitch _portMapping = new() { OffContent = "", OnContent = "", MinWidth = 0 };
    private readonly AdminSettingsRow _portRow;
    private readonly TextBox _stun = U.Input(T.T("web.admin.settings.network.direct.stunServers.label"), "NetworkAccessDirectStun");
    private readonly AdminSettingsRow _stunRow;
    private readonly TextBlock _stunError = U.Text("", 13);
    private readonly Dictionary<string, DirectHostBlock> _blocks = new(StringComparer.Ordinal);
    private bool _syncing, _checking, _disclosureHover, _persistedOpen, _forced, _advancedOpen, _preferencesRead;
    private bool? _override;
    private string _hostSignature = "";
    private AdminDirectConnectionsState _last = new();

    internal AdminSettingsGroup Group => _group;
    internal ToggleSwitch EnabledSwitch => _enabled;
    internal ToggleSwitch PortMappingSwitch => _portMapping;
    internal TextBox StunBox => _stun;
    internal Button RunCheckButton => _run.Button;
    internal Button RetryButton => _retry;
    internal Button Disclosure => _disclosure;
    internal bool AdvancedOpen => _advancedOpen;
    internal IReadOnlyCollection<DirectHostBlock> Blocks => _blocks.Values;

    internal NetworkAccessDirectSection(Func<AdminDirectConnectionsController?> controller, Func<LoggingDisclosurePreferences?> preferences)
    {
        _controller = controller; _preferences = preferences;
        AutomationProperties.SetAutomationId(_group, "NetworkAccessDirectGroup");
        // The settings read failed: the section cannot know whether it exists, so say so and offer a reload.
        AutomationProperties.SetAutomationId(_loadText, "NetworkAccessDirectLoadError");
        _loadText.Foreground = U.Brush("ErrorBrush");
        AutomationProperties.SetLiveSetting(_loadText, AutomationLiveSetting.Assertive);
        _loadReload.HorizontalAlignment = HorizontalAlignment.Left;
        _loadReload.Click += async (_, _) => { if (_controller() is { } controller) await controller.ReloadAsync(preserveDraft: true); };
        _loadAlert.Children.Add(_loadText); _loadAlert.Children.Add(_loadReload);
        Children.Add(_loadAlert);

        // A save the server refused, or whose outcome is unknown, is explained inside the group, next to what was staged.
        AutomationProperties.SetAutomationId(_saveText, "NetworkAccessDirectSaveError");
        _saveText.Foreground = U.Brush("ErrorBrush");
        _saveText.FlowDirection = FlowDirection.LeftToRight;
        AutomationProperties.SetLiveSetting(_saveText, AutomationLiveSetting.Assertive);
        _saveReload.HorizontalAlignment = HorizontalAlignment.Left;
        _saveReload.Click += async (_, _) => { if (_controller() is { } controller) await controller.ReloadAsync(preserveDraft: true); };
        _saveAlert.Children.Add(_saveText); _saveAlert.Children.Add(_saveReload);
        _group.Fields.Children.Add(_saveAlert);

        _enabledRow = new AdminSettingsRow(T.T("web.admin.settings.network.direct.enable"), "", _enabled);
        AutomationProperties.SetAutomationId(_enabled, "NetworkAccessDirectEnabled");
        _enabled.Toggled += (_, _) => { if (!_syncing) _controller()?.SetEnabled(_enabled.IsOn); };
        _group.Add(_enabledRow);

        // Diagnostics: the note and Run check on one line (space between, wrapping), the check message, then the status area.
        var note = U.Text(T.T("web.admin.settings.network.direct.readinessNote"), 14, true);
        _run = NetworkAccessUi.SmallButton(T.T("web.admin.settings.network.direct.runCheck"), "NetworkAccessDirectRunCheck", filled: false);
        _run.Button.Click += async (_, _) => { if (_controller() is { } controller) await controller.RunCheckAsync(); };
        _top = new DirectBetweenLine(note, _run.Button);
        _diagnostics.Children.Add(_top);
        AutomationProperties.SetAutomationId(_checkMessage, "NetworkAccessDirectCheckMessage");
        _checkMessage.FlowDirection = FlowDirection.LeftToRight; _checkMessage.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_checkMessage, AutomationLiveSetting.Assertive);
        _diagnostics.Children.Add(_checkMessage);
        AutomationProperties.SetName(_skeleton, T.T("configure.native.direct.loadingStatus"));
        AutomationProperties.SetLiveSetting(_skeleton, AutomationLiveSetting.Polite);
        _skeleton.Children.Add(NetworkAccessUi.FractionSkeleton(20, 2d / 3));
        _skeleton.Children.Add(U.Skeleton(80));
        _diagnostics.Children.Add(_skeleton);
        AutomationProperties.SetAutomationId(_statusText, "NetworkAccessDirectStatusError");
        _statusText.Foreground = U.Brush("ErrorBrush");
        AutomationProperties.SetLiveSetting(_statusText, AutomationLiveSetting.Assertive);
        _retry.HorizontalAlignment = HorizontalAlignment.Left;
        _retry.Click += (_, _) => _controller()?.RetryStatus();
        _statusAlert.Children.Add(_statusText); _statusAlert.Children.Add(_retry);
        _diagnostics.Children.Add(_statusAlert);
        AutomationProperties.SetAutomationId(_hosts, "NetworkAccessDirectHosts");
        _diagnostics.Children.Add(_hosts);
        _group.Fields.Children.Add(_diagnostics);

        // Advanced: Port mapping and STUN servers.
        _disclosure = NewDisclosure();
        _rail = new Border
        {
            Child = new StackPanel(), BorderThickness = new(2, 0, 0, 0), Margin = new(6, 0, 0, 0), Padding = new(15, 0, 0, 0),
            BorderBrush = AdminMonitorUi.Solid(Windows.UI.Color.FromArgb(255, 167, 139, 250), .32), Visibility = Visibility.Collapsed,
        };
        _disclosure.Click += (_, _) => ToggleAdvanced();
        var rail = (StackPanel)_rail.Child;
        _portRow = new AdminSettingsRow(T.T("web.admin.settings.network.direct.portMapping.label"), T.T("web.admin.settings.network.direct.portMapping.description"), _portMapping);
        AutomationProperties.SetAutomationId(_portMapping, "NetworkAccessDirectPortMapping");
        _portMapping.Toggled += (_, _) => { if (!_syncing) _controller()?.SetPortMapping(_portMapping.IsOn); };
        rail.Children.Add(_portRow);
        _stunRow = new AdminSettingsRow(T.T("web.admin.settings.network.direct.stunServers.label"), T.T("web.admin.settings.network.direct.stunServers.description"), _stun);
        _stun.FlowDirection = FlowDirection.LeftToRight;
        _stun.TextChanged += (_, _) => { if (!_syncing) _controller()?.SetStunServers(_stun.Text); };
        AutomationProperties.SetAutomationId(_stunError, "NetworkAccessDirectStunError");
        _stunError.Foreground = U.Brush("ErrorBrush"); _stunError.FlowDirection = FlowDirection.LeftToRight; _stunError.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_stunError, AutomationLiveSetting.Polite);
        _stunRow.AddStatus(_stunError);
        _stunRow.BorderThickness = new(0); // the last row of the list: official last:border-b-0
        rail.Children.Add(_stunRow);
        _advanced.Children.Add(_disclosure); _advanced.Children.Add(_rail);
        _group.Fields.Children.Add(_advanced);
        Children.Add(_group);
        _group.Visibility = Visibility.Collapsed;
        RenderDisclosure();
    }

    /// <summary>The message the last refused or unknown save is showing, or null; the page also raises it as a toast, as the official save does.</summary>
    internal string? SaveMessage { get; private set; }

    internal bool HasContent => _group.Visibility == Visibility.Visible || _loadAlert.Visibility == Visibility.Visible;

    /// <summary>Reads the persisted disclosure choice once per page visit.</summary>
    internal void Reset()
    {
        _preferencesRead = false; _override = null; _forced = false;
        foreach (var block in _blocks.Values) block.Clear();
        _blocks.Clear(); _hosts.Children.Clear(); _hostSignature = "";
        _last = new();
        _group.Visibility = Visibility.Collapsed; _loadAlert.Visibility = Visibility.Collapsed;
    }

    internal void Render(AdminDirectConnectionsState state)
    {
        _last = state;
        var current = state.Owner?.IsCurrent == true;
        if (!_preferencesRead)
        {
            _preferencesRead = true;
            try { _persistedOpen = _preferences()?.Read("network.direct") ?? false; } catch { _persistedOpen = false; }
        }
        // The settings read failed: the page cannot tell whether the section exists. Say so, with a reload.
        var loadFailed = current && state.LoadFailure != null;
        _loadAlert.Visibility = loadFailed ? Visibility.Visible : Visibility.Collapsed;
        if (loadFailed) U.UpdateLiveText(_loadText, SettingsMessage(state.LoadFailure) ?? T.T("configure.native.direct.settingsFailed"));
        _loadReload.IsEnabled = !state.Loading;
        var visible = current && state.SectionVisible;
        _group.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) { SaveMessage = null; ClearHosts(); return; }

        _syncing = true;
        try
        {
            var dirty = state.DirtyKeys;
            bool Editable(string key) => !state.Loading && !state.Saving && !state.EnvManaged.IsManaged(key) && state.Loaded;
            _group.State(dirty.Count > 0, T.T("web.admin.settings.network.direct.description"));
            RenderSaveMessage(state);
            _enabled.IsOn = state.Values.GetValueOrDefault(R.Enabled) == "true";
            _enabled.IsEnabled = Editable(R.Enabled);
            _enabledRow.State(dirty.Contains(R.Enabled), false);
            _enabledRow.SetEnvironmentNote(state.EnvManaged.Note(R.Enabled));
            _portMapping.IsOn = state.Values.GetValueOrDefault(R.PortMapping) == "true";
            _portMapping.IsEnabled = Editable(R.PortMapping);
            _portRow.State(dirty.Contains(R.PortMapping), false);
            _portRow.SetEnvironmentNote(state.EnvManaged.Note(R.PortMapping));
            var stun = state.Values.GetValueOrDefault(R.StunServers) ?? "";
            // A focused box keeps the admin's typing; the draft already holds it.
            if (_stun.Text != stun && _stun.FocusState == FocusState.Unfocused) _stun.Text = stun;
            _stun.IsEnabled = Editable(R.StunServers);
            _stunRow.State(dirty.Contains(R.StunServers), false);
            _stunRow.SetEnvironmentNote(state.EnvManaged.Note(R.StunServers));
            var stunMessage = state.Stun.IsValid ? "" : state.Stun.Message;
            U.UpdateLiveText(_stunError, stunMessage);
            _stunError.Visibility = stunMessage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(_stun, _stunRow.HelpText);
            UpdateAdvanced(R.AdvancedForcedOpen(dirty));
        }
        finally { _syncing = false; }
        RenderDiagnostics(state);
        _group.RefreshRules();
    }

    private void RenderSaveMessage(AdminDirectConnectionsState state)
    {
        string? message = null;
        if (state.Conflict) message = T.T("configure.native.direct.conflict");
        else if (state.OutcomeUnknown) message = T.T("configure.native.direct.outcomeUnknown");
        else if (!string.IsNullOrEmpty(state.SaveDetail)) message = state.SaveDetail;
        else if (state.SaveFailure is { } failure) message = SettingsMessage(failure) ?? T.T("configure.native.direct.saveFailed");
        SaveMessage = message;
        _saveAlert.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) U.UpdateLiveText(_saveText, message);
        _saveReload.Visibility = state.Conflict || state.OutcomeUnknown ? Visibility.Visible : Visibility.Collapsed;
        _saveReload.IsEnabled = !state.Loading && !state.Saving;
    }

    private void RenderDiagnostics(AdminDirectConnectionsState state)
    {
        SetChecking(state.Checking);
        string? message = null;
        if (state.CheckOutcome == AdminDirectConnectionsCheckOutcome.Unknown) message = T.T("configure.native.direct.checkUnknown");
        else if (state.CheckOutcome == AdminDirectConnectionsCheckOutcome.Refused)
            message = !string.IsNullOrEmpty(state.CheckDetail) ? state.CheckDetail : SettingsMessage(state.CheckFailure) ?? T.T("configure.native.direct.checkRefused");
        _checkMessage.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
        _checkMessage.Foreground = U.Brush(state.CheckOutcome == AdminDirectConnectionsCheckOutcome.Unknown ? "WarningBrush" : "ErrorBrush");
        if (message != null) U.UpdateLiveText(_checkMessage, message);
        _skeleton.Visibility = state.StatusLoading ? Visibility.Visible : Visibility.Collapsed;
        // A failed read keeps the last status on screen with the failure above it and keeps polling.
        var failed = state.StatusFailure != null;
        _statusAlert.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (failed) U.UpdateLiveText(_statusText, StatusMessage(state));
        if (state.Status == null) { ClearHosts(); return; }
        var views = state.Status.Hosts.Select(host => AdminDirectConnectionsPresentation.Present(host, FormatChecked)).ToArray();
        var signature = string.Join("\u001e", views.Select(Signature));
        if (signature == _hostSignature && _blocks.Count == views.Length) return;
        _hostSignature = signature;
        foreach (var gone in _blocks.Keys.Where(id => views.All(view => view.Id != id)).ToArray()) { _hosts.Children.Remove(_blocks[gone]); _blocks.Remove(gone); }
        for (var index = 0; index < views.Length; index++)
        {
            var view = views[index];
            if (!_blocks.TryGetValue(view.Id, out var block)) { block = new DirectHostBlock(view.Id); _blocks[view.Id] = block; }
            var existing = _hosts.Children.IndexOf(block);
            if (existing != index)
            {
                if (existing >= 0) _hosts.Children.RemoveAt(existing);
                _hosts.Children.Insert(Math.Min(index, _hosts.Children.Count), block);
            }
            block.Padding = new(0, index == 0 ? 0 : 16, 0, index == views.Length - 1 ? 0 : 16);
            block.Render(view);
        }
    }

    private void ClearHosts()
    {
        if (_blocks.Count == 0 && _hosts.Children.Count == 0) { _hostSignature = ""; return; }
        foreach (var block in _blocks.Values) block.Clear();
        _blocks.Clear(); _hosts.Children.Clear(); _hostSignature = "";
    }

    private static string Signature(AdminDirectConnectionsHostView view) => string.Join("\u001f", new[] { view.Id, view.Heading, view.LastChecked, view.ListenersHeading ?? "", view.TuningHeading }
        .Concat(view.Features.Concat(view.Listeners).Concat(view.Knobs).Select(row => row.Key + "\u001d" + row.Label + "\u001d" + row.Variable + "\u001d" + row.Value + "\u001d" + string.Join("\u001c", row.Details))));

    /// <summary>Official formatDateTime(value, { seconds: false }): "date, time" under the display preferences.</summary>
    private static string FormatChecked(DateTimeOffset at) => $"{DateTimeDisplay.FormatDate(at)}, {DateTimeDisplay.FormatTime(at)}";

    private void SetChecking(bool checking)
    {
        _run.Button.IsEnabled = !checking;
        if (_checking == checking) return;
        _checking = checking;
        _run.Icon.Child = checking ? AdminMonitorUi.Spinner(16, ((SolidColorBrush)_run.Button.Foreground).Color, "loader-circle") : null;
        _run.Icon.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
        _run.Button.Padding = checking ? new(10, 0, 10, 0) : new(12, 0, 12, 0);
    }

    private static string? SettingsMessage(AdminFailureKind? failure) => AdminNetworkAccessSettingsPage.FailureMessage(failure);

    private static string StatusMessage(AdminDirectConnectionsState state)
    {
        if (state.StatusLimit is { } limit) return T.T("configure.native.network.limit", new { limit });
        if (SettingsMessage(state.StatusFailure) is { } message && state.StatusFailure is not (AdminFailureKind.InvalidResponse or AdminFailureKind.Transport)) return message;
        return T.T(state.Status != null ? "configure.native.direct.refreshFailed" : "configure.native.direct.statusFailed");
    }

    // ── Advanced disclosure (official AdvancedSection) ───────────────────────────

    private void ToggleAdvanced()
    {
        var next = !_advancedOpen;
        _override = next; _persistedOpen = next; _advancedOpen = next;
        try { _preferences()?.Write("network.direct", next); } catch { /* the disclosure still works this session */ }
        RenderDisclosure();
    }

    /// <summary>A reason to force the section open (a dirty field inside) outranks an older manual collapse.</summary>
    private void UpdateAdvanced(bool forced)
    {
        if (forced != _forced) { _forced = forced; if (forced) _override = null; }
        var open = _override ?? (_persistedOpen || forced);
        if (open == _advancedOpen && _disclosure.Content != null) return;
        _advancedOpen = open;
        RenderDisclosure();
    }

    private Button NewDisclosure()
    {
        var template = (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border x:Name="DisclosureRoot" Background="Transparent" Padding="{TemplateBinding Padding}"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ContentPresenter Content="{TemplateBinding Content}" HorizontalContentAlignment="Stretch" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        var button = new Button
        {
            Template = template, MinWidth = 0, MinHeight = 0, Padding = new(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = true,
            FocusVisualPrimaryBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("FocusVisualPrimaryBrush", Microsoft.UI.Colors.White), .5),
            FocusVisualPrimaryThickness = new(3), FocusVisualSecondaryBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new(6),
        };
        AutomationProperties.SetAutomationId(button, "NetworkAccessDirectAdvanced");
        button.PointerEntered += (_, _) => { _disclosureHover = true; RenderDisclosure(); };
        button.PointerExited += (_, _) => { _disclosureHover = false; RenderDisclosure(); };
        button.GotFocus += (_, _) => RenderDisclosure();
        return button;
    }

    private void RenderDisclosure()
    {
        var open = _advancedOpen;
        var count = R.AdvancedKeys.Count;
        var title = AdminText.T("web.settings.advancedSection.title");
        var foreground = open || _disclosureHover ? U.Brush("PrimaryTextBrush") : U.Brush("SecondaryTextBrush");
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var chevron = AdminMonitorUi.Icon(open ? "chevron-down" : "chevron-right", 14, open ? U.Accent : U.Brush("SecondaryTextBrush"));
        chevron.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(chevron);
        var label = U.Text(title, 13); label.FontWeight = FontWeights.Medium; label.Foreground = foreground; label.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(label, 1); content.Children.Add(label);
        var number = U.Text(AdminText.FormatNumber(count), 12, true); number.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(number, 2); content.Children.Add(number);
        _disclosure.Content = content;
        AutomationProperties.SetName(_disclosure, AdminText.T("web.settings.advancedSection.accessibleLabel",
            new Dictionary<string, object?> { ["title"] = title, ["count"] = (long)count, ["count_text"] = AdminText.FormatNumber(count) }));
        AutomationProperties.SetItemStatus(_disclosure, open ? "expanded" : "collapsed");
        _rail.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// <c>flex flex-wrap items-center justify-between gap-3</c>: the note takes the left, the button the right; the button wraps
/// below the note only when both cannot share the line.
/// </summary>
internal sealed class DirectBetweenLine : Panel
{
    private const double Gap = 12;
    private readonly FrameworkElement _note, _action;
    private bool _wrap;
    internal DirectBetweenLine(FrameworkElement note, FrameworkElement action) { _note = note; _action = action; Children.Add(note); Children.Add(action); }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 100000 : available.Width;
        _action.Measure(new(width, double.PositiveInfinity));
        _note.Measure(new(width, double.PositiveInfinity));
        var action = _action.DesiredSize;
        var note = _note.DesiredSize;
        _wrap = note.Width + Gap + action.Width > width + 1;
        if (_wrap) _note.Measure(new(width, double.PositiveInfinity));
        // Never ask the note for exactly its own desired width again: a fractional scale could then wrap it.
        else _note.Measure(new(Math.Max(width - Gap - action.Width, note.Width + 1), double.PositiveInfinity));
        note = _note.DesiredSize;
        var height = _wrap ? note.Height + Gap + action.Height : Math.Max(note.Height, action.Height);
        return new(double.IsInfinity(available.Width) ? note.Width + Gap + action.Width : width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var note = _note.DesiredSize; var action = _action.DesiredSize;
        if (_wrap)
        {
            _note.Arrange(new(0, 0, Math.Min(final.Width, note.Width), note.Height));
            _action.Arrange(new(0, note.Height + Gap, Math.Min(final.Width, action.Width), action.Height));
        }
        else
        {
            var height = Math.Max(note.Height, action.Height);
            _note.Arrange(new(0, (height - note.Height) / 2, Math.Min(final.Width, note.Width), note.Height));
            _action.Arrange(new(Math.Max(0, final.Width - action.Width), (height - action.Height) / 2, action.Width, action.Height));
        }
        return final;
    }
}

/// <summary>One host: its heading and checked time, the feature list, the listeners (only when there are any) and the host tuning list.</summary>
internal sealed class DirectHostBlock : StackPanel
{
    private readonly string _id;
    private readonly TextBlock _heading = U.Text("", 16);
    private readonly TextBlock _checked = U.Text("", 12, true);
    private readonly StackPanel _features = new();
    private readonly TextBlock _listenersHeading = U.Text("", 16);
    private readonly StackPanel _listeners = new();
    private readonly TextBlock _tuningHeading = U.Text("", 16);
    private readonly StackPanel _knobs = new();
    private readonly StackPanel _listenersBlock = new() { Spacing = 8 };
    internal IEnumerable<DirectDiagnosticRow> Rows => _features.Children.Concat(_listeners.Children).Concat(_knobs.Children).OfType<DirectDiagnosticRow>();
    internal TextBlock Heading => _heading;
    internal TextBlock Checked => _checked;
    internal TextBlock ListenersHeading => _listenersHeading;
    internal TextBlock TuningHeading => _tuningHeading;

    internal DirectHostBlock(string id)
    {
        _id = id; Spacing = 12;
        AutomationProperties.SetAutomationId(this, "NetworkAccessDirectHost_" + id);
        _heading.FontWeight = FontWeights.Medium; _heading.LineHeight = 24;
        AutomationProperties.SetHeadingLevel(_heading, AutomationHeadingLevel.Level3);
        _checked.LineHeight = 16;
        _checked.FlowDirection = FlowDirection.LeftToRight;
        var head = new StackPanel { Spacing = 6 };
        head.Children.Add(_heading); head.Children.Add(_checked);
        Children.Add(head);
        Children.Add(_features);
        foreach (var heading in new[] { _listenersHeading, _tuningHeading }) { heading.FontWeight = FontWeights.Medium; heading.LineHeight = 24; AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level4); }
        _listenersBlock.Children.Add(_listenersHeading); _listenersBlock.Children.Add(_listeners);
        Children.Add(_listenersBlock);
        var tuning = new StackPanel { Spacing = 8 };
        tuning.Children.Add(_tuningHeading); tuning.Children.Add(_knobs);
        Children.Add(tuning);
    }

    internal void Render(AdminDirectConnectionsHostView view)
    {
        _heading.Text = view.Heading;
        AutomationProperties.SetName(_heading, view.Heading);
        _checked.Text = view.LastChecked;
        Fill(_features, view.Features, "NetworkAccessDirectFeature_", false);
        _listenersBlock.Visibility = view.ListenersHeading != null ? Visibility.Visible : Visibility.Collapsed;
        _listenersHeading.Text = view.ListenersHeading ?? "";
        AutomationProperties.SetName(_listenersHeading, view.ListenersHeading ?? "");
        Fill(_listeners, view.Listeners, "NetworkAccessDirectListener_", false);
        _tuningHeading.Text = view.TuningHeading;
        AutomationProperties.SetName(_tuningHeading, view.TuningHeading);
        Fill(_knobs, view.Knobs, "NetworkAccessDirectKnob_", true);
    }

    /// <summary>Rows are rebuilt from the view: their hairlines (divide-y) belong to the list, not the row.</summary>
    private static void Fill(StackPanel list, IReadOnlyList<AdminDirectConnectionsRowView> rows, string idPrefix, bool tuning)
    {
        list.Children.Clear();
        for (var index = 0; index < rows.Count; index++)
            list.Children.Add(new DirectDiagnosticRow(rows[index], idPrefix + rows[index].Key, tuning, first: index == 0));
    }

    internal void Clear() { _features.Children.Clear(); _listeners.Children.Clear(); _knobs.Children.Clear(); }
}

/// <summary>
/// One <c>DiagnosticRow</c>: the label (and, for host tuning, the variable under it) on the left, the value on the label line at the
/// right, and the detail lines under the value (host tuning) or under the whole row. Server text is plain text, left to right.
/// </summary>
internal sealed class DirectDiagnosticRow : Grid
{
    private readonly TextBlock _value;

    internal string Label { get; }
    internal string Value { get; }
    internal string? Variable { get; }
    internal IReadOnlyList<string> Details { get; }
    internal TextBlock ValueText => _value;
    internal IEnumerable<TextBlock> DetailTexts => Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<TextBlock>());

    internal DirectDiagnosticRow(AdminDirectConnectionsRowView row, string id, bool tuning, bool first)
    {
        Label = row.Label; Value = row.Value; Variable = row.Variable; Details = row.Details;
        AutomationProperties.SetAutomationId(this, id);
        Padding = new(0, 8, 0, 8); ColumnSpacing = 24; RowSpacing = 4;
        BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6); BorderThickness = new(0, first ? 0 : 1, 0, 0);
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        // The label may wrap; for a tuning variable it is the plain name over the variable in monospace.
        var label = new StackPanel { Spacing = 0, MinWidth = 0 };
        if (row.Label.Length > 0)
        {
            var text = U.Text(row.Label, 14); text.FlowDirection = FlowDirection.LeftToRight; label.Children.Add(text);
        }
        if (row.Variable != null && tuning)
        {
            var variable = U.Text(row.Variable, 12, row.Label.Length > 0);
            variable.FontFamily = new FontFamily("Consolas"); variable.TextWrapping = TextWrapping.Wrap; variable.FlowDirection = FlowDirection.LeftToRight;
            label.Children.Add(variable);
        }
        Children.Add(label);
        if (tuning) SetRowSpan(label, 2);
        _value = U.Text(row.Value, 14); _value.HorizontalAlignment = HorizontalAlignment.Right; _value.TextAlignment = TextAlignment.Right; _value.FlowDirection = FlowDirection.LeftToRight;
        SetColumn(_value, 1); Children.Add(_value);
        var details = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Visibility = row.Details.Count > 0 ? Visibility.Visible : Visibility.Collapsed };
        foreach (var line in row.Details)
        {
            var text = U.Text(line, 12, true);
            text.TextAlignment = TextAlignment.Right; text.HorizontalAlignment = HorizontalAlignment.Right; text.FlowDirection = FlowDirection.LeftToRight;
            details.Children.Add(text);
        }
        SetRow(details, 1); SetColumn(details, tuning ? 1 : 0); SetColumnSpan(details, tuning ? 1 : 2);
        Children.Add(details);
        AutomationProperties.SetName(this, string.Join(" ", new[] { row.Label.Length > 0 ? row.Label : row.Variable, row.Value }.Concat(row.Details).Where(part => !string.IsNullOrEmpty(part))));
        // A value too long for its share of the row wraps instead of squeezing the label to nothing.
        SizeChanged += (_, _) =>
        {
            var cap = Math.Max(96, ActualWidth * .6);
            if (Math.Abs(_value.MaxWidth - cap) > 1 || double.IsInfinity(_value.MaxWidth)) _value.MaxWidth = cap;
        };
    }
}
