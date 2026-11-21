using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official AdminDevices: fleet pulse, filter rail, grouped device list and the per-device detail with profile tabs. Canonical settings only.</summary>
public sealed partial class AdminDevicesPage : Page, IAdminPageSuspension
{
    private readonly AdminUserResourcesApi _api;
    private readonly AdminUserResourceHost _host;
    private AdminRouteRequest _route = new(AdminRoute.Devices);
    private IReadOnlyList<JsonElement> _fleet = [];
    private IReadOnlyDictionary<string, AdminDeviceAnomaly> _anomalies = new Dictionary<string, AdminDeviceAnomaly>();
    private string _search = "", _saved = "", _groupBy = "user";
    private bool _overridesOnly;
    private int _visibleDevices = 100;
    private readonly HashSet<string> _platforms = [], _ranges = [], _recency = [];
    private readonly Dictionary<string, bool> _groupOpen = new(StringComparer.Ordinal);
    private StackPanel _rail = Stack(14), _list = Stack(0), _detail = Stack(0);
    private Border _railPanel = null!, _listPanel = null!, _detailPanel = null!;
    private ScrollViewer _listScroll = null!;
    private StackPanel? _pulse;
    private TextBox? _searchBox;
    private Grid? _panes;
    public AdminUserResourceController Session => _host.Session;

