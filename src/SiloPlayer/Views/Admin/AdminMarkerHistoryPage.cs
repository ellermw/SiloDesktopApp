using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using U = SiloPlayer.Views.Admin.AdminPlaybackAuditUi;
using P = SiloPlayer.Core.Services.Admin.AdminMarkerHistoryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Admin › Playback › Skip Markers (official Silo e61187da <c>pages/AdminMarkerHistory.tsx</c>):
/// the read-only marker EDIT HISTORY. A count selector (25/50/100, default 50,
/// server cap 100) and Refresh; when, item (link), segment, set/cleared with the
/// before → after range, actor and request. No marker editing, deletion,
/// redetection or contribution action exists on this page.
/// </summary>
public sealed partial class AdminMarkerHistoryPage : Page, IAdminPageSuspension
{
    private readonly AdminMarkerHistoryApi? _api;
    private readonly AdminMonitorAuthority? _authority;
    private readonly StackPanel _body = Stack(24);
    private readonly ComboBox _limit;
    private readonly Button _refresh;
    private readonly StackPanel _content = Stack(0);
    private AdminMarkerHistoryController? _controller;
    private long _renderedRevision = -1;
    private object? _renderedRows;
    private string? _renderedKind;
    private bool _navigated, _active, _syncing;

    public AdminMarkerHistoryController? Controller => _controller;
    /// <summary>Test seam: rendered edit ids, in order.</summary>
    public IReadOnlyList<string> RenderedEditIds { get; private set; } = [];
    /// <summary>Test seam: "closed", "loading", "error", "empty" or "table".</summary>
    public string RenderedContent { get; private set; } = "closed";

    public AdminMarkerHistoryPage() : this(App.Services.GetService<AdminMarkerHistoryApi>()
        ?? (App.Services.GetService<SiloApiClient>() is { } client ? new AdminMarkerHistoryApi(client) : null), App.Services.GetService<AdminMonitorAuthority>()) { }

    public AdminMarkerHistoryPage(AdminMarkerHistoryApi? api, AdminMonitorAuthority? authority)
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminMarkerHistoryPage");
        _api = api;
        _authority = authority;

