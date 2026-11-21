using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminShellPage
{
    private IReadOnlyList<AdminPluginApp> _pluginApps = [];
    private bool _pluginAppsFailed;
    private bool _pluginAppsLoading;
    private Action? _refreshPluginSearch;

    private void RetirePluginApps()
    {
        _pluginApps = [];
        _pluginAppsFailed = false;
        _pluginAppsLoading = false;
        // Detached sidebar buttons still retain their captured lease and fail closed.
        if (_active) RebuildSidebars();
    }

    private async Task LoadPluginAppsAsync(MonitorAuthorityLease lease)
    {
        if (_pluginAppsLoading || !_active || !lease.IsCurrent) return;
        _pluginAppsLoading = true;
        try
        {
            var api = App.Services.GetService<AdminPluginAppsApi>();
            if (api == null) return; // Minimal service providers may omit the extension.
            var apps = await api.GetAppsAsync(lease);
            if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent) return;
            _pluginApps = apps;
            _pluginAppsFailed = false;
        }
        catch (Exception)
        {
            if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent) return;
            _pluginApps = [];
            _pluginAppsFailed = true;
        }
        finally
        {
            if (_active && ReferenceEquals(_lease, lease) && lease.IsCurrent)
            {
                _pluginAppsLoading = false;
                RebuildSidebars();
                _refreshPluginSearch?.Invoke();
            }
        }
    }

    private FrameworkElement? BuildPluginAppsGroup(bool embedded)
    {
        if (_lease is not { IsCurrent: true } lease || (_pluginApps.Count == 0 && !_pluginAppsFailed)) return null;
        var group = new StackPanel { Tag = "AdminPluginAppsGroup" };
        var heading = A.Eyebrow(AdminText.T("web.admin.shell.sidebar.pluginApps"), A.Muted, 180);
        heading.Margin = new Thickness(8, 0, 8, 8);
        group.Children.Add(heading);
        var rows = new StackPanel { Spacing = 2 };
        foreach (var app in _pluginApps)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(A.Icon(app.Icon, 18, A.Muted));
            var label = A.Text(app.Label, 13, A.Muted, FontWeights.Medium);
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var external = A.Icon("external-link", 12, A.Muted);
            Grid.SetColumn(external, 2); row.Children.Add(external);
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
            var retry = A.Plain(A.Text("Plugin Apps could not be loaded. Retry", 12, A.Muted), "Retry Plugin Apps", new Thickness(12));
            retry.Tag = "AdminPluginAppsRetry";
            retry.Click += async (_, _) => { if (ReferenceEquals(_lease, lease) && lease.IsCurrent) await LoadPluginAppsAsync(lease); };
            rows.Children.Add(retry);
        }
        group.Children.Add(rows);
        return group;
    }

    private IReadOnlyList<AdminPluginApp> SearchPluginApps(string query)
    {
        if (_lease?.IsCurrent != true) return [];
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _pluginApps.Where(app => terms.All(term =>
            (app.Label + " " + app.PluginId + " Plugin Apps " + AdminText.T("web.admin.shell.nav.group.pluginApps"))
                .Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
    }

    private async Task OpenPluginAppAsync(AdminPluginApp app, MonitorAuthorityLease lease)
    {
        if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent || !_pluginApps.Contains(app)) return;
        if (!await CanLeaveAsync()) return;
        if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent || !_pluginApps.Contains(app)) return;
        CloseDrawer();
        _navigation?.Navigate(typeof(SiloPlayer.Views.PluginRoutePage),
            new SiloPlayer.Views.PluginRoutePage.NavigationArgs(app.InstallationId, app.RoutePath, app.Label, lease.Epoch));
    }
}
