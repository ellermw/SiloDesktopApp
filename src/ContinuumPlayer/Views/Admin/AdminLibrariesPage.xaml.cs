using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminLibrariesPage : Page
{
    public AdminLibrariesViewModel ViewModel { get; }

    // Colors matching the web UI
    private static readonly Color EnabledColor = Color.FromArgb(255, 63, 185, 80);
    private static readonly Color DestructiveColor = Color.FromArgb(255, 220, 90, 90);
    private static readonly Color HealthyColor = Color.FromArgb(255, 52, 211, 153);     // emerald-400
    private static readonly Color HealthyBgColor = Color.FromArgb(12, 52, 211, 153);    // emerald-500/5
    private static readonly Color HealthyBorderColor = Color.FromArgb(64, 52, 211, 153);// emerald-500/25
    private static readonly Color UnhealthyBgColor = Color.FromArgb(12, 220, 90, 90);
    private static readonly Color UnhealthyBorderColor = Color.FromArgb(64, 220, 90, 90);
    private static readonly Color AmberColor = Color.FromArgb(255, 245, 158, 11);       // amber-500

    // Pagination
    private const int UNMATCHED_PAGE_SIZE = 10;
    private const int STALE_PAGE_SIZE = 10;
    private const int ScanUiRefreshMs = 300;
    private const int ScanLibraryRowsRefreshMs = 1500;
    private const int ScanLibraryReloadMs = 10000;
    private const int MaxScanRowsInPopover = 25;
    private int _staleCurrentPage;
    private string _unmatchedFilter = "";
    private string _staleFilter = "";
    private string _ambiguousFilter = "";

    public AdminLibrariesPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminLibrariesViewModel>();
        this.InitializeComponent();
        _scanUiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScanUiRefreshMs) };
        _scanUiRefreshTimer.Tick += ScanUiRefreshTimer_Tick;
        ScanQueueFlyout.Opened += (_, _) =>
        {
            _scanQueueFlyoutOpen = true;
            BuildScanQueuePopover(forceContent: true);
        };
        ScanQueueFlyout.Closed += (_, _) => _scanQueueFlyoutOpen = false;
    }

    // Event channel subscription for realtime refresh
    private IDisposable? _eventSubscription;
    private EventChannelClient? _eventChannel;
    private DateTime _lastEventRefresh = DateTime.MinValue;
    private DateTime _lastScanLibraryRowsRefresh = DateTime.MinValue;
    private DispatcherTimer? _scanUiRefreshTimer;
    private bool _scanQueueFlyoutOpen;

    // Cached brush lookups to avoid repeated resource dictionary access
    private SolidColorBrush _primaryText = null!;
    private SolidColorBrush _secondaryText = null!;
    private SolidColorBrush _tertiaryText = null!;
    private SolidColorBrush _borderBrush = null!;
    private SolidColorBrush _surfaceBrush = null!;
    private SolidColorBrush _accentBrush = null!;
    private SolidColorBrush _cardBg = null!;
    private bool _loaded;

    private void CacheBrushes()
    {
        if (_primaryText != null) return;
        _primaryText = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        _secondaryText = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        _tertiaryText = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"];
        _borderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        _surfaceBrush = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
        _accentBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        _cardBg = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"];
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return; // Prevent double-subscription on re-navigation
        _loaded = true;
        CacheBrushes();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            // Rebuild ONCE after all data is loaded — no CollectionChanged subscriptions needed for initial load
            RebuildAll();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }

        // Subscribe to realtime scan events for live refresh
        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventChannel.EventReceived += OnEventReceived;
            _eventSubscription = _eventChannel.Subscribe("scans");
        }
        catch { }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scanUiRefreshTimer?.Stop();
        if (_eventChannel != null)
            _eventChannel.EventReceived -= OnEventReceived;
        _eventSubscription?.Dispose();
        _eventSubscription = null;
    }

    private static readonly JsonSerializerOptions _scanJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private void OnEventReceived(string channel, string eventName, JsonElement data)
    {
        if (channel != "scans") return;

        List<AdminScanRun>? snapshot = null;
        AdminScanRun? updatedRun = null;

        try
        {
            if (eventName == "snapshot")
            {
                snapshot = data.Deserialize<List<AdminScanRun>>(_scanJsonOpts) ?? [];
            }
            else if (eventName == "scan_updated")
            {
                updatedRun = data.Deserialize<AdminScanRun>(_scanJsonOpts);
            }
        }
        catch { return; }

        if (snapshot == null && updatedRun == null)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (snapshot != null)
            {
                ViewModel.ActiveScans = snapshot.Where(IsActiveScan).ToList();
            }
            else if (updatedRun != null)
            {
                ViewModel.ActiveScans = ViewModel.ActiveScans.Where(s => s.Id != updatedRun.Id).ToList();
                if (IsActiveScan(updatedRun)) ViewModel.ActiveScans.Add(updatedRun);
            }

            ScheduleScanUiRefresh();
        });
    }

    private void ScheduleScanUiRefresh()
    {
        _scanUiRefreshTimer?.Stop();
        _scanUiRefreshTimer?.Start();
    }

    private void ScanUiRefreshTimer_Tick(object? sender, object e)
    {
        _scanUiRefreshTimer?.Stop();

        BuildScanQueuePopover();

        var now = DateTime.UtcNow;
        if ((now - _lastScanLibraryRowsRefresh).TotalMilliseconds >= ScanLibraryRowsRefreshMs)
        {
            _lastScanLibraryRowsRefresh = now;
            BuildLibraryRows();
        }

        if ((now - _lastEventRefresh).TotalMilliseconds >= ScanLibraryReloadMs)
        {
            _lastEventRefresh = now;
            _ = RefreshLibrariesAfterScanEventAsync();
        }
    }

    private async Task RefreshLibrariesAfterScanEventAsync()
    {
        try
        {
            await ViewModel.RefreshLibrariesOnlyAsync();
            DispatcherQueue.TryEnqueue(BuildLibraryRows);
        }
        catch { }
    }

    private static bool IsActiveScan(AdminScanRun scan)
        => scan.Status is "accepted" or "running" or "queued";

    private void RebuildAll()
    {
        BuildLibraryRows();
        BuildSkippedRootsRows();
        BuildScanQueuePopover();
        BuildUnmatchedItemsSection();
        BuildStaleIdsSection();
        BuildAmbiguousRootsSection();
    }

    // ===================================================================
    //  Table Builder — 6 columns: Name | Paths | Type | Status | Last Scanned | Actions
    // ===================================================================

    private void BuildLibraryRows()
    {
        LibrariesPanel.Children.Clear();

        if (ViewModel.Libraries.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        foreach (var lib in ViewModel.Libraries)
        {
            LibrariesPanel.Children.Add(BuildLibraryRow(lib));

            if (lib.ScanWarningCode == "empty_root")
                LibrariesPanel.Children.Add(BuildEmptyRootWarningRow(lib));
        }
    }

    private FrameworkElement BuildLibraryRow(Library lib)
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 12, 16, 12),
            ColumnSpacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

        // ---- Col 0: drag handle — now functional with move up/down ----
        var dragPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var capturedForReorder = lib;
        var moveUpBtn = new Button
        {
            Width = 18, Height = 18, MinWidth = 18, MinHeight = 18,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE70E", FontSize = 9, Foreground = _tertiaryText },
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(moveUpBtn, "Move up");
        moveUpBtn.Click += async (_, _) => await MoveLibraryAsync(capturedForReorder, -1);

        var moveDownBtn = new Button
        {
            Width = 18, Height = 18, MinWidth = 18, MinHeight = 18,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 9, Foreground = _tertiaryText },
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(moveDownBtn, "Move down");
        moveDownBtn.Click += async (_, _) => await MoveLibraryAsync(capturedForReorder, 1);

        var dragStack = new StackPanel { Spacing = 0 };
        dragStack.Children.Add(moveUpBtn);
        dragStack.Children.Add(moveDownBtn);

        Grid.SetColumn(dragStack, 0);
        row.Children.Add(dragStack);

        // ---- Col 1: Name ----
        var nameBlock = new TextBlock
        {
            Text = lib.Name,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = _primaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameBlock, 1);
        row.Children.Add(nameBlock);

        // ---- Col 2: Paths ----
        FrameworkElement pathsElement;
        if (lib.Paths.Count == 1)
        {
            pathsElement = new TextBlock
            {
                Text = lib.Paths[0],
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = _secondaryText,
                Opacity = 0.85,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else if (lib.Paths.Count > 1)
        {
            var capturedForPaths = lib;
            var pathsBtn = new HyperlinkButton
            {
                Content = new TextBlock
                {
                    Text = $"{lib.Paths.Count} folders",
                    FontSize = 12,
                    Foreground = _secondaryText,
                },
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            pathsBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedForPaths);
            pathsElement = pathsBtn;
        }
        else
        {
            pathsElement = new TextBlock
            {
                Text = "\u2014",
                FontSize = 12,
                Foreground = _tertiaryText,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        Grid.SetColumn(pathsElement, 2);
        row.Children.Add(pathsElement);

        // ---- Col 3: Type badge ----
        string typeText = lib.Type switch
        {
            "movies" => "movies",
            "series" => "series",
            "mixed" => "mixed",
            _ => lib.Type,
        };
        var typeCell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        typeCell.Children.Add(MakeBadge(typeText, "secondary"));
        Grid.SetColumn(typeCell, 3);
        row.Children.Add(typeCell);

        // ---- Col 4: Status column ----
        var statusColumn = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        statusColumn.Children.Add(MakeBadge(
            lib.Enabled ? "Enabled" : "Disabled",
            lib.Enabled ? "outline" : "destructive"));

        // Show running/queued scan badges (from active scans)
        var libScans = ViewModel.ActiveScans.Where(s => s.LibraryId == lib.Id).ToList();
        var runningCount = libScans.Count(s => s.Status == "running");
        var queuedCount = libScans.Count - runningCount;
        if (runningCount > 0)
            statusColumn.Children.Add(MakeBadge($"{runningCount} running", "secondary"));
        if (queuedCount > 0)
            statusColumn.Children.Add(MakeBadge($"{queuedCount} queued", "secondary"));

        if (lib.ScanWarningCode == "empty_root")
            statusColumn.Children.Add(MakeBadge("Empty root guarded", "destructive"));
        Grid.SetColumn(statusColumn, 4);
        row.Children.Add(statusColumn);

        // ---- Col 5: Last Scanned ----
        var lastScannedColumn = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        string lastScannedText = "Never";
        if (!string.IsNullOrEmpty(lib.LastScannedAt) &&
            DateTimeOffset.TryParse(lib.LastScannedAt, out var lastScanned))
        {
            lastScannedText = lastScanned.LocalDateTime.ToString("g");
        }
        lastScannedColumn.Children.Add(new TextBlock
        {
            Text = lastScannedText,
            FontSize = 12,
            Foreground = _tertiaryText,
            Opacity = 0.85,
        });
        if (!string.IsNullOrEmpty(lib.ScanWarningAt) &&
            DateTimeOffset.TryParse(lib.ScanWarningAt, out var warningAt))
        {
            lastScannedColumn.Children.Add(new TextBlock
            {
                Text = $"Warning: {warningAt.LocalDateTime:g}",
                FontSize = 11,
                Foreground = new SolidColorBrush(DestructiveColor),
            });
        }
        Grid.SetColumn(lastScannedColumn, 5);
        row.Children.Add(lastScannedColumn);

        // ---- Col 6: Actions ----
        var actionsWrapper = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        var capturedLib = lib;

        // Check mount
        var mountBtn = MakeIconButton28("\uEDA2", "Check mount");
        mountBtn.Click += async (_, _) =>
        {
            await ViewModel.CheckMountCommand.ExecuteAsync(capturedLib.Id);
            DispatcherQueue.TryEnqueue(RebuildAll);
        };
        actionsPanel.Children.Add(mountBtn);

        // Scan Library
        var scanBtn = MakeIconButton28("\uE72C", "Scan Library");
        scanBtn.Click += async (_, _) =>
        {
            await ViewModel.ScanLibraryCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? "Scan started.");
        };
        actionsPanel.Children.Add(scanBtn);

        // Refresh metadata
        var refreshBtn = MakeIconButton28("\uE895", "Refresh metadata");
        refreshBtn.Click += async (_, _) =>
        {
            await ViewModel.RefreshMetadataCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? "Refresh started.");
        };
        actionsPanel.Children.Add(refreshBtn);

        // Empty-root confirm
        if (lib.ScanWarningCode == "empty_root")
        {
            var confirmCleanupBtn = MakeIconButton28("\uE74D", "Confirm empty root cleanup", DestructiveColor);
            confirmCleanupBtn.Click += async (_, _) => await OpenConfirmEmptyRootDialogAsync(capturedLib);
            actionsPanel.Children.Add(confirmCleanupBtn);
        }

        // Cancel Scans (when active scans exist for this library)
        if (libScans.Count > 0)
        {
            var cancelBtn = MakeIconButton28("\uE71A", "Cancel queued and running scans", DestructiveColor);
            cancelBtn.Click += async (_, _) =>
            {
                await ViewModel.CancelLibraryScansCommand.ExecuteAsync(capturedLib.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Scan cancellation requested.");
            };
            actionsPanel.Children.Add(cancelBtn);
        }

        // Edit
        var editBtn = MakeIconButton28("\uE70F", "Edit library");
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedLib);
        actionsPanel.Children.Add(editBtn);

        // Delete
        var deleteBtn = MakeIconButton28("\uE74D", "Delete library");
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedLib);
        actionsPanel.Children.Add(deleteBtn);

        actionsWrapper.Children.Add(actionsPanel);

        // Inline mount-check result
        if (ViewModel.MountCheckResults.TryGetValue(lib.Id, out var mountCheck))
            actionsWrapper.Children.Add(BuildMountCheckInlineResult(mountCheck, isWarningRow: false));

        // Inline active refresh job progress (webui lines 533-538)
        var activeRefreshJob = ViewModel.ActiveRefreshJobs.FirstOrDefault(j =>
        {
            if (j.RequestPayload == null) return false;
            if (j.RequestPayload.TryGetValue("library_id", out var libIdObj))
            {
                if (libIdObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
                    return je.GetInt32() == lib.Id;
                if (libIdObj is int intId) return intId == lib.Id;
            }
            return false;
        });
        if (activeRefreshJob != null)
        {
            var refreshPanel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 6, 0, 0) };
            refreshPanel.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrEmpty(activeRefreshJob.Message) ? activeRefreshJob.Message : "Metadata refresh queued",
                FontSize = 10,
                Foreground = _tertiaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (activeRefreshJob.ProgressTotal > 0)
            {
                refreshPanel.Children.Add(new TextBlock
                {
                    Text = $"Progress: {activeRefreshJob.ProgressCurrent:N0} / {activeRefreshJob.ProgressTotal:N0}",
                    FontSize = 10,
                    Foreground = _tertiaryText,
                });
            }
            actionsWrapper.Children.Add(refreshPanel);
        }

        // Inline active scans list (up to 2 + "+N more")
        if (libScans.Count > 0)
        {
            var scanInfoPanel = new StackPanel { Spacing = 1, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var scan in libScans.Take(2))
            {
                var label = FormatActiveScanMode(scan);
                label += scan.Status == "running" ? " running" : " queued";
                if (!string.IsNullOrEmpty(scan.Path)) label += $" \u00b7 {scan.Path}";
                scanInfoPanel.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    Foreground = _tertiaryText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            if (libScans.Count > 2)
            {
                scanInfoPanel.Children.Add(new TextBlock
                {
                    Text = $"+{libScans.Count - 2} more scan(s)",
                    FontSize = 10,
                    Foreground = _tertiaryText,
                });
            }
            actionsWrapper.Children.Add(scanInfoPanel);
        }

        Grid.SetColumn(actionsWrapper, 6);
        row.Children.Add(actionsWrapper);

        var rowBorder = new Border
        {
            Child = row,
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        var hoverBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
        var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        rowBorder.PointerEntered += (s, _) => { if (s is Border b) b.Background = hoverBrush; };
        rowBorder.PointerExited  += (s, _) => { if (s is Border b) b.Background = transparentBrush; };
        return rowBorder;
    }

    // ===================================================================
    //  Library Reorder (Move Up/Down approach — WinUI 3 lacks built-in
    //  drag-reorder for arbitrary Grid children. Move buttons provide the
    //  same functionality as the webui's DnD reorder.)
    // ===================================================================

    private async Task MoveLibraryAsync(Library lib, int direction)
    {
        var list = ViewModel.Libraries.ToList();
        var idx = list.FindIndex(l => l.Id == lib.Id);
        if (idx < 0) return;
        var newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= list.Count) return;

        // Swap
        (list[idx], list[newIdx]) = (list[newIdx], list[idx]);

        // Update UI immediately
        ViewModel.Libraries.Clear();
        foreach (var l in list) ViewModel.Libraries.Add(l);
        BuildLibraryRows();

        // Send reorder to server
        var entries = list.Select((l, i) => new Dictionary<string, object> { ["id"] = l.Id, ["position"] = i }).ToList();
        await ViewModel.ReorderLibrariesCommand.ExecuteAsync(
            new Dictionary<string, object> { ["entries"] = entries });
    }

    // ===================================================================
    //  Empty Root Warning Row (colSpan=6, bg-destructive/5)
    // ===================================================================

    private FrameworkElement BuildEmptyRootWarningRow(Library lib)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(12, 220, 90, 90)),
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10, 16, 14),
        };

        var content = new StackPanel { Spacing = 8 };

        content.Children.Add(new TextBlock
        {
            Text = "Scan found 0 media files for this library. Cleanup was paused to avoid accidental deletion.",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(DestructiveColor),
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = lib.ScanWarningMessage
                   ?? "Run another scan after storage returns, or confirm deletion before the next empty-root scan.",
            FontSize = 13,
            Foreground = _secondaryText,
            TextWrapping = TextWrapping.Wrap
        });

        var capturedLib = lib;
        var checkMountBtn = new Button
        {
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            Padding = new Thickness(10, 6, 10, 6)
        };
        var checkMountContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        checkMountContent.Children.Add(new FontIcon { Glyph = "\uEDA2", FontSize = 13 });
        checkMountContent.Children.Add(new TextBlock { Text = "Check Mount", FontSize = 12 });
        checkMountBtn.Content = checkMountContent;
        checkMountBtn.Click += async (_, _) =>
        {
            await ViewModel.CheckMountCommand.ExecuteAsync(capturedLib.Id);
            BuildLibraryRows();
        };

        content.Children.Add(checkMountBtn);

        if (ViewModel.MountCheckResults.TryGetValue(lib.Id, out var mountCheck))
        {
            content.Children.Add(BuildMountCheckInlineResult(mountCheck, isWarningRow: true));
        }

        border.Child = content;
        return border;
    }

    // ===================================================================
    //  Mount Check Inline Result
    // ===================================================================

    private FrameworkElement BuildMountCheckInlineResult(LibraryMountCheckResponse result, bool isWarningRow)
    {
        bool healthy = result.Healthy;
        var bgColor = healthy ? HealthyBgColor : UnhealthyBgColor;
        var borderColor = healthy ? HealthyBorderColor : UnhealthyBorderColor;
        var textColor = healthy ? HealthyColor : DestructiveColor;

        var container = new Border
        {
            Background = new SolidColorBrush(bgColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var stack = new StackPanel { Spacing = 4 };

        stack.Children.Add(new TextBlock
        {
            Text = result.Summary,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(textColor),
            TextWrapping = TextWrapping.Wrap
        });

        if (healthy && isWarningRow)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Storage looks available again. Run Scan Library to verify contents and clear the warning.",
                FontSize = 12,
                Foreground = _secondaryText,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (!healthy)
        {
            foreach (var root in result.Roots.Where(r => !r.Reachable))
            {
                var rootLine = new TextBlock
                {
                    FontSize = 12,
                    Foreground = _secondaryText,
                    TextWrapping = TextWrapping.Wrap
                };
                rootLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = root.Path,
                    FontFamily = new FontFamily("Consolas")
                });
                if (!string.IsNullOrEmpty(root.ErrorMessage))
                {
                    rootLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text = $": {root.ErrorMessage}"
                    });
                }
                stack.Children.Add(rootLine);
            }
        }

        string checkedText = "Checked ";
        if (DateTimeOffset.TryParse(result.CheckedAt, out var checkedAt))
            checkedText += checkedAt.LocalDateTime.ToString("g");
        else
            checkedText += result.CheckedAt;

        stack.Children.Add(new TextBlock
        {
            Text = checkedText,
            FontSize = 11,
            Foreground = _tertiaryText
        });

        container.Child = stack;
        return container;
    }

    // ===================================================================
    //  Scan Queue Popover (P0 Item 1)
    // ===================================================================

    private void BuildScanQueuePopover(bool forceContent = false)
    {
        var scans = ViewModel.ActiveScans;
        if (scans.Count == 0)
        {
            ScanQueueButton.Visibility = Visibility.Collapsed;
            ScanQueueFlyoutContent.Children.Clear();
            return;
        }

        ScanQueueButton.Visibility = Visibility.Visible;
        var totalRunning = scans.Count(s => s.Status == "running");
        var totalQueued = scans.Count - totalRunning;
        var totalScans = totalRunning + totalQueued;
        ScanQueueLabel.Text = totalScans == 1 ? "1 scan" : $"{totalScans} scans";

        if (!_scanQueueFlyoutOpen && !forceContent)
            return;

        // Build the flyout content
        ScanQueueFlyoutContent.Children.Clear();

        // Header
        var header = new Grid { Padding = new Thickness(16, 12, 16, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        headerLeft.Children.Add(new FontIcon { Glyph = "\uE72C", FontSize = 12, Foreground = _accentBrush });
        headerLeft.Children.Add(new TextBlock { Text = "Scan Queue", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _primaryText });
        Grid.SetColumn(headerLeft, 0);
        header.Children.Add(headerLeft);

        var headerRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (totalRunning > 0)
        {
            headerRight.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(HealthyColor) });
            headerRight.Children.Add(new TextBlock { Text = $"{totalRunning} running", FontSize = 10, Foreground = _tertiaryText });
        }
        if (totalRunning > 0 && totalQueued > 0)
            headerRight.Children.Add(new TextBlock { Text = "\u00b7", FontSize = 10, Foreground = _tertiaryText });
        if (totalQueued > 0)
        {
            headerRight.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = _tertiaryText, Opacity = 0.4 });
            headerRight.Children.Add(new TextBlock { Text = $"{totalQueued} queued", FontSize = 10, Foreground = _tertiaryText });
        }
        Grid.SetColumn(headerRight, 1);
        header.Children.Add(headerRight);

        ScanQueueFlyoutContent.Children.Add(header);
        ScanQueueFlyoutContent.Children.Add(new Border { BorderBrush = _borderBrush, BorderThickness = new Thickness(0, 1, 0, 0), Opacity = 0.4 });

        // Group by library
        var groups = scans.GroupBy(s => s.LibraryId)
            .Select(g =>
            {
                var lib = ViewModel.Libraries.FirstOrDefault(l => l.Id == g.Key);
                return new { LibraryId = g.Key, LibraryName = lib?.Name ?? $"Library #{g.Key}", Scans = g.OrderBy(s => s.Status == "running" ? 0 : 1).ToList() };
            })
            .OrderByDescending(g => g.Scans.Count(s => s.Status == "running"))
            .ThenBy(g => g.LibraryName)
            .ToList();

        var groupsPanel = new StackPanel { Spacing = 4, Padding = new Thickness(12, 8, 12, 8) };
        var visibleRows = 0;
        var hiddenRows = 0;

        foreach (var group in groups)
        {
            if (visibleRows >= MaxScanRowsInPopover)
            {
                hiddenRows += group.Scans.Count;
                continue;
            }

            var visibleScans = group.Scans.Take(MaxScanRowsInPopover - visibleRows).ToList();
            hiddenRows += group.Scans.Count - visibleScans.Count;

            // Library header
            var libHeader = new Grid { Padding = new Thickness(0, 6, 0, 6) };
            libHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            libHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            libHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var libLabel = new TextBlock
            {
                Text = group.LibraryName.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = _tertiaryText,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                CharacterSpacing = 50,
            };
            Grid.SetColumn(libLabel, 0);
            libHeader.Children.Add(libLabel);

            var separator = new Border { BorderBrush = _borderBrush, BorderThickness = new Thickness(0, 0.5, 0, 0), Opacity = 0.25, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(separator, 1);
            libHeader.Children.Add(separator);

            var capturedLibId = group.LibraryId;
            var cancelBtn = new Button
            {
                Padding = new Thickness(8, 3, 8, 3),
                MinHeight = 24,
                MinWidth = 0,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var cancelContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            cancelContent.Children.Add(new FontIcon { Glyph = "\uE71A", FontSize = 9, Foreground = _tertiaryText });
            cancelContent.Children.Add(new TextBlock { Text = "Cancel", FontSize = 10, Foreground = _tertiaryText });
            cancelBtn.Content = cancelContent;
            cancelBtn.Click += async (_, _) =>
            {
                await ViewModel.CancelLibraryScansCommand.ExecuteAsync(capturedLibId);
                ShowStatus("Scan cancellation requested.");
            };
            Grid.SetColumn(cancelBtn, 2);
            libHeader.Children.Add(cancelBtn);

            groupsPanel.Children.Add(libHeader);

            // Scan rows
            foreach (var scan in visibleScans)
            {
                var scanRow = new StackPanel
                {
                    Spacing = 2,
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = scan.Status == "running"
                        ? new SolidColorBrush(Color.FromArgb(10, 96, 165, 250))
                        : new SolidColorBrush(Colors.Transparent),
                    CornerRadius = new CornerRadius(8),
                };

                // First line: mode + trigger
                var line1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                line1.Children.Add(new Ellipse
                {
                    Width = 7, Height = 7,
                    Fill = scan.Status == "running"
                        ? new SolidColorBrush(HealthyColor)
                        : new SolidColorBrush(Color.FromArgb(50, 144, 160, 181)),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                line1.Children.Add(new TextBlock
                {
                    Text = FormatActiveScanMode(scan),
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = _primaryText,
                });
                if (!string.IsNullOrEmpty(scan.Trigger))
                {
                    line1.Children.Add(MakeBadge(FormatActiveScanTrigger(scan.Trigger), "secondary"));
                }
                scanRow.Children.Add(line1);

                // Second line: path + timing
                var line2Parts = new List<string>();
                if (!string.IsNullOrEmpty(scan.Path))
                    line2Parts.Add(scan.Path);
                else
                    line2Parts.Add("Entire library");

                if (scan.Status == "running")
                    line2Parts.Add(FormatActiveScanTime(scan.StartedAt, "Started"));
                else
                    line2Parts.Add("Waiting for capacity");

                scanRow.Children.Add(new TextBlock
                {
                    Text = string.Join(" \u00b7 ", line2Parts),
                    FontSize = 10,
                    Foreground = _tertiaryText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

                // Third line: progress
                var progress = FormatActiveScanProgress(scan);
                if (!string.IsNullOrEmpty(progress))
                {
                    scanRow.Children.Add(new TextBlock
                    {
                        Text = progress,
                        FontSize = 10,
                        Foreground = _tertiaryText,
                        Opacity = 0.8,
                    });
                }

                groupsPanel.Children.Add(scanRow);
                visibleRows++;
            }
        }

        if (hiddenRows > 0)
        {
            groupsPanel.Children.Add(new TextBlock
            {
                Text = $"+{hiddenRows} more scans",
                FontSize = 11,
                Foreground = _tertiaryText,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 4),
            });
        }

        ScanQueueFlyoutContent.Children.Add(groupsPanel);
    }

    private void ScanQueueButton_Click(object sender, RoutedEventArgs e)
    {
        // Flyout opens automatically via Button.Flyout property
        BuildScanQueuePopover(forceContent: true);
    }

    // ===================================================================
    //  Scan Queue Helpers (matching webui formatActiveScan* functions)
    // ===================================================================

    private static string FormatActiveScanMode(AdminScanRun scan) => scan.Mode switch
    {
        "library" => "Full library scan",
        "subtree" => "Subtree scan",
        "file" => "Single file scan",
        _ => scan.Mode,
    };

    private static string FormatActiveScanTrigger(string trigger) => trigger switch
    {
        "library_created" => "Created",
        "library_updated" => "Updated",
        "manual" => "Manual",
        "task_scan_libraries" => "Scheduled",
        _ => trigger.Replace('_', ' '),
    };

    private static string FormatActiveScanTime(string? iso, string prefix)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, out var date))
            return prefix;

        var seconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - date).TotalSeconds);
        if (seconds < 60) return $"{prefix} just now";
        if (seconds < 3600) return $"{prefix} {seconds / 60}m ago";
        if (seconds < 86400) return $"{prefix} {seconds / 3600}h ago";
        return $"{prefix} {seconds / 86400}d ago";
    }

    private static string FormatActiveScanProgress(AdminScanRun scan)
    {
        var result = scan.Result;
        if (result == null) return "";
        if (result.TotalFiles > 0 && result.FilesProcessed > 0)
        {
            var percent = Math.Max(0, Math.Min(100, (int)Math.Round((double)result.FilesProcessed / result.TotalFiles * 100)));
            return $"{result.Message ?? "Processing files"} \u00b7 {result.FilesProcessed:N0} / {result.TotalFiles:N0} ({percent}%)";
        }
        return result.Message ?? "";
    }

    // ===================================================================
    //  Unmatched Items Section (P0 Item 2)
    // ===================================================================

    private void BuildUnmatchedItemsSection()
    {
        if (ViewModel.UnmatchedTotal == 0 && ViewModel.UnmatchedPage == 0)
        {
            UnmatchedItemsSection.Visibility = Visibility.Collapsed;
            return;
        }

        UnmatchedItemsSection.Visibility = Visibility.Visible;
        UnmatchedCountBadge.Child = MakeBadge($"{ViewModel.UnmatchedTotal}", "secondary");

        BuildUnmatchedTable();
        BuildUnmatchedPagination();
    }

    private void BuildUnmatchedTable()
    {
        UnmatchedTablePanel.Children.Clear();

        var items = ViewModel.UnmatchedItems;
        if (!string.IsNullOrEmpty(_unmatchedFilter))
        {
            var q = _unmatchedFilter.ToLowerInvariant();
            items = items.Where(i =>
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.LibraryName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.ContentType.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Status.Contains(q, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        // Table header
        var header = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Title
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Library
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });                   // Type
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });                   // Status
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });                  // Actions

        AddHeaderCell(header, 0, "Title");
        AddHeaderCell(header, 1, "Library");
        AddHeaderCell(header, 2, "Type");
        AddHeaderCell(header, 3, "Status");
        AddHeaderCell(header, 4, "Actions");

        UnmatchedTablePanel.Children.Add(header);
        UnmatchedTablePanel.Children.Add(new Border { BorderBrush = _borderBrush, BorderThickness = new Thickness(0, 1, 0, 0), Opacity = 0.4 });

        if (items.Count == 0)
        {
            UnmatchedTablePanel.Children.Add(new TextBlock
            {
                Text = "No unmatched items on this page match your filter.",
                FontSize = 13,
                Foreground = _tertiaryText,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(0, 16, 0, 16),
            });
            return;
        }

        foreach (var item in items)
        {
            var row = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            // Title (with year)
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            titlePanel.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = _primaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (item.Year > 0)
            {
                titlePanel.Children.Add(new TextBlock
                {
                    Text = $"({item.Year})",
                    FontSize = 12,
                    Foreground = _tertiaryText,
                });
            }
            Grid.SetColumn(titlePanel, 0);
            row.Children.Add(titlePanel);

            // Library
            var libBlock = new TextBlock { Text = item.LibraryName, FontSize = 13, Foreground = _primaryText, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(libBlock, 1);
            row.Children.Add(libBlock);

            // Type
            var typeBadge = MakeBadge(item.ContentType, "outline");
            typeBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(typeBadge, 2);
            row.Children.Add(typeBadge);

            // Status
            var statusBadge = MakeBadge(item.Status, "secondary");
            statusBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(statusBadge, 3);
            row.Children.Add(statusBadge);

            // Match button
            var capturedItem = item;
            var matchBtn = new Button
            {
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                Padding = new Thickness(8, 4, 8, 4),
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var matchContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            matchContent.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 11 });
            matchContent.Children.Add(new TextBlock { Text = "Match", FontSize = 11 });
            matchBtn.Content = matchContent;
            matchBtn.Click += async (_, _) => await OpenMatchDialogAsync(capturedItem.ContentId, capturedItem.Title, capturedItem.Year, capturedItem.ContentType);
            Grid.SetColumn(matchBtn, 4);
            row.Children.Add(matchBtn);

            var rowBorder = new Border
            {
                Child = row,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Opacity = 0.7,
            };
            UnmatchedTablePanel.Children.Add(rowBorder);
        }
    }

    private void BuildUnmatchedPagination()
    {
        UnmatchedPaginationPanel.Children.Clear();

        if (ViewModel.UnmatchedTotal <= UNMATCHED_PAGE_SIZE)
        {
            UnmatchedPaginationPanel.Visibility = Visibility.Collapsed;
            return;
        }

        UnmatchedPaginationPanel.Visibility = Visibility.Visible;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)ViewModel.UnmatchedTotal / UNMATCHED_PAGE_SIZE));
        var page = ViewModel.UnmatchedPage;
        var rangeStart = ViewModel.UnmatchedTotal == 0 ? 0 : page * UNMATCHED_PAGE_SIZE + 1;
        var rangeEnd = Math.Min((page + 1) * UNMATCHED_PAGE_SIZE, ViewModel.UnmatchedTotal);

        UnmatchedPaginationPanel.Children.Add(new TextBlock
        {
            Text = $"{rangeStart}\u2013{rangeEnd} of {ViewModel.UnmatchedTotal}",
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        UnmatchedPaginationPanel.Children.Add(MakePaginationButton("\uE892", page > 0, async () =>
        {
            ViewModel.UnmatchedPage = 0;
            await ViewModel.LoadUnmatchedItemsAsync();
            BuildUnmatchedItemsSection();
        }));
        UnmatchedPaginationPanel.Children.Add(MakePaginationButton("\uE76B", page > 0, async () =>
        {
            ViewModel.UnmatchedPage = Math.Max(0, page - 1);
            await ViewModel.LoadUnmatchedItemsAsync();
            BuildUnmatchedItemsSection();
        }));
        UnmatchedPaginationPanel.Children.Add(MakePaginationButton("\uE76C", page < totalPages - 1, async () =>
        {
            ViewModel.UnmatchedPage = Math.Min(totalPages - 1, page + 1);
            await ViewModel.LoadUnmatchedItemsAsync();
            BuildUnmatchedItemsSection();
        }));
        UnmatchedPaginationPanel.Children.Add(MakePaginationButton("\uE893", page < totalPages - 1, async () =>
        {
            ViewModel.UnmatchedPage = totalPages - 1;
            await ViewModel.LoadUnmatchedItemsAsync();
            BuildUnmatchedItemsSection();
        }));
    }

    private void UnmatchedSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _unmatchedFilter = (sender as TextBox)?.Text ?? "";
        BuildUnmatchedTable();
    }

    // ===================================================================
    //  Stale Media IDs Section (P0 Item 3)
    // ===================================================================

    private void BuildStaleIdsSection()
    {
        if (ViewModel.StaleIds.Count == 0)
        {
            StaleIdsSection.Visibility = Visibility.Collapsed;
            return;
        }

        StaleIdsSection.Visibility = Visibility.Visible;
        StaleCountBadge.Child = MakeBadge($"{ViewModel.StaleIds.Count}", "secondary");

        BuildStaleTable();
        BuildStalePagination();
    }

    private void BuildStaleTable()
    {
        StaleTablePanel.Children.Clear();

        var items = ViewModel.StaleIds.AsEnumerable();
        if (!string.IsNullOrEmpty(_staleFilter))
        {
            var q = _staleFilter.ToLowerInvariant();
            items = items.Where(s =>
                s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.ProviderId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Provider.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.LibraryName.Contains(q, StringComparison.OrdinalIgnoreCase)
            );
        }
        var filteredList = items.ToList();

        // Paginate
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)filteredList.Count / STALE_PAGE_SIZE));
        _staleCurrentPage = Math.Min(_staleCurrentPage, totalPages - 1);
        var pageItems = filteredList.Skip(_staleCurrentPage * STALE_PAGE_SIZE).Take(STALE_PAGE_SIZE).ToList();

        // Table header
        var header = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); // Title
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });                     // Year
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // Library
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });                     // Provider
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });                    // Provider ID
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });                    // First Seen
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });                    // Last Seen
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });                     // Actions

        AddHeaderCell(header, 0, "Title");
        AddHeaderCell(header, 1, "Year");
        AddHeaderCell(header, 2, "Library");
        AddHeaderCell(header, 3, "Provider");
        AddHeaderCell(header, 4, "Provider ID");
        AddHeaderCell(header, 5, "First Seen");
        AddHeaderCell(header, 6, "Last Seen");
        AddHeaderCell(header, 7, "Actions");

        StaleTablePanel.Children.Add(header);
        StaleTablePanel.Children.Add(new Border { BorderBrush = _borderBrush, BorderThickness = new Thickness(0, 1, 0, 0), Opacity = 0.4 });

        foreach (var s in pageItems)
        {
            var row = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            AddCell(row, 0, s.Title, FontWeights.Medium, _primaryText);
            AddCell(row, 1, s.Year > 0 ? s.Year.ToString() : "", FontWeights.Normal, _tertiaryText);
            AddCell(row, 2, s.LibraryName, FontWeights.Normal, _primaryText);

            var providerBadge = MakeBadge(s.Provider, "outline");
            providerBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(providerBadge, 3);
            row.Children.Add(providerBadge);

            AddCell(row, 4, s.ProviderId, FontWeights.Normal, _tertiaryText, fontFamily: new FontFamily("Consolas"), fontSize: 11);

            string firstSeen = DateTimeOffset.TryParse(s.FirstSeenAt, out var fs) ? fs.LocalDateTime.ToString("g") : "";
            string lastSeen = DateTimeOffset.TryParse(s.LastSeenAt, out var ls) ? ls.LocalDateTime.ToString("g") : "";
            AddCell(row, 5, firstSeen, FontWeights.Normal, _tertiaryText, fontSize: 11);
            AddCell(row, 6, lastSeen, FontWeights.Normal, _tertiaryText, fontSize: 11);

            // Match button
            var capturedStale = s;
            var matchBtn = new Button
            {
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                Padding = new Thickness(8, 4, 8, 4),
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var matchContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            matchContent.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 11 });
            matchContent.Children.Add(new TextBlock { Text = "Match", FontSize = 11 });
            matchBtn.Content = matchContent;
            matchBtn.Click += async (_, _) => await OpenMatchDialogAsync(capturedStale.ContentId, capturedStale.Title, capturedStale.Year, capturedStale.ContentType);
            Grid.SetColumn(matchBtn, 7);
            row.Children.Add(matchBtn);

            var rowBorder = new Border
            {
                Child = row,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Opacity = 0.7,
            };
            StaleTablePanel.Children.Add(rowBorder);
        }
    }

    private void BuildStalePagination()
    {
        StalePaginationPanel.Children.Clear();
        var total = ViewModel.StaleIds.Count;
        if (total <= STALE_PAGE_SIZE)
        {
            StalePaginationPanel.Visibility = Visibility.Collapsed;
            return;
        }

        StalePaginationPanel.Visibility = Visibility.Visible;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / STALE_PAGE_SIZE));
        var page = _staleCurrentPage;
        var rangeStart = total == 0 ? 0 : page * STALE_PAGE_SIZE + 1;
        var rangeEnd = Math.Min((page + 1) * STALE_PAGE_SIZE, total);

        StalePaginationPanel.Children.Add(new TextBlock
        {
            Text = $"{rangeStart}\u2013{rangeEnd} of {total}",
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        StalePaginationPanel.Children.Add(MakePaginationButton("\uE892", page > 0, () => { _staleCurrentPage = 0; BuildStaleTable(); BuildStalePagination(); return Task.CompletedTask; }));
        StalePaginationPanel.Children.Add(MakePaginationButton("\uE76B", page > 0, () => { _staleCurrentPage = Math.Max(0, page - 1); BuildStaleTable(); BuildStalePagination(); return Task.CompletedTask; }));
        StalePaginationPanel.Children.Add(MakePaginationButton("\uE76C", page < totalPages - 1, () => { _staleCurrentPage = Math.Min(totalPages - 1, page + 1); BuildStaleTable(); BuildStalePagination(); return Task.CompletedTask; }));
        StalePaginationPanel.Children.Add(MakePaginationButton("\uE893", page < totalPages - 1, () => { _staleCurrentPage = totalPages - 1; BuildStaleTable(); BuildStalePagination(); return Task.CompletedTask; }));
    }

    private void StaleSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _staleFilter = (sender as TextBox)?.Text ?? "";
        _staleCurrentPage = 0;
        BuildStaleTable();
        BuildStalePagination();
    }

    // ===================================================================
    //  Ambiguous Roots Section (P0 Item 5)
    // ===================================================================

    private void BuildAmbiguousRootsSection()
    {
        // Populate library selector
        if (AmbiguousLibraryCombo.Items.Count == 0 && ViewModel.Libraries.Count > 0)
        {
            foreach (var lib in ViewModel.Libraries)
            {
                AmbiguousLibraryCombo.Items.Add(new ComboBoxItem { Content = lib.Name, Tag = lib.Id });
            }
            if (AmbiguousLibraryCombo.Items.Count > 0)
                AmbiguousLibraryCombo.SelectedIndex = 0;
        }

        // Show section unconditionally (webui shows it whenever libraries exist)
        if (ViewModel.Libraries.Count == 0)
        {
            AmbiguousRootsSection.Visibility = Visibility.Collapsed;
            return;
        }

        AmbiguousRootsSection.Visibility = Visibility.Visible;
        AmbiguousCountBadge.Child = MakeBadge($"{ViewModel.AmbiguousRoots.Count}", "secondary");

        BuildAmbiguousTable();
    }

    private void BuildAmbiguousTable()
    {
        AmbiguousTablePanel.Children.Clear();

        var items = ViewModel.AmbiguousRoots.AsEnumerable();
        if (!string.IsNullOrEmpty(_ambiguousFilter))
        {
            var q = _ambiguousFilter.ToLowerInvariant();
            items = items.Where(r =>
                r.RootPath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (r.SampleFilePath ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            );
        }
        var filteredList = items.ToList();

        // Table header
        var header = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); // Root
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });                   // Type
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });                   // Confidence
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });                   // Files
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });                  // Actions

        AddHeaderCell(header, 0, "Root");
        AddHeaderCell(header, 1, "Type");
        AddHeaderCell(header, 2, "Confidence");
        AddHeaderCell(header, 3, "Files");
        AddHeaderCell(header, 4, "Actions");

        AmbiguousTablePanel.Children.Add(header);
        AmbiguousTablePanel.Children.Add(new Border { BorderBrush = _borderBrush, BorderThickness = new Thickness(0, 1, 0, 0), Opacity = 0.4 });

        if (filteredList.Count == 0)
        {
            AmbiguousTablePanel.Children.Add(new TextBlock
            {
                Text = "No ambiguous roots for this library.",
                FontSize = 13,
                Foreground = _tertiaryText,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(0, 16, 0, 16),
            });
            return;
        }

        foreach (var root in filteredList)
        {
            var row = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            // Root column: title + path + evidence
            var rootStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var titleOrFolder = !string.IsNullOrEmpty(root.Title)
                ? root.Title
                : root.RootPath.Split('/').Where(s => !string.IsNullOrEmpty(s)).LastOrDefault() ?? root.RootPath;
            rootStack.Children.Add(new TextBlock
            {
                Text = titleOrFolder,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = _primaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            rootStack.Children.Add(new TextBlock
            {
                Text = root.RootPath,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = _tertiaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (root.EvidenceJson != null && root.EvidenceJson.Count > 0)
            {
                rootStack.Children.Add(new TextBlock
                {
                    Text = BuildRootEvidenceSummary(root),
                    FontSize = 11,
                    Foreground = _tertiaryText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            Grid.SetColumn(rootStack, 0);
            row.Children.Add(rootStack);

            // Type
            var typeBadge = MakeBadge(root.InferredType.Length > 0 ? root.InferredType : "unknown", "outline");
            typeBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(typeBadge, 1);
            row.Children.Add(typeBadge);

            // Confidence
            var confBadge = MakeBadge(root.TypeConfidence, "secondary");
            confBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(confBadge, 2);
            row.Children.Add(confBadge);

            // Files
            var filesBlock = new TextBlock
            {
                Text = root.ObservedFileCount.ToString(),
                FontSize = 12,
                Foreground = _tertiaryText,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(filesBlock, 3);
            row.Children.Add(filesBlock);

            // Override button
            var capturedRoot = root;
            var overrideBtn = new Button
            {
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                Padding = new Thickness(8, 4, 8, 4),
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center,
            };
            overrideBtn.Content = new TextBlock { Text = "Override", FontSize = 11 };
            overrideBtn.Click += async (_, _) => await OpenRootOverrideDialogAsync(capturedRoot);
            Grid.SetColumn(overrideBtn, 4);
            row.Children.Add(overrideBtn);

            var rowBorder = new Border
            {
                Child = row,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Opacity = 0.7,
            };
            AmbiguousTablePanel.Children.Add(rowBorder);
        }
    }

    private async void AmbiguousLibraryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AmbiguousLibraryCombo.SelectedItem is ComboBoxItem item && item.Tag is int libraryId)
        {
            await ViewModel.LoadAmbiguousRootsAsync(libraryId);
            BuildAmbiguousRootsSection();
        }
    }

    private void AmbiguousSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ambiguousFilter = (sender as TextBox)?.Text ?? "";
        BuildAmbiguousTable();
    }

    private static string BuildRootEvidenceSummary(LibraryRoot root)
    {
        var evidence = root.EvidenceJson;
        if (evidence == null) return "";
        var parts = new List<string>();

        if (evidence.TryGetValue("has_folder_ids", out var hfi))
        {
            if (hfi is JsonElement je && je.ValueKind == JsonValueKind.True) parts.Add("folder IDs");
            else if (hfi is JsonElement jf && jf.ValueKind == JsonValueKind.False) parts.Add("no folder IDs");
            else if (hfi is bool b) parts.Add(b ? "folder IDs" : "no folder IDs");
        }
        if (TryGetInt(evidence, "season_structure_files", out var ssf) && ssf > 0)
            parts.Add($"{ssf} season-structured files");
        if (TryGetInt(evidence, "movie_evidence_files", out var mef) && mef > 0)
            parts.Add($"{mef} movie-shaped files");
        if (TryGetInt(evidence, "wrapper_collapses", out var wc) && wc > 0)
            parts.Add($"{wc} wrapper collapses");
        if (TryGetInt(evidence, "ancestor_promotions", out var ap) && ap > 0)
            parts.Add($"{ap} ancestor promotions");

        return string.Join(" \u00b7 ", parts);
    }

    private static bool TryGetInt(Dictionary<string, object> dict, string key, out int value)
    {
        value = 0;
        if (!dict.TryGetValue(key, out var obj)) return false;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) { value = je.GetInt32(); return true; }
        if (obj is int i) { value = i; return true; }
        if (obj is long l) { value = (int)l; return true; }
        if (obj is double d) { value = (int)d; return true; }
        return false;
    }

    // ===================================================================
    //  Root Override Dialog (P0 Item 5)
    // ===================================================================

    private async Task OpenRootOverrideDialogAsync(LibraryRoot root)
    {
        var form = new StackPanel { Width = 420, Spacing = 14 };

        form.Children.Add(new TextBlock
        {
            Text = $"Force the inferred identity for {root.RootPath}",
            FontSize = 12,
            Foreground = _secondaryText,
            TextWrapping = TextWrapping.Wrap,
        });

        // Type + Year row
        var typeYearRow = new Grid { ColumnSpacing = 12 };
        typeYearRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        typeYearRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var typeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 13 };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Auto", Tag = "" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Movie", Tag = "movie" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Series", Tag = "series" });
        var currentForcedType = root.ActiveOverride?.ForcedType ?? root.InferredType ?? "";
        foreach (ComboBoxItem item in typeCombo.Items)
        {
            if ((string)item.Tag == currentForcedType) { typeCombo.SelectedItem = item; break; }
        }
        if (typeCombo.SelectedItem == null) typeCombo.SelectedIndex = 0;

        var typeGroup = new StackPanel { Spacing = 6 };
        typeGroup.Children.Add(MakeFormLabel("Type"));
        typeGroup.Children.Add(typeCombo);
        Grid.SetColumn(typeGroup, 0);
        typeYearRow.Children.Add(typeGroup);

        var yearBox = new TextBox
        {
            Text = root.ActiveOverride?.ForcedYear?.ToString() ?? (root.Year > 0 ? root.Year.ToString() : ""),
            PlaceholderText = "2024",
            FontSize = 13,
        };
        var yearGroup = new StackPanel { Spacing = 6 };
        yearGroup.Children.Add(MakeFormLabel("Year"));
        yearGroup.Children.Add(yearBox);
        Grid.SetColumn(yearGroup, 1);
        typeYearRow.Children.Add(yearGroup);
        form.Children.Add(typeYearRow);

        // Title
        var titleBox = new TextBox
        {
            Text = root.ActiveOverride?.ForcedTitle ?? root.Title ?? "",
            PlaceholderText = "Forced title",
            FontSize = 13,
        };
        var titleGroup = new StackPanel { Spacing = 6 };
        titleGroup.Children.Add(MakeFormLabel("Title"));
        titleGroup.Children.Add(titleBox);
        form.Children.Add(titleGroup);

        // TMDB / IMDb / TVDB row
        var idsRow = new Grid { ColumnSpacing = 8 };
        idsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        idsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        idsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tmdbBox = new TextBox { Text = root.ActiveOverride?.ForcedTmdbId ?? root.TmdbId ?? "", FontSize = 13 };
        var tmdbGroup = new StackPanel { Spacing = 6 };
        tmdbGroup.Children.Add(MakeFormLabel("TMDB ID"));
        tmdbGroup.Children.Add(tmdbBox);
        Grid.SetColumn(tmdbGroup, 0);
        idsRow.Children.Add(tmdbGroup);

        var imdbBox = new TextBox { Text = root.ActiveOverride?.ForcedImdbId ?? root.ImdbId ?? "", FontSize = 13 };
        var imdbGroup = new StackPanel { Spacing = 6 };
        imdbGroup.Children.Add(MakeFormLabel("IMDb ID"));
        imdbGroup.Children.Add(imdbBox);
        Grid.SetColumn(imdbGroup, 1);
        idsRow.Children.Add(imdbGroup);

        var tvdbBox = new TextBox { Text = root.ActiveOverride?.ForcedTvdbId ?? root.TvdbId ?? "", FontSize = 13 };
        var tvdbGroup = new StackPanel { Spacing = 6 };
        tvdbGroup.Children.Add(MakeFormLabel("TVDB ID"));
        tvdbGroup.Children.Add(tvdbBox);
        Grid.SetColumn(tvdbGroup, 2);
        idsRow.Children.Add(tvdbGroup);
        form.Children.Add(idsRow);

        // Note
        var noteBox = new TextBox
        {
            Text = root.ActiveOverride?.Note ?? "",
            PlaceholderText = "Why this override exists",
            FontSize = 13,
        };
        var noteGroup = new StackPanel { Spacing = 6 };
        noteGroup.Children.Add(MakeFormLabel("Note"));
        noteGroup.Children.Add(noteBox);
        form.Children.Add(noteGroup);

        var dialog = new ContentDialog
        {
            Title = "Root Override",
            PrimaryButtonText = "Save Override",
            SecondaryButtonText = root.ActiveOverride != null ? "Remove Override" : "",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var selectedType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            int.TryParse(yearBox.Text.Trim(), out var year);

            var request = new UpsertLibraryRootOverrideRequest
            {
                LibraryId = root.LibraryId,
                RootPath = root.RootPath,
                ForcedType = string.IsNullOrEmpty(selectedType) ? null : selectedType,
                ForcedTitle = string.IsNullOrEmpty(titleBox.Text.Trim()) ? null : titleBox.Text.Trim(),
                ForcedYear = year > 0 ? year : null,
                ForcedTmdbId = string.IsNullOrEmpty(tmdbBox.Text.Trim()) ? null : tmdbBox.Text.Trim(),
                ForcedImdbId = string.IsNullOrEmpty(imdbBox.Text.Trim()) ? null : imdbBox.Text.Trim(),
                ForcedTvdbId = string.IsNullOrEmpty(tvdbBox.Text.Trim()) ? null : tvdbBox.Text.Trim(),
                Note = string.IsNullOrEmpty(noteBox.Text.Trim()) ? null : noteBox.Text.Trim(),
            };

            await ViewModel.UpsertRootOverrideCommand.ExecuteAsync(request);
            ShowStatus(ViewModel.StatusMessage ?? "Root override saved.");
            BuildAmbiguousRootsSection();
        }
        else if (result == ContentDialogResult.Secondary)
        {
            var delRequest = new DeleteLibraryRootOverrideRequest
            {
                LibraryId = root.LibraryId,
                RootPath = root.RootPath,
            };
            await ViewModel.DeleteRootOverrideCommand.ExecuteAsync(delRequest);
            ShowStatus(ViewModel.StatusMessage ?? "Root override removed.");
            BuildAmbiguousRootsSection();
        }
    }

    // ===================================================================
    //  Match Item Dialog (used by Unmatched Items and Stale IDs)
    // ===================================================================

    private async Task OpenMatchDialogAsync(string contentId, string title, int year, string contentType)
    {
        var api = App.Services.GetRequiredService<Core.Api.AdminApi>();

        // Search for candidates
        var searchRequest = new ItemMatchSearchRequest
        {
            Title = title,
            Year = year > 0 ? year : null,
        };

        List<MatchCandidate> candidates;
        try
        {
            var response = await api.MatchSearchAsync(contentId, searchRequest);
            candidates = response.Candidates;
        }
        catch (Exception ex)
        {
            ShowStatus($"Match search failed: {ex.Message}");
            return;
        }

        // Build candidate list
        var form = new StackPanel { Width = 480, Spacing = 8 };

        form.Children.Add(new TextBlock
        {
            Text = $"Match \"{title}\" ({contentType})",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = _primaryText,
            TextWrapping = TextWrapping.Wrap,
        });

        if (candidates.Count == 0)
        {
            form.Children.Add(new TextBlock
            {
                Text = "No candidates found. Try adjusting the title or year.",
                FontSize = 13,
                Foreground = _tertiaryText,
                Padding = new Thickness(0, 16, 0, 16),
            });
        }

        MatchCandidate? selectedCandidate = null;
        var candidateButtons = new List<Border>();

        foreach (var c in candidates)
        {
            var capturedCandidate = c;
            var row = new StackPanel { Spacing = 2, Padding = new Thickness(10, 8, 10, 8) };

            var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            titleLine.Children.Add(new TextBlock { Text = c.Title, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = _primaryText });
            if (c.Year > 0) titleLine.Children.Add(new TextBlock { Text = $"({c.Year})", FontSize = 12, Foreground = _tertiaryText });
            row.Children.Add(titleLine);

            if (!string.IsNullOrEmpty(c.Overview))
            {
                row.Children.Add(new TextBlock
                {
                    Text = c.Overview.Length > 120 ? c.Overview[..117] + "..." : c.Overview,
                    FontSize = 11,
                    Foreground = _tertiaryText,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2,
                });
            }

            // Provider IDs
            if (c.ProviderIds.Count > 0)
            {
                var idsLine = string.Join(" \u00b7 ", c.ProviderIds.Select(kv => $"{kv.Key}: {kv.Value}"));
                row.Children.Add(new TextBlock
                {
                    Text = idsLine,
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = _tertiaryText,
                    Opacity = 0.7,
                });
            }

            var candidateBorder = new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(8),
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Colors.Transparent),
                Margin = new Thickness(0, 2, 0, 2),
            };

            candidateBorder.PointerPressed += (_, _) =>
            {
                selectedCandidate = capturedCandidate;
                foreach (var b in candidateButtons)
                    b.Background = new SolidColorBrush(Colors.Transparent);
                candidateBorder.Background = new SolidColorBrush(Color.FromArgb(20, 96, 165, 250));
            };

            candidateButtons.Add(candidateBorder);
            form.Children.Add(candidateBorder);
        }

        var dialog = new ContentDialog
        {
            Title = "Match Item",
            PrimaryButtonText = candidates.Count > 0 ? "Apply Match" : "",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = new ScrollViewer { Content = form, MaxHeight = 400 },
            DefaultButton = ContentDialogButton.Primary,
        };

        var dialogResult = await dialog.ShowAsync();
        if (dialogResult == ContentDialogResult.Primary && selectedCandidate != null)
        {
            try
            {
                await api.MatchApplyAsync(contentId, new ItemMatchApplyRequest
                {
                    ProviderIds = selectedCandidate.ProviderIds,
                });
                ShowStatus("Match applied successfully.");

                // Reload data
                await ViewModel.LoadUnmatchedItemsAsync();
                BuildUnmatchedItemsSection();
                BuildStaleIdsSection();
            }
            catch (Exception ex)
            {
                ShowStatus($"Match failed: {ex.Message}");
            }
        }
    }

    // ===================================================================
    //  Skipped Roots Table
    // ===================================================================

    private string _skippedRootsSearch = "";

    private void SkippedRootsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _skippedRootsSearch = SkippedRootsSearchBox.Text?.Trim() ?? "";
        BuildSkippedRootsRows();
    }

    private void BuildSkippedRootsRows()
    {
        SkippedRootsPanel.Children.Clear();

        if (ViewModel.SkippedRoots.Count == 0)
        {
            SkippedRootsSection.Visibility = Visibility.Collapsed;
            return;
        }

        SkippedRootsSection.Visibility = Visibility.Visible;

        // Apply search filter
        var filtered = ViewModel.SkippedRoots.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(_skippedRootsSearch))
        {
            var q = _skippedRootsSearch.ToLowerInvariant();
            filtered = filtered.Where(r =>
                (r.RootPath?.ToLowerInvariant().Contains(q) == true) ||
                (r.Reason?.ToLowerInvariant().Contains(q) == true) ||
                (r.SampleFilePath?.ToLowerInvariant().Contains(q) == true));
        }

        bool isFirst = true;
        foreach (var root in filtered)
        {
            if (!isFirst)
            {
                SkippedRootsPanel.Children.Add(new Border
                {
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            SkippedRootsPanel.Children.Add(BuildSkippedRootRow(root));
        }
    }

    private FrameworkElement BuildSkippedRootRow(LibrarySkippedRoot root)
    {
        var row = new Grid
        {
            Padding = new Thickness(0, 8, 0, 8),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var pathBlock = new TextBlock
        {
            Text = root.RootPath,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = _primaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var libNameBlock = new TextBlock
        {
            Text = root.LibraryName,
            FontSize = 13,
            Foreground = _primaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        var reasonBadge = MakeBadge(root.Reason, "outline");

        var sampleBlock = new TextBlock
        {
            Text = root.SampleFilePath,
            FontSize = 12,
            Foreground = _tertiaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400,
            VerticalAlignment = VerticalAlignment.Center
        };

        string firstSeenText = "";
        if (DateTimeOffset.TryParse(root.FirstSeenAt, out var firstSeen))
            firstSeenText = firstSeen.LocalDateTime.ToString("g");
        var firstSeenBlock = new TextBlock
        {
            Text = firstSeenText,
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        string lastSeenText = "";
        if (DateTimeOffset.TryParse(root.LastSeenAt, out var lastSeen))
            lastSeenText = lastSeen.LocalDateTime.ToString("g");
        var lastSeenBlock = new TextBlock
        {
            Text = lastSeenText,
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(pathBlock, 0);
        Grid.SetColumn(libNameBlock, 1);
        Grid.SetColumn(reasonBadge, 2);
        Grid.SetColumn(sampleBlock, 3);
        Grid.SetColumn(firstSeenBlock, 4);
        Grid.SetColumn(lastSeenBlock, 5);

        row.Children.Add(pathBlock);
        row.Children.Add(libNameBlock);
        row.Children.Add(reasonBadge);
        row.Children.Add(sampleBlock);
        row.Children.Add(firstSeenBlock);
        row.Children.Add(lastSeenBlock);

        return row;
    }

    // ===================================================================
    //  Badge Builder
    // ===================================================================

    private static Border MakeBadge(string text, string variant)
    {
        Color bgColor, fgColor, borderColorVal;
        double borderWidth = 0;

        switch (variant)
        {
            case "destructive":
                bgColor = Color.FromArgb(30, 220, 90, 90);
                fgColor = DestructiveColor;
                borderColorVal = Colors.Transparent;
                break;
            case "outline":
                bgColor = Colors.Transparent;
                fgColor = Color.FromArgb(255, 144, 160, 181);
                borderColorVal = Color.FromArgb(120, 130, 130, 130);
                borderWidth = 1;
                break;
            case "secondary":
            default:
                bgColor = Color.FromArgb(40, 144, 160, 181);
                fgColor = Color.FromArgb(255, 144, 160, 181);
                borderColorVal = Colors.Transparent;
                break;
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bgColor),
            BorderBrush = new SolidColorBrush(borderColorVal),
            BorderThickness = new Thickness(borderWidth),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fgColor)
        };
        return badge;
    }

    // ===================================================================
    //  Icon Button Builder (28x28, matching web h-7 w-7)
    // ===================================================================

    private Button MakeIconButton28(string glyph, string tooltip, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : _secondaryText;

        var btn = new Button
        {
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = fg
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    // ===================================================================
    //  Pagination Button Builder
    // ===================================================================

    private Button MakePaginationButton(string glyph, bool enabled, Func<Task> action)
    {
        var btn = new Button
        {
            Width = 28, Height = 28, MinWidth = 28, MinHeight = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            IsEnabled = enabled,
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 13,
                Foreground = enabled ? _secondaryText : _tertiaryText,
            }
        };
        btn.Click += async (_, _) => await action();
        return btn;
    }

    // ===================================================================
    //  Table Helper Methods
    // ===================================================================

    private void AddHeaderCell(Grid grid, int column, string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private void AddCell(Grid grid, int column, string text, Windows.UI.Text.FontWeight weight, SolidColorBrush fg, FontFamily? fontFamily = null, int fontSize = 13)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (fontFamily != null) tb.FontFamily = fontFamily;
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    // ===================================================================
    //  Header Button Handlers
    // ===================================================================

    private async void ScanAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanAllCommand.ExecuteAsync(null);
        ShowStatus(ViewModel.StatusMessage ?? "Scan all started.");
    }

    private async void AddLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    private void CatalogMaintenanceButton_Click(object sender, RoutedEventArgs e)
    {
        try { Frame.Navigate(typeof(AdminMaintenancePage)); }
        catch { }
    }

    // ===================================================================
    //  Create Dialog
    // ===================================================================

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getBody, getProviderChain, getPosterFile) = BuildLibraryForm(null);

        var dialog = new ContentDialog
        {
            Title = "Add Library",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var body = getBody();
            if (body == null) return;

            try
            {
                await ViewModel.CreateLibraryCommand.ExecuteAsync(body);
                ShowStatus(ViewModel.StatusMessage ?? "Library created.");

                // Apply provider chain if modified
                var chain = getProviderChain();
                if (chain != null && ViewModel.Libraries.Count > 0)
                {
                    var newLib = ViewModel.Libraries.Last();
                    await ViewModel.SetLibraryProvidersAsync(newLib.Id, chain);

                    // Upload poster if selected
                    var poster = getPosterFile();
                    if (poster.Bytes != null && poster.Name != null && poster.ContentType != null)
                    {
                        var adminApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.AdminApi>();
                        await adminApi.SetLibraryPosterAsync(newLib.Id, poster.Bytes, poster.Name, poster.ContentType);
                    }
                }
            }
            catch { }
        }
    }

    // ===================================================================
    //  Edit Dialog
    // ===================================================================

    private async Task OpenEditDialogAsync(Library lib)
    {
        var (formContent, getBody, getProviderChain, getPosterFile) = BuildLibraryForm(lib);

        var dialog = new ContentDialog
        {
            Title = "Edit Library",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var body = getBody();
            if (body == null) return;

            try
            {
                await ViewModel.UpdateLibraryCommand.ExecuteAsync((lib.Id, body));
                ShowStatus(ViewModel.StatusMessage ?? "Library updated.");

                // Apply provider chain if modified
                var chain = getProviderChain();
                if (chain != null)
                    await ViewModel.SetLibraryProvidersAsync(lib.Id, chain);

                // Upload poster if selected
                var poster = getPosterFile();
                if (poster.Bytes != null && poster.Name != null && poster.ContentType != null)
                {
                    var adminApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.AdminApi>();
                    await adminApi.SetLibraryPosterAsync(lib.Id, poster.Bytes, poster.Name, poster.ContentType);
                }
            }
            catch { }
        }
    }

    // ===================================================================
    //  Delete Dialog
    // ===================================================================

    private async Task OpenDeleteDialogAsync(Library lib)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete library",
            Content = $"Delete library \"{lib.Name}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.DeleteLibraryCommand.ExecuteAsync(lib.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Library deleted.");
            }
            catch { }
        }
    }

    // ===================================================================
    //  Confirm Empty Root Cleanup Dialog
    // ===================================================================

    private async Task OpenConfirmEmptyRootDialogAsync(Library lib)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirm empty root cleanup",
            Content = $"If the next scan still finds 0 media files for \"{lib.Name}\", remove the library items?",
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.ConfirmEmptyRootCleanupCommand.ExecuteAsync(lib.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Empty root cleanup confirmed.");
            }
            catch { }
        }
    }

    // ===================================================================
    //  Form Builder — matches web: Name+Enabled, Paths, Type+Poster, Metadata Providers
    // ===================================================================

    private (FrameworkElement Content, Func<object?> GetBody, Func<SetLibraryChainRequest?> GetProviderChain,
        Func<(byte[]? Bytes, string? Name, string? ContentType)> GetPosterFile)
        BuildLibraryForm(Library? editingLib)
    {
        // Name field
        var nameBox = new TextBox
        {
            PlaceholderText = "Library name",
            Text = editingLib?.Name ?? "",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        // Enabled toggle
        var enabledSwitch = new ToggleSwitch
        {
            IsOn = editingLib?.Enabled ?? true,
            OnContent = "Enabled",
            OffContent = "Disabled"
        };

        // Type ComboBox
        var typeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Movies", Tag = "movies" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Series", Tag = "series" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Audiobooks", Tag = "audiobooks" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Ebooks", Tag = "ebooks" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Podcasts", Tag = "podcasts" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Mixed", Tag = "mixed" });

        string currentType = editingLib?.Type ?? "movies";
        foreach (ComboBoxItem item in typeCombo.Items)
        {
            if ((string)item.Tag == currentType)
            {
                typeCombo.SelectedItem = item;
                break;
            }
        }
        if (typeCombo.SelectedItem == null && typeCombo.Items.Count > 0)
            typeCombo.SelectedIndex = 0;

        // ---- Paths section ----
        var pathsPanel = new StackPanel { Spacing = 6 };
        var pathInputs = new List<TextBox>();

        void AddPathRow(string initialValue)
        {
            var tb = new TextBox
            {
                PlaceholderText = "/mnt/media/movies",
                Text = initialValue,
                CornerRadius = new CornerRadius(8),
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            pathInputs.Add(tb);

            var rowGrid = new Grid { ColumnSpacing = 4 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Browse button for folder selection
            var browseBtn = new Button
            {
                Width = 36, Height = 36, MinWidth = 36, MinHeight = 36,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon { Glyph = "\uED25", FontSize = 13 }, // FolderOpen
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(browseBtn, "Browse folder");
            var capturedTb = tb;
            browseBtn.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FolderPicker();
                    picker.FileTypeFilter.Add("*");
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                    var folder = await picker.PickSingleFolderAsync();
                    if (folder != null) capturedTb.Text = folder.Path;
                }
                catch { }
            };

            var deleteBtn = new Button
            {
                Width = 36,
                Height = 36,
                MinWidth = 36,
                MinHeight = 36,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 13 },
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(deleteBtn, "Remove path");
            deleteBtn.Click += (_, _) =>
            {
                if (pathInputs.Count <= 1) return;
                pathInputs.Remove(tb);
                pathsPanel.Children.Remove(rowGrid);
                RefreshDeleteButtons();
            };

            Grid.SetColumn(tb, 0);
            Grid.SetColumn(browseBtn, 1);
            Grid.SetColumn(deleteBtn, 2);
            rowGrid.Children.Add(tb);
            rowGrid.Children.Add(browseBtn);
            rowGrid.Children.Add(deleteBtn);
            pathsPanel.Children.Add(rowGrid);
        }

        void RefreshDeleteButtons()
        {
            bool moreThanOne = pathInputs.Count > 1;
            foreach (Grid g in pathsPanel.Children.Cast<Grid>())
            {
                // Delete button is the last child (col 2)
                if (g.Children.Count > 2 && g.Children[2] is Button btn)
                    btn.Visibility = moreThanOne ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        var initialPaths = editingLib?.Paths ?? [];
        if (initialPaths.Count == 0)
            AddPathRow("");
        else
            foreach (var p in initialPaths)
                AddPathRow(p);

        RefreshDeleteButtons();

        var addPathBtn = new Button
        {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"]
        };
        var addPathContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        addPathContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 13 });
        addPathContent.Children.Add(new TextBlock { Text = "Add Path", FontSize = 12 });
        addPathBtn.Content = addPathContent;
        addPathBtn.Click += (_, _) =>
        {
            AddPathRow("");
            RefreshDeleteButtons();
        };

        // ---- Metadata Providers Section (P0 Item 4) ----
        bool chainDirty = false;
        var levelChains = new Dictionary<string, List<LibraryProviderChainEntry>>();
        var providerSection = new StackPanel { Spacing = 8 };

        // Load current provider chain if editing
        if (editingLib != null)
        {
            _ = LoadProviderChainAsync(editingLib.Id, editingLib.Type, providerSection, levelChains, () => chainDirty = true);
        }
        else
        {
            // For new libraries, show providers based on selected type
            _ = LoadDefaultProvidersAsync(currentType, providerSection, levelChains, () => chainDirty = true);
        }

        // Update provider section when type changes
        typeCombo.SelectionChanged += (_, _) =>
        {
            var newType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "movies";
            _ = LoadDefaultProvidersAsync(newType, providerSection, levelChains, () => chainDirty = true);
        };

        // Build form layout
        var form = new StackPanel { Width = 420, Spacing = 14 };

        // Row 1: Name + Enabled
        var nameRow = new Grid { ColumnSpacing = 12 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameGroup = new StackPanel { Spacing = 6 };
        nameGroup.Children.Add(MakeFormLabel("Name"));
        nameGroup.Children.Add(nameBox);

        var enabledGroup = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        enabledGroup.Children.Add(enabledSwitch);

        Grid.SetColumn(nameGroup, 0);
        Grid.SetColumn(enabledGroup, 1);
        nameRow.Children.Add(nameGroup);
        nameRow.Children.Add(enabledGroup);
        form.Children.Add(nameRow);

        // Row 2: Paths
        var pathsGroup = new StackPanel { Spacing = 6 };
        pathsGroup.Children.Add(MakeFormLabel("Paths"));
        pathsGroup.Children.Add(pathsPanel);
        pathsGroup.Children.Add(addPathBtn);
        form.Children.Add(pathsGroup);

        // Poster file picker state (for upload after save)
        byte[]? posterFileBytes = null;
        string? posterFileName = null;
        string? posterContentType = null;

        // Row 3: Type + Poster
        var typeRow = new Grid { ColumnSpacing = 12 };
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var typeGroup = new StackPanel { Spacing = 6 };
        typeGroup.Children.Add(MakeFormLabel("Type"));
        typeGroup.Children.Add(typeCombo);
        Grid.SetColumn(typeGroup, 0);
        typeRow.Children.Add(typeGroup);

        // Poster section (only when editing)
        if (editingLib != null)
        {
            var posterGroup = new StackPanel { Spacing = 6 };
            posterGroup.Children.Add(MakeFormLabel("Poster"));

            var posterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            if (!string.IsNullOrEmpty(editingLib.PosterUrl))
            {
                var posterImg = new Image
                {
                    Height = 56,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(editingLib.PosterUrl))
                };
                var posterBorder = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(1),
                    Child = posterImg
                };
                posterRow.Children.Add(posterBorder);
            }
            else
            {
                var placeholder = new Border
                {
                    Width = 100,
                    Height = 56,
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(20, 144, 160, 181)),
                    Child = new FontIcon
                    {
                        Glyph = "\uEB9F",
                        FontSize = 16,
                        Foreground = _tertiaryText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                posterRow.Children.Add(placeholder);
            }

            // File picker button
            var pickPosterBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uEB9F", FontSize = 13 },
                Width = 36, Height = 36, MinWidth = 36, MinHeight = 36,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(pickPosterBtn, "Upload poster image");
            var posterPickLabel = new TextBlock { FontSize = 11, Foreground = _tertiaryText, VerticalAlignment = VerticalAlignment.Center };
            pickPosterBtn.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker();
                    picker.FileTypeFilter.Add(".jpg");
                    picker.FileTypeFilter.Add(".jpeg");
                    picker.FileTypeFilter.Add(".png");
                    picker.FileTypeFilter.Add(".webp");
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                    var file = await picker.PickSingleFileAsync();
                    if (file != null)
                    {
                        var buf = await Windows.Storage.FileIO.ReadBufferAsync(file);
                        posterFileBytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buf);
                        posterFileName = file.Name;
                        posterContentType = file.ContentType;
                        posterPickLabel.Text = file.Name;
                    }
                }
                catch { }
            };
            posterRow.Children.Add(pickPosterBtn);
            posterRow.Children.Add(posterPickLabel);

            posterGroup.Children.Add(posterRow);
            Grid.SetColumn(posterGroup, 1);
            typeRow.Children.Add(posterGroup);
        }

        form.Children.Add(typeRow);

        // Metadata Language selector (webui LibraryForm: language select)
        var langCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8), FontSize = 13, Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (var (code, name) in new[] {
            ("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"),
            ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("ja", "Japanese"),
            ("ko", "Korean"), ("zh", "Chinese"), ("ru", "Russian"), ("ar", "Arabic"),
            ("sv", "Swedish"), ("da", "Danish"), ("no", "Norwegian"), ("fi", "Finnish"),
            ("pl", "Polish"), ("cs", "Czech"), ("hu", "Hungarian"), ("ro", "Romanian"),
            ("tr", "Turkish"), ("th", "Thai"), ("vi", "Vietnamese"), ("id", "Indonesian") })
        {
            var item = new ComboBoxItem { Content = name, Tag = code };
            if (code == (editingLib?.MetadataLanguage ?? "en")) item.IsSelected = true;
            langCombo.Items.Add(item);
        }
        if (langCombo.SelectedIndex < 0) langCombo.SelectedIndex = 0;
        var langField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        langField.Children.Add(new TextBlock
        {
            Text = "Metadata Language", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = _secondaryText,
        });
        langField.Children.Add(langCombo);
        form.Children.Add(langField);

        // Chapter Thumbnails toggle (webui LibraryForm: chapter thumbnails switch)
        var chapterThumbToggle = new ToggleSwitch
        {
            IsOn = editingLib?.ChapterThumbnailsEnabled ?? true,
            OnContent = "Chapter thumbnails enabled",
            OffContent = "Chapter thumbnails disabled",
        };
        form.Children.Add(chapterThumbToggle);

        var introDetectionToggle = new ToggleSwitch
        {
            IsOn = editingLib?.IntroDetectionEnabled ?? false,
            OnContent = "Detect intro markers",
            OffContent = "Intro marker detection disabled",
        };
        form.Children.Add(introDetectionToggle);

        // Row 4: Metadata Providers
        var providerWrapper = new StackPanel { Spacing = 6 };
        providerWrapper.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 4, 0, 0),
        });
        providerWrapper.Children.Add(new TextBlock
        {
            Text = "Metadata Providers",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = _primaryText,
            Margin = new Thickness(0, 4, 0, 0),
        });
        providerWrapper.Children.Add(providerSection);
        form.Children.Add(providerWrapper);

        // Wrap in ScrollViewer for long forms
        var scrollViewer = new ScrollViewer
        {
            Content = form,
            MaxHeight = 500,
        };

        // GetBody func
        object? GetBody()
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return null;

            string selectedType = "movies";
            if (typeCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string tag)
                selectedType = tag;

            var paths = pathInputs
                .Select(tb => tb.Text.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var selectedLang = (langCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["type"] = selectedType,
                ["enabled"] = enabledSwitch.IsOn,
                ["paths"] = paths,
                ["metadata_language"] = selectedLang,
                ["chapter_thumbnails_enabled"] = chapterThumbToggle.IsOn,
                ["intro_detection_enabled"] = introDetectionToggle.IsOn,
            };
        }

        // GetProviderChain func
        SetLibraryChainRequest? GetProviderChain()
        {
            if (!chainDirty || levelChains.Count == 0) return null;
            var request = new SetLibraryChainRequest
            {
                Levels = levelChains.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select((e, i) => new SetLibraryChainEntry
                    {
                        ProviderId = e.ProviderId,
                        Priority = i,
                        Enabled = e.Enabled,
                    }).ToList()
                )
            };
            return request;
        }

        (byte[]? Bytes, string? Name, string? ContentType) GetPosterFile() => (posterFileBytes, posterFileName, posterContentType);

        return (scrollViewer, GetBody, GetProviderChain, GetPosterFile);
    }

    // ===================================================================
    //  Metadata Provider Chain UI (P0 Item 4)
    // ===================================================================

    private async Task LoadProviderChainAsync(
        int libraryId, string libraryType, StackPanel container,
        Dictionary<string, List<LibraryProviderChainEntry>> levelChains,
        Action onDirty)
    {
        var chain = await ViewModel.GetLibraryProvidersAsync(libraryId);
        if (chain?.Levels != null)
        {
            foreach (var kv in chain.Levels)
                levelChains[kv.Key] = kv.Value.OrderBy(e => e.Priority).ToList();
        }

        // Fill missing levels with defaults
        var levels = GetContentLevelsForType(libraryType);
        foreach (var level in levels)
        {
            if (!levelChains.ContainsKey(level))
            {
                levelChains[level] = ViewModel.MetadataProviders
                    .Select(p => new LibraryProviderChainEntry
                    {
                        ProviderId = p.Id,
                        ProviderSlug = p.Slug,
                        Enabled = true,
                        Priority = 0,
                    }).ToList();
            }
        }

        DispatcherQueue.TryEnqueue(() => RebuildProviderSection(container, levelChains, levels, onDirty));
    }

    private Task LoadDefaultProvidersAsync(
        string libraryType, StackPanel container,
        Dictionary<string, List<LibraryProviderChainEntry>> levelChains,
        Action onDirty)
    {
        levelChains.Clear();
        var levels = GetContentLevelsForType(libraryType);
        foreach (var level in levels)
        {
            levelChains[level] = ViewModel.MetadataProviders
                .Select(p => new LibraryProviderChainEntry
                {
                    ProviderId = p.Id,
                    ProviderSlug = p.Slug,
                    Enabled = true,
                    Priority = 0,
                }).ToList();
        }

        DispatcherQueue.TryEnqueue(() => RebuildProviderSection(container, levelChains, levels, onDirty));
        return Task.CompletedTask;
    }

    private void RebuildProviderSection(
        StackPanel container,
        Dictionary<string, List<LibraryProviderChainEntry>> levelChains,
        string[] levels,
        Action onDirty)
    {
        container.Children.Clear();

        if (levels.Length == 0 || levelChains.Count == 0)
        {
            container.Children.Add(new TextBlock
            {
                Text = "No metadata providers configured.",
                FontSize = 12,
                Foreground = _tertiaryText,
            });
            return;
        }

        foreach (var level in levels)
        {
            if (!levelChains.TryGetValue(level, out var items) || items.Count == 0) continue;

            var levelPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };

            // Level label (collapsible)
            var levelLabel = new TextBlock
            {
                Text = char.ToUpper(level[0]) + level[1..],
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = _accentBrush,
                CharacterSpacing = 50,
            };
            levelPanel.Children.Add(levelLabel);

            // Provider items
            for (int i = 0; i < items.Count; i++)
            {
                var idx = i;
                var item = items[i];
                var capturedLevel = level;

                var itemRow = new Grid { ColumnSpacing = 4, Padding = new Thickness(8, 4, 8, 4) };
                itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // checkbox
                itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // slug
                itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // up/down
                itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // priority

                itemRow.Background = item.Enabled
                    ? new SolidColorBrush(Color.FromArgb(15, 144, 160, 181))
                    : new SolidColorBrush(Color.FromArgb(8, 144, 160, 181));
                itemRow.CornerRadius = new CornerRadius(6);
                itemRow.BorderBrush = item.Enabled
                    ? _borderBrush
                    : new SolidColorBrush(Color.FromArgb(40, 130, 130, 130));
                itemRow.BorderThickness = new Thickness(1);

                var cb = new CheckBox
                {
                    IsChecked = item.Enabled,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                cb.Checked += (_, _) => { items[idx].Enabled = true; onDirty(); };
                cb.Unchecked += (_, _) => { items[idx].Enabled = false; onDirty(); };
                Grid.SetColumn(cb, 0);
                itemRow.Children.Add(cb);

                var slug = new TextBlock
                {
                    Text = item.ProviderSlug,
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = item.Enabled ? _primaryText : _tertiaryText,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(slug, 1);
                itemRow.Children.Add(slug);

                var arrowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
                var upBtn = new Button
                {
                    Width = 20, Height = 20, MinWidth = 20, MinHeight = 20,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    IsEnabled = idx > 0,
                    Content = new FontIcon { Glyph = "\uE70E", FontSize = 9, Foreground = _tertiaryText },
                };
                upBtn.Click += (_, _) =>
                {
                    if (idx > 0)
                    {
                        (items[idx], items[idx - 1]) = (items[idx - 1], items[idx]);
                        onDirty();
                        RebuildProviderSection(container, levelChains, levels, onDirty);
                    }
                };
                arrowPanel.Children.Add(upBtn);

                var downBtn = new Button
                {
                    Width = 20, Height = 20, MinWidth = 20, MinHeight = 20,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    IsEnabled = idx < items.Count - 1,
                    Content = new FontIcon { Glyph = "\uE70D", FontSize = 9, Foreground = _tertiaryText },
                };
                downBtn.Click += (_, _) =>
                {
                    if (idx < items.Count - 1)
                    {
                        (items[idx], items[idx + 1]) = (items[idx + 1], items[idx]);
                        onDirty();
                        RebuildProviderSection(container, levelChains, levels, onDirty);
                    }
                };
                arrowPanel.Children.Add(downBtn);

                Grid.SetColumn(arrowPanel, 2);
                itemRow.Children.Add(arrowPanel);

                var priorityLabel = new TextBlock
                {
                    Text = (idx + 1).ToString(),
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = _tertiaryText,
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(priorityLabel, 3);
                itemRow.Children.Add(priorityLabel);

                levelPanel.Children.Add(itemRow);
            }

            container.Children.Add(levelPanel);
        }
    }

    private static string[] GetContentLevelsForType(string libraryType) => libraryType switch
    {
        "series" => ["series", "season", "episode"],
        "movies" => ["movie"],
        "audiobooks" => ["audiobook"],
        "ebooks" => ["ebook"],
        "podcasts" => ["podcast"],
        "mixed" => ["movie", "series", "season", "episode"],
        _ => [],
    };

    // ===================================================================
    //  Helpers
    // ===================================================================

    private TextBlock MakeFormLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = _secondaryText
    };

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}