        var actions = Inline(8);
        _limit = U.Picker("AdminMarkerHistoryLimit", U.T("web.admin.markers.history.limitLabel"), 128);
        U.SetOptions(_limit, AdminMarkerHistoryApi.LimitOptions.Select(count =>
            (count.ToString(System.Globalization.CultureInfo.InvariantCulture), U.T("web.admin.markers.history.limitOption", AdminPlaybackAuditText.Count(count)))),
            AdminMarkerHistoryApi.DefaultLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _limit.SelectionChanged += (_, _) => { if (!_syncing && int.TryParse(U.Selected(_limit), out var count)) _controller?.SetLimit(count); };
        actions.Children.Add(_limit);
        var label = U.T("web.admin.markers.history.refresh");
        _refresh = Button(label, "AdminMarkerHistoryRefresh");
        var refreshContent = Inline(8);
        refreshContent.Children.Add(SiloPlayer.Controls.AdminMonitorUi.Icon("refresh-cw", 16, Brush("PrimaryTextBrush")));
        refreshContent.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        _refresh.Content = refreshContent;
        _refresh.MinHeight = 32;
        _refresh.Click += (_, _) => { if (_controller != null) _ = _controller.RefreshAsync(); };
        actions.Children.Add(_refresh);
        _body.Children.Add(Header(U.T("web.admin.markers.history.title"), U.T("web.admin.markers.history.subtitle"), actions));

        // WebUI: border border-border bg-surface rounded-lg overflow-hidden.
        _body.Children.Add(new Border
        {
            Child = _content, CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Brush("SurfaceBrush"),
        });
        _body.MaxWidth = AdminShellPage.ContentMaxWidth;
        _body.HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 0, 24) };
        Loaded += (_, _) => { if (_navigated) Activate(); };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!IsLoaded) Deactivate(dispose: false); });
        RenderClosed();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _navigated = true;
        Activate();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _navigated = false;
        Deactivate(dispose: true);
    }

    public void SuspendAdminPage()
    {
        _navigated = false;
        Deactivate(dispose: true);
    }

    private void Activate()
    {
        if (_active || _api == null || _authority == null) return;
        _active = true;
        if (_controller == null)
        {
            _controller = new AdminMarkerHistoryController(_api, _authority);
            _controller.Changed += QueueRender;
            _controller.AuthorityRetired += () => DispatcherQueue.TryEnqueue(() => { if (_active) _controller?.Reopen(); });
        }
        else _controller.Resume();
        _controller.Open();
        Render();
    }

    private void Deactivate(bool dispose)
    {
        if (_controller is { } controller)
        {
            if (dispose)
            {
                controller.Changed -= QueueRender;
                controller.Dispose();
                _controller = null;
            }
            else if (_active) controller.Suspend();
        }
        _active = false;
        RenderClosed();
    }

    private void QueueRender()
    {
        var controller = _controller;
        DispatcherQueue?.TryEnqueue(() => { if (_active && ReferenceEquals(controller, _controller)) Render(); });
    }

    private void RenderClosed()
    {
        _renderedRevision = -1;
        _renderedRows = null;
        _renderedKind = null;
        _content.Children.Clear();
        RenderedEditIds = [];
        RenderedContent = "closed";
        _refresh.IsEnabled = false;
    }

    private void Render()
    {
        if (_controller is not { } controller) return;
        var state = controller.State;
        if (!state.IsOpen) { RenderClosed(); return; }
        if (state.Revision == _renderedRevision) return;
        _renderedRevision = state.Revision;
        _syncing = true;
        try { U.Select(_limit, state.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        finally { _syncing = false; }
        _refresh.IsEnabled = !state.Fetching;
        AutomationProperties.SetItemStatus(_refresh, state.Fetching ? U.T("web.admin.activity.page.refreshing") : "");

        var kind = state.Loading || state.Rows == null && !state.Failed ? "loading" : state.Failed ? "error" : state.Rows!.Count == 0 ? "empty" : "table";
        if (kind == _renderedKind && (kind != "table" || ReferenceEquals(state.Rows, _renderedRows))) return;
        _renderedKind = kind;
        _renderedRows = state.Rows;
        RenderedContent = kind;
        _content.Children.Clear();
        switch (kind)
        {
            case "loading":
                var skeleton = Stack(12);
                skeleton.Padding = new(16);
                for (var index = 0; index < 6; index++) skeleton.Children.Add(U.Skeleton(40));
                AutomationProperties.SetName(skeleton, U.T("admin.native.playbackAudit.loading"));
                _content.Children.Add(skeleton);
                RenderedEditIds = [];
                break;
            case "error":
                var error = Text(U.T("web.admin.markers.history.loadFailed"), 14);
                error.Foreground = Brush("ErrorBrush");
                error.TextAlignment = TextAlignment.Center;
                error.Margin = new(16, 32, 16, 32);
                AutomationProperties.SetAutomationId(error, "AdminMarkerHistoryError");
                AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
                _content.Children.Add(error);
                RenderedEditIds = [];
                break;
            case "empty":
                var empty = Text(U.T("web.admin.markers.history.empty"), 14, true);
                empty.TextAlignment = TextAlignment.Center;
                empty.Margin = new(16, 32, 16, 32);
                AutomationProperties.SetAutomationId(empty, "AdminMarkerHistoryEmpty");
                _content.Children.Add(empty);
                RenderedEditIds = [];
                break;
            default:
                _content.Children.Add(Table(state.Rows!));
                RenderedEditIds = state.Rows!.Select(row => row.Id).ToArray();
                break;
        }
    }

    private static LogsNativeTable Table(IReadOnlyList<AdminMarkerEditRow> rows)
    {
        var headers = new[] { "when", "item", "segment", "change", "actor", "request" }.Select(key => U.T("web.admin.markers.history.column." + key)).ToArray();
        var table = new LogsNativeTable(headers, [120, 220, 110, 240, 140, 160]);
        foreach (var row in rows)
        {
            var item = Stack(2);
            item.MaxWidth = 288;
            var label = P.ItemLabel(row);
            var labelText = U.Strong(label);
            labelText.TextTrimming = TextTrimming.CharacterEllipsis;
            labelText.TextWrapping = TextWrapping.NoWrap;
            ToolTipService.SetToolTip(labelText, label);
            if (!string.IsNullOrEmpty(row.ItemId))
            {
                var itemId = row.ItemId;
                item.Children.Add(U.Link(labelText, label, "AdminMarkerHistoryItem_" + row.Id, () =>
                {
                    if (App.Services.GetService<NavigationService>() is { } navigation) navigation.Navigate<ItemDetailPage>(itemId);
                }));
            }
            else item.Children.Add(labelText);
            item.Children.Add(Cell(P.FileLine(row), muted: true, max: 288));

            var change = Inline(8);
            change.Children.Add(U.Badge(P.ActionLabel(row), row.Action == "clear" ? U.BadgeKind.Outline : U.BadgeKind.Secondary));
            change.Children.Add(Text(P.RangeChange(row), 12, true, mono: true));

            var actor = Stack(2);
            actor.Children.Add(U.Strong(P.Actor(row)));
            if (P.ImpersonatedVia(row) is { } via) actor.Children.Add(Text(via, 12, true));

            var request = Stack(2);
            request.Children.Add(Cell(P.Request(row), muted: true, max: 208));
            request.Children.Add(Cell(P.Client(row), muted: true, max: 208));

            var when = AdminPlaybackAuditText.TryParseInstant(row.CreatedAt, out var created) ? DateTimeDisplay.FormatShortDateTime(created) : row.CreatedAt;
            var whenCell = Text(when, 12, true);
            whenCell.TextWrapping = TextWrapping.NoWrap;

            table.Add([whenCell, item, Text(P.SegmentLabel(row.Segment)), change, actor, request]);
        }
        return table;
    }
}
