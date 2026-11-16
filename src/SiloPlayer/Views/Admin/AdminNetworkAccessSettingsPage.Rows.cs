using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Windows.Foundation;
using Windows.UI;
using U = SiloPlayer.Controls.AdminSettingsUi;
using X = SiloPlayer.Core.Services.Admin.AdminNetworkAccessText;

namespace SiloPlayer.Views.Admin;

internal enum NetworkAccessLink { Origin, Enrollment }

/// <summary>Shared Network access primitives: skeleton fractions and the WebUI small button (h-8, rounded-md).</summary>
internal static class NetworkAccessUi
{
    internal static FrameworkElement FractionSkeleton(double height, double fraction)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new(fraction, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(1 - fraction, GridUnitType.Star) });
        grid.Children.Add(U.Skeleton(height));
        return grid;
    }

    /// <summary>
    /// The official <c>Button size="sm"</c>: 32 high, 12 side padding (10 with an icon), 6 gap, radius-md (10 at the
    /// 0.75rem theme radius). A local template keeps the rectangle in every state so app-wide pill styles never apply.
    /// </summary>
    internal static (Button Button, Border Icon, TextBlock Label) SmallButton(string label, string id, bool filled)
    {
        var primary = AdminMonitorUi.ColorOf("PrimaryTextColor", Colors.White);
        var hover = filled ? $"#E6{primary.R:X2}{primary.G:X2}{primary.B:X2}" : "{ThemeResource MutedBrush}";
        var template = (ControlTemplate)XamlReader.Load($$"""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="SettingsButtonRoot" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
                  <VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="SettingsButtonRoot.Background" Value="{{hover}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="SettingsButtonRoot.Background" Value="{{hover}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="SettingsButtonRoot.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
                </VisualStateGroup></VisualStateManager.VisualStateGroups>
                <ContentPresenter Content="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}" FontSize="{TemplateBinding FontSize}"
                  FontWeight="{TemplateBinding FontWeight}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        var icon = new Border { Width = 16, Height = 16, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(icon); content.Children.Add(text);
        var button = new Button
        {
            Content = content, Template = template, MinWidth = 0, MinHeight = 32, Height = 32, Padding = new(12, 0, 12, 0),
            CornerRadius = new(10), FontSize = 14, FontWeight = FontWeights.Medium, UseSystemFocusVisuals = true,
            Background = filled ? U.Brush("PrimaryTextBrush") : new SolidColorBrush(Colors.Transparent),
            Foreground = filled ? U.Brush("AppBackgroundBrush") : U.Brush("PrimaryTextBrush"),
            BorderBrush = filled ? new SolidColorBrush(Colors.Transparent) : U.Brush("BorderBrush"), BorderThickness = new(filled ? 0 : 1),
            FocusVisualPrimaryBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("FocusVisualPrimaryBrush", Colors.White), .5),
            FocusVisualPrimaryThickness = new(3), FocusVisualSecondaryBrush = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        return (button, icon, text);
    }
}

/// <summary>One provider's FieldGroup: skeleton, error, empty message or the host list (official ProviderGroup).</summary>
internal sealed class NetworkAccessProviderView
{
    private readonly Action<string, string, AdminNetworkAccessCommandKind> _command;
    private readonly Action<string, string, NetworkAccessLink> _link;
    private readonly StackPanel _skeleton = new() { Spacing = 12 };
    private readonly TextBlock _error = U.Text("", 14);
    private readonly TextBlock _noHosts = U.Text(X.T("web.admin.settings.network.provider.noHosts"), 14, true);
    private readonly StackPanel _list = new();
    private readonly Dictionary<string, NetworkAccessHostRow> _rows = new(StringComparer.Ordinal);
    private readonly string _provider;
    internal AdminSettingsGroup Group { get; }

    internal NetworkAccessProviderView(AdminNetworkAccessProvider summary, Action<string, string, AdminNetworkAccessCommandKind> command,
        Action<string, string, NetworkAccessLink> link)
    {
        _provider = summary.Provider; _command = command; _link = link;
        Group = new AdminSettingsGroup(summary.DisplayName, X.T("web.admin.settings.network.provider.description", new { name = summary.DisplayName }));
        AutomationProperties.SetAutomationId(Group, "NetworkAccessProvider_" + summary.Provider);
        AutomationProperties.SetName(_skeleton, X.T("configure.native.network.loadingStatus"));
        AutomationProperties.SetLiveSetting(_skeleton, AutomationLiveSetting.Polite);
        _skeleton.Children.Add(NetworkAccessUi.FractionSkeleton(20, 2d / 3));
        _skeleton.Children.Add(NetworkAccessUi.FractionSkeleton(16, .5));
        _error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        AutomationProperties.SetAutomationId(_error, "NetworkAccessStatusError_" + summary.Provider);
        AutomationProperties.SetAutomationId(_noHosts, "NetworkAccessNoHosts_" + summary.Provider);
        AutomationProperties.SetAutomationId(_list, "NetworkAccessHosts_" + summary.Provider);
        foreach (var element in new FrameworkElement[] { _skeleton, _error, _noHosts, _list }) { element.Visibility = Visibility.Collapsed; Group.Fields.Children.Add(element); }
    }

    internal void Render(AdminNetworkAccessProviderState state)
    {
        Group.State(false, X.T("web.admin.settings.network.provider.description", new { name = state.Summary.DisplayName }));
        var hosts = state.Status?.Hosts ?? [];
        var mode = state.Loading ? 0 : state.Failure != null ? 1 : hosts.Count == 0 ? 2 : 3;
        _skeleton.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        _error.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        _noHosts.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (mode == 1) U.UpdateLiveText(_error, StatusMessage(state));
        // Rows are keyed by host id and updated in place, so polling never remounts a focused or hovered row.
        var wanted = hosts.Select(host => host.Host.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _rows.Keys.Where(id => !wanted.Contains(id)).ToArray()) { _list.Children.Remove(_rows[gone]); _rows.Remove(gone); }
        for (var index = 0; index < hosts.Count; index++)
        {
            var host = hosts[index];
            if (!_rows.TryGetValue(host.Host.Id, out var row)) { row = new NetworkAccessHostRow(_provider, host.Host.Id, _command, _link); _rows[host.Host.Id] = row; }
            var existing = _list.Children.IndexOf(row);
            if (existing != index)
            {
                if (existing >= 0) _list.Children.RemoveAt(existing);
                _list.Children.Insert(Math.Min(index, _list.Children.Count), row);
            }
            row.Render(host, state.BusyHosts.Contains(host.Host.Id), index == 0, index == hosts.Count - 1);
        }
    }

    private static string StatusMessage(AdminNetworkAccessProviderState state) => state.Limit is { } limit
        ? X.T("configure.native.network.limit", new { limit })
        : state.Failure == AdminFailureKind.NotFound ? X.T("configure.native.network.providerNotFound")
        : AdminNetworkAccessSettingsPage.FailureMessage(state.Failure) is { } message && state.Failure is not (AdminFailureKind.InvalidResponse or AdminFailureKind.Transport)
            ? message : X.T("web.admin.settings.network.provider.statusFailed");

    internal void Clear()
    {
        foreach (var row in _rows.Values) row.Clear();
        _rows.Clear(); _list.Children.Clear();
    }
}

/// <summary>
/// One host (official HostRow): label, state pill, version badge, reach/address/enrollment/error/last-heard lines and one
/// Connect or Disconnect action. Every element persists across renders; only changed text is replaced.
/// </summary>
internal sealed class NetworkAccessHostRow : Grid
{
    private static readonly Color Emerald500 = Color.FromArgb(255, 0x10, 0xB9, 0x81);
    private readonly string _provider, _hostId;
    private readonly Action<string, string, AdminNetworkAccessCommandKind> _command;
    private readonly Action<string, string, NetworkAccessLink> _link;
    private readonly NetworkAccessFlexRow _flex;
    private readonly StackPanel _info = new() { Spacing = 6, MinWidth = 0 };
    private readonly NetworkAccessFlowLine _headline = new(12, 4);
    private readonly TextBlock _name = U.Text("", 16);
    private readonly StackPanel _pill = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Border _dot = new() { Width = 8, Height = 8, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _stateText = U.Text("", 14);
    private readonly Border _badge = new() { CornerRadius = new(10), BorderThickness = new(1), Padding = new(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _badgeText = U.Text("", 12);
    private readonly TextBlock _reach = U.Text("", 14, true);
    private readonly TextBlock _addresses = U.Text("", 12, true);
    private readonly TextBlock _authorize = U.Text("", 14);
    private readonly TextBlock _error = U.Text("", 14);
    private readonly TextBlock _lastHeard = U.Text("", 12, true);
    private readonly Border _actionHost = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly (Button Button, Border Icon, TextBlock Label) _connect, _disconnect;
    private AdminNetworkAccessHostStatus? _rendered;
    private bool? _renderedBusy;
    private bool _spinning, _restoreFocus, _authorizeShown;
    private WeakReference<DependencyObject>? _displaced;

    internal string HostId => _hostId;
    internal Button ConnectButton => _connect.Button;
    internal Button DisconnectButton => _disconnect.Button;

    internal NetworkAccessHostRow(string provider, string hostId, Action<string, string, AdminNetworkAccessCommandKind> command,
        Action<string, string, NetworkAccessLink> link)
    {
        _provider = provider; _hostId = hostId; _command = command; _link = link;
        AutomationProperties.SetAutomationId(this, $"NetworkAccessHost_{provider}_{hostId}");
        BorderBrush = U.Brush("BorderBrush");
        _name.FontWeight = FontWeights.Medium; _name.LineHeight = 24;
        _stateText.LineHeight = 20;
        _pill.Children.Add(_dot); _pill.Children.Add(_stateText);
        AutomationProperties.SetAccessibilityView(_dot, AccessibilityView.Raw);
        _badge.BorderBrush = U.Brush("BorderBrush");
        _badgeText.LineHeight = 16; _badgeText.TextWrapping = TextWrapping.NoWrap;
        _badge.Child = _badgeText;
        _headline.Children.Add(_name); _headline.Children.Add(_pill); _headline.Children.Add(_badge);
        _info.Children.Add(_headline);
        foreach (var line in new[] { _reach, _addresses, _authorize, _error, _lastHeard }) { line.Visibility = Visibility.Collapsed; _info.Children.Add(line); }
        _addresses.LineHeight = _lastHeard.LineHeight = 16;
        _error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_error, $"NetworkAccessHostError_{provider}_{hostId}");
        foreach (var text in new[] { _reach, _authorize })
        {
            // Links keep the text colour in every state, as the WebUI anchor does.
            text.Resources["HyperlinkForegroundPointerOver"] = U.Brush("PrimaryTextBrush");
            text.Resources["HyperlinkForegroundPressed"] = U.Brush("PrimaryTextBrush");
            text.IsTextSelectionEnabled = false;
        }
        _connect = NetworkAccessUi.SmallButton(X.T("web.admin.settings.network.host.connect"), $"NetworkAccessConnect_{provider}_{hostId}", filled: true);
        _disconnect = NetworkAccessUi.SmallButton(X.T("web.admin.settings.network.host.disconnect"), $"NetworkAccessDisconnect_{provider}_{hostId}", filled: false);
        _connect.Button.Click += (_, _) => Invoke(AdminNetworkAccessCommandKind.Connect);
        _disconnect.Button.Click += (_, _) => Invoke(AdminNetworkAccessCommandKind.Disconnect);
        _actions.Children.Add(_disconnect.Button); _actions.Children.Add(_connect.Button);
        _actionHost.Child = _actions;
        _flex = new NetworkAccessFlexRow(_info, _actionHost);
        Children.Add(_flex);
    }

    private void Invoke(AdminNetworkAccessCommandKind kind)
    {
        var button = kind == AdminNetworkAccessCommandKind.Connect ? _connect.Button : _disconnect.Button;
        // Keep the keyboard position: the busy action disables, then focus returns to this row's action.
        _restoreFocus = button.FocusState != FocusState.Unfocused;
        _command(_provider, _hostId, kind);
    }

    internal void Render(AdminNetworkAccessHostStatus host, bool busy, bool first, bool last)
    {
        Padding = new(0, first ? 0 : 16, 0, last ? 0 : 16);
        BorderThickness = new(0, first ? 0 : 1, 0, 0);
        if (host.SameAs(_rendered) && _renderedBusy == busy) return;
        var label = X.HostLabel(host.Host);
        Set(_name, label);
        AutomationProperties.SetName(this, label);
        Set(_stateText, X.State(host.State));
        _dot.Background = AdminMonitorUi.Solid(host.State switch
        {
            AdminNetworkAccessHostStatus.AwaitingAuthorization or AdminNetworkAccessHostStatus.Connecting => AdminMonitorUi.Amber500,
            AdminNetworkAccessHostStatus.Connected => Emerald500,
            AdminNetworkAccessHostStatus.ErrorState => AdminMonitorUi.Destructive,
            _ => AdminMonitorUi.Muted,
        }, host.State is AdminNetworkAccessHostStatus.AwaitingAuthorization or AdminNetworkAccessHostStatus.Connecting or AdminNetworkAccessHostStatus.Connected or AdminNetworkAccessHostStatus.ErrorState ? 1 : .4);
        _badge.Visibility = host.ProviderVersion is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        Set(_badgeText, host.ProviderVersion ?? "");

        if (_rendered?.Origin != host.Origin || _rendered?.Hostname != host.Hostname) RenderReach(host);
        _addresses.Visibility = host.Addresses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Set(_addresses, string.Join(", ", host.Addresses));
        var waiting = host.State == AdminNetworkAccessHostStatus.AwaitingAuthorization && !string.IsNullOrEmpty(host.AuthUrl);
        if (waiting && !_authorizeShown) RenderAuthorize();
        else if (!waiting && _authorizeShown) { _authorize.Inlines.Clear(); _authorizeShown = false; }
        _authorize.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        _error.Visibility = host.Error is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (host.Error is { Length: > 0 } error) U.UpdateLiveText(_error, error); else _error.Text = "";
        _lastHeard.Visibility = host.UpdatedAt != null ? Visibility.Visible : Visibility.Collapsed;
        Set(_lastHeard, host.UpdatedAt is { } at ? X.T("web.admin.settings.network.host.lastHeard", new { time = FormatHeard(at) }) : "");

        RenderActions(host, busy, label);
        _rendered = host; _renderedBusy = busy;
    }

    /// <summary>Official formatDateTime(value, { seconds: false }): "date, time" under the display preferences.</summary>
    private static string FormatHeard(DateTimeOffset at) => $"{DateTimeDisplay.FormatDate(at)}, {DateTimeDisplay.FormatTime(at)}";

    private void RenderReach(AdminNetworkAccessHostStatus host)
    {
        _reach.Inlines.Clear();
        if (host.Origin is { Length: > 0 } origin)
        {
            var key = host.Hostname is { Length: > 0 } name && name != origin ? "web.admin.settings.network.host.reachAtWithHostname" : "web.admin.settings.network.host.reachAt";
            foreach (var run in X.Rich(key, new { origin, hostname = host.Hostname ?? "" }))
            {
                // Only a safe HTTP(S) origin becomes a link; anything else is shown as text and cannot be activated.
                if (run.Tag != "link" || !ExternalBrowserUrlPolicy.TryGetSafeUri(origin, out _)) { _reach.Inlines.Add(new Run { Text = run.Text }); continue; }
                var link = new Hyperlink { UnderlineStyle = UnderlineStyle.Single, Foreground = U.Brush("PrimaryTextBrush") };
                link.Inlines.Add(new Run { Text = run.Text });
                AutomationProperties.SetName(link, X.T("configure.native.network.openOrigin", new { origin = run.Text }));
                AutomationProperties.SetAutomationId(link, $"NetworkAccessOrigin_{_provider}_{_hostId}");
                link.Click += (_, _) => _link(_provider, _hostId, NetworkAccessLink.Origin);
                _reach.Inlines.Add(link);
            }
            AutomationProperties.SetName(_reach, string.Concat(X.Rich(key, new { origin, hostname = host.Hostname ?? "" }).Select(run => run.Text)));
            _reach.Visibility = Visibility.Visible;
        }
        else if (host.Hostname is { Length: > 0 } hostname)
        {
            _reach.Inlines.Add(new Run { Text = hostname });
            AutomationProperties.SetName(_reach, hostname);
            _reach.Visibility = Visibility.Visible;
        }
        else _reach.Visibility = Visibility.Collapsed;
    }

    private void RenderAuthorize()
    {
        _authorize.Inlines.Clear();
        _authorizeShown = true;
        foreach (var run in X.Rich("web.admin.settings.network.host.authorize"))
        {
            if (run.Tag == "link")
            {
                var link = new Hyperlink { UnderlineStyle = UnderlineStyle.Single, Foreground = U.Brush("PrimaryTextBrush"), FontWeight = FontWeights.Medium };
                link.Inlines.Add(new Run { Text = run.Text });
                // Lucide ExternalLink (size-3.5) after the label, decorative.
                link.Inlines.Add(new Run { Text = " ", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12 });
                AutomationProperties.SetName(link, run.Text);
                AutomationProperties.SetAutomationId(link, $"NetworkAccessAuthorize_{_provider}_{_hostId}");
                link.Click += (_, _) => _link(_provider, _hostId, NetworkAccessLink.Enrollment);
                _authorize.Inlines.Add(link);
            }
            else _authorize.Inlines.Add(new Run { Text = run.Text, Foreground = U.Brush("SecondaryTextBrush") });
        }
        AutomationProperties.SetName(_authorize, string.Concat(X.Rich("web.admin.settings.network.host.authorize").Select(run => run.Text)));
    }

    private void RenderActions(AdminNetworkAccessHostStatus host, bool busy, string label)
    {
        var disconnect = host.ShowsDisconnect;
        var unavailable = host.State == AdminNetworkAccessHostStatus.Unavailable;
        var focused = _connect.Button.FocusState != FocusState.Unfocused ? _connect.Button.FocusState
            : _disconnect.Button.FocusState != FocusState.Unfocused ? _disconnect.Button.FocusState : FocusState.Unfocused;
        _disconnect.Button.Visibility = disconnect ? Visibility.Visible : Visibility.Collapsed;
        _connect.Button.Visibility = disconnect ? Visibility.Collapsed : Visibility.Visible;
        _disconnect.Button.IsEnabled = !busy;
        _connect.Button.IsEnabled = !busy && !unavailable;
        AutomationProperties.SetName(_connect.Button, X.T("configure.native.network.connectHost", new { host = label }));
        AutomationProperties.SetName(_disconnect.Button, X.T("configure.native.network.disconnectHost", new { host = label }));
        var hint = unavailable ? X.T("web.admin.settings.network.host.startPluginFirst") : "";
        AutomationProperties.SetHelpText(_connect.Button, hint);
        // A disabled button receives no pointer input, so the title lives on its wrapper (official title attribute).
        ToolTipService.SetToolTip(_actionHost, unavailable && !disconnect ? hint : null);
        var started = busy && !_spinning;
        if (_spinning != busy)
        {
            _spinning = busy;
            var visible = disconnect ? _disconnect : _connect;
            var hidden = disconnect ? _connect : _disconnect;
            hidden.Icon.Child = null; hidden.Icon.Visibility = Visibility.Collapsed; hidden.Button.Padding = new(12, 0, 12, 0);
            visible.Icon.Child = busy ? AdminMonitorUi.Spinner(16, ((SolidColorBrush)visible.Button.Foreground).Color, "loader-circle") : null;
            visible.Icon.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            visible.Button.Padding = busy ? new(10, 0, 10, 0) : new(12, 0, 12, 0);
        }
        // Keep the keyboard position on this row. Disabling the busy action moves focus to the next stop; remember where,
        // and return only if the user has not moved it since. A Connect/Disconnect switch keeps focus on the new action.
        var target = disconnect ? _disconnect.Button : _connect.Button;
        if (started && _restoreFocus)
            DispatcherQueue.TryEnqueue(() => _displaced = XamlRoot == null ? null : new(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject ?? this));
        var switched = focused != FocusState.Unfocused && target.FocusState == FocusState.Unfocused;
        if (target.IsEnabled && (switched || _restoreFocus && !busy))
        {
            var fromCommand = !switched;
            _restoreFocus = false;
            var state = focused == FocusState.Unfocused ? FocusState.Keyboard : focused;
            var displaced = _displaced; _displaced = null;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!target.IsLoaded || !target.IsEnabled || target.Visibility != Visibility.Visible || XamlRoot == null) return;
                var now = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                var untouched = now == null || now is ScrollViewer or Page || displaced != null && displaced.TryGetTarget(out var moved) && ReferenceEquals(moved, now);
                if (!fromCommand || untouched) target.Focus(state);
            });
        }
    }

    private static void Set(TextBlock text, string value) { if (text.Text != value) text.Text = value; }

    internal void Clear()
    {
        _reach.Inlines.Clear(); _authorize.Inlines.Clear(); _authorizeShown = false;
        _connect.Icon.Child = null; _disconnect.Icon.Child = null;
        _rendered = null; _renderedBusy = null;
    }
}

/// <summary>
/// The row's <c>flex flex-wrap justify-between gap-x-6 gap-y-3</c>: details and actions share a line while the details'
/// natural width fits beside the actions, otherwise the actions wrap below, left-aligned.
/// </summary>
internal sealed class NetworkAccessFlexRow : Panel
{
    private readonly FrameworkElement _info, _actions;
    private bool _wrap;
    internal NetworkAccessFlexRow(FrameworkElement info, FrameworkElement actions) { _info = info; _actions = actions; Children.Add(info); Children.Add(actions); }
    internal bool IsWrapped => _wrap;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 100000 : available.Width;
        _actions.Measure(new(width, double.PositiveInfinity));
        _info.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var natural = _info.DesiredSize.Width;
        var actions = _actions.DesiredSize;
        _wrap = actions.Width > 0 && natural + 24 + actions.Width > width;
        var infoWidth = _wrap || actions.Width == 0 ? width : Math.Max(0, width - 24 - actions.Width);
        _info.Measure(new(infoWidth, double.PositiveInfinity));
        var height = _wrap ? _info.DesiredSize.Height + 12 + actions.Height : Math.Max(_info.DesiredSize.Height, actions.Height);
        return new(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var actions = _actions.DesiredSize;
        if (_wrap)
        {
            _info.Arrange(new(0, 0, final.Width, _info.DesiredSize.Height));
            _actions.Arrange(new(0, _info.DesiredSize.Height + 12, actions.Width, actions.Height));
        }
        else
        {
            _info.Arrange(new(0, 0, Math.Max(0, final.Width - (actions.Width > 0 ? 24 + actions.Width : 0)), _info.DesiredSize.Height));
            _actions.Arrange(new(Math.Max(0, final.Width - actions.Width), 0, actions.Width, actions.Height));
        }
        return final;
    }
}

/// <summary><c>flex flex-wrap items-center</c>: children flow on lines and centre vertically on each line.</summary>
internal sealed class NetworkAccessFlowLine(double columnGap, double rowGap) : Panel
{
    private readonly List<(int Start, int End, double Height)> _lines = [];

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width;
        _lines.Clear();
        double x = 0, lineHeight = 0, total = 0, widest = 0;
        var start = 0;
        var visible = Children.Where(child => child.Visibility == Visibility.Visible).ToList();
        for (var index = 0; index < visible.Count; index++)
        {
            var child = visible[index];
            child.Measure(new(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (index > start && x + columnGap + size.Width > width)
            {
                _lines.Add((start, index, lineHeight)); total += lineHeight + rowGap; widest = Math.Max(widest, x);
                start = index; x = 0; lineHeight = 0;
            }
            x += (index > start ? columnGap : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        if (visible.Count > 0) { _lines.Add((start, visible.Count, lineHeight)); total += lineHeight; widest = Math.Max(widest, x); }
        return new(double.IsInfinity(width) ? widest : Math.Min(widest, width), total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var visible = Children.Where(child => child.Visibility == Visibility.Visible).ToList();
        foreach (var child in Children.Where(child => child.Visibility != Visibility.Visible)) child.Arrange(new(0, 0, 0, 0));
        double y = 0;
        foreach (var (start, end, height) in _lines)
        {
            double x = 0;
            for (var index = start; index < end && index < visible.Count; index++)
            {
                var size = visible[index].DesiredSize;
                if (index > start) x += columnGap;
                var w = Math.Min(size.Width, final.Width);
                visible[index].Arrange(new(x, y + (height - size.Height) / 2, w, size.Height));
                x += w;
            }
            y += height + rowGap;
        }
        return final;
    }
}
