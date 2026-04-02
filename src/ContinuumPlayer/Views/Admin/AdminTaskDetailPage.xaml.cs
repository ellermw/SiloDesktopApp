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
        EditScheduleButton.Click += async (_, _) => await OpenEditScheduleDialogAsync();
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

        // Task name — page-title clamp(2rem,4vw,3rem) = large bold
        TitleBadgeRow.Children.Add(new TextBlock
        {
            Text       = task.Name,
            FontSize   = 32,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        // Category badge — variant="outline"
        TitleBadgeRow.Children.Add(MakeOutlineBadge(task.Category));

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

        // Web: Math.max(task.progress, 2)% where task.progress is 0-100
        // Our model: Progress is 0.0-1.0, so multiply by 100
        double pct = Math.Max(task.Progress * 100, 2);
        TaskProgressBar.Value = pct;

        // Cancelling → yellow-500, else accent (primary)
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

        for (int i = 0; i < task.Triggers.Count; i++)
        {
            if (i > 0)
            {
                // border-b between rows
                TriggersPanel.Children.Add(new Border
                {
                    BorderBrush     = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            TriggersPanel.Children.Add(BuildTriggerRow(task.Triggers[i]));
        }
    }

    private FrameworkElement BuildTriggerRow(TriggerConfig trigger)
    {
        // Web: border-b px-4 py-2.5 text-sm last:border-b-0
        // px-4 = 16px, py-2.5 = 10px
        string description = DescribeTrigger(trigger);

        // If max runtime is set, append it
        if (trigger.MaxRuntimeMs.HasValue && trigger.MaxRuntimeMs.Value > 0)
        {
            long minutes = trigger.MaxRuntimeMs.Value / 60_000;
            description += $"  (max {minutes}m)";
        }

        return new TextBlock
        {
            Text              = description,
            FontSize          = 13,
            Foreground        = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            Padding           = new Thickness(16, 10, 16, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static string DescribeTrigger(TriggerConfig trigger)
    {
        // Matches web describeTrigger() exactly
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
                // Web: "${days[t.day_of_week ?? 0]} at ${t.time_of_day ?? "00:00"}"
                // No "Weekly on" prefix
                string[] days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
                string day = (trigger.DayOfWeek.HasValue && trigger.DayOfWeek.Value >= 0 && trigger.DayOfWeek.Value < 7)
                    ? days[trigger.DayOfWeek.Value]
                    : "Sunday";
                return $"{day} at {trigger.TimeOfDay ?? "00:00"}";
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

        for (int i = 0; i < ViewModel.History.Count; i++)
        {
            if (i > 0)
            {
                HistoryRowsPanel.Children.Add(new Border
                {
                    BorderBrush     = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            HistoryRowsPanel.Children.Add(BuildHistoryRow(ViewModel.History[i]));
        }
    }

    private FrameworkElement BuildHistoryRow(ExecutionResult result)
    {
        // Web: px-4 py-2 per cell (16px horiz, 8px vert)
        var row = new Grid
        {
            Padding       = new Thickness(16, 8, 16, 8),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });

        // Failed rows get bg-destructive/5
        if (result.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            row.Background = new SolidColorBrush(Color.FromArgb(13, 220, 70, 70)); // ~5% opacity red
        }

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

        // Status badge — matches web Badge variants:
        // failed → destructive, cancelled → outline, else → secondary
        var statusBadge = BuildStatusBadge(result.Status);

        // Error — text-muted-foreground max-w-xs truncate
        var errorBlock = new TextBlock
        {
            Text              = result.ErrorMessage ?? "\u2014",  // em dash
            FontSize          = 13,
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
        // Web Badge variants:
        //   failed → destructive (red bg/text)
        //   cancelled → outline (border only, muted text)
        //   else (completed) → secondary (muted bg, normal text)
        switch (status.ToLowerInvariant())
        {
            case "failed":
                return MakeBadge(status,
                    Color.FromArgb(40, 220, 70, 70),
                    Color.FromArgb(255, 220, 90, 90));
            case "cancelled":
                // outline variant: transparent bg, just a border
                return MakeOutlineBadge(status);
            default:
                // secondary: muted bg
                return MakeBadge(status,
                    Color.FromArgb(40, 120, 120, 120),
                    Color.FromArgb(255, 160, 160, 160));
        }
    }

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background          = new SolidColorBrush(bg),
            CornerRadius        = new CornerRadius(6),
            Padding             = new Thickness(8, 2, 8, 2),
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text       = text,
                FontSize   = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(fg)
            }
        };
    }

    private static Border MakeOutlineBadge(string text)
    {
        return new Border
        {
            Background          = new SolidColorBrush(Colors.Transparent),
            BorderBrush         = new SolidColorBrush(Color.FromArgb(100, 160, 160, 160)),
            BorderThickness     = new Thickness(1),
            CornerRadius        = new CornerRadius(6),
            Padding             = new Thickness(8, 2, 8, 2),
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text       = text,
                FontSize   = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160))
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

    // ===== Edit Schedule Dialog =====

    private async Task OpenEditScheduleDialogAsync()
    {
        var task = ViewModel.TaskDetail;
        if (task == null) return;

        // Build form with current triggers + add button
        var triggersPanel = new StackPanel { Spacing = 8, Width = 420 };

        var triggerList = new List<TriggerConfig>(task.Triggers);

        void RebuildTriggerEditor()
        {
            triggersPanel.Children.Clear();

            for (int i = 0; i < triggerList.Count; i++)
            {
                var idx = i;
                var t = triggerList[i];

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8)
                };

                var cardGrid = new Grid { ColumnSpacing = 8 };
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var desc = new TextBlock
                {
                    Text = DescribeTrigger(t),
                    FontSize = 13,
                    Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(desc, 0);
                cardGrid.Children.Add(desc);

                var removeBtn = new Button
                {
                    Width = 24, Height = 24, Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(4),
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 90, 90)) },
                    VerticalAlignment = VerticalAlignment.Center
                };
                removeBtn.Click += (_, _) => { triggerList.RemoveAt(idx); RebuildTriggerEditor(); };
                Grid.SetColumn(removeBtn, 1);
                cardGrid.Children.Add(removeBtn);

                card.Child = cardGrid;
                triggersPanel.Children.Add(card);
            }

            // Add trigger section
            var typeCombo = new ComboBox { FontSize = 13, CornerRadius = new CornerRadius(8), Width = 140 };
            typeCombo.Items.Add(new ComboBoxItem { Content = "Interval", Tag = "interval" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "Daily", Tag = "daily" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "Weekly", Tag = "weekly" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "On Startup", Tag = "startup" });
            typeCombo.SelectedIndex = 0;

            var valueBox = new TextBox { PlaceholderText = "e.g. 3600000 (ms)", FontSize = 13, CornerRadius = new CornerRadius(8), Width = 140 };
            var timeBox = new TextBox { PlaceholderText = "HH:MM (e.g. 03:00)", FontSize = 13, CornerRadius = new CornerRadius(8), Width = 140 };

            var addBtn = new Button
            {
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13
            };
            addBtn.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE710", FontSize = 10 },
                    new TextBlock { Text = "Add" }
                }
            };
            addBtn.Click += (_, _) =>
            {
                var selectedType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "interval";
                var newTrigger = new TriggerConfig { Type = selectedType };

                if (selectedType == "interval" && long.TryParse(valueBox.Text, out var ms))
                    newTrigger.IntervalMs = ms;
                else if (selectedType == "daily")
                    newTrigger.TimeOfDay = string.IsNullOrWhiteSpace(timeBox.Text) ? "00:00" : timeBox.Text.Trim();
                else if (selectedType == "weekly")
                    newTrigger.TimeOfDay = string.IsNullOrWhiteSpace(timeBox.Text) ? "00:00" : timeBox.Text.Trim();

                triggerList.Add(newTrigger);
                RebuildTriggerEditor();
            };

            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            addRow.Children.Add(typeCombo);
            addRow.Children.Add(valueBox);
            addRow.Children.Add(timeBox);
            addRow.Children.Add(addBtn);
            triggersPanel.Children.Add(addRow);
        }

        RebuildTriggerEditor();

        var dialog = new ContentDialog
        {
            Title = "Edit Schedule",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = triggersPanel,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await ViewModel.UpdateTriggersAsync(_taskKey, triggerList);
            await LoadAsync();
        }
        catch { }
    }

    // ===== Navigation =====

    private void GoBack()
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
