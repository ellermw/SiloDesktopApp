using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using J = SiloPlayer.Core.Services.Admin.AdminRequestJson;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminRequestsPage
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _queueTimer;
    private void StopQueuePolling() { _queueTimer?.Stop(); _queueTimer = null; }
    private void ScheduleQueuePolling(MonitorAuthorityLease owner)
    {
        StopQueuePolling();
        if (!Current(owner) || _queueList == null || _view != "in_progress" || !_queueRows.Any(row => row["download"] != null || J.Items(row, "targets").Any(target => target?["download"] != null))) return;
        var timer = DispatcherQueue.CreateTimer(); _queueTimer = timer;
        timer.Interval = TimeSpan.FromSeconds(30); timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            if (!ReferenceEquals(_queueTimer, timer) || !Current(owner)) return;
            _queueTimer = null;
            try { if (!_readingQueue && !_busy && _dialog == null) await RefreshQueueAsync(poll: true); }
            catch (OperationCanceledException) when (!Current(owner)) { }
            catch (Exception error) { if (Current(owner)) Notice(AdminRequestRules.Error(error), true); }
            finally { if (Current(owner) && _queueTimer == null) ScheduleQueuePolling(owner); }
        };
        timer.Start();
    }
    private readonly List<JsonObject> _queueRows = [];
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queueCursors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _viewButtons = new(StringComparer.Ordinal);
    private string _view = "needs_approval", _search = "", _media = "";
    private string? _user, _cursor;
    private JsonObject _queueCounts = new();
    private JsonArray _users = new();
    private StackPanel? _queueList;
    private StackPanel? _bulkBar;
    private TextBlock? _bulkProgress;
    private long _queueRead;
    private bool _readingQueue;
    private string? _queueError;
    private CancellationTokenSource? _queueCancellation;

    private async Task BuildQueueAsync(MonitorAuthorityLease owner, long generation)
    {
        try { _queueCounts = await _counts.GetAsync(owner); } catch (Exception) when (Current(owner)) { _queueCounts = new(); }
        if (!Current(owner) || generation != _generation) return;
        _view = _request.Get("view") is { } requested && AdminRequestRules.Views.Contains(requested) ? requested : J.Number(_queueCounts, "needs_approval") > 0 ? "needs_approval" : "in_progress";
        _search = _request.Get("q") ?? ""; _media = _request.Get("media_type") is "movie" or "series" ? _request.Get("media_type")! : "";
        _user = int.TryParse(_request.Get("user"), out var user) && user > 0 ? user.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        ReplaceLocation("view", _view);
        Header(S("web.admin.requests.page.title"), S("web.admin.requests.page.subtitle"), Button(S("web.admin.requests.page.settingsLink"), "AdminRequestSettingsLink", () => NavigateAsync(AdminRoute.RequestSettings)),
            Button(S("web.admin.requests.queue.refresh"), "AdminRequestQueueRefresh", () => RefreshQueueAsync(true)));
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _viewButtons.Clear();
        foreach (var view in AdminRequestRules.Views)
        {
            var target = view;
            var tab = Button(ViewLabel(view) + "  " + AdminText.FormatNumber(J.Number(_queueCounts, view)), "AdminRequestView-" + view, async () =>
            { _view = target; ReplaceLocation("view", target); await RefreshQueueAsync(); }, view == _view);
            _viewButtons.Add(view, tab); tabs.Children.Add(tab);
        }
        _content.Children.Add(new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var search = Input(_search, "AdminRequestQueueSearch", S("web.admin.requests.queue.searchPlaceholder")); search.MaxLength = 200;
        var media = Select("AdminRequestQueueMedia", [("", S("web.admin.requests.queue.media.all")), ("movie", S("web.admin.requests.queue.media.movies")), ("series", S("web.admin.requests.queue.media.series"))], _media);
        // These two are event handlers, not Button actions: a read that fails or is superseded under a write review rethrows and must end here.
        async Task ApplyFilter(Func<Task> refresh)
        {
            try { await refresh(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (Current(owner)) Notice(AdminRequestRules.Error(error), true); }
        }
        media.SelectionChanged += async (_, _) =>
        {
            if (!Current(owner) || Selected(media) == _media) return;
            // Like a Button, a filter is refused during a write so it cannot supersede the reconciling read; the selector returns to the applied filter.
            if (_busy) { media.SelectedItem = media.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, _media)); return; }
            _media = Selected(media); ReplaceLocation("media_type", _media.Length == 0 ? null : _media); await ApplyFilter(() => RefreshQueueAsync());
        };
        async Task Search() { _search = search.Text.Trim(); ReplaceLocation("q", _search.Length == 0 ? null : _search); await RefreshQueueAsync(); }
        search.KeyDown += async (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter && Current(owner)) { args.Handled = true; if (!_busy) await ApplyFilter(Search); } };
        _content.Children.Add(Stack(Field(S("web.admin.requests.queue.searchLabel"), search), Row(media, Button(S("web.admin.requests.queue.searchLabel"), "AdminRequestQueueSearchGo", Search))));
        if (_user != null) _content.Children.Add(Button(S("web.admin.requests.queue.clearUserFilter"), "AdminRequestClearUser", async () => { _user = null; ReplaceLocation("user", null); await RefreshQueueAsync(); }));
        _bulkBar = Stack(); _content.Children.Add(_bulkBar);
        _bulkProgress = U.Text("", 13); _content.Children.Add(_bulkProgress);
        _queueList = Stack(); _content.Children.Add(_queueList);
        _queueCursors.Clear(); _queueRows.Clear(); _selected.Clear(); _cursor = null; _queueError = null;
        await ReadQueueAsync(owner, append: false);
        try { var users = await _api.ListUsersAsync(owner); if (Current(owner) && generation == _generation) { _users = users; RenderQueue(); } } catch (Exception) { }
    }
    private static string ViewLabel(string view) => S("web.admin.requests.queue.view." + (view switch { "needs_approval" => "needsApproval", "in_progress" => "inProgress", _ => view }));
    private async Task RefreshQueueAsync(bool counts = false, bool poll = false)
    {
        var owner = Owner; if (!Current(owner)) return;
        ClearReadNotice();
        if (counts) { try { _queueCounts = await _counts.GetAsync(owner!, true); } catch (Exception error) when (Current(owner)) { Notice(AdminRequestRules.Error(error), true); } }
        if (!Current(owner)) return;
        foreach (var (view, button) in _viewButtons)
        {
            button.Content = ViewLabel(view) + "  " + AdminText.FormatNumber(J.Number(_queueCounts, view));
            button.Background = view == _view ? U.Accent : U.Brush("SurfaceBrush");
        }
        // A poll keeps the rows on screen until its read succeeds, so one failed poll neither blanks the list nor ends polling.
        if (!poll) { _selected.Clear(); _queueCursors.Clear(); _queueRows.Clear(); _cursor = null; }
        await ReadQueueAsync(owner!, false, poll);
    }
    private async Task ReadQueueAsync(MonitorAuthorityLease owner, bool append, bool poll = false)
    {
        if (append && _readingQueue) return;
        _queueCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); _queueCancellation = cancellation;
        var read = ++_queueRead; _readingQueue = true;
        try
        {
            var page = await _api.ListQueueAsync(owner, _view, _search, _media, _user, append ? _cursor : null, cancellation.Token);
            if (!Current(owner) || read != _queueRead) { if (_pendingWriteReview != null) throw new OperationCanceledException(); return; }
            var next = AdminRequestsApi.NextCursor(page);
            if (!poll && next != null && !_queueCursors.Add(next)) throw new InvalidDataException("Repeated request continuation.");
            var rows = poll ? new List<JsonObject>() : _queueRows;
            var ids = rows.Select(row => J.Text(row, "id")).ToHashSet(StringComparer.Ordinal);
            foreach (var row in J.Items(page).OfType<JsonObject>())
            { if (!ids.Add(J.Text(row, "id"))) throw new InvalidDataException("Repeated request row."); if (rows.Count >= AdminRequestsApi.RetainedQueueLimit) throw new InvalidDataException("Narrow the filters before loading more requests."); rows.Add((JsonObject)row.DeepClone()); }
            if (poll) { _queueCursors.Clear(); if (next != null) _queueCursors.Add(next); _queueRows.Clear(); _queueRows.AddRange(rows); }
            _cursor = next; _queueError = null; RenderQueue(); ScheduleQueuePolling(owner);
        }
        catch (Exception error)
        {
            if (Current(owner) && read == _queueRead) { var message = AdminRequestRules.Error(error); if (!append && !poll) _queueError = message; Notice(message, true); if (!poll) RenderQueue(); }
            if (_pendingWriteReview != null) throw;
        }
        finally { if (ReferenceEquals(_queueCancellation, cancellation)) _queueCancellation = null; if (read == _queueRead) _readingQueue = false; }
    }
    private void RenderQueue()
    {
        if (_queueList == null) return;
        _queueList.Children.Clear(); RenderBulk();
        // An empty list says nothing is waiting only after a read succeeded; a failed read offers a retry instead.
        if (_queueRows.Count == 0 && _queueError != null)
            _queueList.Children.Add(Card(Stack(U.Text("Requests could not be loaded", 18), U.Text(_queueError, 14, true), Button("Try again", "AdminRequestQueueRetry", () => RefreshQueueAsync(true)))));
        else if (_queueRows.Count == 0)
        {
            var filtered = _search.Length > 0 || _media.Length > 0 || _user != null;
            _queueList.Children.Add(Card(Stack(U.Text(S(filtered ? "web.admin.requests.queue.noMatches.title" : "web.admin.requests.queue.allClear"), 18),
                U.Text(S(filtered ? "web.admin.requests.queue.noMatches.detail" : "web.admin.requests.queue.view." + (_view switch { "needs_approval" => "needsApproval", "in_progress" => "inProgress", _ => _view }) + "Empty"), 14, true))));
        }
        foreach (var row in _queueRows) _queueList.Children.Add(QueueRow(row));
        if (_cursor != null)
        {
            if (_queueRows.Count < AdminRequestsApi.RetainedQueueLimit) _queueList.Children.Add(Button(S("web.admin.requests.queue.loadMore"), "AdminRequestLoadMore", () => ReadQueueAsync(Owner!, true)));
            else _queueList.Children.Add(U.Text("500 requests shown. Narrow the filters to see another window.", 13, true));
        }
    }
    private void RenderBulk()
    {
        if (_bulkBar == null) return; _bulkBar.Children.Clear();
        if (_view != "needs_approval") return;
        var eligible = _queueRows.Where(row => AdminRequestRules.Actions(row).Contains("approve")).ToArray();
        var select = new CheckBox { Content = S("web.admin.requests.bulk.selectAll", Count(eligible.Length)), IsThreeState = true,
            IsChecked = _selected.Count == 0 ? false : _selected.Count == eligible.Length ? true : null, IsEnabled = eligible.Length > 0 };
        var owner = Owner;
        select.Click += (_, _) => { if (!Current(owner) || _busy) return; var all = _selected.Count == eligible.Length; _selected.Clear(); if (!all) foreach (var row in eligible) _selected.Add(J.Text(row, "id")); RenderQueue(); };
        var controls = Row(select);
        if (_selected.Count > 0)
        {
            controls.Children.Add(Button(S("web.admin.requests.bulk.approve", Count(_selected.Count)), "AdminRequestBulkApprove", () => BulkAsync("approve"), true));
            controls.Children.Add(Button(S("web.admin.requests.reasonDialog.bulkDecline.confirm", Count(_selected.Count)), "AdminRequestBulkDecline", () => BulkAsync("decline")));
            controls.Children.Add(Button(S("web.admin.requests.bulk.clear"), "AdminRequestBulkClear", () => { _selected.Clear(); RenderQueue(); return Task.CompletedTask; }));
        }
        _bulkBar.Children.Add(new ScrollViewer { Content = controls, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }
    private FrameworkElement QueueRow(JsonObject request)
    {
        var id = J.Text(request, "id"); var title = J.Text(request, "title"); var owner = Owner;
        var panel = Stack(); var top = Row();
        if (_view == "needs_approval")
        {
            var selected = new CheckBox { IsChecked = _selected.Contains(id), IsEnabled = AdminRequestRules.Actions(request).Contains("approve") };
            AutomationProperties.SetName(selected, S("web.admin.requests.queue.selectRequest", new { title }));
            selected.Click += (_, _) => { if (!Current(owner) || _busy) return; if (selected.IsChecked == true) _selected.Add(id); else _selected.Remove(id); RenderBulk(); };
            top.Children.Add(selected);
        }
        var poster = J.Text(request, "poster_path");
        if (poster.StartsWith('/') && !poster.Contains('?')) top.Children.Add(new Image { Width = 40, Height = 60, Source = new BitmapImage(new Uri("https://image.tmdb.org/t/p/w92" + poster)) });
        top.Children.Add(Stack(Button(title, "AdminRequestTitle-" + id, () => OpenTitleAsync(request)), U.Text(string.Join(" · ", new[] { J.Text(request, "year"), J.Text(request, "media_type"), SeasonsText(request) }.Where(value => value.Length > 0)), 12, true)));
        panel.Children.Add(top);
        var user = J.Text(request, "requested_by_user_id");
        if (user.Length > 0) panel.Children.Add(Button(AccountName(user), "AdminRequestUser-" + id, async () => { _user = user; ReplaceLocation("user", user); await RefreshQueueAsync(); }));
        if (J.Text(request, "created_at").Length > 0) panel.Children.Add(U.Text(J.Text(request, "created_at"), 12, true));
        if (J.Text(request, "source") == "watchlist") panel.Children.Add(U.Text(S("web.admin.requests.viaWatchlist"), 12, true));
        var stateInfo = Stack(U.Text(StateText(request), 13));
        var problem = J.Text(request, "last_error"); if (problem.Length == 0) problem = J.Text(request, "outcome_reason");
        if (problem.Length > 0) stateInfo.Children.Add(U.Text(SafeDisplay(problem), 12, true));
        var targetInfo = Stack();
        foreach (var target in J.Items(request, "targets")) targetInfo.Children.Add(TargetView(target!));
        if (J.Items(request, "targets").Count == 0) targetInfo.Children.Add(U.Text(S("web.admin.requests.queue.notSent"), 12, true));
        var actions = Row();
        foreach (var action in AdminRequestRules.Actions(request))
        {
            var captured = action; var label = action == "cancel" && AdminRequestRules.View(request) == "failed" ? S("web.admin.requests.reasonDialog.close.confirm") : S("web.admin.requests.action." + action);
            if (action == "retry") label = "Retry";
            actions.Children.Add(Button(label, "AdminRequest" + char.ToUpperInvariant(action[0]) + action[1..] + "-" + id, () => ActAsync(request, captured), action == "approve"));
        }
        actions.Children.Add(Button("Details", "AdminRequestDetails-" + id, () => DetailsAsync(request)));
        var actionHost = new ScrollViewer { Content = actions, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 12 };
        grid.Children.Add(panel); grid.Children.Add(stateInfo); grid.Children.Add(targetInfo); grid.Children.Add(actionHost);
        bool? wasWide = null;
        void Layout(double width)
        {
            var wide = width >= 1040; if (wasWide == wide) return; wasWide = wide; grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            if (wide)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                foreach (var ratio in new[] { 1.4, 1.0, 1.3 }) grid.ColumnDefinitions.Add(new() { Width = new(ratio, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            }
            else { grid.ColumnDefinitions.Add(new()); for (var index = 0; index < 4; index++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            for (var index = 0; index < grid.Children.Count; index++) { var child = (FrameworkElement)grid.Children[index]; Grid.SetColumn(child, wide ? index : 0); Grid.SetRow(child, wide ? 0 : index); }
        }
        grid.SizeChanged += (_, e) => Layout(e.NewSize.Width); Layout(ActualWidth);
        return Card(grid);
    }
    private string AccountName(string id) => _users.FirstOrDefault(row => J.Text(row, "id") == id) is { } user ? J.Text(user, "username") : S("web.admin.requests.userFallback", new { id });
    private static string SafeDisplay(string text) => text.Contains("://", StringComparison.Ordinal) || text.Contains("token", StringComparison.OrdinalIgnoreCase) || text.Contains("api_key", StringComparison.OrdinalIgnoreCase) ? "The server reported a request error. Review server diagnostics for details." : text;
    private static string StateText(JsonNode request) => RequestViewerPolicy.Label(J.Text(request, "status"), J.Text(request, "outcome"), J.Text(request, "state"));
    private static string SeasonsText(JsonNode request) => J.Items(request, "seasons").Count > 0 ? "Seasons " + string.Join(", ", J.Items(request, "seasons").Select(node => node?.ToString())) : "";
    private FrameworkElement TargetView(JsonNode target)
    {
        var name = J.Text(target, "instance_name"); if (name.Length == 0) name = J.Text(target, "integration_kind");
        var panel = Stack(U.Text(J.Text(target, "quality") + " · " + name + " · " + J.Text(target, "status"), 13));
        if (J.Text(target, "external_status").Length > 0) panel.Children.Add(U.Text(SafeDisplay(J.Text(target, "external_status")), 12, true));
        if (J.Text(target, "route_name").Length > 0) panel.Children.Add(U.Text(S("web.admin.requests.queue.byRoute", new { route = J.Text(target, "route_name") }), 12, true));
        if (target["download"] is JsonObject download)
        {
            var model = JsonSerializer.Deserialize<RequestDownload>(download.ToJsonString(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            if (model != null) panel.Children.Add(RequestDownloadProgress.Build(model, 240));
            foreach (var key in new[] { "status", "status_message", "error_message" }) if (J.Text(download, key).Length > 0) panel.Children.Add(U.Text(SafeDisplay(J.Text(download, key)), 12, true));
        }
        if (J.Text(target, "last_error").Length > 0) panel.Children.Add(U.Text(SafeDisplay(J.Text(target, "last_error")), 12, true));
        return panel;
    }
    private Task OpenTitleAsync(JsonObject request)
    {
        var media = J.Text(request, "media_type"); var tmdb = J.Number(request, "tmdb_id");
        App.Services.GetRequiredService<NavigationService>().Navigate<SiloPlayer.Views.RequestDetailPage>(new RequestDetailNavigation(media, tmdb)); return Task.CompletedTask;
    }
    private async Task ActAsync(JsonObject request, string action)
    {
        if (!AdminRequestRules.Actions(request).Contains(action)) return;
        if (action is "decline" or "cancel")
        {
            var kind = action == "cancel" && AdminRequestRules.View(request) == "failed" ? "close" : action;
            var reason = Input("", "AdminRequestReason", S("web.admin.requests.reason.placeholder")); reason.AcceptsReturn = true; reason.TextWrapping = TextWrapping.Wrap;
            await EditAsync(S("web.admin.requests.reasonDialog." + kind + ".title"), Stack(U.Text(S("web.admin.requests.reasonDialog." + kind + ".description", new { title = J.Text(request, "title") })), Field(S("web.admin.requests.reason.label"), reason)),
                lease => _api.ActionAsync(lease, J.Text(request, "id"), action, reason.Text), S("web.admin.requests.reasonDialog." + kind + ".confirm"));
            if (Owner is { IsCurrent: true }) await RefreshQueueAsync(true);
        }
        else await WriteAsync(lease => _api.ActionAsync(lease, J.Text(request, "id"), action), () => RefreshQueueAsync(true));
    }
    private async Task BulkAsync(string action)
    {
        var rows = _queueRows.Where(row => _selected.Contains(J.Text(row, "id"))).Select(row => (JsonObject)row.DeepClone()).ToArray();
        async Task Send(MonitorAuthorityLease lease, string? reason)
        {
            var controller = new AdminRequestsController(_api);
            var result = await controller.BulkAsync(lease, rows, action, reason, progress => DispatcherQueue.TryEnqueue(() =>
            { if (lease.IsCurrent && _bulkProgress != null) _bulkProgress.Text = S("web.admin.requests.bulk." + (action == "approve" ? "approvingProgress" : "decliningProgress"), new { count = progress.Total, count_text = progress.Total, done = progress.Done }); }));
            if (!lease.IsCurrent) return;
            _selected.Clear(); foreach (var failure in result.Failures) _selected.Add(failure.Id);
            if (_bulkProgress != null) _bulkProgress.Text = S("web.admin.requests.bulk." + (action == "approve" ? "approveResult" : "declineResult"), new { count = result.Failures.Count, count_text = result.Failures.Count, succeeded = result.Total - result.Failures.Count, total = result.Total }) +
                string.Join("", result.Failures.Select(f => "\n" + S("web.admin.requests.bulk.failure", new { title = f.Title, message = f.Message })));
        }
        if (action == "decline")
        {
            var reason = Input("", "AdminRequestReason", S("web.admin.requests.reason.placeholder"));
            await EditAsync(S("web.admin.requests.reasonDialog.bulkDecline.title", Count(rows.Length)), Stack(U.Text(S("web.admin.requests.reasonDialog.bulkDecline.description", Count(rows.Length))), Field(S("web.admin.requests.reason.label"), reason)),
                lease => Send(lease, reason.Text), S("web.admin.requests.reasonDialog.bulkDecline.confirm", Count(rows.Length)));
        }
        else await WriteAsync(lease => Send(lease, null));
        var retained = _selected.ToArray();
        await RefreshQueueAsync(true); foreach (var id in retained) if (_queueRows.Any(row => J.Text(row, "id") == id)) _selected.Add(id); RenderQueue();
    }
    private async Task DetailsAsync(JsonObject request)
    {
        if (_lifetime == null || _dialog != null) return;
        using var lease = _lifetime.CaptureAction();
        JsonArray events = new(); string? historyError = null;
        try { events = await _api.GetEventsAsync(lease, J.Text(request, "id")); } catch (Exception error) { historyError = AdminRequestRules.Error(error); }
        lease.ThrowIfRetired();
        var fields = Stack(U.Text(StateText(request)), U.Text(S("web.admin.requests.detail.requestedBy") + ": " + AccountName(J.Text(request, "requested_by_user_id"))), U.Text("TMDB ID: " + J.Number(request, "tmdb_id")), U.Text(SeasonsText(request), 13, true));
        foreach (var (key, label) in new[] { ("created_at", "requested"), ("approved_at", "approved"), ("completed_at", "completed") })
            if (J.Text(request, key).Length > 0) fields.Children.Add(U.Text(S("web.admin.requests.detail." + label) + ": " + J.Text(request, key), 13, true));
        foreach (var season in J.Items(request, "season_progress"))
            fields.Children.Add(U.Text(J.Number(season, "episodes_aired") > 0 ? S("web.admin.requests.detail.seasonProgressOfAired", new { season = J.Number(season, "season_number"), available = J.Number(season, "episodes_available"), count = J.Number(season, "episodes_aired"), count_text = J.Number(season, "episodes_aired") }) :
                S("web.admin.requests.detail.seasonProgress", new { season = J.Number(season, "season_number"), count = J.Number(season, "episodes_available"), count_text = J.Number(season, "episodes_available") }), 12, true));
        foreach (var target in J.Items(request, "targets")) fields.Children.Add(Card(TargetView(target!)));
        fields.Children.Add(U.Text(S("web.admin.requests.detail.section.history"), 18));
        foreach (var entry in events) fields.Children.Add(U.Text(EventText(entry!), 13, true));
        if (historyError != null) fields.Children.Add(U.Text(historyError, 13, true));
        if (J.Flag(_capabilities, "routing") && AdminRequestRules.View(request) is "needs_approval" or "failed")
            fields.Children.Add(Button(S("web.admin.requests.detail.section.routing"), "AdminRequestDetailsPreview", async () =>
            {
                var token = _dialogReads?.Token ?? lease.Token;
                var result = await _api.PreviewRouteAsync(lease, J.Text(request, "media_type"), J.Number(request, "tmdb_id"), J.Text(request, "requested_by_user_id") is { Length: > 0 } user ? user : null, token);
                lease.ThrowIfRetired(); token.ThrowIfCancellationRequested();
                var rendered = await RoutePreviewAsync(result, lease, token);
                lease.ThrowIfRetired(); token.ThrowIfCancellationRequested(); fields.Children.Add(rendered);
            }));
        string? chosen = null;
        var actions = Row();
        foreach (var action in AdminRequestRules.Actions(request))
        {
            var captured = action;
            actions.Children.Add(Button(action == "retry" ? "Retry" : S("web.admin.requests.action." + action), "AdminRequestDetailAction-" + action, () => { chosen = captured; _dialog?.Hide(); return Task.CompletedTask; }));
        }
        actions.Children.Add(Button(S("web.admin.requests.detail.titlePage"), "AdminRequestDetailTitlePage", () => { chosen = "title"; _dialog?.Hide(); return Task.CompletedTask; }));
        if (J.Text(request, "library_content_id").Length > 0) actions.Children.Add(Button(S("web.admin.requests.detail.openInLibrary"), "AdminRequestDetailLibrary", () => { chosen = "library"; _dialog?.Hide(); return Task.CompletedTask; }));
        fields.Children.Add(actions);
        await EditAsync(J.Text(request, "title"), fields, _ => Task.CompletedTask, S("web.admin.requests.detail.close"), mutates: false);
        if (!lease.IsCurrent) return;
        if (chosen == "title") await OpenTitleAsync(request);
        else if (chosen == "library")
        {
            var id = J.Text(request, "library_content_id");
            var access = await RequestLibraryLinks.CheckAsync(id, App.Services.GetRequiredService<ItemDetailPrefetchCache>(), () => lease.IsCurrent && IsLoaded, lease.Token);
            if (access == RequestLibraryAccess.Open && lease.IsCurrent) App.Services.GetRequiredService<NavigationService>().Navigate<SiloPlayer.Views.ItemDetailPage>(id);
            else if (access == RequestLibraryAccess.Unavailable) Notice(RequestLibraryLinks.UnavailableNotice);
        }
        else if (chosen != null) await ActAsync(request, chosen);
    }
    private static string EventText(JsonNode entry)
    {
        var type = J.Text(entry, "type"); var key = type switch { "status_approved" => "approved", "status_queued" => "queued", "status_downloading" => "downloading", "status_completed" => "downloaded", "available_in_library" => "inLibrary", "outcome_declined" => "declined", "outcome_cancelled" => "cancelled", "outcome_failed" => "failed", "submit_deferred" => "deferred", _ => type };
        var label = S("web.admin.requests.event." + key); if (label.StartsWith("web.", StringComparison.Ordinal)) label = type;
        return string.Join(" · ", new[] { J.Text(entry, "created_at"), label, J.Text(entry, "actor_username"), SafeDisplay(J.Text(entry, "message")) }.Where(value => value.Length > 0));
    }
}
