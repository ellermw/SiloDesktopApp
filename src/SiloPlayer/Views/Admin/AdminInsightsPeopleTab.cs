using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;
using P = SiloPlayer.Core.Services.Admin.AdminInsightsPeoplePresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Insights › People (official <c>PeopleTab.tsx</c>, c59798116): who watched, as a searchable, sortable table of accounts with their household
/// profiles one click away, and a row's Details and History. A new search, sort or filter keeps the rows on screen (links off) until its first
/// page arrives; the search box is created once and kept across every redraw so typing never loses focus. "Show more accounts" adds the next 50.
/// </summary>
internal sealed class AdminInsightsPeopleTab : AdminInsightsTabBase<AdminInsightsPeoplePages>
{
    private const double SearchWidth = 320;
    private const double AccountNameMaxWidth = 300;
    private readonly TextBox _searchBox;
    private readonly Grid _searchHost = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly FrameworkElement _searchSpinner;
    private readonly DispatcherQueueTimer _debounce;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly StackPanel _root = new() { Spacing = 20 };
    private readonly ContentControl _noticeSlot = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly ContentControl _kpiSlot = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly ContentControl _contentSlot = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private string _search = "";
    private AdminInsightsPeopleSort _sort = AdminInsightsPeopleSort.WatchTime;
    private bool _descending = true;

