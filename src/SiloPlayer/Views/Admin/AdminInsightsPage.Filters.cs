using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminInsightsPage
{
    private sealed record FilterOption(long Id, string Label);
    private MonitorAuthorityLease? _optionsLease;
    private FilterOption[] _libraries = [];
    private FilterOption[] _accounts = [];

    private void RenderToolbar(AdminInsightsFilterState filters)
    {
        _toolbar.Visibility = filters.Tab.ShowsToolbar() ? Visibility.Visible : Visibility.Collapsed;
        if (!filters.Tab.ShowsToolbar()) { _toolbar.Content = null; return; }
        var focused = XamlRoot != null ? FocusManager.GetFocusedElement(XamlRoot) as Control : null;
        var focusId = focused == null ? "" : AutomationProperties.GetAutomationId(focused);
        var row = new LogsWrapPanel { Spacing = 10, RowSpacing = 8, Margin = new(12) };
        var ranges = new LogsWrapPanel { Spacing = 2, Margin = new(2) };
        AutomationProperties.SetName(ranges, AdminInsightsText.T("web.admin.insights.filter.rangeLabel"));
        foreach (var range in AdminInsightsRanges.Ordered)
        {
            var selected = range == filters.Range;
            var text = AdminInsightsText.T("web.admin.insights.range." + range.Wire());
            var button = A.Plain(A.Text(text, 12, selected ? A.Foreground : A.Muted, FontWeights.Medium), text, new Thickness(10, 5, 10, 5));
            AutomationProperties.SetAutomationId(button, "AdminInsightsRange_" + range.Wire());
            AutomationProperties.SetItemStatus(button, selected ? "pressed" : "");
            button.Background = A.Solid(selected ? A.Mix(A.Surface, A.Primary, 0.15) : A.Surface);
            button.CornerRadius = new(6);
            button.Click += (_, _) => ReplaceLocation(AdminInsightsFilters.WithRange(Location, range, Today));
            ranges.Children.Add(button);
        }
        row.Children.Add(new Border { Child = ranges, CornerRadius = new(8), BorderThickness = new(1), BorderBrush = A.Solid(A.Border) });
        if (filters.Range == AdminInsightsRange.Custom)
        {
            var dates = new LogsWrapPanel { Spacing = 8 };
            dates.Children.Add(DateInput("fromDate", filters.FromDate, null, filters.ToDate, value => AdminInsightsFilters.WithCustomFrom(Location, value)));
            dates.Children.Add(A.Text("–", 12, A.Muted));
            dates.Children.Add(DateInput("toDate", filters.ToDate, filters.FromDate, null, value => AdminInsightsFilters.WithCustomTo(Location, value)));
            row.Children.Add(dates);
        }
        var options = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (filters.Tab.ShowsLibraryFilter())
            options.Children.Add(IdMenu("allLibraries", _libraries, filters.LibraryId, id => AdminInsightsFilters.WithLibrary(Location, id)));
        if (filters.Tab.ShowsAccountFilter())
            options.Children.Add(IdMenu("allAccounts", _accounts, filters.UserId, id => AdminInsightsFilters.WithAccount(Location, id)));
        if (filters.Tab.ShowsTimezoneFilter())
        {
            string Label(string zone) => zone == "UTC" ? AdminInsightsText.T("web.admin.insights.filter.timezoneUtc") :
                zone == LocalTimezone ? AdminInsightsText.T("web.admin.insights.filter.timezoneBrowser", ("tz", zone)) : zone;
            var zones = new[] { LocalTimezone, "UTC", filters.Tz }.Distinct().Select(zone => (Label(zone), zone));
            options.Children.Add(OptionMenu("timezone", Label(filters.Tz), zones, filters.Tz, zone => AdminInsightsFilters.WithTimezone(Location, zone, LocalTimezone)));
        }
        if (options.Children.Count > 0) { row.Children.Add(options); row.TrailingActions = options; }
        var panel = new Border { Child = row, CornerRadius = new(16), Background = A.Solid(A.Surface) };
        AutomationProperties.SetAutomationId(panel, "AdminInsightsFilters");
        _toolbar.Content = panel;
        if (focusId.StartsWith("AdminInsightsFilter_", StringComparison.Ordinal) || focusId.StartsWith("AdminInsightsRange_", StringComparison.Ordinal))
            DispatcherQueue.TryEnqueue(() => FindControl(panel, focusId)?.Focus(FocusState.Programmatic));
    }

    private static Control? FindControl(DependencyObject node, string id)
    {
        if (node is Control control && AutomationProperties.GetAutomationId(control) == id) return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            if (FindControl(VisualTreeHelper.GetChild(node, index), id) is { } child) return child;
        return null;
    }

    private CalendarDatePicker DateInput(string key, string value, string? min, string? max, Func<string, AdminRouteRequest> update)
    {
        static DateTimeOffset? Date(string? date) => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed.ToDateTime(TimeOnly.MinValue)) : null;
        var input = new CalendarDatePicker { Width = 145, FontSize = 12, MinHeight = 32 };
        if (Date(min) is { } first) input.MinDate = first;
        if (Date(max) is { } last) input.MaxDate = last;
        input.Date = Date(value);
        AutomationProperties.SetAutomationId(input, "AdminInsightsFilter_" + key);
        AutomationProperties.SetName(input, AdminInsightsText.T("web.admin.insights.filter." + key));
        input.DateChanged += (_, args) =>
        {
            if (args.NewDate is { } date) ReplaceLocation(update(AdminInsightsFilters.FormatIsoDate(DateOnly.FromDateTime(date.Date))));
        };
        return input;
    }

    private Button IdMenu(string key, IEnumerable<FilterOption> items, long? selected, Func<long?, AdminRouteRequest> update)
    {
        var all = AdminInsightsText.T("web.admin.insights.filter." + key);
        var options = items.Select(item => (item.Label, (long?)item.Id)).Prepend((all, null)).ToArray();
        var label = options.FirstOrDefault(option => option.Item2 == selected).Item1 ?? selected?.ToString(CultureInfo.InvariantCulture) ?? all;
        return OptionMenu(key, label, options, selected, update);
    }

    private Button OptionMenu<T>(string key, string label, IEnumerable<(string Label, T Value)> items, T selected, Func<T, AdminRouteRequest> update)
    {
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.Children.Add(A.Text(label, 12, A.Foreground, FontWeights.Medium));
        var icon = A.Icon("chevron-down", 14, A.Muted);
        Grid.SetColumn(icon, 1);
        content.Children.Add(icon);
        var button = A.Outline("", null);
        button.Content = content;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.MinWidth = 118;
        button.MaxWidth = 220;
        AutomationProperties.SetAutomationId(button, "AdminInsightsFilter_" + key);
        AutomationProperties.SetName(button, key == "timezone" ? AdminInsightsText.T("web.admin.insights.filter.timezone") : label);
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
        foreach (var (name, value) in items)
        {
            var item = new RadioMenuFlyoutItem { Text = name, IsChecked = EqualityComparer<T>.Default.Equals(value, selected), GroupName = key };
            item.Click += (_, _) => ReplaceLocation(update(value));
            menu.Items.Add(item);
        }
        button.Flyout = menu;
        return button;
    }

    private async Task ReadOptionsAsync()
    {
        if (_authority?.IsAvailable != true) return;
        MonitorAuthorityLease lease;
        try { lease = _authority.Capture(); }
        catch (OperationCanceledException) { return; }
        _optionsLease = lease;
        await Task.WhenAll(ReadLibrariesAsync(lease), ReadAccountsAsync(lease));
    }

    private async Task ReadLibrariesAsync(MonitorAuthorityLease lease)
    {
        var api = App.Services.GetService<AdminLibrariesApi>();
        if (api == null) return;
        try
        {
            var result = await api.ListAsync(lease);
            if (!lease.IsCurrent || !ReferenceEquals(_optionsLease, lease)) return;
            _libraries = Options(result.Items.Select(item => ((string?)item.Id, item.Name)));
            RenderToolbar(Filters);
        }
        catch (Exception e) when (e is ApiException or IOException or OperationCanceledException or HttpRequestException) { }
    }

    private async Task ReadAccountsAsync(MonitorAuthorityLease lease)
    {
        var api = App.Services.GetService<AdminUsersApi>();
        if (api == null) return;
        try
        {
            var users = await AdminCollectionWalk.ReadAsync<System.Text.Json.Nodes.JsonObject>(lease, new AdminCollectionScope("insights-accounts"),
                new AdminWalkLimits(pageSize: 200, maxPages: 50, maxItems: 10_000), async (cursor, limit, token) =>
                {
                    var page = await api.PageAsync(lease, "/api/v2/admin/users", limit, cursor, ct: token);
                    return new ApiCollectionPage<System.Text.Json.Nodes.JsonObject>
                    {
                        Items = page.Items.ToList(),
                        Page = new ApiPageInfo { HasMore = page.NextCursor != null, NextCursor = page.NextCursor },
                    };
                }, item => item["id"]?.ToString());
            if (!users.IsComplete || !lease.IsCurrent || !ReferenceEquals(_optionsLease, lease)) return;
            _accounts = Options(users.Items.Select(item => (item["id"]?.ToString(), item.Text("username"))));
            RenderToolbar(Filters);
        }
        catch (Exception e) when (e is ApiException or IOException or OperationCanceledException or HttpRequestException) { }
    }

    private static FilterOption[] Options(IEnumerable<(string? Id, string Name)> items) => items
        .Select(item => (Id: AdminInsightsFilters.ParsePositiveId(item.Id), item.Name))
        .Where(item => item.Id.HasValue)
        .Select(item => new FilterOption(item.Id!.Value, !string.IsNullOrEmpty(item.Name) ? item.Name : item.Id.Value.ToString(CultureInfo.InvariantCulture))).ToArray();

    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        ClearOptions();
        CloseDetailSheet();
        LeaveTab();
        RenderLocation();
    });

    private void ClearOptions()
    {
        _optionsLease?.Dispose();
        _optionsLease = null;
        _libraries = [];
        _accounts = [];
    }
}
