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

    public AdminTasksPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminTasksViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Tasks.CollectionChanged += (_, _) => RebuildTaskGroups();

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
            // Section header
            var header = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                CharacterSpacing = 100
            };

            // Card container
            var card = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
                CornerRadius = new CornerRadius(24),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0)
            };

            var rowsPanel = new StackPanel { Spacing = 0 };
            bool isFirst = true;

            foreach (var task in tasks)
            {
                if (!isFirst)
                {
                    rowsPanel.Children.Add(new Border
                    {
                        BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Margin = new Thickness(16, 0, 16, 0)
                    });
                }
                isFirst = false;
                rowsPanel.Children.Add(BuildTaskRow(task));
            }

            card.Child = rowsPanel;

            var group = new StackPanel { Spacing = 10 };
            group.Children.Add(header);
            group.Children.Add(card);
            TaskGroupsPanel.Children.Add(group);
        }

        // Update timer after rebuild
        StartOrStopRefreshTimer();
    }

    // ===== Task Row =====

    private FrameworkElement BuildTaskRow(TaskInfo task)
    {
        bool isRunning = task.State == "running" || task.State == "cancelling";

        // Outer button for row click (navigate to detail)
        var rowButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(20, 16, 20, 16),
            CornerRadius = new CornerRadius(0)
        };

        var capturedTask = task;
        rowButton.Click += (_, _) =>
            Frame.Navigate(typeof(PlaceholderPage), $"Task: {capturedTask.Key}");

        // Root layout: left info | center progress (if running) | right badges + action
        var rootGrid = new Grid { ColumnSpacing = 12 };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ===== LEFT: name, description, metadata =====
        var leftPanel = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };

        var nameBlock = new TextBlock
        {
            Text = task.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        leftPanel.Children.Add(nameBlock);

        if (!string.IsNullOrEmpty(task.Description))
        {
            leftPanel.Children.Add(new TextBlock
            {
                Text = task.Description,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        // Metadata line (idle only)
        if (!isRunning)
        {
            var metaParts = new List<string>();
            if (task.LastExecution != null && !string.IsNullOrEmpty(task.LastExecution.CompletedAt))
                metaParts.Add($"Last run: {FormatRelativeTime(task.LastExecution.CompletedAt)}");
            else
                metaParts.Add("Never run");

            if (!string.IsNullOrEmpty(task.NextRunAt))
                metaParts.Add($"Next: {FormatNextRun(task.NextRunAt)}");

            leftPanel.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", metaParts),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }
        else
        {
            // Progress bar
            double progressValue = Math.Max(task.Progress * 100, 2);
            var progressBar = new ProgressBar
            {
                Value = progressValue,
                Minimum = 0,
                Maximum = 100,
                Height = 4,
                Foreground = task.State == "cancelling"
                    ? new SolidColorBrush(Color.FromArgb(255, 234, 179, 8))   // amber
                    : (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                Margin = new Thickness(0, 4, 0, 0)
            };
            leftPanel.Children.Add(progressBar);

            string progressText = task.State == "cancelling"
                ? "Cancelling..."
                : (task.ProgressMessage ?? $"{Math.Round(task.Progress * 100)}%");

            leftPanel.Children.Add(new TextBlock
            {
                Text = progressText,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        Grid.SetColumn(leftPanel, 0);
        rootGrid.Children.Add(leftPanel);

        // ===== RIGHT: badges + action button =====
        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        // State badge
        rightPanel.Children.Add(BuildStateBadge(task.State));

        // Last execution status badge (idle only, when execution exists)
        if (!isRunning && task.LastExecution != null)
        {
            rightPanel.Children.Add(BuildStatusBadge(task.LastExecution.Status));
        }

        // Action button
        rightPanel.Children.Add(BuildActionButton(task));

        Grid.SetColumn(rightPanel, 1);
        rootGrid.Children.Add(rightPanel);

        rowButton.Content = rootGrid;
        return rowButton;
    }

    // ===== Badge Builders =====

    private static Border BuildStateBadge(string state)
    {
        Color bg, fg;
        string label;

        switch (state)
        {
            case "running":
                bg = Color.FromArgb(40, 120, 174, 252);
                fg = Color.FromArgb(255, 120, 174, 252);
                label = "Running";
                break;
            case "cancelling":
                bg = Color.FromArgb(40, 234, 179, 8);
                fg = Color.FromArgb(255, 234, 179, 8);
                label = "Cancelling";
                break;
            default: // idle
                bg = Color.FromArgb(40, 120, 120, 120);
                fg = Color.FromArgb(255, 160, 160, 160);
                label = "Idle";
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
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg)
        };
        return badge;
    }

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
            FontSize = 13,
            Glyph = isRunning ? "\uE71A" : "\uE768"  // Stop : Play
        };

        string label = isCancelling ? "Cancelling..." : isRunning ? "Cancel" : "Run";

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, FontSize = 13 });

        var btn = new Button
        {
            Content = content,
            IsEnabled = !isCancelling,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
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
