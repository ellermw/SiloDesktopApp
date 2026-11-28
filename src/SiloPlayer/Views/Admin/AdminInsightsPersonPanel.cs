using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;
using P = SiloPlayer.Core.Services.Admin.AdminInsightsPersonPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The person panel (official <c>PersonDetailSheet.tsx</c>, c59798116): one account, or one of its household profiles, over the page's window and
/// library. Three independent reads (Activity, Titles and Devices) scoped to the person each fill their own part, so a slow or failed one never
/// blanks the others. It reads only while open and never keeps another person's numbers on screen while a new one loads.
/// </summary>
internal sealed class AdminInsightsPersonPanel : IAdminInsightsPanelContent, IDisposable
{
    private readonly AdminInsightsPage _page;
    private readonly MonitorAuthorityLease? _lease;
    private readonly AdminInsightsReader<AdminInsightsActivity>? _activity;
    private readonly AdminInsightsReader<AdminInsightsTitles>? _titles;
    private readonly AdminInsightsReader<AdminInsightsDevices>? _devices;
    private AdminInsightsDetailState _detail = new();
    private AdminInsightsFilterState? _filters;
    private IReadOnlyList<AdminHistoryProfileOption>? _profiles;
    private long? _profilesFor;
    private object? _signature;
    private AdminInsightsPanelPresentation? _presentation;
    private bool _disposed;

    internal AdminInsightsPersonPanel(AdminInsightsPage page)
    {
        _page = page;
        _lease = page.CaptureLease();
        if (_lease == null) return;
        _activity = Reader<AdminInsightsActivity>(_lease, page);
        _titles = Reader<AdminInsightsTitles>(_lease, page);
        _devices = Reader<AdminInsightsDevices>(_lease, page);
    }

    private static AdminInsightsReader<T> Reader<T>(MonitorAuthorityLease lease, AdminInsightsPage page) where T : class
    {
        // A panel never keeps another person's numbers on screen while a new one loads.
        var reader = new AdminInsightsReader<T>(lease, keepPrevious: false);
        reader.Changed += () => page.RefreshPanel();
        return reader;
    }

    public AdminInsightsPanelPresentation ApplyState(AdminInsightsDetailState detail, AdminInsightsFilterState filters)
    {
        _detail = detail;
        _filters = filters;
        RequestAll(force: false);
        _ = ReadProfilesAsync(detail.Person);
        return Present();
    }

    public Task RefreshAsync()
    {
        if (_disposed) return Task.CompletedTask;
        return Task.WhenAll(RequestActivity(force: true), RequestTitles(force: true), RequestDevices(force: true));
    }

    private AdminInsightsPersonScope? Person => _detail.Person;

    private AdminInsightsWindow Window => _filters!.ToWindow(person: Person);

    private bool Refresh => _page.RefreshGate?.ShouldRefresh ?? false;

    private void RequestAll(bool force)
    {
        _ = RequestActivity(force);
        _ = RequestTitles(force);
        _ = RequestDevices(force);
    }

    private Task RequestActivity(bool force)
    {
        if (_activity == null || _lease == null || _filters == null || Person == null) return Task.CompletedTask;
        var window = Window;
        var refresh = Refresh;
        return _activity.Request(AdminInsightsApi.ActivityPath(window), ct => _page.Insights.GetActivityAsync(_lease, window, refresh, ct), force);
    }

    private Task RequestTitles(bool force)
    {
        if (_titles == null || _lease == null || _filters == null || Person == null) return Task.CompletedTask;
        var window = Window;
        var refresh = Refresh;
        return _titles.Request(AdminInsightsApi.TitlesPath(window, AdminInsightsTitleSort.Plays, AdminInsightsLimits.PersonTopTitles),
            ct => _page.Insights.GetTitlesAsync(_lease, window, AdminInsightsTitleSort.Plays, AdminInsightsLimits.PersonTopTitles, refresh, ct), force);
    }

