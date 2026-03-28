using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminTaskDetailPage : Page
{
    public AdminTaskDetailViewModel ViewModel { get; }

    private string _taskKey = "";
    private DispatcherTimer? _refreshTimer;

    public AdminTaskDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminTaskDetailViewModel>();
        this.InitializeComponent();

        BackButton.Click   += (_, _) => GoBack();
        RetryButton.Click  += async (_, _) => await LoadAsync();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Accept either a raw key string, or "Task: <key>" from AdminTasksPage
        _taskKey = e.Parameter switch
        {
            string s when s.StartsWith("Task: ", StringComparison.OrdinalIgnoreCase)
                => s["Task: ".Length..].Trim(),
            string s => s,
            _ => ""
        };

        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopRefreshTimer();
    }

    // ===== Load =====

    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(_taskKey)) return;

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(_taskKey);
            RebuildPage();
        }
        catch { }
    }

    // ===== Auto-refresh timer (while task is running) =====

    private void StartOrStopRefreshTimer()
    {
        if (ViewModel.IsRunning)
        {
            if (_refreshTimer == null)
            {
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _refreshTimer.Tick += async (_, _) =>
                {
                    await ViewModel.LoadCommand.ExecuteAsync(_taskKey);
                    RebuildPage();
                    if (!ViewModel.IsRunning) StopRefreshTimer();
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

    // ===== Page rebuild =====

    private void RebuildPage()
    {
        var task = ViewModel.TaskDetail;
        if (task == null) return;

        BuildHeader(task);
        BuildProgressSection(task);
        BuildTriggersSection(task);
        BuildHistoryTable();
        StartOrStopRefreshTimer();
    }

    // ===== Header =====

    private void BuildHeader(TaskInfo task)
    {
        TitleBadgeRow.Children.Clear();

        TitleBadgeRow.Children.Add(new TextBlock
        {
            Text      = task.Name,
            FontSize  = 24,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        // Category badge
        TitleBadgeRow.Children.Add(MakeBadge(
            task.Category,
            Color.FromArgb(40, 120, 120, 120),
            Color.FromArgb(255, 160, 160, 160)));

        DescriptionText.Text = task.Description;

        // Action button
        bool isRunning    = task.State == "running";
        bool isCancelling = task.State == "cancelling";
        bool isActive     = isRunning || isCancelling;

        var actionIcon = new FontIcon
        {
            FontSize = 13,
            Glyph    = isActive ? "\uE71A" : "\uE768"  // Stop : Play
        };

        string actionLabel = isCancelling ? "Cancelling..." : isActive ? "Cancel" : "Run Now";

        var actionContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actionContent.Children.Add(actionIcon);
        actionContent.Children.Add(new TextBlock { Text = actionLabel, FontSize = 13 });

        ActionButton.Content   = actionContent;
        ActionButton.IsEnabled = !isCancelling;
        ActionButton.Click    -= OnActionButtonClick;
        ActionButton.Click    += OnActionButtonClick;
    }

    private async void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        ActionButton.IsEnabled = false;
        if (ViewModel.IsRunning)
            await ViewModel.CancelCommand.ExecuteAsync(null);
        else
            await ViewModel.RunCommand.ExecuteAsync(null);

        RebuildPage();
    }

    // ===== Progress section =====

    private void BuildProgressSection(TaskInfo task)
    {
        bool isActive = task.State == "running" || task.State == "cancelling";
        ProgressSection.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

        if (!isActive) return;

        double pct = Math.Max(task.Progress * 100, 2);
        TaskProgressBar.Value = pct;

        TaskProgressBar.Foreground = task.State == "cancelling"
            ? new SolidColorBrush(Color.FromArgb(255, 234, 179, 8))
            : (SolidColorBrush)Application.Current.Resources["AccentBrush"];

        ProgressMessageText.Text = task.State == "cancelling"
            ? "Cancelling..."
            : (task.ProgressMessage ?? $"{Math.Round(pct)}%");
    }

    // ===== Triggers section =====

    private void BuildTriggersSection(TaskInfo task)
    {
        TriggersPanel.Children.Clear();

        if (task.Triggers.Count == 0)
        {
            NoTriggersMessage.Visibility = Visibility.Visible;
            return;
        }

        NoTriggersMessage.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var trigger in task.Triggers)
        {
            if (!isFirst)
            {
                TriggersPanel.Children.Add(new Border
                {
                    BorderBrush     = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;

            TriggersPanel.Children.Add(BuildTriggerRow(trigger));
        }
    }

    private FrameworkElement BuildTriggerRow(TriggerConfig trigger)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 10,
            Padding     = new Thickness(20, 12, 20, 12),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Type icon
        string glyph = trigger.Type switch
        {
            "interval" => "\uE916",  // Clock
            "daily"    => "\uE787",  // Calendar day
            "weekly"   => "\uE787",  // Calendar
            "startup"  => "\uE7E8",  // Power
            _          => "\uE916"
        };

        row.Children.Add(new FontIcon
        {
            Glyph      = glyph,
            FontSize   = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        string description = DescribeTrigger(trigger);

        // If max runtime is set, append it
        string maxRuntime = "";
        if (trigger.MaxRuntimeMs.HasValue && trigger.MaxRuntimeMs.Value > 0)
        {
            long minutes = trigger.MaxRuntimeMs.Value / 60_000;
            maxRuntime = $"  (max {minutes}m)";
        }

        row.Children.Add(new TextBlock
        {
            Text              = description + maxRuntime,
            FontSize          = 13,
            Foreground        = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        return row;
    }

    private static string DescribeTrigger(TriggerConfig trigger)
    {
        switch (trigger.Type)
        {
            case "interval":
            {
                long ms = trigger.IntervalMs ?? 0;
                if (ms >= 3_600_000) return $"Every {Math.Round((double)ms / 3_600_000)} hour(s)";
                if (ms >= 60_000)    return $"Every {Math.Round((double)ms / 60_000)} minute(s)";
                return $"Every {Math.Round((double)ms / 1000)} second(s)";
            }
            case "daily":
                return $"Daily at {trigger.TimeOfDay ?? "00:00"}";
            case "weekly":
            {
                string[] days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
                string day = (trigger.DayOfWeek.HasValue && trigger.DayOfWeek.Value >= 0 && trigger.DayOfWeek.Value < 7)
                    ? days[trigger.DayOfWeek.Value]
                    : "Sunday";
                return $"Weekly on {day} at {trigger.TimeOfDay ?? "00:00"}";
            }
            case "startup":
                return "On server startup";
            default:
                return trigger.Type;
        }
    }

    // ===== History table =====

    private void BuildHistoryTable()
    {
        HistoryRowsPanel.Children.Clear();

        if (ViewModel.History.Count == 0)
        {
            NoHistoryMessage.Visibility = Visibility.Visible;
            return;
        }

        NoHistoryMessage.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var result in ViewModel.History)
        {
            if (!isFirst)
            {
                HistoryRowsPanel.Children.Add(new Border
                {
                    BorderBrush     = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            HistoryRowsPanel.Children.Add(BuildHistoryRow(result));
        }
    }

    private FrameworkElement BuildHistoryRow(ExecutionResult result)
    {
        var row = new Grid
        {
            Padding       = new Thickness(20, 10, 20, 10),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });

        // Started
        var startedBlock = new TextBlock
        {
            Text              = FormatDateTime(result.StartedAt),
            FontSize          = 13,
            Foreground        = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // Duration
        var durationBlock = new TextBlock
        {
            Text              = FormatDuration(result.DurationMs),
            FontSize          = 13,
            Foreground        = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // Status badge
        var statusBadge = BuildStatusBadge(result.Status);

        // Error
        var errorBlock = new TextBlock
        {
            Text              = result.ErrorMessage ?? "\u2014",  // em dash
            FontSize          = 12,
            Foreground        = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            MaxLines          = 1
        };

        Grid.SetColumn(startedBlock,  0);
        Grid.SetColumn(durationBlock, 1);
        Grid.SetColumn(statusBadge,   2);
        Grid.SetColumn(errorBlock,    3);

        row.Children.Add(startedBlock);
        row.Children.Add(durationBlock);
        row.Children.Add(statusBadge);
        row.Children.Add(errorBlock);

        return row;
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

        return MakeBadge(status, bg, fg);
    }

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background        = new SolidColorBrush(bg),
            CornerRadius      = new CornerRadius(4),
            Padding           = new Thickness(7, 3, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text       = text,
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg)
            }
        };
    }

    // ===== Formatting Helpers =====

    private static string FormatDateTime(string dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return "\u2014";
        if (DateTime.TryParse(dateStr, out var dt))
            return dt.ToLocalTime().ToString("g");
        return dateStr;
    }

    private static string FormatDuration(long ms)
    {
        if (ms < 1000) return $"{ms}ms";
        double seconds = ms / 1000.0;
        if (seconds < 60) return $"{seconds:F1}s";
        int minutes    = (int)(seconds / 60);
        int remainSec  = (int)Math.Round(seconds % 60);
        return $"{minutes}m {remainSec}s";
    }

    // ===== Navigation =====

    private void GoBack()
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
