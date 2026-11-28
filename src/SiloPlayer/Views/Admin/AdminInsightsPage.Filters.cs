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
    private enum OptionsStatus { NotLoaded, Loading, Ready, Failed }
    private sealed class FilterOptions(bool accounts)
    {
        public bool Accounts { get; } = accounts;
        public string Key => Accounts ? "allAccounts" : "allLibraries";
        public AdminCursorPager Pager { get; } = new();
        public FilterOption[] Items { get; set; } = [];
        public OptionsStatus Status { get; set; }
        public string? Next { get; set; }
    }
    private MonitorAuthorityLease? _optionsLease;
    private FilterOptions _libraries = new(accounts: false);
    private FilterOptions _accountOptions = new(accounts: true);
    private FilterOption[] _accounts => _accountOptions.Items;
    private FilterOption? _selectedAccount;

    private void RenderToolbar(AdminInsightsFilterState filters)
    {
        _toolbar.Visibility = filters.Tab.ShowsToolbar() ? Visibility.Visible : Visibility.Collapsed;
        if (!filters.Tab.ShowsToolbar()) { _toolbar.Content = null; return; }
        var focused = XamlRoot != null ? FocusManager.GetFocusedElement(XamlRoot) as Control : null;
        var focusId = focused == null ? "" : AutomationProperties.GetAutomationId(focused) ?? "";
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
            dates.Children.Add(DateInput("fromDate", filters.FromDate, null, filters.ToDate,
                value => AdminInsightsFilters.WithCustomFrom(Location.WithQuery("to", filters.ToDate), value)));
            dates.Children.Add(A.Text("–", 12, A.Muted));
            dates.Children.Add(DateInput("toDate", filters.ToDate, filters.FromDate, null,
                value => AdminInsightsFilters.WithCustomTo(Location.WithQuery("from", filters.FromDate), value)));
            row.Children.Add(dates);
        }
        var options = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (filters.Tab.ShowsLibraryFilter())
            options.Children.Add(IdMenu(_libraries, filters.LibraryId, id => AdminInsightsFilters.WithLibrary(Location, id)));
        if (filters.Tab.ShowsAccountFilter())
            options.Children.Add(IdMenu(_accountOptions, filters.UserId, id => AdminInsightsFilters.WithAccount(Location, id)));
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
            DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_toolbar.Content, panel)) FindControl(panel, focusId)?.Focus(FocusState.Programmatic); });
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
        static DateTimeOffset CalendarValue(DateOnly date)
        {
            // The native picker uses local calendar days; anchor at local noon, away from midnight transitions.
            var local = date.ToDateTime(new TimeOnly(12, 0));
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
        var input = new CalendarDatePicker { Width = 145, FontSize = 12, MinHeight = 32, CalendarIdentifier = "GregorianCalendar" };
        input.MinDate = CalendarValue(AdminInsightsFilters.MinCustomDate);
        input.MaxDate = CalendarValue(AdminInsightsFilters.TryParseCustomDate(max, out var last) ? last : AdminInsightsFilters.MaxCustomDate);
        if (AdminInsightsFilters.TryParseCustomDate(min, out var first)) input.MinDate = CalendarValue(first);
        if (AdminInsightsFilters.TryParseCustomDate(value, out var date)) input.Date = CalendarValue(date);
        AutomationProperties.SetAutomationId(input, "AdminInsightsFilter_" + key);
        AutomationProperties.SetName(input, AdminInsightsText.T("web.admin.insights.filter." + key));
        input.DateChanged += (_, args) =>
        {
            if (args.NewDate is { } selected) ReplaceLocation(update(AdminInsightsFilters.FormatIsoDate(DateOnly.FromDateTime(selected.Date))));
        };
        return input;
    }

    private FrameworkElement IdMenu(FilterOptions list, long? selected, Func<long?, AdminRouteRequest> update)
    {
        var all = AdminInsightsText.T("web.admin.insights.filter." + list.Key);
        IEnumerable<FilterOption> items = list.Items;
        if (list.Accounts && _selectedAccount is { } account && account.Id == selected && list.Items.All(item => item.Id != account.Id))
            items = items.Prepend(account);
        var options = items.Select(item => (item.Label, (long?)item.Id)).Prepend((all, null)).ToArray();
        var label = options.FirstOrDefault(option => option.Item2 == selected).Item1 ?? selected?.ToString(CultureInfo.InvariantCulture) ?? all;
        var button = OptionMenu(list.Key, label, options, selected, id =>
        {
            if (!OwnsOptions(list)) return Location;
            if (list.Accounts) _selectedAccount = items.FirstOrDefault(item => item.Id == id);
            return update(id);
        });
        var menu = (MenuFlyout)button.Flyout;
        var actions = 0;
        var position = list.Pager.Capture();
        void AddAction(string text, Func<bool> move)
        {
            var action = new MenuFlyoutItem { Text = text };
            action.Click += (_, _) =>
            {
                if (!OwnsOptions(list) || list.Status == OptionsStatus.Loading || list.Pager.Capture() != position ||
                    _optionsLease is not { IsCurrent: true } lease) return;
                if (move()) _ = ReadOptionsPageAsync(list, lease);
                else
                {
                    // A repeated or out-of-bounds continuation is not an empty last page.
                    list.Next = null;
                    list.Status = OptionsStatus.Failed;
                    RenderToolbar(Filters);
                }
            };
            menu.Items.Insert(actions++, action);
        }
        if (list.Status != OptionsStatus.Loading)
        {
            if (list.Status == OptionsStatus.Failed) AddAction(AdminInsightsText.T("web.admin.insights.retry"), () => true);
            if (list.Pager.CanGoBack) AddAction("Previous page", list.Pager.Back);
            if (list.Next != null) AddAction("Next page", () => list.Pager.TryAdvance(position, list.Next));
        }
        if (actions > 0) menu.Items.Insert(actions, new MenuFlyoutSeparator());
        var status = list.Status switch
        {
            OptionsStatus.NotLoaded or OptionsStatus.Loading => list.Accounts ? "Loading accounts…" : "Loading libraries…",
            OptionsStatus.Failed => list.Accounts ? "Couldn’t load accounts. Retry or refresh." : "Couldn’t load libraries. Retry or refresh.",
            _ when list.Next != null => $"Page {list.Pager.PageIndex + 1} · More results available",
            _ when list.Pager.CanGoBack => $"Page {list.Pager.PageIndex + 1} · Last page",
            _ => null,
        };
        if (status == null) return button;
        AutomationProperties.SetItemStatus(button, status);
        var feedback = A.Text(status, 11, list.Status == OptionsStatus.Failed ? A.Warning : A.Muted, trim: false);
        AutomationProperties.SetAutomationId(feedback, "AdminInsightsOptionStatus_" + list.Key);
        return new StackPanel { Spacing = 4, MaxWidth = 220, Children = { button, feedback } };
    }

    private bool OwnsOptions(FilterOptions list) => _active && _optionsLease?.IsCurrent == true &&
        ReferenceEquals(list, list.Accounts ? _accountOptions : _libraries);

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

    private async Task ReadOptionsAsync(bool retryFailed = false)
    {
        if (_authority?.IsAvailable != true) return;
        var tab = Filters.Tab;
        if (!tab.ShowsToolbar() || !tab.ShowsLibraryFilter()) return;
        MonitorAuthorityLease lease;
        try { lease = _optionsLease ??= _authority.Capture(); }
        catch (OperationCanceledException) { return; }
        bool NeedsRead(FilterOptions list) => list.Status == OptionsStatus.NotLoaded || retryFailed && list.Status == OptionsStatus.Failed;
        await Task.WhenAll(NeedsRead(_libraries) ? ReadOptionsPageAsync(_libraries, lease) : Task.CompletedTask,
            tab.ShowsAccountFilter() && NeedsRead(_accountOptions) ? ReadOptionsPageAsync(_accountOptions, lease) : Task.CompletedTask);
    }

    private async Task ReadOptionsPageAsync(FilterOptions list, MonitorAuthorityLease lease)
    {
        if (!lease.IsCurrent || !ReferenceEquals(_optionsLease, lease)) return;
        if (list.Accounts)
            _selectedAccount = list.Items.FirstOrDefault(item => item.Id == Filters.UserId) ?? _selectedAccount;
        list.Status = OptionsStatus.Loading;
        list.Items = [];
        list.Next = null;
        RenderToolbar(Filters);
        try
        {
            var cursor = list.Pager.Cursor;
            FilterOption[] items;
            string? next;
            if (list.Accounts)
            {
                var api = App.Services.GetService<AdminUsersApi>() ?? throw new InvalidDataException("Accounts service unavailable.");
                var page = await api.PageAsync(lease, "/api/v2/admin/users", limit: 200, cursor: cursor, ct: lease.Token);
                items = Options(page.Items.Select(item => (item["id"]?.ToString(), item.Text("username"))));
                next = page.NextCursor;
            }
            else
            {
                var api = App.Services.GetService<AdminLibrariesApi>() ?? throw new InvalidDataException("Libraries service unavailable.");
                var page = await api.ListPickerPageAsync(lease, cursor);
                items = Options(page.Items.Select(item => ((string?)item.Id, item.Name)));
                next = page.NextCursor;
            }
            if (!lease.IsCurrent || !ReferenceEquals(_optionsLease, lease)) return;
            list.Items = items;
            list.Next = next;
            list.Status = OptionsStatus.Ready;
            if (list.Accounts) RefreshPanel();
        }
        catch (Exception e) when (e is ApiException or InvalidDataException or IOException or OperationCanceledException or
            HttpRequestException or System.Text.Json.JsonException or AdminListInconsistentException)
        {
            if (!lease.IsCurrent || !ReferenceEquals(_optionsLease, lease)) return;
            list.Status = OptionsStatus.Failed;
        }
        RenderToolbar(Filters);
    }

    private static FilterOption[] Options(IEnumerable<(string? Id, string Name)> items) => items
        .Select(item => (Id: AdminInsightsFilters.ParsePositiveId(item.Id), item.Name))
        .Where(item => item.Id.HasValue).DistinctBy(item => item.Id)
        .Select(item => new FilterOption(item.Id!.Value, string.IsNullOrEmpty(item.Name) ? item.Id.Value.ToString(CultureInfo.InvariantCulture) : item.Name)).ToArray();

    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        ClearOptions();
        RetirePins(departing: false);
        CloseDetailSheet();
        LeaveTab();
        RetireTabReads();
        RenderLocation();
        StartPins();
    });

    private void ClearOptions()
    {
        _optionsLease?.Dispose();
        _optionsLease = null;
        _libraries = new(accounts: false);
        _accountOptions = new(accounts: true);
        _selectedAccount = null;
    }
}
