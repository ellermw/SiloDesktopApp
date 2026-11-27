using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The title panel (official <c>TitleDetailSheet.tsx</c>, c59798116): one movie or series over the page's window and filters, opened from a
/// row of the Titles or Library tab, or from a person's panel, in which case <c>person</c> scopes it to that person's plays. It reads only
/// while it is open, never keeps another title's numbers on screen while a new one loads, and says plainly when the title is gone.
/// </summary>
internal sealed class AdminInsightsTitlePanel : IAdminInsightsPanelContent, IDisposable
{
    /// <summary>The answer of one read: null when the server no longer has the title.</summary>
    private sealed record TitleRead(AdminInsightsTitle? Title);

    private readonly AdminInsightsPage _page;
    private readonly MonitorAuthorityLease? _lease;
    private readonly AdminInsightsReader<TitleRead>? _reader;
    private AdminInsightsDetailState _detail = new();
    private AdminInsightsFilterState? _filters;
    private IReadOnlyList<AdminHistoryProfileOption>? _profiles;
    private long? _profilesFor;
    private (AdminInsightsReadView<TitleRead>, string?, string?, AdminInsightsPersonScope?, AdminInsightsFilterState?)? _signature;
    private AdminInsightsPanelPresentation? _presentation;
    private bool _disposed;

    internal AdminInsightsTitlePanel(AdminInsightsPage page)
    {
        _page = page;
        _lease = page.CaptureLease();
        if (_lease == null) return;
        _reader = new AdminInsightsReader<TitleRead>(_lease, keepPrevious: false);
        _reader.Changed += () => page.RefreshPanel();
    }

    public AdminInsightsPanelPresentation ApplyState(AdminInsightsDetailState detail, AdminInsightsFilterState filters)
    {
        _detail = detail;
        _filters = filters;
        _ = Request(force: false);
        _ = ReadProfilesAsync(detail.Person);
        return Present();
    }

    public Task RefreshAsync() => _disposed ? Task.CompletedTask : Request(force: true);

    private AdminInsightsWindow Window => _filters!.ToWindow(person: _detail.Person);

    private Task Request(bool force)
    {
        if (_reader == null || _lease == null || _filters == null || _detail.TitleId is not { } id) return Task.CompletedTask;
        var window = Window;
        string key;
        try { key = AdminInsightsApi.TitlePath(id, window); }
        catch (ArgumentException error) { return _reader.Request("invalid:" + error.Message, _ => Task.FromException<TitleRead>(error), force); }
        return _reader.Request(key, async ct => new TitleRead(await _page.Insights.GetTitleAsync(_lease, id, window, ct)), force);
    }

    /// <summary>The scoped profile's name, from the account's profile list (read once per account).</summary>
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

    private string? PersonName => _detail.Person is { } person
        ? AdminInsightsPersonNames.NameOf(person, _page.AccountName(person.UserId), _profiles) : null;

    private AdminInsightsPanelPresentation Present()
    {
        var view = _reader?.View ?? AdminInsightsReadView<TitleRead>.Idle;
        var signature = (view, _detail.TitleId, PersonName, _detail.Person, _filters);
        if (_signature is { } previous && previous.Equals(signature) && _presentation != null) return _presentation;
        _signature = signature;
        var data = view.Data?.Title;
        FrameworkElement body = view.Data == null
            ? view.ShowsError ? AdminInsightsUi.SectionError(view.Error!.Message, () => _ = Request(force: true)) : Skeleton()
            : data == null ? NotFound() : Body(data);
        if (view.RefreshError is { } warning)
        {
            var retry = AdminInsightsUi.SectionError(warning.Message, () => _ = Request(force: true), retryEnabled: !view.IsFetching);
            body = AdminInsightsLayout.Stack(retry, body);
        }
        return _presentation = new AdminInsightsPanelPresentation(
            AdminInsightsTitlePanelPresentation.Heading(data), AdminInsightsTitlePanelPresentation.Description(data), body, null, Supplement(data));
    }

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    // ── States ───────────────────────────────────────────────

