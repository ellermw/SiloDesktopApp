using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Libraries › Subtitle Files (official WebUI <c>pages/AdminSubtitles.tsx</c>
/// with <c>components/admin/subtitles/*</c> at fd0d8c6c): stats, provider /
/// language / uploader / release-name filters mirrored into the route query,
/// a cursor-paged table with 25/50/100 rows, item links, bounded downloads,
/// the metadata editor and the delete confirmation. Subtitle content,
/// timing and provider configuration are not edited here. Pages swap in place
/// (no entrance or slide transition); the editor is an immediately shown
/// side panel, the delete confirmation a modal dialog.
/// </summary>
public sealed partial class AdminSubtitlesPage : Page, IAdminNavigationGuard, IAdminPageSuspension
{
    private const double StatsFourColumns = 1280;
    private const double StatsTwoColumns = 640;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly AdminSubtitlesApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body = Stack(24);
    private readonly StackPanel _skeleton = Stack(24);
    private readonly StackPanel _content = Stack(24);
    private readonly Grid _stats = new() { ColumnSpacing = 16, RowSpacing = 16 };
    private readonly StackPanel _alerts = Stack(8);
    private readonly ContentControl _table = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Grid _pagination = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly StackPanel _paginationControls = Inline(8);
    private readonly TextBlock _paginationSummary = Text("", 14, true);
    private readonly TextBlock _pageNumber = Text("", 14, true);
    private readonly Button _previousPage;
    private readonly Button _nextPage;
    private int _paginationColumns;
    private readonly TextBox _search;
    private readonly ComboBox _language;
    private readonly ComboBox _uploader;
    private readonly ComboBox _pageSize;
    private readonly LogsWrapPanel _providers = new() { Spacing = 6 };
    private readonly Grid _searchHost = new() { Width = 320 };
    private readonly LogsActionFeedback _feedback;
    private readonly LogsDetailSheet _sheet;
    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDebounce };
    private readonly AdminLeaveGuard _leaveGuard;
    private AdminSubtitlesController? _controller;
    private AdminRouteRequest _route = new(AdminRoute.SubtitleFiles);
    private AdminSubtitleListQuery _query = new();
    private bool _syncing;
    private bool _narrow;
    private double ViewportWidth => XamlRoot?.Size.Width ?? ActualWidth;

    /// <summary>The list owner (tests and fixtures).</summary>
    public AdminSubtitlesController? Controller => _controller;

    public AdminSubtitlesPage() : this(App.Services.GetRequiredService<AdminSubtitlesApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>()) { }

    public AdminSubtitlesPage(AdminSubtitlesApi api, AdminMonitorAuthority authority)
    {
        InitializeComponent();
        _api = api; _authority = authority;
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminSubtitlesPage");
        _leaveGuard = new AdminLeaveGuard(() => _controller?.State.Owner, () => _editor?.State.IsDirty == true, () => { _editor?.Discard(); CloseEditor(); });

        _search = Input(S("web.admin.common.subtitles.filters.searchPlaceholder"), "SubtitlesSearch", 384, mono: true);
        _search.FontSize = 12; _search.MaxWidth = 384; _search.Width = double.NaN; _search.MinWidth = 192; _search.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_search, S("web.admin.common.subtitles.filters.searchLabel"));
        _search.TextChanged += (_, _) => { if (_syncing) return; _searchTimer.Stop(); _searchTimer.Start(); };
        _search.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; _searchTimer.Stop(); ApplySearch(); } };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplySearch(); };

        foreach (var option in AdminSubtitlesText.ProviderOptions)
        {
            var pill = Button(option.Label, "SubtitlesProvider_" + option.Value, "GhostButtonStyle");
            pill.CornerRadius = new(15);
            pill.SizeChanged += (_, _) => pill.CornerRadius = new(pill.ActualHeight / 2); pill.MinHeight = 30; pill.Padding = new(12, 5, 12, 5); pill.FontSize = 12; pill.BorderThickness = new(1); pill.Tag = option.Value;
            var value = option.Value;
            pill.Click += (_, _) => UpdateFilter(_query with { Provider = value });
            _providers.Children.Add(pill);
        }
        AutomationProperties.SetName(_providers, S("web.admin.common.subtitles.filters.providerGroup"));

        _language = Select("SubtitlesLanguage", [(AdminSubtitleIds.FilterAll, S("web.admin.common.subtitles.filters.allLanguages"))], 180);
        _language.MinWidth = 180; _language.MaxWidth = 320; _language.Width = 200;
        AutomationProperties.SetName(_language, S("web.admin.common.subtitles.filters.languageLabel"));
        _language.PlaceholderText = S("web.admin.common.subtitles.filters.languagePlaceholder");
        _language.SelectionChanged += (_, _) => { if (!_syncing) UpdateFilter(_query with { Language = Selected(_language) }); };

        _uploader = Select("SubtitlesUploader", [(AdminSubtitleIds.FilterAll, S("web.admin.common.subtitles.filters.allUploaders"))], 200);
        _uploader.MinWidth = 200; _uploader.MaxWidth = 320; _uploader.Width = 220;
        AutomationProperties.SetName(_uploader, S("web.admin.common.subtitles.filters.uploaderLabel"));
        _uploader.PlaceholderText = S("web.admin.common.subtitles.filters.uploaderPlaceholder");
        _uploader.SelectionChanged += (_, _) => { if (!_syncing) UpdateFilter(_query with { UserId = Selected(_uploader) }); };

        _pageSize = Select("SubtitlesPageSize", AdminSubtitleListQuery.PageSizes.Select(size => (size.ToString(System.Globalization.CultureInfo.InvariantCulture), AdminSubtitlesText.PageSizeOption(size))), 110);
        AutomationProperties.SetName(_pageSize, AdminSubtitlesText.PageSizeOption(_query.PageSize));
        _pageSize.SelectionChanged += (_, _) =>
        {
            if (_syncing || !int.TryParse(Selected(_pageSize), out var size)) return;
            _query = _query with { PageSize = size };
            if (_controller != null) _ = _controller.SetQueryAsync(_query);
        };

        _previousPage = Button(S("web.admin.subtitles.page.previous"), "SubtitlesPrevious");
        _previousPage.Click += (_, _) => { if (_controller != null) _ = _controller.PreviousAsync(); };
        _nextPage = Button(S("web.admin.subtitles.page.next"), "SubtitlesNext");
        _nextPage.Click += (_, _) => { if (_controller != null) _ = _controller.NextAsync(); };
        AutomationProperties.SetAutomationId(_paginationSummary, "SubtitlesSummary");
        AutomationProperties.SetAutomationId(_pageNumber, "SubtitlesPageNumber");
        _pageNumber.Margin = new(4, 0, 4, 0);
        _paginationControls.Children.Add(_pageSize);
        _paginationControls.Children.Add(_previousPage);
        _paginationControls.Children.Add(_pageNumber);
        _paginationControls.Children.Add(_nextPage);
        _pagination.Children.Add(_paginationSummary);
        _pagination.Children.Add(_paginationControls);

        var reset = Button(S("web.admin.common.subtitles.resetFilters"), "SubtitlesReset", "GhostButtonStyle");
        reset.MinHeight = 32; reset.FontSize = 13; reset.Click += (_, _) => ResetFilters();

        var filterRow = new LogsWrapPanel { Spacing = 16, RowSpacing = 12 };
        _searchHost.Children.Add(_search); filterRow.Children.Add(_searchHost);
        filterRow.Children.Add(_providers);
        var selects = new LogsWrapPanel { Spacing = 8 }; reset.VerticalAlignment = VerticalAlignment.Center; selects.Children.Add(_language); selects.Children.Add(_uploader); selects.Children.Add(reset); filterRow.Children.Add(selects);
        var filters = Panel(filterRow, 16, 12); filters.BorderThickness = new(0); filters.Padding = new(16, 12, 16, 12);

        _content.Children.Add(Header(S("web.admin.subtitles.page.title"), S("web.admin.subtitles.page.subtitle")));
        var statsPanel = Panel(_stats, 16, 16); statsPanel.Background = Tint("MutedBrush", 0.25); statsPanel.BorderThickness = new(0);
        _content.Children.Add(statsPanel);
        _content.Children.Add(filters);
        _content.Children.Add(_alerts);
        _content.Children.Add(_table);
        _content.Children.Add(_pagination);
        BuildSkeleton();
        _body.Children.Add(_skeleton);
        _body.Children.Add(_content);
        _body.Padding = new(0, 0, 0, 24);

        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _root.Children.Add(_scroll);
        _sheet = new LogsDetailSheet(_scroll, 448);
        _sheet.Closed += OnSheetClosed;
        _root.Children.Add(_sheet);
        _feedback = new LogsActionFeedback(this);
        _root.Children.Add(_feedback);
        Content = _root;

        SizeChanged += (_, args) => ApplyWidth(args.NewSize.Width);
        Loaded += (_, _) => Open();
        Unloaded += (_, _) => Close();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not AdminRouteRequest route || route.Route != AdminRoute.SubtitleFiles) return;
        _route = route;
        _query = new AdminSubtitleListQuery
        {
            Provider = route.Get("provider"), Language = route.Get("language"), UserId = route.Get("user_id"), Search = route.Get("q"), PageSize = _query.PageSize,
        };
        SyncFilters(_query, route.Get("q") ?? "");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Open()
    {
        Close();
        var controller = new AdminSubtitlesController(_api, _authority);
        _controller = controller;
        controller.Changed += QueueRender;
        controller.Retired += OnRetired;
        controller.Notice += OnNotice;
        ApplyWidth(ActualWidth);
        Render(controller.State);
        _ = controller.OpenAsync(_query);
    }

    private void OnRetired()
    {
        var controller = _controller;
        EnqueueUi(() =>
        {
            if (!IsLoaded || controller == null || !ReferenceEquals(controller, _controller)) return;
            CloseEditor(); _deleteDialog?.Hide(); _discardDialog?.Hide(); _download?.Cancel();
            Render(controller.State);
            if (_authority.IsAvailable) _ = controller.OpenAsync(_query);
        });
    }

    /// <summary>The whole Admin area departed: retire reads, dialogs, drafts and downloads; never resume from here.</summary>
    public void SuspendAdminPage() => Close();

    private void Close()
    {
        _searchTimer.Stop();
        _feedback.Clear();
        CloseEditor();
        _deleteDialog?.Hide(); _deleteDialog = null;
        _discardDialog?.Hide(); _discardDialog = null;
        _deletion?.Dispose(); _deletion = null;
        _download?.Cancel(); _download = null;
        if (_controller != null)
        {
            _controller.Changed -= QueueRender; _controller.Retired -= OnRetired; _controller.Notice -= OnNotice;
            _controller.Dispose(); _controller = null;
        }
        _renderedTableState = null; _downloadButtons.Clear();
        _table.Content = null; _alerts.Children.Clear(); _pagination.Visibility = Visibility.Collapsed; _stats.Children.Clear();
        _skeleton.Visibility = Visibility.Visible; _content.Visibility = Visibility.Collapsed;
    }

    public bool RequiresLeaveConfirmation => _leaveGuard.RequiresConfirmation;

    public Task<bool> CanLeaveAsync() => _leaveGuard.CanLeaveAsync(ConfirmDiscardAsync);

    // Dispatcher callbacks must not escape into WinUI's native message loop.
    private void EnqueueUi(Action render)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            try { render(); }
            catch (Exception ex)
            {
                Core.Services.LocalLog.AppendLine("admin_monitor.txt",
                    $"subtitle files render failed: {ex.GetType().Name} (0x{ex.HResult:X8})");
            }
        });
    }

    private void QueueRender()
    {
        var controller = _controller;
        if (controller == null) return;
        var state = controller.State;
        EnqueueUi(() =>
        {
            if (IsLoaded && ReferenceEquals(controller, _controller) && controller.IsCurrent(state)) Render(state);
        });
    }

    private void OnNotice(string message, bool error)
    {
        var controller = _controller;
        EnqueueUi(() =>
        {
            if (IsLoaded && ReferenceEquals(controller, _controller)) _feedback.Show(error ? message : null, error ? null : message);
        });
    }

    // ── Filters and URL state ────────────────────────────────────────────

    private void ApplySearch() => UpdateFilter(_query with { Search = _search.Text });

    /// <summary>WebUI <c>updateFilter</c>: replace the URL in place, then restart at the first page.</summary>
    private void UpdateFilter(AdminSubtitleListQuery next)
    {
        var controller = _controller;
        if (controller?.State.Owner is not { IsCurrent: true }) return;
        _query = next;
        var request = _route;
        foreach (var (key, value) in new[] { ("provider", next.Provider), ("language", next.Language), ("user_id", next.UserId), ("q", next.Search) })
            request = request.WithQuery(key, string.IsNullOrWhiteSpace(value) || value == AdminSubtitleIds.FilterAll ? null : value);
        if (AdminShellPage.TryReplace(this, request)) _route = request;
        RenderProviders(next.Normalize());
        _ = controller.SetQueryAsync(next);
    }

    /// <summary>WebUI <c>resetFilters</c>: every URL parameter is cleared.</summary>
    private void ResetFilters()
    {
        var controller = _controller;
        if (controller?.State.Owner is not { IsCurrent: true }) return;
        _searchTimer.Stop();
        _query = new AdminSubtitleListQuery { PageSize = _query.PageSize };
        var request = new AdminRouteRequest(AdminRoute.SubtitleFiles);
        if (AdminShellPage.TryReplace(this, request)) _route = request;
        SyncFilters(_query, "");
        _ = controller.SetQueryAsync(_query);
    }

    private void SyncFilters(AdminSubtitleListQuery query, string searchText)
    {
        _syncing = true;
        try
        {
            if (_search.Text != searchText) _search.Text = searchText;
            var normalized = query.Normalize();
            RenderProviders(normalized);
            RebuildLanguageOptions(normalized.Language);
            RebuildUploaderOptions(_controller?.State, normalized.UserId);
            SetSelected(_pageSize, normalized.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { _syncing = false; }
    }

    private void RenderProviders(AdminSubtitleListQuery normalized)
    {
        var active = normalized.Provider ?? AdminSubtitleIds.FilterAll;
        foreach (var pill in _providers.Children.OfType<Button>())
        {
            var selected = (string)pill.Tag == active;
            pill.Background = selected ? Tint("AccentBrush", 0.15) : Tint("MutedBrush", 0.20);
            pill.BorderBrush = selected ? Tint("AccentBrush", 0.40) : Tint("BorderBrush", 0.60);
            pill.Foreground = Brush(selected ? "PrimaryTextBrush" : "SecondaryTextBrush");
            AutomationProperties.SetItemStatus(pill, selected ? "pressed" : "");
        }
    }

    private void RebuildLanguageOptions(string? selected)
    {
        _language.Items.Clear();
        _language.Items.Add(new ComboBoxItem { Content = S("web.admin.common.subtitles.filters.allLanguages"), Tag = AdminSubtitleIds.FilterAll });
        foreach (var option in AdminSubtitlesText.LanguageOptions()) _language.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Code });
        SetSelected(_language, selected ?? AdminSubtitleIds.FilterAll);
    }

    private void RebuildUploaderOptions(AdminSubtitlesSnapshot? state, string? selected)
    {
        _uploader.Items.Clear();
        _uploader.Items.Add(new ComboBoxItem { Content = S("web.admin.common.subtitles.filters.allUploaders"), Tag = AdminSubtitleIds.FilterAll });
        foreach (var user in state?.Uploaders ?? []) _uploader.Items.Add(new ComboBoxItem { Content = user.Username, Tag = user.Id });
        if (state?.UploadersTruncated == true)
            _uploader.Items.Add(new ComboBoxItem { Content = S("admin.native.subtitles.uploadersTruncated", ("count_text", (state.Uploaders.Count).ToString("N0", Core.Localization.AppStrings.FormatCulture))), IsEnabled = false, Tag = "" });
        if (state?.UploadersError is { } error) _uploader.Items.Add(new ComboBoxItem { Content = error, IsEnabled = false, Tag = "" });
        if (selected != null && !(state?.Uploaders ?? []).Any(user => user.Id == selected))
            _uploader.Items.Add(new ComboBoxItem { Content = S("admin.native.subtitles.uploaderId", ("id", selected)), Tag = selected });
        SetSelected(_uploader, selected ?? AdminSubtitleIds.FilterAll);
    }

    // ── Render ────────────────────────────────────────────────────────────

    private AdminSubtitlesSnapshot? _renderedTableState;
    private bool _renderedTableNarrow;
    private readonly Dictionary<string, Button> _downloadButtons = new(StringComparer.Ordinal);
    private string? _renderedDownloadId;
    private IReadOnlyList<AdminSubtitleUploader>? _renderedUploaders;
    private bool _renderedUploadersTruncated;
    private string? _renderedUploadersError;

    private void Render(AdminSubtitlesSnapshot state)
    {
        var ready = state.Owner != null && state.Loaded;
        _skeleton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        _content.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        if (state.Owner == null) { CloseEditor(); _deleteDialog?.Hide(); }
        if (!ready) return;

        if (!ReferenceEquals(_renderedUploaders, state.Uploaders) || _renderedUploadersTruncated != state.UploadersTruncated || _renderedUploadersError != state.UploadersError)
        {
            _renderedUploaders = state.Uploaders; _renderedUploadersTruncated = state.UploadersTruncated; _renderedUploadersError = state.UploadersError;
            _syncing = true;
            try { RebuildUploaderOptions(state, state.Query.UserId); }
            finally { _syncing = false; }
        }

        RenderStats(state);
        _alerts.Children.Clear();
        if (state.Error != null)
        {
            var banner = Banner(S("web.admin.subtitles.page.loadFailed", ("error", state.Error)), error: true);
            AutomationProperties.SetLiveSetting(banner, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
            _alerts.Children.Add(banner);
            var retry = Button(S("common.retry"), "SubtitlesRetry");
            retry.HorizontalAlignment = HorizontalAlignment.Left;
            retry.Click += (_, _) => { if (_controller != null) _ = _controller.ReloadAsync(); };
            _alerts.Children.Add(retry);
        }
        _alerts.Visibility = _alerts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var previous = _renderedTableState;
        if (previous == null || !ReferenceEquals(previous.Items, state.Items) || previous.Loading != state.Loading ||
            previous.Error != state.Error || previous.Query.HasActiveFilters != state.Query.HasActiveFilters || _renderedTableNarrow != _narrow)
        {
            _downloadButtons.Clear();
            _renderedDownloadId = state.DownloadingId;
            if (state.Loading) _table.Content = RowsSkeleton();
            else if (state.Error != null) _table.Content = null;
            else if (state.Items.Count == 0) _table.Content = EmptyState(state.Query.HasActiveFilters);
            else _table.Content = Table(state);
            _renderedTableState = state;
            _renderedTableNarrow = _narrow;
        }
        else if (_renderedDownloadId != state.DownloadingId)
        {
            // Keep row controls and keyboard focus when only the download state changes.
            foreach (var (id, button) in _downloadButtons)
            {
                button.IsEnabled = state.DownloadingId == null;
                if (id == _renderedDownloadId || id == state.DownloadingId)
                    button.Content = Glyph(id == state.DownloadingId ? "loader" : "download", 16);
            }
            _renderedDownloadId = state.DownloadingId;
        }

        RenderPagination(state);
    }

    private void RenderStats(AdminSubtitlesSnapshot state)
    {
        _stats.Children.Clear();
        var blocks = new (string Key, string Label, int Value)[]
        {
            ("total", S("web.admin.subtitles.stats.totalStored"), state.Total),
            ("uploads", S("web.admin.subtitles.stats.userUploads"), state.Uploads),
            ("providers", S("web.admin.subtitles.stats.providerDownloads"), state.ProviderDownloads),
            ("languages", S("web.admin.subtitles.stats.languagesOnPage"), state.LanguagesOnPage),
        };
        var columns = ViewportWidth >= StatsFourColumns ? 4 : ViewportWidth >= StatsTwoColumns ? 2 : 1;
        _stats.ColumnDefinitions.Clear(); _stats.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++) _stats.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var row = 0; row < (blocks.Length + columns - 1) / columns; row++) _stats.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < blocks.Length; index++)
        {
            var (key, label, value) = blocks[index];
            var block = Stack(4);
            var caption = Text(Core.Localization.AppStrings.Uppercase(label), 12, true); caption.FontWeight = FontWeights.Medium; caption.CharacterSpacing = 180;
            block.Children.Add(caption);
            FrameworkElement number = state.Loading ? SkeletonBar(28, 72, 6) : Text(value.ToString("N0", Core.Localization.AppStrings.FormatCulture), 24);
            if (number is TextBlock text) { text.FontWeight = FontWeights.SemiBold; text.CharacterSpacing = -25; text.LineHeight = 32; }
            AutomationProperties.SetAutomationId(number, "SubtitlesStat_" + key);
            block.Children.Add(number);
            Grid.SetColumn(block, index % columns); Grid.SetRow(block, index / columns);
            _stats.Children.Add(block);
        }
    }

    private void RenderPagination(AdminSubtitlesSnapshot state)
    {
        // Keep interactive controls mounted across list reads and button clicks.
        _pagination.Visibility = state.Loaded && (state.Total > 0 || state.PageIndex > 0) ? Visibility.Visible : Visibility.Collapsed;
        _paginationSummary.Visibility = state.Loading ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(_pageSize, AdminSubtitlesText.PageSizeOption(state.Query.PageSize));
        _paginationSummary.Text = S("web.admin.subtitles.page.summary", ("count", state.Items.Count), ("total", state.Total));
        _pageNumber.Text = S("web.admin.subtitles.page.pageNumber", ("page", state.PageIndex + 1));
        _previousPage.IsEnabled = state.CanPrevious && !state.Loading;
        _nextPage.IsEnabled = state.CanNext && !state.Loading;
        _syncing = true;
        try { SetSelected(_pageSize, state.Query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        finally { _syncing = false; }
        var columns = _narrow ? 1 : 2;
        if (_paginationColumns == columns) return;
        _paginationColumns = columns;
        _pagination.ColumnDefinitions.Clear(); _pagination.RowDefinitions.Clear();
        if (_narrow)
        {
            _pagination.RowDefinitions.Add(new() { Height = GridLength.Auto }); _pagination.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(_paginationControls, 0); Grid.SetRow(_paginationControls, 1);
        }
        else
        {
            _pagination.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _pagination.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Grid.SetRow(_paginationControls, 0); Grid.SetColumn(_paginationControls, 1);
        }
    }

    private FrameworkElement EmptyState(bool filtered)
    {
        var stack = Stack(8); stack.HorizontalAlignment = HorizontalAlignment.Center; stack.MaxWidth = 512;
        var bars = Stack(6); bars.Width = 448; bars.MaxWidth = 448; bars.Margin = new(0, 0, 0, 12); bars.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var width in new[] { 1.0, 0.8, 0.55 })
            bars.Children.Add(new Border { Height = 10, Width = 448 * width, CornerRadius = new(5), Background = Tint("MutedBrush", 0.6), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(bars);
        var title = Text(S(filtered ? "web.admin.common.subtitles.table.emptyFiltered" : "web.admin.common.subtitles.table.empty"), 18);
        title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center; title.TextAlignment = TextAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        AutomationProperties.SetAutomationId(title, "SubtitlesEmptyTitle");
        stack.Children.Add(title);
        var help = Text(S(filtered ? "web.admin.common.subtitles.table.emptyFilteredHelp" : "web.admin.common.subtitles.table.emptyHelp"), 14, true);
        help.TextAlignment = TextAlignment.Center; help.HorizontalAlignment = HorizontalAlignment.Center; help.LineHeight = 22;
        stack.Children.Add(help);
        if (filtered)
        {
            var reset = Button(S("web.admin.common.subtitles.resetFilters"), "SubtitlesEmptyReset");
            reset.HorizontalAlignment = HorizontalAlignment.Center; reset.Margin = new(0, 12, 0, 0); reset.Click += (_, _) => ResetFilters();
            stack.Children.Add(reset);
        }
        var panel = Panel(stack, 16, 24); panel.BorderThickness = new(0); panel.Padding = new(24, 64, 24, 64);
        return panel;
    }

    private void BuildSkeleton()
    {
        var titles = Stack(12);
        titles.Children.Add(SkeletonBar(48, 288, 8));
        titles.Children.Add(SkeletonBar(20, double.NaN, 8, 576));
        _skeleton.Children.Add(titles);
        _skeleton.Children.Add(SkeletonBar(80, double.NaN, 16));
        _skeleton.Children.Add(SkeletonBar(96, double.NaN, 16));
        _skeleton.Children.Add(RowsSkeleton());
        AutomationProperties.SetAutomationId(_skeleton, "SubtitlesSkeleton");
        _content.Visibility = Visibility.Collapsed;
    }

    private static StackPanel RowsSkeleton()
    {
        var rows = Stack(12);
        for (var index = 0; index < 6; index++) rows.Children.Add(SkeletonBar(48, double.NaN, 8));
        AutomationProperties.SetAutomationId(rows, "SubtitlesRowsSkeleton");
        return rows;
    }

    private static Border SkeletonBar(double height, double width, double radius, double maxWidth = double.PositiveInfinity) => new()
    {
        Height = height, Width = width, MaxWidth = maxWidth, CornerRadius = new(radius), Background = Tint("MutedBrush", 0.7),
        HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
    };

    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        _searchHost.Width = Math.Max(160, Math.Min(384, width - 64));
        var viewportWidth = ViewportWidth;
        var narrow = viewportWidth < StatsTwoColumns;
        var changed = narrow != _narrow;
        _narrow = narrow;
        if (_controller != null && (changed || _stats.ColumnDefinitions.Count != (viewportWidth >= StatsFourColumns ? 4 : viewportWidth >= StatsTwoColumns ? 2 : 1)))
            QueueRender(); // Never replace table children from a SizeChanged/layout callback.
    }

    private void OpenItem(string contentId)
    {
        if (_controller?.State.Owner is not { IsCurrent: true }) return;
        App.Services.GetService<NavigationService>()?.Navigate<ItemDetailPage>(contentId);
    }

    internal static string S(string key, params (string Name, object? Value)[] values) => AdminSubtitlesText.T(key, values);
}
