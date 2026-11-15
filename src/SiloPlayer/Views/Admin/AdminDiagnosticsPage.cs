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
using Windows.Storage.Pickers;
using Windows.System;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminDiagnosticsPage : Page
{
    private readonly AdminMonitorApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly StackPanel _body = Stack(24);
    private readonly StackPanel _banners = Stack(8);
    private readonly StackPanel _reports = Stack();
    private readonly Grid _pagination = new();
    private readonly Button _clearFilters;
    private readonly TextBlock _retention = Text("", 14);
    private readonly TextBlock _validation = Text("", 13);
    private readonly ToggleSwitch _uploads = new() { OnContent = "", OffContent = "", MinWidth = 40 };
    private readonly Dictionary<string, TextBox> _filters = new();
    private readonly ComboBox _platform;
    private readonly ComboBox _type;
    private readonly LogsDateTimeInput _from;
    private readonly LogsDateTimeInput _to;
    private readonly LogsDetailSheet _sheet;
    private readonly LogsActionFeedback _feedback;
    private DiagnosticsController? _controller;
    private DiagnosticsQuery _initialQuery = new();
    private ContentDialog? _dialog;
    private CancellationTokenSource? _download;
    private string? _manifestReport;
    private bool _manifestExpanded;
    private bool _syncing;
    public DiagnosticsController? Controller => _controller;
    public StackPanel MonitorScrollContent => _body;

    public AdminDiagnosticsPage() : this(App.Services.GetRequiredService<AdminMonitorApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>()) { }
    public AdminDiagnosticsPage(AdminMonitorApi api, AdminMonitorAuthority authority)
    {
        InitializeComponent();
        _api = api; _authority = authority; NavigationCacheMode = NavigationCacheMode.Disabled; AutomationProperties.SetAutomationId(this, "AdminDiagnosticsPage");
        var actions = Inline(24); var toggle = Inline(8); toggle.Children.Add(Text(D("page.clientUploads"), 14, true)); toggle.Children.Add(_uploads); actions.Children.Add(toggle); var retention = Stack(); var retentionLabel = Text(D("page.retention").ToUpperInvariant(), 12, true); retentionLabel.FontWeight = Microsoft.UI.Text.FontWeights.Medium; retentionLabel.CharacterSpacing = 200; retentionLabel.TextAlignment = TextAlignment.Right; _retention.TextAlignment = TextAlignment.Right; retention.Children.Add(retentionLabel); retention.Children.Add(_retention); actions.Children.Add(retention);
        AutomationProperties.SetName(_uploads, D("page.clientUploads")); AutomationProperties.SetAutomationId(_uploads, "DiagnosticsUploads"); _uploads.Toggled += async (_, _) => await ToggleAsync();
        _body.Children.Add(Header(D("page.title"), D("page.subtitle"), actions)); _body.Children.Add(_banners);
        var filters = new LogsWrapPanel { Spacing = 12, RowSpacing = 16, GrowFields = true };
        filters.Children.Add(Filter("user", "user", 96, D("filters.userAny")));
        _platform = Select("web.admin.diagnostics.filters.platform", new[] { ("", D("filters.allPlatforms")), ("android", "Android"), ("android-tv", "Android TV"), ("ios", "iOS"), ("tvos", "tvOS"), ("roku", "Roku") }, 144); filters.Children.Add(Labelled(D("filters.platform"), _platform));
        _type = Select("web.admin.diagnostics.filters.reportType", new[] { ("", D("filters.allTypes")) }.Concat(new[] { "crash", "anr", "native_crash", "hang", "abnormal_exit", "manual" }.Select(value => (value, TypeLabel(value)))), 144); filters.Children.Add(Labelled(D("filters.reportType"), _type));
        _from = new(D("filters.from"), "DiagnosticsFrom"); _to = new(D("filters.to"), "DiagnosticsTo"); filters.Children.Add(Labelled(D("filters.from"), _from)); filters.Children.Add(Labelled(D("filters.to"), _to)); filters.Children.Add(Filter("shortId", "short", 128, D("filters.shortIdPlaceholder")));
        var filterActions = Inline(8);
        var apply = Button(D("filters.apply"), "DiagnosticsApply", ""); apply.VerticalAlignment = VerticalAlignment.Bottom; apply.Click += async (_, _) => await ApplyAsync(); filterActions.Children.Add(apply);
        var clear = Button(D("filters.clear"), "DiagnosticsClear", icon: "filter-x"); clear.VerticalAlignment = VerticalAlignment.Bottom; AutomationProperties.SetName(clear, D("filters.clearLabel")); clear.Click += async (_, _) => { _initialQuery = new(); SyncFilters(_initialQuery); if (_controller != null) await _controller.ClearFiltersAsync(); }; filterActions.Children.Add(clear); _clearFilters = clear; filters.TrailingActions = filterActions; filters.Children.Add(filterActions);
        var filterPanel = Stack(8); filterPanel.Children.Add(filters); filterPanel.Children.Add(_validation); _validation.Visibility = Visibility.Collapsed; _body.Children.Add(Panel(filterPanel, 24, 16, true));
        _body.Children.Add(_reports); _body.Children.Add(_pagination);
        _pagination.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _pagination.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _pagination.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var root = new Grid(); var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll); _sheet = new(scroll, 768) { MonospaceTitle = true }; _sheet.Closed += () => { _download?.Cancel(); _controller?.CloseDetail(); }; root.Children.Add(_sheet); _feedback = new(this); root.Children.Add(_feedback); Content = root;
        Loaded += (_, _) => Open(); Unloaded += (_, _) => Close();
    }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args); if (args.Parameter is not AdminRouteRequest route) return;
        _initialQuery = new() { UserId = route.Get("user_id"), Platform = route.Get("platform"), ReportType = route.Get("report_type"), From = route.Get("from"), To = route.Get("to"), ShortId = route.Get("short_id") }; SyncFilters(_initialQuery);
    }
    private FrameworkElement Filter(string label, string key, double width, string? placeholder = null)
    {
        var input = Input(placeholder ?? D("filters." + label), "DiagnosticsFilter_" + key, width, key == "short"); _filters[key] = input;
        AutomationProperties.SetName(input, D("filters." + label));
        input.KeyDown += async (_, args) => { if (args.Key == VirtualKey.Enter) { args.Handled = true; await ApplyAsync(); } }; var field = Labelled(D("filters." + label), input, key != "user");
        if (key == "user") input.InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName { NameValue = Microsoft.UI.Xaml.Input.InputScopeNameValue.Number } } };
        if (key == "short") input.CharacterCasing = CharacterCasing.Upper;
        return field;
    }
    private static StackPanel Labelled(string label, FrameworkElement control, bool grow = true)
    {
        var field = Stack(6); var title = Text(label.ToUpperInvariant(), 11, true); title.FontWeight = Microsoft.UI.Text.FontWeights.Medium; title.CharacterSpacing = 50; field.Children.Add(title); field.Children.Add(control);
        if (grow) { field.MinWidth = control.Width; control.MinWidth = control.Width; control.Width = double.NaN; control.HorizontalAlignment = HorizontalAlignment.Stretch; }
        else field.Width = control.Width;
        return field;
    }
    private void SyncFilters(DiagnosticsQuery query)
    {
        _filters["user"].Text = query.UserId ?? ""; _filters["short"].Text = query.ShortId ?? ""; _from.Value = query.From ?? ""; _to.Value = query.To ?? ""; SetSelected(_platform, query.Platform); SetSelected(_type, query.ReportType);
        _validation.Visibility = Visibility.Collapsed;
    }
    private async Task ApplyAsync()
    {
        if (_controller == null) return;
        try
        {
            var query = new DiagnosticsQuery { UserId = _filters["user"].Text, Platform = Selected(_platform), ReportType = Selected(_type), From = _from.Value, To = _to.Value, ShortId = _filters["short"].Text }.Apply();
            _initialQuery = query;
            await _controller.ApplyAsync(query); _validation.Visibility = Visibility.Collapsed;
        }
        catch (ArgumentException error) { _validation.Text = error.Message; _validation.Foreground = Brush("ErrorBrush"); _validation.Visibility = Visibility.Visible; }
        catch (OperationCanceledException) { }
    }
    private void Open()
    {
        Close(); SyncFilters(_initialQuery); _controller = new(_api, _authority); _controller.Changed += QueueRender; _authority.AvailabilityChanged += OnAvailabilityChanged; _ = OpenControllerAsync(_controller);
    }
    private async Task OpenControllerAsync(DiagnosticsController controller)
    {
        try { await controller.OpenAsync(_initialQuery); }
        catch (ArgumentException error) { if (ReferenceEquals(controller, _controller) && IsLoaded) { _validation.Text = error.Message; _validation.Visibility = Visibility.Visible; await controller.OpenAsync(); } }
    }
    private void OnAvailabilityChanged()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || controller == null || !ReferenceEquals(controller, _controller) || controller.State.Owner is { IsCurrent: true }) return;
            Render(controller.State);
            if (_authority.IsAvailable) _ = OpenControllerAsync(controller);
        });
    }
    internal void SuspendMonitor() => Close();

    private void Close()
    {
        _authority.AvailabilityChanged -= OnAvailabilityChanged;
        _feedback.Clear();
        _dialog?.Hide(); _dialog = null; _download?.Cancel(); _download?.Dispose(); _download = null;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Dispose(); _controller = null; }
        _sheet.Close(); _reports.Children.Clear(); _banners.Children.Clear(); _pagination.Children.Clear(); _retention.Text = ""; _syncing = true; _uploads.IsOn = false; _uploads.IsEnabled = false; _syncing = false;
    }
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return; var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (IsLoaded && ReferenceEquals(controller, _controller) && controller.IsCurrent(state)) Render(state); });
    }
    private void Render(DiagnosticsPageSnapshot state)
    {
        _banners.Children.Clear(); _reports.Children.Clear(); _pagination.Children.Clear();
        _syncing = true; _uploads.IsOn = state.UploadsEnabled; _uploads.IsEnabled = state.CanToggle; _syncing = false;
        _clearFilters.IsEnabled = state.Query.HasFilters;
        _retention.Text = state.Capabilities == null ? D("page.loading") : D("page.retentionDays", ("count", state.Capabilities.RetentionDays), ("count_text", state.Capabilities.RetentionDays));
        if (state.Owner == null) { _feedback.Clear(); _sheet.Close(); _dialog?.Hide(); _download?.Cancel(); _retention.Text = ""; _validation.Visibility = Visibility.Collapsed; return; }
        if (state.Capabilities?.Status is "disabled" or "storage_unavailable") _banners.Children.Add(Banner(D(state.Capabilities.Status == "disabled" ? "banner.disabled" : "banner.storageUnavailable")));
        if (state.StatusError != null) _banners.Children.Add(Banner(state.StatusError, true));
        _feedback.Present(state.ActionError, state.Notice);
        if (state.StatusError != null || state.Error != null) { var retry = Button(T("common.retry"), "DiagnosticsRetry"); retry.Click += (_, _) => { if (_controller != null) _ = _controller.RefreshAsync(); }; _banners.Children.Add(retry); }
        var table = new LogsNativeTable(new[] { "received", "shortId", "user", "platform", "type", "summary", "state", "bundle" }.Select(key => D("columns." + key)).ToArray(), [175, 100, 125, 140, 125, 0, 100, 100]);
        if (state.Loading) table.AddFull(Centered(D("list.loading")));
        else if (state.Error != null) table.AddFull(Centered(state.Error, true));
        else if (state.Reports.Count == 0) { var empty = Stack(8); empty.MaxWidth = 448; empty.HorizontalAlignment = HorizontalAlignment.Center; var icon = Panel(Icon("bug", 20), 12, 10); icon.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(icon); var title = Text(D("empty.title")); title.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(title); var description = Text(D(state.Query.HasFilters ? "empty.filtered" : "empty.unfiltered"), 14, true); description.TextAlignment = TextAlignment.Center; empty.Children.Add(description); table.AddFull(empty, 208); }
        foreach (var report in state.Reports)
        {
            var user = Stack(); user.Children.Add(Text(D("userRef", ("id", report.UserId)))); if (report.ProfileId != null) user.Children.Add(new TextBlock { Text = report.ProfileId, FontSize = 12, LineHeight = 16, FontWeight = Microsoft.UI.Text.FontWeights.Normal, Foreground = Brush("SecondaryTextBrush"), MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis });
            var platform = Stack(); platform.Children.Add(Text(DiagnosticsPresentation.Platform(report.Platform))); platform.Children.Add(Text(report.AppBuild.Length > 0 ? D("row.versionWithBuild", ("version", report.AppVersion), ("build", report.AppBuild)) : D("row.version", ("version", report.AppVersion)), 12, true));
            table.Add([Cell(AdminLogsPage.Date(report.ReceivedAt)), Cell(report.ShortId, mono: true), user, platform, Badge(TypeLabel(report.ReportType)), Cell(report.CrashSummary ?? "—", max: 360), Badge(StateLabel(report.State), report.State), Cell(DiagnosticsPresentation.Bytes(report.BlobBytes), mono: true)], () => { if (_controller != null) _ = _controller.SelectAsync(report.Id); }, D("row.openLabel", ("id", report.ShortId)));
        }
        table.AlignEnd(7);
        var tablePanel = Panel(table, 24, 0); tablePanel.BorderThickness = new(0); _reports.Children.Add(tablePanel);
        _banners.Visibility = _banners.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var previous = Button(D("pagination.previous"), "DiagnosticsPrevious"); previous.IsEnabled = state.CanPrevious && !state.Loading; previous.Click += (_, _) => { if (_controller != null) _ = _controller.PreviousAsync(); }; _pagination.Children.Add(previous);
        var page = Text(state.Loading ? D("pagination.refreshing") : state.NextCursor != null ? D("pagination.pageWithMore", ("page", state.Page)) : state.Reports.Count > 0 ? D("pagination.page", ("page", state.Page)) : "", 12, true); page.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(page, 1); _pagination.Children.Add(page);
        var next = Button(D("pagination.next"), "DiagnosticsNext"); next.IsEnabled = state.NextCursor != null && !state.Loading; next.Click += (_, _) => { if (_controller != null) _ = _controller.NextAsync(); }; Grid.SetColumn(next, 2); _pagination.Children.Add(next);
        RenderDetail(state);
    }
    private void RenderDetail(DiagnosticsPageSnapshot state)
    {
        if (state.SelectedId == null) { _sheet.Close(); _manifestReport = null; _manifestExpanded = false; return; }
        if (_manifestReport != state.SelectedId) { _manifestReport = state.SelectedId; _manifestExpanded = false; }
        var content = Stack(24); var report = state.Detail;
        if (state.DetailLoading) content.Children.Add(Text(D("sheet.loading"), 14, true));
        if (state.DetailError != null) content.Children.Add(Banner(state.DetailError, true));
        if (report != null)
        {
            var actions = new LogsWrapPanel(); var download = Button(D(state.Downloading ? "detail.preparing" : "detail.download"), "DiagnosticsDownload", "", "download"); download.IsEnabled = report.State == "ready" && !state.Downloading; download.Click += async (_, _) => await DownloadAsync(report); actions.Children.Add(download);
            var delete = Button(D("detail.delete"), "DiagnosticsDelete", "DestructiveButtonStyle", "trash-2"); delete.IsEnabled = !state.Deleting; delete.Click += async (_, _) => await DeleteAsync(report); actions.Children.Add(delete);
            if (state.Downloading) { var cancel = Button(T("common.cancel"), "DiagnosticsCancelDownload"); cancel.Click += (_, _) => _download?.Cancel(); actions.Children.Add(cancel); }
            if (report.State != "ready") actions.Children.Add(Text(D("detail.onlyReadyDownloadable"), 12, true)); content.Children.Add(actions);
            var device = new LogsMetricGrid(3, 1);
            string Manifest(string section, string key) => DiagnosticsPresentation.ManifestValue(report.Manifest, section, key);
            AddMetric(device, "device", (Manifest("device_summary", "manufacturer") + " " + Manifest("device_summary", "model")).Trim()); AddMetric(device, "os", Manifest("device_summary", "os") is { Length: > 0 } os ? os : Manifest("report", "os_version")); AddMetric(device, "app", report.AppBuild.Length > 0 ? D("detail.appVersionWithBuild", ("version", report.AppVersion), ("build", report.AppBuild)) : report.AppVersion); content.Children.Add(Panel(device, 16, 16, true));
            var fields = new LogsMetricGrid(3, 2);
            foreach (var (key, value) in new[] { ("state", StateLabel(report.State)), ("user", D("userRef", ("id", report.UserId))), ("profile", report.ProfileId ?? "—"), ("captured", AdminLogsPage.Date(report.CapturedAt)), ("received", AdminLogsPage.Date(report.ReceivedAt)), ("formFactor", DiagnosticsPresentation.Token(Manifest("device_summary", "form_factor"))), ("compressed", DiagnosticsPresentation.Bytes(report.BlobBytes)), ("uncompressed", DiagnosticsPresentation.Bytes(report.UncompressedBytes)), ("reportId", report.Id) }) AddMetric(fields, key, value); content.Children.Add(fields);
            if (!string.IsNullOrEmpty(report.CrashSummary)) { var crashSection = Stack(8); var crashHeading = Text(D("detail.crashSummary")); crashHeading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; crashSection.Children.Add(crashHeading); var crash = Text(report.CrashSummary); crash.LineHeight = 24; crash.IsTextSelectionEnabled = true; crashSection.Children.Add(new Border { Child = crash, Background = Brush("MutedBrush"), CornerRadius = new(8), Padding = new(12) }); content.Children.Add(crashSection); }
            var sessions = Stack(8); var sessionsHeading = Text(D("detail.playbackSessions")); sessionsHeading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; sessions.Children.Add(sessionsHeading);
            if (report.PlaybackSessionIds.Length == 0) sessions.Children.Add(Text(D("detail.noPlaybackSessions"), 14, true));
            foreach (var session in report.PlaybackSessionIds) { var link = Button(session, "DiagnosticsPlayback_" + session, "GhostButtonStyle"); var label = Inline(6); label.Children.Add(Text(session, 12, mono: true)); label.Children.Add(Icon("external-link", 12)); link.Content = label; link.Padding = new(0); link.MinHeight = 16; link.HorizontalAlignment = HorizontalAlignment.Left; link.Click += (_, _) => { if (state.Owner?.IsCurrent == true) AdminShellPage.Open(new(AdminRoute.ServerLog, new Dictionary<string, string> { ["playback_session_id"] = session, ["focus"] = "playback" })); }; sessions.Children.Add(link); } content.Children.Add(sessions);
            var json = Text(report.Manifest.ValueKind == JsonValueKind.Undefined ? "{}" : JsonSerializer.Serialize(report.Manifest, new JsonSerializerOptions { WriteIndented = true }), 12, mono: true); json.IsTextSelectionEnabled = true;
            json.LineHeight = 20;
            var manifestBody = new ScrollViewer { Content = json, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(16), Visibility = _manifestExpanded ? Visibility.Visible : Visibility.Collapsed };
            var manifest = Stack(); var disclosure = Button(D("detail.manifestJson"), "DiagnosticsManifest", "GhostButtonStyle"); disclosure.HorizontalAlignment = HorizontalAlignment.Stretch; disclosure.HorizontalContentAlignment = HorizontalAlignment.Left; disclosure.Padding = new(16, 12, 16, 12); disclosure.Background = Tint("MutedBrush", 0.4);
            void UpdateDisclosure() { var label = Inline(6); label.Children.Add(Icon(_manifestExpanded ? "chevron-down" : "chevron-right", 12)); var title = Text(D("detail.manifestJson")); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; label.Children.Add(title); disclosure.Content = label; }
            disclosure.Click += (_, _) => { _manifestExpanded = !_manifestExpanded; manifestBody.Visibility = _manifestExpanded ? Visibility.Visible : Visibility.Collapsed; UpdateDisclosure(); }; UpdateDisclosure(); manifest.Children.Add(disclosure); manifest.Children.Add(manifestBody); content.Children.Add(new Border { Child = manifest, BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), CornerRadius = new(12) });
        }
        var title = report?.ShortId ?? D("sheet.titleFallback"); var description = report == null ? D("sheet.descriptionFallback") : D("sheet.description", ("type", TypeLabel(report.ReportType)), ("received", AdminLogsPage.Date(report.ReceivedAt)));
        if (_sheet.Visibility == Visibility.Visible) _sheet.Update(title, description, content); else _sheet.Show(title, description, content);
    }
    private async Task ToggleAsync()
    {
        var controller = _controller; if (_syncing || controller == null) return; var enabled = _uploads.IsOn;
        _syncing = true; _uploads.IsOn = controller.State.UploadsEnabled; _syncing = false;
        try { using var action = controller.CaptureAction(); await controller.SetUploadsEnabledAsync(action, enabled); }
        catch (OperationCanceledException) { }
    }
    private async Task DeleteAsync(DiagnosticsReportSummary report)
    {
        var controller = _controller; if (controller == null || _dialog != null) return;
        try
        {
            using var action = controller.CaptureAction();
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = D("delete.title"), Content = D("delete.description", ("id", report.ShortId)), PrimaryButtonText = D("delete.confirm"), CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close, PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"] };
            _dialog = dialog; using var cancellation = action.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            var result = await dialog.ShowAsync(); if (ReferenceEquals(_dialog, dialog)) _dialog = null;
            if (result == ContentDialogResult.Primary && ReferenceEquals(controller, _controller) && controller.IsActionCurrent(action)) await controller.DeleteAsync(action, report.Id);
        }
        catch (OperationCanceledException) { }
    }
    private async Task DownloadAsync(DiagnosticsReportSummary report)
    {
        var controller = _controller; if (controller == null || _download != null) return;
        CancellationTokenSource? cancellation = null;
        MonitorAuthorityLease? action = null;
        try
        {
            action = controller.CaptureAction(); cancellation = CancellationTokenSource.CreateLinkedTokenSource(action.Token); _download = cancellation; var token = cancellation.Token;
            var safeName = new string((report.ShortId.Length > 0 ? report.ShortId : report.Id).Where(character => char.IsAsciiLetterOrDigit(character) || character == '-').Take(80).ToArray());
            var picker = new FileSavePicker { SuggestedFileName = "silo-diagnostics-" + safeName + ".tar.gz" }; picker.FileTypeChoices.Add(T("logs.native.diagnostics.bundleFileType"), [".gz"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var file = await picker.PickSaveFileAsync(); action.ThrowIfRetired(); token.ThrowIfCancellationRequested(); if (file == null || !ReferenceEquals(controller, _controller)) return;
            await DiagnosticsFileTransfer.SaveAsync(action, file.Path, (destination, transferToken) => controller.DownloadAsync(action, report, destination, transferToken), token);
            action.ThrowIfRetired();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (action != null && ReferenceEquals(controller, _controller) && controller.IsActionCurrent(action)) _feedback.Show(D("detail.downloadFailed"), null); }
        finally { if (ReferenceEquals(_download, cancellation)) _download = null; cancellation?.Dispose(); action?.Dispose(); }
    }
    private static void AddMetric(LogsMetricGrid panel, string key, string value) { var metric = Metric(D("detail." + key), value.Length == 0 ? "—" : value, key is "profile" or "reportId"); panel.Add(metric); }
    private static TextBlock Centered(string text, bool error = false) { var block = Text(text, 14, !error); block.TextAlignment = TextAlignment.Center; block.HorizontalAlignment = HorizontalAlignment.Center; if (error) block.Foreground = Brush("ErrorBrush"); return block; }
    private static Border Badge(string value, string? state = null)
    {
        var label = Text(value, 12); label.FontWeight = Microsoft.UI.Text.FontWeights.Medium; label.LineHeight = 16;
        var badge = new Border { Child = label, CornerRadius = new(10), Padding = new(8, 2, 8, 2), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), HorizontalAlignment = HorizontalAlignment.Left };
        if (state == null) return badge;
        var dark = Brush("AppBackgroundBrush") is Microsoft.UI.Xaml.Media.SolidColorBrush background && background.Color.R < 128;
        var tone = state == "ready" ? Windows.UI.Color.FromArgb(255, 16, 185, 129)
            : state == "failed" && Brush("ErrorBrush") is Microsoft.UI.Xaml.Media.SolidColorBrush error ? error.Color
            : Windows.UI.Color.FromArgb(255, 245, 158, 11);
        var text = state == "ready" ? (dark ? Windows.UI.Color.FromArgb(255, 52, 211, 153) : Windows.UI.Color.FromArgb(255, 5, 150, 105))
            : state == "failed" ? tone : (dark ? Windows.UI.Color.FromArgb(255, 251, 191, 36) : Windows.UI.Color.FromArgb(255, 180, 83, 9));
        label.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(text);
        var fill = tone; fill.A = 26; var outline = tone; outline.A = 77;
        badge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(fill); badge.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(outline); return badge;
    }
    private static string TypeLabel(string value) => value switch { "native_crash" => D("reportType.nativeCrash"), "abnormal_exit" => D("reportType.abnormalExit"), "crash" or "anr" or "hang" or "manual" => D("reportType." + value), _ => DiagnosticsPresentation.Token(value) };
    private static string StateLabel(string value) => value is "ready" or "receiving" or "failed" ? D("state." + value) : DiagnosticsPresentation.Token(value);
    private static string D(string key, params (string Name, object? Value)[] values) => T("web.admin.diagnostics." + key, values);
}
