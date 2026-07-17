using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels.Admin;
using Microsoft.UI.Xaml.Navigation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminTasksPage : Page
{
    public AdminTasksViewModel ViewModel { get; }

    private DispatcherTimer? _refreshTimer;
    private bool _rebuildPending;
    private MetadataRefreshMetrics? _refreshMetrics;
    private EventChannelClient? _eventChannel;
    private IDisposable? _eventSubscription;
    private DateTime _lastEventRefresh = DateTime.MinValue;
    private bool _compactLayout;
    private bool _loaded;
    private int _taskActionFeedbackCount;

    public AdminTasksPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminTasksViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        ApplyResponsiveLayout();
        ViewModel.Tasks.CollectionChanged += Tasks_CollectionChanged;
        if (ViewModel.Tasks.Count > 0) RebuildTaskGroups();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            // Load refresh_metadata metrics for inline summary
            try
            {
                var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                _refreshMetrics = await adminApi.GetTaskMetricsAsync("refresh_metadata");
            }
            catch { _refreshMetrics = null; }
            RebuildTaskGroups();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }

        StartOrStopRefreshTimer();

        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventChannel.EventReceived += OnEventReceived;
            _eventSubscription = _eventChannel.Subscribe("tasks");
        }
        catch { }
    }

    private void ApplyResponsiveLayout()
    {
        var compact = ActualWidth < 720;
        var side = ActualWidth < 640 ? 16 : ActualWidth < 1024 ? 24 : 40;
        TasksPageShell.Padding = new Thickness(side, ActualWidth < 640 ? 16 : 24, side, 40);
        if (_compactLayout == compact) return;
        _compactLayout = compact;
        if (ViewModel.Tasks.Count > 0) RebuildTaskGroups();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _loaded = false;
        StopRefreshTimer();
        ViewModel.Tasks.CollectionChanged -= Tasks_CollectionChanged;
        if (_eventChannel is not null) _eventChannel.EventReceived -= OnEventReceived;
        _eventSubscription?.Dispose();
        _eventSubscription = null;
        base.OnNavigatedFrom(e);
    }

    private void Tasks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScheduleRebuild();

    private void OnEventReceived(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (channel != "tasks" || Volatile.Read(ref _taskActionFeedbackCount) > 0 || (DateTime.UtcNow - _lastEventRefresh).TotalMilliseconds < 750) return;
        _lastEventRefresh = DateTime.UtcNow;
        DispatcherQueue.TryEnqueue(async () => await ViewModel.LoadCommand.ExecuteAsync(null));
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildPending = false;
            RebuildTaskGroups();
        });
    }

    // ===== Auto-refresh timer =====

    private void StartOrStopRefreshTimer()
    {
        if (ViewModel.AnyTaskRunning)
        {
            if (_refreshTimer == null)
            {
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _refreshTimer.Tick += async (_, _) =>
                {
                    if (Volatile.Read(ref _taskActionFeedbackCount) > 0) return;
                    await ViewModel.LoadCommand.ExecuteAsync(null);
                    if (!ViewModel.AnyTaskRunning) StopRefreshTimer();
                };
                _refreshTimer.Start();
            }
        }
        else
        {
            StopRefreshTimer();
        }
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    // ===== Task Group Builder =====

    private void RebuildTaskGroups()
    {
        TaskGroupsPanel.Children.Clear();

        foreach (var (_, label, tasks) in ViewModel.GetGroupedTasks())
        {
            // Section header — text-xs font-medium tracking-[0.24em] uppercase text-muted-foreground
            var header = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                CharacterSpacing = 240
            };

            // Card container — surface-panel overflow-hidden rounded-[1.6rem] border-0
            var card = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };

            var rowsPanel = new StackPanel { Spacing = 0 };
            var taskList = tasks.ToList();

            for (int i = 0; i < taskList.Count; i++)
            {
                bool isLast = i == taskList.Count - 1;
                rowsPanel.Children.Add(BuildTaskRow(taskList[i], isLast));

                // Inline refresh_metadata metrics summary (webui: RefreshMetadataSummary)
                if (taskList[i].Key == "refresh_metadata" && _refreshMetrics != null)
                    rowsPanel.Children.Add(BuildRefreshMetricsSummary(_refreshMetrics));
            }

            card.Child = rowsPanel;

            var group = new StackPanel { Spacing = 12 };
            group.Children.Add(header);
            group.Children.Add(card);
            TaskGroupsPanel.Children.Add(group);
        }

        // Update timer after rebuild
        StartOrStopRefreshTimer();
    }

    // ===== Task Row =====

    private FrameworkElement BuildTaskRow(TaskInfo task, bool isLast)
    {
        bool isRunning = task.State == "running" || task.State == "cancelling";

        // Outer row container with bottom border (border-b last:border-b-0)
        var rowBorder = new Border
        {
            BorderBrush = isLast
                ? new SolidColorBrush(Colors.Transparent)
                : (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1),
        };

        // Web rows stack their trailing state/actions below the task copy on compact canvases.
        var rootGrid = new Grid { ColumnSpacing = 12, RowSpacing = 10, Padding = new Thickness(16) };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!_compactLayout)
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        else
        {
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        // ===== LEFT: name + metadata/progress =====
        var leftPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        // Task name — text-sm font-medium, hover:text-primary (simulated via HyperlinkButton style)
        var capturedTask = task;
        var nameBlock = new HyperlinkButton
        {
            Content = task.Name,
            Padding = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        };
        nameBlock.Click += (_, _) => Frame.Navigate(typeof(AdminTaskDetailPage), capturedTask.Key);
        leftPanel.Children.Add(nameBlock);

        // Metadata / progress (idle shows meta line; running shows progress bar)
        if (!isRunning)
        {
            // Meta line matches web AdminTasks:
            //   "<schedule>" | "No schedule"
            //   " · Last run: {rel}" (if any)
            //   " · Never run" (only if no schedule AND no last execution)
            //   " · Next: in X" (if next_run_at)
            var parts = new List<string>();

            string? scheduleDesc = DescribeSchedule(task.Triggers);
            if (!string.IsNullOrEmpty(scheduleDesc))
                parts.Add(scheduleDesc);
            else
                parts.Add("No schedule");

            if (task.LastExecution != null && !string.IsNullOrEmpty(task.LastExecution.CompletedAt))
            {
                parts.Add($"Last run: {FormatRelativeTime(task.LastExecution.CompletedAt)}");
                parts.Add($"Duration: {FormatDuration(task.LastExecution.DurationMs)}");
                var resultSummary = FormatTaskResultSummary(task);
                if (!string.IsNullOrWhiteSpace(resultSummary)) parts.Add($"Result: {resultSummary}");
            }
            else if (string.IsNullOrEmpty(scheduleDesc))
                parts.Add("Never run");

            var overdue = !string.IsNullOrEmpty(task.NextRunAt) && IsOverdue(task.NextRunAt);
            if (!string.IsNullOrEmpty(task.NextRunAt) && !overdue)
                parts.Add($"Next: {FormatNextRun(task.NextRunAt)}");

            string metaText = string.Join(" \u00B7 ", parts);

            leftPanel.Children.Add(new TextBlock
            {
                Text = metaText,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 2, 0, 0)
            });
            if (overdue)
            {
                var overduePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 3, 0, 0) };
                overduePanel.Children.Add(new FontIcon { Glyph = "\uE7BA", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["WarningBrush"] });
                overduePanel.Children.Add(new TextBlock { Text = "Overdue", FontSize = 12, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["WarningBrush"] });
                leftPanel.Children.Add(overduePanel);
            }
        }
        else
        {
            // Progress bar container — bg-muted h-2 rounded-full (h-2 = 8px).
            // CornerRadius = height/2 gives a clean capsule fill.
            var progressTrack = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                CornerRadius = new CornerRadius(4),
                Height = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };

            double pct = Math.Max(task.Progress, 2.0);
            var progressFill = new Border
            {
                Background = task.State == "cancelling"
                    ? new SolidColorBrush(Color.FromArgb(255, 234, 179, 8))
                    : (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(4),
            };

            // Use a Grid to simulate percentage width
            var progressGrid = new Grid();
            progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
            Grid.SetColumn(progressFill, 0);
            progressGrid.Children.Add(progressFill);
            progressTrack.Child = progressGrid;
            leftPanel.Children.Add(progressTrack);

            string progressText = task.State == "cancelling"
                ? "Cancelling..."
                : (task.ProgressMessage ?? $"{Math.Round(task.Progress)}%");

            leftPanel.Children.Add(new TextBlock
            {
                Text = progressText,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        Grid.SetColumn(leftPanel, 0);
        if (_compactLayout) Grid.SetColumnSpan(leftPanel, 2);
        rootGrid.Children.Add(leftPanel);

        // ===== CENTER: status badge (idle only, when last_execution exists) =====
        if (!isRunning && task.LastExecution != null)
        {
            var statusBadge = BuildStatusBadge(task.LastExecution.Status);
            Grid.SetColumn(statusBadge, _compactLayout ? 0 : 1);
            Grid.SetRow(statusBadge, _compactLayout ? 1 : 0);
            if (_compactLayout) statusBadge.HorizontalAlignment = HorizontalAlignment.Left;
            rootGrid.Children.Add(statusBadge);
        }

        // ===== RIGHT: action button =====
        var actionBtn = BuildActionButton(task);
        Grid.SetColumn(actionBtn, _compactLayout ? 1 : 2);
        Grid.SetRow(actionBtn, _compactLayout ? 1 : 0);
        rootGrid.Children.Add(actionBtn);

        rowBorder.Child = rootGrid;
        return rowBorder;
    }

    // ===== Badge Builders =====

    private static Border BuildStatusBadge(string status)
    {
        Color bg, fg;

        switch (status.ToLowerInvariant())
        {
            case "completed":
                bg = Color.FromArgb(40, 34, 197, 94);
                fg = Color.FromArgb(255, 34, 197, 94);
                break;
            case "failed":
                bg = Color.FromArgb(40, 220, 70, 70);
                fg = Color.FromArgb(255, 220, 90, 90);
                break;
            case "cancelled":
                bg = Color.FromArgb(40, 234, 179, 8);
                fg = Color.FromArgb(255, 234, 179, 8);
                break;
            default:
                bg = Color.FromArgb(40, 120, 120, 120);
                fg = Color.FromArgb(255, 160, 160, 160);
                break;
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 3, 7, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = status,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg)
        };
        return badge;
    }

    // ===== Action Button =====

    private Button BuildActionButton(TaskInfo task)
    {
        var capturedTask = task;
        bool isRunning = task.State == "running";
        bool isCancelling = task.State == "cancelling";

        var icon = new FontIcon
        {
            FontSize = 12,
            Glyph = isRunning || isCancelling ? "\uE71A" : "\uE768"  // Stop : Play
        };

        var labelBlock = new TextBlock
        {
            Text = isCancelling ? "Stopping..." : isRunning ? "Stop" : "Run Now",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(icon);
        content.Children.Add(labelBlock);

        var btn = new Button
        {
            Content = content,
            IsEnabled = !isCancelling,
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(12, 6, 12, 6),
            MinWidth = 132,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isRunning || isCancelling)
        {
            btn.Click += async (_, _) =>
            {
                var started = DateTime.UtcNow;
                Interlocked.Increment(ref _taskActionFeedbackCount);
                btn.IsEnabled = false;
                labelBlock.Text = "Stopping...";
                await ViewModel.CancelTaskCommand.ExecuteAsync(capturedTask.Key);
                var remaining = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - started);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
                Interlocked.Decrement(ref _taskActionFeedbackCount);
                await ViewModel.LoadCommand.ExecuteAsync(null);
                RebuildTaskGroups();
            };
        }
        else
        {
            btn.Click += async (_, _) =>
            {
                var started = DateTime.UtcNow;
                Interlocked.Increment(ref _taskActionFeedbackCount);
                btn.IsEnabled = false;
                labelBlock.Text = "Starting...";
                await ViewModel.RunTaskCommand.ExecuteAsync(capturedTask.Key);
                var remaining = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - started);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
                Interlocked.Decrement(ref _taskActionFeedbackCount);
                await ViewModel.LoadCommand.ExecuteAsync(null);
                RebuildTaskGroups();
            };
        }

        return btn;
    }

    // ===== Time Formatting =====
    // B29: Delegated to SiloPlayer.Core.Helpers.TimeAgo for consistency.
    private static string FormatRelativeTime(string dateStr)
        => Core.Helpers.TimeAgo.FormatShort(dateStr);

    private static string FormatNextRun(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return dateStr;
        var diff = dt.ToUniversalTime() - DateTimeOffset.UtcNow;
        if (diff.TotalSeconds < 0) return "overdue";
        int minutes = (int)diff.TotalMinutes;
        if (minutes < 60) return $"in {minutes}m";
        int hours = (int)diff.TotalHours;
        if (hours < 24) return $"in {hours}h";
        int days = (int)diff.TotalDays;
        return $"in {days}d";
    }

    private static bool IsOverdue(string dateStr)
        => DateTime.TryParse(dateStr, out var dt) && dt.ToUniversalTime() < DateTime.UtcNow;

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds < 1000) return $"{milliseconds}ms";
        var totalSeconds = milliseconds / 1000;
        if (totalSeconds < 60) return $"{totalSeconds}s";
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        if (minutes < 60) return $"{minutes}m {seconds}s";
        return $"{minutes / 60}h {minutes % 60}m";
    }

    private static string? FormatTaskResultSummary(TaskInfo task)
    {
        if (task.Key != "refresh_trending_discover" || task.LastExecution?.ResultData is not { } data) return null;
        static int? Number(IReadOnlyDictionary<string, object> values, string key)
        {
            if (!values.TryGetValue(key, out var raw) || raw == null) return null;
            if (raw is int value) return value;
            if (raw is long longValue) return (int)longValue;
            if (raw is System.Text.Json.JsonElement element && element.TryGetInt32(out var jsonValue)) return jsonValue;
            return int.TryParse(raw.ToString(), out var parsed) ? parsed : null;
        }
        var combos = Number(data, "combos"); var refreshed = Number(data, "refreshed");
        var empty = Number(data, "empty"); var failed = Number(data, "failed");
        if (combos == null || refreshed == null || empty == null || failed == null) return null;
        return combos == 0 ? "No enabled Trending Discover sections" : $"{refreshed} refreshed, {empty} empty, {failed} failed";
    }

    // Matches web AdminTasks describeTrigger / describeSchedule.
    private static string? DescribeSchedule(System.Collections.Generic.List<TriggerConfig>? triggers)
    {
        if (triggers == null || triggers.Count == 0) return null;
        var parts = new System.Collections.Generic.List<string>();
        foreach (var t in triggers) parts.Add(DescribeTrigger(t));
        return string.Join(", ", parts);
    }

    private FrameworkElement BuildRefreshMetricsSummary(MetadataRefreshMetrics metrics)
    {
        var panel = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(16, 0, 16, 12),
        };

        var content = new StackPanel { Spacing = 8 };

        var metricColumns = _compactLayout ? 2 : 5;
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        for (int i = 0; i < metricColumns; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < (int)Math.Ceiling(5d / metricColumns); i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddCell(int col, string label, string value)
        {
            var cell = new StackPanel { Spacing = 2 };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
            cell.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            Grid.SetColumn(cell, col % metricColumns);
            Grid.SetRow(cell, col / metricColumns);
            grid.Children.Add(cell);
        }

        string FormatDt(string? s) => string.IsNullOrEmpty(s) ? "\u2014" :
            (DateTimeOffset.TryParse(s, out var d) ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(d) : s);

        AddCell(0, "Refresh Backlog", metrics.Total.ToString("N0"));
        AddCell(1, "Due for Refresh", metrics.Due.ToString("N0"));
        AddCell(2, "Processing", metrics.Leased.ToString("N0"));
        AddCell(3, "Waiting Since", FormatDt(metrics.OldestDueAt));
        AddCell(4, "Next Claim Timeout", FormatDt(metrics.OldestLeaseExpiresAt));
        content.Children.Add(grid);

        // Reason badges
        var reasons = metrics.ReasonCounts.Where(r => r.Count > 0).ToList();
        if (reasons.Count > 0)
        {
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var r in reasons)
            {
                string label = r.Reason switch
                {
                    "episode_incomplete" => "Episode incomplete",
                    "stale_provider_id" => "Stale provider ID",
                    "refresh_failure" => "Refresh failure",
                    "core_metadata_incomplete" => "Core metadata incomplete",
                    _ => r.Reason,
                };
                badges.Children.Add(new Border
                {
                    Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3),
                    Child = new TextBlock { Text = $"{label}: {r.Count}", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] }
                });
            }
            content.Children.Add(badges);
        }

        panel.Child = content;
        return panel;
    }

    private static string DescribeTrigger(TriggerConfig t)
    {
        switch (t.Type)
        {
            case "interval":
            {
                long ms = t.IntervalMs ?? 0;
                if (ms >= 86_400_000) return $"Every {Math.Round(ms / 86_400_000.0)}d";
                if (ms >= 3_600_000) return $"Every {Math.Round(ms / 3_600_000.0)}h";
                if (ms >= 60_000)    return $"Every {Math.Round(ms / 60_000.0)}m";
                return $"Every {Math.Round(ms / 1000.0)}s";
            }
            case "daily":
                return $"Daily at {t.TimeOfDay ?? "00:00"}";
            case "weekly":
            {
                var days = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
                int d = t.DayOfWeek ?? 0;
                if (d < 0 || d > 6) d = 0;
                return $"{days[d]} at {t.TimeOfDay ?? "00:00"}";
            }
            case "startup":
                return "On startup";
            default:
                return t.Type;
        }
    }
}
