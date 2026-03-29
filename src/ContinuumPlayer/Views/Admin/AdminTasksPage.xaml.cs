using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminTasksPage : Page
{
    public AdminTasksViewModel ViewModel { get; }

    private DispatcherTimer? _refreshTimer;
    private bool _rebuildPending;

    public AdminTasksPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminTasksViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Tasks.CollectionChanged += (_, _) => ScheduleRebuild();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }

        StartOrStopRefreshTimer();
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
                CornerRadius = new CornerRadius(24),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };

            var rowsPanel = new StackPanel { Spacing = 0 };
            var taskList = tasks.ToList();

            for (int i = 0; i < taskList.Count; i++)
            {
                bool isLast = i == taskList.Count - 1;
                rowsPanel.Children.Add(BuildTaskRow(taskList[i], isLast));
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

        // Clickable button fills the row
        var rowButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 16, 16, 16),
            CornerRadius = new CornerRadius(0)
        };

        var capturedTask = task;
        rowButton.Click += (_, _) =>
            Frame.Navigate(typeof(AdminTaskDetailPage), capturedTask.Key);

        // Root layout: left info (flex-1) | center badge | right action button
        var rootGrid = new Grid { ColumnSpacing = 12 };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ===== LEFT: name + metadata/progress =====
        var leftPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        // Task name — text-sm font-medium, hover:text-primary (simulated via HyperlinkButton style)
        var nameBlock = new TextBlock
        {
            Text = task.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        leftPanel.Children.Add(nameBlock);

        // Metadata / progress (idle shows meta line; running shows progress bar)
        if (!isRunning)
        {
            // Meta line: "Last run: Xm ago · Next: in Xm" or "Never run"
            string lastRunPart = task.LastExecution != null && !string.IsNullOrEmpty(task.LastExecution.CompletedAt)
                ? $"Last run: {FormatRelativeTime(task.LastExecution.CompletedAt)}"
                : "Never run";

            string metaText = !string.IsNullOrEmpty(task.NextRunAt)
                ? $"{lastRunPart} · Next: {FormatNextRun(task.NextRunAt)}"
                : lastRunPart;

            leftPanel.Children.Add(new TextBlock
            {
                Text = metaText,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        else
        {
            // Progress bar container — bg-muted h-2 rounded-full (h-2 = 8px)
            var progressTrack = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                CornerRadius = new CornerRadius(999),
                Height = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };

            double pct = Math.Max(task.Progress, 2.0);
            var progressFill = new Border
            {
                Background = task.State == "cancelling"
                    ? new SolidColorBrush(Color.FromArgb(255, 234, 179, 8))
                    : (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(999),
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
        rootGrid.Children.Add(leftPanel);

        // ===== CENTER: status badge (idle only, when last_execution exists) =====
        if (!isRunning && task.LastExecution != null)
        {
            var statusBadge = BuildStatusBadge(task.LastExecution.Status);
            Grid.SetColumn(statusBadge, 1);
            rootGrid.Children.Add(statusBadge);
        }

        // ===== RIGHT: action button =====
        var actionBtn = BuildActionButton(task);
        Grid.SetColumn(actionBtn, 2);
        rootGrid.Children.Add(actionBtn);

        rowButton.Content = rootGrid;
        rowBorder.Child = rowButton;
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
            Glyph = isRunning ? "\uE71A" : "\uE768"  // Stop : Play
        };

        string label = isCancelling ? "Cancelling..." : isRunning ? "Cancel" : "Run Now";

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button
        {
            Content = content,
            IsEnabled = !isCancelling,
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isRunning || isCancelling)
        {
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                await ViewModel.CancelTaskCommand.ExecuteAsync(capturedTask.Key);
                RebuildTaskGroups();
            };
        }
        else
        {
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                await ViewModel.RunTaskCommand.ExecuteAsync(capturedTask.Key);
                RebuildTaskGroups();
            };
        }

        return btn;
    }

    // ===== Time Formatting =====

    private static string FormatRelativeTime(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return dateStr;
        var diff = DateTimeOffset.UtcNow - dt.ToUniversalTime();
        int seconds = (int)diff.TotalSeconds;
        if (seconds < 60) return "just now";
        int minutes = (int)diff.TotalMinutes;
        if (minutes < 60) return $"{minutes}m ago";
        int hours = (int)diff.TotalHours;
        if (hours < 24) return $"{hours}h ago";
        int days = (int)diff.TotalDays;
        return $"{days}d ago";
    }

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
}
