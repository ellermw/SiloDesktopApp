using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminPlaybackSettingsRules;

namespace SiloPlayer.Views.Admin;

/// <summary>Manage > Playback > Nodes. Official 217ff25 AdminNodes + NodeRoutingSettings, in place without transitions.</summary>
public sealed partial class AdminNodesPage : Page, IAdminGutterScrollPage, IAdminPageSuspension, IAdminNavigationGuard
{
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _body = new() { Spacing = 28 };
    private readonly StackPanel _cards = new() { Spacing = 28 };
    private readonly LogsWrapPanel _groups = new() { Spacing = 8 };
    private readonly TextBlock _status = U.Text("", 13, true);
    private readonly Button _reload = U.Action(T("common.retry"), "NodesReload");
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly AdminMonitorAuthority _authority;
    private AdminPlaybackNodesController? _controller;
    private AdminPlaybackSettingsController? _routing;
    private AdminPlaybackSettingsPanel? _routingPanel;
    private AdminPlaybackNodeWindow? _rendered;
    private ContentDialog? _dialog;
    private AdminLeaveGuard? _leave;
    private string? _group;
    private bool _active, _focused = true, _visible = true;
    public FrameworkElement ScrollContent => _body;
    public AdminPlaybackNodesController? Controller => _controller;
    public bool RequiresLeaveConfirmation => _dialog != null || _leave?.RequiresConfirmation == true;
    internal ContentDialog? ActiveDialog => _dialog;
    private static string T(string key, object? args = null) => AdminPlaybackAdministrationText.T(key, args);

