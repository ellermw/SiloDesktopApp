using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.Controls;

/// <summary>
/// Reusable activity indicator + popover matching the webui ServerActivity
/// component. Polls admin sessions + running tasks on a timer and shows a
/// badge with the total count. Clicking opens a popover with Streams/Tasks/Scans
/// sections.
///
/// The button only renders when the current user has admin privileges —
/// non-admin calls would 403 anyway and we don't want to show a useless icon.
/// Hidden entirely when there's no activity (HideWhenEmpty=true).
/// </summary>
public sealed partial class ServerActivityButton : UserControl
{
    private readonly AdminApi _adminApi;
    private DispatcherTimer? _pollTimer;

    // Cached snapshot for popover rebuilds
    private List<AdminSession> _lastSessions = [];
    private List<TaskInfo> _lastRunningTasks = [];

    /// <summary>
    /// Kept for backwards compatibility with the admin shell XAML attribute
    /// <c>HideWhenEmpty="False"</c>. The property is now purely cosmetic —
    /// visibility is owned by the host shell (MainWindow / AdminShellPage),
    /// which gates the control in lock-step with the sidebar Admin button.
    /// </summary>
    public bool HideWhenEmpty { get; set; } = false;

    /// <summary>
    /// Route click handlers. Must be provided by the host (MainWindow or
    /// AdminShellPage) since nav targets differ between the two shells.
    /// </summary>
    public Action? OnViewStreams { get; set; }
    public Action? OnViewTasks { get; set; }
    public Action? OnViewScans { get; set; }

    public ServerActivityButton()
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        this.InitializeComponent();

        // Visibility is owned by the HOST, not this control. MainWindow flips
        // it in ShowMainNavigation / HideMainNavigation (same gate as the
        // sidebar Admin button), and AdminShellPage sets it once from XAML.
        // This control only fetches/renders the badge content for sessions
        // and tasks — never decides for itself whether to be shown.

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Kick off an immediate poll so the badge count is fresh the moment
        // the host makes us visible. Don't wait for the first timer tick.
        _ = PollAsync();

