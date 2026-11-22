using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using Presentation = SiloPlayer.Core.Services.Admin.AdminMaintenancePresentation;
using AdminSettingsUi = SiloPlayer.Controls.AdminSettingsUi;
using UI = SiloPlayer.Views.Admin.AdminLibrariesUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure › Server › Maintenance (official <c>AdminMaintenance.tsx</c> at 92ee0518): the page
/// header, Catalog Import &amp; Export (<c>AdminCatalogMaintenance.tsx</c>: Start Export, the Import
/// Catalog dialog, Recent Catalog Imports and Exports) and the read-only Job History
/// (<c>AdminJobHistory.tsx</c>). No restart, repair, cancel, delete or run command exists here,
/// and nothing polls: lists change on Refresh, Restart, Load older and command reconciliation.
/// Signed download and published links are opened or copied only for the current owner, never
/// with account credentials, and are never logged or rendered.
/// </summary>
public sealed partial class AdminMaintenancePage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private const double WideWidth = 1024;
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { Spacing = 24, Padding = new(0, 0, 0, 24) };
    private readonly TextBlock _accessText = UI.Text("", 13);
    private readonly Border _access;
    private readonly StackPanel _sections = new() { Spacing = 24 };
    private readonly Button _startExport;
    private readonly Button _importCatalog;
    private readonly StackPanel _catalogHeaderText = new() { Spacing = 4 };
    private readonly StackPanel _catalogHeaderActions = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid _catalogHeader = new() { ColumnSpacing = 16, RowSpacing = 16 };
    private readonly JobList _imports, _exports, _history;
    private readonly AdminMonitorAuthority _authority;
    private AdminMaintenanceController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private bool _active, _opening, _wide;

    public FrameworkElement ScrollContent => _scrollContent;
    public AdminMaintenanceController? Controller => _controller;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;

    /// <summary>Opens a signed download in the default browser (no account credentials). Replaced by fixtures.</summary>
    public Func<Uri, Task<bool>> Launcher { get; set; } = async uri => await Windows.System.Launcher.LaunchUriAsync(uri);

    /// <summary>Places a published link on the clipboard. Replaced by fixtures so the owner's clipboard is never read or overwritten.</summary>
    public Func<string, bool> CopyText { get; set; } = CopyToClipboard;

    public AdminMaintenancePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminMaintenancePage");

        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(AdminSettingsUi.Title(T("web.admin.maintenance.page.title")));
        var subtitle = UI.Text(T("web.admin.maintenance.page.subtitle"), 14, true);
        subtitle.MaxWidth = 800; subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        header.Children.Add(subtitle);
        _body.Children.Add(header);

        AutomationProperties.SetAutomationId(_accessText, "MaintenanceAccessText");
        AutomationProperties.SetLiveSetting(_accessText, AutomationLiveSetting.Polite);
        _access = AdminSettingsUi.Alert(_accessText);
        _access.Visibility = Visibility.Collapsed;
        _body.Children.Add(_access);

        // Catalog Import & Export header: title and body, Start Export and Import Catalog.
        var title = UI.Text(T("web.admin.catalogMaintenance.header.title"), 18); title.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        _catalogHeaderText.Children.Add(title);
        _catalogHeaderText.Children.Add(UI.Text(T("web.admin.catalogMaintenance.header.body"), 14, true));
        _startExport = UI.Action(T("web.admin.catalogMaintenance.header.startExport"), "MaintenanceStartExport");
        _startExport.Content = IconLabel("download", T("web.admin.catalogMaintenance.header.startExport"));
        _startExport.Click += async (_, _) => { if (_controller is { } controller && controller.State.Owner is { IsCurrent: true }) await controller.StartExportAsync(); };
        _importCatalog = UI.Action(T("web.admin.catalogMaintenance.header.importCatalog"), "MaintenanceImportCatalog");
        _importCatalog.Content = IconLabel("upload", T("web.admin.catalogMaintenance.header.importCatalog"));
        _importCatalog.Click += async (_, _) => await ShowImportDialogAsync();
        _catalogHeaderActions.Children.Add(_startExport); _catalogHeaderActions.Children.Add(_importCatalog);
        _catalogHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _catalogHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _catalogHeader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _catalogHeader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _catalogHeader.Children.Add(_catalogHeaderText); _catalogHeader.Children.Add(_catalogHeaderActions);
        _sections.Children.Add(Card(_catalogHeader, padding: 16));

        _imports = new JobList(this, AdminMaintenanceListKind.Imports, "MaintenanceImports", T("web.admin.catalogMaintenance.imports.title"),
            T("web.admin.catalogMaintenance.imports.body"), T("web.admin.catalogMaintenance.imports.empty"), T("web.admin.catalogMaintenance.imports.loadOlder"),
            T("web.admin.catalogMaintenance.jobs.refreshing"), controlsFirst: true, refreshName: T("web.admin.catalogMaintenance.refresh"));
        _exports = new JobList(this, AdminMaintenanceListKind.Exports, "MaintenanceExports", T("web.admin.catalogMaintenance.exports.title"),
            T("web.admin.catalogMaintenance.exports.body"), T("web.admin.catalogMaintenance.exports.empty"), T("web.admin.catalogMaintenance.exports.loadOlder"),
            T("web.admin.catalogMaintenance.jobs.refreshing"), controlsFirst: true, refreshName: T("web.admin.catalogMaintenance.refresh"));
        _history = new JobList(this, AdminMaintenanceListKind.History, "MaintenanceHistory", T("web.admin.common.jobHistory.title"),
            T("web.admin.common.jobHistory.subtitle"), T("web.admin.common.jobHistory.empty"), T("web.admin.common.jobPageControls.loadOlder"),
            T("web.admin.common.jobHistory.refreshing"), controlsFirst: false, refreshName: T("web.admin.common.jobHistory.refresh"));
        _sections.Children.Add(_imports.Root); _sections.Children.Add(_exports.Root); _sections.Children.Add(_history.Root);
        _body.Children.Add(_sections);

        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent; Content = _scroller;
        Loaded += (_, _) => Activate();
        Unloaded += (_, _) => Close();
        SizeChanged += (_, args) => { var wide = args.NewSize.Width >= WideWidth; if (wide != _wide) { _wide = wide; Layout(); QueueRender(); } };
    }

    private static string T(string key, params (string Name, object? Value)[] values) => AdminMaintenanceText.T(key, values);

    // ── Lifetime ──────────────────────────────────────────────────────────

    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }

    private void Activate()
    {
        if (_active) return;
        _active = true;
        _wide = ActualWidth >= WideWidth; Layout();
        _controller = App.Services.GetRequiredService<AdminMaintenanceController>();
        _leave = new(() => _controller?.State.Owner, () => _controller?.RequiresLeaveConfirmation == true, () => _controller?.DiscardDraft());
        _controller.Changed += QueueRender; _controller.Notice += OnNotice; _controller.ImportCompleted += OnImportCompleted;
        _authority.AvailabilityChanged += OnAuthorityChanged;
        Render(_controller.State);
        _ = OpenAsync();
    }

    private async Task OpenAsync()
    {
        if (_opening || !_active || _controller == null || !_authority.IsAvailable) return;
        _opening = true;
        try { await _controller.OpenAsync(); }
        finally
        {
            _opening = false;
            if (_active && _authority.IsAvailable && _controller?.State is { Owner: null, OpenFailure: null })
                DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); });
        }
    }

    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_active && _controller?.State.Owner is not { IsCurrent: true }) _ = OpenAsync();
    });

    public Task<bool> CanLeaveAsync() =>
        _leave?.CanLeaveAsync(owner => AdminSettingsUi.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);

    public void SuspendAdminPage() => Close();

    /// <summary>Departure: hide the dialog, retire the controller (cancelling its reads and sends) and clear every row.</summary>
    private void Close()
    {
        if (!_active) return;
        _active = false;
        _authority.AvailabilityChanged -= OnAuthorityChanged;
        var dialog = _dialog; _dialog = null; dialog?.Hide();
        if (_controller != null)
        {
            _controller.Changed -= QueueRender; _controller.Notice -= OnNotice; _controller.ImportCompleted -= OnImportCompleted;
            _controller.Dispose(); _controller = null;
        }
        _leave = null;
        _importView = null;
        _imports.Clear(); _exports.Clear(); _history.Clear();
        _access.Visibility = Visibility.Collapsed;
    }

    private void QueueRender()
    {
        var controller = _controller;
        if (controller == null) return;
        DispatcherQueue.TryEnqueue(() => { if (_active && ReferenceEquals(controller, _controller)) Render(controller.State); });
    }

    private void OnNotice(AdminMaintenanceNotice notice)
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || !ReferenceEquals(controller, _controller)) return;
            var toast = App.Services.GetService<ToastService>();
            switch (notice.Kind)
            {
                case AdminMaintenanceNoticeKind.Success: toast?.Success(notice.Message); break;
                case AdminMaintenanceNoticeKind.Warning: toast?.Warning(notice.Message); break;
                default: toast?.Error(notice.Message); break;
            }
        });
    }

    // ── Render ────────────────────────────────────────────────────────────

    private void Layout()
    {
        Grid.SetRow(_catalogHeaderActions, _wide ? 0 : 1); Grid.SetColumn(_catalogHeaderActions, _wide ? 1 : 0);
        Grid.SetColumnSpan(_catalogHeaderActions, _wide ? 1 : 2); Grid.SetColumnSpan(_catalogHeaderText, _wide ? 1 : 2);
        _catalogHeaderActions.VerticalAlignment = VerticalAlignment.Center;
    }

    private void Render(AdminMaintenanceState state)
    {
        var current = state.Owner is { IsCurrent: true };
        var blocked = state.OpenFailure != null;
        _access.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        if (blocked) AdminSettingsUi.UpdateLiveText(_accessText, T("admin.native.maintenance.error.forbidden"));
        _sections.Visibility = blocked ? Visibility.Collapsed : Visibility.Visible;
        _startExport.IsEnabled = current && !state.ExportPending;
        _importCatalog.IsEnabled = current;
        _imports.Render(state, state.Imports, current);
        _exports.Render(state, state.Exports, current);
        _history.Render(state, state.History, current);
        RenderImportDialog(state);
    }

    /// <summary>A catalog import row (official Recent Catalog Imports).</summary>
    private FrameworkElement ImportRow(AdminMaintenanceJob job, MonitorAuthorityLease? owner, bool current)
    {
        var text = new StackPanel { Spacing = 8 };
        text.Children.Add(TitleLine(job, Presentation.CatalogBadge(job), Presentation.StatusWord(job), Presentation.ImportDescription(job)));
        text.Children.Add(UI.Text(Presentation.Message(job) is { Length: > 0 } message ? message : T("web.admin.catalogMaintenance.imports.jobFallback"), 14, true));
        text.Children.Add(ProgressTrack(Presentation.ProgressPercent(job)));
        var details = Details(T("web.admin.catalogMaintenance.jobs.progress", ("progress", Presentation.ProgressText(job))));
        if (job.FinishedAt is { } finished) details.Children.Add(UI.Text(T("web.admin.catalogMaintenance.jobs.finished", ("date", AdminLibraryPresentation.DateTimeText(finished))), 12, true));
        if (Presentation.CatalogListResult(job) is { } result) details.Children.Add(UI.Text(result, 12, true));
        text.Children.Add(Wrap(details));
        AddFailure(text, job);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(RefreshButton(AdminMaintenanceListKind.Imports, "MaintenanceImportRefresh_" + job.Id, owner, current));
        return Row(text, actions);
    }

    /// <summary>A catalog export row with Download, Create seven-day link and Copy URL.</summary>
    private FrameworkElement ExportRow(AdminMaintenanceState state, AdminMaintenanceJob job, MonitorAuthorityLease? owner, bool current)
    {
        var text = new StackPanel { Spacing = 8 };
        text.Children.Add(TitleLine(job, Presentation.CatalogBadge(job), Presentation.StatusWord(job), Presentation.ExportScope(job)));
        text.Children.Add(UI.Text(Presentation.Message(job) is { Length: > 0 } message ? message : T("web.admin.catalogMaintenance.exports.jobFallback"), 14, true));
        var details = Details(T("web.admin.catalogMaintenance.jobs.progress", ("progress", Presentation.ExportProgressText(job))));
        if (job.FinishedAt is { } finished) details.Children.Add(UI.Text(T("web.admin.catalogMaintenance.jobs.finished", ("date", AdminLibraryPresentation.DateTimeText(finished))), 12, true));
        if (Presentation.CatalogListResult(job) is { } result) details.Children.Add(UI.Text(result, 12, true));
        text.Children.Add(Wrap(details));
        AddFailure(text, job);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(RefreshButton(AdminMaintenanceListKind.Exports, "MaintenanceExportRefresh_" + job.Id, owner, current));
        if (!string.IsNullOrEmpty(job.DownloadUrl))
        {
            var download = UI.Action(T("web.admin.catalogMaintenance.exports.download"), "MaintenanceDownload_" + job.Id, style: "AccentButtonStyle");
            download.Content = IconLabel("download", T("web.admin.catalogMaintenance.exports.download"), UI.Brush("AccentForegroundBrush"));
            download.IsEnabled = current;
            download.Click += async (_, _) => await DownloadAsync(owner, job.Id);
            actions.Children.Add(download);
        }
        if (Presentation.CanCreateLink(job))
        {
            var link = UI.Action(T("web.admin.catalogMaintenance.exports.createLink"), "MaintenanceCreateLink_" + job.Id);
            link.IsEnabled = current && !state.PublishPending;
            link.Click += async (_, _) => { if (_controller is { } controller && controller.IsCurrentOwner(owner)) await controller.PublishAsync(job.Id); };
            actions.Children.Add(link);
        }
        if (!string.IsNullOrEmpty(job.PublicUrl))
        {
            var copy = UI.Action(T("web.admin.catalogMaintenance.exports.copyUrl"), "MaintenanceCopyUrl_" + job.Id);
            copy.IsEnabled = current;
            copy.Click += (_, _) => Copy(owner, job.Id);
            actions.Children.Add(copy);
        }
        return Row(text, actions);
    }

    /// <summary>A read-only Job History row (status, type, target, requested, message, progress, result, finished, failure).</summary>
    private FrameworkElement HistoryRow(AdminMaintenanceJob job)
    {
        var panel = new StackPanel { Spacing = 4, Padding = new(16, 12, 16, 12) };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(Badge(Presentation.StatusLabel(job), Presentation.HistoryBadge(job)));
        if (Presentation.KindLabel(job.Kind) is { Length: > 0 } kind) line.Children.Add(Badge(kind, AdminMaintenanceBadge.Outline));
        if (Presentation.Description(job) is { Length: > 0 } description) line.Children.Add(UI.Strong(description, 14));
        line.Children.Add(UI.Text(AdminLibraryPresentation.DateTimeText(job.CreatedAt), 12, true));
        panel.Children.Add(Wrap(line));
        var details = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        if (Presentation.Message(job) is { Length: > 0 } message) details.Children.Add(UI.Text(message, 12, true));
        if (Presentation.IsActive(job)) details.Children.Add(UI.Text(T("web.admin.common.jobHistory.progress", ("progress", Presentation.ProgressText(job))), 12, true));
        if (Presentation.Result(job) is { } result) details.Children.Add(UI.Text(result, 12, true));
        if (job.FinishedAt is { } finished) details.Children.Add(UI.Text(T("web.admin.common.jobHistory.finished", ("date", AdminLibraryPresentation.DateTimeText(finished))), 12, true));
        if (details.Children.Count > 0) panel.Children.Add(Wrap(details));
        AddFailure(panel, job);
        AutomationProperties.SetAutomationId(panel, "MaintenanceHistoryRow_" + job.Id);
        return panel;
    }

    // ── Signed links ──────────────────────────────────────────────────────

    private async Task DownloadAsync(MonitorAuthorityLease? owner, string jobId)
    {
        var controller = _controller;
        if (controller == null || owner == null || !controller.IsCurrentOwner(owner)) return;
        var uri = controller.DownloadUri(owner, jobId);
        bool launched;
        if (uri == null) launched = false;
        else
        {
            try { launched = await Launcher(uri); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { launched = false; }
        }
        if (!launched && _active && ReferenceEquals(controller, _controller) && controller.IsCurrentOwner(owner))
            App.Services.GetService<ToastService>()?.Error(T("admin.native.maintenance.downloadFailed"));
    }

    private void Copy(MonitorAuthorityLease? owner, string jobId)
    {
        var controller = _controller;
        if (controller == null || owner == null || !controller.IsCurrentOwner(owner)) return;
        var url = controller.PublicUrl(owner, jobId);
        var copied = url != null && CopyText(url);
        var toast = App.Services.GetService<ToastService>();
        if (copied) toast?.Success(T("web.admin.catalogMaintenance.exports.copied"));
        else toast?.Error(T("web.admin.catalogMaintenance.exports.copyFailed"));
    }

    private static bool CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    // ── Building blocks ───────────────────────────────────────────────────

    private Button RefreshButton(AdminMaintenanceListKind kind, string automationId, MonitorAuthorityLease? owner, bool current)
    {
        var button = UI.Action(T("web.admin.catalogMaintenance.refresh"), automationId);
        button.Content = IconLabel("refresh-cw", T("web.admin.catalogMaintenance.refresh"));
        button.IsEnabled = current;
        button.Click += async (_, _) => { if (_controller is { } controller && controller.IsCurrentOwner(owner)) await controller.RefreshAsync(kind); };
        return button;
    }

    private FrameworkElement Row(FrameworkElement text, FrameworkElement actions)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12, Padding = new(16) };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetColumnSpan(text, _wide ? 1 : 2);
        grid.Children.Add(text);
        var wrapped = Wrap(actions);
        wrapped.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(wrapped, _wide ? 0 : 1); Grid.SetColumn(wrapped, _wide ? 1 : 0); Grid.SetColumnSpan(wrapped, _wide ? 1 : 2);
        grid.Children.Add(wrapped);
        return grid;
    }

    private static FrameworkElement TitleLine(AdminMaintenanceJob job, AdminMaintenanceBadge badge, string status, string label)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(Badge(status, badge));
        line.Children.Add(UI.Strong(label, 14));
        line.Children.Add(UI.Text(T("web.admin.catalogMaintenance.jobs.requested", ("date", AdminLibraryPresentation.DateTimeText(job.CreatedAt))), 12, true));
        return Wrap(line);
    }

    private static StackPanel Details(string progress)
    {
        var details = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        details.Children.Add(UI.Text(progress, 12, true));
        return details;
    }

    private static void AddFailure(Panel panel, AdminMaintenanceJob job)
    {
        if (Presentation.FailureText(job) is not { } failure) return;
        var text = UI.Text(failure, 12);
        text.Foreground = UI.Solid(UI.Destructive);
        panel.Children.Add(text);
    }

    private static Border ProgressTrack(double percent)
    {
        var fill = new Border { CornerRadius = new(999), Background = UI.Solid(UI.Primary), HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { Height = 8, CornerRadius = new(999), Background = UI.Tint("MutedBrush", 1), Child = fill };
        SiloPlayer.Helpers.RoundedBadge.SetRadius(fill, 999); SiloPlayer.Helpers.RoundedBadge.SetRadius(track, 999);
        track.SizeChanged += (_, args) => fill.Width = Math.Max(0, args.NewSize.Width * percent / 100);
        AutomationProperties.SetName(track, Math.Round(percent).ToString(Core.Localization.AppStrings.FormatCulture) + "%");
        return track;
    }

    /// <summary>A horizontal group that wraps onto further lines when narrow (flex-wrap).</summary>
    private static FrameworkElement Wrap(StackPanel line)
    {
        var wrap = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = line.Spacing, VerticalSpacing = 4 };
        var children = line.Children.ToArray();
        line.Children.Clear();
        foreach (var child in children) wrap.Children.Add(child);
        return wrap;
    }

    private static FrameworkElement Wrap(FrameworkElement element) => element is StackPanel { Orientation: Orientation.Horizontal } line ? Wrap(line) : element;

    private static Border Card(UIElement child, double padding = 0) => new()
    {
        Child = child, Padding = new(padding), CornerRadius = new(8), BorderThickness = new(1),
        BorderBrush = UI.Tint("BorderBrush", 0.7), Background = UI.Tint("SurfaceBrush", 0.6),
    };

    private static Border Badge(string text, AdminMaintenanceBadge badge)
    {
        if (badge != AdminMaintenanceBadge.Default)
            return UI.Badge(text, badge switch
            {
                AdminMaintenanceBadge.Destructive => UI.BadgeTone.Destructive,
                AdminMaintenanceBadge.Outline => UI.BadgeTone.Outline,
                _ => UI.BadgeTone.Secondary,
            });
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = UI.Brush("AccentForegroundBrush") };
        return new Border { Child = label, CornerRadius = new(6), Padding = new(8, 2, 8, 2), Background = UI.Solid(UI.Primary), HorizontalAlignment = HorizontalAlignment.Left };
    }

    private static FrameworkElement IconLabel(string icon, string label, Brush? foreground = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(Icon(icon, 14, foreground));
        var text = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        if (foreground != null) text.Foreground = foreground;
        content.Children.Add(text);
        return content;
    }

    /// <summary>Lucide download/upload are local to this page; other icons come from the shared Libraries set.</summary>
    private static FrameworkElement Icon(string name, double size, Brush? foreground)
    {
        var data = name switch
        {
            "download" => "M12 15V3 M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5",
            "upload" => "M12 3v12 M17 8l-5-5-5 5 M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4",
            _ => null,
        };
        if (data == null) return UI.Icon(name, size, foreground);
        var path = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data +
            "' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = foreground ?? UI.Brush("PrimaryTextBrush");
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>A message with its <c>&lt;code&gt;</c> segments set in a monospace font.</summary>
    private static TextBlock RichHelp(string formatted)
    {
        var block = UI.Text("", 12, true);
        foreach (var (text, tag) in AdminLibrariesText.Segments(formatted))
        {
            var run = new Run { Text = text };
            if (tag == "code") run.FontFamily = new FontFamily("Consolas");
            block.Inlines.Add(run);
        }
        AutomationProperties.SetName(block, AdminMaintenanceText.Plain(formatted));
        return block;
    }

    /// <summary>One job list card: header with count or Refreshing, official page controls and rows.</summary>
    private sealed class JobList
    {
        private readonly AdminMaintenancePage _page;
        private readonly AdminMaintenanceListKind _kind;
        private readonly string _id, _empty;
        private readonly string _loadOlder, _refreshing;
        private readonly bool _controlsFirst;
        private readonly ContentControl _badge = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _rows = new();
        private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(12) };
        private readonly Button? _refresh;
        private MonitorAuthorityLease? _refreshOwner;
        private object? _rendered;
        private bool _renderedWide;

        internal Border Root { get; }

        internal JobList(AdminMaintenancePage page, AdminMaintenanceListKind kind, string id, string title, string body, string empty, string loadOlder,
            string refreshing, bool controlsFirst, string? refreshName = null)
        {
            _page = page; _kind = kind; _id = id; _empty = empty; _loadOlder = loadOlder; _refreshing = refreshing; _controlsFirst = controlsFirst;
            var header = new Grid { ColumnSpacing = 8, Padding = new(16, 12, 16, 12) };
            header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var titles = new StackPanel { Spacing = 2 };
            var heading = UI.Text(title, 14); heading.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3);
            titles.Children.Add(heading);
            titles.Children.Add(UI.Text(body, 12, true));
            header.Children.Add(titles);
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            trailing.Children.Add(_badge);
            if (refreshName != null)
            {
                _refresh = UI.IconButton("refresh-cw", refreshName, id + "Refresh", 36, 16);
                _refresh.Click += async (_, _) => { if (_page._controller is { } controller && controller.IsCurrentOwner(_refreshOwner)) await controller.RefreshAsync(_kind); };
                trailing.Children.Add(_refresh);
            }
            Grid.SetColumn(trailing, 1); header.Children.Add(trailing);
            var stack = new StackPanel();
            stack.Children.Add(new Border { Child = header, BorderThickness = new(0, 0, 0, 1), BorderBrush = UI.Tint("BorderBrush", 0.7) });
            if (controlsFirst) stack.Children.Add(_controls);
            stack.Children.Add(_rows);
            if (!controlsFirst) stack.Children.Add(_controls);
            AutomationProperties.SetAutomationId(_rows, id + "Rows");
            Root = Card(stack);
            AutomationProperties.SetAutomationId(Root, id);
        }

        internal void Render(AdminMaintenanceState state, AdminMaintenanceList<AdminMaintenanceJob> list, bool current)
        {
            var count = UI.Badge(list.IsFetching ? _refreshing : list.Items.Count.ToString("N0", Core.Localization.AppStrings.FormatCulture),
                list.IsFetching ? UI.BadgeTone.Outline : UI.BadgeTone.Secondary);
            AutomationProperties.SetAutomationId(count, _id + "Count");
            _badge.Content = count;
            _refreshOwner = state.Owner;
            if (_refresh != null) _refresh.IsEnabled = current;
            RenderControls(list, state.Owner, current);
            // Rows are rebuilt only when the list or the state they show changed, so focus survives unrelated renders.
            object key = _kind == AdminMaintenanceListKind.Exports ? (list, state.PublishingJobId, current) : (list, current);
            if (Equals(key, _rendered) && _renderedWide == _page._wide) return;
            _rendered = key; _renderedWide = _page._wide;
            _rows.Children.Clear();
            if (list.Items.Count == 0)
            {
                var empty = UI.Text(_empty, 14, true); empty.Margin = new(16, 20, 16, 20);
                AutomationProperties.SetAutomationId(empty, _id + "Empty");
                _rows.Children.Add(empty);
                return;
            }
            var first = true;
            foreach (var job in list.Items)
            {
                if (!first) _rows.Children.Add(new Border { Height = 1, Background = UI.Tint("BorderBrush", 0.6) });
                first = false;
                _rows.Children.Add(_kind switch
                {
                    AdminMaintenanceListKind.Imports => _page.ImportRow(job, state.Owner, current),
                    AdminMaintenanceListKind.Exports => _page.ExportRow(state, job, state.Owner, current),
                    _ => _page.HistoryRow(job),
                });
            }
        }

        /// <summary>Official <c>JobPageControls</c>: “Jobs could not load. Restart jobs” on error, then Load older while more exist.</summary>
        private void RenderControls(AdminMaintenanceList<AdminMaintenanceJob> list, MonitorAuthorityLease? owner, bool current)
        {
            _controls.Children.Clear();
            if (list.Failure != null)
            {
                var alert = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var (text, tag) in AdminLibrariesText.Segments(T("web.admin.common.jobPageControls.loadFailed")))
                {
                    if (tag == "button")
                    {
                        var restart = UI.Action(text, _id + "Restart");
                        restart.IsEnabled = current;
                        restart.Click += async (_, _) => { if (_page._controller is { } controller && controller.IsCurrentOwner(owner)) await controller.RestartAsync(_kind); };
                        alert.Children.Add(restart);
                    }
                    else if (text.Trim().Length > 0) { var label = UI.Text(text.Trim(), 13); label.VerticalAlignment = VerticalAlignment.Center; alert.Children.Add(label); }
                }
                AutomationProperties.SetLiveSetting(alert, AutomationLiveSetting.Assertive);
                AutomationProperties.SetAutomationId(alert, _id + "LoadFailed");
                _controls.Children.Add(alert);
            }
            if (list.HasMore)
            {
                var more = UI.Action(_loadOlder, _id + "LoadOlder");
                more.IsEnabled = current && !list.LoadingMore && !list.Loading;
                more.Click += async (_, _) => { if (_page._controller is { } controller && controller.IsCurrentOwner(owner)) await controller.LoadMoreAsync(_kind); };
                _controls.Children.Add(more);
            }
            _controls.Visibility = _controls.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void Clear()
        {
            _rendered = null;
            _refreshOwner = null;
            if (_refresh != null) _refresh.IsEnabled = false;
            _rows.Children.Clear(); _controls.Children.Clear(); _badge.Content = null;
        }
    }
}
