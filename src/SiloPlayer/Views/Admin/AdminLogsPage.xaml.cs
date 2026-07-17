using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminLogsPage : Page
{
    public AdminLogsViewModel ViewModel { get; }

    private bool _isAppTab = true;
    private OperationalLogEntry? _selectedAppEntry;
    private bool _rebuildAppPending;
    private bool _rebuildAuditPending;
    private bool _suspendAppCollectionRebuild;
    private bool _suspendAuditCollectionRebuild;
    private bool _loaded;
    private bool _uiEventsAttached;

    // Debounce timer for live-filter (webui has no Search button — inputs filter on change)
    private DispatcherTimer? _filterDebounce;

    // Live log stream (webui parity — /api/v1/admin/logs/ws). One client per
    // active tab; reconnect on tab switch or filter change.
    private AdminLogStreamClient? _stream;
    private readonly SemaphoreSlim _streamRestartGate = new(1, 1);
    private bool _isNavigatedAway;
    private const int LogStreamCap = 500;

    // Navigation parameter: pass a string "sessionId" or "sessionId|ffmpeg" to pre-filter logs
    private string? _pendingSessionId;
    private bool _pendingFfmpegFilter;

    public AdminLogsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminLogsViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
    }

    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width);
        AdminPageContent.Width = Math.Min(1640, width);
        var horizontalPadding = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
        var verticalPadding = width >= 1024 ? 32 : 16;
        AdminPageContent.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, 40);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isNavigatedAway = false;
        if (e.Parameter is string param && !string.IsNullOrWhiteSpace(param))
        {
            var parts = param.Split('|');
            _pendingSessionId = parts[0];
            _pendingFfmpegFilter = parts.Length > 1 && parts[1] == "ffmpeg";
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        ViewModel.AppLogs.CollectionChanged += AppLogs_CollectionChanged;
        ViewModel.AuditLogs.CollectionChanged += AuditLogs_CollectionChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Apply navigation parameter (from "View Logs" / "FFmpeg Logs" links)
        if (!string.IsNullOrWhiteSpace(_pendingSessionId))
        {
            ViewModel.PlaybackSessionId = _pendingSessionId;
            if (_pendingFfmpegFilter)
                ViewModel.AppComponent = "ffmpeg";
            _pendingSessionId = null;
        }

        // Live-filter: debounce text changes. Restarting the stream pushes
        // new filter params to the server; the incoming snapshot replaces
        // the current rows.
        _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _filterDebounce.Tick += async (_, _) =>
        {
            _filterDebounce!.Stop();
            await RestartStreamAsync();
        };
        if (!_uiEventsAttached)
        {
            _uiEventsAttached = true;
            AppRequestIdBox.TextChanged += FilterTextBox_TextChanged;
            AppMessageBox.TextChanged += FilterTextBox_TextChanged;
            AppComponentBox.TextChanged += FilterTextBox_TextChanged;
            AuditRequestIdBox.TextChanged += FilterTextBox_TextChanged;
            AuditMethodBox.TextChanged += FilterTextBox_TextChanged;
            AuditClientIpBox.TextChanged += FilterTextBox_TextChanged;
            PlaybackSessionBox.TextChanged += PlaybackSessionBox_TextChanged;
        }

        SetActiveTab(_isAppTab);
        UpdatePlaybackSessionTag();

        // Live stream replaces the old "load once, refresh manually" pattern.
        // On connect the server pushes a snapshot of recent rows; subsequent
        // appends show up immediately via the OnAppAppend / OnAuditAppend paths.
        await RestartStreamAsync();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _loaded = false;
        _isNavigatedAway = true;
        _filterDebounce?.Stop();
        ViewModel.AppLogs.CollectionChanged -= AppLogs_CollectionChanged;
        ViewModel.AuditLogs.CollectionChanged -= AuditLogs_CollectionChanged;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        await _streamRestartGate.WaitAsync();
        try
        {
            if (_stream != null)
            {
                try { await _stream.StopAsync(); } catch { }
                _stream.Dispose();
                _stream = null;
            }
        }
        finally { _streamRestartGate.Release(); }
    }

    private void AppLogs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_suspendAppCollectionRebuild) ScheduleRebuildApp();
    }

    private void AuditLogs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_suspendAuditCollectionRebuild) ScheduleRebuildAudit();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(ViewModel.PlaybackSessionId):
                DispatcherQueue.TryEnqueue(UpdatePlaybackSessionTag);
                break;
            case nameof(ViewModel.AppLogsHasMore):
            case nameof(ViewModel.AuditLogsHasMore):
            case nameof(ViewModel.IsLoadingMore):
                DispatcherQueue.TryEnqueue(UpdateLoadMoreState);
                break;
        }
    }

    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => RestartFilterDebounce();

    private void RestartFilterDebounce()
    {
        _filterDebounce?.Stop();
        _filterDebounce?.Start();
    }

    private void ScheduleRebuildApp()
    {
        if (_rebuildAppPending) return;
        _rebuildAppPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildAppPending = false;
            RebuildAppTable();
        });
    }

    private void ScheduleRebuildAudit()
    {
        if (_rebuildAuditPending) return;
        _rebuildAuditPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildAuditPending = false;
            RebuildAuditTable();
        });
    }

    // ===== Tab switching =====

    private void TabApp_Click(object sender, RoutedEventArgs e)
    {
        if (_isAppTab) return;
        SetActiveTab(true);
        _ = RestartStreamAsync();
    }

    private void TabAudit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAppTab) return;
        SetActiveTab(false);
        _ = RestartStreamAsync();
    }

    // ===== Live log stream (webui parity: /admin/logs/ws) =====

    private async Task RestartStreamAsync()
    {
        await _streamRestartGate.WaitAsync();
        try
        {
            if (_isNavigatedAway) return;
            if (_stream != null)
            {
                await _stream.StopAsync();
            }
            else
            {
                var api = App.Services.GetRequiredService<SiloApiClient>();
                if (string.IsNullOrEmpty(api.BaseUrl) || string.IsNullOrEmpty(api.AccessToken)) return;
                _stream = new AdminLogStreamClient(api.BaseUrl, () => api.AccessToken);
                _stream.AppSnapshotReceived += OnAppSnapshot;
                _stream.AuditSnapshotReceived += OnAuditSnapshot;
                _stream.AppEntryAppended += OnAppAppend;
                _stream.AuditEntryAppended += OnAuditAppend;
                _stream.StateChanged += OnStreamStateChanged;
                _stream.ErrorReceived += (msg) => DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.ConnectionState = "Disconnected";
                    ReconnectButton.Visibility = Visibility.Visible;
                });
            }

            var filters = BuildCurrentFilters();
            var stream = _isAppTab ? AdminLogStreamClient.Stream.App : AdminLogStreamClient.Stream.Audit;
            await _stream.StartAsync(stream, filters);
        }
        catch { /* surfaced via StateChanged */ }
        finally { _streamRestartGate.Release(); }
    }

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        await RestartStreamAsync();
        ReconnectButton.IsEnabled = true;
    }

    private Dictionary<string, string> BuildCurrentFilters()
    {
        var map = new Dictionary<string, string> { ["limit"] = "200" };
        var playback = ViewModel.PlaybackSessionId?.Trim();
        if (!string.IsNullOrEmpty(playback)) map["playback_session_id"] = playback;
        if (_isAppTab)
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.AppRequestId)) map["request_id"] = ViewModel.AppRequestId.Trim();
            if (!string.IsNullOrWhiteSpace(ViewModel.AppMessageQuery)) map["q"] = ViewModel.AppMessageQuery.Trim();
            if (!string.IsNullOrWhiteSpace(ViewModel.AppComponent)) map["component"] = ViewModel.AppComponent.Trim();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.AuditRequestId)) map["request_id"] = ViewModel.AuditRequestId.Trim();
            if (!string.IsNullOrWhiteSpace(ViewModel.AuditMethod)) map["method"] = ViewModel.AuditMethod.Trim();
            if (!string.IsNullOrWhiteSpace(ViewModel.AuditClientIp)) map["client_ip"] = ViewModel.AuditClientIp.Trim();
        }
        return map;
    }

    private void OnAppSnapshot(List<OperationalLogEntry> entries, string? nextCursor)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _suspendAppCollectionRebuild = true;
            try
            {
                ViewModel.AppLogs.Clear();
                foreach (var e in entries) ViewModel.AppLogs.Add(e);
            }
            finally { _suspendAppCollectionRebuild = false; }
            ViewModel.AppLogsNextCursor = nextCursor;
            RebuildAppTable();
        });
    }

    private void OnAuditSnapshot(List<AuditLogEntry> entries, string? nextCursor)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _suspendAuditCollectionRebuild = true;
            try
            {
                ViewModel.AuditLogs.Clear();
                foreach (var e in entries) ViewModel.AuditLogs.Add(e);
            }
            finally { _suspendAuditCollectionRebuild = false; }
            ViewModel.AuditLogsNextCursor = nextCursor;
            RebuildAuditTable();
        });
    }

    private void OnAppAppend(OperationalLogEntry entry)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isNavigatedAway) return;
            _suspendAppCollectionRebuild = true;
            try
            {
                var existingIndex = -1;
                for (var i = 0; i < ViewModel.AppLogs.Count; i++)
                {
                    if (ViewModel.AppLogs[i].Id == entry.Id) { existingIndex = i; break; }
                }
                if (existingIndex >= 0)
                {
                    ViewModel.AppLogs.RemoveAt(existingIndex);
                    if (_isAppTab && existingIndex < AppLogsPanel_Rows.Children.Count)
                        AppLogsPanel_Rows.Children.RemoveAt(existingIndex);
                }
                ViewModel.AppLogs.Insert(0, entry);
                if (_isAppTab)
                    AppLogsPanel_Rows.Children.Insert(0, BuildAppLogRow(entry));
                while (ViewModel.AppLogs.Count > LogStreamCap)
                {
                    ViewModel.AppLogs.RemoveAt(ViewModel.AppLogs.Count - 1);
                    if (_isAppTab && AppLogsPanel_Rows.Children.Count > LogStreamCap)
                        AppLogsPanel_Rows.Children.RemoveAt(AppLogsPanel_Rows.Children.Count - 1);
                }
            }
            finally { _suspendAppCollectionRebuild = false; }
            AppLogsEmpty.Visibility = Visibility.Collapsed;
            AppLogsLoading.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(ViewModel.PlaybackSessionId)) RebuildPlaybackSummary();
        });
    }

    private void OnAuditAppend(AuditLogEntry entry)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isNavigatedAway) return;
            _suspendAuditCollectionRebuild = true;
            try
            {
                var existingIndex = -1;
                for (var i = 0; i < ViewModel.AuditLogs.Count; i++)
                {
                    if (ViewModel.AuditLogs[i].Id == entry.Id) { existingIndex = i; break; }
                }
                if (existingIndex >= 0)
                {
                    ViewModel.AuditLogs.RemoveAt(existingIndex);
                    if (!_isAppTab && existingIndex < AuditLogsPanel_Rows.Children.Count)
                        AuditLogsPanel_Rows.Children.RemoveAt(existingIndex);
                }
                ViewModel.AuditLogs.Insert(0, entry);
                if (!_isAppTab)
                    AuditLogsPanel_Rows.Children.Insert(0, BuildAuditLogRow(entry));
                while (ViewModel.AuditLogs.Count > LogStreamCap)
                {
                    ViewModel.AuditLogs.RemoveAt(ViewModel.AuditLogs.Count - 1);
                    if (!_isAppTab && AuditLogsPanel_Rows.Children.Count > LogStreamCap)
                        AuditLogsPanel_Rows.Children.RemoveAt(AuditLogsPanel_Rows.Children.Count - 1);
                }
            }
            finally { _suspendAuditCollectionRebuild = false; }
            AuditLogsEmpty.Visibility = Visibility.Collapsed;
            AuditLogsLoading.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(ViewModel.PlaybackSessionId)) RebuildPlaybackSummary();
        });
    }

    private void OnStreamStateChanged(AdminLogStreamClient.ConnectionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.ConnectionState = state switch
            {
                AdminLogStreamClient.ConnectionState.Connecting => "Connecting…",
                AdminLogStreamClient.ConnectionState.Live => "Live",
                _ => "Disconnected",
            };
            ReconnectButton.Visibility = state == AdminLogStreamClient.ConnectionState.Disconnected
                ? Visibility.Visible
                : Visibility.Collapsed;
        });
    }

    private void SetActiveTab(bool appTab)
    {
        _isAppTab = appTab;

        var accentBg = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        var transparent = new SolidColorBrush(Colors.Transparent);

        TabAppBtn.Background = appTab ? accentBg : transparent;
        TabAppBtn.Foreground = appTab ? accentFg : secondaryFg;
        TabAppBtn.BorderThickness = new Thickness(1);
        TabAppBtn.BorderBrush = appTab
            ? (SolidColorBrush)Application.Current.Resources["BorderBrush"]
            : transparent;

        TabAuditBtn.Background = !appTab ? accentBg : transparent;
        TabAuditBtn.Foreground = !appTab ? accentFg : secondaryFg;
        TabAuditBtn.BorderThickness = new Thickness(1);
        TabAuditBtn.BorderBrush = !appTab
            ? (SolidColorBrush)Application.Current.Resources["BorderBrush"]
            : transparent;

        AppLogsPanel.Visibility = appTab ? Visibility.Visible : Visibility.Collapsed;
        AuditLogsPanel.Visibility = !appTab ? Visibility.Visible : Visibility.Collapsed;
        if (appTab) RebuildAppTable(); else RebuildAuditTable();

        // Clear detail panel when switching
        HideAppDetail();
    }

    // ===== Playback session tag & summary =====

    private void UpdatePlaybackSessionTag()
    {
        var pid = ViewModel.PlaybackSessionId;
        if (!string.IsNullOrWhiteSpace(pid))
        {
            PlaybackSessionTag.Visibility = Visibility.Visible;
            PlaybackSessionTagText.Text = $"Playback session {AdminLogsViewModel.ShortId(pid.Trim())}";
            PlaybackSummaryPanel.Visibility = Visibility.Visible;
            RebuildPlaybackSummary();
        }
        else
        {
            PlaybackSessionTag.Visibility = Visibility.Collapsed;
            PlaybackSummaryPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RebuildPlaybackSummary()
    {
        ViewModel.UpdatePlaybackSummary();
        SummaryMetricsPanel.Children.Clear();

        AddSummaryMetric("Playback Session", AdminLogsViewModel.ShortId(ViewModel.PlaybackSessionId), true);
        AddSummaryMetric("Application Logs", ViewModel.SummaryAppCount.ToString(), false);
        AddSummaryMetric("FFmpeg Logs", ViewModel.SummaryFfmpegCount.ToString(), false);
        AddSummaryMetric("Audit Logs", ViewModel.SummaryAuditCount.ToString(), false);
        AddSummaryMetric("First Seen", ViewModel.SummaryFirstSeen, false);
        AddSummaryMetric("Nodes Seen", ViewModel.SummaryNodes, ViewModel.SummaryNodes != "-");
        if (ViewModel.SummaryLastSeen != "-")
            AddSummaryMetric("Last Seen", ViewModel.SummaryLastSeen, false);

        // Update ffmpeg button text
        var isFFmpegFilter = ViewModel.AppComponent.Trim().Equals("ffmpeg", StringComparison.OrdinalIgnoreCase);
        BtnFilterFfmpegText.Text = isFFmpegFilter ? "Showing ffmpeg only" : "Open ffmpeg logs";
    }

    private void AddSummaryMetric(string label, string value, bool mono)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = mono ? 12 : 14,
            FontFamily = mono ? new FontFamily("Consolas") : null,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        var index = SummaryMetricsPanel.Children.Count;
        var row = index / 6;
        while (SummaryMetricsPanel.RowDefinitions.Count <= row)
            SummaryMetricsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(stack, index % 6);
        Grid.SetRow(stack, row);
        SummaryMetricsPanel.Children.Add(stack);
    }

    private async void BtnFilterFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var isFFmpegFilter = ViewModel.AppComponent.Trim().Equals("ffmpeg", StringComparison.OrdinalIgnoreCase);
        ViewModel.AppComponent = isFFmpegFilter ? "" : "ffmpeg";
        RebuildPlaybackSummary();
        _filterDebounce?.Stop();
        await RestartStreamAsync();
    }

    private void PlaybackSessionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaybackSessionTag();
        RestartFilterDebounce();
    }

    private async void PlaybackSessionBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _filterDebounce?.Stop();
            await RestartStreamAsync();
        }
    }

    // ===== Search button handlers =====

    private async void BtnSearchApp_Click(object sender, RoutedEventArgs e)
    {
        HideAppDetail();
        try { await ViewModel.LoadAppLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    private async void BtnSearchAudit_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.LoadAuditLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    // ===== KeyDown handlers =====

    private async void FilterBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        _filterDebounce?.Stop();
        HideAppDetail();
        await RestartStreamAsync();
    }

    private async void AuditFilterBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        _filterDebounce?.Stop();
        await RestartStreamAsync();
    }

    // ===== App log table =====

    private void RebuildAppTable()
    {
        AppLogsPanel_Rows.Children.Clear();
        HideAppDetail();

        var logs = ViewModel.AppLogs;
        if (logs.Count == 0)
        {
            AppLogsEmpty.Visibility = ViewModel.IsLoading ? Visibility.Collapsed : Visibility.Visible;
            AppLogsLoading.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        AppLogsEmpty.Visibility = Visibility.Collapsed;
        AppLogsLoading.Visibility = Visibility.Collapsed;

        foreach (var entry in logs)
        {
            var row = BuildAppLogRow(entry);
            AppLogsPanel_Rows.Children.Add(row);
        }

        // Update summary if playback session is active
        if (!string.IsNullOrWhiteSpace(ViewModel.PlaybackSessionId))
            RebuildPlaybackSummary();
    }

    private FrameworkElement BuildAppLogRow(OperationalLogEntry entry)
    {
        // Highlight ffmpeg rows when in playback focus mode
        bool highlight = !string.IsNullOrWhiteSpace(ViewModel.PlaybackSessionId) && entry.Component == "ffmpeg";

        var row = new Grid
        {
            Padding = new Thickness(12, 8, 12, 8),
            ColumnSpacing = 8,
            Tag = entry,
            Background = highlight
                ? new SolidColorBrush(Color.FromArgb(12, 99, 102, 241))  // bg-primary/5
                : new SolidColorBrush(Colors.Transparent),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        // Proportional columns matching XAML header: Time / Level / Component / Status / Duration / Message
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });

        var defaultBg = row.Background;

        // Time
        row.Children.Add(MakeTextCell(0, AdminLogsViewModel.FormatDateTime(entry.Timestamp), 14));

        // Level (uppercase)
        var levelBlock = new TextBlock
        {
            Text = entry.Level.ToUpperInvariant(),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetLevelBrush(entry.Level),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(levelBlock, 1);
        row.Children.Add(levelBlock);

        // Component
        row.Children.Add(MakeTextCell(2, entry.Component, 14));

        // Status from attrs
        row.Children.Add(MakeTextCell(3, AdminLogsViewModel.GetAttr(entry, "status"), 14));

        // Duration from attrs (duration_ms + " ms")
        row.Children.Add(MakeTextCell(4, AdminLogsViewModel.GetDurationAttr(entry), 14));

        // Message (max-w truncated with title tooltip)
        var msgBlock = new TextBlock
        {
            Text = entry.Message,
            FontSize = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(msgBlock, entry.Message);
        Grid.SetColumn(msgBlock, 5);
        row.Children.Add(msgBlock);

        // Make row clickable (cursor-pointer) — click opens detail sheet with full metadata
        row.PointerPressed += (_, _) => ShowAppDetail(entry);
        row.PointerEntered += (_, _) =>
            row.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
        row.PointerExited += (_, _) =>
            row.Background = defaultBg;

        return row;
    }

    // ===== App detail panel =====

    private void ShowAppDetail(OperationalLogEntry entry)
    {
        // Toggle: deselect if same entry clicked again
        if (_selectedAppEntry == entry)
        {
            HideAppDetail();
            return;
        }

        _selectedAppEntry = entry;
        AppDetailPanel.Visibility = Visibility.Visible;
        AppDetailContent.Children.Clear();

        // Title: message
        AppDetailContent.Children.Add(new TextBlock
        {
            Text = entry.Message,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 40, 0),
        });

        // Subtitle: component . LEVEL . timestamp
        AppDetailContent.Children.Add(new TextBlock
        {
            Text = $"{entry.Component} \u00B7 {entry.Level.ToUpperInvariant()} \u00B7 {AdminLogsViewModel.FormatDateTime(entry.Timestamp)}",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });

        // 2-column detail grid
        var detailGrid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fields = new[]
        {
            ("Request ID", entry.RequestId ?? "-", true),
            ("Node", entry.NodeId ?? "-", false),
            ("Method", AdminLogsViewModel.GetAttr(entry, "method"), false),
            ("Status", AdminLogsViewModel.GetAttr(entry, "status"), false),
            ("Duration", AdminLogsViewModel.GetDurationAttr(entry), false),
            ("Client IP", !string.IsNullOrEmpty(entry.ClientIp) ? entry.ClientIp : AdminLogsViewModel.GetAttr(entry, "client_ip"), true),
            ("User ID", entry.UserId.HasValue ? entry.UserId.ToString()! : "-", false),
            ("Session ID", entry.SessionId ?? AdminLogsViewModel.GetAttr(entry, "session_id"), true),
            ("Playback Session", entry.PlaybackSessionId ?? AdminLogsViewModel.GetAttr(entry, "playback_session_id"), true),
        };

        for (int i = 0; i < fields.Length; i++)
        {
            var (label, value, mono) = fields[i];
            int gridRow = i / 2;
            int col = i % 2;

            while (detailGrid.RowDefinitions.Count <= gridRow)
                detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var fieldStack = new StackPanel { Spacing = 2 };
            fieldStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
            fieldStack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = mono ? 12 : 13,
                FontFamily = mono ? new FontFamily("Consolas") : null,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });

            Grid.SetRow(fieldStack, gridRow);
            Grid.SetColumn(fieldStack, col);
            detailGrid.Children.Add(fieldStack);
        }

        AppDetailContent.Children.Add(detailGrid);

        // "View related playback session logs" link button
        var playbackId = entry.PlaybackSessionId ?? AdminLogsViewModel.GetAttr(entry, "playback_session_id");
        if (!string.IsNullOrEmpty(playbackId) && playbackId != "-")
        {
            var linkBtn = new Button
            {
                Content = "View related playback session logs",
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 4, 0, 4),
                Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            linkBtn.Click += (_, _) =>
            {
                ViewModel.PlaybackSessionId = playbackId;
                UpdatePlaybackSessionTag();
                HideAppDetail();
                _ = ViewModel.LoadAppLogsCommand.ExecuteAsync(null);
                _ = ViewModel.LoadAuditLogsCommand.ExecuteAsync(null);
            };
            AppDetailContent.Children.Add(linkBtn);
        }

        // Path block
        var pathVal = AdminLogsViewModel.GetAttr(entry, "path");
        if (pathVal != "-")
        {
            var pathStack = new StackPanel { Spacing = 4 };
            pathStack.Children.Add(new TextBlock
            {
                Text = "Path",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
            });
            pathStack.Children.Add(new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Child = new TextBlock
                {
                    Text = pathVal,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap
                }
            });
            AppDetailContent.Children.Add(pathStack);
        }

        // Attributes block
        var attrsStack = new StackPanel { Spacing = 4 };
        attrsStack.Children.Add(new TextBlock
        {
            Text = "Attributes",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        string attrsJson = entry.Attrs != null
            ? JsonSerializer.Serialize(entry.Attrs, new JsonSerializerOptions { WriteIndented = true })
            : "{}";

        attrsStack.Children.Add(new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Child = new ScrollViewer
            {
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = attrsJson,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap
                }
            }
        });

        AppDetailContent.Children.Add(attrsStack);
    }

    private void HideAppDetail()
    {
        _selectedAppEntry = null;
        AppDetailPanel.Visibility = Visibility.Collapsed;
        AppDetailContent.Children.Clear();
    }

    private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
    {
        HideAppDetail();
    }

    // ===== Audit log table =====

    private void RebuildAuditTable()
    {
        AuditLogsPanel_Rows.Children.Clear();

        var logs = ViewModel.AuditLogs;
        if (logs.Count == 0)
        {
            AuditLogsEmpty.Visibility = ViewModel.IsLoading ? Visibility.Collapsed : Visibility.Visible;
            AuditLogsLoading.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        AuditLogsEmpty.Visibility = Visibility.Collapsed;
        AuditLogsLoading.Visibility = Visibility.Collapsed;

        foreach (var entry in logs)
        {
            AuditLogsPanel_Rows.Children.Add(BuildAuditLogRow(entry));
        }

        // Update summary if playback session is active
        if (!string.IsNullOrWhiteSpace(ViewModel.PlaybackSessionId))
            RebuildPlaybackSummary();
    }

    private static FrameworkElement BuildAuditLogRow(AuditLogEntry entry)
    {
        var row = new Grid
        {
            Padding = new Thickness(12, 8, 12, 8),
            ColumnSpacing = 8,
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        // Proportional columns matching XAML header
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

        // Time (formatted, whitespace-nowrap)
        row.Children.Add(MakeTextCell(0, AdminLogsViewModel.FormatDateTime(entry.Timestamp), 14));

        // Method
        row.Children.Add(MakeTextCell(1, entry.Method, 14));

        // Path (monospace 12px, max-w 420px truncated with title tooltip)
        var pathBlock = new TextBlock
        {
            Text = entry.Path,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(pathBlock, entry.Path);
        Grid.SetColumn(pathBlock, 2);
        row.Children.Add(pathBlock);

        // Status (colored)
        var statusBlock = new TextBlock
        {
            Text = entry.StatusCode.ToString(),
            FontSize = 14,
            Foreground = GetStatusBrush(entry.StatusCode),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(statusBlock, 3);
        row.Children.Add(statusBlock);

        // Client IP (strip CIDR suffix, mono 12px)
        row.Children.Add(MakeMonoCell(4, AdminLogsViewModel.FormatClientIp(entry.ClientIp), 12));

        // User: #ID or -
        row.Children.Add(MakeTextCell(5, entry.UserId.HasValue ? $"#{entry.UserId}" : "-", 14));

        // Session (mono 12px)
        row.Children.Add(MakeMonoCell(6, entry.SessionId ?? "-", 12));

        // Playback session (mono 12px)
        row.Children.Add(MakeMonoCell(7, entry.PlaybackSessionId ?? "-", 12));

        // Request ID (mono 12px)
        row.Children.Add(MakeMonoCell(8, entry.RequestId ?? "-", 12));

        row.PointerEntered += (_, _) =>
            row.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
        row.PointerExited += (_, _) =>
            row.Background = new SolidColorBrush(Colors.Transparent);

        return row;
    }

    // ===== Helpers =====

    private static TextBlock MakeTextCell(int col, string text, double fontSize, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = bold
                ? (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static TextBlock MakeMonoCell(int col, string text, double fontSize)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static SolidColorBrush GetLevelBrush(string level) =>
        level.ToLowerInvariant() switch
        {
            "error" or "fatal" => new SolidColorBrush(Color.FromArgb(255, 248, 81, 73)),
            "warn" or "warning" => new SolidColorBrush(Color.FromArgb(255, 210, 153, 34)),
            "debug" or "trace" => (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            _ => (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

    private static SolidColorBrush GetStatusBrush(int status) =>
        status switch
        {
            >= 500 => new SolidColorBrush(Color.FromArgb(255, 248, 81, 73)),
            >= 400 => new SolidColorBrush(Color.FromArgb(255, 210, 153, 34)),
            >= 200 and < 300 => new SolidColorBrush(Color.FromArgb(255, 63, 185, 80)),
            _ => (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

    // ===== Cursor pagination =====

    private void UpdateLoadMoreState()
    {
        // App logs footer.
        AppLogsLoadMorePanel.Visibility = ViewModel.AppLogsHasMore
            ? Visibility.Visible : Visibility.Collapsed;
        AppLogsLoadMoreButton.IsEnabled = !ViewModel.IsLoadingMore;
        AppLogsLoadMoreRing.IsActive = ViewModel.IsLoadingMore;
        AppLogsLoadMoreRing.Visibility = ViewModel.IsLoadingMore
            ? Visibility.Visible : Visibility.Collapsed;

        // Audit logs footer.
        AuditLogsLoadMorePanel.Visibility = ViewModel.AuditLogsHasMore
            ? Visibility.Visible : Visibility.Collapsed;
        AuditLogsLoadMoreButton.IsEnabled = !ViewModel.IsLoadingMore;
        AuditLogsLoadMoreRing.IsActive = ViewModel.IsLoadingMore;
        AuditLogsLoadMoreRing.Visibility = ViewModel.IsLoadingMore
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AppLogsLoadMore_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.LoadMoreAppLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    private async void AuditLogsLoadMore_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.LoadMoreAuditLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }
}