    internal AdminInsightsPeopleTab(AdminInsightsPage page) : base(page)
    {
        var placeholder = I("web.admin.insights.people.searchPlaceholder");
        _searchBox = new TextBox
        {
            PlaceholderText = placeholder, FontSize = 12, Height = 32, MinHeight = 0, Padding = new Thickness(30, 6, 30, 6), CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // The server refuses a longer search; the box stops short of it rather than failing the read.
            MaxLength = AdminInsightsApi.PeopleMaxSearchLength,
        };
        AutomationProperties.SetName(_searchBox, placeholder);
        AutomationProperties.SetAutomationId(_searchBox, "AdminInsightsPeopleSearch");
        var icon = A.Icon("search", 14, A.Muted);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(10, 0, 0, 0);
        _searchSpinner = A.Spinner(14, A.Muted);
        _searchSpinner.HorizontalAlignment = HorizontalAlignment.Right;
        _searchSpinner.Margin = new Thickness(0, 0, 10, 0);
        _searchSpinner.VerticalAlignment = VerticalAlignment.Center;
        _searchSpinner.IsHitTestVisible = false;
        _searchSpinner.Visibility = Visibility.Collapsed;
        var field = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Width = SearchWidth };
        field.Children.Add(_searchBox);
        field.Children.Add(icon);
        field.Children.Add(_searchSpinner);
        _searchHost.Children.Add(field);
        _searchHost.SizeChanged += (_, e) => field.Width = Math.Min(SearchWidth, Math.Max(0, e.NewSize.Width));
        _debounce = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _debounce.Interval = AdminInsightsIntervals.SearchDebounce;
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => ApplySearch();
        _searchBox.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };
        _root.Children.Add(_noticeSlot);
        _root.Children.Add(_kpiSlot);
        _root.Children.Add(BuildFrame());
    }

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    private AdminInsightsPeopleQuery Query(int offset) => new(string.IsNullOrEmpty(_search) ? null : _search, _sort, _descending, AdminInsightsApi.PeoplePageSize, offset);

    protected override string KeyFor(AdminInsightsFilterState filters) => AdminInsightsApi.PeoplePath(filters.ToWindow(), Query(0));

    protected override async Task<AdminInsightsPeoplePages> LoadAsync(MonitorAuthorityLease lease, AdminInsightsFilterState filters, bool refresh, CancellationToken ct)
    {
        var window = filters.ToWindow();
        var query = Query(0);
        var first = await Page.Insights.GetPeopleAsync(lease, window, query, refresh, ct);
        return new AdminInsightsPeoplePages(first, (offset, token) => Page.Insights.GetPeopleAsync(lease, window, query with { Offset = offset }, false, token));
    }

    /// <summary>The trimmed text of the box, once it has been still for the debounce: only then does it become a different read.</summary>
    private void ApplySearch()
    {
        var text = _searchBox.Text.Trim();
        if (text == _search) return;
        _search = text;
        Reload();
    }

    protected override FrameworkElement BuildSkeleton() => Layout(null, Filters);

    protected override FrameworkElement BuildBody(AdminInsightsPeoplePages data, AdminInsightsFilterState filters) => Layout(data, filters);

    protected override FrameworkElement BuildError(Exception error, Action retry)
    {
        Show(_noticeSlot, null);
        Show(_kpiSlot, null);
        _contentSlot.Content = AdminInsightsUi.SectionError(error.Message, retry);
        _searchSpinner.Visibility = Visibility.Collapsed;
        return _root;
    }

    // ── Layout ───────────────────────────────────────────────

    /// <summary>
    /// The tab's frame (notice, KPI cards, the card with the search box) is built once and kept; a result only changes what its slots hold. The
    /// search box and its focus are therefore never rebuilt, and the same root is handed back for every result.
    /// </summary>
    private FrameworkElement Layout(AdminInsightsPeoplePages? pages, AdminInsightsFilterState filters)
    {
        _searchSpinner.Visibility = IsPlaceholder ? Visibility.Visible : Visibility.Collapsed;
        if (pages == null)
        {
            Show(_noticeSlot, null);
            Show(_kpiSlot, Kpis(null));
            _contentSlot.Content = Skeleton();
            return _root;
        }
        var notice = P.Notice(pages.First);
        Show(_noticeSlot, notice == null ? null : AdminInsightsUi.Notice(notice));
        Show(_kpiSlot, Kpis(pages));
        _contentSlot.Content = pages.Accounts.Count == 0 ? Empty() : TableWithMore(pages, filters);
        return _root;
    }

    /// <summary>A slot with nothing in it takes no room (the stack's spacing would still count it).</summary>
    private static void Show(ContentControl slot, FrameworkElement? content)
    {
        slot.Content = content;
        slot.Visibility = content == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static FrameworkElement Kpis(AdminInsightsPeoplePages? pages)
    {
        if (pages != null) return AdminInsightsLayout.KpiGridOfThree(P.Kpis(pages.First));
        var grid = new AdminInsightsGrid(12, (AdminInsightsGrid.Sm, 3));
        for (var index = 0; index < 3; index++) grid.Add(DashboardUi.Skeleton(96, 16));
        return grid;
    }

    private static FrameworkElement Skeleton() => new StackPanel
    {
        Spacing = 12,
        Children = { DashboardUi.Skeleton(36, 8), DashboardUi.Skeleton(48, 8), DashboardUi.Skeleton(48, 8), DashboardUi.Skeleton(48, 8), DashboardUi.Skeleton(48, 8), DashboardUi.Skeleton(48, 8) },
    };

    private static FrameworkElement Empty()
    {
        var text = A.Text(I("web.admin.insights.people.empty"), 14, A.Muted, trim: false);
        text.TextAlignment = TextAlignment.Center;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(text, "AdminInsightsPeopleEmpty");
        return new Border { Padding = new Thickness(0, 48, 0, 48), Child = text };
    }

    /// <summary>The card: the search box and "Add to dashboard" in its header, then a slot for whatever the read gave.</summary>
    private FrameworkElement BuildFrame()
    {
        var pin = new AdminInsightsPinButton(Page, DashboardRegistry.TopProfiles, DashboardText.T("web.admin.dashboard.widgetCatalog.topProfiles.title"));
        var header = new AdminInsightsCardHeader(_searchHost, pin, centered: true);
        var stack = new StackPanel { Spacing = 24, Children = { header, _contentSlot } };
        return new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(24), Background = A.Solid(A.Surface), Child = stack };
    }

    // ── Table ────────────────────────────────────────────────

    private FrameworkElement TableWithMore(AdminInsightsPeoplePages pages, AdminInsightsFilterState filters)
    {
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(Table(P.Rows(pages, filters, DateTimeOffset.UtcNow)));
        if (pages.HasMore && !IsPlaceholder) stack.Children.Add(MoreButton(pages));
        return stack;
    }

    private FrameworkElement MoreButton(AdminInsightsPeoplePages pages)
    {
        var label = I("web.admin.insights.people.loadMore");
        var button = A.Outline(label);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Height = 32;
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, "AdminInsightsPeopleMore");
        button.IsEnabled = !pages.IsLoadingMore;
        button.Click += async (_, _) =>
        {
            if (pages.IsLoadingMore) return;
            var more = pages.LoadMoreAsync();
            Redraw();
            await more;
            Redraw();
        };
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { button } };
        if (pages.MoreError != null)
            stack.Children.Add(A.Text(I("web.admin.insights.loadError") + " " + pages.MoreError.Message, 12, A.Muted, trim: false));
        return stack;
    }

    private FrameworkElement Table(IReadOnlyList<P.AccountRow> rows)
    {
        var table = new AdminInsightsTable(7);
        for (var column = 1; column <= 6; column++) table.Align(column, HorizontalAlignment.Right);
        var header = new List<UIElement> { AdminInsightsTable.HeaderText(I("web.admin.insights.people.column.account")) };
        header.AddRange(P.SortColumns.Select(SortHeader));
        header.Add(AdminInsightsTable.HeaderText(I("web.admin.insights.people.column.actions")));
        table.AddHeader(header);
        foreach (var row in rows)
        {
            var expanded = _expanded.Contains(row.UserId);
            table.AddRow(
            [
                AccountCell(row, expanded),
                Value(row.WatchTime, 14, FontWeights.SemiBold, A.Foreground), Value(row.Plays, 14, null, A.Foreground), Value(row.DirectRate, 14, null, A.Foreground),
                Value(row.CompletionRate, 14, null, A.Foreground), SeenCell(row.LastSeen, row.LastSeenTitle, 12),
                Actions(row.Person, row.DetailsName, row.History, FontWeights.Medium),
            ]);
            if (!expanded) continue;
            foreach (var profile in row.Profiles)
                table.AddRow(
                [
                    ProfileCell(profile),
                    Value(profile.WatchTime, 12, null, A.Foreground), Value(profile.Plays, 12, null, A.Foreground), Value(profile.DirectRate, 12, null, A.Foreground),
                    Value(profile.CompletionRate, 12, null, A.Foreground), SeenCell(profile.LastSeen, profile.LastSeenTitle, 12),
                    Actions(profile.Person, profile.DetailsName, profile.History, FontWeights.Normal),
                ], tinted: true);
        }
        return table;
    }

    private static TextBlock Value(string text, double size, Windows.UI.Text.FontWeight? weight, Windows.UI.Color color)
    {
        var block = A.Text(text, size, color, weight, trim: false);
        block.HorizontalAlignment = HorizontalAlignment.Right;
        return block;
    }

    private static TextBlock SeenCell(string text, string title, double size)
    {
        var block = A.Text(text, size, A.Muted, trim: false);
        ToolTipService.SetToolTip(block, title);
        return block;
    }

    private UIElement SortHeader(AdminInsightsPeopleSort column)
    {
        var label = I(P.ColumnLabelKey(column));
        var indicator = P.IndicatorFor(column, _sort, _descending);
        var text = AdminInsightsTable.HeaderText(label);
        FrameworkElement icon = indicator switch
        {
            P.SortIndicator.Descending => A.Icon("arrow-down", 12, A.Primary),
            P.SortIndicator.Ascending => A.Icon("arrow-up", 12, A.Primary),
            _ => AdminInsightsTable.SortNone(12, A.Solid(A.Muted, 0.6)),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { text, icon } };
        var button = A.Plain(row, label, new Thickness(6, 2, 6, 2));
        button.Margin = new Thickness(-6, -2, -6, -2);
        button.CornerRadius = new CornerRadius(4);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.HorizontalContentAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetAutomationId(button, "AdminInsightsPeopleSort_" + column);
        AutomationProperties.SetItemStatus(button, indicator == P.SortIndicator.None ? "" : indicator == P.SortIndicator.Descending ? "descending" : "ascending");
        button.PointerEntered += (_, _) => text.Foreground = A.Solid(A.Foreground);
        button.PointerExited += (_, _) => text.Foreground = A.Solid(A.Muted);
        button.Click += (_, _) => ChooseSort(column);
        return button;
    }

    /// <summary>A header click: the active column flips its direction, a new column starts descending.</summary>
    private void ChooseSort(AdminInsightsPeopleSort column)
    {
        (_sort, _descending) = AdminInsightsRules.NextPeopleSort(_sort, _descending, column);
        Reload();
    }

    private UIElement AccountCell(P.AccountRow row, bool expanded)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (row.HasProfiles)
        {
            var name = expanded ? row.CollapseName : row.ExpandName;
            var chevron = A.Plain(A.Icon(expanded ? "chevron-down" : "chevron-right", 14, A.Muted), name);
            chevron.Width = 24;
            chevron.Height = 24;
            chevron.CornerRadius = new CornerRadius(6);
            chevron.HorizontalContentAlignment = HorizontalAlignment.Center;
            chevron.VerticalAlignment = VerticalAlignment.Top;
            AutomationProperties.SetAutomationId(chevron, "AdminInsightsPeopleExpand_" + row.UserId);
            AutomationProperties.SetItemStatus(chevron, expanded ? "expanded" : "collapsed");
            chevron.Click += (_, _) =>
            {
                if (!_expanded.Add(row.UserId)) _expanded.Remove(row.UserId);
                Redraw();
            };
            grid.Children.Add(chevron);
        }
        var lines = new StackPanel { Spacing = 2 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        top.Children.Add(Username(row));
        if (row.IsLive) top.Children.Add(LiveBadge());
        if (row.ProfilesBadge != null) top.Children.Add(AdminInsightsTable.OutlineBadge(row.ProfilesBadge));
        lines.Children.Add(top);
        if (row.TopClient != null) lines.Children.Add(A.Text(row.TopClient, 11, A.Muted, trim: true));
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);
        return grid;
    }

    private FrameworkElement Username(P.AccountRow row)
    {
        var request = row.AdminUserId is { } id ? AdminRouteRequest.UserDetail(id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
        if (request == null || !AdminShellPage.CanOpen(request))
        {
            // A very long name ends in an ellipsis (its full text is the tooltip) instead of making the account column, and the table, wider than the card.
            var plain = A.Text(row.Username, 14, A.Foreground, FontWeights.SemiBold);
            plain.MaxWidth = AccountNameMaxWidth;
            return plain;
        }
        var name = A.Text(row.Username, 14, A.Foreground, FontWeights.SemiBold);
        var link = A.LinkButton(name, row.Username, A.Foreground);
        link.Tag = DashboardBarList.LinkTag;
        link.MaxWidth = AccountNameMaxWidth;
        link.Click += (_, _) => Guarded(() => AdminShellPage.Open(request))?.Invoke();
        return link;
    }

    /// <summary>"Live" with a dot: the account has a session open now (a pill with a success tint).</summary>
    private static FrameworkElement LiveBadge()
    {
        var success = A.ToneColor(ActivityTone.Success);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = A.Solid(success), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(A.Text(I("web.admin.insights.people.liveBadge"), 10, A.LabelColor(ActivityTone.Success), FontWeights.SemiBold, trim: false));
        return new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = A.Solid(success, 0.30), Background = A.Solid(success, 0.15),
            Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center, Child = row,
        };
    }

    private static UIElement ProfileCell(P.ProfileRow profile)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(28, 0, 0, 0) };
        row.Children.Add(A.Text(profile.Label, 12, A.Foreground, FontWeights.Medium, trim: true));
        if (profile.IsLive)
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = A.Solid(A.ToneColor(ActivityTone.Success)), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(dot, I("web.admin.insights.people.liveBadge"));
            row.Children.Add(dot);
        }
        return row;
    }

    /// <summary>Details opens the person's panel; History opens Playback History for the person over the same window and library.</summary>
    private UIElement Actions(AdminInsightsPersonScope? person, string detailsName, AdminRouteRequest history, Windows.UI.Text.FontWeight weight)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right };
        // The row-link button has no padding or margin tricks to clip: at rest the primary colour, underlined on hover and focus.
        Button Link(string label, string name, string id, Action open)
        {
            var text = A.Text(label, 12, A.Primary, weight, trim: false);
            var button = A.LinkButton(text, name, A.Primary);
            button.Tag = DashboardBarList.LinkTag;
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => Guarded(open)?.Invoke();
            return button;
        }
        if (person != null)
            row.Children.Add(Link(I("web.admin.insights.people.action.details"), detailsName, "AdminInsightsPeopleDetails", () => Page.OpenPerson(person)));
        row.Children.Add(Link(I("web.admin.insights.openHistory"), I("web.admin.insights.openHistory"), "AdminInsightsPeopleHistory", () => AdminShellPage.Open(history)));
        return row;
    }
}