    private static FrameworkElement Skeleton() => new StackPanel
    {
        Spacing = 12,
        Margin = new Thickness(0, 4, 0, 0),
        Children = { DashboardUi.Skeleton(96, 12), DashboardUi.Skeleton(160, 12), DashboardUi.Skeleton(128, 12) },
    };

    private static FrameworkElement NotFound()
    {
        var text = A.Text(I("web.admin.insights.titleDetail.notFound"), 14, A.Muted, trim: false);
        text.TextAlignment = TextAlignment.Center;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(text, "AdminInsightsTitleNotFound");
        return new Border { Padding = new Thickness(0, 48, 0, 48), Child = text };
    }

    // ── Header: Open title, View history, the person banner ──

    private FrameworkElement? Supplement(AdminInsightsTitle? data)
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 0) };
        if (data != null)
        {
            var actions = new DashboardWrapPanel { Spacing = 8, RowSpacing = 8 };
            actions.Children.Add(HeaderButton(AdminPlaybackAuditText.T("web.admin.history.detail.action.openMedia"), "external-link", "AdminInsightsTitleOpen", () =>
            {
                if (App.Services.GetService<NavigationService>() is { } navigation) navigation.Navigate<ItemDetailPage>(data.ContentId);
            }));
            actions.Children.Add(HeaderButton(I("web.admin.insights.detail.action.history"), "history", "AdminInsightsTitleHistory",
                () => AdminShellPage.Open(AdminInsightsTitlePanelPresentation.History(data, _filters!, _detail.Person))));
            stack.Children.Add(actions);
        }
        if (_detail.Person is { } person)
        {
            var banner = new DashboardWrapPanel { Spacing = 8, RowSpacing = 4 };
            banner.Children.Add(A.Text(AdminInsightsTitlePanelPresentation.ScopedBanner(PersonName, person), 12, A.Muted, trim: false));
            var everyone = DashboardUi.TextLink(I("web.admin.insights.titleDetail.showEveryone"), 12, (_, _) => _page.ShowEveryone(), A.Primary);
            everyone.FontWeight = FontWeights.Medium;
            AutomationProperties.SetAutomationId(everyone, "AdminInsightsShowEveryone");
            banner.Children.Add(everyone);
            stack.Children.Add(banner);
        }
        return stack.Children.Count == 0 ? null : stack;
    }

    private static Button HeaderButton(string label, string icon, string id, Action click)
    {
        var button = A.Outline(label, icon);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    // ── Body ─────────────────────────────────────────────────

    private FrameworkElement Body(AdminInsightsTitle data)
    {
        var sections = new List<FrameworkElement?>
        {
            AdminInsightsLayout.PanelKpiGrid(AdminInsightsTitlePanelPresentation.Kpis(data)),
            Section(I("web.admin.insights.detail.playsOverTime"), PlaysChart(data)),
            Section(I("web.admin.insights.titleDetail.viewers.title"), Viewers(data), data.ViewersTruncated ? Note(I("web.admin.insights.titleDetail.viewers.truncated")) : null),
        };
        if (data.Kind == "series")
            sections.Add(Section(I("web.admin.insights.titleDetail.episodes.title"), Episodes(data),
                data.EpisodesTruncated ? Note(I("web.admin.insights.titleDetail.episodes.truncated")) : null));
        var stack = AdminInsightsLayout.Stack([.. sections]);
        stack.Margin = new Thickness(0, 16, 0, 0);
        return stack;
    }

    private static FrameworkElement Section(string heading, FrameworkElement content, FrameworkElement? note = null)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(A.Eyebrow(heading, A.Muted, 25, 12));
        stack.Children.Add(content);
        if (note != null) stack.Children.Add(note);
        return stack;
    }

    private static FrameworkElement Note(string text) => A.Text(text, 12, A.Muted, trim: false);

    private static FrameworkElement Quiet(string text)
    {
        var line = A.Text(text, 14, A.Muted, trim: false);
        line.TextAlignment = TextAlignment.Center;
        line.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Padding = new Thickness(0, 16, 0, 16), Child = line };
    }

    private static FrameworkElement PlaysChart(AdminInsightsTitle data)
    {
        if (AdminInsightsTitlePanelPresentation.PlaysColumns(data) is not { } shape)
            return AdminInsightsLayout.Empty(I("web.admin.insights.activity.volume.empty"));
        return DashboardInsightsCharts.Columns(shape, time => DashboardInsightsPresentation.BucketLabel(time, data.BucketUnit),
            DashboardInsightsCharts.WatchTimeOrCount(false), "", I("web.admin.insights.detail.playsOverTime"), 150, 0);
    }

    /// <summary>A bordered list whose rows are separated by hairlines (<c>divide-y rounded-xl border</c>).</summary>
    private static FrameworkElement Rows(IEnumerable<FrameworkElement> rows)
    {
        var stack = new StackPanel();
        var first = true;
        foreach (var row in rows)
        {
            stack.Children.Add(new Border { BorderBrush = A.Solid(A.Border, 0.5), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Child = row });
            first = false;
        }
        return new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = A.Solid(A.Border, 0.7), Child = stack };
    }

    private FrameworkElement Viewers(AdminInsightsTitle data)
    {
        var viewers = AdminInsightsTitlePanelPresentation.Viewers(data);
        if (viewers.Count == 0) return Quiet(I("web.admin.insights.detail.noPlays"));
        return Rows(viewers.Select(viewer =>
        {
            var grid = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement name = viewer.Person is { } person && _page.CanOpenPerson
                ? DashboardUi.TextLink(viewer.Name, 14, (_, _) => _page.OpenPerson(person), A.Foreground, underlineOnHover: true)
                : A.Text(viewer.Name, 14, A.Foreground, FontWeights.Medium);
            if (name is Button { Content: TextBlock linkText }) { linkText.TextTrimming = TextTrimming.CharacterEllipsis; linkText.FontWeight = FontWeights.Medium; }
            name.HorizontalAlignment = HorizontalAlignment.Left;
            var left = new StackPanel { Spacing = 2, Children = { name, A.Text(viewer.Detail, 12, A.Muted) } };
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Spacing = 2 };
            var plays = A.Text(viewer.Plays, 12, A.Foreground, FontWeights.SemiBold, trim: false);
            plays.HorizontalAlignment = HorizontalAlignment.Right;
            var meta = A.Text(viewer.Meta, 12, A.Muted, trim: false);
            meta.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(plays);
            right.Children.Add(meta);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            return (FrameworkElement)grid;
        }));
    }

    private static FrameworkElement Episodes(AdminInsightsTitle data)
    {
        var episodes = AdminInsightsTitlePanelPresentation.Episodes(data);
        if (episodes.Count == 0) return Quiet(I("web.admin.insights.detail.noPlays"));
        return Rows(episodes.Select(episode =>
        {
            var grid = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var code = A.Text(episode.Code, 12, A.Muted);
            code.FontFamily = A.Mono;
            var title = A.Text(episode.Title, 12, A.Foreground);
            Grid.SetColumn(title, 1);
            var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            counts.Children.Add(A.Text(episode.Plays, 12, A.Foreground, FontWeights.SemiBold, trim: false));
            counts.Children.Add(A.Text(episode.Viewers, 12, A.Muted, trim: false));
            Grid.SetColumn(counts, 2);
            grid.Children.Add(code);
            grid.Children.Add(title);
            grid.Children.Add(counts);
            return (FrameworkElement)grid;
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader?.Dispose();
        _lease?.Dispose();
    }
}