        if (_pollTimer == null)
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _pollTimer.Tick += async (_, _) => await PollAsync();
        }
        _pollTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pollTimer?.Stop();
    }

    private async Task PollAsync()
    {
        try
        {
            var sessionsTask = _adminApi.GetSessionsAsync();
            var tasksTask = _adminApi.GetTasksAsync();
            await Task.WhenAll(sessionsTask, tasksTask);

            _lastSessions = sessionsTask.Result ?? [];
            var allTasks = tasksTask.Result ?? [];
            _lastRunningTasks = allTasks.Where(t => t.State == "running").ToList();

            DispatcherQueue.TryEnqueue(UpdateBadgeState);
        }
        catch
        {
            // Non-admin / offline — zero out counts. The host decides whether
            // we're visible; if we got a 403 here we're wrongly visible and
            // the host missed our admin-gate, which is a bug we'd rather not
            // paper over silently.
            _lastSessions = [];
            _lastRunningTasks = [];
            DispatcherQueue.TryEnqueue(UpdateBadgeState);
        }
    }

    private void UpdateBadgeState()
    {
        // Visibility is owned by the host (MainWindow / AdminShellPage) and
        // tied to the same admin-gate as the sidebar Admin button. This method
        // only updates the count badge and icon tint — it never touches the
        // outer control's Visibility.
        var total = _lastSessions.Count + _lastRunningTasks.Count;

        if (total > 0)
        {
            CountBadge.Visibility = Visibility.Visible;
            CountBadgeText.Text = total.ToString();
            ActivityIcon.Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            CountBadge.Visibility = Visibility.Collapsed;
            ActivityIcon.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        }
    }

    private void RootButton_Click(object sender, RoutedEventArgs e)
    {
        // Rebuild popover contents from the latest cache every open so we don't
        // flash stale counts from the previous view. Rebuild is synchronous but
        // only renders 3 small sections — cheap enough to run inline.
        RebuildPopover();

        // Fire-and-forget: refresh the cache in the background so the NEXT open
        // is up to date. Does NOT block this click — the current flyout renders
        // from the existing cache.
        _ = PollAsync();
    }

    // ─── Popover layout ──────────────────────────────────────────────────

    private void RebuildPopover()
    {
        PopoverRoot.Children.Clear();

        // Header
        var headerRow = new Grid { Padding = new Thickness(16, 14, 16, 12) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.Children.Add(new TextBlock
        {
            Text = "Server Activity",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        PopoverRoot.Children.Add(headerRow);
        PopoverRoot.Children.Add(Divider());

        int total = _lastSessions.Count + _lastRunningTasks.Count;
        if (total == 0)
        {
            PopoverRoot.Children.Add(new TextBlock
            {
                Text = "No active server activity",
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(16, 32, 16, 32),
            });
            return;
        }

        PopoverRoot.Children.Add(BuildSection("STREAMS", _lastSessions.Count,
            () => { ActivityFlyout.Hide(); OnViewStreams?.Invoke(); },
            BuildStreamsContent()));
        PopoverRoot.Children.Add(Divider());

        PopoverRoot.Children.Add(BuildSection("TASKS", _lastRunningTasks.Count,
            () => { ActivityFlyout.Hide(); OnViewTasks?.Invoke(); },
            BuildTasksContent()));
        PopoverRoot.Children.Add(Divider());

        PopoverRoot.Children.Add(BuildSection("SCANS", 0,
            () => { ActivityFlyout.Hide(); OnViewScans?.Invoke(); },
            new TextBlock
            {
                Text = "Scan updates arrive via the events channel (not yet wired here).",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2),
            }));
    }

    private FrameworkElement BuildSection(string title, int count, Action onViewAll, FrameworkElement body)
    {
        var wrapper = new StackPanel { Padding = new Thickness(16, 12, 16, 12), Spacing = 8 };

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 120,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (count > 0)
        {
            titleStack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x60, 0xA5, 0xFA)),
                CornerRadius = new CornerRadius(5),
                Height = 16,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        Grid.SetColumn(titleStack, 0);
        titleRow.Children.Add(titleStack);

        var viewAllBtn = new HyperlinkButton
        {
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var viewAllContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        viewAllContent.Children.Add(new TextBlock
        {
            Text = "View all",
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        });
        viewAllContent.Children.Add(new FontIcon { Glyph = "\uE76C", FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
        viewAllBtn.Content = viewAllContent;
        viewAllBtn.Click += (_, _) => onViewAll();
        Grid.SetColumn(viewAllBtn, 1);
        titleRow.Children.Add(viewAllBtn);

        wrapper.Children.Add(titleRow);
        wrapper.Children.Add(body);
        return wrapper;
    }

    private FrameworkElement BuildStreamsContent()
    {
        if (_lastSessions.Count == 0)
            return new TextBlock
            {
                Text = "No active streams",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            };

        var counts = new Dictionary<string, int>();
        foreach (var s in _lastSessions)
        {
            var method = string.IsNullOrEmpty(s.PlayMethod) ? "unknown" : s.PlayMethod;
            counts[method] = counts.GetValueOrDefault(method) + 1;
        }

        var stack = new StackPanel { Spacing = 6 };
        foreach (var (method, count) in counts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(MethodDotColor(method)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = $"{count} {MethodLabel(method)}",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(row);
        }
        return stack;
    }

    private FrameworkElement BuildTasksContent()
    {
        if (_lastRunningTasks.Count == 0)
            return new TextBlock
            {
                Text = "No running tasks",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            };

        var stack = new StackPanel { Spacing = 8 };
        foreach (var task in _lastRunningTasks)
        {
            var taskStack = new StackPanel { Spacing = 3 };

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.Children.Add(new TextBlock
            {
                Text = task.Name ?? task.Key ?? "Task",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            });
            var pctText = new TextBlock
            {
                Text = $"{Math.Round(task.Progress)}%",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(pctText, 1);
            headerRow.Children.Add(pctText);
            taskStack.Children.Add(headerRow);

            var trackGrid = new Grid { Height = 6 };
            var track = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                CornerRadius = new CornerRadius(3),
            };
            trackGrid.Children.Add(track);

            double pct = Math.Clamp(task.Progress, 0.0, 100.0);
            var fillGrid = new Grid();
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
            var fill = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(3),
            };
            Grid.SetColumn(fill, 0);
            fillGrid.Children.Add(fill);
            trackGrid.Children.Add(fillGrid);
            taskStack.Children.Add(trackGrid);

            if (!string.IsNullOrEmpty(task.ProgressMessage))
            {
                taskStack.Children.Add(new TextBlock
                {
                    Text = task.ProgressMessage,
                    FontSize = 10,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }

            stack.Children.Add(taskStack);
        }
        return stack;
    }

    private Border Divider() => new()
    {
        Height = 1,
        Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
        Opacity = 0.4,
    };

    private static string MethodLabel(string method) => method switch
    {
        "direct"    => "Direct Play",
        "remux"     => "Remux",
        "transcode" => "Transcode",
        _ => method,
    };

    private static Color MethodDotColor(string method) => method switch
    {
        "direct"    => Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E),
        "remux"     => Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA),
        "transcode" => Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24),
        _ => Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF),
    };
}
