using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private long _overrideRevision;
    private const string SubtitleAppearanceKey = "playback.subtitle_appearance";
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
        _device = default; _rowsScroll = null; _detail.Children.Clear(); ShowDetailSkeleton();
        var detail = await Session.ReadAsync("device", owner => _api.GetDeviceAsync(owner, user, device));
        if (!Session.IsCurrent(pageOwner) || revision != _detailRevision) return;
        if (detail.ValueKind != JsonValueKind.Object) { ShowDetailProblem(Session.Error != null ? "loadError" : "notFound"); return; }
        // Without the stored values every row would read as "app default" and edits would start from values the account may not have.
        var values = await ReadDeviceValuesAsync(pageOwner, user, revision);
        if (!Session.IsCurrent(pageOwner) || revision != _detailRevision) return;
        if (values == null) { ShowDetailProblem("loadError"); return; }
        _device = detail; _values = values.Items;
        var tabs = Tabs(); if (tabs.Count > 0 && tabs.All(x => x.Id != _profile)) _profile = tabs[0].Id;
        RenderDetailBody();
    }

    private async Task<AdminDeviceValues?> ReadDeviceValuesAsync(MonitorAuthorityLease? owner, string user, long detailRevision)
    {
        while (Session.IsCurrent(owner) && detailRevision == _detailRevision)
        {
            var beforeWrite = _overrideRevision;
            var values = await Session.ReadAsync("values", lease => _api.GetDeviceValuesAsync(lease, user));
            // A confirmed write made after this request started already changed the shown overrides.
            if (beforeWrite == _overrideRevision) return values;
        }
        return null;
    }

    private void ApplyOverrideChange(MonitorAuthorityLease? owner, string user, Func<IReadOnlyList<JsonElement>, IReadOnlyList<JsonElement>> change)
    {
        if (!Session.IsCurrent(owner)) return;
        _overrideRevision++;
        if (_device.Text("user_id") != user) return;
        _values = change(_values);
        RenderDetailBody();
    }
    /// <summary>The official loading state: three placeholder blocks (16, 32 and 32 tall) in a padded column. They do not pulse: a page swaps in place.</summary>
    private void ShowDetailSkeleton()
    {
        var column = Stack(16); column.Margin = new(20);
        foreach (var height in new[] { 64d, 128d, 128d }) column.Children.Add(new Border { Height = height, CornerRadius = new(6), Background = LogsNativeUi.Tint("MutedBrush", .6), Opacity = .7 });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(column, "DeviceDetailSkeleton");
        _detail.Children.Add(column);
    }
    private void ShowDetailProblem(string kind)
    {
        // Official: a column at least 320 tall, its title (14, medium) and body (12, up to 320 wide) centred.
        _detail.Children.Clear(); var panel = Stack(4); panel.HorizontalAlignment = HorizontalAlignment.Center; panel.VerticalAlignment = VerticalAlignment.Center; panel.Margin = new(24, 0, 24, 0);
        var title = Text(D("detail." + kind + "Title"), 14); title.FontWeight = Microsoft.UI.Text.FontWeights.Medium; title.HorizontalAlignment = HorizontalAlignment.Center; var body = Text(D("detail." + kind + "Body"), 12, true); body.TextAlignment = TextAlignment.Center; body.MaxWidth = 320;
        panel.Children.Add(title); panel.Children.Add(body); if (kind == "loadError") panel.Children.Add(Button(T("common.retry"), "DeviceDetailRetry", () => _ = LoadDetailAsync()));
        _detail.Children.Add(new Grid { MinHeight = 320, Children = { panel } });
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
        RememberRowsView();
        // The bar and the list stay in the tree across a re-read, so the list keeps its place without any scrolling being replayed; only what is above them is rebuilt.
        var reuse = _rowsScroll != null && ReferenceEquals(_rowsScroll.Parent, _detail) && _device.ValueKind == JsonValueKind.Object && Tabs().Count > 0 && _detail.Children.Count >= 2;
        if (reuse) { while (_detail.Children.Count > 2) _detail.Children.RemoveAt(0); } else { _detail.Children.Clear(); _rowsScroll = null; }
        var slot = 0; void Put(UIElement element) => _detail.Children.Insert(slot++, element);
        if (_device.ValueKind != JsonValueKind.Object) return;
        var tabs = Tabs(); var overrides = DeviceOverrides; var kind = AdminDevicePresentation.Platform(_device.Text("device_platform")); var anomaly = DeviceAnomaly;
        // Official: the header wraps, so the actions move under the device when both do not fit on one line.
        var header = new Grid { ColumnSpacing = 16, RowSpacing = 0, Margin = new(24, 16, 24, 16) }; header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.RowDefinitions.Add(new() { Height = GridLength.Auto }); header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var identity = Stack(4); var titleRow = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        var tileIcon = new FontIcon { Glyph = PlatformGlyph(kind), FontSize = 18 };
        var tile = new Border { Width = 44, Height = 44, CornerRadius = new(6), BorderThickness = new(1), BorderBrush = LogsNativeUi.Tint("BorderBrush", .7), Background = LogsNativeUi.Tint("SurfaceRaisedBrush", .5), VerticalAlignment = VerticalAlignment.Top, Child = tileIcon };
        var title = Text(AdminNameText.SingleLine(_device.Text("device_name") is { Length: > 0 } name ? name : D("unnamedDevice")), 20); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; title.LineHeight = 25;
        titleRow.Children.Add(title);
        if (anomaly != null)
        {
            var label = Caps(D("detail.anomalyBadge"), 10.5, 40, Microsoft.UI.Text.FontWeights.Normal, lineHeight: 16);
            var pill = new Border { Child = DeviceSettingWarning(label, 12), Padding = new(8, 2, 8, 2), BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .3), Background = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .1), VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(SiloPlayer.Helpers.RoundedBadge.Apply(pill, 10));
        }
        identity.Children.Add(titleRow);
        FrameworkElement Separator() { var dot = Text(D("separator"), 12, true); dot.Opacity = .6; return dot; }
        var facts = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 }; var deviceId = Mono(_device.Text("device_id"), 12, false); deviceId.Opacity = .8; facts.Children.Add(deviceId); facts.Children.Add(Separator()); facts.Children.Add(Text(AdminDevicePresentation.PlatformLabel(_device.Text("device_platform")), 12, true));
        facts.Children.Add(Separator());
        var userId = _device.Text("user_id");
        if (AdminShellPage.CanOpen(AdminRouteRequest.UserDetail(userId)))
        {
            var owner = Link(_device.Text("username"), "DeviceOpenUserName", () => AdminShellPage.Open(AdminRouteRequest.UserDetail(userId)));
            var ownerContent = Inline(2); var ownerName = Text(_device.Text("username"), 12); ownerName.FontWeight = Microsoft.UI.Text.FontWeights.Medium; ownerName.Opacity = .85;
            ownerContent.Children.Add(ownerName); ownerContent.Children.Add(AdminMonitorUi.Icon("arrow-up-right", 12, Brush("PrimaryTextBrush")));
            owner.Content = ownerContent; owner.Padding = new(0); owner.MinHeight = 0; facts.Children.Add(owner);
        }
        else facts.Children.Add(Text(_device.Text("username"), 12));
        facts.Children.Add(Separator()); facts.Children.Add(Text(D("detail.updated", ("time", RelativeUpdated(_device.Text("last_updated")))), 12, true));
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
        var open = Button(D("detail.openUser"), "DeviceOpenUser", () => AdminShellPage.Open(AdminRouteRequest.UserDetail(userId))); open.IsEnabled = AdminShellPage.CanOpen(AdminRouteRequest.UserDetail(userId));
        var openContent = Inline(6); openContent.Children.Add(Text(D("detail.openUser"), 14)); openContent.Children.Add(AdminMonitorUi.Icon("arrow-up-right", 12, Brush("PrimaryTextBrush"))); open.Content = openContent; actions.Children.Add(open);
        Grid.SetColumn(actions, 1); header.Children.Add(actions); Put(header);
        header.SizeChanged += (_, args) =>
        {
            // flex-wrap: the actions go under the device when the two, side by side at their natural widths, do not fit.
            identityRow.Measure(new(double.PositiveInfinity, double.PositiveInfinity)); actions.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var stacked = identityRow.DesiredSize.Width + actions.DesiredSize.Width + header.ColumnSpacing > args.NewSize.Width - header.Margin.Left - header.Margin.Right + .5; identityRow.InvalidateMeasure(); actions.InvalidateMeasure();
            header.RowSpacing = stacked ? 16 : 0; Grid.SetRow(actions, stacked ? 1 : 0); Grid.SetColumn(actions, stacked ? 0 : 1); Grid.SetColumnSpan(actions, stacked ? 2 : 1); Grid.SetColumnSpan(identityRow, stacked ? 2 : 1);
            actions.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };
        Put(Divider()); Put(RenderConnections()); Put(Readout(tabs, overrides, anomaly, kind)); Put(Divider());
        if (tabs.Count == 0)
        {
            // Official: a dashed outlined box with the title and body centred.
            var none = Stack(4); none.HorizontalAlignment = HorizontalAlignment.Center; none.Margin = new(24, 48, 24, 48);
            var noneTitle = Text(D("detail.noProfilesTitle"), 14); noneTitle.FontWeight = Microsoft.UI.Text.FontWeights.Medium; noneTitle.HorizontalAlignment = HorizontalAlignment.Center;
            var noneBody = Text(D("detail.noProfilesBody"), 12, true); noneBody.TextAlignment = TextAlignment.Center; noneBody.MaxWidth = 384; noneBody.LineHeight = 19.5;
            none.Children.Add(noneTitle); none.Children.Add(noneBody);
            var box = new Border { Child = none, BorderThickness = new(1), BorderBrush = LogsNativeUi.Tint("BorderBrush", .6), CornerRadius = new(6), Margin = new(24, 20, 24, 20) };
            _detail.Children.Add(box); RestoreRowsView(); return;
        }
        if (!reuse)
        {
            _tabsHost = Stack(0); _rowsHost = Stack(0);
            // The profile bar is pinned above the list; the list scrolls on its own (official: the tabs own the scroll region below the strip and the bar).
            _rowsScroll = new ScrollViewer { Content = _rowsHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Auto };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_rowsScroll, "DeviceRowsScroll");
            _detail.Children.Add(_tabsHost); _detail.Children.Add(_rowsScroll);
        }
        ApplyRowsBound(); RenderTabsAndRows(keepView: false); RestoreRowsView();
    }
    private static FrameworkElement Divider(double inset = 0, double opacity = .6) => new Border { Height = 1, Background = Brush("BorderBrush"), Opacity = opacity, Margin = new(inset, 0, inset, 0) };

    private ScrollViewer? _rowsScroll;
    private string? _focusedControl;
    /// <summary>The list is rebuilt after every confirmed write: the control being used keeps the keyboard focus across it, as a slider dragged or stepped far down the list must.</summary>
    private void RememberRowsView()
    {
        // While a write is on its way the page is disabled and the control loses the focus, so the one that sent it is remembered from the press.
        _focusedControl = FocusedDetailControl(out _focusedState);
        if (_focusedControl == null) { _focusedControl = _pendingFocus; _focusedState = _pendingState; }
        _pendingFocus = null;
    }
    private string? _pendingFocus;
    private FocusState _pendingState, _focusedState;
    /// <summary>The control in the detail that has the focus, and how it got it (a key press shows the focus ring, a click does not).</summary>
    private string? FocusedDetailControl(out FocusState state)
    {
        state = FocusState.Keyboard;
        if (XamlRoot == null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused || !IsInside(focused, _detail)) return null;
        if (focused is Control { FocusState: not FocusState.Unfocused } control) state = control.FocusState;
        return Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused) is { Length: > 0 } id ? id : null;
    }
    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var node = element; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node)) if (ReferenceEquals(node, container)) return true;
        return false;
    }
    private void RestoreRowsView()
    {
        if (_focusedControl != null) _detail.UpdateLayout();
        var id = _focusedControl; var state = _focusedState; _focusedControl = null; var seq = ++_renderSeq;
        if (id == null) return;
        bool TryFocus() => FindById(_detail, id) is Control control && control.Focus(state);
        // A control built a moment ago may not take focus until it has loaded: try once more when the page is idle, unless the list was rebuilt again meanwhile.
        if (!TryFocus()) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (seq == _renderSeq) TryFocus(); });
    }
    private int _renderSeq;
    private static DependencyObject? FindById(DependencyObject root, string id)
    {
        if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(root) == id) return root;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindById(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }
    /// <summary>
    /// The list may grow until the whole detail fills the page; past that it scrolls. (The official panel is as tall as its grid row, which a long list
    /// stretches, so the WebUI's own scroll region only shows once the page is bounded; the desktop page always is.)
    /// </summary>
    private void ApplyRowsBound() { if (_rowsScroll != null) _rowsScroll.MaxHeight = Math.Max(480, (ActualHeight > 0 ? ActualHeight : 900) - 200); }

    private FrameworkElement Readout(IReadOnlyList<ProfileTab> tabs, IReadOnlyList<JsonElement> overrides, AdminDeviceAnomaly? anomaly, string kind)
    {
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 12, Margin = new(24, 16, 24, 16) };
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        FrameworkElement Cell(string label, string value, string? sub, string? tone = null, bool wrapSub = false, string? subTooltip = null, bool primary = false)
        {
            // Official ReadoutCell: a tracked uppercase label, a mono value (full text colour for the count, 90% otherwise, red for an anomaly), a muted line under it.
            var cell = Stack(0); cell.MinWidth = 0; cell.Children.Add(Caps(label, 10, 120, Microsoft.UI.Text.FontWeights.SemiBold, lineHeight: 15));
            var v = Mono(value, 14, false); v.Margin = new(0, 4, 0, 0); v.LineHeight = 21; v.Opacity = primary || tone != null ? 1 : .9; if (tone != null) v.Foreground = Brush(tone);
            v.TextTrimming = TextTrimming.CharacterEllipsis; v.TextWrapping = TextWrapping.NoWrap; ToolTipService.SetToolTip(v, value); cell.Children.Add(v);
            if (sub != null)
            {
                var text = Text(sub, 11, true); text.Margin = new(0, 2, 0, 0); text.LineHeight = 16.5;
                if (!wrapSub)
                {
                    text.TextTrimming = TextTrimming.CharacterEllipsis;
                    text.TextWrapping = TextWrapping.NoWrap;
                }
                ToolTipService.SetToolTip(text, subTooltip ?? sub);
                cell.Children.Add(text);
            }
            return cell;
        }
        var lastUpdate = overrides.Select(x => x.Text("updated_at")).Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).LastOrDefault() ?? "";
        var names = AdminAccessGroupBody.JoinList(tabs.Take(2).Select(x => x.Name).ToArray());
        var cells = new[]
        {
            Cell(D("readout.overrides"), overrides.Count.ToString(CultureInfo.CurrentCulture), D("readout.acrossProfiles", ("count", tabs.Count), ("count_text", tabs.Count)), primary: true),
            Cell(D("readout.profilesTouched"), names.Length > 0 ? names : "—", tabs.Count > 2 ? D("readout.moreProfiles", ("count", tabs.Count - 2)) : string.Join(" / ", tabs.Select(x => x.Overrides.Count.ToString(CultureInfo.CurrentCulture)))),
            Cell(D("readout.status"), anomaly != null ? D("readout.statusAnomaly") : D("readout.statusNormal"),
                anomaly?.Summary ?? D("readout.aligned", ("platform", AdminDevicePresentation.PlatformKindLabel(kind).ToLowerInvariant())),
                anomaly != null ? "ErrorBrush" : null, wrapSub: true, subTooltip: anomaly?.Detail),
            Cell(D("readout.lastUpdate"), RelativeUpdated(lastUpdate.Length > 0 ? lastUpdate : _device.Text("last_updated")), lastUpdate.Length > 0 ? Date(lastUpdate) : "—"),
        };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        // Official: two columns below the sm breakpoint, four from it.
        grid.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 440;
            grid.RowDefinitions.Clear(); if (narrow) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], narrow ? i % 2 : i); Grid.SetRow(cells[i], narrow ? i / 2 : 0); }
            while (grid.ColumnDefinitions.Count > (narrow ? 2 : 4)) grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
            while (grid.ColumnDefinitions.Count < (narrow ? 2 : 4)) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        };
        return grid;
    }

    // ---------- profile tabs + rows ----------
    private ProfileTab? ActiveTab => Tabs().FirstOrDefault(x => x.Id == _profile) ?? Tabs().FirstOrDefault();
    private void RenderTabsAndRows(bool keepView = true)
    {
        if (keepView) RememberRowsView();
        var tabs = Tabs(); var active = tabs.FirstOrDefault(x => x.Id == _profile) ?? tabs[0]; _profile = active.Id;
        _tabsHost.Children.Clear(); _rowsHost.Children.Clear(); _subtitleButtons.Clear();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(8, 8, 8, 0) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(strip, T("web.admin.common.deviceOverrides.tabs.label"));
        foreach (var tab in tabs)
        {
            var initial = AdminNameText.Initial(tab.Name); if (initial.Length == 0) initial = "?";
            var isActive = tab.Id == active.Id;
            var tabName = Truncated(tab.Name.Length > 0 ? tab.Name : T("web.admin.common.deviceOverrides.tabs.unnamed"), 12.5, auto: true); tabName.MaxWidth = 120; tabName.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
            var content = Inline(8); content.Children.Add(TabInitial(initial, tab.Id)); content.Children.Add(Inherited(tabName));
            content.Children.Add(TabCount(tab.Overrides.Count.ToString(CultureInfo.CurrentCulture), isActive));
            // The official tab ends in a 2 px bar in the profile's colour, inset 8 px from either side.
            var bar = new Microsoft.UI.Xaml.Shapes.Rectangle { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Fill = AccentBrush(tab.Id), Margin = new(-4, 0, -4, -8), Visibility = isActive ? Visibility.Visible : Visibility.Collapsed, IsHitTestVisible = false };
            var holder = new Grid(); holder.Children.Add(content); holder.Children.Add(bar);
            var button = Id(new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = holder, IsChecked = isActive, Padding = new(12, 8, 12, 8), MinHeight = 36, CornerRadius = new(6, 6, 0, 0) }, "DeviceProfileTab_" + tab.Id);
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
        // The profile bar: the profile's colour fades in from the start edge, then its id, the counts, a suspect pill and Reset all.
        var meta = new Grid { ColumnSpacing = 12, Padding = new(16, 10, 16, 10) }; meta.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); meta.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var fade = new Microsoft.UI.Xaml.Media.LinearGradientBrush { StartPoint = new(0, .5), EndPoint = new(1, .5) };
        fade.GradientStops.Add(new() { Color = AccentColor(active.Id, 20), Offset = 0 }); fade.GradientStops.Add(new() { Color = AccentColor(active.Id, 0), Offset = .7 }); fade.GradientStops.Add(new() { Color = AccentColor(active.Id, 0), Offset = 1 });
        // Brushes do not follow the flow direction, and the official gradient runs from the start edge.
        if (FlowDirection == FlowDirection.RightToLeft) { fade.StartPoint = new(1, .5); fade.EndPoint = new(0, .5); }
        meta.Background = fade;
        var summary = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement Dot() { var mark = Text(D("separator"), 11, true); mark.Opacity = .4; return mark; }
        summary.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, Fill = AccentBrush(active.Id), VerticalAlignment = VerticalAlignment.Center });
        var profileId = Mono(active.Id, 11); profileId.TextTrimming = TextTrimming.CharacterEllipsis; profileId.TextWrapping = TextWrapping.NoWrap; summary.Children.Add(profileId); summary.Children.Add(Dot());
        var counts = T("web.admin.common.deviceOverrides.summary.overrides", ("count", active.Overrides.Count), ("count_text", active.Overrides.Count));
        if (showAll && rows.Count > active.Overrides.Count) counts += " · " + T("web.admin.common.deviceOverrides.summary.inherited", ("count", rows.Count - active.Overrides.Count), ("count_text", rows.Count - active.Overrides.Count));
        var countsText = Text(counts, 11, true); Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(countsText, FontNumeralAlignment.Tabular); summary.Children.Add(countsText);
        if (suspect > 0)
        {
            summary.Children.Add(Dot());
            var suspectText = Caps(T("web.admin.common.deviceOverrides.summary.suspect", ("count", suspect), ("count_text", suspect)), 10, 40, Microsoft.UI.Text.FontWeights.Medium, lineHeight: 15);
            var suspectPill = new Border { Child = DeviceSettingWarning(suspectText, 10), Padding = new(6, 1, 6, 1), CornerRadius = new(4), Background = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .1), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(suspectPill, T("web.admin.common.deviceOverrides.summary.suspectTitle")); summary.Children.Add(suspectPill);
        }
        meta.Children.Add(summary);
        var resetAll = DetailButton(T("web.admin.common.deviceOverrides.summary.resetAll"), "DeviceResetProfile", active.Id, () => _ = ResetProfileAsync(active.Id, active.Name, active.Overrides));
        var resetAllContent = Inline(4); resetAllContent.Children.Add(AdminMonitorUi.Icon("rotate-ccw", 12, Brush("SecondaryTextBrush"))); resetAllContent.Children.Add(Text(T("web.admin.common.deviceOverrides.summary.resetAll"), 11.5, true)); resetAll.Content = resetAllContent;
        resetAll.Padding = new(8, 4, 8, 4); resetAll.VerticalAlignment = VerticalAlignment.Center;
        resetAll.IsEnabled = active.Overrides.Count > 0 && active.Id != UnknownProfile && _device.Text("device_id").Length <= 128 && !Session.NeedsReview; Grid.SetColumn(resetAll, 1); meta.Children.Add(resetAll);
        _tabsHost.Children.Add(Divider(0, .6)); _tabsHost.Children.Add(meta); _tabsHost.Children.Add(Divider(0, .6));
        if (rows.Count == 0) _rowsHost.Children.Add(Padded(Text(D("detail.noProfilesBody"), 12, true)));
        // Official: the rows are divided by hairlines at 40%, inset by the list's own 16 px.
        for (var index = 0; index < rows.Count; index++)
        {
            if (index > 0) _rowsHost.Children.Add(Divider(16, .4));
            _rowsHost.Children.Add(rows[index]);
        }
        if (keepView) RestoreRowsView();
    }
    /// <summary>The official profile initial: a 20 px circle in the profile's colour.</summary>
    private static Border TabInitial(string text, string profileId) => new()
    {
        Width = 20, Height = 20, CornerRadius = new(10), BorderThickness = new(1), BorderBrush = LogsNativeUi.Tint("BorderBrush", .6), Background = LogsNativeUi.Tint("SurfaceRaisedBrush", .7),
        Child = new TextBlock { Text = text, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AccentBrush(profileId), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };
    /// <summary>The official count pill on a tab: rounded, outlined, on a darker fill than the tab; its text follows the tab's own colour.</summary>
    private static Border TabCount(string text, bool active)
    {
        var label = Inherited(Text(text, 10.5), active ? 1 : .7); label.LineHeight = 16; Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        return SiloPlayer.Helpers.RoundedBadge.Apply(new Border { Child = label, Padding = new(6, 2, 6, 2), BorderThickness = new(1), BorderBrush = LogsNativeUi.Tint("BorderBrush", active ? .7 : .4), Background = LogsNativeUi.Tint("AppBackgroundBrush", active ? .7 : .3) }, 10);
    }
    private void ReplaceProfileQuery()
    {
        var request = _route.WithQuery("profile", _profile == UnknownProfile ? null : _profile);
        if (AdminShellPage.TryReplace(this, request)) _route = request;
    }

    private FrameworkElement SettingRow(ProfileTab tab, string key, DeviceSettingDefinition? definition, bool isOverride, string value, AdminInheritedValue? inherited, AdminSettingAnomaly? anomaly)
    {
        var canWrite = tab.Id != UnknownProfile && _device.Text("device_id").Length <= 128 && !Session.NeedsReview; var structured = AdminDevicePresentation.IsStructured(definition);
        var grid = new Grid { ColumnSpacing = 24, Margin = new(16, 0, 16, 0), Padding = new(0, 12, 0, 12) };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (anomaly != null)
        {
            grid.Margin = new(4, 0, 16, 0);
            grid.Padding = new(10, 12, 0, 12);
            grid.BorderThickness = new(2, 0, 0, 0);
            grid.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .4);
        }

        var copy = Stack(2);
        var title = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        var heading = Text(definition?.Label ?? key, 13.5);
        heading.FontWeight = Microsoft.UI.Text.FontWeights.Medium; heading.LineHeight = 17;
        title.Children.Add(heading);
        var source = inherited != null ? AdminDevicePresentation.InheritedSource(inherited) : T("web.admin.common.deviceOverrides.source.appDefault");
        if (isOverride) title.Children.Add(OverrideDot(T("web.admin.common.deviceOverrides.override.setTitle"), T("web.admin.common.deviceOverrides.override.setLabel")));
        else
        {
            // Official: a setting the device does not override shows where its value comes from, as an outlined label.
            var badge = DevicePill(T(inherited?.ProfileName != null ? "web.admin.common.deviceOverrides.badge.profile" : "web.admin.common.deviceOverrides.badge.default"));
            ToolTipService.SetToolTip(badge, T("web.admin.common.deviceOverrides.override.notSetTitle", ("source", source)));
            title.Children.Add(badge);
        }
        if (anomaly != null)
        {
            var warning = DevicePill(T("web.admin.common.deviceOverrides.badge." + anomaly.Kind), warning: true);
            ToolTipService.SetToolTip(warning, anomaly.Reason);
            title.Children.Add(warning);
        }
        var rawKey = Mono(key, 11); rawKey.LineHeight = 14; rawKey.Opacity = .8;
        copy.Children.Add(title); copy.Children.Add(rawKey);
        if (definition?.Description is { Length: > 0 } description) { var about = Text(description, 12.5, true); about.LineHeight = 20; about.Margin = new(0, 4, 0, 0); copy.Children.Add(about); }
        // The shown value reads at 80% of the text colour, then the default or the source in the muted colour.
        var valueLine = Text("", 11.5, true); valueLine.LineHeight = 17; valueLine.Margin = new(0, 2, 0, 0);
        valueLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = AdminDevicePresentation.FormatValue(key, value), Foreground = LogsNativeUi.Tint("PrimaryTextBrush", .8) });
        if (isOverride) { if (!structured && definition != null) valueLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " + T("web.admin.common.deviceOverrides.value.default", ("value", AdminDevicePresentation.FormatValue(key, AdminDevicePresentation.DefaultString(definition)))) }); }
        else if (inherited != null) valueLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " + source });
        copy.Children.Add(valueLine);
        if (anomaly != null)
        {
            var text = Text(anomaly.Reason, 11.5); text.LineHeight = 18.7;
            var reason = DeviceSettingWarning(text, 12);
            reason.Margin = new(0, 6, 0, 0);
            copy.Children.Add(reason);
        }
        grid.Children.Add(copy);
        var controls = Stack(8);
        controls.HorizontalAlignment = HorizontalAlignment.Right;
        controls.VerticalAlignment = VerticalAlignment.Center;
        controls.MinWidth = 220;
        Grid.SetColumn(controls, 1);
        var control = ControlFor(tab.Id, key, definition, value, canWrite);
        controls.Children.Add(control);
        if (isOverride)
        {
            var label = T("web.admin.common.deviceOverrides.reset");
            var reset = DetailButton(label, "DeviceReset_" + key, tab.Id, () => _ = ResetOneAsync(tab.Id, key));
            var resetContent = Inline(4);
            resetContent.Children.Add(AdminMonitorUi.Icon("rotate-ccw", 12, Brush("SecondaryTextBrush")));
            resetContent.Children.Add(Text(label, 11.5, true));
            reset.Content = resetContent;
            reset.IsEnabled = canWrite;
            reset.HorizontalAlignment = HorizontalAlignment.Right;
            controls.Children.Add(reset);
        }
        else { var hint = Text(T("web.admin.common.deviceOverrides.changeToOverride"), 11, true); hint.HorizontalAlignment = HorizontalAlignment.Right; hint.FontStyle = Windows.UI.Text.FontStyle.Italic; controls.Children.Add(hint); }
        grid.Children.Add(controls);
        grid.SizeChanged += (_, args) =>
        {
            // Keep room for the copy when both the device pane and its control column are narrow.
            var stackControls = args.NewSize.Width < 400;
            Grid.SetColumnSpan(copy, stackControls ? 2 : 1);
            Grid.SetRow(controls, stackControls ? 1 : 0);
            Grid.SetColumn(controls, stackControls ? 0 : 1);
            Grid.SetColumnSpan(controls, stackControls ? 2 : 1);
            controls.MinWidth = stackControls ? 0 : 220;
            controls.Margin = new(0, stackControls ? 10 : 0, 0, 0);
            // Official: the column stretches below md, so the control sits at the start and a slider or select takes the width (a slider up to 260 px).
            controls.HorizontalAlignment = stackControls ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
            control.HorizontalAlignment = stackControls ? control is ComboBox ? HorizontalAlignment.Stretch : HorizontalAlignment.Left : control is AdminSettingSlider ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
            // A stretched element held by its MaxWidth is centred, so the stacked slider is given the width a start-aligned w-full max-w-[260px] box has.
            if (control is AdminSettingSlider) control.Width = stackControls ? Math.Min(260, Math.Max(0, args.NewSize.Width - grid.Padding.Left - grid.Padding.Right - grid.BorderThickness.Left)) : double.NaN;
        };
        return grid;
    }

    private static Grid DeviceSettingWarning(TextBlock label, double iconSize)
    {
        label.Foreground = Brush("ErrorBrush");
        var row = new Grid { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var icon = AdminMonitorUi.Icon("alert-triangle", iconSize, label.Foreground);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new(0, (label.LineHeight - iconSize) / 2, 0, 0);
        Grid.SetColumn(label, 1);
        row.Children.Add(icon);
        row.Children.Add(label);
        return row;
    }

    private FrameworkElement ControlFor(string profile, string key, DeviceSettingDefinition? definition, string value, bool enabled)
    {
        var owner = Session.Owner; var revision = _detailRevision; var label = definition?.Label ?? key;
        // True when the change was taken (sent, or refused with a notice); false when nothing could be sent, so a control can put itself back.
        bool Commit(string raw)
        {
            if (!TargetCurrent(owner, revision, profile) || Session.Busy || Session.NeedsReview) return false;
            JsonElement typed;
            try { typed = AdminDevicePresentation.ValueFromString(key, raw); }
            catch (ArgumentException error) { Session.Report(error); RenderTabsAndRows(); return true; }
            _pendingFocus = FocusedDetailControl(out _pendingState);
            _ = SaveSettingAsync(profile, key, typed);
            return true;
        }
        if (definition == null || AdminDevicePresentation.IsStructured(definition))
        {
            var edit = DetailButton(key == "playback.subtitle_appearance" ? T("web.admin.common.deviceOverrides.edit.customize") : T("web.admin.common.deviceOverrides.edit.json"), "DeviceEdit_" + key, profile,
                () => _ = key == SubtitleAppearanceKey ? EditSubtitlesAsync(profile) : EditJsonAsync(profile, key, value));
            if (key == SubtitleAppearanceKey) { _subtitleButtons.Add((edit, enabled)); ShowSubtitleButton(edit, enabled); } else edit.IsEnabled = enabled;
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
            // The official slider: the thumb and the value beside it follow a drag, and one write is sent when the thumb is released (or for each key press).
            AdminSettingSlider? slider = null;
            slider = new AdminSettingSlider(definition.Minimum, definition.Maximum, AdminDevicePresentation.SliderStep(definition), current, shown => AdminDevicePresentation.SliderReadout(definition, shown),
                next => AdminDevicePresentation.NumberChange(definition, slider!.Saved, next) is not { } changed || Commit(changed), label, "DeviceSetting_" + key, enabled);
            return slider;
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
        var ok = await Session.WriteAsync(async lease =>
        {
            await _api.DeleteDeviceSettingAsync(lease, user, profile, device, key);
            ApplyOverrideChange(owner, user, rows => AdminDevicePresentation.RemoveOverride(rows, profile, device, key));
        });
        if (!Session.IsCurrent(owner) || revision != _detailRevision) return;
        if (ok) await RefreshAfterWriteAsync(owner); else RenderTabsAndRowsIfShown();
    }
    private async Task ResetProfileAsync(string profile, string profileName, IReadOnlyList<JsonElement> overrides)
    {
        var owner = Session.Owner; var revision = _detailRevision; var user = _device.Text("user_id"); var device = _device.Text("device_id");
        if (!await _host.ConfirmAsync(D("resetProfile.title", ("name", Safe(profileName))), D("resetProfile.description"), D("resetProfile.confirm"), destructive: true, stillCurrent: () => TargetCurrent(owner, revision, profile)) || !TargetCurrent(owner, revision, profile)) return;
        var keys = overrides.Select(x => x.Text("key")).ToArray(); var completed = 0; var unknown = false;
        var ok = await Session.WriteAsync(async lease =>
        {
            var result = await _api.ResetDeviceProfileAsync(lease, user, profile, device, keys, confirmedDelete: key =>
                ApplyOverrideChange(owner, user, rows => AdminDevicePresentation.RemoveOverride(rows, profile, device, key)));
            completed = result.Completed;
            if (result.OutcomeUnknown) { unknown = true; throw new AdminResourceOutcomeUnknownException(); }
            if (result.FailureStatus is { } status) throw new ApiException("reset_stopped", "The reset stopped.", status);
        });
        if (!Session.IsCurrent(owner) || revision != _detailRevision) return;
        if (ok) { await RefreshAfterWriteAsync(owner); return; }
        var stopped = T("web.admin.common.users.resetAllStopped", ("count", keys.Length), ("count_text", keys.Length), ("completed", completed), ("reason", Session.Error ?? ""));
        void ShowStopped() { if (_device.ValueKind == JsonValueKind.Object && Tabs().Count > 0) _rowsHost.Children.Insert(0, Padded(Text(stopped, 12), 16, 8)); }
        // Reconcile partial progress after a definite refusal and retain the notice.
        if (completed > 0 && !unknown) { await RefreshAfterWriteAsync(owner, ShowStopped); return; }
        RenderTabsAndRows(); ShowStopped();
    }
    /// <summary>After a confirmed write the device, the account's values and the fleet are read again, so every pane shows what the server now holds.</summary>
    private async Task RefreshAfterWriteAsync(MonitorAuthorityLease? owner, Action? rendered = null)
    {
        var user = _device.Text("user_id"); var device = _device.Text("device_id"); var revision = ++_detailRevision;
        var detail = await Session.ReadAsync("device", lease => _api.GetDeviceAsync(lease, user, device));
        if (!Session.IsCurrent(owner) || revision != _detailRevision) return;
        var values = await ReadDeviceValuesAsync(owner, user, revision);
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
                var current = DeviceOverrides.FirstOrDefault(x => x.Text("profile_id") == profile && x.Text("key") == SubtitleAppearanceKey);
                // The official eyebrow names the scope of the override: profile (its name, else its id, else "default"), then device.
                var profileLabel = Safe(Tabs().FirstOrDefault(x => x.Id == profile)?.Name) is { Length: > 0 } tabName ? tabName : T("web.admin.common.subtitleAppearanceDialog.defaultProfile");
                var deviceLabel = Safe(_device.Text("device_name")) is { Length: > 0 } deviceName ? deviceName : T("web.admin.common.subtitleAppearanceDialog.unnamedDevice");
                var initial = current.Member("value")?.GetRawText() ??
                    (AdminDevicePresentation.Definition(SubtitleAppearanceKey) is { } definition
                        ? AdminDevicePresentation.Inherited(definition, profile, profileLabel, _values).Raw : "{}");
                var dialog = new SubtitleAppearanceDialog { XamlRoot = XamlRoot, InitialValue = initial, ApplyToLocalPlayer = false, CanReset = current.ValueKind == JsonValueKind.Object,
                    Eyebrow = profileLabel + " · " + deviceLabel, ResetLabel = T("web.admin.common.subtitleAppearanceDialog.resetOverride") };
                dialog.SaveDraftAsync = raw => SaveSubtitleDraftAsync(owner, revision, profile, user, device, raw);
                dialog.ResetOverrideAsync = async () =>
                {
                    if (!TargetCurrent(owner, revision, profile)) throw new MonitorAuthorityRetiredException();
                    if (!await Session.WriteAsync(async lease =>
                    {
                        await _api.DeleteDeviceSettingAsync(lease, user, profile, device, SubtitleAppearanceKey);
                        ApplyOverrideChange(owner, user, rows => AdminDevicePresentation.RemoveOverride(rows, profile, device, SubtitleAppearanceKey));
                    }))
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
                if (TargetCurrent(owner, revision, profile)) await RefreshAfterWriteAsync(owner);
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
        using var typed = JsonDocument.Parse(raw);
        string? storedJson = null;
        var written = await Session.TryWriteAsync(async lease =>
        {
            var stored = await _api.SetDeviceSettingAsync(lease, user, profile, device, SubtitleAppearanceKey, typed.RootElement);
            ApplyOverrideChange(owner, user, rows => AdminDevicePresentation.UpsertOverride(rows, stored));
            storedJson = stored.GetProperty("value").GetRawText();
        });
        // A write that never left (another one was running, or the page needs a review already) is a refusal that says so, never an unknown outcome.
        return AdminSubtitleDraft.ResultOf(written, Session.IsCurrent(owner), Session.NeedsReview, Session.Error) with { StoredJson = storedJson };
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