    public AdminDevicesPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled; Id(this, "AdminDevicesPage");
        _api = App.Services.GetRequiredService<AdminUserResourcesApi>();
        _host = new(this, new(App.Services.GetRequiredService<AdminMonitorAuthority>()), LoadAsync, Clear);
        BuildPanes();
        SizeChanged += (_, _) => LayoutPanes();
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest route) _route = route; _profile = _route.Get("profile") ?? ""; }
    public void SuspendAdminPage() => _host.Suspend();
    /// <summary>Fresh panes for every render: an element taken out of an old tree can still belong to it, so none is reused.</summary>
    private void BuildPanes()
    {
        _rail = Stack(14); _list = Stack(0); _detail = Stack(0);
        _listScroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _railPanel = LogsNativeUi.Panel(_rail, 12, 12); _listPanel = LogsNativeUi.Panel(_listScroll, 12, 4); _detailPanel = LogsNativeUi.Panel(_detail, 12, 0);
        _railPanel.VerticalAlignment = _listPanel.VerticalAlignment = _detailPanel.VerticalAlignment = VerticalAlignment.Top; _listScroll.VerticalAlignment = VerticalAlignment.Top; _list.VerticalAlignment = _detail.VerticalAlignment = VerticalAlignment.Top;
    }
    private static string D(string key, params (string Name, object? Value)[] args) => T("web.admin.devices." + key, args);

    private void Clear()
    {
        _detailRevision++; _subtitleDialog?.Hide(); _subtitleDialog = null; _fleet = []; _values = []; _anomalies = new Dictionary<string, AdminDeviceAnomaly>(); _device = default;
        _search = _saved = ""; _platforms.Clear(); _ranges.Clear(); _recency.Clear(); _groupOpen.Clear(); _overridesOnly = false; _visibleDevices = 100;
        _list.Children.Clear(); _detail.Children.Clear(); _rail.Children.Clear(); _panes = null; _pulse = null;
    }

    private void SetContent(params UIElement[] elements) { _host.Content.Children.Clear(); foreach (var element in elements) _host.Content.Children.Add(element); }
    private void ShowFailure(string text) { SetContent(Text(text), Button(T("common.retry"), "DeviceRetry", () => _ = _host.ReloadAsync())); _host.RaiseChanged(); }

    private async Task LoadAsync()
    {
        var pageOwner = Session.Owner;
        SetContent(Text("Loading devices…"));
        var capabilities = await Session.ReadAsync("capabilities", owner => _api.DeviceCapabilitiesAsync(owner));
        if (!Session.IsCurrent(pageOwner)) return;
        if (capabilities.ValueKind != JsonValueKind.Object) { ShowFailure("Device administration could not be read."); return; }
        if (!capabilities.Flag("available")) { SetContent(LogsNativeUi.Header(D("title"), D("subtitle", ("shortcut", "Ctrl+K"))), Text("Device administration is unavailable on this server.")); return; }
        var fleet = await Session.ReadAsync("fleet", owner => _api.ListAllDevicesAsync(owner));
        if (!Session.IsCurrent(pageOwner)) return;
        if (fleet == null) { ShowFailure("Devices could not be loaded."); return; }
        _fleet = fleet; _anomalies = AdminDevicePresentation.Anomalies(fleet, DateTimeOffset.UtcNow); RenderFleet();
        if (_route.Segments.Count == 2) await LoadDetailAsync(); else ShowEmptyDetail();
    }

    private void ShowEmptyDetail()
    {
        _detail.Children.Clear(); var empty = Stack(6); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.Margin = new(24, 64, 24, 64);
        var title = Text(D("detail.emptyTitle"), 16); title.HorizontalAlignment = HorizontalAlignment.Center; var body = Text(D("detail.emptyBody"), 12, true); body.TextAlignment = TextAlignment.Center; body.MaxWidth = 320;
        empty.Children.Add(title); empty.Children.Add(body); _detail.Children.Add(empty);
    }

    // ---------- scaffold ----------
    private void RenderFleet()
    {
        var search = Input(D("search.placeholder"), "DeviceSearch", _search); search.PlaceholderText = D("search.placeholder"); search.Header = null; search.MinWidth = 280;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, D("search.placeholder"));
        _searchBox = search; search.TextChanged += (_, _) => { _search = search.Text; _visibleDevices = 100; RenderList(); RenderFilters(); };
        var elements = new List<UIElement> { LogsNativeUi.Header(D("title"), D("subtitle", ("shortcut", "Ctrl+K")), search) };
        _pulse = Inline(16); _pulse.HorizontalAlignment = HorizontalAlignment.Left; elements.Add(_pulse);
        var scope = Stack(0); scope.HorizontalAlignment = HorizontalAlignment.Left; scope.Tag = "scope"; elements.Add(scope);
        BuildPanes(); _panes = new Grid { ColumnSpacing = 16, RowSpacing = 16 }; _panes.Children.Add(_railPanel); _panes.Children.Add(_listPanel); _panes.Children.Add(_detailPanel);
        elements.Add(_panes); SetContent([.. elements]);
        _scopeHost = scope; RenderPulse(); RenderFilters(); RenderList(); LayoutPanes();
    }
    private StackPanel? _scopeHost;
    private static void DetachFromParent(FrameworkElement element) { if (element.Parent is Panel panel) panel.Children.Remove(element); }

    private void LayoutPanes()
    {
        if (_panes == null) return; _panes.ColumnDefinitions.Clear(); _panes.RowDefinitions.Clear();
        var wide = ActualWidth >= 1050; _listScroll.MaxHeight = wide ? 640 : double.PositiveInfinity; _railPanel.VerticalAlignment = VerticalAlignment.Top; _railPanel.Visibility = Visibility.Visible;
        if (wide)
        {
            _panes.ColumnDefinitions.Add(new() { Width = new(ActualWidth >= 1440 ? 220 : 200) }); _panes.ColumnDefinitions.Add(new() { Width = new(ActualWidth >= 1440 ? 360 : 320) }); _panes.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 320 });
            Grid.SetRow(_railPanel, 0); Grid.SetRow(_listPanel, 0); Grid.SetRow(_detailPanel, 0); Grid.SetColumn(_railPanel, 0); Grid.SetColumn(_listPanel, 1); Grid.SetColumn(_detailPanel, 2);
        }
        else
        {
            _panes.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); for (var i = 0; i < 3; i++) _panes.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(_railPanel, 0); Grid.SetColumn(_listPanel, 0); Grid.SetColumn(_detailPanel, 0); Grid.SetRow(_railPanel, 0); Grid.SetRow(_listPanel, 1); Grid.SetRow(_detailPanel, 2);
        }
    }

    private IEnumerable<JsonElement> Scoped => _overridesOnly ? _fleet.Where(x => x.Number("override_count") > 0) : _fleet;
    private bool HasAnyFilter => _saved.Length > 0 || _overridesOnly || _platforms.Count > 0 || _ranges.Count > 0 || _recency.Count > 0 || _search.Length > 0;
    private void ClearFilters()
    {
        _saved = ""; _platforms.Clear(); _ranges.Clear(); _recency.Clear(); _overridesOnly = false; _search = ""; _visibleDevices = 100; _groupOpen.Clear();
        if (_searchBox != null) _searchBox.Text = "";
        RenderPulse(); RenderFilters(); RenderList();
    }

    // ---------- fleet pulse + scope ----------
    private void RenderPulse()
    {
        if (_pulse == null) return; _pulse.Children.Clear();
        if (_fleet.Count > 0)
        {
            var users = _fleet.Select(x => x.Text("user_id")).Distinct().Count(); var profiles = _fleet.Sum(AdminDevicePresentation.ProfileCount); var overrides = _fleet.Sum(x => x.Number("override_count"));
            void Stat(string key, int value, bool destructive = false)
            {
                var row = Inline(6); var number = Text(value.ToString(CultureInfo.CurrentCulture), 14); if (destructive) number.Foreground = Brush("ErrorBrush");
                row.Children.Add(number); row.Children.Add(Text(D("stats." + key, ("count", value)), 11, true)); _pulse.Children.Add(row);
            }
            Stat("users", users); Stat("devices", _fleet.Count); Stat("profiles", profiles); Stat("overrides", overrides); if (_anomalies.Count > 0) Stat("anomalies", _anomalies.Count, true);
            var density = Inline(6); density.Children.Add(Text(D("stats.density"), 10, true)); density.Children.Add(Histogram()); _pulse.Children.Add(density);
            _pulse.Children.Add(Text(D("groupBy.label"), 10, true));
            _pulse.Children.Add(Segmented(D("groupBy.label"), [("user", D("groupBy.user")), ("platform", D("groupBy.platform")), ("activity", D("groupBy.activity"))], _groupBy, value => { _groupBy = value; _groupOpen.Clear(); RenderList(); }, "DeviceGroupBy"));
        }
        if (_scopeHost != null)
        {
            _scopeHost.Children.Clear();
            if (_fleet.Count > 0)
                _scopeHost.Children.Add(Segmented(D("scope.allDevices"), [("all", D("scope.allDevices") + "  " + _fleet.Count), ("overrides", D("scope.withOverrides") + "  " + _fleet.Count(x => x.Number("override_count") > 0))],
                    _overridesOnly ? "overrides" : "all", value => { _overridesOnly = value == "overrides"; _visibleDevices = 100; RenderFilters(); RenderList(); if (_device.ValueKind == JsonValueKind.Object) RenderDetailBody(); }, "DeviceScope"));
        }
    }
    private FrameworkElement Histogram()
    {
        var counts = _fleet.Select(x => x.Number("override_count")).OrderByDescending(x => x).ToArray(); var max = Math.Max(counts.FirstOrDefault(), 1); const int slots = 28;
        var values = new int[slots];
        if (counts.Length <= slots) for (var i = 0; i < counts.Length; i++) values[i] = counts[i];
        else { var step = counts.Length / (double)slots; for (var i = 0; i < slots; i++) values[i] = counts[(int)Math.Floor(i * step)]; }
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(bars, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        foreach (var value in values)
        {
            var height = Math.Max(value / (double)max * 24, value > 0 ? 4 : 2);
            bars.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 3, Height = height, VerticalAlignment = VerticalAlignment.Bottom, RadiusX = 1, RadiusY = 1,
                Fill = Brush(value == 0 ? "BorderBrush" : value >= max * 0.6 ? "PrimaryTextBrush" : "SecondaryTextBrush") });
        }
        return bars;
    }

    // ---------- filter rail ----------
    private void RenderFilters()
    {
        _rail.Children.Clear(); var now = DateTimeOffset.UtcNow; var scoped = Scoped.ToArray(); var anomalyKeys = _anomalies.Keys.ToHashSet(StringComparer.Ordinal);
        var head = new Grid(); head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        head.Children.Add(Text(D("filters.savedViews"), 11, true));
        if (HasAnyFilter) { var clear = Link(D("filters.clear"), "DeviceFiltersClear", ClearFilters); Grid.SetColumn(clear, 1); head.Children.Add(clear); }
        _rail.Children.Add(head);
        var views = new (string Value, string Label, Func<JsonElement, bool> Match)[]
        {
            ("anomalies", D("filters.anomalies"), x => AdminDevicePresentation.SavedView(x, "anomalies", anomalyKeys, now)), ("recent", D("filters.recent"), x => AdminDevicePresentation.SavedView(x, "recent", anomalyKeys, now)),
            ("hdr", D("filters.hdr"), x => AdminDevicePresentation.SavedView(x, "hdr", anomalyKeys, now)), ("subtitle", D("filters.heavyCustomizers"), x => AdminDevicePresentation.SavedView(x, "subtitle", anomalyKeys, now)),
            ("dormant", D("filters.dormant"), x => AdminDevicePresentation.SavedView(x, "dormant", anomalyKeys, now)),
        };
        foreach (var (value, label, match) in views) _rail.Children.Add(FacetRow(label, scoped.Count(match), _saved == value, true, on => { _saved = on ? value : ""; _visibleDevices = 100; RenderFilters(); RenderList(); }, "DeviceView_" + value));
        var available = AdminDevicePresentation.PlatformOrder.Where(kind => scoped.Any(x => AdminDevicePresentation.Platform(x.Text("device_platform")) == kind)).ToArray();
        if (available.Length > 1) AddFacetGroup(D("filters.platform"), _platforms, available.Select(kind => (kind, AdminDevicePresentation.PlatformKindLabel(kind))).ToArray(), x => AdminDevicePresentation.Platform(x.Text("device_platform")), scoped, "Platform");
        AddFacetGroup(D("filters.overrideCount"), _ranges, [("none", D("filters.overrideNone")), ("1-2", "1-2"), ("3-5", "3-5"), ("6+", "6+")], AdminDevicePresentation.OverrideRange, scoped, "Overrides");
        AddFacetGroup(D("filters.lastSeen"), _recency, [("<24h", D("filters.recency24h")), ("<7d", D("filters.recency7d")), ("<30d", D("filters.recency30d")), (">30d", D("filters.recencyOlder"))], x => AdminDevicePresentation.Recency(x, now), scoped, "LastSeen");
    }
    private void AddFacetGroup(string title, HashSet<string> chosen, (string Value, string Label)[] options, Func<JsonElement, string> bucket, JsonElement[] scoped, string id)
    {
        _rail.Children.Add(Text(title, 11, true));
        foreach (var (value, label) in options)
            _rail.Children.Add(FacetRow(label, scoped.Count(x => bucket(x) == value), chosen.Contains(value), false, on => { if (on) chosen.Add(value); else chosen.Remove(value); _visibleDevices = 100; RenderFilters(); RenderList(); }, "Facet_" + id + "_" + value));
    }
    /// <summary>A saved view (a pressed button) or a facet (a checkbox) with its count, in the official rail's row shape.</summary>
    private static FrameworkElement FacetRow(string label, int count, bool active, bool asButton, Action<bool> changed, string id)
    {
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = Truncated(label, 13); var total = Text(count.ToString(CultureInfo.CurrentCulture), 11, true);
        if (asButton) { Inherited(name); Inherited(total, 0.75); } Grid.SetColumn(total, 1); content.Children.Add(name); content.Children.Add(total);
        if (asButton)
        {
            var button = Id(new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = content, IsChecked = active, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(8, 6, 8, 6), BorderThickness = new(0) }, id);
            OfficialSelected(button); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label + ", " + count); button.Checked += (_, _) => changed(true); button.Unchecked += (_, _) => changed(false); return button;
        }
        var box = Id(new CheckBox { Content = content, IsChecked = active, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 32 }, id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, label + ", " + count);
        box.Checked += (_, _) => changed(true); box.Unchecked += (_, _) => changed(false); return box;
    }

    // ---------- list ----------
    private IReadOnlyList<JsonElement> Filtered()
    {
        var now = DateTimeOffset.UtcNow; var anomalyKeys = _anomalies.Keys.ToHashSet(StringComparer.Ordinal); var query = _search.Trim();
        return Scoped.Where(row => (_saved.Length == 0 || AdminDevicePresentation.SavedView(row, _saved, anomalyKeys, now)) &&
            (_platforms.Count == 0 || _platforms.Contains(AdminDevicePresentation.Platform(row.Text("device_platform")))) && (_ranges.Count == 0 || _ranges.Contains(AdminDevicePresentation.OverrideRange(row))) &&
            (_recency.Count == 0 || _recency.Contains(AdminDevicePresentation.Recency(row, now))) && (query.Length == 0 ||
            string.Join(" ", row.Text("device_name"), row.Text("device_id"), row.Text("username"), row.Text("email"), row.Text("device_platform"), string.Join(" ", row.Rows("profiles").Select(x => x.Text("profile_name") + " " + x.Text("profile_id"))))
                .Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
    }
    private sealed record DeviceGroup(string Key, string Label, string? UserId, IReadOnlyList<JsonElement> Devices);
    private IReadOnlyList<DeviceGroup> Groups(IReadOnlyList<JsonElement> rows)
    {
        var now = DateTimeOffset.UtcNow;
        IEnumerable<IGrouping<string, JsonElement>> grouped = _groupBy switch
        {
            "platform" => rows.GroupBy(row => "p:" + AdminDevicePresentation.Platform(row.Text("device_platform"))), "activity" => rows.GroupBy(row => "a:" + AdminDevicePresentation.Recency(row, now)),
            _ => rows.GroupBy(row => "u:" + row.Text("user_id")),
        };
        var result = grouped.Select(group =>
        {
            var first = group.First(); var key = group.Key; string label; string? user = null;
            if (_groupBy == "platform") label = AdminDevicePresentation.PlatformKindLabel(key[2..]);
            else if (_groupBy == "activity") label = D(key[2..] switch { "<24h" => "group.last24h", "<7d" => "group.thisWeek", "<30d" => "group.thisMonth", _ => "group.older" });
            else { user = first.Text("user_id"); label = first.Text("username") is { Length: > 0 } name ? name : first.Text("email") is { Length: > 0 } email ? email : D("group.userFallback", ("id", user)); }
            return new DeviceGroup(key, label, user, group.ToArray());
        });
        return _groupBy == "user" ? result.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToArray()
            : result.OrderBy(x => _groupBy == "platform" ? AdminDevicePresentation.PlatformOrder.ToList().IndexOf(x.Key[2..]) : Array.IndexOf(new[] { "<24h", "<7d", "<30d", ">30d" }, x.Key[2..])).ToArray();
    }
    private string? ActiveUser => _route.Segment(0); private string? ActiveDevice => _route.Segment(1);

    private void RenderList()
    {
        _list.Children.Clear(); var rows = Filtered();
        var head = new Grid { Margin = new(12, 8, 12, 8) }; head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        head.Children.Add(Text(rows.Count != _fleet.Count ? D("list.countOfTotal", ("count", rows.Count), ("count_text", rows.Count), ("total", _fleet.Count)) : D("list.count", ("count", rows.Count), ("count_text", rows.Count)), 11, true));
        if (HasAnyFilter) { var clear = Link(D("list.clearAll"), "DeviceListClear", ClearFilters); Grid.SetColumn(clear, 1); head.Children.Add(clear); }
        _list.Children.Add(head);
        if (rows.Count == 0)
        {
            var empty = Stack(8); empty.Margin = new(16, 40, 16, 40); empty.HorizontalAlignment = HorizontalAlignment.Center;
            var title = Text(D(_fleet.Count > 0 ? "empty.noMatchTitle" : "empty.noDevicesTitle"), 14); title.HorizontalAlignment = HorizontalAlignment.Center;
            var body = Text(D(_fleet.Count > 0 ? "empty.noMatchBody" : "empty.noDevicesBody"), 12, true); body.TextAlignment = TextAlignment.Center; body.MaxWidth = 260;
            empty.Children.Add(title); empty.Children.Add(body); if (_fleet.Count > 0) empty.Children.Add(Button(D("empty.clearFilters"), "DeviceEmptyClear", ClearFilters));
            _list.Children.Add(empty); return;
        }
        var groups = Groups(rows); var forceOpen = HasAnyFilter || groups.Count == 1; var budget = _visibleDevices;
        foreach (var group in groups)
        {
            var containsActive = group.Devices.Any(x => x.Text("user_id") == ActiveUser && x.Text("device_id") == ActiveDevice);
            var open = _groupOpen.TryGetValue(group.Key, out var chosen) ? chosen : forceOpen || containsActive;
            var anomalyCount = group.Devices.Count(x => _anomalies.ContainsKey(AdminDevicePresentation.DeviceKey(x)));
            var header = new Grid { Margin = new(8, 6, 8, 2) }; header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var toggle = Id(new Button { Content = Truncated((open ? "▾  " : "▸  ") + group.Label, 14, auto: true), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), Padding = new(6, 4, 6, 4) }, "DeviceGroup_" + group.Key);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, group.Label); toggle.Click += (_, _) => { _groupOpen[group.Key] = !open; RenderList(); };
            var meta = Inline(8);
            if (group.UserId != null && AdminShellPage.CanOpen(AdminRouteRequest.UserDetail(group.UserId)))
            {
                var userId = group.UserId; var open_user = Link("↗", "DeviceGroupUser_" + userId, () => AdminShellPage.Open(AdminRouteRequest.UserDetail(userId)));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open_user, D("group.open", ("name", group.Label))); meta.Children.Add(open_user);
            }
            if (anomalyCount > 0) meta.Children.Add(Chip("⚠ " + anomalyCount, "ErrorBrush"));
            meta.Children.Add(Text(D("group.meta", ("devices", group.Devices.Count), ("profiles", group.Devices.Sum(AdminDevicePresentation.ProfileCount)), ("overrides", group.Devices.Sum(x => x.Number("override_count")))), 11, true));
            Grid.SetColumn(meta, 1); header.Children.Add(toggle); header.Children.Add(meta); _list.Children.Add(header);
            if (!open) continue;
            foreach (var device in group.Devices)
            {
                if (budget-- <= 0) break;
                _list.Children.Add(DeviceRow(device));
            }
        }
        if (budget < 0) _list.Children.Add(Button("Show more devices", "DeviceMoreRows", () => { _visibleDevices += 100; RenderList(); }));
    }

    private static string PlatformGlyph(string kind) => kind switch { "tv" => "", "mobile" => "", "tablet" => "", "desktop" => "", _ => "" };
    private FrameworkElement DeviceRow(JsonElement device)
    {
        var user = device.Text("user_id"); var id = device.Text("device_id"); var active = user == ActiveUser && id == ActiveDevice; var kind = AdminDevicePresentation.Platform(device.Text("device_platform"));
        var grid = new Grid { ColumnSpacing = 10 }; grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var tile = new Border { Width = 36, Height = 36, CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Child = new FontIcon { Glyph = PlatformGlyph(kind), FontSize = 16 } };
        Microsoft.UI.Xaml.Shapes.Ellipse? dot = null;
        if (_anomalies.ContainsKey(AdminDevicePresentation.DeviceKey(device))) { dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brush("ErrorBrush") }; ToolTipService.SetToolTip(dot, D("row.anomalyTitle")); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dot, D("row.anomalyAriaLabel")); }
        var name = new NameWithMark(Truncated(device.Text("device_name") is { Length: > 0 } deviceName ? deviceName : D("unnamedDevice"), 13, auto: true), dot) { HorizontalAlignment = HorizontalAlignment.Left };
        var text = Stack(0); text.Children.Add(name); text.Children.Add(Truncated(AdminDevicePresentation.PlatformLabel(device.Text("device_platform")) + " · " + RelativeUpdated(device.Text("last_updated")), 11, true));
        var count = device.Number("override_count"); var gauge = Inline(6); var filled = count == 0 ? 0 : Math.Min(count, 5);
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 5; i++) bars.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 3, Height = 12, Fill = Brush(i < filled ? "PrimaryTextBrush" : "BorderBrush") });
        gauge.Children.Add(bars); gauge.Children.Add(Text(count.ToString(CultureInfo.CurrentCulture), 11, !active)); Grid.SetColumn(text, 1); Grid.SetColumn(gauge, 2);
        grid.Children.Add(tile); grid.Children.Add(text); grid.Children.Add(gauge);
        var button = Id(new Button { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(8, 6, 8, 6), BorderThickness = new(0),
            Background = active ? Brush("SurfaceHoverBrush") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), CornerRadius = new(6), Margin = new(6, 0, 6, 0) }, "Device_" + user + "_" + id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, AdminNameText.Label(device.Text("device_name") is { Length: > 0 } label ? label : D("unnamedDevice"), 200) + ", " + AdminDevicePresentation.PlatformLabel(device.Text("device_platform")) + ", " + count);
        button.Click += (_, _) => OpenDevice(user, id); return button;
    }
    private void OpenDevice(string user, string device)
    {
        var request = AdminRouteRequest.Device(user, device).WithQuery("profile", _profile.Length > 0 && _profile != "unknown-profile" ? _profile : null);
        if (AdminShellPage.TryReplace(this, request)) { _route = request; _ = LoadDetailAsync(); RenderList(); } else AdminShellPage.Open(request);
    }
    private static string RelativeUpdated(string value) => string.IsNullOrEmpty(value) ? "—" : Relative(value);
    private static string Relative(string value) => DateTimeOffset.TryParse(value, out var at) && DateTimeOffset.UtcNow - at > TimeSpan.FromDays(30)
        ? Date(value) : AdminPlaybackAuditText.Relative(value, DateTimeOffset.UtcNow) ?? "—";
    private static string Date(string value) => DateTimeOffset.TryParse(value, out var at) ? SiloPlayer.Helpers.DateTimeDisplay.FormatDate(at) : "—";
}
