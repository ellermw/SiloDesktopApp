using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminDevicesPage
{
    private IReadOnlyList<JsonElement> _values = [];
    private JsonElement _device;
    private string _profile = "";
    private bool _showAll = true;
    private long _detailRevision;
    private SubtitleAppearanceDialog? _subtitleDialog;
    // From the press on "Edit subtitles" until the editor has closed, its last save has landed and the page has read again: the editor cannot be opened
    // twice, and the button says so instead of looking pressable.
    private bool _subtitleBusy;
    private readonly List<(Button Button, bool Enabled)> _subtitleButtons = [];
    private void SetSubtitleBusy(bool busy) { _subtitleBusy = busy; foreach (var (button, enabled) in _subtitleButtons) ShowSubtitleButton(button, enabled); }
    private void ShowSubtitleButton(Button button, bool enabled)
    {
        button.IsEnabled = enabled && !_subtitleBusy;
        ToolTipService.SetToolTip(button, _subtitleBusy ? AdminUserResourceText.T("admin.native.userResources.subtitleEditor.finishing") : null);
    }
    private StackPanel _tabsHost = Stack(0), _rowsHost = Stack(0);
    private const string UnknownProfile = "unknown-profile";

    private static FrameworkElement Padded(UIElement child, double horizontal = 20, double vertical = 16) => new Border { Child = child, Padding = new(horizontal, vertical, horizontal, vertical) };

    private async Task LoadDetailAsync()
    {
        var pageOwner = Session.Owner; var revision = ++_detailRevision; var user = _route.Segment(0); var device = _route.Segment(1);
        if (user == null || device == null) { ShowEmptyDetail(); return; }
        // The device on screen is no longer the one the route names: nothing may redraw it, or write to it, while the new one loads.
        _device = default; _detail.Children.Clear(); _detail.Children.Add(Padded(Text("Loading device…", 12, true)));
        var detail = await Session.ReadAsync("device", owner => _api.GetDeviceAsync(owner, user, device));
        if (!Session.IsCurrent(pageOwner) || revision != _detailRevision) return;
        if (detail.ValueKind != JsonValueKind.Object) { ShowDetailProblem(Session.Error != null ? "loadError" : "notFound"); return; }
        // Without the stored values every row would read as "app default" and edits would start from values the account may not have.
        var values = await Session.ReadAsync("values", owner => _api.GetDeviceValuesAsync(owner, user));
        if (!Session.IsCurrent(pageOwner) || revision != _detailRevision) return;
        if (values == null) { ShowDetailProblem("loadError"); return; }
        _device = detail; _values = values.Items;
        var tabs = Tabs(); if (tabs.Count > 0 && tabs.All(x => x.Id != _profile)) _profile = tabs[0].Id;
        RenderDetailBody();
    }
    private void ShowDetailProblem(string kind)
    {
        _detail.Children.Clear(); var panel = Stack(6); panel.HorizontalAlignment = HorizontalAlignment.Center; panel.Margin = new(24, 64, 24, 64);
        var title = Text(D("detail." + kind + "Title"), 14); title.HorizontalAlignment = HorizontalAlignment.Center; var body = Text(D("detail." + kind + "Body"), 12, true); body.TextAlignment = TextAlignment.Center; body.MaxWidth = 320;
        panel.Children.Add(title); panel.Children.Add(body); if (kind == "loadError") panel.Children.Add(Button(T("common.retry"), "DeviceDetailRetry", () => _ = LoadDetailAsync()));
        _detail.Children.Add(panel);
    }

    private sealed record ProfileTab(string Id, string Name, IReadOnlyList<JsonElement> Overrides);
    private IReadOnlyList<JsonElement> DeviceOverrides => _values.Where(x => x.Text("scope") == "profile_device" && x.Text("device_id") == _device.Text("device_id")).ToArray();
    /// <summary>Registered profiles first, so one that has used the device but overridden nothing still gets a tab, then any override profile the device list does not know.</summary>
    private IReadOnlyList<ProfileTab> Tabs()
    {
        var names = new List<(string Id, string Name)>(); var rows = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var profile in _device.Rows("profiles"))
        {
            var id = profile.Text("profile_id") is { Length: > 0 } profileId ? profileId : UnknownProfile;
            if (!rows.ContainsKey(id)) { names.Add((id, profile.Text("profile_name") is { Length: > 0 } name ? name : id)); rows[id] = []; }
        }
        foreach (var row in DeviceOverrides)
        {
            var id = row.Text("profile_id") is { Length: > 0 } profileId ? profileId : UnknownProfile;
            if (!rows.TryGetValue(id, out var list)) { names.Add((id, id)); rows[id] = list = []; }
            list.Add(row);
        }
        return names.Select(x => new ProfileTab(x.Id, x.Name, rows[x.Id])).ToArray();
    }

    private AdminDeviceAnomaly? DeviceAnomaly => _anomalies.TryGetValue(AdminDevicePresentation.DeviceKey(_device), out var anomaly) ? anomaly : null;

    private void RenderDetailBody()
    {
        _detail.Children.Clear();
        if (_device.ValueKind != JsonValueKind.Object) return;
        var tabs = Tabs(); var overrides = DeviceOverrides; var kind = AdminDevicePresentation.Platform(_device.Text("device_platform")); var anomaly = DeviceAnomaly;
        var header = new Grid { ColumnSpacing = 12, Margin = new(20, 16, 20, 12) }; header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = Stack(4); var titleRow = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        var tile = new Border { Width = 40, Height = 40, CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), VerticalAlignment = VerticalAlignment.Top, Child = new FontIcon { Glyph = PlatformGlyph(kind), FontSize = 18 } };
        titleRow.Children.Add(Text(AdminNameText.SingleLine(_device.Text("device_name") is { Length: > 0 } name ? name : D("unnamedDevice")), 20));
        if (anomaly != null) titleRow.Children.Add(Chip("⚠ " + D("detail.anomalyBadge"), "ErrorBrush"));
        identity.Children.Add(titleRow);
        var facts = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 }; facts.Children.Add(Mono(_device.Text("device_id"))); facts.Children.Add(Text(D("separator"), 12, true)); facts.Children.Add(Text(AdminDevicePresentation.PlatformLabel(_device.Text("device_platform")), 12, true));
        facts.Children.Add(Text(D("separator"), 12, true));
        var userId = _device.Text("user_id");
        if (AdminShellPage.CanOpen(AdminRouteRequest.UserDetail(userId))) facts.Children.Add(Link(_device.Text("username") + " ↗", "DeviceOpenUserName", () => AdminShellPage.Open(AdminRouteRequest.UserDetail(userId))));
        else facts.Children.Add(Text(_device.Text("username"), 12));
        facts.Children.Add(Text(D("separator"), 12, true)); facts.Children.Add(Text(D("detail.updated", ("time", RelativeUpdated(_device.Text("last_updated")))), 12, true));
        identity.Children.Add(facts);
        var identityRow = new Grid { ColumnSpacing = 12 }; identityRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); identityRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(identity, 1); identityRow.Children.Add(tile); identityRow.Children.Add(identity); header.Children.Add(identityRow);
        var actions = Inline(8); var forceAll = overrides.Count == 0; var effectiveAll = forceAll || _showAll;
        var totalSettings = AdminDevicePresentation.ForPlatform(_device.Text("device_platform"), overrides.Select(x => x.Text("key"))).Count;
        var scope = Segmented(D("settingsScope.allSettings"), [("all", D("settingsScope.allSettings") + "  " + totalSettings), ("overrides", D("settingsScope.overrides") + "  " + overrides.Count)], effectiveAll ? "all" : "overrides",
            value => { if (forceAll || tabs.Count == 0) { RenderDetailBody(); return; } _showAll = value == "all"; RenderDetailBody(); }, "DeviceSettingsScope");
        foreach (var button in scope.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>())
            if (tabs.Count == 0 || forceAll && (string?)button.Tag == "overrides")
            {
                // The official control says why a segment is off: no profile has used the device, or there is no override to show on its own.
                button.IsEnabled = false; ToolTipService.SetToolTip(button, D(tabs.Count == 0 ? "settingsScope.noProfile" : "settingsScope.noOverrides"));
            }
        actions.Children.Add(scope);
        var open = Button(D("detail.openUser"), "DeviceOpenUser", () => AdminShellPage.Open(AdminRouteRequest.UserDetail(userId))); open.IsEnabled = AdminShellPage.CanOpen(AdminRouteRequest.UserDetail(userId)); actions.Children.Add(open);
        Grid.SetColumn(actions, 1); header.Children.Add(actions); _detail.Children.Add(header);
        _detail.Children.Add(Divider()); _detail.Children.Add(RenderConnections()); _detail.Children.Add(Readout(tabs, overrides, anomaly, kind)); _detail.Children.Add(Divider());
        if (tabs.Count == 0)
        {
            var none = Stack(4); none.Margin = new(20); none.Children.Add(Text(D("detail.noProfilesTitle"), 14)); none.Children.Add(Text(D("detail.noProfilesBody"), 12, true)); _detail.Children.Add(none); return;
        }
        _tabsHost = Stack(0); _rowsHost = Stack(0);
        _detail.Children.Add(_tabsHost); _detail.Children.Add(_rowsHost); RenderTabsAndRows();
    }
    private static FrameworkElement Divider() => new Border { Height = 1, Background = Brush("BorderBrush"), Opacity = 0.6 };

    private FrameworkElement Readout(IReadOnlyList<ProfileTab> tabs, IReadOnlyList<JsonElement> overrides, AdminDeviceAnomaly? anomaly, string kind)
    {
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 12, Margin = new(20, 12, 20, 12) };
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        FrameworkElement Cell(string label, string value, string? sub, string? tone = null)
        {
            var cell = Stack(2); cell.Children.Add(Text(label, 10, true)); var v = Mono(value, 14, tone == null); if (tone != null) v.Foreground = Brush(tone); v.TextTrimming = TextTrimming.CharacterEllipsis; v.TextWrapping = TextWrapping.NoWrap; cell.Children.Add(v);
            if (sub != null) { var s = Text(sub, 11, true); s.TextTrimming = TextTrimming.CharacterEllipsis; s.TextWrapping = TextWrapping.NoWrap; ToolTipService.SetToolTip(s, sub); cell.Children.Add(s); }
            return cell;
        }
        var lastUpdate = overrides.Select(x => x.Text("updated_at")).Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).LastOrDefault();
        var names = AdminAccessGroupBody.JoinList(tabs.Take(2).Select(x => x.Name).ToArray());
        var cells = new[]
        {
            Cell(D("readout.overrides"), overrides.Count.ToString(CultureInfo.CurrentCulture), D("readout.acrossProfiles", ("count", tabs.Count), ("count_text", tabs.Count))),
            Cell(D("readout.profilesTouched"), names.Length > 0 ? names : "—", tabs.Count > 2 ? D("readout.moreProfiles", ("count", tabs.Count - 2)) : string.Join(" / ", tabs.Select(x => x.Overrides.Count.ToString(CultureInfo.CurrentCulture)))),
            Cell(D("readout.status"), anomaly != null ? D("readout.statusAnomaly") : D("readout.statusNormal"), anomaly?.Summary ?? D("readout.aligned", ("platform", AdminDevicePresentation.PlatformKindLabel(kind).ToLowerInvariant())), anomaly != null ? "ErrorBrush" : null),
            Cell(D("readout.lastUpdate"), RelativeUpdated(lastUpdate.Length > 0 ? lastUpdate : _device.Text("last_updated")), lastUpdate.Length > 0 ? Date(lastUpdate) : "—"),
        };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }

    // ---------- profile tabs + rows ----------
    private ProfileTab? ActiveTab => Tabs().FirstOrDefault(x => x.Id == _profile) ?? Tabs().FirstOrDefault();
    private void RenderTabsAndRows()
    {
        var tabs = Tabs(); var active = tabs.FirstOrDefault(x => x.Id == _profile) ?? tabs[0]; _profile = active.Id;
        _tabsHost.Children.Clear(); _rowsHost.Children.Clear(); _subtitleButtons.Clear();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(8, 8, 8, 0) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(strip, T("web.admin.common.deviceOverrides.tabs.label"));
        foreach (var tab in tabs)
        {
            var initial = AdminNameText.Initial(tab.Name); if (initial.Length == 0) initial = "?";
            var tabName = Truncated(tab.Name.Length > 0 ? tab.Name : T("web.admin.common.deviceOverrides.tabs.unnamed"), 13, auto: true); tabName.MaxWidth = 120;
            var content = Inline(8); content.Children.Add(TabChip(initial)); content.Children.Add(Inherited(tabName));
            content.Children.Add(TabChip(tab.Overrides.Count.ToString(CultureInfo.CurrentCulture)));
            var button = Id(new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = content, IsChecked = tab.Id == active.Id, Padding = new(10, 6, 10, 6), MinHeight = 36 }, "DeviceProfileTab_" + tab.Id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, AdminNameText.Label(tab.Name, 200) + ", " + tab.Overrides.Count);
            OfficialSelected(button);
            button.Checked += (_, _) => { if (_profile == tab.Id) return; _profile = tab.Id; ReplaceProfileQuery(); RenderTabsAndRows(); };
            button.Unchecked += (_, _) => { if (_profile == tab.Id) button.IsChecked = true; };
            strip.Children.Add(button);
        }
        _tabsHost.Children.Add(new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto });

        var overrides = active.Overrides.GroupBy(x => x.Text("key")).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var forceAll = DeviceOverrides.Count == 0; var showAll = forceAll || _showAll;
        var offered = AdminDevicePresentation.ForPlatform(_device.Text("device_platform")).Select(x => x.Key).ToList();
        var keys = overrides.Keys.ToList(); if (showAll) keys.AddRange(offered.Where(key => !overrides.ContainsKey(key)));
        keys = keys.OrderBy(AdminDevicePresentation.Order).ThenBy(key => key, StringComparer.Ordinal).ToList();
        var conflicts = AdminDevicePresentation.Conflicts(overrides.ToDictionary(x => x.Key, x => AdminDevicePresentation.ValueString(x.Value.Member("value")), StringComparer.Ordinal));
        var staleDays = DeviceAnomaly?.StaleDays; var suspect = 0; var rows = new List<FrameworkElement>();
        foreach (var key in keys)
        {
            overrides.TryGetValue(key, out var row); var isOverride = row.ValueKind == JsonValueKind.Object; var definition = AdminDevicePresentation.Definition(key);
            AdminInheritedValue? inherited = !isOverride && definition != null ? AdminDevicePresentation.Inherited(definition, active.Id, active.Name, _values) : null;
            var value = isOverride ? AdminDevicePresentation.ValueString(row.Member("value")) : inherited?.Raw ?? "";
            var anomaly = AdminDevicePresentation.RowAnomaly(key, value, isOverride, conflicts, staleDays); if (anomaly != null) suspect++;
            rows.Add(SettingRow(active, key, definition, isOverride, value, inherited, anomaly));
        }
        var meta = new Grid { ColumnSpacing = 12, Margin = new(16, 10, 16, 10) }; meta.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); meta.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var summary = Inline(8); summary.Children.Add(Mono(active.Id, 11));
        var counts = T("web.admin.common.deviceOverrides.summary.overrides", ("count", active.Overrides.Count), ("count_text", active.Overrides.Count));
        if (showAll && rows.Count > active.Overrides.Count) counts += " · " + T("web.admin.common.deviceOverrides.summary.inherited", ("count", rows.Count - active.Overrides.Count), ("count_text", rows.Count - active.Overrides.Count));
        summary.Children.Add(Text(counts, 11, true));
        if (suspect > 0) summary.Children.Add(Chip("⚠ " + T("web.admin.common.deviceOverrides.summary.suspect", ("count", suspect), ("count_text", suspect)), "ErrorBrush"));
        meta.Children.Add(summary);
        var resetAll = DetailButton(T("web.admin.common.deviceOverrides.summary.resetAll"), "DeviceResetProfile", active.Id, () => _ = ResetProfileAsync(active.Id, active.Name, active.Overrides));
        resetAll.IsEnabled = active.Overrides.Count > 0 && active.Id != UnknownProfile && _device.Text("device_id").Length <= 128 && !Session.NeedsReview; Grid.SetColumn(resetAll, 1); meta.Children.Add(resetAll);
        _tabsHost.Children.Add(Divider()); _tabsHost.Children.Add(meta); _tabsHost.Children.Add(Divider());
        if (rows.Count == 0) _rowsHost.Children.Add(Padded(Text(D("detail.noProfilesBody"), 12, true)));
        foreach (var row in rows) { _rowsHost.Children.Add(row); _rowsHost.Children.Add(Divider()); }
    }
    /// <summary>A small outlined label inside a profile tab; its text follows the tab's own colour, so it stays readable on the selected tab.</summary>
    private static Border TabChip(string text) => new() { Child = Inherited(Text(text, 12)), Padding = new(8, 2, 8, 2), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") };
    private void ReplaceProfileQuery()
    {
        var request = _route.WithQuery("profile", _profile == UnknownProfile ? null : _profile);
        if (AdminShellPage.TryReplace(this, request)) _route = request;
    }

    private FrameworkElement SettingRow(ProfileTab tab, string key, DeviceSettingDefinition? definition, bool isOverride, string value, AdminInheritedValue? inherited, AdminSettingAnomaly? anomaly)
    {
        var canWrite = tab.Id != UnknownProfile && _device.Text("device_id").Length <= 128 && !Session.NeedsReview; var structured = AdminDevicePresentation.IsStructured(definition);
        var grid = new Grid { ColumnSpacing = 24, Margin = new(16, 12, 16, 12) }; grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = Stack(2); var title = Inline(8); title.Children.Add(Text(definition?.Label ?? key, 14));
        if (isOverride) title.Children.Add(Chip(T("web.admin.common.deviceOverrides.override.setLabel")));
        else { var badge = Chip(inherited?.ProfileName != null ? T("web.admin.common.deviceOverrides.badge.profile") : T("web.admin.common.deviceOverrides.badge.default")); ToolTipService.SetToolTip(badge, T("web.admin.common.deviceOverrides.override.notSetTitle", ("source", inherited != null ? AdminDevicePresentation.InheritedSource(inherited) : T("web.admin.common.deviceOverrides.source.appDefault")))); title.Children.Add(badge); }
        if (anomaly != null) title.Children.Add(Chip("⚠ " + T("web.admin.common.deviceOverrides.badge." + anomaly.Kind), "ErrorBrush"));
        copy.Children.Add(title); copy.Children.Add(Mono(key, 11));
        if (definition?.Description is { Length: > 0 } description) copy.Children.Add(Text(description, 12, true));
        var valueText = AdminDevicePresentation.FormatValue(key, value);
        if (isOverride) { if (!structured && definition != null) valueText += " · " + T("web.admin.common.deviceOverrides.value.default", ("value", AdminDevicePresentation.FormatValue(key, AdminDevicePresentation.DefaultString(definition)))); }
        else if (inherited != null) valueText += " · " + AdminDevicePresentation.InheritedSource(inherited);
        copy.Children.Add(Text(valueText, 12, true));
        if (anomaly != null) { var reason = Text("⚠ " + anomaly.Reason, 12); reason.Foreground = Brush("ErrorBrush"); copy.Children.Add(reason); }
        grid.Children.Add(copy);
        var controls = Stack(6); controls.HorizontalAlignment = HorizontalAlignment.Right; controls.MinWidth = 220; Grid.SetColumn(controls, 1);
        controls.Children.Add(ControlFor(tab.Id, key, definition, value, canWrite));
        if (isOverride)
        {
            var reset = DetailButton(T("web.admin.common.deviceOverrides.reset"), "DeviceReset_" + key, tab.Id, () => _ = ResetOneAsync(tab.Id, key)); reset.IsEnabled = canWrite; reset.HorizontalAlignment = HorizontalAlignment.Right; controls.Children.Add(reset);
        }
        else { var hint = Text(T("web.admin.common.deviceOverrides.changeToOverride"), 11, true); hint.HorizontalAlignment = HorizontalAlignment.Right; hint.FontStyle = Windows.UI.Text.FontStyle.Italic; controls.Children.Add(hint); }
        grid.Children.Add(controls);
        return grid;
    }

    private FrameworkElement ControlFor(string profile, string key, DeviceSettingDefinition? definition, string value, bool enabled)
    {
        var owner = Session.Owner; var revision = _detailRevision; var label = definition?.Label ?? key;
        void Commit(string raw)
        {
            if (!TargetCurrent(owner, revision, profile) || Session.Busy || Session.NeedsReview) return;
            JsonElement typed;
            try { typed = AdminDevicePresentation.ValueFromString(key, raw); }
            catch (ArgumentException error) { Session.Report(error); RenderTabsAndRows(); return; }
            _ = SaveSettingAsync(profile, key, typed);
        }
        if (definition == null || AdminDevicePresentation.IsStructured(definition))
        {
            var edit = DetailButton(key == "playback.subtitle_appearance" ? T("web.admin.common.deviceOverrides.edit.customize") : T("web.admin.common.deviceOverrides.edit.json"), "DeviceEdit_" + key, profile,
                () => _ = key == "playback.subtitle_appearance" ? EditSubtitlesAsync(profile) : EditJsonAsync(profile, key, value));
            if (key == "playback.subtitle_appearance") { _subtitleButtons.Add((edit, enabled)); ShowSubtitleButton(edit, enabled); } else edit.IsEnabled = enabled;
            return edit;
        }
        var kind = AdminDevicePresentation.ControlKind(definition);
        if (kind == "switch")
        {
            var toggle = new ToggleSwitch { IsOn = value == "true", OnContent = "", OffContent = "", IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "DeviceSetting_" + key);
            toggle.Toggled += (_, _) => Commit(toggle.IsOn ? "true" : "false"); return toggle;
        }
        if (kind is "slider" or "stepper")
        {
            var fallback = double.TryParse(AdminDevicePresentation.DefaultString(definition), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            var current = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && value.Length > 0 && double.IsFinite(parsed) ? parsed : fallback;
            var box = new NumberBox { Minimum = definition.Minimum, Maximum = definition.Maximum, SmallChange = definition.Step, LargeChange = definition.Step * 10, Value = Math.Clamp(current, definition.Minimum, definition.Maximum),
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 160, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Right };
            // A fractional setting always shows its fraction, so the text the box reads back on focus loss is the value it holds.
            if (AdminDevicePresentation.StepDigits(definition) is > 0 and var digits) box.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter { IntegerDigits = 1, FractionDigits = digits };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "DeviceSetting_" + key);
            box.ValueChanged += (_, e) => { if (AdminDevicePresentation.NumberChange(definition, current, e.NewValue) is { } changed) Commit(changed); };
            return box;
        }
        if (kind == "select")
        {
            var options = AdminDevicePresentation.Options(definition, value); var select = new ComboBox { MinWidth = 200, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var option in options) select.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == value);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(select, label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(select, "DeviceSetting_" + key);
            select.SelectionChanged += (_, _) => { if (select.SelectedItem is ComboBoxItem { Tag: string chosen } && chosen != value) Commit(chosen); };
            return select;
        }
        var text = DetailButton("Edit", "DeviceEdit_" + key, profile, () => _ = EditValueAsync(profile, key, definition, value)); text.IsEnabled = enabled; return text;
    }

    // ---------- writes ----------
    private bool TargetCurrent(MonitorAuthorityLease? owner, long revision, string? profile) => Session.IsCurrent(owner) && revision == _detailRevision && (profile == null || profile == _profile && profile != UnknownProfile && _device.Text("device_id").Length <= 128);
    private Button DetailButton(string label, string id, string? profile, Action action)
    {
        var owner = Session.Owner; var revision = _detailRevision; var button = Link(label, id, () => { if (TargetCurrent(owner, revision, profile) && !Session.Busy && !Session.NeedsReview) action(); });
        return button;
    }
    /// <summary>The write, then the saved state: controls always follow what the server holds, so a refused, conflicting or unknown change leaves no stale control behind.</summary>
    private async Task SaveSettingAsync(string profile, string key, JsonElement value)
    {
        var owner = Session.Owner; var revision = _detailRevision; if (profile != _profile || profile == UnknownProfile) return;
        var user = _device.Text("user_id"); var device = _device.Text("device_id");
        var ok = await Session.WriteAsync(lease => _api.SetDeviceSettingAsync(lease, user, profile, device, key, value));
        if (!Session.IsCurrent(owner)) return;
        if (ok) await RefreshAfterWriteAsync(owner); else if (revision == _detailRevision) RenderTabsAndRows();
    }
    private async Task ResetOneAsync(string profile, string key)
    {
        var owner = Session.Owner; var revision = _detailRevision; var user = _device.Text("user_id"); var device = _device.Text("device_id");
        if (!await _host.ConfirmAsync(D("resetSetting.title"), D("resetSetting.description"), D("resetSetting.confirm"), destructive: true, stillCurrent: () => TargetCurrent(owner, revision, profile)) || !TargetCurrent(owner, revision, profile)) return;
        var gone = false;
        var ok = await Session.WriteAsync(async lease =>
        {
            try { await _api.DeleteDeviceSettingAsync(lease, user, profile, device, key); }
            catch (ApiException error) when (error.StatusCode == 404) { gone = true; throw; }
        });
        if (!Session.IsCurrent(owner)) return;
        // A 404 says the override is already gone on the server: read again so its row does not stay on screen as one.
        if (ok || (gone && revision == _detailRevision)) await RefreshAfterWriteAsync(owner); else if (revision == _detailRevision) RenderTabsAndRows();
    }
    private async Task ResetProfileAsync(string profile, string profileName, IReadOnlyList<JsonElement> overrides)
    {
        var owner = Session.Owner; var revision = _detailRevision; var user = _device.Text("user_id"); var device = _device.Text("device_id");
        if (!await _host.ConfirmAsync(D("resetProfile.title", ("name", Safe(profileName))), D("resetProfile.description"), D("resetProfile.confirm"), destructive: true, stillCurrent: () => TargetCurrent(owner, revision, profile)) || !TargetCurrent(owner, revision, profile)) return;
        var keys = overrides.Select(x => x.Text("key")).ToArray(); var completed = 0; var unknown = false;
        var ok = await Session.WriteAsync(async lease =>
        {
            var result = await _api.ResetDeviceProfileAsync(lease, user, profile, device, keys);
            completed = result.Completed;
            if (result.OutcomeUnknown) { unknown = true; throw new AdminResourceOutcomeUnknownException(); }
            if (result.FailureStatus is { } status) throw new ApiException("reset_stopped", "The reset stopped.", status);
        });
        if (!Session.IsCurrent(owner)) return;
        if (ok) { await RefreshAfterWriteAsync(owner); return; }
        if (revision != _detailRevision) return;
        var stopped = T("web.admin.common.users.resetAllStopped", ("count", keys.Length), ("count_text", keys.Length), ("completed", completed), ("reason", Session.Error ?? ""));
        void ShowStopped() { if (_device.ValueKind == JsonValueKind.Object && Tabs().Count > 0) _rowsHost.Children.Insert(0, Padded(Text(stopped, 12), 16, 8)); }
        // The overrides deleted before a definite refusal are gone on the server: read again so their rows do not stay on screen, and keep the notice.
        if (completed > 0 && !unknown) { await RefreshAfterWriteAsync(owner, ShowStopped); return; }
        RenderTabsAndRows(); ShowStopped();
    }
    /// <summary>After a confirmed write the device, the account's values and the fleet are read again, so every pane shows what the server now holds.</summary>
    private async Task RefreshAfterWriteAsync(MonitorAuthorityLease? owner, Action? rendered = null)
    {
        var user = _device.Text("user_id"); var device = _device.Text("device_id"); var revision = ++_detailRevision;
        var detail = await Session.ReadAsync("device", lease => _api.GetDeviceAsync(lease, user, device));
        if (!Session.IsCurrent(owner) || revision != _detailRevision) return;
        var values = await Session.ReadAsync("values", lease => _api.GetDeviceValuesAsync(lease, user));
        if (!Session.IsCurrent(owner) || revision != _detailRevision) return;
        if (detail.ValueKind != JsonValueKind.Object || values == null) { RenderTabsAndRowsIfShown(); rendered?.Invoke(); return; }
        _device = detail; _values = values.Items; RenderDetailBody(); rendered?.Invoke();
        var fleet = await Session.ReadAsync("fleet", lease => _api.ListAllDevicesAsync(lease));
        if (!Session.IsCurrent(owner) || fleet == null) return;
        _fleet = fleet; _anomalies = AdminDevicePresentation.Anomalies(fleet, DateTimeOffset.UtcNow); RenderPulse(); RenderFilters(); RenderList();
        if (_device.ValueKind == JsonValueKind.Object && _device.Text("user_id") == user && _device.Text("device_id") == device) { RenderDetailBody(); if (revision == _detailRevision) rendered?.Invoke(); }
    }
    private void RenderTabsAndRowsIfShown() { if (_device.ValueKind == JsonValueKind.Object && Tabs().Count > 0) RenderTabsAndRows(); }

    private async Task EditValueAsync(string profile, string key, DeviceSettingDefinition definition, string value)
    {
        var owner = Session.Owner; var revision = _detailRevision;
        var input = Input(definition.Label, "DeviceSettingValue", value); var message = Text("", 12); message.Foreground = Brush("ErrorBrush"); var content = Stack(8); content.Children.Add(input); content.Children.Add(message);
        JsonElement typed = default;
        bool Valid()
        {
            try { typed = AdminDevicePresentation.ValueFromString(key, input.Text); message.Text = ""; return true; }
            catch (ArgumentException error) { message.Text = error.Message; return false; }
        }
        if (await _host.DialogAsync(Safe(definition.Label), content, T("common.save"), Valid, stillCurrent: () => TargetCurrent(owner, revision, profile)) && TargetCurrent(owner, revision, profile)) await SaveSettingAsync(profile, key, typed);
    }
    private async Task EditJsonAsync(string profile, string key, string value)
    {
        var owner = Session.Owner; var revision = _detailRevision;
        var input = Input(D("jsonEditor.help"), "DeviceJsonValue", value); input.AcceptsReturn = true; input.MinHeight = 260; input.MinWidth = 360; input.TextWrapping = TextWrapping.Wrap;
        var message = Text("", 12); message.Foreground = Brush("ErrorBrush"); var content = Stack(8); content.Children.Add(input); content.Children.Add(message);
        JsonElement typed = default;
        bool Valid()
        {
            if (input.Text.Length > 64 * 1024) { message.Text = "The value is too large."; return false; }
            try { typed = AdminDevicePresentation.ValueFromString(key, input.Text); message.Text = ""; return true; }
            catch (ArgumentException error) { message.Text = error.Message; return false; }
        }
        if (await _host.DialogAsync(Safe(key), content, D("jsonEditor.save"), Valid, stillCurrent: () => TargetCurrent(owner, revision, profile)) && TargetCurrent(owner, revision, profile)) await SaveSettingAsync(profile, key, typed);
    }
    private async Task EditSubtitlesAsync(string profile)
    {
        if (_subtitleBusy) return;
        var owner = Session.Owner; var revision = _detailRevision; var user = _device.Text("user_id"); var device = _device.Text("device_id");
        SetSubtitleBusy(true);
        SubtitleAppearanceDialog? opened = null; Action? retire = null;
        try
        {
            // The editor opens through the host's slot like every dialog of the page, so it never overlaps another and a press made while another is closing
            // waits for it. The slot, and this flag, are held until the editor has closed, a save already on its way has landed and the page has read again:
            // a second editor opened before that would start from a value the server no longer holds and be retired by the read that follows.
            await _host.CustomDialogAsync(() =>
            {
                var current = DeviceOverrides.FirstOrDefault(x => x.Text("profile_id") == profile && x.Text("key") == "playback.subtitle_appearance");
                var initial = current.ValueKind == JsonValueKind.Object && current.Member("value") is { } stored ? stored.GetRawText() : AdminDevicePresentation.Definition("playback.subtitle_appearance")?.Data.Member("default_value")?.GetRawText() ?? "{}";
                // The official eyebrow names the scope of the override: profile (its name, else its id, else "default"), then device.
                var profileLabel = Safe(Tabs().FirstOrDefault(x => x.Id == profile)?.Name) is { Length: > 0 } tabName ? tabName : T("web.admin.common.subtitleAppearanceDialog.defaultProfile");
                var deviceLabel = Safe(_device.Text("device_name")) is { Length: > 0 } deviceName ? deviceName : T("web.admin.common.subtitleAppearanceDialog.unnamedDevice");
                var dialog = new SubtitleAppearanceDialog { XamlRoot = XamlRoot, InitialValue = initial, ApplyToLocalPlayer = false, CanReset = current.ValueKind == JsonValueKind.Object,
                    Eyebrow = profileLabel + " · " + deviceLabel, ResetLabel = T("web.admin.common.subtitleAppearanceDialog.resetOverride") };
                dialog.SaveDraftAsync = raw => SaveSubtitleDraftAsync(owner, revision, profile, user, device, raw);
                dialog.ResetOverrideAsync = async () =>
                {
                    if (!TargetCurrent(owner, revision, profile)) throw new MonitorAuthorityRetiredException();
                    if (!await Session.WriteAsync(lease => _api.DeleteDeviceSettingAsync(lease, user, profile, device, "playback.subtitle_appearance")))
                    {
                        // A write that may have reached the server (the page now needs a reload) locks the editor; a refusal leaves it open to retry.
                        if (Session.NeedsReview) throw new AdminResourceOutcomeUnknownException();
                        throw new InvalidOperationException(Session.Error ?? AdminUserResourceText.T("admin.native.userResources.subtitleEditor.resetFailed"));
                    }
                };
                retire = () => DispatcherQueue.TryEnqueue(() => dialog.Hide());
                Session.Retired += retire; _subtitleDialog = opened = dialog;
                return dialog;
            },
            async dialog =>
            {
                // A save already on its way finishes first, so the page reads what the server holds after the last write, not before it.
                await ((SubtitleAppearanceDialog)dialog).WhenSavesSettledAsync();
                if (Session.IsCurrent(owner)) await RefreshAfterWriteAsync(owner);
            },
            stillCurrent: () => TargetCurrent(owner, revision, profile));
        }
        finally
        {
            if (retire != null) Session.Retired -= retire;
            if (opened != null) opened.InitialValue = null;
            if (ReferenceEquals(_subtitleDialog, opened)) _subtitleDialog = null;
            SetSubtitleBusy(false);
        }
    }
    /// <summary>
    /// One autosaved write of the whole appearance value (official #4543). A refusal (a 4xx other than a timeout) is shown with the server's safe
    /// detail and the edit is dropped from the editor; a timeout, 5xx or lost answer after sending is an unknown outcome: the page locks until it is
    /// reloaded, the edit is dropped from the editor, and nothing is ever sent again for it.
    /// </summary>
    private async Task<DraftSaveResult> SaveSubtitleDraftAsync(MonitorAuthorityLease? owner, long revision, string profile, string user, string device, string raw)
    {
        if (!TargetCurrent(owner, revision, profile)) return DraftSaveResult.Retired;
        var typed = JsonDocument.Parse(raw).RootElement.Clone();
        var written = await Session.TryWriteAsync(lease => _api.SetDeviceSettingAsync(lease, user, profile, device, "playback.subtitle_appearance", typed));
        // A write that never left (another one was running, or the page needs a review already) is a refusal that says so, never an unknown outcome.
        return AdminSubtitleDraft.ResultOf(written, Session.IsCurrent(owner), Session.NeedsReview, Session.Error);
    }

    // ---------- connections ----------
    private FrameworkElement RenderConnections()
    {
        var panel = Stack(8); panel.Margin = new(20, 8, 20, 8);
        var connections = _device.Rows("connections");
        if (connections.Count == 0) return panel;
        panel.Children.Add(Text(C("title"), 14));
        foreach (var connection in connections.Take(100))
        {
            var name = connection.Text("connection");
            var title = name == "local" ? C("local") : name == "remote" ? C("remote") : name.StartsWith("provider:", StringComparison.Ordinal) ? C("provider", ("provider", name[9..])) : "Other connection";
            var section = Stack(4); section.Children.Add(Text(title, 13));
            if (connection.Member("probe") is { ValueKind: JsonValueKind.Object } probe)
            {
                var transport = probe.Text("transport") switch { "wifi" => "wifi", "ethernet" => "ethernet", "cellular" => "cellular", _ => "otherNetwork" };
                section.Children.Add(Text(C("probe", ("lower_bound", probe.Flag("lower_bound") ? "yes" : "no"), ("rate", Rate(probe.Number("kbps"))), ("transport", C("transport." + transport)), ("measured", Relative(probe.Text("measured_at")))), 12, true));
            }
            if (connection.Member("playback_sample") is { ValueKind: JsonValueKind.Object } sample)
                section.Children.Add(Text(C("playbackSample", ("rate", Rate(sample.Number("kbps"))), ("seen", Relative(sample.Text("measured_at")))), 12, true));
            if (connection.Member("learned_limit") is { ValueKind: JsonValueKind.Object } limit)
            {
                section.Children.Add(Text(C("learnedLimit", ("rate", Rate(limit.Number("kbps"))), ("until", Date(limit.Text("expires_at")))), 12));
                var user = _device.Text("user_id"); var device = _device.Text("device_id"); var id = connection.Text("id");
                section.Children.Add(DetailButton(C("clearLimit"), "DeviceResetLimit_" + id, null, () => _ = ResetLimitAsync(user, device, id)));
            }
            if (connection.Member("plan_summary") is { ValueKind: JsonValueKind.Object } plan && plan.Member("diagnostics") is { ValueKind: JsonValueKind.Object } diagnostics)
            {
                int.TryParse(diagnostics.Text("stall_count"), out var stalls);
                double.TryParse(diagnostics.Text("stall_total_ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds);
                var seconds = double.IsFinite(milliseconds) ? Math.Max(0, Math.Round(milliseconds / 1000)) : 0;
                var action = diagnostics.Text("buffering_action") switch { "ignored" => "ignored", "lowered" => "lowered", "restored" => "restored", "accepted" => "accepted", "kept" => "kept", "timed_out" => "timedOut", "cant_lower" => "cantLower", _ => "none" };
                var summary = C("stalls", ("count", stalls), ("count_text", stalls)) + " · " + C("bufferingSeconds", ("count", seconds), ("count_text", seconds)) + " · " + C("bufferingAction." + action) + " · " + Relative(plan.Text("reported_at"));
                section.Children.Add(Text(C("latestPlayback", ("summary", summary)), 12, true));
            }
            panel.Children.Add(new Border { Child = section, Padding = new(12, 8, 12, 8), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") });
        }
        if (connections.Count > 100) panel.Children.Add(Text("Showing the first 100 connection observations.", 12, true));
        return panel;
    }
    private static string C(string key, params (string Name, object? Value)[] args) => T("web.admin.common.deviceConnections." + key, args);
    private static string Rate(int kbps) => AdminDevicePresentation.FormatBitrate(Math.Max(0, kbps));
    private async Task ResetLimitAsync(string user, string device, string connection)
    {
        var owner = Session.Owner;
        var ok = await Session.WriteAsync(lease => _api.ResetLearnedLimitAsync(lease, user, device, connection));
        if (Session.IsCurrent(owner) && ok) await RefreshAfterWriteAsync(owner);
    }
}
