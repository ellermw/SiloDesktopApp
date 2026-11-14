using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminLogsPage : Page
{
    private readonly AdminMonitorApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly LogsStreamClient? _stream;
    private readonly Grid _root = new();
    private readonly StackPanel _body = Stack(24);
    private readonly StackPanel _results = Stack(16);
    private readonly StackPanel _summary = Stack(12);
    private readonly LogsWrapPanel _history = new();
    private readonly LogsWrapPanel _appFilters = new();
    private readonly LogsWrapPanel _auditFilters = new();
    private readonly Dictionary<string, TextBox> _inputs = new();
    private readonly TextBlock _connection = Text("", 14);
    private readonly TextBlock _connectionError = Text("", 12, true);
    private readonly TextBlock _sessionChip = Text("", 12);
    private readonly Border _sessionChipHost;
    private readonly Button _reconnect;
    private readonly Button _appTab;
    private readonly Button _auditTab;
    private readonly Button _focusedFFmpeg;
    private readonly ComboBox _level;
    private readonly AutoSuggestBox _component;
    private static readonly string[] LogsComponents = ["access", "activitylog", "adminjob", "ai", "api", "app", "audiobooks", "auth", "autoscan", "catalog", "chapterthumbs", "diagnostics", "downloads", "ebooks", "ffmpeg", "historyimport", "jellycompat", "libraryingest", "manga", "metadata", "nodeconfig", "nodepool", "noderecipe", "nodesessions", "notifications", "opslog", "playback", "plugins", "policy", "proxy", "ratelimit", "recommendations", "requests", "scanner", "scanqueue", "sections", "settings", "taskmanager", "telemetry", "transcodenode", "watchlist", "watchsync", "webhook_sync", "webhooksync", "worker"];
    private readonly LogsDetailSheet _sheet;
    private LogsPageController? _controller;
    private LogsQuery _query = new();
    private string _tab = "app";
    private bool _playbackFocus;
    private bool _syncing;
    private long _detailSelection;
    public LogsPageController? Controller => _controller;
    public StackPanel MonitorScrollContent => _body;

    public AdminLogsPage() : this(App.Services.GetRequiredService<AdminMonitorApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>(), App.Services.GetService<LogsStreamClient>()) { }
    public AdminLogsPage(AdminMonitorApi api, AdminMonitorAuthority authority, LogsStreamClient? stream = null)
    {
        InitializeComponent();
        _api = api; _authority = authority; _stream = stream; NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminLogsPage");
        var status = Stack(); var streamLabel = Text(T("web.admin.logs.page.streamLabel").ToUpperInvariant(), 12, true); streamLabel.CharacterSpacing = 200; streamLabel.TextAlignment = TextAlignment.Right; _connection.TextAlignment = TextAlignment.Right; _connectionError.TextAlignment = TextAlignment.Right;
        status.Children.Add(streamLabel); status.Children.Add(_connection); status.Children.Add(_connectionError);
        _reconnect = Button(T("web.admin.logs.page.reconnect"), "LogsReconnect", "GhostButtonStyle"); _reconnect.MinHeight = 28; _reconnect.FontSize = 12;
        _reconnect.Click += (_, _) => { if (_controller != null) _ = _controller.ReconnectAsync(); }; status.Children.Add(_reconnect);
        _body.Children.Add(Header(T("web.admin.logs.page.title"), T("web.admin.logs.page.subtitle"), status));
        var playback = new LogsWrapPanel(); playback.Children.Add(InputField("playbackSessionId", "playback", 448, true)); _sessionChipHost = new Border { Child = _sessionChip, CornerRadius = new(10), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Brush("AppBackgroundBrush"), Padding = new(8, 4, 8, 4), Visibility = Visibility.Collapsed }; playback.Children.Add(_sessionChipHost);
        _body.Children.Add(Panel(playback)); _body.Children.Add(_summary);
        var tabs = Inline(3); tabs.Padding = new(3); tabs.CornerRadius = new(12); tabs.Background = Brush("SurfaceBrush"); tabs.HorizontalAlignment = HorizontalAlignment.Left;
        _appTab = Button(T("web.admin.logs.tabs.application"), "LogsApplicationTab", "GhostButtonStyle"); _auditTab = Button(T("web.admin.logs.tabs.audit"), "LogsAuditTab", "GhostButtonStyle");
        foreach (var button in new[] { _appTab, _auditTab }) { button.MinHeight = 30; button.Padding = new(8, 4, 8, 4); button.CornerRadius = new(10); button.FontWeight = Microsoft.UI.Text.FontWeights.Medium; tabs.Children.Add(button); }
        _appTab.Click += (_, _) => SelectTab("app"); _auditTab.Click += (_, _) => SelectTab("audit");
        foreach (var tab in new[] { _appTab, _auditTab }) tab.KeyDown += (_, args) => { if (args.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End) { var audit = args.Key is Windows.System.VirtualKey.Right or Windows.System.VirtualKey.End; SelectTab(audit ? "audit" : "app"); (audit ? _auditTab : _appTab).Focus(FocusState.Keyboard); args.Handled = true; } };
        _appFilters.Children.Add(InputField("requestId", "request", 320, true)); _appFilters.Children.Add(InputField("messageContains", "message", 384));
        _level = Select("web.admin.logs.filters.level", new[] { ("", T("web.admin.logs.filters.allLevels")) }.Concat(new[] { "debug", "info", "warn", "error" }.Select(value => (value, T("web.admin.logs.level." + value)))));
        _level.SelectionChanged += (_, _) => FilterChanged(); _appFilters.Children.Add(_level);
        _component = new AutoSuggestBox { PlaceholderText = T("web.admin.logs.filters.allComponents"), Width = 320, Height = 36, FontSize = 14 }; AutomationProperties.SetName(_component, T("web.admin.logs.filters.component")); AutomationProperties.SetAutomationId(_component, "LogsFilter_component");
        _component.TextChanged += (_, _) => { if (_syncing) return; _component.ItemsSource = LogsComponents.Where(value => value.Contains(_component.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); FilterChanged(); }; _component.SuggestionChosen += (_, args) => _component.Text = (string)args.SelectedItem; _appFilters.Children.Add(_component);
        _focusedFFmpeg = Button(T("web.admin.logs.ffmpeg.filter"), "LogsFocusedFFmpeg"); _focusedFFmpeg.FontSize = 12; _focusedFFmpeg.Click += (_, _) => ToggleFFmpeg(); _appFilters.Children.Add(_focusedFFmpeg);
        _auditFilters.Children.Add(InputField("requestId", "auditRequest", 320, true)); _auditFilters.Children.Add(InputField("method", "method", 120)); _auditFilters.Children.Add(InputField("clientIp", "ip", 320, true));
        foreach (var (label, key, width) in new[] { ("auditActionPlaceholder", "action", 320), ("auditActorPlaceholder", "actor", 180), ("auditTargetTypePlaceholder", "targetType", 320), ("auditTargetIdPlaceholder", "targetId", 180) }) _auditFilters.Children.Add(InputField(label, key, width));
        var content = Stack(16); content.Children.Add(Panel(_appFilters, 24, 12, true)); content.Children.Add(Panel(_auditFilters, 24, 12, true)); content.Children.Add(_results); var tabbed = Stack(8); tabbed.Children.Add(tabs); tabbed.Children.Add(content); _body.Children.Add(tabbed); _body.Children.Add(_history);
        var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 0, 24) };
        _body.MaxWidth = 1560; _body.HorizontalAlignment = HorizontalAlignment.Stretch; _root.Children.Add(scroll); _sheet = new(scroll, 672); _root.Children.Add(_sheet); Content = _root;
        Loaded += (_, _) => Open(); Unloaded += (_, _) => Close();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not AdminRouteRequest route) return;
        _query = new() { RequestId = route.Get("request_id"), PlaybackSessionId = route.Get("playback_session_id"), Message = route.Get("q"), Level = route.Get("level"), Component = route.Get("component"), Method = route.Get("method"), ClientIp = route.Get("client_ip"), Action = route.Get("action"), ActorUserId = route.Get("actor_user_id"), TargetId = route.Get("target_id"), TargetType = route.Get("target_type"), From = route.Get("from"), To = route.Get("to"), NodeId = route.Get("node_id"), UserId = route.Get("user_id"), SessionId = route.Get("session_id"), StatusCode = route.Get("status_code"), PathPrefix = route.Get("path_prefix") };
        _tab = route.Get("tab") == "audit" ? "audit" : "app"; _playbackFocus = route.Get("focus") == "playback"; SyncInputs();
    }
    private TextBox InputField(string label, string key, double width, bool mono = false)
    {
        var field = Input(T("web.admin.logs.filters." + label), "LogsFilter_" + key, width, mono); _inputs[key] = field;
        field.TextChanged += (_, _) => { if (_syncing) return; _syncing = true; try { if (key == "request") _inputs["auditRequest"].Text = field.Text; else if (key == "auditRequest") _inputs["request"].Text = field.Text; } finally { _syncing = false; } FilterChanged(); };
        return field;
    }
    private void SyncInputs()
    {
        _syncing = true;
        try
        {
            foreach (var (key, value) in new[] { ("playback", _query.PlaybackSessionId), ("request", _query.RequestId), ("auditRequest", _query.RequestId), ("message", _query.Message), ("method", _query.Method), ("ip", _query.ClientIp), ("action", _query.Action), ("actor", _query.ActorUserId), ("targetType", _query.TargetType), ("targetId", _query.TargetId) }) _inputs[key].Text = value ?? "";
            _component.Text = _query.Component ?? "";
            SetSelected(_level, _query.Level is "all" ? "" : _query.Level);
        }
        finally { _syncing = false; }
    }
    private void FilterChanged()
    {
        if (_syncing || _controller == null) return;
        _query = _query with { PlaybackSessionId = _inputs["playback"].Text, RequestId = _inputs[_tab == "app" ? "request" : "auditRequest"].Text, Message = _inputs["message"].Text, Level = Selected(_level), Component = _component.Text, Method = _inputs["method"].Text, ClientIp = _inputs["ip"].Text, Action = _inputs["action"].Text, ActorUserId = _inputs["actor"].Text, TargetType = _inputs["targetType"].Text, TargetId = _inputs["targetId"].Text };
        _sheet.Close(); _ = _controller.SelectAsync(_tab, _query);
    }
    private void SelectTab(string tab) { if (_tab == tab) return; _tab = tab; FilterChanged(); }
    private void Open()
    {
        Close(); SyncInputs(); _controller = new(_api, _authority, _stream); _controller.Changed += QueueRender; _authority.AvailabilityChanged += OnAvailabilityChanged; _ = _controller.OpenAsync(_tab, _query, false);
    }
    private void OnAvailabilityChanged()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || controller == null || !ReferenceEquals(controller, _controller) || controller.State.Owner is { IsCurrent: true }) return;
            Render(controller.State);
            if (_authority.IsAvailable) _ = controller.OpenAsync(_tab, _query, false);
        });
    }
    internal void SuspendMonitor() => Close();

    private void Close()
    {
        _authority.AvailabilityChanged -= OnAvailabilityChanged;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Dispose(); _controller = null; }
        _sheet.Close(); _results.Children.Clear(); _summary.Children.Clear(); _connection.Text = ""; _connectionError.Text = ""; _history.Children.Clear(); _sessionChip.Text = "";
    }
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return; var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (IsLoaded && ReferenceEquals(controller, _controller) && controller.IsCurrent(state)) Render(state); });
    }
    private void Render(LogsPageSnapshot state)
    {
        _results.Children.Clear(); _history.Children.Clear(); _summary.Children.Clear(); _summary.Visibility = Visibility.Collapsed;
        if (state.Owner == null) { _sheet.Close(); _connection.Text = ""; _connectionError.Text = ""; _sessionChip.Text = ""; return; }
        if (_sheet.Visibility == Visibility.Visible && _detailSelection != state.Selection) _sheet.Close();
        _connection.Text = T(state.History ? "web.admin.logs.page.historicalResults" : "web.admin.logs.connection." + state.Connection); _connectionError.Text = state.History ? "" : state.Error ?? "";
        _connectionError.Visibility = string.IsNullOrEmpty(_connectionError.Text) ? Visibility.Collapsed : Visibility.Visible;
        _reconnect.Visibility = !state.History && state.Connection == "disconnected" ? Visibility.Visible : Visibility.Collapsed;
        _appFilters.Visibility = state.Stream == "app" ? Visibility.Visible : Visibility.Collapsed; ((FrameworkElement)_appFilters.Parent).Visibility = _appFilters.Visibility;
        _auditFilters.Visibility = state.Stream == "audit" ? Visibility.Visible : Visibility.Collapsed; ((FrameworkElement)_auditFilters.Parent).Visibility = _auditFilters.Visibility;
        foreach (var tab in new[] { _appTab, _auditTab }) { var selected = ReferenceEquals(tab, _appTab) == (state.Stream == "app"); tab.Background = selected ? Brush("SurfaceRaisedBrush") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); tab.Foreground = Brush(selected ? "PrimaryTextBrush" : "SecondaryTextBrush"); tab.BorderBrush = selected ? Brush("BorderBrush") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); tab.BorderThickness = new(1); }
        var session = state.Query.PlaybackSessionId ?? ""; _sessionChipHost.Visibility = session.Length > 0 ? Visibility.Visible : Visibility.Collapsed; _sessionChip.Text = session.Length == 0 ? "" : T("web.admin.logs.filters.playbackSessionChip", ("id", LogsPresentation.ShortId(session)));
        _focusedFFmpeg.Visibility = _playbackFocus && session.Length > 0 ? Visibility.Visible : Visibility.Collapsed; SetFFmpegButton(_focusedFFmpeg, state.Query.Component == "ffmpeg", "filter");
        if (session.Length > 0) { RenderSummary(state, session); _summary.Visibility = Visibility.Visible; }
        if (state.Loading) { var loading = Text(T("web.admin.logs.loading"), 14, true); loading.Margin = new(0, 32, 0, 32); _results.Children.Add(loading); }
        else if ((state.Stream == "app" ? state.AppRows.Count : state.AuditRows.Count) == 0) { var empty = Text(T("web.admin.logs." + (state.Stream == "app" ? "app" : "audit") + (state.Error == null ? ".empty" : ".loadError")), 14, true); empty.Margin = new(0, 32, 0, 32); _results.Children.Add(empty); }
        else _results.Children.Add(state.Stream == "app" ? AppTable(state) : AuditTable(state));
        AddHistory(state);
    }
    private void RenderSummary(LogsPageSnapshot state, string session)
    {
        var summary = LogsPlaybackSummary.From(session, state.AppRows, state.AuditRows);
        var values = new List<(string Key, string Value)> { ("playbackSession", LogsPresentation.ShortId(session)), ("applicationLogs", summary.ApplicationCount.ToString()), ("ffmpegLogs", summary.FFmpegCount.ToString()), ("auditLogs", summary.AuditCount.ToString()), ("firstSeen", Date(summary.FirstSeen)), ("nodesSeen", summary.Nodes.Length > 0 ? string.Join(", ", summary.Nodes) : "-") };
        if (summary.LastSeen != null) values.Add(("lastSeen", Date(summary.LastSeen)));
        var ffmpeg = Button("", "LogsFilterFFmpeg"); SetFFmpegButton(ffmpeg, state.Query.Component == "ffmpeg", "open"); ffmpeg.FontSize = 12; ffmpeg.VerticalAlignment = VerticalAlignment.Top; ffmpeg.Click += (_, _) => ToggleFFmpeg();
        var layout = new LogsSummaryLayout(ffmpeg) { Margin = new(0, 0, 0, 16) };
        foreach (var (key, value) in values) { var metric = Metric(T("web.admin.logs.summary." + key), value, key == "playbackSession" || key == "nodesSeen" && summary.Nodes.Length > 0); AutomationProperties.SetAutomationId(metric, "LogsSummary_" + key); layout.Children.Add(metric); }
        _summary.Children.Add(Panel(layout, 8, 16, true));
    }
    private void ToggleFFmpeg() => _component.Text = _component.Text == "ffmpeg" ? "" : "ffmpeg";
    private static void SetFFmpegButton(Button button, bool selected, string alternate)
    {
        var label = T("web.admin.logs.ffmpeg." + (selected ? "showingOnly" : alternate)); var caption = Text(label, 12); caption.FontWeight = Microsoft.UI.Text.FontWeights.Medium; button.Content = caption; button.Padding = new(12, 8, 12, 8); button.MinHeight = alternate == "open" ? 34 : 36; AutomationProperties.SetName(button, label);
        button.Foreground = Brush(selected ? "AccentBrush" : "SecondaryTextBrush"); button.BorderBrush = Brush(selected ? "AccentBrush" : "BorderBrush"); button.Background = selected ? new Microsoft.UI.Xaml.Media.SolidColorBrush(((Microsoft.UI.Xaml.Media.SolidColorBrush)Brush("AccentBrush")).Color) { Opacity = 0.1 } : Brush("AppBackgroundBrush");
    }
    private LogsNativeTable AppTable(LogsPageSnapshot state)
    {
        var table = new LogsNativeTable(new[] { "time", "level", "component", "status", "duration", "message" }.Select(key => T("web.admin.logs.columns." + key)).ToArray(), [185, 85, 140, 75, 90, 0]);
        foreach (var row in state.AppRows)
        {
            var level = Cell(row.Level.ToUpperInvariant());
            table.Add([Cell(Date(row.Timestamp)), level, Cell(row.Component), Cell(LogsPresentation.Attribute(row, "status")), Cell(Duration(row)), Cell(row.Message, max: 360)], () => ShowEntry(row, state.Selection), row.Message, _playbackFocus && row.Component == "ffmpeg");
        }
        return table;
    }
    private LogsNativeTable AuditTable(LogsPageSnapshot state)
    {
        var table = new LogsNativeTable(new[] { "time", "actionChanges", "target", "method", "path", "status", "client", "actor", "session", "playback", "request" }.Select(key => T("web.admin.logs.columns." + key)).ToArray(), [185, 280, 150, 80, 300, 75, 150, 150, 180, 180, 180]);
        foreach (var row in state.AuditRows)
        {
            var changes = Stack(4); changes.MinWidth = 240; changes.MaxWidth = 520; changes.Children.Add(Text(row.Action ?? T("web.admin.logs.audit.httpRequest")));
            foreach (var change in row.Changes) changes.Children.Add(Text(T("web.admin.logs.audit.change", ("field", change.Field.Replace('_', ' ')), ("summary", LogsPresentation.ChangeSummary(change, row.TargetType))), 12, true));
            var target = row.TargetType == null ? "-" : T("web.admin.logs.audit.target", ("type", row.TargetType == "api_key" ? T("web.admin.logs.audit.targetApiKey") : row.TargetType.Replace('_', ' ')), ("id", row.TargetId));
            var actor = row.ImpersonatorUserId != null ? T("web.admin.logs.audit.impersonatedActor", ("impersonator", row.ImpersonatorUserId), ("user", row.UserId ?? "-")) : row.UserId == null ? "-" : T("web.admin.logs.audit.actor", ("user", row.UserId));
            table.Add([Cell(Date(row.Timestamp)), changes, Cell(target), Cell(row.Method), Cell(row.Path, mono: true, max: 420), Cell(row.StatusCode.ToString()), Cell(LogsPresentation.ClientIp(row.ClientIp), mono: true), Text(actor), Cell(row.SessionId ?? "-", mono: true), Cell(row.PlaybackSessionId ?? "-", mono: true), Cell(row.RequestId ?? "-", mono: true)]);
        }
        return table;
    }
    private void AddHistory(LogsPageSnapshot state)
    {
        if (_controller == null) return; var controller = _controller;
        void Action(string key, Func<Task> action, bool enabled = true) { var button = Button(T("web.admin.logs.history." + key), "LogsHistory_" + key); button.IsEnabled = enabled; button.Click += (_, _) => { if (ReferenceEquals(controller, _controller)) _ = action(); }; _history.Children.Add(button); }
        if (!state.History) { Action("browse", controller.BrowseHistoryAsync); return; }
        Action("returnToLive", controller.ReconnectAsync); Action("newer", controller.NewerAsync, state.CanNewer && !state.Loading); _history.Children.Add(Text(T("web.admin.logs.history.page", ("page", state.Page)), 14, true)); Action("older", controller.OlderAsync, state.NextCursor != null && !state.Loading && state.Error == null);
        if (state.Error != null) { _history.Children.Add(Text(state.Error)); Action("retry", controller.RetryHistoryAsync, !state.Loading); }
    }
    private void ShowEntry(LogsOperationalEntry entry, long selection)
    {
        var state = _controller?.State; if (state?.Owner?.IsCurrent != true || state.Selection != selection) return;
        _detailSelection = selection; var content = Stack(16); var fields = new LogsMetricGrid(2, 2) { ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var (key, value) in new[] { ("requestId", entry.RequestId ?? "-"), ("node", entry.NodeId ?? "-"), ("method", LogsPresentation.Attribute(entry, "method")), ("status", LogsPresentation.Attribute(entry, "status")), ("duration", Duration(entry)), ("clientIp", entry.ClientIp ?? LogsPresentation.Attribute(entry, "client_ip")), ("userId", entry.UserId ?? "-"), ("sessionId", entry.SessionId ?? LogsPresentation.Attribute(entry, "session_id")), ("playbackSession", entry.PlaybackSessionId ?? LogsPresentation.Attribute(entry, "playback_session_id")) }) { var metric = Metric(T("web.admin.logs.detail." + key), value, key is "requestId" or "sessionId" or "playbackSession" or "clientIp"); fields.Add(metric); }
        content.Children.Add(fields); var session = entry.PlaybackSessionId ?? LogsPresentation.Attribute(entry, "playback_session_id");
        if (session != "-") { var related = Button(T("web.admin.logs.detail.viewPlaybackSessionLogs"), "LogsRelatedPlayback", "GhostButtonStyle"); related.Padding = new(0); related.MinHeight = 0; related.FontWeight = Microsoft.UI.Text.FontWeights.Medium; related.Foreground = Brush("AccentBrush"); related.HorizontalAlignment = HorizontalAlignment.Left; related.Click += (_, _) => { _sheet.Close(); _inputs["playback"].Text = session; }; content.Children.Add(related); }
        var path = Stack(4); var pathLabel = Text(T("web.admin.logs.detail.path")); pathLabel.FontWeight = Microsoft.UI.Text.FontWeights.Medium; path.Children.Add(pathLabel); var pathValue = Text(LogsPresentation.Attribute(entry, "path"), 12, mono: true); path.Children.Add(new Border { Child = pathValue, Background = Brush("MutedBrush"), CornerRadius = new(10), Padding = new(12, 8, 12, 8) }); content.Children.Add(path); var attributesLabel = Text(T("web.admin.logs.detail.attributes")); attributesLabel.FontWeight = Microsoft.UI.Text.FontWeights.Medium; content.Children.Add(attributesLabel);
        var json = Text(JsonSerializer.Serialize(entry.Attrs ?? [], new JsonSerializerOptions { WriteIndented = true }), 12, mono: true); json.IsTextSelectionEnabled = true; json.LineHeight = 20; content.Children.Add(new Border { Background = Brush("SurfaceRaisedBrush"), CornerRadius = new(10), Padding = new(12), Child = new ScrollViewer { Content = json, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        _sheet.Show(entry.Message, entry.Component + " · " + entry.Level.ToUpperInvariant() + " · " + Date(entry.Timestamp), content);
    }
    private static string Duration(LogsOperationalEntry entry) { var value = LogsPresentation.Attribute(entry, "duration_ms"); return value == "-" ? value : T("web.admin.logs.durationMs", ("value", value)); }
    internal static string Date(DateTimeOffset? value) => value == null ? "—" : $"{SiloPlayer.Helpers.DateTimeDisplay.FormatDate(value.Value)}, {SiloPlayer.Helpers.DateTimeDisplay.FormatTime(value.Value, seconds: true)}";
}
