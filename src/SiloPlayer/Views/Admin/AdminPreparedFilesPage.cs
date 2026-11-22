using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Manage &gt; Downloads &gt; Prepared Files (<c>/admin/downloads</c>): the five official tabs (Storage, Preparation, Prepared files,
/// Device copies, History) of Silo Server/WebUI <c>pages/AdminDownloads.tsx</c> and <c>pages/admin-downloads/*</c> at 66b836f82.
/// Each tab is a persistent view whose rows and controls are built once and updated in place from controller snapshots
/// (see <c>AdminPreparedFilesPage.Stable.cs</c>), so a progress reading, a poll or a selection toggle never replaces a control the
/// administrator is using, moves focus or resets what was typed. Pages swap in place with no entrance or slide transition.
/// </summary>
public sealed partial class AdminPreparedFilesPage : Page, IAdminGutterScrollPage, IAdminPageSuspension
{
    private static readonly string[] TabIds = ["storage", "preparation", "files", "devices", "history"];

    private readonly AdminPreparedFilesApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminPreparedFilesRealtimeSource? _realtime;
    private readonly AdminMonitorApi? _monitor;
    private readonly DispatcherQueue _dispatcher;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _layout = Stack(24);
    private readonly StackPanel _tabs = Inline(4);
    private readonly StackPanel _notices = Stack(8);
    private readonly ContentControl _bodyHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Dictionary<string, Button> _tabButtons = [];
    private readonly Dictionary<string, TextBlock> _tabLabels = [];
    private readonly Dictionary<string, Border> _tabBadges = [];
    private readonly Dictionary<string, TabView> _views = [];
    private AdminRouteRequest _request = new(AdminRoute.PreparedFiles);
    private ContentDialog? _dialog;
    private string? _shownTab;
    private (string Active, int Jobs)? _strip;
    private (AdminPreparedActionState? Action, bool Blocked)? _noticeKey;
    private bool _opened;
    private bool _renderQueued;
    private bool _disposed;

    public AdminPreparedFilesController Controller { get; private set; }
    public FrameworkElement ScrollContent => _layout;

    /// <summary>Test seam: the tab the content area shows.</summary>
    internal string? ShownTab => _shownTab;

    public AdminPreparedFilesPage() : this(App.Services.GetRequiredService<AdminPreparedFilesApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>(),
        App.Services.GetService<AdminPreparedFilesRealtimeSource>(), App.Services.GetService<AdminMonitorApi>()) { }

    public AdminPreparedFilesPage(AdminPreparedFilesApi api, AdminMonitorAuthority authority, AdminPreparedFilesRealtimeSource? realtime = null, AdminMonitorApi? monitor = null)
    {
        _api = api; _authority = authority; _realtime = realtime; _monitor = monitor; _dispatcher = DispatcherQueue.GetForCurrentThread();
        Controller = CreateController();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminPreparedFilesPage");
        BuildShell();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is AdminRouteRequest request && request.Route == AdminRoute.PreparedFiles) _request = request;
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SuspendAdminPage();
        base.OnNavigatedFrom(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_opened) return;
        if (_disposed) { Controller = CreateController(); _disposed = false; }
        _opened = true;
        await OpenControllerAsync();
    }

    private async Task OpenControllerAsync()
    {
        foreach (var view in _views.Values) view.Reset();
        try { await Controller.OpenAsync(_request.Get("tab"), _request.Get("location"), _request.Get("stale") == "1"); }
        catch (Exception) when (_disposed) { }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Nested Frame pages may transiently unload while the Admin shell is still current.
        if (Frame?.Content != this) SuspendAdminPage();
    }

    public void SuspendAdminPage()
    {
        if (_disposed) return;
        try { _dialog?.Hide(); } catch (Exception) { /* already closing */ }
        _dialog = null;
        Controller.Changed -= QueueRender; Controller.Retired -= QueueReopen; Controller.Dispose();
        foreach (var view in _views.Values) view.Reset();
        _opened = false; _disposed = true;
    }

    private AdminPreparedFilesController CreateController()
    {
        var controller = new AdminPreparedFilesController(_api, _authority, _realtime?.Channel, new SystemAdminScheduler(), _monitor);
        controller.Changed += QueueRender;
        controller.Retired += QueueReopen;
        return controller;
    }

    private void BuildShell()
    {
        _scroll.Content = _layout;
        _scroll.Padding = new Thickness(0, 20, 0, 40); // the shell applies the gutters and maximum width to the layout
        _layout.Children.Add(Header(T("web.admin.downloads.page.title"), T("web.admin.downloads.page.subtitle")));
        foreach (var tab in TabIds)
        {
            var label = new TextBlock { Text = AdminPreparedFilesText.Tab(tab), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            var content = Inline(8);
            content.Children.Add(label);
            _tabLabels[tab] = label;
            if (tab == "preparation")
            {
                var count = new TextBlock { FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                var badge = new Border { Child = count, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 3, 6, 3), Background = Tint("MutedBrush", 0.5) };
                content.Children.Add(badge);
                _tabBadges[tab] = badge;
            }
            var button = new Button
            {
                Content = content, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = 40, Padding = new Thickness(14, 6, 14, 6),
                BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent), CornerRadius = new CornerRadius(0),
            };
            AutomationProperties.SetAutomationId(button, "PreparedFilesTab_" + tab);
            AutomationProperties.SetName(button, AdminPreparedFilesText.Tab(tab));
            var id = tab;
            button.Click += (_, _) => OpenTab(id);
            _tabButtons[tab] = button;
            _tabs.Children.Add(button);
        }
        var strip = new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ScrollViewer { Content = _tabs, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        _layout.Children.Add(strip);
        _layout.Children.Add(_notices);
        _layout.Children.Add(_bodyHost);
        PageRoot.Children.Add(_scroll);
        _views["storage"] = new StorageView(this);
        _views["preparation"] = new PreparationView(this);
        _views["files"] = new FilesView(this);
        _views["devices"] = new DevicesView(this);
        _views["history"] = new HistoryView(this);
    }

    // ---- Navigation -------------------------------------------------------------------------------

    private void ReplaceRoute(string tab, string? location = null, bool stale = false)
    {
        var query = new Dictionary<string, string>();
        if (tab != "storage") query["tab"] = tab;
        if (!string.IsNullOrEmpty(location)) query["location"] = location;
        if (stale) query["stale"] = "1";
        var request = new AdminRouteRequest(AdminRoute.PreparedFiles, query.Count == 0 ? null : query);
        if (AdminShellPage.TryReplace(this, request)) _request = request;
    }

    /// <summary>A tab opens with its own default filters, as the official tabs remount when left.</summary>
    private async void OpenTab(string tab)
    {
        if (_disposed || Controller.State.ActiveTab == tab) return;
        ReplaceRoute(tab);
        await ActivateAsync(tab, null, false);
    }

    private async Task ActivateAsync(string tab, string? location, bool stale)
    {
        if (_disposed) return;
        _views[tab].Reset();
        // The tabs being left reset their queries without any read; only the tab that opens reads its list.
        await Controller.ActivateTabAsync(tab, new AdminPreparedFileQuery { Location = location }, new AdminDownloadDeviceQuery { Stale = stale }, new AdminDownloadHistoryQuery());
    }

    private async void OpenFilesAt(string location)
    {
        if (_disposed || !AdminPreparedFilesContract.IsLocation(location)) return;
        ReplaceRoute("files", location);
        await ActivateAsync("files", location, false);
    }

    private async void OpenDevices(bool stale)
    {
        if (_disposed) return;
        ReplaceRoute("devices", null, stale);
        await ActivateAsync("devices", null, stale);
    }

    private void QueueReopen()
    {
        _dispatcher.TryEnqueue(async () =>
        {
            try { _dialog?.Hide(); } catch (Exception) { /* already closing */ }
            _dialog = null;
            if (_disposed || !_opened || !_authority.IsAvailable) return;
            await OpenControllerAsync();
        });
    }

    private void QueueRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        _dispatcher.TryEnqueue(() => { _renderQueued = false; if (!_disposed) Render(Controller.State); });
    }

    // ---- Rendering --------------------------------------------------------------------------------

    private void Render(AdminPreparedFilesSnapshot state)
    {
        var active = state.ActiveTab;
        var activeJobs = state.PreparationsLoaded ? AdminPreparationPresentation.ActiveCount(state.PreparationCounts) : 0;
        // The tab strip and its badge change only when the shown tab or the active-job count does.
        if (_strip != (active, activeJobs))
        {
            _strip = (active, activeJobs);
            var accent = Brush("AccentBrush");
            foreach (var (tab, button) in _tabButtons)
            {
                var selected = tab == active;
                button.BorderBrush = selected ? accent : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                _tabLabels[tab].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                AutomationProperties.SetItemStatus(button, selected ? "selected" : "");
            }
            if (_tabBadges.TryGetValue("preparation", out var badge) && badge.Child is TextBlock count)
            {
                count.Text = activeJobs > 99 ? T("web.admin.downloads.controls.countOverflow") : activeJobs.ToString(System.Globalization.CultureInfo.CurrentCulture);
                count.Foreground = Brush(activeJobs > 0 ? "AccentBrush" : "SecondaryTextBrush");
                badge.Background = activeJobs > 0 ? Tint("AccentBrush", 0.12) : Tint("MutedBrush", 0.5);
            }
        }
        RenderNotices(state);
        if (state.Owner == null)
        {
            if (_shownTab != "closed")
            {
                _bodyHost.Content = Panel(Text(T("configure.native.preparedFiles.adminRequired")), 16, 24, true);
                _shownTab = "closed";
            }
            return;
        }
        if (_shownTab != active)
        {
            _bodyHost.Content = _views[active].Root;
            _shownTab = active;
        }
        // Only the tab that is shown draws, and only when what it shows changed; a control the administrator was using keeps focus.
        KeepFocus(() => _views[active].Apply(state));
    }

    private void RenderNotices(AdminPreparedFilesSnapshot state)
    {
        var key = (state.LastAction, state.DestructiveBlocked);
        if (_noticeKey == key) return;
        _noticeKey = key;
        _notices.Children.Clear();
        if (state.LastAction is { } action)
        {
            // A partial failure ("Deleted 2, 1 failed") is an error, as the official page's error toast; a part the server refused on purpose is not.
            var failed = action.IsError;
            var row = Inline(12);
            row.Children.Add(Icon(failed ? "circle-alert" : "circle-check", 16, Brush(failed ? "ErrorBrush" : "SuccessBrush")));
            var message = Text(action.Message, 14, !failed);
            message.MaxWidth = 900;
            AutomationProperties.SetAutomationId(message, "PreparedFilesNoticeText");
            row.Children.Add(message);
            var dismiss = Button(T("configure.native.preparedFiles.dismiss"), "PreparedFilesNoticeDismiss", "GhostButtonStyle");
            dismiss.Click += (_, _) => Controller.DismissNotice();
            row.Children.Add(dismiss);
            var panel = Panel(row, 12, 0, true);
            panel.Padding = new Thickness(16, 8, 16, 8);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesNotice");
            AutomationProperties.SetLiveSetting(panel, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            _notices.Children.Add(panel);
        }
        if (state.DestructiveBlocked)
        {
            var row = Inline(12);
            row.Children.Add(Icon("triangle-alert", 16, Brush("WarningBrush")));
            row.Children.Add(Text(T("configure.native.preparedFiles.blocked"), 14));
            var reload = Button(T("configure.native.preparedFiles.reload"), "PreparedFilesBlockedReload", "OutlineButtonStyle");
            reload.Click += (_, _) => _ = Controller.ReloadBlockedAsync();
            row.Children.Add(reload);
            var panel = Panel(row, 12, 0, true);
            panel.Padding = new Thickness(16, 8, 16, 8);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesBlocked");
            _notices.Children.Add(panel);
        }
    }

    // ---- Shared building blocks -------------------------------------------------------------------

    internal static string T(string key, object? values = null) => AdminPreparedFilesText.T(key, values);

    private static string Format(DateTimeOffset value) => SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(value);

    private abstract class TabView(AdminPreparedFilesPage page)
    {
        private object? _inputs;
        protected AdminPreparedFilesPage P { get; } = page;
        public abstract UIElement Root { get; }
        /// <summary>Draws the tab from the snapshot, updating what changed in place.</summary>
        public abstract void Update(AdminPreparedFilesSnapshot state);
        /// <summary>What the tab draws from; a snapshot that changes none of it (a poll of something else) draws nothing.</summary>
        protected abstract object Inputs(AdminPreparedFilesSnapshot state);
        public void Apply(AdminPreparedFilesSnapshot state)
        {
            var inputs = Inputs(state);
            if (_inputs != null && _inputs.Equals(inputs)) return;
            _inputs = inputs;
            Update(state);
        }
        /// <summary>Back to the defaults a freshly mounted official tab starts with.</summary>
        public virtual void Reset() { _inputs = null; }
    }

    private static TextBlock Strong(string text, double size = 14, bool muted = false)
    {
        var block = Text(text, size, muted);
        block.FontWeight = FontWeights.SemiBold;
        return block;
    }

    /// <summary>A text block whose rich tags draw as bold, code and muted runs.</summary>
    private static TextBlock RichText(IEnumerable<AdminPreparedRichRun> runs, double size = 14, bool muted = false)
    {
        var block = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(muted ? "SecondaryTextBrush" : "PrimaryTextBrush"), LineHeight = size <= 12 ? 16 : 20 };
        foreach (var run in runs)
        {
            var inline = new Run { Text = run.Text };
            if (run.Has("b") || run.Has("strong")) inline.FontWeight = FontWeights.SemiBold;
            if (run.Has("code")) inline.FontFamily = new FontFamily("Consolas");
            if (run.Has("hint") || run.Has("muted")) inline.Foreground = Brush("SecondaryTextBrush");
            block.Inlines.Add(inline);
        }
        return block;
    }

    private static Border Tile(string label, string value, UIElement detail, Brush? valueBrush = null)
    {
        var stack = Stack(4);
        stack.Children.Add(Text(label, 12, true));
        var amount = Text(value, 24); amount.FontWeight = FontWeights.SemiBold;
        if (valueBrush != null) amount.Foreground = valueBrush;
        stack.Children.Add(amount);
        stack.Children.Add(detail);
        return Panel(stack, 16, 16, true);
    }

    private static Border Pill(string text, string tone)
    {
        var (foreground, background) = tone switch
        {
            "active" => (Brush("PrimaryTextBrush"), Tint("Chart1Brush", 0.30)),
            "cached" => (Brush("PrimaryTextBrush"), Tint("Chart2Brush", 0.30)),
            "warning" => (Brush("WarningBrush"), Tint("WarningBrush", 0.12)),
            "danger" => (Brush("ErrorLabelBrush"), Brush("ErrorTintBrush")),
            "success" => (Brush("SuccessBrush"), Tint("SuccessBrush", 0.12)),
            "neutral" => (Brush("PrimaryTextBrush"), Tint("MutedBrush", 0.8)),
            _ => (Brush("SecondaryTextBrush"), Tint("MutedBrush", 0.8)),
        };
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = foreground, TextWrapping = TextWrapping.NoWrap };
        return new Border { Child = label, Background = background, CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
    }

    private static CheckBox Check(string label, string id, bool isChecked, bool enabled, Action<bool> changed)
    {
        var box = new CheckBox { MinWidth = 0, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 0, 0) };
        if (!string.IsNullOrEmpty(label)) box.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        box.IsChecked = isChecked;
        AutomationProperties.SetAutomationId(box, id);
        AutomationProperties.SetName(box, label);
        // Handlers attach after the initial state is set, so showing a saved value never reads as a change.
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    /// <summary>
    /// A search box that applies its text on Enter or when it loses focus (the official SearchField). The starting text is never
    /// applied: only a different trimmed value is reported, so WinUI's initial text notification cannot start a read.
    /// </summary>
    private sealed class SearchField
    {
        private readonly TextBox _box;
        private string _applied = "";
        public SearchField(string label, string id, double width, Action<string> apply)
        {
            _box = Input(label, id, width);
            _box.FontSize = 13; _box.Height = 32;
            AutomationProperties.SetName(_box, label);
            void Commit()
            {
                var value = _box.Text.Trim();
                if (value == _applied) return;
                _applied = value;
                apply(value);
            }
            _box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Commit(); } };
            _box.LostFocus += (_, _) => Commit();
        }
        public UIElement Root => _box;
        public string Applied => _applied;
        public void Reset() { _applied = ""; _box.Text = ""; }
    }

    /// <summary>A centered message, optionally with a reload control the administrator can always reach.</summary>
    private static Border Message(string text, string? reloadId = null, Action? reload = null, string? reloadLabel = null)
    {
        var stack = Stack(12);
        var message = Text(text, 14, true); message.HorizontalAlignment = HorizontalAlignment.Center; message.TextAlignment = TextAlignment.Center;
        stack.Children.Add(message);
        if (reload != null)
        {
            var button = Button(reloadLabel ?? T("common.tryAgain"), reloadId ?? "PreparedFilesReload");
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Click += (_, _) => reload();
            stack.Children.Add(button);
        }
        return Panel(stack, 16, 24, true);
    }

    /// <summary>The banner above rows that stay on screen after a background read failed.</summary>
    private static Border StaleBanner(string reloadId, Action reload) =>
        Message(T("configure.native.preparedFiles.backgroundFailed"), reloadId, reload, T("configure.native.preparedFiles.reload"));

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialog != null || XamlRoot == null || _disposed) return ContentDialogResult.None;
        dialog.XamlRoot = XamlRoot;
        dialog.Resources["ContentDialogMaxWidth"] = 560d;
        _dialog = dialog;
        try { return await dialog.ShowAsync(); }
        catch (OperationCanceledException) { return ContentDialogResult.None; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private ContentDialog NewDialog(string title, UIElement content, string primary, bool destructive, string? cancel = null)
    {
        var dialog = new ContentDialog { Title = title, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 },
            PrimaryButtonText = primary, CloseButtonText = cancel ?? T("common.cancel"), DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary };
        if (destructive) dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"];
        return dialog;
    }

    private static string LocationName(AdminDownloadStorage? storage, string key) =>
        storage?.Locations.FirstOrDefault(x => x.Key == key)?.Name ?? key;
}
