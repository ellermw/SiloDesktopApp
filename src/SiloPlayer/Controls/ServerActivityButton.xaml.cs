using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Controls;

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
    private static readonly TimeSpan ConnectionProblemIndicatorDelay = TimeSpan.FromSeconds(4);
    private const int MaxActivityScanRows = 25;
    private readonly AdminApi _adminApi;
    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _connectionProblemTimer;
    private readonly EventChannelClient _events;
    private IDisposable? _subscription;
    private bool _wsConnected;
    private bool _showConnectionProblem;
    private WebSocketState _wsState = WebSocketState.None;
    private bool _hostVisibilityAllowed = true;
    private bool _isLoaded;
    private DateTime _lastEventPollAt = DateTime.MinValue;
    private bool _badgePulseRunning;

    // Cached snapshot for popover rebuilds
    private List<AdminSession> _lastSessions = [];
    private List<TaskInfo> _lastRunningTasks = [];
    private List<AdminScanRun> _lastActiveScans = [];
    private Dictionary<int, string> _libraryNames = [];

    /// <summary>
    /// Matches the WebUI's hide-when-empty option. Effective visibility
    /// when <c>HideWhenEmpty</c> is enabled.
    /// combines badge state with the host navigation/authorization gate, so
    /// realtime updates cannot resurrect a hidden trigger.
    /// </summary>
    public bool HideWhenEmpty { get; set; } = false;

    public void SetHostVisibility(bool allowed)
    {
        if (_hostVisibilityAllowed == allowed)
        {
            UpdateBadgeState();
            return;
        }

        _hostVisibilityAllowed = allowed;
        if (_isLoaded)
        {
            if (allowed)
                StartMonitoring();
            else
                StopMonitoring();
        }
        UpdateBadgeState();
    }

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
        _events = App.Services.GetRequiredService<EventChannelClient>();
        this.InitializeComponent();

        // The host supplies navigation/authorization visibility. Badge state
        // supplies the optional empty-state visibility.

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (!_hostVisibilityAllowed)
        {
            UpdateBadgeState();
            return;
        }

        StartMonitoring();
    }

    private void StartMonitoring()
    {
        // Kick off an immediate poll so the badge count is fresh the moment
        // the host makes us visible. Don't wait for the first timer tick.
        _ = PollAsync();

        // Fallback poll — keeps data fresh if the WebSocket is down or never
        // connected (non-admin fallback, network glitch). Events drive most
        // of the freshness once the channel is live.
        if (_pollTimer == null)
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _pollTimer.Tick += async (_, _) => await PollAsync();
        }
        _pollTimer.Start();

        SubscribeToEvents();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        StopMonitoring();
    }

    private void StopMonitoring()
    {
        _pollTimer?.Stop();
        StopConnectionProblemTimer();
        _showConnectionProblem = false;
        UnsubscribeFromEvents();
    }

    // ─── Event channel ───────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        if (_subscription != null) return;
        try
        {
            // The shared client may already be live before this control loads.
            // Seed from its current state instead of showing a false warning
            // until the next reconnect transition.
            ApplyConnectionState(_events.CurrentState);
            _subscription = _events.Subscribe("sessions", "tasks", "scans");
            _events.SnapshotReceived += OnSnapshot;
            _events.EventReceived += OnEvent;
            _events.StateChanged += OnWsStateChanged;
            if (_events.TryGetLatestSnapshot("scans", out var cachedScans))
                OnSnapshot("scans", cachedScans);
        }
        catch
        {
            // Non-admin / transport issue — polling still carries the button.
            UnsubscribeFromEvents();
        }
    }

    private void UnsubscribeFromEvents()
    {
        try
        {
            _events.SnapshotReceived -= OnSnapshot;
            _events.EventReceived -= OnEvent;
            _events.StateChanged -= OnWsStateChanged;
            _subscription?.Dispose();
            _subscription = null;
        }
        catch { }
    }

    private void OnWsStateChanged(WebSocketState state)
    {
        DispatcherQueue.TryEnqueue(() => ApplyConnectionState(state));
    }

    private void ApplyConnectionState(WebSocketState state)
    {
        _wsState = state;
        _wsConnected = state == WebSocketState.Open;
        if (_wsConnected)
        {
            StopConnectionProblemTimer();
            _showConnectionProblem = false;
            UpdateBadgeState();
            return;
        }

        if (!_showConnectionProblem && _connectionProblemTimer == null)
        {
            _connectionProblemTimer = new DispatcherTimer
            {
                // ServerActivity.tsx deliberately suppresses transient
                // reconnect noise for four seconds.
                Interval = ConnectionProblemIndicatorDelay,
            };
            _connectionProblemTimer.Tick += ConnectionProblemTimer_Tick;
            _connectionProblemTimer.Start();
        }

        UpdateBadgeState();
    }

    private void ConnectionProblemTimer_Tick(object? sender, object e)
    {
        StopConnectionProblemTimer();
        if (_wsConnected)
            return;

        _showConnectionProblem = true;
        UpdateBadgeState();
    }

    private void StopConnectionProblemTimer()
    {
        if (_connectionProblemTimer == null)
            return;

        _connectionProblemTimer.Stop();
        _connectionProblemTimer.Tick -= ConnectionProblemTimer_Tick;
        _connectionProblemTimer = null;
    }

    private void OnSnapshot(string channel, System.Text.Json.JsonElement data)
    {
        if (channel == "scans" && data.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var scans = new List<AdminScanRun>();
            foreach (var el in data.EnumerateArray())
            {
                try
                {
                    var run = el.Deserialize<AdminScanRun>(JsonOpts);
                    if (run != null) scans.Add(run);
                }
                catch { }
            }
            _lastActiveScans = scans.Where(IsActiveScanStatus).ToList();
            DispatcherQueue.TryEnqueue(UpdateBadgeState);
            return;
        }
        // sessions/tasks snapshots — the REST endpoints give richer data, so we
        // refresh via PollAsync instead of parsing the snapshot payload.
        DispatcherQueue.TryEnqueue(() => _ = PollAsync());
    }

    private void OnEvent(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        switch (channel)
        {
            case "scans":
                try
                {
                    var run = data.Deserialize<AdminScanRun>(JsonOpts);
                    if (run != null)
                    {
                        _lastActiveScans = _lastActiveScans.Where(s => s.Id != run.Id).ToList();
                        if (IsActiveScanStatus(run)) _lastActiveScans.Add(run);
                        DispatcherQueue.TryEnqueue(UpdateBadgeState);
                    }
                }
                catch { }
                break;
            case "sessions":
            case "tasks":
                // Coalesce rapid event bursts: only trigger a REST refresh once per second.
                var now = DateTime.UtcNow;
                if ((now - _lastEventPollAt).TotalMilliseconds < 1000) return;
                _lastEventPollAt = now;
                DispatcherQueue.TryEnqueue(() => _ = PollAsync());
                break;
        }
    }

    private static bool IsActiveScanStatus(AdminScanRun run) =>
        // Match the current WebUI exactly. A queued scan is pending work, not
        // active server activity; counting the entire queue can turn a real
        // "2 active" badge into "99+" as soon as the realtime snapshot lands.
        run.Status is "accepted" or "running";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    private async Task PollAsync()
    {
        try
        {
            // Run calls in parallel; isolate failures so one doesn't block the others
            var sessionsTask = _adminApi.GetSessionsAsync();
            var tasksTask = _adminApi.GetTasksAsync();
            var librariesTask = _libraryNames.Count == 0
                ? _adminApi.GetAdminLibrariesAsync()
                : null;

            try { _lastSessions = await sessionsTask ?? []; } catch { }
            try
            {
                var allTasks = await tasksTask ?? [];
                _lastRunningTasks = allTasks.Where(t => t.State == "running").ToList();
            }
            catch { }
            if (librariesTask != null)
            {
                try
                {
                    var libs = await librariesTask ?? [];
                    _libraryNames = libs.ToDictionary(l => l.Id, l => l.Name);
                }
                catch { }
            }

            DispatcherQueue.TryEnqueue(UpdateBadgeState);
        }
        catch
        {
            // Keep stale data on transient errors (matches webui React Query
            // behavior which shows stale counts while retrying). Only clear
            // on the very first load when there's nothing to show yet.
            if (_lastSessions.Count == 0 && _lastRunningTasks.Count == 0)
                DispatcherQueue.TryEnqueue(UpdateBadgeState);
        }
    }

    private void UpdateBadgeState()
    {
        // Recompute from both host and badge state on every realtime update.
        var total = _lastSessions.Count + _lastRunningTasks.Count + _lastActiveScans.Count;
        Visibility = _hostVisibilityAllowed && (!HideWhenEmpty || total > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (total > 0)
        {
            CountBadge.Visibility = Visibility.Visible;
            // Match ServerActivity.tsx: keep the badge compact so large scan
            // queues do not cover the activity glyph or collide with the
            // window edge.
            CountBadgeText.Text = total > 99 ? "99+" : total.ToString();
            ActivityIcon.Stroke = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            AutomationProperties.SetName(
                RootButton,
                $"Server activity: {total} active");
            if (!_badgePulseRunning)
            {
                CountBadgePulse.Begin();
                _badgePulseRunning = true;
            }
        }
        else
        {
            if (_badgePulseRunning)
            {
                CountBadgePulse.Stop();
                CountBadge.Opacity = 1;
                _badgePulseRunning = false;
            }
            CountBadge.Visibility = Visibility.Collapsed;
            ActivityIcon.Stroke = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
            AutomationProperties.SetName(RootButton, "Server activity");
        }

        // Disconnected indicator — small warning dot when WS is down but we
        // still have activity to report. Mirrors upstream ServerActivity.tsx:100.
        if (DisconnectedDot != null)
            DisconnectedDot.Visibility =
                _showConnectionProblem ? Visibility.Visible : Visibility.Collapsed;
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
        if (!_wsConnected)
        {
            var status = new TextBlock
            {
                Text = _wsState is WebSocketState.Connecting or WebSocketState.None
                    ? "Connecting\u2026"
                    : "Disconnected",
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(status, 1);
            headerRow.Children.Add(status);
        }
        PopoverRoot.Children.Add(headerRow);
        PopoverRoot.Children.Add(Divider());

        int total = _lastSessions.Count + _lastRunningTasks.Count + _lastActiveScans.Count;
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

        PopoverRoot.Children.Add(BuildSection("SCANS", _lastActiveScans.Count,
            () => { ActivityFlyout.Hide(); OnViewScans?.Invoke(); },
            BuildScansContent()));
    }

    private FrameworkElement BuildScansContent()
    {
        if (_lastActiveScans.Count == 0)
            return new TextBlock
            {
                Text = "No active scans",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            };

        var stack = new StackPanel { Spacing = 6 };
        foreach (var scan in _lastActiveScans.Take(MaxActivityScanRows))
        {
            var row = new StackPanel { Spacing = 1 };
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.Children.Add(new TextBlock
            {
                Text = ResolveLibraryName(scan.LibraryId),
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            });
            var statusText = new TextBlock
            {
                Text = scan.Status == "running" ? "Scanning…" : "Queued",
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(statusText, 1);
            headerRow.Children.Add(statusText);
            row.Children.Add(headerRow);

            var modeLabel = scan.Mode switch
            {
                "library" => "Full library",
                "subtree" => "Subtree",
                "file" => "Single file",
                _ => scan.Mode,
            };
            var subLine = string.IsNullOrEmpty(scan.Path) ? modeLabel : $"{modeLabel} · {scan.Path}";
            row.Children.Add(new TextBlock
            {
                Text = subLine,
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var progressLabel = FormatScanProgress(scan);
            if (!string.IsNullOrEmpty(progressLabel))
            {
                row.Children.Add(new TextBlock
                {
                    Text = progressLabel,
                    FontSize = 10,
                    Opacity = 0.8,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }

            stack.Children.Add(row);
        }

        var hiddenCount = _lastActiveScans.Count - MaxActivityScanRows;
        if (hiddenCount > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"+{hiddenCount} more scans queued",
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(24, 4, 0, 0),
            });
        }
        return stack;
    }

    private string ResolveLibraryName(int id) =>
        _libraryNames.TryGetValue(id, out var name) && !string.IsNullOrEmpty(name)
            ? name
            : $"Library #{id}";

    private static string? FormatScanProgress(AdminScanRun scan)
    {
        var r = scan.Result;
        if (r == null) return null;
        if (r.TotalFiles > 0 && r.FilesProcessed > 0)
        {
            var pct = Math.Clamp((int)Math.Round(r.FilesProcessed * 100.0 / r.TotalFiles), 0, 100);
            var msg = string.IsNullOrEmpty(r.Message) ? "Processing files" : r.Message;
            return $"{msg} · {r.FilesProcessed:N0} / {r.TotalFiles:N0} ({pct}%)";
        }
        return r.Message;
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
