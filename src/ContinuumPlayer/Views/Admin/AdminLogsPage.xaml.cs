using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminLogsPage : Page
{
    public AdminLogsViewModel ViewModel { get; }

    private bool _isAppTab = true;
    private OperationalLogEntry? _selectedAppEntry;
    private bool _rebuildAppPending;
    private bool _rebuildAuditPending;

    // Navigation parameter: pass a string "sessionId" or "sessionId|ffmpeg" to pre-filter logs
    private string? _pendingSessionId;
    private bool _pendingFfmpegFilter;

    public AdminLogsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminLogsViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string param && !string.IsNullOrWhiteSpace(param))
        {
            var parts = param.Split('|');
            _pendingSessionId = parts[0];
            _pendingFfmpegFilter = parts.Length > 1 && parts[1] == "ffmpeg";
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AppLogs.CollectionChanged += (_, _) => ScheduleRebuildApp();
        ViewModel.AuditLogs.CollectionChanged += (_, _) => ScheduleRebuildAudit();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.PlaybackSessionId))
                DispatcherQueue.TryEnqueue(UpdatePlaybackSessionTag);
        };

        // Apply navigation parameter (from "View Logs" / "FFmpeg Logs" links)
        if (!string.IsNullOrWhiteSpace(_pendingSessionId))
        {
            ViewModel.PlaybackSessionId = _pendingSessionId;
            if (_pendingFfmpegFilter)
                ViewModel.AppComponent = "ffmpeg";
            _pendingSessionId = null;
        }

        SetActiveTab(true);
        UpdatePlaybackSessionTag();
        try { await ViewModel.LoadAppLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
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
        if (ViewModel.AppLogs.Count == 0)
            _ = ViewModel.LoadAppLogsCommand.ExecuteAsync(null);
    }

    private void TabAudit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAppTab) return;
        SetActiveTab(false);
        if (ViewModel.AuditLogs.Count == 0)
            _ = ViewModel.LoadAuditLogsCommand.ExecuteAsync(null);
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

        TabAuditBtn.Background = !appTab ? accentBg : transparent;
        TabAuditBtn.Foreground = !appTab ? accentFg : secondaryFg;

        AppLogsPanel.Visibility = appTab ? Visibility.Visible : Visibility.Collapsed;
        AuditLogsPanel.Visibility = !appTab ? Visibility.Visible : Visibility.Collapsed;

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
        if (ViewModel.SummaryLastSeen != "-")
            AddSummaryMetric("Last Seen", ViewModel.SummaryLastSeen, false);
        AddSummaryMetric("Nodes Seen", ViewModel.SummaryNodes, ViewModel.SummaryNodes != "-");

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
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = mono ? 12 : 13,
            FontFamily = mono ? new FontFamily("Consolas") : null,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        SummaryMetricsPanel.Children.Add(stack);
    }

    private void BtnFilterFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var isFFmpegFilter = ViewModel.AppComponent.Trim().Equals("ffmpeg", StringComparison.OrdinalIgnoreCase);
        ViewModel.AppComponent = isFFmpegFilter ? "" : "ffmpeg";
        RebuildPlaybackSummary();
        _ = ViewModel.LoadAppLogsCommand.ExecuteAsync(null);
    }

    private void PlaybackSessionBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            UpdatePlaybackSessionTag();
            // Reload both tabs with the playback session filter
            _ = ViewModel.LoadAppLogsCommand.ExecuteAsync(null);
            _ = ViewModel.LoadAuditLogsCommand.ExecuteAsync(null);
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

    private void FilterBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) BtnSearchApp_Click(sender, e);
    }

    private void AuditFilterBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) BtnSearchAudit_Click(sender, e);
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

        bool first = true;
        foreach (var entry in logs)
        {
            if (!first)
            {
                AppLogsPanel_Rows.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            first = false;

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
            Padding = new Thickness(16, 8, 16, 8),
            ColumnSpacing = 8,
            Tag = entry,
            Background = highlight
                ? new SolidColorBrush(Color.FromArgb(12, 99, 102, 241))  // bg-primary/5
                : new SolidColorBrush(Colors.Transparent)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        var defaultBg = row.Background;

        // Time (whitespace-nowrap)
        row.Children.Add(MakeMonoCell(0, AdminLogsViewModel.FormatDateTime(entry.Timestamp), 11));

        // Level (uppercase)
        var levelBlock = new TextBlock
        {
            Text = entry.Level.ToUpperInvariant(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetLevelBrush(entry.Level),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(levelBlock, 1);
        row.Children.Add(levelBlock);

        // Component
        row.Children.Add(MakeTextCell(2, entry.Component, 12));

        // User: #ID or -
        row.Children.Add(MakeTextCell(3, entry.UserId.HasValue ? $"#{entry.UserId}" : "-", 12));

        // Session (mono 12px)
        row.Children.Add(MakeMonoCell(4, entry.SessionId ?? AdminLogsViewModel.GetAttr(entry, "session_id"), 11));

        // Playback (mono 12px)
        row.Children.Add(MakeMonoCell(5, entry.PlaybackSessionId ?? AdminLogsViewModel.GetAttr(entry, "playback_session_id"), 11));

        // Method from attrs
        row.Children.Add(MakeTextCell(6, AdminLogsViewModel.GetAttr(entry, "method"), 12));

        // Path from attrs (mono 12px, max-w 260px truncated with title tooltip)
        var pathAttr = AdminLogsViewModel.GetAttr(entry, "path");
        var pathBlock = new TextBlock
        {
            Text = pathAttr,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(pathBlock, pathAttr);
        Grid.SetColumn(pathBlock, 7);
        row.Children.Add(pathBlock);

        // Status from attrs
        row.Children.Add(MakeTextCell(8, AdminLogsViewModel.GetAttr(entry, "status"), 12));

        // Duration from attrs (duration_ms + " ms")
        row.Children.Add(MakeTextCell(9, AdminLogsViewModel.GetDurationAttr(entry), 11));

        // Message (max-w 360px truncated with title tooltip)
        var msgBlock = new TextBlock
        {
            Text = entry.Message,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 360,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(msgBlock, entry.Message);
        Grid.SetColumn(msgBlock, 10);
        row.Children.Add(msgBlock);

        // Request ID (mono 12px)
        row.Children.Add(MakeMonoCell(11, entry.RequestId ?? "-", 11));

        // Make row clickable (cursor-pointer)
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
            TextWrapping = TextWrapping.Wrap
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

        bool first = true;
        foreach (var entry in logs)
        {
            if (!first)
            {
                AuditLogsPanel_Rows.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            first = false;
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
            Padding = new Thickness(16, 8, 16, 8),
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        // Time (formatted, whitespace-nowrap)
        row.Children.Add(MakeMonoCell(0, AdminLogsViewModel.FormatDateTime(entry.Timestamp), 11));

        // Method
        row.Children.Add(MakeTextCell(1, entry.Method, 12, bold: true));

        // Path (monospace 12px, max-w 420px truncated with title tooltip)
        var pathBlock = new TextBlock
        {
            Text = entry.Path,
            FontSize = 11,
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
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetStatusBrush(entry.StatusCode),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(statusBlock, 3);
        row.Children.Add(statusBlock);

        // Client IP (strip CIDR suffix, mono 12px)
        row.Children.Add(MakeMonoCell(4, AdminLogsViewModel.FormatClientIp(entry.ClientIp), 11));

        // User: #ID or -
        row.Children.Add(MakeTextCell(5, entry.UserId.HasValue ? $"#{entry.UserId}" : "-", 12));

        // Session (mono 12px)
        row.Children.Add(MakeMonoCell(6, entry.SessionId ?? "-", 11));

        // Playback session (mono 12px)
        row.Children.Add(MakeMonoCell(7, entry.PlaybackSessionId ?? "-", 11));

        // Request ID (mono 12px)
        row.Children.Add(MakeMonoCell(8, entry.RequestId ?? "-", 11));

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
}