    private Task RequestDevices(bool force)
    {
        if (_devices == null || _lease == null || _filters == null || Person == null) return Task.CompletedTask;
        var window = Window;
        var refresh = Refresh;
        return _devices.Request(AdminInsightsApi.DevicesPath(window, AdminInsightsLimits.PersonDevicesMinSessions),
            ct => _page.Insights.GetDevicesAsync(_lease, window, AdminInsightsLimits.PersonDevicesMinSessions, refresh, ct), force);
    }

    /// <summary>The account's household profiles, read once per account: the picker's choices and the scoped profile's name.</summary>
    private async Task ReadProfilesAsync(AdminInsightsPersonScope? person)
    {
        if (person == null || _lease == null || _profilesFor == person.UserId || _disposed) return;
        var api = App.Services.GetService<AdminPlaybackHistoryApi>();
        if (api == null) return;
        _profilesFor = person.UserId;
        try
        {
            var list = await api.ListProfilesAsync(_lease, person.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (_disposed || !_lease.IsCurrent) return;
            _profiles = list.Items;
        }
        catch (Exception e) when (e is ApiException or IOException or OperationCanceledException or HttpRequestException or InvalidDataException)
        {
            _profiles = null;
        }
        if (!_disposed) _page.RefreshPanel();
    }

    private string? PersonName => Person is { } person ? AdminInsightsPersonNames.NameOf(person, _page.AccountName(person.UserId), _profiles) : null;

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    // ── Presentation ─────────────────────────────────────────

    private AdminInsightsPanelPresentation Present()
    {
        var activity = _activity?.View ?? AdminInsightsReadView<AdminInsightsActivity>.Idle;
        var titles = _titles?.View ?? AdminInsightsReadView<AdminInsightsTitles>.Idle;
        var devices = _devices?.View ?? AdminInsightsReadView<AdminInsightsDevices>.Idle;
        var signature = (activity.Data, activity.Error, activity.IsLoading, titles.Data, titles.Error, titles.IsLoading, devices.Data, devices.Error, devices.IsLoading,
            Person, PersonName, _page.AccountName(Person?.UserId ?? 0), _profiles, _filters);
        if (_signature is { } previous && previous.Equals(signature) && _presentation != null) return _presentation;
        _signature = signature;
        var person = Person!;
        var body = Body(activity, titles, devices);
        return _presentation = new AdminInsightsPanelPresentation(P.Heading(PersonName), P.Description(person, _page.AccountName(person.UserId), activity.Data), body, null,
            Supplement(person, activity.Data));
    }

    private FrameworkElement Body(AdminInsightsReadView<AdminInsightsActivity> activity, AdminInsightsReadView<AdminInsightsTitles> titles, AdminInsightsReadView<AdminInsightsDevices> devices)
    {
        var sections = new List<FrameworkElement?>();
        if (activity.Data is { } data)
        {
            sections.Add(AdminInsightsLayout.PanelKpiGrid(P.Kpis(data)));
            sections.Add(Section(I("web.admin.insights.detail.playsOverTime"), PlaysChart(data)));
        }
        else if (activity.ShowsError) sections.Add(AdminInsightsUi.SectionError(activity.Error!.Message, () => _ = RequestActivity(force: true)));
        else sections.Add(new StackPanel { Spacing = 12, Children = { DashboardUi.Skeleton(96, 12), DashboardUi.Skeleton(160, 12) } });

        sections.Add(Section(I("web.admin.insights.personDetail.topTitles"),
            titles.Data is { } titleData ? AdminInsightsLayout.RowList(P.TopTitles(titleData), value => AdminInsightsRules.PlayCount((long)Math.Round(value)),
                    I("web.admin.insights.detail.noPlays"), 0, open: row => row.TitleId is { } id ? () => _page.OpenTitle(id) : null)
                : titles.ShowsError ? AdminInsightsUi.SectionError(titles.Error!.Message, () => _ = RequestTitles(force: true))
                : DashboardChartChrome.BarListSkeleton()));
        sections.Add(Section(I("web.admin.insights.personDetail.clients"),
            devices.Data is { } deviceData ? AdminInsightsLayout.RowList(P.Clients(deviceData), value => AdminInsightsRules.SessionCount((long)Math.Round(value)),
                    I("web.admin.insights.personDetail.clientsEmpty"), 1)
                : devices.ShowsError ? AdminInsightsUi.SectionError(devices.Error!.Message, () => _ = RequestDevices(force: true))
                : DashboardChartChrome.BarListSkeleton()));
        var stack = AdminInsightsLayout.Stack([.. sections]);
        stack.Margin = new Thickness(0, 16, 0, 0);
        return stack;
    }

    private static FrameworkElement Section(string heading, FrameworkElement content)
        => new StackPanel { Spacing = 8, Children = { A.Eyebrow(heading, A.Muted, 25, 12), content } };

    private static FrameworkElement PlaysChart(AdminInsightsActivity data)
        => P.PlaysColumns(data) is { } shape
            ? DashboardInsightsCharts.Columns(shape, time => DashboardInsightsPresentation.BucketLabel(time, data.BucketUnit),
                DashboardInsightsCharts.WatchTimeOrCount(false), "", I("web.admin.insights.detail.playsOverTime"), 150, 0)
            : AdminInsightsLayout.Empty(I("web.admin.insights.activity.volume.empty"));

    // ── Header: the profile picker, Open account, View history ──

    private FrameworkElement Supplement(AdminInsightsPersonScope person, AdminInsightsActivity? activity)
    {
        var row = new DashboardWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (P.ShowsProfilePicker(person, _profiles)) row.Children.Add(ProfilePicker(person));
        var account = AdminRouteRequest.UserDetail(person.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (AdminShellPage.CanOpen(account))
            row.Children.Add(HeaderButton(I("web.admin.insights.personDetail.action.openAccount"), "user-round", "AdminInsightsPersonOpenAccount", () => AdminShellPage.Open(account)));
        // Only once the window is resolved: an unbounded link would open the person's whole history.
        if (activity != null && _filters != null)
        {
            var history = P.History(person, _filters, activity);
            row.Children.Add(HeaderButton(I("web.admin.insights.detail.action.history"), "history", "AdminInsightsPersonHistory", () => AdminShellPage.Open(history)));
        }
        return row;
    }

    private static Button HeaderButton(string label, string icon, string id, Action click)
    {
        var button = A.Outline(label, icon);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>"All profiles" and each profile of the account; choosing one changes the location in place.</summary>
    private Button ProfilePicker(AdminInsightsPersonScope person)
    {
        var options = P.ProfileOptions(person, _profiles);
        var selected = person.ProfileId ?? P.AllProfiles;
        var label = options.FirstOrDefault(option => option.Value == selected).Label ?? options[0].Label;
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(A.Text(label, 12, A.Foreground, FontWeights.Medium));
        var chevron = A.Icon("chevron-down", 14, A.Muted);
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        var button = A.Outline("", null);
        button.Content = content;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.MinWidth = 136;
        button.MaxWidth = 224;
        AutomationProperties.SetName(button, I("web.admin.insights.personDetail.profileLabel"));
        AutomationProperties.SetAutomationId(button, "AdminInsightsPersonProfile");
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        foreach (var (value, text) in options)
        {
            var item = new RadioMenuFlyoutItem { Text = text, IsChecked = value == selected, GroupName = "AdminInsightsPersonProfile" };
            item.Click += (_, _) => _page.ReplaceLocation(AdminInsightsFilters.WithPerson(_page.Location, P.WithProfile(person, value)));
            menu.Items.Add(item);
        }
        button.Flyout = menu;
        return button;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activity?.Dispose();
        _titles?.Dispose();
        _devices?.Dispose();
        _lease?.Dispose();
    }
}
