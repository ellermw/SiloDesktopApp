using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminPlaybackSettingsRules;

namespace SiloPlayer.Views.Admin;

/// <summary>Manage > Playback > Nodes. Official 8ae4f4f5a AdminNodes + NodeRoutingSettings, in place without transitions.</summary>
public sealed partial class AdminNodesPage : Page, IAdminGutterScrollPage, IAdminPageSuspension, IAdminNavigationGuard
{
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _body = new() { Spacing = 32 };
    private readonly StackPanel _cards = new() { Spacing = 32 };
    private readonly LogsWrapPanel _groups = new() { Spacing = 8 };
    private readonly TextBlock _status = U.Text("", 13, true);
    private readonly Button _reload = U.Action(T("common.retry"), "NodesReload");
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly AdminMonitorAuthority _authority;
    private AdminPlaybackNodesController? _controller;
    private AdminPlaybackSettingsController? _routing;
    private AdminPlaybackSettingsPanel? _routingPanel;
    private TextBlock? _routingHeading;
    private AdminPlaybackNodeWindow? _rendered;
    private ContentDialog? _dialog;
    private AdminLeaveGuard? _leave;
    private Task? _pollReload;
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
        // Official PageHeader: the title and its subtitle sit 8 px apart; the sections below are 32 px apart.
        var header = new StackPanel { Spacing = 8 }; header.Children.Add(U.Title(T("web.admin.nodes.page.title"))); header.Children.Add(U.Text(T("web.admin.nodes.page.subtitle"), 14, true)); _body.Children.Add(header);
        _status.Visibility = Visibility.Collapsed; _groups.Visibility = Visibility.Collapsed;
        _body.Children.Add(_status); _body.Children.Add(_reload); _body.Children.Add(_groups); _body.Children.Add(_cards);
        _scroll.Content = _body; _root.Children.Add(_scroll); Content = _root;
        _scroll.SizeChanged += (_, _) => Fit();
        _reload.Click += async (_, _) => { if (_controller != null) await _controller.ReloadAsync(); };
        _poll.Tick += async (_, _) => { if (_active && _focused && _visible && App.Services.GetService<PlayerService>()?.IsLoading != true) await Refresh(); };
        Loaded += async (_, _) => { Fit(); await Activate(); }; Unloaded += (_, _) => SuspendAdminPage();
    }
    private void Fit() { var width = _scroll.ActualWidth - _body.Margin.Left - _body.Margin.Right; if (width > 0) _body.Width = width; }
    // The refresh the admin did not ask for: the 30 s poll, and the rows after a re-probe has ended.
    private async Task Refresh()
    {
        if (_pollReload != null || !_active || _dialog != null || _controller is not { State.ReviewRequired: false } controller) return;
        // Kept while in flight: Render leaves the cards usable during this background refresh and a card action waits for it in Settled.
        var reload = _pollReload = controller.ReloadAsync();
        try { await reload; } finally { if (ReferenceEquals(_pollReload, reload)) _pollReload = null; }
    }
    private async Task Activate()
    {
        if (_active) return; _active = true;
        _controller = new(App.Services.GetRequiredService<AdminPlaybackAdministrationApi>(), _authority); _controller.Changed += QueueRender;
        _routing = new(App.Services.GetRequiredService<AdminServerSettingsApi>(), App.Services.GetRequiredService<AdminServerSettingsStore>(), _authority, true);
        _routingPanel = new(_routing, true, this); _routingPanel.MaxWidth = 768; _routingPanel.HorizontalAlignment = HorizontalAlignment.Left;
        // Official: <section max-w-3xl><h2>Routing</h2><NodeRoutingSettings/></section>.
        _routingHeading = U.Text(T("web.admin.nodes.page.routing"), 18); _routingHeading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; AutomationProperties.SetHeadingLevel(_routingHeading, AutomationHeadingLevel.Level2);
        _body.Children.Add(_routingHeading); _body.Children.Add(_routingPanel); _root.Children.Add(_routingPanel.SaveBar);
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
    private void QueueRender()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || !ReferenceEquals(controller, _controller)) return;
            // A card that cannot be drawn must not take the application down: the cause is logged and the page says so.
            try { Render(); }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                LocalLog.AppendException("admin-nodes-render.log", failure, "Render the Nodes page");
                U.UpdateLiveText(_status, "Nodes could not be displayed (" + failure.GetType().Name + "). Reload the page.");
            }
        });
    }
    private void Render()
    {
        if (_controller == null) return; var state = _controller.State; var current = state.Owner?.IsCurrent == true;
        U.UpdateLiveText(_status, !current ? "" : state.Error ?? state.Feedback ?? (state.Loading && !state.Loaded ? T("web.admin.nodes.page.loading") : state.Reprobing.Count > 0 ? "Re-probing hardware. This can take a couple of minutes." : ""));
        _status.Visibility = _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _reload.Visibility = current && state.Error != null ? Visibility.Visible : Visibility.Collapsed; _reload.IsEnabled = !state.Busy && !state.Loading;
        if (!current) { _cards.Children.Clear(); _groups.Children.Clear(); _rendered = null; _dialog?.Hide(); return; }
        _routingPanel?.SetNodeWindow(state.Loaded ? state.Window : null);
        if (!ReferenceEquals(state.Window, _rendered))
        {
            // Every refresh replaces the cards, so keyboard and Narrator focus is handed to the same control of the same node instead of dropping out of the list.
            var held = FocusedListControl(); var id = held == null ? "" : AutomationProperties.GetAutomationId(held); var focus = held?.FocusState ?? FocusState.Unfocused;
            _rendered = state.Window; RenderGroups(state.Window.Items); _groups.Visibility = _groups.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed; RenderCards(state.Window.Items);
            if (id.Length > 0) Refocus(id, focus);
        }
        // Only a foreground load disables the cards. Disabling them for the 30 s background refresh would take focus away from the control in use.
        var loading = state.Loading && _pollReload == null;
        // A re-probe can take minutes, so it holds back only its own node's Re-probe button (the one whose Tag is the node id), never the page.
        foreach (var control in Descendants(_cards).OfType<Control>()) control.IsEnabled = state.Loaded && !state.Busy && !loading && !state.ReviewRequired && control.Tag is not false && !(control.Tag is string reprobe && state.Reprobing.Contains(reprobe));
    }
    private Control? FocusedListControl()
    {
        if (XamlRoot == null || Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is not Control { FocusState: not FocusState.Unfocused } focused) return null;
        var owned = false;
        for (DependencyObject? parent = focused; parent != null && !owned; parent = VisualTreeHelper.GetParent(parent)) owned = ReferenceEquals(parent, _cards) || ReferenceEquals(parent, _groups);
        if (!owned) return null;
        // A control the admin scrolled away from is not chased: taking focus back there would scroll the page on every refresh.
        var bounds = focused.TransformToVisual(_scroll).TransformBounds(new(0, 0, focused.ActualWidth, focused.ActualHeight));
        return bounds.Bottom > 0 && bounds.Top < _scroll.ActualHeight ? focused : null;
    }
    private void Refocus(string id, FocusState focus)
    {
        var target = Descendants(_groups).Concat(Descendants(_cards)).OfType<Control>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id);
        if (target == null) return;
        if (target.IsLoaded) { target.Focus(focus); return; }
        // A replacement control cannot take focus before it has loaded.
        RoutedEventHandler? loaded = null; loaded = (_, _) => { target.Loaded -= loaded; if (_active) target.Focus(focus); }; target.Loaded += loaded;
    }
    // The background refresh leaves the cards usable, so an action started during one waits for it and then uses the refreshed row. A row that is gone or whose configuration changed meanwhile is left for the admin to review on its new card.
    private async Task<AdminPlaybackNode?> Settled(AdminPlaybackNode node)
    {
        if (_pollReload is { } poll) await poll;
        return _active ? _controller?.State.Window.Items.FirstOrDefault(row => row.Id == node.Id && row.ConfigETag == node.ConfigETag) : null;
    }
    private static readonly Windows.UI.Color Success = Windows.UI.Color.FromArgb(255, 0x57, 0xCB, 0x60), Warning = Windows.UI.Color.FromArgb(255, 0xEA, 0xB5, 0x32), Info = Windows.UI.Color.FromArgb(255, 0x4B, 0xA3, 0xF7), Danger = Windows.UI.Color.FromArgb(255, 0xEF, 0x44, 0x44);
    private static Brush Tint(Windows.UI.Color color, double opacity) => new SolidColorBrush(color) { Opacity = opacity };
    private static string P(string? value, int limit = AdminPlaybackNodeText.DefaultLimit) => AdminPlaybackNodeText.Plain(value, limit);
    /// <summary>Official hero-eyebrow: micro caps with wide tracking.</summary>
    private static TextBlock Eyebrow(string text) => new() { Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 220, Foreground = U.Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.NoWrap };
    /// <summary>Machine identities (URLs, device paths) are set in the mono face and read left to right in every interface language.</summary>
    private static TextBlock Mono(string text, double size = 12, Brush? foreground = null) => new()
    {
        Text = text, FontFamily = new("Consolas"), FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FlowDirection = FlowDirection.LeftToRight,
        Foreground = foreground ?? U.Brush("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Left,
    };
    private static FrameworkElement Badge(string text, Windows.UI.Color? tint, string? title, bool mono = false)
    {
        var label = new TextBlock { Text = text, FontSize = mono ? 10 : 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.NoWrap, Foreground = tint is { } color ? new SolidColorBrush(color) : U.Brush("SecondaryTextBrush") };
        if (mono) label.FontFamily = new("Consolas");
        var badge = new Border { Child = label, Padding = new(8, 2, 8, 2), CornerRadius = new(10), BorderThickness = new(1), BorderBrush = tint is { } edge ? Tint(edge, .15) : U.Brush("BorderBrush"), Background = tint is { } fill ? Tint(fill, .1) : U.Brush("SurfaceBrush"), VerticalAlignment = VerticalAlignment.Center };
        if (title != null) ToolTipService.SetToolTip(badge, title);
        return badge;
    }
    private static FrameworkElement MetricGrid(IEnumerable<DashboardMetric> metrics, double fontSize = 11)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 32 }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var row = 0;
        foreach (var metric in metrics)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var label = U.Text(metric.Label, fontSize, true); var meter = DashboardLoadMeter.Create(metric.Fill, metric.Warning);
            var value = new TextBlock { Text = metric.Value, FontFamily = new("Consolas"), FontSize = fontSize, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = metric.Muted ? U.Brush("SecondaryTextBrush") : metric.Warning ? new SolidColorBrush(Warning) : U.Brush("PrimaryTextBrush"), FontWeight = metric.Warning ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Normal };
            foreach (var (cell, column) in new (FrameworkElement, int)[] { (label, 0), (meter, 1), (value, 2) })
            {
                Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
                if (metric.Title.Length > 0 && column != 1) ToolTipService.SetToolTip(cell, metric.Title);
            }
            row++;
        }
        return grid;
    }
    private static FrameworkElement Block(string label, params FrameworkElement[] content)
    {
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(Eyebrow(label));
        foreach (var child in content) panel.Children.Add(child);
        return panel;
    }
    private void RenderGroups(IReadOnlyList<AdminPlaybackNode> nodes)
    {
        _groups.Children.Clear(); var groups = AdminPlaybackNodePresentation.Groups(nodes);
        if (_group != null && groups.All(group => group.Value != _group)) _group = null;
        if (groups.Count < 2) return;
        _groups.Children.Add(Eyebrow(T("web.admin.nodes.groupFilter.heading")));
        AddGroup(null, T("web.admin.nodes.groupFilter.all"), nodes.Count, false, T("web.admin.nodes.groupFilter.allTitle"));
        foreach (var group in groups) AddGroup(group.Value, group.Label, group.Count, group.Degraded, group.Title);
        void AddGroup(string? value, string label, int count, bool degraded, string title)
        {
            var active = _group == value;
            var button = U.Action("", "NodesGroup_" + value, primary: active, pill: true); button.Height = 28; button.MinHeight = 28; button.Padding = new(12, 0, 12, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            if (degraded) row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Warning), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = active ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Normal });
            row.Children.Add(new TextBlock { Text = count.ToString(), FontFamily = new("Consolas"), FontSize = 10, Opacity = active ? 1 : .7, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row; AutomationProperties.SetName(button, label + " " + count); ToolTipService.SetToolTip(button, title);
            button.Click += (_, _) => { _group = value; _rendered = null; Render(); }; _groups.Children.Add(button);
        }
    }
    private FrameworkElement Section(string type, IReadOnlyList<AdminPlaybackNode> all, HashSet<string> shared)
    {
        var section = new StackPanel { Spacing = 12 };
        var matching = all.Where(node => node.Type == type && (_group == null || AdminPlaybackNodePresentation.GroupOf(node) == _group)).ToArray();
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var h2 = U.Text(T(type == "proxy" ? "web.admin.nodes.section.proxyTitle" : "web.admin.nodes.section.transcodeTitle"), 18); h2.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; AutomationProperties.SetHeadingLevel(h2, AutomationHeadingLevel.Level2);
        title.Children.Add(h2); title.Children.Add(Badge(matching.Length.ToString(), null, null));
        heading.Children.Add(title);
        var addLabel = T(type == "proxy" ? "web.admin.nodes.section.addProxy" : "web.admin.nodes.section.addTranscode");
        FrameworkElement AddButton(string id, bool primary)
        {
            var add = U.Action(addLabel, id, primary: primary); add.HorizontalAlignment = HorizontalAlignment.Right; add.Height = 32;
            add.Click += async (_, _) => { if (_pollReload is { } poll) await poll; if (_active) await EditNode(type, null); };
            return add;
        }
        var top = AddButton("NodesAdd_" + type, true); Grid.SetColumn(top, 1); heading.Children.Add(top); section.Children.Add(heading);
        section.Children.Add(InfoBanner(type));
        if (matching.Length == 0)
        {
            var empty = type == "proxy" ? "emptyProxy" : "emptyTranscode"; var box = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            var text = U.Text(T("web.admin.nodes.section." + empty + (_group == null ? "" : "InGroup")), 14, true); text.TextAlignment = TextAlignment.Center; text.MaxWidth = 448; text.HorizontalAlignment = HorizontalAlignment.Center; box.Children.Add(text);
            FrameworkElement action;
            if (_group != null) { var clear = U.Action(T("web.admin.nodes.section.showAllGroups"), "NodesClearGroup_" + type); clear.Click += (_, _) => { _group = null; _rendered = null; Render(); }; action = clear; }
            else action = AddButton("NodesAddEmpty_" + type, false);
            action.HorizontalAlignment = HorizontalAlignment.Center; box.Children.Add(action);
            section.Children.Add(new Border { Child = box, Padding = new(16, 40, 16, 40), CornerRadius = new(16), Background = U.Brush("SurfaceBrush") });
        }
        else
        {
            var list = new StackPanel { Spacing = 12 };
            foreach (var node in matching) list.Children.Add(Card(node, all, shared));
            section.Children.Add(list);
        }
        return section;
    }
    private static FrameworkElement InfoBanner(string type)
    {
        var color = type == "proxy" ? Info : Warning; var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var icon = AdminPlaybackNodeIcons.Icon(type == "proxy" ? "info" : "alert-triangle", 16, new SolidColorBrush(color)); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 2, 0, 0); row.Children.Add(icon);
        var text = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(color), LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight }; Grid.SetColumn(text, 1);
        var sentence = T(type == "proxy" ? "web.admin.nodes.section.proxyInfo" : "web.admin.nodes.section.transcodeInfo");
        // The official string marks one sentence strong; the isolated English catalog keeps its text, so the sentence is found in it.
        const string strong = "Must be on the same network as proxy nodes and the backend.";
        if (type != "proxy" && sentence.IndexOf(strong, StringComparison.Ordinal) is var at and >= 0)
        {
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = sentence[..at] }); text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = strong, FontWeight = Microsoft.UI.Text.FontWeights.Bold }); text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = sentence[(at + strong.Length)..] });
        }
        else text.Text = sentence;
        row.Children.Add(text);
        return new Border { Child = row, Padding = new(12), CornerRadius = new(12), Background = U.Brush("SurfaceBrush") };
    }
    private void RenderCards(IReadOnlyList<AdminPlaybackNode> nodes)
    {
        _cards.Children.Clear();
        // One pass per window instead of comparing every pair of nodes for every card: a physical GPU key reported by more than one node is shared.
        var shared = nodes.SelectMany(node => (node.PhysicalGpuKeys ?? []).Where(key => !string.IsNullOrEmpty(key)).Distinct()).GroupBy(key => key).Where(keys => keys.Count() > 1).Select(keys => keys.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var type in new[] { "proxy", "transcode" }) _cards.Children.Add(Section(type, nodes, shared));
        if (_controller!.State.Window.HasMore) _cards.Children.Add(U.Alert("Showing the first 1600 nodes. This bounded view does not include the remaining nodes; routing availability may be incomplete."));
    }
    private FrameworkElement Card(AdminPlaybackNode node, IReadOnlyList<AdminPlaybackNode> all, HashSet<string> sharedGpus)
    {
        var name = P(node.Name); var state = AdminPlaybackNodePresentation.State(node); var drain = AdminPlaybackNodePresentation.Drain(node); var now = DateTimeOffset.UtcNow;
        var railColor = state switch { DashboardNodeState.Healthy => Success, DashboardNodeState.Draining => Warning, DashboardNodeState.Drained => Info, DashboardNodeState.Unhealthy => Danger, _ => Windows.UI.Color.FromArgb(255, 0x8A, 0x8A, 0x94) };
        var railOpacity = state == DashboardNodeState.Healthy ? .7 : state == DashboardNodeState.Disabled ? .3 : 1;
        // ---- identity ----
        var details = new StackPanel { Spacing = 4 }; var heading = new LogsWrapPanel { Spacing = 10, RowSpacing = 4 };
        var title = U.Text(name, 15); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; title.TextWrapping = TextWrapping.WrapWholeWords; AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3); heading.Children.Add(title);
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(railColor) { Opacity = state == DashboardNodeState.Disabled ? .6 : 1 } });
        status.Children.Add(U.Text(AdminPlaybackNodePresentation.StateLabel(node), 12, true)); heading.Children.Add(status);
        if (!string.IsNullOrEmpty(node.Group)) heading.Children.Add(Badge(P(node.Group), null, null, mono: true));
        details.Children.Add(heading);
        var url = Mono(P(node.Url, 2000)); ToolTipService.SetToolTip(url, P(node.Url, 2000)); url.Margin = new(0, 4, 0, 0); details.Children.Add(url);
        if (!string.IsNullOrEmpty(node.PublicUrl)) details.Children.Add(PublicUrl(node));
        if (node.Type == "transcode" && node.KeyIssued != null) details.Children.Add(KeyLine(node));
        // ---- actions ----
        var actions = new LogsWrapPanel { Spacing = 4 }; var enabled = new ToggleSwitch { IsOn = node.Enabled, OffContent = "", OnContent = "", MinWidth = 0 };
        enabled.Tag = AdminValidators.IsStrong(node.ConfigETag);
        AutomationProperties.SetName(enabled, T("web.admin.nodes.unit.enable", new { name })); AutomationProperties.SetAutomationId(enabled, "NodeEnabled_" + node.Id);
        // The official switch is controlled (checked={node.enabled}): a rejected, conflicting or unconfirmed update leaves the saved value showing.
        var restoring = false;
        enabled.Toggled += async (_, _) =>
        {
            if (restoring || enabled.IsOn == node.Enabled) return;
            var requested = enabled.IsOn; var current = await Settled(node);
            var controller = _controller; var saved = current != null && controller != null && enabled.IsOn == requested && await controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = requested });
            if (saved || enabled.IsOn == node.Enabled) return;
            restoring = true; try { enabled.IsOn = node.Enabled; } finally { restoring = false; }
        }; actions.Children.Add(enabled);
        actions.Children.Add(new Border { Width = 1, Height = 20, Margin = new(6, 0, 6, 0), Background = U.Brush("BorderBrush"), Opacity = .7, VerticalAlignment = VerticalAlignment.Center });
        AddAction("check", "NodeCheck_", "web.admin.nodes.unit.checkHealth", null);
        var drainRequested = node.DrainRequested == true;
        if (node.Type == "transcode" && (node.Enabled || drainRequested) && _controller!.State.Capabilities is { Available: true, Drain: true })
            AddAction(drainRequested ? "resume" : "drain", "NodeDrain_", drainRequested ? "web.admin.nodes.unit.resume" : "web.admin.nodes.unit.drain", drainRequested ? "web.admin.nodes.unit.resumeTitle" : "web.admin.nodes.unit.drainTitle");
        if (AdminPlaybackNodePresentation.ReportsAcceleration(node)) AddAction("reprobe", "NodeReprobe_", "web.admin.nodes.unit.reprobe", "web.admin.nodes.unit.reprobeTitle");
        var edit = IconAction(T("web.admin.nodes.unit.edit", new { name }), "NodeEdit_" + node.Id, "pencil"); edit.Tag = AdminValidators.IsStrong(node.ConfigETag); edit.Click += async (_, _) => { if (await Settled(node) is { } current) await EditNode(current.Type, current); }; actions.Children.Add(edit);
        // A node the deployment configuration owns cannot be deleted here. The button stays focusable (never disabled, so UI Automation can still invoke it) and a press shows the reason instead of a confirmation.
        var delete = IconAction(T("web.admin.nodes.unit.delete", new { name }), "NodeDelete_" + node.Id, "trash-2"); delete.Tag = AdminValidators.IsStrong(node.ConfigETag);
        if (node.IsDeploymentManaged) { delete.Opacity = .5; ToolTipService.SetToolTip(delete, AdminPlaybackNodesController.DeploymentManagedNote); AutomationProperties.SetHelpText(delete, AdminPlaybackNodesController.DeploymentManagedNote); }
        delete.Click += async (_, _) =>
        {
            if (node.IsDeploymentManaged) { if (_controller is { } managed) await managed.DeleteAsync(node); return; }
            if (await Settled(node) is { } current) await DeleteNode(current);
        }; actions.Children.Add(delete);
        var header = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.RowDefinitions.Add(new() { Height = GridLength.Auto }); header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        header.Children.Add(details); header.Children.Add(actions); Grid.SetColumn(actions, 1); actions.HorizontalAlignment = HorizontalAlignment.Right; actions.VerticalAlignment = VerticalAlignment.Top;
        // Official flex-wrap: the actions drop under the identity when the two no longer fit side by side.
        header.SizeChanged += (_, args) => { var narrow = args.NewSize.Width < 700; Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumnSpan(details, narrow ? 2 : 1); Grid.SetColumnSpan(actions, narrow ? 2 : 1); actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right; };
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(header);
        if (!node.Enabled) content.Children.Add(U.Text(T("web.admin.nodes.unit.disabledNote"), 12, true));
        if (drain != null) content.Children.Add(DrainLine(drain));
        // ---- blocks ----
        var blocks = new Grid { ColumnSpacing = 32, RowSpacing = 20 }; var parts = new List<FrameworkElement>();
        if (AdminPlaybackNodePresentation.ReportsAcceleration(node)) parts.Add(Acceleration(node, all, sharedGpus, now));
        parts.Add(LoadBlock(node, now)); parts.Add(CapacityBlock(node, now));
        foreach (var part in parts) blocks.Children.Add(part);
        var applied = 0;
        void Reflow()
        {
            var width = XamlRoot?.Size.Width ?? 1440; var columns = width >= 1280 ? parts.Count : width >= 640 ? Math.Min(2, parts.Count) : 1;
            if (columns == applied) return; applied = columns;
            blocks.ColumnDefinitions.Clear(); blocks.RowDefinitions.Clear();
            for (var c = 0; c < columns; c++) blocks.ColumnDefinitions.Add(new() { Width = new(parts.Count == 3 && columns == 3 ? (c == 0 ? 1.1 : c == 1 ? 1 : .85) : 1, GridUnitType.Star) });
            for (var r = 0; r < (parts.Count + columns - 1) / columns; r++) blocks.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var i = 0; i < parts.Count; i++) { Grid.SetColumn(parts[i], i % columns); Grid.SetRow(parts[i], i / columns); }
        }
        blocks.Loaded += (_, _) => Reflow(); blocks.SizeChanged += (_, _) => Reflow(); Reflow();
        var divider = new Border { BorderBrush = U.Brush("BorderBrush"), BorderThickness = new(0, 1, 0, 0), Padding = new(0, 16, 0, 0), Margin = new(0, 4, 0, 0), Child = blocks }; content.Children.Add(divider);
        if (node.NetworkAccess is { Count: > 0 } providers)
        {
            // Not on the official page: the provider status a node reports, with the link to the Network Access page that owns enrollment.
            foreach (var (provider, providerStatus) in providers.Take(32)) content.Children.Add(U.Text(P(provider) + " · " + P(providerStatus.State) + " · " + P(providerStatus.Hostname), 12, true));
            var network = U.Action(AdminText.T("web.admin.settings.search.networkAccess.label"), "NodeNetwork_" + node.Id);
            network.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/settings/network-access"); content.Children.Add(network);
        }
        var rail = new Border { Width = 3, CornerRadius = new(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Margin = new(0, 16, 0, 16), Background = new SolidColorBrush(railColor) { Opacity = railOpacity }, IsHitTestVisible = false };
        var root = new Grid(); root.Children.Add(new Border { Child = content, Padding = new(24, 20, 20, 20) }); root.Children.Add(rail);
        return new Border { Child = root, CornerRadius = new(20), Background = U.Brush("SurfaceBrush"), Opacity = node.Enabled ? 1 : .65 };
        void AddAction(string command, string id, string key, string? titleKey)
        {
            var button = IconAction(T(key, new { name }), id + node.Id, command switch { "check" => "refresh-cw", "reprobe" => "scan-search", "resume" => "circle-play", _ => "circle-pause" }); if (command == "reprobe") button.Tag = node.Id;
            if (titleKey != null) ToolTipService.SetToolTip(button, T(titleKey));
            button.Click += async (_, _) =>
            {
                if (await Settled(node) is not { } current || _controller is not { } controller) return;
                await controller.CommandAsync(current, command);
                // A re-probe does not hold the controller busy, so the rows are refreshed here once it has ended. A refusal changed nothing and keeps its message.
                if (command != "reprobe" || !ReferenceEquals(controller, _controller) || controller.State.Error != null) return;
                if (_pollReload is { } poll) await poll;
                await Refresh();
            }; actions.Children.Add(button);
        }
    }
    private FrameworkElement PublicUrl(AdminPlaybackNode node)
    {
        // The sentence follows the interface direction; the address inside it always reads left to right.
        const string marker = "\u0001"; var sentence = T("web.admin.nodes.unit.publicUrl", new { url = marker }); var at = sentence.IndexOf(marker, StringComparison.Ordinal);
        var address = P(node.PublicUrl, 2000);
        if (at < 0) return Mono(sentence.Replace(marker, address));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        // Spaces at the edge of a text block are not drawn, so the gap around the address is a margin.
        if (at > 0) { var prefix = Mono(sentence[..at].TrimEnd()); prefix.Margin = new(0, 0, 6, 0); row.Children.Add(prefix); }
        row.Children.Add(Mono(address));
        if (at + marker.Length < sentence.Length) { var suffix = Mono(sentence[(at + marker.Length)..].TrimStart()); suffix.Margin = new(6, 0, 0, 0); row.Children.Add(suffix); }
        ToolTipService.SetToolTip(row, T("web.admin.nodes.unit.publicUrlTitle")); return row;
    }
    private FrameworkElement KeyLine(AdminPlaybackNode node)
    {
        var issued = node.KeyIssued == true; var row = new LogsWrapPanel { Spacing = 8, RowSpacing = 4 };
        var color = issued ? U.Brush("SecondaryTextBrush") : new SolidColorBrush(Warning);
        var icon = AdminPlaybackNodeIcons.Icon("key-round", 12, color); icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
        // A deployment-managed node's key comes from the server's deployment configuration: that is all the line says, and it offers no Issue or Rotate.
        var text = U.Text(T(node.IsDeploymentManaged ? "web.admin.nodes.key.deploymentManaged" : issued ? "web.admin.nodes.key.issued" : "web.admin.nodes.key.missing"), 12, issued); if (!issued) { text.Foreground = color; text.FontWeight = Microsoft.UI.Text.FontWeights.Medium; }
        row.Children.Add(text);
        if (!node.IsDeploymentManaged && _controller!.State.Capabilities is { Available: true, NodeKeys: true })
        {
            var name = P(node.Name); var key = U.Action(T(issued ? "web.admin.nodes.key.rotate" : "web.admin.nodes.key.issue"), "NodeKey_" + node.Id); key.Height = 22; key.MinHeight = 22; key.Padding = new(0); key.FontSize = 12; key.BorderThickness = new(0); key.Foreground = U.Brush("PrimaryTextBrush");
            AutomationProperties.SetName(key, T(issued ? "web.admin.nodes.key.rotateFor" : "web.admin.nodes.key.issueFor", new { name }));
            key.Click += async (_, _) => { if (await Settled(node) is { } current) await ShowKey(current); }; row.Children.Add(key);
        }
        return new Border { Child = row, Margin = new(0, 6, 0, 0) };
    }
    private static FrameworkElement DrainLine(AdminPlaybackDrainLine drain)
    {
        var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        foreach (var run in DashboardText.Rich("web.admin.nodes.drain.statusLine", ("label", drain.Label), ("detail", drain.Detail)))
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = run.Text, FontWeight = run.Tag == "status" ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Normal, Foreground = run.Tag == "status" ? new SolidColorBrush(drain.Phase == "drained" ? Info : Warning) : U.Brush(run.Tag == "muted" ? "SecondaryTextBrush" : "PrimaryTextBrush") });
        ToolTipService.SetToolTip(text, drain.Detail); return text;
    }
    private static Button IconAction(string label, string id, string icon)
    {
        var button = U.Action(label, id); button.Content = AdminPlaybackNodeIcons.Icon(icon, 12, U.Brush("PrimaryTextBrush")); button.Width = 28; button.Height = 28; button.MinHeight = 28; button.Padding = new(0); button.CornerRadius = new(10); ToolTipService.SetToolTip(button, label); return button;
    }
    private static FrameworkElement LoadBlock(AdminPlaybackNode node, DateTimeOffset now)
    {
        var system = AdminPlaybackNodePresentation.Host(node, now); FrameworkElement body;
        if (!system.Reported) { var note = U.Text(T(system.Stale ? "web.admin.nodes.unit.staleSample" : "web.admin.nodes.unit.noSample"), 12, true); ToolTipService.SetToolTip(note, system.Title); body = note; }
        else body = MetricGrid(system.Rows);
        var children = new List<FrameworkElement> { body };
        if (node.LastStats?.Build?.Display is { Length: > 0 } build) children.Add(Mono(P(build, 120), 11));
        return Block(T("web.admin.nodes.unit.load"), [.. children]);
    }
    private static FrameworkElement CapacityBlock(AdminPlaybackNode node, DateTimeOffset now)
    {
        var rows = new List<DashboardMetric> { AdminPlaybackNodePresentation.Jobs(node) };
        if (node.Type == "proxy") rows.Add(AdminPlaybackNodePresentation.Egress(node));
        var grid = (Grid)MetricGrid(rows); var checkedAt = DateTimeOffset.TryParse(node.LastHealthCheck, out var parsed) ? parsed : (DateTimeOffset?)null; var row = rows.Count;
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = U.Text(T("web.admin.nodes.unit.checked"), 11, true); Grid.SetRow(label, row); grid.Children.Add(label);
        var value = new TextBlock { Text = AdminPlaybackAuditText.Relative(node.LastHealthCheck, now) ?? T("web.admin.nodes.unit.never"), FontFamily = new("Consolas"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right, Foreground = U.Brush("PrimaryTextBrush") };
        if (checkedAt is { } at) ToolTipService.SetToolTip(value, SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(at));
        Grid.SetRow(value, row); Grid.SetColumn(value, 2); grid.Children.Add(value);
        return Block(T("web.admin.nodes.unit.capacity"), grid);
    }
    private static FrameworkElement Acceleration(AdminPlaybackNode node, IReadOnlyList<AdminPlaybackNode> all, HashSet<string> sharedGpus, DateTimeOffset now)
    {
        var gpu = AdminPlaybackNodePresentation.Gpu(node, now); var body = new StackPanel { Spacing = 8 }; var top = new LogsWrapPanel { Spacing = 6, RowSpacing = 6 };
        var drift = AdminPlaybackNodePresentation.Drift(node); var over = AdminPlaybackNodePresentation.Override(node);
        FrameworkElement DriftBadge() => Badge(drift!.Label, Warning, drift.Title);
        if (gpu.Awaiting)
        {
            var label = U.Text(gpu.AwaitingLabel, 12, true); ToolTipService.SetToolTip(label, gpu.AwaitingTitle); top.Children.Add(label);
            if (drift != null) top.Children.Add(DriftBadge()); body.Children.Add(top);
        }
        else
        {
            var tint = gpu.Backend!.State switch { AdminPlaybackBackendState.Verified => (Windows.UI.Color?)Success, AdminPlaybackBackendState.Failed => Warning, _ => null };
            top.Children.Add(Badge(gpu.Backend.Label, tint, gpu.Backend.Title));
            if (drift != null) top.Children.Add(DriftBadge());
            if (sharedGpus.Count > 0 && AdminPlaybackNodePresentation.SharedGpu(node, all) is { } shared) top.Children.Add(Badge(shared.Label, null, shared.Title));
            if (gpu.Failures.Count > 0)
            {
                var warning = AdminPlaybackNodeIcons.Icon("alert-triangle", 14, new SolidColorBrush(Warning)); warning.VerticalAlignment = VerticalAlignment.Center;
                ToolTipService.SetToolTip(warning, string.Join("\n", gpu.Failures.Select(failure => T("web.admin.nodes.unit.probeFailure", new { backend = failure.Label, reason = failure.Reason}))));
                AutomationProperties.SetName(warning, T("web.admin.nodes.unit.probeFailureCount", new { count = gpu.Failures.Count, name = P(node.Name) })); top.Children.Add(warning);
            }
            if (gpu.Stale != null) { var stale = U.Text(T("web.admin.nodes.unit.stale"), 12, true); ToolTipService.SetToolTip(stale, AdminPlaybackNodePresentation.StaleTitle(gpu.Stale, node)); top.Children.Add(stale); }
            body.Children.Add(top);
            if (gpu.DeviceSummary != null) { var summary = U.Text(gpu.DeviceSummary, 12, true); if (gpu.DeviceTitle != null) ToolTipService.SetToolTip(summary, gpu.DeviceTitle); body.Children.Add(summary); }
        }
        if (over != null) { var line = U.Text(over.Label, 12, true); ToolTipService.SetToolTip(line, over.Title); body.Children.Add(line); }
        if (!gpu.Awaiting && gpu.Live.Count > 0)
        {
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8, Margin = new(0, 2, 0, 0) };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 32 }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            for (var i = 0; i < gpu.Live.Count; i++)
            {
                var device = gpu.Live[i]; grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var name = Mono(device.Label, 11); name.MaxWidth = 112; var busy = new TextBlock { Text = device.Busy, FontFamily = new("Consolas"), FontSize = 11, TextAlignment = TextAlignment.Right, Foreground = U.Brush(device.BusyMuted ? "SecondaryTextBrush" : "PrimaryTextBrush"), VerticalAlignment = VerticalAlignment.Center };
                var sessions = new TextBlock { Text = device.Sessions, FontSize = 10, Foreground = U.Brush("SecondaryTextBrush"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
                var meter = DashboardLoadMeter.Create(device.BusyFill);
                var cells = new FrameworkElement[] { name, meter, busy, sessions };
                for (var c = 0; c < cells.Length; c++) { Grid.SetRow(cells[c], i); Grid.SetColumn(cells[c], c); grid.Children.Add(cells[c]); if (c != 1) ToolTipService.SetToolTip(cells[c], device.Title); }
            }
            body.Children.Add(grid);
        }
        return Block(T("web.admin.nodes.unit.acceleration"), body);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    public async Task<bool> CanLeaveAsync()
    {
        if (_dialog != null || _routingPanel?.IsDialogOpen == true || _controller?.State.Busy == true || _routing?.State.Saving == true) return false;
        return _leave == null || await _leave.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog));
    }
    public void SuspendAdminPage()
    {
        if (!_active) return; _active = false; _poll.Stop(); _pollReload = null; _dialog?.Hide(); _dialog = null;
        _authority.AvailabilityChanged -= AuthorityChanged;
        if (App.MainWindowInstance is { } window) { window.Activated -= Activated; window.VisibilityChanged -= VisibilityChanged; }
        _controller?.Dispose(); _controller = null; _routingPanel?.Dispose(); if (_routingPanel != null) { _root.Children.Remove(_routingPanel.SaveBar); _body.Children.Remove(_routingPanel); } if (_routingHeading != null) _body.Children.Remove(_routingHeading); _routingHeading = null; _routingPanel = null;
        _routing?.Dispose(); _routing = null; _cards.Children.Clear(); _groups.Children.Clear(); _rendered = null; _status.Text = "";
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { SuspendAdminPage(); base.OnNavigatedFrom(args); }
}
