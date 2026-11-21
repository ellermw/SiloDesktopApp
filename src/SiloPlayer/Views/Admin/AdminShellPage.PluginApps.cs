using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// WebUI AdminSidebar "Plugin Apps": <c>buildAdminPluginNavItems</c> over the admin installations
/// inventory, shown as the last sidebar section only when it has entries, and appended as the
/// last command-search group (<c>appendAdminPluginNavSection</c>). Each entry is an external
/// link (<c>navigateToPluginRoute</c>): it leaves the Admin shell for the plugin's own page.
/// </summary>
public sealed partial class AdminShellPage
{
    // lucide 0.576.0 icons/puzzle.svg (the WebUI's Puzzle icon for every plugin app).
    private const string PuzzleIconPath = "M15.39 4.39a1 1 0 0 0 1.68-.474 2.5 2.5 0 1 1 3.014 3.015 1 1 0 0 0-.474 1.68l1.683 1.682a2.414 2.414 0 0 1 0 3.414L19.61 15.39a1 1 0 0 1-1.68-.474 2.5 2.5 0 1 0-3.014 3.015 1 1 0 0 1 .474 1.68l-1.683 1.682a2.414 2.414 0 0 1-3.414 0L8.61 19.61a1 1 0 0 0-1.68.474 2.5 2.5 0 1 1-3.014-3.015 1 1 0 0 0 .474-1.68l-1.683-1.682a2.414 2.414 0 0 1 0-3.414L4.39 8.61a1 1 0 0 1 1.68.474 2.5 2.5 0 1 0 3.014-3.015 1 1 0 0 1-.474-1.68l1.683-1.682a2.414 2.414 0 0 1 3.414 0z";
    private IReadOnlyList<AdminPluginApp> _pluginApps = [];
    private bool _pluginAppsFailed;
    private bool _pluginAppsLoading;
    private Action? _refreshPluginSearch;

    /// <summary>The Plugin Apps entries the sidebar and search currently offer (native regression fixture).</summary>
    internal IReadOnlyList<AdminPluginApp> PluginApps => _pluginApps;

    private void RetirePluginApps()
    {
        _pluginApps = [];
        _pluginAppsFailed = false;
        _pluginAppsLoading = false;
        // Detached sidebar buttons still hold their captured lease and fail closed.
        if (_active) RebuildSidebars();
    }

    private async Task LoadPluginAppsAsync(MonitorAuthorityLease lease)
    {
        if (_pluginAppsLoading || !_active || !lease.IsCurrent) return;
        _pluginAppsLoading = true;
        var apply = false;
        try
        {
            var api = App.Services.GetService<AdminPluginAppsApi>();
            if (api == null) return; // Minimal service providers may omit the extension.
            var apps = await api.GetAppsAsync(lease);
            if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent) return;
            _pluginApps = apps;
            _pluginAppsFailed = false;
            apply = true;
        }
        catch (Exception)
        {
            if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent) return;
            _pluginApps = [];
            _pluginAppsFailed = true;
            apply = true;
        }
        finally
        {
            if (ReferenceEquals(_lease, lease)) _pluginAppsLoading = false;
            if (apply)
            {
                RebuildSidebars();
                _refreshPluginSearch?.Invoke();
            }
        }
    }

    private static Viewbox PluginIcon(string data, double size, Brush brush)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(
            $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = brush;
        path.StrokeThickness = 2;
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    private FrameworkElement? BuildPluginAppsGroup(bool embedded)
    {
        // Like the WebUI, the section exists only when it has entries; a failed read still offers a retry.
        if (_lease is not { IsCurrent: true } lease || (_pluginApps.Count == 0 && !_pluginAppsFailed)) return null;
        var group = new StackPanel { Tag = "AdminPluginAppsGroup" };
        var heading = A.Eyebrow(AdminText.T("web.admin.shell.sidebar.pluginApps"), A.Muted, 180);
        heading.Margin = new Thickness(8, 0, 8, 8);
        group.Children.Add(heading);
        var rows = new StackPanel { Spacing = 2 };
        foreach (var app in _pluginApps)
        {
            // SideNavItem: 18px icon, then the label clamped to two lines (no external marker).
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(PluginIcon(PuzzleIconPath, 18, A.Solid(A.Muted)));
            var label = A.Text(app.Label, 13, A.Muted, FontWeights.Medium, trim: false);
            label.TextWrapping = TextWrapping.WrapWholeWords;
            label.MaxLines = 2;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var button = A.Plain(row, app.Label, new Thickness(12, 10, 12, 10));
            button.Tag = app;
            button.CornerRadius = new CornerRadius(12);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (embedded) button.MinHeight = 44;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "AdminPluginApp_" + app.InstallationId + "_" + Uri.EscapeDataString(app.RoutePath));
            ToolTipService.SetToolTip(button, AdminText.T("web.admin.shell.nav.pluginApp.description", new { plugin = app.PluginId }));
            button.Click += async (_, _) => await OpenPluginAppAsync(app, lease);
            rows.Children.Add(button);
        }
        if (_pluginAppsFailed)
        {
            var retry = A.Plain(A.Text(AdminText.T("admin.native.pluginApps.inventoryFailed"), 12, A.Muted, trim: false), AdminText.T("admin.native.pluginApps.reload"), new Thickness(12));
            retry.Tag = "AdminPluginAppsRetry";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(retry, "AdminPluginAppsRetry");
            retry.Click += async (_, _) => { if (ReferenceEquals(_lease, lease) && lease.IsCurrent) await LoadPluginAppsAsync(lease); };
            rows.Children.Add(retry);
        }
        group.Children.Add(rows);
        return group;
    }

    /// <summary>The command search's Plugin Apps group: label, description and the WebUI keywords.</summary>
    private IReadOnlyList<AdminPluginApp> SearchPluginApps(string query)
    {
        if (_lease?.IsCurrent != true) return [];
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _pluginApps.Where(app => terms.All(term =>
            string.Join(' ', app.Label, AdminText.T("web.admin.shell.nav.pluginApp.description", new { plugin = app.PluginId }),
                    app.PluginId, "plugin", "plugin app")
                .Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
    }

    private async Task OpenPluginAppAsync(AdminPluginApp app, MonitorAuthorityLease lease)
    {
        if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent || !_pluginApps.Contains(app)) return;
        if (!await CanLeaveAsync()) return;
        if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent || !_pluginApps.Contains(app)) return;
        CloseDrawer();
        _navigation?.Navigate(typeof(SiloPlayer.Views.PluginRoutePage),
            new SiloPlayer.Views.PluginRoutePage.AdminNavigationArgs(app.InstallationId, app.RoutePath, app.Label, lease.Epoch));
    }
}