    public AdminNodesPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminNodesPage");
        _body.Children.Add(U.Title(T("web.admin.nodes.page.title")));
        _body.Children.Add(U.Text(T("web.admin.nodes.page.subtitle"), 14, true));
        _body.Children.Add(_status); _body.Children.Add(_reload); _body.Children.Add(_groups); _body.Children.Add(_cards);
        _scroll.Content = _body; _root.Children.Add(_scroll); Content = _root;
        _scroll.SizeChanged += (_, _) => Fit();
        _reload.Click += async (_, _) => { if (_controller != null) await _controller.ReloadAsync(); };
        _poll.Tick += async (_, _) =>
        {
            if (_active && _focused && _visible && _dialog == null && App.Services.GetService<PlayerService>()?.IsLoading != true && _controller is { State.ReviewRequired: false } controller) await controller.ReloadAsync();
        };
        Loaded += async (_, _) => { Fit(); await Activate(); }; Unloaded += (_, _) => SuspendAdminPage();
    }
    private void Fit() { var width = _scroll.ActualWidth - _body.Margin.Left - _body.Margin.Right; if (width > 0) _body.Width = width; }
    private async Task Activate()
    {
        if (_active) return; _active = true;
        _controller = new(App.Services.GetRequiredService<AdminPlaybackAdministrationApi>(), _authority); _controller.Changed += QueueRender;
        _routing = new(App.Services.GetRequiredService<AdminServerSettingsApi>(), App.Services.GetRequiredService<AdminServerSettingsStore>(), _authority, true);
        _routingPanel = new(_routing, true, this); _body.Children.Add(_routingPanel); _root.Children.Add(_routingPanel.SaveBar);
        var routing = _routing;
        _leave = new(() => routing.State.Owner, () => routing.State.DirtyKeys.Count > 0, routing.Discard);
        if (App.MainWindowInstance is { } window) { window.Activated += Activated; window.VisibilityChanged += VisibilityChanged; _visible = window.Visible; }
        _authority.AvailabilityChanged += AuthorityChanged; _poll.Start();
        await Task.WhenAll(_controller.OpenAsync(), _routing.OpenAsync());
    }
    private void Activated(object sender, WindowActivatedEventArgs args) => _focused = args.WindowActivationState != WindowActivationState.Deactivated;
    private void VisibilityChanged(object sender, WindowVisibilityChangedEventArgs args) => _visible = args.Visible;
    private void AuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return; _dialog?.Hide(); _cards.Children.Clear(); _groups.Children.Clear();
        if (_authority.IsAvailable && _controller != null && _routing != null) { _ = _controller.OpenAsync(); _ = _routing.OpenAsync(); }
    });
    private void QueueRender() { var controller = _controller; DispatcherQueue.TryEnqueue(() => { if (_active && ReferenceEquals(controller, _controller)) Render(); }); }
    private void Render()
    {
        if (_controller == null) return; var state = _controller.State; var current = state.Owner?.IsCurrent == true;
        U.UpdateLiveText(_status, !current ? "" : state.Error ?? state.Feedback ?? (state.Loading && !state.Loaded ? T("web.admin.nodes.page.loading") : ""));
        _reload.Visibility = current && state.Error != null ? Visibility.Visible : Visibility.Collapsed; _reload.IsEnabled = !state.Busy && !state.Loading;
        if (!current) { _cards.Children.Clear(); _groups.Children.Clear(); _rendered = null; _dialog?.Hide(); return; }
        _routingPanel?.SetNodeWindow(state.Loaded ? state.Window : null);
        if (!ReferenceEquals(state.Window, _rendered))
        {
            _rendered = state.Window; RenderGroups(state.Window.Items); RenderCards(state.Window.Items);
        }
        foreach (var control in Descendants(_cards).OfType<Control>()) control.IsEnabled = state.Loaded && !state.Busy && !state.Loading && !state.ReviewRequired && control.Tag is not false;
    }
    private void RenderGroups(IReadOnlyList<AdminPlaybackNode> nodes)
    {
        _groups.Children.Clear(); var groups = nodes.GroupBy(node => node.Group ?? "").ToArray();
        if (_group != null && !groups.Any(group => group.Key == _group)) _group = null;
        if (groups.Length < 2) return;
        AddGroup(null, T("web.admin.nodes.groupFilter.all"), nodes.Count);
        foreach (var group in groups) AddGroup(group.Key, group.Key.Length > 0 ? group.Key : T("web.admin.nodes.groups.ungrouped"), group.Count());
        void AddGroup(string? value, string label, int count)
        {
            var button = U.Action(label + " · " + count, "NodesGroup_" + value, primary: _group == value, pill: true);
            button.Click += (_, _) => { _group = value; _rendered = null; Render(); }; _groups.Children.Add(button);
        }
    }
    private void RenderCards(IReadOnlyList<AdminPlaybackNode> nodes)
    {
        _cards.Children.Clear();
        foreach (var type in new[] { "proxy", "transcode" })
        {
            var section = new StackPanel { Spacing = 12 }; var heading = new Grid();
            heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnSpacing = 12;
            var matching = nodes.Where(node => node.Type == type && (_group == null || (node.Group ?? "") == _group)).ToArray();
            heading.Children.Add(U.Text(T(type == "proxy" ? "web.admin.nodes.section.proxyTitle" : "web.admin.nodes.section.transcodeTitle") + " · " + matching.Length, 20));
            var add = U.Action(T(type == "proxy" ? "web.admin.nodes.section.addProxy" : "web.admin.nodes.section.addTranscode"), "NodesAdd_" + type, primary: true); add.HorizontalAlignment = HorizontalAlignment.Right;
            add.Click += async (_, _) => await EditNode(type, null); Grid.SetColumn(add, 1); heading.Children.Add(add); section.Children.Add(heading);
            section.Children.Add(U.Text(T(type == "proxy" ? "web.admin.nodes.section.proxyInfo" : "web.admin.nodes.section.transcodeInfo"), 13, true));
            if (matching.Length == 0)
            {
                var empty = type == "proxy" ? "emptyProxy" : "emptyTranscode";
                section.Children.Add(U.Text(T("web.admin.nodes.section." + empty + (_group == null ? "" : "InGroup")), 14, true));
                if (_group != null) { var clear = U.Action(T("web.admin.nodes.section.showAllGroups"), "NodesClearGroup_" + type); clear.Click += (_, _) => { _group = null; _rendered = null; Render(); }; section.Children.Add(clear); }
            }
            foreach (var node in matching) section.Children.Add(Card(node, nodes));
            _cards.Children.Add(section);
        }
        if (_controller!.State.Window.HasMore) _cards.Children.Add(U.Alert("Showing the first 1600 nodes. This bounded view does not include the remaining nodes; routing availability may be incomplete."));
    }
    private FrameworkElement Card(AdminPlaybackNode node, IReadOnlyList<AdminPlaybackNode> all)
    {
        var content = new StackPanel { Spacing = 10 };
        var state = !node.Enabled ? "disabled" : !node.Healthy ? "unhealthy" : node.Type == "transcode" && node.DrainState == "drained" ? "drained" : node.Type == "transcode" && (node.DrainRequested == true || node.DrainState == "draining") ? "draining" : "healthy";
        var heading = new LogsWrapPanel { Spacing = 12 };
        heading.Children.Add(U.Text(node.Name, 15)); heading.Children.Add(U.Text(T("web.admin.nodes.state." + state), 12, true));
        if (!string.IsNullOrEmpty(node.Group)) heading.Children.Add(U.Text(node.Group, 12, true));
        content.Children.Add(heading); content.Children.Add(U.Text(Safe(node.Url), 12, true));
        if (!string.IsNullOrEmpty(node.PublicUrl)) content.Children.Add(U.Text(T("web.admin.nodes.unit.publicUrl", new { url = Safe(node.PublicUrl) }), 12, true));
        if (!node.Enabled) content.Children.Add(U.Text(T("web.admin.nodes.unit.disabledNote"), 12, true));
        if (DrainDescription(node) is { } drain) content.Children.Add(U.Text(drain, 12, true));
        var actions = new LogsWrapPanel { Spacing = 8 }; var enabled = new ToggleSwitch { IsOn = node.Enabled, OffContent = "", OnContent = "", MinWidth = 0 };
        enabled.Tag = AdminValidators.IsStrong(node.ConfigETag);
        AutomationProperties.SetName(enabled, T("web.admin.nodes.unit.enable", new { name = node.Name })); AutomationProperties.SetAutomationId(enabled, "NodeEnabled_" + node.Id);
        // The official switch is controlled (checked={node.enabled}): a rejected, conflicting or unconfirmed update leaves the saved value showing.
        var restoring = false;
        enabled.Toggled += async (_, _) =>
        {
            if (restoring || enabled.IsOn == node.Enabled) return;
            var controller = _controller; var saved = controller != null && await controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = enabled.IsOn });
            if (saved || enabled.IsOn == node.Enabled) return;
            restoring = true; try { enabled.IsOn = node.Enabled; } finally { restoring = false; }
        }; actions.Children.Add(enabled);
        AddAction("check", "NodeCheck_", "web.admin.nodes.unit.checkHealth");
        if (node.Type == "transcode") AddAction("reprobe", "NodeReprobe_", "web.admin.nodes.unit.reprobe");
        if (node.Type == "transcode" && (node.Enabled || node.DrainRequested == true) && _controller!.State.Capabilities is { Available: true, Drain: true })
            AddAction(node.DrainRequested == true ? "resume" : "drain", "NodeDrain_", node.DrainRequested == true ? "web.admin.nodes.unit.resume" : "web.admin.nodes.unit.drain");
        var edit = IconAction(T("web.admin.nodes.unit.edit", new { name = node.Name }), "NodeEdit_" + node.Id, "pencil"); edit.Tag = AdminValidators.IsStrong(node.ConfigETag); edit.Click += async (_, _) => await EditNode(node.Type, node); actions.Children.Add(edit);
        var delete = IconAction(T("web.admin.nodes.unit.delete", new { name = node.Name }), "NodeDelete_" + node.Id, "trash-2"); delete.Tag = AdminValidators.IsStrong(node.ConfigETag); delete.Click += async (_, _) => await DeleteNode(node); actions.Children.Add(delete);
        var details = new StackPanel { Spacing = 6 };
        foreach (var child in content.Children.ToArray()) { content.Children.Remove(child); details.Children.Add(child); }
        var header = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.RowDefinitions.Add(new() { Height = GridLength.Auto }); header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        header.Children.Add(details); header.Children.Add(actions); Grid.SetColumn(actions, 1); actions.HorizontalAlignment = HorizontalAlignment.Right;
        header.SizeChanged += (_, args) => { var narrow = args.NewSize.Width < 700; Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumnSpan(details, narrow ? 2 : 1); Grid.SetColumnSpan(actions, narrow ? 2 : 1); actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right; };
        content.Children.Add(header);
        if (node.Type == "transcode" && node.KeyIssued != null)
        {
            var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; keyRow.Children.Add(U.Text(T(node.KeyIssued == true ? "web.admin.nodes.key.issued" : "web.admin.nodes.key.missing"), 12, true));
            if (_controller!.State.Capabilities is { Available: true, NodeKeys: true })
            {
                var key = U.Action(T(node.KeyIssued == true ? "web.admin.nodes.key.rotate" : "web.admin.nodes.key.issue"), "NodeKey_" + node.Id); key.Click += async (_, _) => await ShowKey(node); keyRow.Children.Add(key);
            }
            details.Children.Add(keyRow);
        }
        var metrics = new LogsWrapPanel { Spacing = 30 };
        if (node.Type == "transcode") metrics.Children.Add(Acceleration(node, all));
        metrics.Children.Add(Load(node)); metrics.Children.Add(Capacity(node)); content.Children.Add(metrics);
        if (node.NetworkAccess is { Count: > 0 } providers)
        {
            foreach (var (provider, status) in providers.Take(32)) content.Children.Add(U.Text(provider + " · " + status.State + " · " + status.Hostname, 12, true));
            var network = U.Action(AdminText.T("web.admin.settings.search.networkAccess.label"), "NodeNetwork_" + node.Id);
            network.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/settings/network-access"); content.Children.Add(network);
        }
        var color = state == "healthy" ? Windows.UI.Color.FromArgb(255, 34, 197, 94) : state == "unhealthy" ? Windows.UI.Color.FromArgb(255, 239, 68, 68) : Windows.UI.Color.FromArgb(255, 245, 158, 11);
        return new Border { Child = content, Padding = new(20), CornerRadius = new(16), Background = U.Brush("SurfaceBrush"), BorderThickness = new(3, 0, 0, 0), BorderBrush = new SolidColorBrush(color), Opacity = node.Enabled ? 1 : .65 };
        void AddAction(string command, string id, string key)
        {
            var button = IconAction(T(key, new { name = node.Name }), id + node.Id, command switch { "check" => "refresh-cw", "reprobe" => "scan-search", "resume" => "circle-play", _ => "circle-pause" }); button.Click += async (_, _) => { if (_controller != null) await _controller.CommandAsync(node, command); }; actions.Children.Add(button);
        }
    }
    private static Button IconAction(string label, string id, string icon)
    {
        var button = U.Action(label, id); button.Content = AdminMonitorUi.Icon(icon, 14, U.Brush("PrimaryTextBrush")); button.Width = 32; button.Padding = new(6); ToolTipService.SetToolTip(button, label); return button;
    }
    private static FrameworkElement Capacity(AdminPlaybackNode node)
    {
        var panel = new StackPanel { Spacing = 7, MinWidth = 200 }; panel.Children.Add(U.Text(T("web.admin.nodes.unit.capacity"), 12, true));
        panel.Children.Add(U.Text($"{node.ActiveJobs} / {(node.MaxJobs is > 0 ? node.MaxJobs.ToString() : T("web.admin.nodes.form.unlimited"))}", 14));
        panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = node.MaxJobs is > 0 ? Math.Clamp(100d * node.ActiveJobs / node.MaxJobs.Value, 0, 100) : 0, Height = 4 });
        if (node.Type == "proxy") panel.Children.Add(U.Text($"{node.EgressKbps / 1000d:0.##} Mbps / {(node.MaxBandwidthKbps is > 0 ? (node.MaxBandwidthKbps.Value / 1000d).ToString("0.##") + " Mbps" : T("web.admin.nodes.form.unlimited"))}", 12, true));
        panel.Children.Add(U.Text(T("web.admin.nodes.unit.checked") + " · " + (AdminPlaybackAuditText.Relative(node.LastHealthCheck, DateTimeOffset.UtcNow) ?? T("web.admin.nodes.unit.never")), 12, true)); return panel;
    }
    private static FrameworkElement Load(AdminPlaybackNode node)
    {
        var panel = new StackPanel { Spacing = 7, MinWidth = 220 }; panel.Children.Add(U.Text(T("web.admin.nodes.unit.load"), 12, true));
        var stats = node.LastStats?.System;
        var stale = SampleStale(node);
        if (stats == null || !node.Healthy || stale) panel.Children.Add(U.Text(T(stale ? "web.admin.nodes.unit.staleSample" : "web.admin.nodes.unit.noSample"), 12, true));
        else
        {
            Metric("CPU", stats.CpuPct, "%");
            panel.Children.Add(U.Text($"Memory · {Value(stats.MemUsedMb)} / {Value(stats.MemTotalMb)} MB", 12, true));
            foreach (var disk in (stats.Disks ?? []).Take(8)) panel.Children.Add(U.Text((disk.Role ?? "Disk") + " · " + (disk.Unavailable == true ? "—" : $"{Value(disk.UsedGb)} / {Value(disk.TotalGb)} GB") + (disk.Stale == true ? " · stale" : ""), 12, true));
            panel.Children.Add(U.Text($"Network · ↓ {Value(stats.NetRxBps / 1_000_000)} / ↑ {Value(stats.NetTxBps / 1_000_000)} Mbps", 12, true));
            if (node.LastStats?.Attribution?.Process?.ResidentBytes is { } resident) panel.Children.Add(U.Text($"Process · {resident / 1048576d:0.#} MB", 12, true));
        }
        if (node.LastStats?.Build?.Display is { } build) panel.Children.Add(U.Text(build, 12, true)); return panel;
        void Metric(string label, double? value, string unit) { panel.Children.Add(U.Text(label + " · " + Value(value) + unit, 12, true)); panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Value = value.HasValue && double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0, 100) : 0 }); }
    }
    private static FrameworkElement Acceleration(AdminPlaybackNode node, IReadOnlyList<AdminPlaybackNode> all)
    {
        var panel = new StackPanel { Spacing = 7, MinWidth = 220 }; panel.Children.Add(U.Text(T("web.admin.nodes.unit.acceleration"), 12, true));
        panel.Children.Add(U.Text(node.Capabilities?.Resolved ?? T("web.admin.nodes.gpu.awaitingLabel"), 14));
        if (!string.IsNullOrEmpty(node.HwAccelOverride)) panel.Children.Add(U.Text("Override · " + node.HwAccelOverride, 12, true));
        if (!string.IsNullOrEmpty(node.CapabilityDrift)) panel.Children.Add(U.Text("Drift · " + Safe(node.CapabilityDrift), 12, true));
        var staleReason = node.AdvertisedCapabilitiesHash != null && node.AdvertisedCapabilitiesHash.Trim() != (node.CapabilitiesHash ?? "").Trim() ? node.AdvertisedCapabilitiesHash.Trim().Length == 0 ? "unreported" : "contradicted"
            : DateTimeOffset.TryParse(node.LastHealthCheck, out var checkedAt) && DateTimeOffset.UtcNow - checkedAt > TimeSpan.FromMinutes(10) ? "unconfirmed" : null;
        if (staleReason != null)
        {
            var stale = U.Text(T("web.admin.nodes.unit.stale"), 12, true); ToolTipService.SetToolTip(stale, T("web.admin.nodes.staleInventory." + staleReason, new { refreshed = node.CapabilitiesRefreshedAt ?? "—", @checked = node.LastHealthCheck ?? "—" })); panel.Children.Add(stale);
        }
        if (all.Any(other => other.Id != node.Id && other.PhysicalGpuKeys.Intersect(node.PhysicalGpuKeys).Any())) panel.Children.Add(U.Text(T("web.admin.nodes.sharedGpu.label"), 12, true));
        foreach (var path in (node.Capabilities?.RenderDevices ?? []).Take(16)) panel.Children.Add(U.Text(path, 12, true));
        foreach (var probe in (node.Capabilities?.DetectedBackends ?? []).Where(probe => !probe.Verified).Take(8)) panel.Children.Add(U.Text(probe.Backend + " · " + (probe.Skipped ? "Not accessible" : "Probe not verified"), 12, true));
        foreach (var gpu in (node.Healthy && !SampleStale(node) ? node.LastStats?.Gpu ?? [] : Array.Empty<DashboardGpuStats>()).Take(16))
        {
            panel.Children.Add(U.Text($"{gpu.Device} · {Value(gpu.VideoBusyPct)}% · {Value(gpu.Sessions)} sessions", 12, true));
            panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Value = gpu.VideoBusyPct is { } busy && double.IsFinite(busy) ? Math.Clamp(busy, 0, 100) : 0 });
        }
        return panel;
    }
    private static string Value(double? value) => value is { } number && double.IsFinite(number) ? number.ToString("0.#") : "—";
    private static string? DrainDescription(AdminPlaybackNode node)
    {
        if (node.Type != "transcode") return null; var requested = node.DrainRequested == true; var state = node.DrainState ?? "none";
        if (!requested && (!node.Enabled || !node.Healthy || state == "none")) return null;
        var label = !node.Enabled ? "requestedLabel" : !node.Healthy ? "notAnsweringLabel" : state == "drained" ? requested ? "safeToStopLabel" : "noWorkLabel" : "drainingLabel";
        var detail = !node.Enabled ? "disabledDetail" : !node.Healthy ? "notAnsweringDetail" : state == "drained" ? requested ? "safeToStopDetail" : "noWorkDetail" : requested ? "requestedDetail" : "stopSignalDetail";
        return T("web.admin.nodes.drain.statusLine", new { label = T("web.admin.nodes.drain." + label, new { count = node.ActiveJobs, count_text = node.ActiveJobs }), detail = T("web.admin.nodes.drain." + detail) });
    }
    private static bool SampleStale(AdminPlaybackNode node) => DateTimeOffset.TryParse(node.LastStats?.SampledAt, out var sampled) && DateTimeOffset.TryParse(node.LastHealthCheck, out var checkedAt)
        && (checkedAt - sampled > TimeSpan.FromSeconds((node.LastStats?.Attribution?.SampleIntervalSeconds ?? 5) * 3) || DateTimeOffset.UtcNow - checkedAt > TimeSpan.FromSeconds(90));
    private static string Safe(string? value) => LocalLog.RedactDiagnosticText((value ?? "").Length > 2000 ? value![..2000] + "…" : value ?? "");
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    public async Task<bool> CanLeaveAsync()
    {
        if (_dialog != null || _routingPanel?.IsDialogOpen == true || _controller?.State.Busy == true || _routing?.State.Saving == true) return false;
        return _leave == null || await _leave.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog));
    }
    public void SuspendAdminPage()
    {
        if (!_active) return; _active = false; _poll.Stop(); _dialog?.Hide(); _dialog = null;
        _authority.AvailabilityChanged -= AuthorityChanged;
        if (App.MainWindowInstance is { } window) { window.Activated -= Activated; window.VisibilityChanged -= VisibilityChanged; }
        _controller?.Dispose(); _controller = null; _routingPanel?.Dispose(); if (_routingPanel != null) { _root.Children.Remove(_routingPanel.SaveBar); _body.Children.Remove(_routingPanel); } _routingPanel = null;
        _routing?.Dispose(); _routing = null; _cards.Children.Clear(); _groups.Children.Clear(); _rendered = null; _status.Text = "";
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { SuspendAdminPage(); base.OnNavigatedFrom(args); }
}
