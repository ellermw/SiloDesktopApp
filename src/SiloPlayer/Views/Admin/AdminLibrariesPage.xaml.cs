using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

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
    private const int AMBIGUOUS_PAGE_SIZE = 10;
    private const int SKIPPED_ROOTS_PAGE_SIZE = 10;
    private const int ScanUiRefreshMs = 300;
    private const int ScanLibraryRowsRefreshMs = 1500;
    private const int ScanLibraryReloadMs = 10000;
    private const int MaxScanRowsInPopover = 25;
    private int _staleCurrentPage;
    private string _unmatchedFilter = "";
    private string _staleFilter = "";
    private string _ambiguousFilter = "";
    private int _ambiguousCurrentPage;
    private string _staleSortField = "last_seen";
    private bool _staleSortAscending;
    private int _skippedRootsCurrentPage;
    private string _skippedRootsSortField = "last_seen";
    private bool _skippedRootsSortAscending;

    public AdminLibrariesPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminLibrariesViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _scanUiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScanUiRefreshMs) };
        _scanUiRefreshTimer.Tick += ScanUiRefreshTimer_Tick;
        ScanQueueFlyout.Opened += (_, _) =>
        {
            _scanQueueFlyoutOpen = true;
            BuildScanQueuePopover(forceContent: true);
        };
        ScanQueueFlyout.Closed += (_, _) => _scanQueueFlyoutOpen = false;
    }

    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width);
        AdminPageContent.Width = Math.Min(1640, width);

        // AdminLayout.tsx: px-4 / sm:px-6 / lg:px-8 / xl:px-10 and a wrapping
        // page-header. The table itself uses overflow-x-auto in the WebUI.
        var horizontalPadding = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
        var verticalPadding = width >= 1024 ? 32 : 16;
        AdminPageContent.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, 40);
        var contentWidth = Math.Max(0, width - (horizontalPadding * 2));

        var wrapHeader = contentWidth < 1080;
        Grid.SetColumn(PageHeaderCopy, 0);
        Grid.SetColumnSpan(PageHeaderCopy, wrapHeader ? 2 : 1);
        Grid.SetColumn(PageHeaderActions, wrapHeader ? 0 : 1);
        Grid.SetColumnSpan(PageHeaderActions, wrapHeader ? 2 : 1);
        Grid.SetRow(PageHeaderActions, wrapHeader ? 1 : 0);
        PageHeaderActions.HorizontalAlignment = wrapHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PageTitleText.FontSize = contentWidth < 640 ? 32 : contentWidth < 1024 ? 40 : 48;
        PageTitleText.LineHeight = contentWidth < 640 ? 31 : contentWidth < 1024 ? 38 : 46;
    }

    // Event channel subscription for realtime refresh
    private IDisposable? _eventSubscription;
    private EventChannelClient? _eventChannel;
    private DateTime _lastEventRefresh = DateTime.MinValue;
    private DateTime _lastScanLibraryRowsRefresh = DateTime.MinValue;
    private DispatcherTimer? _scanUiRefreshTimer;
    private bool _scanQueueFlyoutOpen;
    private DispatcherTimer? _statusHideTimer;
    private DispatcherTimer? _unmatchedSearchTimer;

    private void UnmatchedHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ToggleDiagnosticsSection(UnmatchedContent);
        UpdateDiagnosticsHeader(UnmatchedContent, UnmatchedCountBadge, UnmatchedChevron, ViewModel.UnmatchedTotal);
    }

    private void AmbiguousHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ToggleDiagnosticsSection(AmbiguousContent);
        UpdateDiagnosticsHeader(AmbiguousContent, AmbiguousCountBadge, AmbiguousChevron, ViewModel.AmbiguousRoots.Count);
    }

    private void StaleHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ToggleDiagnosticsSection(StaleContent);
        UpdateDiagnosticsHeader(StaleContent, StaleCountBadge, StaleChevron, ViewModel.StaleIds.Count);
    }

    private void SkippedHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        SkippedContent.Visibility = SkippedContent.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateSkippedDiagnosticsHeader();
        BuildSkippedRootsRows();
    }

    private void UpdateSkippedDiagnosticsHeader()
    {
        var expanded = SkippedContent.Visibility == Visibility.Visible;
        SkippedChevron.Glyph = expanded ? "\uE70D" : "\uE76C";
        SkippedCountBadge.Child = expanded
            ? MakeBadge($"{ViewModel.SkippedRoots.Count}", "secondary")
            : new TextBlock
            {
                Text = ViewModel.SkippedRoots.Count.ToString("N0"),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = _primaryText,
                VerticalAlignment = VerticalAlignment.Center,
            };
    }

    private static void ToggleDiagnosticsSection(StackPanel content)
        => content.Visibility = content.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void UpdateDiagnosticsHeader(
        StackPanel content,
        Border countHost,
        FontIcon chevron,
        long count)
    {
        var expanded = content.Visibility == Visibility.Visible;
        chevron.Glyph = expanded ? "\uE70D" : "\uE76C";
        countHost.Child = expanded
            ? MakeBadge($"{count:N0}", "secondary")
            : new TextBlock
            {
                Text = count.ToString("N0"),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = _primaryText,
                VerticalAlignment = VerticalAlignment.Center,
            };
    }

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
        if (ViewModel.Libraries.Count > 0)
        {
            // A cached page should paint its last complete snapshot immediately;
            // the server refresh below reconciles it without returning to a blank
            // table or skeleton-only state on every admin navigation.
            BuildLibraryRows();
            BuildScanQueuePopover();
            BuildUnmatchedItemsSection();
            BuildAmbiguousRootsSection();
            BuildSkippedRootsRows();
            BuildStaleIdsSection();
        }
        else
        {
            BuildLibraryLoadingRows();
        }

        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventChannel.SnapshotReceived += OnSnapshotReceived;
            _eventChannel.EventReceived += OnEventReceived;
            _eventSubscription = _eventChannel.Subscribe("scans");
            if (_eventChannel.TryGetLatestSnapshot("scans", out var cachedScans))
                OnSnapshotReceived("scans", cachedScans);
        }
        catch { }

        try
        {
            // The WebUI starts its independent diagnostics queries together.
            // Start them before the library request completes so the lower
            // sections do not appear an extra API round-trip after the table.
            var skippedRootsTask = LoadAndRenderAsync(ViewModel.LoadSkippedRootsAsync, BuildSkippedRootsRows);
            var unmatchedItemsTask = LoadAndRenderAsync(
                () => ViewModel.LoadUnmatchedItemsAsync(),
                BuildUnmatchedItemsSection);
            var staleIdsTask = LoadAndRenderAsync(ViewModel.LoadStaleIdsAsync, BuildStaleIdsSection);
            var metadataProvidersTask = ViewModel.LoadMetadataProvidersAsync();
            var activeRefreshJobsTask = ViewModel.LoadActiveRefreshJobsAsync();

            await ViewModel.LoadLibrariesAsync();
            // Rebuild ONCE after all data is loaded — no CollectionChanged subscriptions needed for initial load
            BuildLibraryRows();
            BuildScanQueuePopover();
            BuildAmbiguousRootsSection();

            async Task LoadAndRenderAsync(Func<Task> load, Action render)
            {
                await load();
                render();
            }

            await activeRefreshJobsTask;
            BuildLibraryRows();
            await Task.WhenAll(
                skippedRootsTask,
                unmatchedItemsTask,
                staleIdsTask,
                metadataProvidersTask);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }

    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _loaded = false;
        _scanUiRefreshTimer?.Stop();
        if (_eventChannel != null)
        {
            _eventChannel.SnapshotReceived -= OnSnapshotReceived;
            _eventChannel.EventReceived -= OnEventReceived;
        }
        _eventSubscription?.Dispose();
        _eventSubscription = null;
    }

    private static readonly JsonSerializerOptions _scanJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private void OnSnapshotReceived(string channel, JsonElement data)
    {
        if (channel != "scans" || data.ValueKind != JsonValueKind.Array) return;
        try
        {
            var scans = data.Deserialize<List<AdminScanRun>>(_scanJsonOpts) ?? [];
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.ActiveScans = scans.Where(IsActiveScan).ToList();
                ScheduleScanUiRefresh();
            });
        }
        catch { }
    }

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
            else if (data.ValueKind == JsonValueKind.Object)
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
        // The current scans snapshot uses "queued" for work waiting on scanner
        // capacity.  Those entries are active work in the WebUI: they contribute
        // to the page-level count, the per-library queued badge, and the inline
        // queue rows.  Keep "accepted" for compatibility with older servers.
        => scan.Status is "accepted" or "queued" or "running";

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

        if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        if (ViewModel.Libraries.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        foreach (var lib in ViewModel.Libraries)
        {
            LibrariesPanel.Children.Add(BuildLibraryRow(lib));
            var activeScans = ViewModel.ActiveScans.Where(scan => scan.LibraryId == lib.Id).ToList();
            var activeRefreshJob = FindActiveRefreshJob(lib.Id);
            if (activeScans.Count > 0 || activeRefreshJob != null)
                LibrariesPanel.Children.Add(BuildLibraryActiveWorkRow(lib, activeScans, activeRefreshJob));
        }

        // The WebUI keeps warnings in a distinct block below the library/work rows.
        foreach (var lib in ViewModel.Libraries.Where(library =>
                     library.ScanWarningCode is "empty_root" or "dead_root"))
            LibrariesPanel.Children.Add(BuildEmptyRootWarningRow(lib));
    }

    private void BuildLibraryLoadingRows()
    {
        LibrariesPanel.Children.Clear();
        EmptyState.Visibility = Visibility.Collapsed;

        // Match the WebUI table skeleton so navigation never presents a large
        // empty body while the primary libraries request is in flight.
        for (var i = 0; i < 6; i++)
        {
            var row = new Grid
            {
                Height = 46,
                Padding = new Thickness(12, 8, 12, 8),
                ColumnSpacing = 16,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

            foreach (var (column, width) in new[] { (1, 112d), (2, 210d), (3, 54d), (4, 62d), (5, 92d), (6, 132d) })
            {
                var bar = new Border
                {
                    Width = width,
                    Height = column is 3 or 4 ? 18 : 10,
                    CornerRadius = new CornerRadius(column is 3 or 4 ? 7 : 5),
                    Background = _surfaceBrush,
                    Opacity = 0.72,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(bar, column);
                row.Children.Add(bar);
            }

            LibrariesPanel.Children.Add(new Border
            {
                Child = row,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
            });
        }
    }

    private FrameworkElement BuildLibraryRow(Library lib)
    {
        var row = new Grid
        {
            Padding = new Thickness(12, 8, 12, 8),
            ColumnSpacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

        // ---- Col 0: drag handle — now functional with move up/down ----
        var capturedForReorder = lib;
        var dragGrip = new TextBlock
        {
            Text = "⠿",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 14,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            CanDrag = true,
        };
        ToolTipService.SetToolTip(dragGrip, $"Drag {lib.Name}");
        dragGrip.DragStarting += (_, args) =>
        {
            args.Data.SetText(capturedForReorder.Id.ToString());
            args.Data.RequestedOperation = DataPackageOperation.Move;
        };
        row.AllowDrop = true;
        row.DragOver += (_, args) =>
        {
            if (args.DataView.Contains(StandardDataFormats.Text))
                args.AcceptedOperation = DataPackageOperation.Move;
        };
        row.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            var sourceText = await args.DataView.GetTextAsync();
            if (int.TryParse(sourceText, out var sourceId))
                await MoveLibraryToAsync(sourceId, capturedForReorder.Id);
        };
        Grid.SetColumn(dragGrip, 0);
        row.Children.Add(dragGrip);

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
        if (lib.ScanWarningCode == "dead_root")
            statusColumn.Children.Add(MakeBadge("Root unreachable", "destructive"));
        Grid.SetColumn(statusColumn, 4);
        row.Children.Add(statusColumn);

        // ---- Col 5: Last Scanned ----
        var lastScannedColumn = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        string lastScannedText = "Never";
        if (!string.IsNullOrEmpty(lib.LastScannedAt) &&
            DateTimeOffset.TryParse(lib.LastScannedAt, out var lastScanned))
        {
            lastScannedText = DateTimeDisplay.FormatDateTime(lastScanned);
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
        var mountBtn = MakeSymbolButton28(Symbol.Repair, "Verify Mounts");
        mountBtn.Click += async (_, _) =>
        {
            await ViewModel.CheckMountCommand.ExecuteAsync(capturedLib.Id);
            DispatcherQueue.TryEnqueue(RebuildAll);
        };

        // Scan/stop is one stateful control, matching the current WebUI.
        var scanBtn = MakeSymbolButton28(
            libScans.Count > 0 ? Symbol.Stop : Symbol.Refresh,
            libScans.Count > 0 ? "Stop Library Scans" : "Scan Library",
            libScans.Count > 0 ? DestructiveColor : null);
        scanBtn.Click += async (_, _) =>
        {
            scanBtn.IsEnabled = false;
            if (libScans.Count > 0)
                await ViewModel.CancelLibraryScansCommand.ExecuteAsync(capturedLib.Id);
            else
                await ViewModel.ScanLibraryCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? (libScans.Count > 0 ? "Scan cancellation requested." : "Scan started."));
            scanBtn.IsEnabled = true;
        };
        actionsPanel.Children.Add(scanBtn);

        var activeRefreshJob = FindActiveRefreshJob(lib.Id);
        // Refresh/stop is one stateful control, matching the current WebUI.
        var refreshBtn = MakeSymbolButton28(
            activeRefreshJob != null ? Symbol.Stop : Symbol.Save,
            activeRefreshJob != null ? "Stop Metadata Refresh" : "Rescan Metadata",
            activeRefreshJob != null ? DestructiveColor : null);
        refreshBtn.Click += async (_, _) =>
        {
            refreshBtn.IsEnabled = false;
            if (activeRefreshJob != null)
                await ViewModel.CancelRefreshJobAsync(activeRefreshJob.Id);
            else
                await ViewModel.RefreshMetadataCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? (activeRefreshJob != null ? "Metadata refresh cancellation requested." : "Refresh started."));
            refreshBtn.IsEnabled = true;
        };
        actionsPanel.Children.Add(refreshBtn);
        actionsPanel.Children.Add(mountBtn);

        // Edit
        var editBtn = MakeSymbolButton28(Symbol.Edit, "Edit library");
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedLib);
        actionsPanel.Children.Add(editBtn);

        // Delete
        var deleteBtn = MakeSymbolButton28(Symbol.Delete, "Delete library");
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedLib);
        actionsPanel.Children.Add(deleteBtn);

        // The current WebUI places the guarded destructive confirmation last.
        if (lib.ScanWarningCode is "empty_root" or "dead_root")
        {
            var confirmCleanupBtn = MakeIconButton28("\uE74D", "Confirm cleanup for missing or empty roots", DestructiveColor);
            confirmCleanupBtn.Click += async (_, _) => await OpenConfirmEmptyRootDialogAsync(capturedLib);
            actionsPanel.Children.Add(confirmCleanupBtn);
        }

        actionsWrapper.Children.Add(actionsPanel);

        // Inline mount-check result
        if (ViewModel.MountCheckResults.TryGetValue(lib.Id, out var mountCheck))
            actionsWrapper.Children.Add(BuildMountCheckInlineResult(mountCheck, isWarningRow: false));

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

    private FrameworkElement BuildLibraryActiveWorkRow(Library lib, List<AdminScanRun> scans, AdminJob? refreshJob)
    {
        var workPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(52, 0, 12, 0),
        };

        if (refreshJob != null)
        {
            var refreshRow = new Grid { ColumnSpacing = 10 };
            refreshRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            refreshRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var copy = new StackPanel { Spacing = 2 };
            copy.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrWhiteSpace(refreshJob.Message) ? refreshJob.Message : "Metadata refresh queued",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = _primaryText,
            });
            if (refreshJob.ProgressTotal > 0)
            {
                copy.Children.Add(new TextBlock
                {
                    Text = $"Progress: {refreshJob.ProgressCurrent:N0} / {refreshJob.ProgressTotal:N0}",
                    FontSize = 11,
                    Foreground = _tertiaryText,
                });
            }
            refreshRow.Children.Add(copy);

            var stopRefresh = MakeSymbolButton28(Symbol.Stop, "Stop Metadata Refresh", DestructiveColor);
            stopRefresh.Click += async (_, _) =>
            {
                stopRefresh.IsEnabled = false;
                await ViewModel.CancelRefreshJobAsync(refreshJob.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Metadata refresh cancellation requested.");
            };
            Grid.SetColumn(stopRefresh, 1);
            refreshRow.Children.Add(stopRefresh);
            workPanel.Children.Add(refreshRow);
        }

        foreach (var scan in scans)
        {
            // WebUI LibraryScanTaskRow: stop control, scan glyph, then the
            // status/detail/progress copy.  Keeping this hierarchy also makes
            // queued and running work visually distinguishable at a glance.
            var scanRow = new Grid { ColumnSpacing = 6 };
            scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var stopScan = MakeSymbolButton28(Symbol.Stop, "Cancel library scans", DestructiveColor);
            stopScan.Width = 20;
            stopScan.Height = 20;
            stopScan.MinWidth = 20;
            stopScan.MinHeight = 20;
            stopScan.Padding = new Thickness(0);
            stopScan.VerticalAlignment = VerticalAlignment.Top;
            stopScan.Click += async (_, _) =>
            {
                stopScan.IsEnabled = false;
                await ViewModel.CancelLibraryScansCommand.ExecuteAsync(lib.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Scan cancellation requested.");
            };
            scanRow.Children.Add(stopScan);

            var scanGlyph = new SymbolIcon(Symbol.Refresh)
            {
                Width = 14,
                Height = 14,
                Foreground = _secondaryText,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 0, 0),
            };
            Grid.SetColumn(scanGlyph, 1);
            scanRow.Children.Add(scanGlyph);

            var copy = new StackPanel { Spacing = 2 };
            var progress = FormatActiveScanProgress(scan);
            var headline = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            headline.Children.Add(new TextBlock
            {
                Text = "Scan",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = _primaryText,
            });
            headline.Children.Add(new TextBlock
            {
                Text = scan.Status == "running" ? "Running" : "Queued",
                FontSize = 12,
                Foreground = _secondaryText,
            });
            copy.Children.Add(headline);

            var detail = FormatActiveScanMode(scan);
            detail += !string.IsNullOrWhiteSpace(scan.Path) ? $" \u00b7 {scan.Path}" : " \u00b7 Entire library";
            copy.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 10,
                Foreground = _tertiaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrWhiteSpace(progress))
            {
                copy.Children.Add(new TextBlock
                {
                    Text = progress,
                    FontSize = 10,
                    Foreground = _tertiaryText,
                    Opacity = 0.8,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            Grid.SetColumn(copy, 2);
            scanRow.Children.Add(copy);
            workPanel.Children.Add(scanRow);
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 10, 0, 10),
            Child = workPanel,
        };
    }

    private AdminJob? FindActiveRefreshJob(int libraryId)
        => ViewModel.ActiveRefreshJobs.FirstOrDefault(job =>
        {
            if (job.RequestPayload == null || !job.RequestPayload.TryGetValue("library_id", out var value))
                return false;
            if (value is JsonElement json && json.ValueKind == JsonValueKind.Number)
                return json.TryGetInt32(out var id) && id == libraryId;
            return value is int idValue && idValue == libraryId;
        });

    // ===================================================================
    //  Library Reorder (Move Up/Down approach — WinUI 3 lacks built-in
    //  drag-reorder for arbitrary Grid children. Move buttons provide the
    //  same functionality as the webui's DnD reorder.)
    // ===================================================================

    private async Task MoveLibraryToAsync(int sourceId, int targetId)
    {
        var list = ViewModel.Libraries.ToList();
        var sourceIndex = list.FindIndex(library => library.Id == sourceId);
        var targetIndex = list.FindIndex(library => library.Id == targetId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

        var moved = list[sourceIndex];
        list.RemoveAt(sourceIndex);
        list.Insert(targetIndex, moved);

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
    //  Guarded Root Warning Row (colSpan=6, bg-destructive/5)
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

        var deadRoot = lib.ScanWarningCode == "dead_root";
        content.Children.Add(new TextBlock
        {
            Text = deadRoot
                ? "One or more library roots are unreachable or mounted but returned no files. Their files are hidden, but nothing will be deleted until the root is back or cleanup is confirmed."
                : "Scan found 0 media files for this library. Cleanup was paused to avoid accidental deletion.",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(DestructiveColor),
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = lib.ScanWarningMessage
                   ?? (deadRoot
                       ? "Run another scan after storage returns, or use Check Mount to verify connectivity."
                       : "Run another scan after storage returns, or confirm deletion before the next empty-root scan."),
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

        var warningActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        warningActions.Children.Add(checkMountBtn);

        if (deadRoot)
        {
            var confirmCleanupBtn = new Button
            {
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Padding = new Thickness(10, 6, 10, 6),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE74D", FontSize = 13 },
                        new TextBlock { Text = "Confirm Cleanup", FontSize = 12 },
                    },
                },
            };
            confirmCleanupBtn.Click += async (_, _) => await OpenConfirmEmptyRootDialogAsync(capturedLib);
            warningActions.Children.Add(confirmCleanupBtn);
        }

        content.Children.Add(warningActions);

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
            foreach (var root in result.Roots.Where(r => !r.Reachable || r.SuspectEmpty))
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
                else if (root.SuspectEmpty)
                {
                    rootLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text = ": mounted, but the root returned no files"
                    });
                }
                stack.Children.Add(rootLine);
            }
        }

        string checkedText = "Checked ";
        if (DateTimeOffset.TryParse(result.CheckedAt, out var checkedAt))
            checkedText += DateTimeDisplay.FormatDateTime(checkedAt);
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
        headerLeft.Children.Add(new SymbolIcon { Symbol = Symbol.Refresh, Foreground = _accentBrush });
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
        UpdateDiagnosticsHeader(UnmatchedContent, UnmatchedCountBadge, UnmatchedChevron, ViewModel.UnmatchedTotal);

        BuildUnmatchedTable();
        BuildUnmatchedPagination();
    }

    private void BuildUnmatchedTable()
    {
        UnmatchedTablePanel.Children.Clear();

        var items = ViewModel.UnmatchedItems;

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
                Text = string.IsNullOrWhiteSpace(_unmatchedFilter) ? "No unmatched items on this page." : "No unmatched items match your search.",
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
            await ViewModel.LoadUnmatchedItemsAsync(_unmatchedFilter);
            BuildUnmatchedItemsSection();
        }));
        UnmatchedPaginationPanel.Children.Add(MakePaginationButton("\uE76B", page > 0, async () =>
        {
            ViewModel.UnmatchedPage = Math.Max(0, page - 1);
            await ViewModel.LoadUnmatchedItemsAsync(_unmatchedFilter);
            BuildUnmatchedItemsSection();
        }));
        UnmatchedPaginationPanel.Children.Add(MakePaginationButton("\uE76C", page < totalPages - 1, async () =>
        {
            ViewModel.UnmatchedPage = Math.Min(totalPages - 1, page + 1);
            await ViewModel.LoadUnmatchedItemsAsync(_unmatchedFilter);
            BuildUnmatchedItemsSection();
        }));
        UnmatchedPaginationPanel.Children.Add(MakePaginationButton("\uE893", page < totalPages - 1, async () =>
        {
            ViewModel.UnmatchedPage = totalPages - 1;
            await ViewModel.LoadUnmatchedItemsAsync(_unmatchedFilter);
            BuildUnmatchedItemsSection();
        }));
    }

    private void UnmatchedSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _unmatchedFilter = (sender as TextBox)?.Text ?? "";
        _unmatchedSearchTimer?.Stop();
        _unmatchedSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _unmatchedSearchTimer.Tick += async (_, _) =>
        {
            _unmatchedSearchTimer?.Stop();
            ViewModel.UnmatchedPage = 0;
            await ViewModel.LoadUnmatchedItemsAsync(_unmatchedFilter);
            BuildUnmatchedItemsSection();
        };
        _unmatchedSearchTimer.Start();
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
        UpdateDiagnosticsHeader(StaleContent, StaleCountBadge, StaleChevron, ViewModel.StaleIds.Count);

        BuildStaleTable();
        BuildStalePagination();
    }

    private void BuildStaleTable()
    {
        StaleTablePanel.Children.Clear();
        var filteredList = GetFilteredSortedStaleIds();

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

        AddSortableHeaderCell(header, 0, "Title", "title", _staleSortField, _staleSortAscending, SetStaleSort);
        AddSortableHeaderCell(header, 1, "Year", "year", _staleSortField, _staleSortAscending, SetStaleSort);
        AddSortableHeaderCell(header, 2, "Library", "library", _staleSortField, _staleSortAscending, SetStaleSort);
        AddSortableHeaderCell(header, 3, "Provider", "provider", _staleSortField, _staleSortAscending, SetStaleSort);
        AddHeaderCell(header, 4, "Provider ID");
        AddSortableHeaderCell(header, 5, "First Seen", "first_seen", _staleSortField, _staleSortAscending, SetStaleSort);
        AddSortableHeaderCell(header, 6, "Last Seen", "last_seen", _staleSortField, _staleSortAscending, SetStaleSort);
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

            string firstSeen = DateTimeOffset.TryParse(s.FirstSeenAt, out var fs) ? DateTimeDisplay.FormatDateTime(fs) : "";
            string lastSeen = DateTimeOffset.TryParse(s.LastSeenAt, out var ls) ? DateTimeDisplay.FormatDateTime(ls) : "";
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
        var total = GetFilteredSortedStaleIds().Count;
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

    private List<StaleMediaId> GetFilteredSortedStaleIds()
    {
        var items = ViewModel.StaleIds.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(_staleFilter))
        {
            var q = _staleFilter.Trim();
            items = items.Where(s =>
                s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.ProviderId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Provider.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.LibraryName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return (_staleSortField, _staleSortAscending) switch
        {
            ("title", true) => items.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            ("title", false) => items.OrderByDescending(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            ("year", true) => items.OrderBy(s => s.Year).ToList(),
            ("year", false) => items.OrderByDescending(s => s.Year).ToList(),
            ("library", true) => items.OrderBy(s => s.LibraryName, StringComparer.OrdinalIgnoreCase).ToList(),
            ("library", false) => items.OrderByDescending(s => s.LibraryName, StringComparer.OrdinalIgnoreCase).ToList(),
            ("provider", true) => items.OrderBy(s => s.Provider, StringComparer.OrdinalIgnoreCase).ToList(),
            ("provider", false) => items.OrderByDescending(s => s.Provider, StringComparer.OrdinalIgnoreCase).ToList(),
            ("first_seen", true) => items.OrderBy(s => s.FirstSeenAt, StringComparer.Ordinal).ToList(),
            ("first_seen", false) => items.OrderByDescending(s => s.FirstSeenAt, StringComparer.Ordinal).ToList(),
            ("last_seen", true) => items.OrderBy(s => s.LastSeenAt, StringComparer.Ordinal).ToList(),
            _ => items.OrderByDescending(s => s.LastSeenAt, StringComparer.Ordinal).ToList(),
        };
    }

    private void SetStaleSort(string field)
    {
        if (_staleSortField == field)
            _staleSortAscending = !_staleSortAscending;
        else
        {
            _staleSortField = field;
            _staleSortAscending = false;
        }
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
        UpdateDiagnosticsHeader(AmbiguousContent, AmbiguousCountBadge, AmbiguousChevron, ViewModel.AmbiguousRoots.Count);

        BuildAmbiguousTable();
    }

    private void BuildAmbiguousTable()
    {
        AmbiguousTablePanel.Children.Clear();
        AmbiguousPaginationPanel.Children.Clear();
        AmbiguousPaginationPanel.Visibility = Visibility.Collapsed;

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
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)filteredList.Count / AMBIGUOUS_PAGE_SIZE));
        _ambiguousCurrentPage = Math.Clamp(_ambiguousCurrentPage, 0, totalPages - 1);
        var pageItems = filteredList
            .Skip(_ambiguousCurrentPage * AMBIGUOUS_PAGE_SIZE)
            .Take(AMBIGUOUS_PAGE_SIZE)
            .ToList();

        // Table header
        var header = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); // Root
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });                   // Type
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });                   // Confidence
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });                   // Files
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });                  // Actions

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

        foreach (var root in pageItems)
        {
            var row = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

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

            // Actions match the WebUI: Override is always available and Resolve
            // links to the matched item when the server supplied content_id.
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
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            actionPanel.Children.Add(overrideBtn);
            if (!string.IsNullOrWhiteSpace(root.ContentId))
            {
                var resolveButton = new Button
                {
                    Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                    Padding = new Thickness(8, 4, 8, 4),
                    MinHeight = 28,
                    VerticalAlignment = VerticalAlignment.Center,
                    Content = new TextBlock { Text = "Resolve", FontSize = 11 },
                };
                var contentId = root.ContentId;
                ToolTipService.SetToolTip(resolveButton, "Open the matched item; use Split Versions to separate wrongly merged files");
                resolveButton.Click += (_, _) => Frame.Navigate(typeof(SiloPlayer.Views.ItemDetailPage), contentId);
                actionPanel.Children.Add(resolveButton);
            }
            Grid.SetColumn(actionPanel, 4);
            row.Children.Add(actionPanel);

            var rowBorder = new Border
            {
                Child = row,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Opacity = 0.7,
            };
            AmbiguousTablePanel.Children.Add(rowBorder);
        }

        BuildAmbiguousPagination(filteredList.Count, totalPages);
    }

    private void BuildAmbiguousPagination(int total, int totalPages)
    {
        AmbiguousPaginationPanel.Children.Clear();
        if (total <= AMBIGUOUS_PAGE_SIZE)
        {
            AmbiguousPaginationPanel.Visibility = Visibility.Collapsed;
            return;
        }

        AmbiguousPaginationPanel.Visibility = Visibility.Visible;
        var page = _ambiguousCurrentPage;
        var rangeStart = page * AMBIGUOUS_PAGE_SIZE + 1;
        var rangeEnd = Math.Min((page + 1) * AMBIGUOUS_PAGE_SIZE, total);
        AmbiguousPaginationPanel.Children.Add(new TextBlock
        {
            Text = $"{rangeStart}\u2013{rangeEnd} of {total:N0}",
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        AmbiguousPaginationPanel.Children.Add(MakePaginationButton("\uE892", page > 0, () =>
        {
            _ambiguousCurrentPage = 0;
            BuildAmbiguousTable();
            return Task.CompletedTask;
        }));
        AmbiguousPaginationPanel.Children.Add(MakePaginationButton("\uE76B", page > 0, () =>
        {
            _ambiguousCurrentPage = Math.Max(0, page - 1);
            BuildAmbiguousTable();
            return Task.CompletedTask;
        }));
        AmbiguousPaginationPanel.Children.Add(MakePaginationButton("\uE76C", page < totalPages - 1, () =>
        {
            _ambiguousCurrentPage = Math.Min(totalPages - 1, page + 1);
            BuildAmbiguousTable();
            return Task.CompletedTask;
        }));
        AmbiguousPaginationPanel.Children.Add(MakePaginationButton("\uE893", page < totalPages - 1, () =>
        {
            _ambiguousCurrentPage = totalPages - 1;
            BuildAmbiguousTable();
            return Task.CompletedTask;
        }));
    }

    private async void AmbiguousLibraryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AmbiguousLibraryCombo.SelectedItem is ComboBoxItem item && item.Tag is int libraryId)
        {
            _ambiguousCurrentPage = 0;
            await ViewModel.LoadAmbiguousRootsAsync(libraryId);
            BuildAmbiguousRootsSection();
        }
    }

    private void AmbiguousSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ambiguousFilter = (sender as TextBox)?.Text ?? "";
        _ambiguousCurrentPage = 0;
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
                await ViewModel.LoadUnmatchedItemsAsync(_unmatchedFilter);
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
        _skippedRootsCurrentPage = 0;
        BuildSkippedRootsRows();
    }

    private void SkippedItemHeader_Click(object sender, RoutedEventArgs e) => SetSkippedRootsSort("root_path");
    private void SkippedLibraryHeader_Click(object sender, RoutedEventArgs e) => SetSkippedRootsSort("library");
    private void SkippedReasonHeader_Click(object sender, RoutedEventArgs e) => SetSkippedRootsSort("reason");
    private void SkippedFirstSeenHeader_Click(object sender, RoutedEventArgs e) => SetSkippedRootsSort("first_seen");
    private void SkippedLastSeenHeader_Click(object sender, RoutedEventArgs e) => SetSkippedRootsSort("last_seen");

    private void SetSkippedRootsSort(string field)
    {
        if (_skippedRootsSortField == field)
            _skippedRootsSortAscending = !_skippedRootsSortAscending;
        else
        {
            _skippedRootsSortField = field;
            _skippedRootsSortAscending = false;
        }
        _skippedRootsCurrentPage = 0;
        UpdateSkippedSortHeaders();
        BuildSkippedRootsRows();
    }

    private void UpdateSkippedSortHeaders()
    {
        string Label(string title, string field) => _skippedRootsSortField == field
            ? $"{title} {(_skippedRootsSortAscending ? "\u2191" : "\u2193")}" : title;
        SkippedItemHeaderText.Text = Label("Item", "root_path");
        SkippedLibraryHeaderText.Text = Label("Library", "library");
        SkippedReasonHeaderText.Text = Label("Reason", "reason");
        SkippedFirstSeenHeaderText.Text = Label("First Seen", "first_seen");
        SkippedLastSeenHeaderText.Text = Label("Last Seen", "last_seen");
    }

    private void BuildSkippedRootsRows()
    {
        SkippedRootsPanel.Children.Clear();
        SkippedRootsPaginationPanel.Children.Clear();
        SkippedRootsPaginationPanel.Visibility = Visibility.Collapsed;

        if (ViewModel.SkippedRoots.Count == 0 && string.IsNullOrWhiteSpace(ViewModel.SkippedRootsError))
        {
            SkippedRootsSection.Visibility = Visibility.Collapsed;
            return;
        }

        SkippedRootsSection.Visibility = Visibility.Visible;
        UpdateSkippedDiagnosticsHeader();

        // The WebUI keeps this section collapsed by default. More importantly, do
        // not create thousands of WinUI controls for data the user cannot see.
        if (SkippedContent.Visibility != Visibility.Visible)
            return;

        if (!string.IsNullOrWhiteSpace(ViewModel.SkippedRootsError))
        {
            SkippedRootsPanel.Children.Add(new TextBlock
            {
                Text = $"Unable to load troubleshooting roots: {ViewModel.SkippedRootsError}",
                FontSize = 12,
                Foreground = new SolidColorBrush(DestructiveColor),
                Margin = new Thickness(4, 8, 4, 8),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        // Apply search filter
        var filtered = ViewModel.SkippedRoots.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(_skippedRootsSearch))
        {
            var q = _skippedRootsSearch.ToLowerInvariant();
            filtered = filtered.Where(r =>
                (r.RootPath?.ToLowerInvariant().Contains(q) == true) ||
                (r.LibraryName?.ToLowerInvariant().Contains(q) == true) ||
                (r.Reason?.ToLowerInvariant().Contains(q) == true) ||
                (r.SampleFilePath?.ToLowerInvariant().Contains(q) == true));
        }

        // Match the WebUI's sortable headers and default last-seen descending order.
        var rows = (_skippedRootsSortField, _skippedRootsSortAscending) switch
        {
            ("root_path", true) => filtered.OrderBy(r => r.RootPath, StringComparer.OrdinalIgnoreCase).ToList(),
            ("root_path", false) => filtered.OrderByDescending(r => r.RootPath, StringComparer.OrdinalIgnoreCase).ToList(),
            ("library", true) => filtered.OrderBy(r => r.LibraryName, StringComparer.OrdinalIgnoreCase).ToList(),
            ("library", false) => filtered.OrderByDescending(r => r.LibraryName, StringComparer.OrdinalIgnoreCase).ToList(),
            ("reason", true) => filtered.OrderBy(r => r.Reason, StringComparer.OrdinalIgnoreCase).ToList(),
            ("reason", false) => filtered.OrderByDescending(r => r.Reason, StringComparer.OrdinalIgnoreCase).ToList(),
            ("first_seen", true) => filtered.OrderBy(r => r.FirstSeenAt, StringComparer.Ordinal).ToList(),
            ("first_seen", false) => filtered.OrderByDescending(r => r.FirstSeenAt, StringComparer.Ordinal).ToList(),
            ("last_seen", true) => filtered.OrderBy(r => r.LastSeenAt, StringComparer.Ordinal).ToList(),
            _ => filtered.OrderByDescending(r => r.LastSeenAt, StringComparer.Ordinal).ToList(),
        };
        var total = rows.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / SKIPPED_ROOTS_PAGE_SIZE));
        _skippedRootsCurrentPage = Math.Clamp(_skippedRootsCurrentPage, 0, totalPages - 1);

        bool isFirst = true;
        foreach (var root in rows
            .Skip(_skippedRootsCurrentPage * SKIPPED_ROOTS_PAGE_SIZE)
            .Take(SKIPPED_ROOTS_PAGE_SIZE))
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

        BuildSkippedRootsPagination(total, totalPages);
    }

    private void BuildSkippedRootsPagination(int total, int totalPages)
    {
        SkippedRootsPaginationPanel.Children.Clear();
        if (total <= SKIPPED_ROOTS_PAGE_SIZE)
        {
            SkippedRootsPaginationPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SkippedRootsPaginationPanel.Visibility = Visibility.Visible;
        var page = _skippedRootsCurrentPage;
        var rangeStart = total == 0 ? 0 : page * SKIPPED_ROOTS_PAGE_SIZE + 1;
        var rangeEnd = Math.Min((page + 1) * SKIPPED_ROOTS_PAGE_SIZE, total);
        SkippedRootsPaginationPanel.Children.Add(new TextBlock
        {
            Text = $"{rangeStart}\u2013{rangeEnd} of {total:N0}",
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        SkippedRootsPaginationPanel.Children.Add(MakePaginationButton("\uE892", page > 0, () =>
        {
            _skippedRootsCurrentPage = 0;
            BuildSkippedRootsRows();
            return Task.CompletedTask;
        }));
        SkippedRootsPaginationPanel.Children.Add(MakePaginationButton("\uE76B", page > 0, () =>
        {
            _skippedRootsCurrentPage = Math.Max(0, page - 1);
            BuildSkippedRootsRows();
            return Task.CompletedTask;
        }));
        SkippedRootsPaginationPanel.Children.Add(MakePaginationButton("\uE76C", page < totalPages - 1, () =>
        {
            _skippedRootsCurrentPage = Math.Min(totalPages - 1, page + 1);
            BuildSkippedRootsRows();
            return Task.CompletedTask;
        }));
        SkippedRootsPaginationPanel.Children.Add(MakePaginationButton("\uE893", page < totalPages - 1, () =>
        {
            _skippedRootsCurrentPage = totalPages - 1;
            BuildSkippedRootsRows();
            return Task.CompletedTask;
        }));
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        var pathBlock = new TextBlock
        {
            Text = GetPathLeaf(root.RootPath),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
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

        var fileCountBlock = new TextBlock
        {
            Text = root.FileCount.ToString("N0"),
            FontSize = 12,
            Foreground = _tertiaryText,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        string firstSeenText = "";
        if (DateTimeOffset.TryParse(root.FirstSeenAt, out var firstSeen))
            firstSeenText = DateTimeDisplay.FormatDateTime(firstSeen);
        var firstSeenBlock = new TextBlock
        {
            Text = firstSeenText,
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        string lastSeenText = "";
        if (DateTimeOffset.TryParse(root.LastSeenAt, out var lastSeen))
            lastSeenText = DateTimeDisplay.FormatDateTime(lastSeen);
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
        Grid.SetColumn(fileCountBlock, 3);
        Grid.SetColumn(firstSeenBlock, 4);
        Grid.SetColumn(lastSeenBlock, 5);

        row.Children.Add(pathBlock);
        row.Children.Add(libNameBlock);
        row.Children.Add(reasonBadge);
        row.Children.Add(fileCountBlock);
        row.Children.Add(firstSeenBlock);
        row.Children.Add(lastSeenBlock);

        var chevron = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 11,
            Foreground = _tertiaryText,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var itemCell = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Remove(pathBlock);
        itemCell.Children.Add(chevron);
        itemCell.Children.Add(pathBlock);
        Grid.SetColumn(itemCell, 0);
        row.Children.Add(itemCell);

        var detail = BuildSkippedRootDetails(root);
        var container = new StackPanel();
        container.Children.Add(row);
        container.Children.Add(detail);
        row.Tapped += (_, _) =>
        {
            var expanded = detail.Visibility != Visibility.Visible;
            detail.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            chevron.Glyph = expanded ? "\uE70D" : "\uE76C";
        };
        return container;
    }

    private FrameworkElement BuildSkippedRootDetails(LibrarySkippedRoot root)
    {
        var grid = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(24, 144, 160, 181)),
            Padding = new Thickness(16, 12, 16, 12),
            ColumnSpacing = 16,
            RowSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddSkippedDetailRow(grid, 0, "Root path", root.RootPath, true);
        var row = 1;
        if (!string.IsNullOrWhiteSpace(root.SampleFilePath))
            AddSkippedDetailRow(grid, row++, "Sample file", root.SampleFilePath, true);
        AddSkippedDetailRow(grid, row, "Files affected", root.FileCount.ToString("N0"), false);
        return grid;
    }

    private void AddSkippedDetailRow(Grid grid, int row, string label, string value, bool monospace)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = _tertiaryText,
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = _primaryText,
            TextWrapping = TextWrapping.Wrap,
        };
        if (monospace)
            valueBlock.FontFamily = new FontFamily("Consolas");
        Grid.SetRow(labelBlock, row);
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
    }

    private static string GetPathLeaf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var trimmed = path.TrimEnd('/', '\\');
        var separator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
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

    private Button MakeSymbolButton28(Symbol symbol, string tooltip, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : _secondaryText;
        var button = new Button
        {
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new SymbolIcon(symbol) { Foreground = fg },
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
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

    private void AddSortableHeaderCell(
        Grid grid,
        int column,
        string title,
        string field,
        string activeField,
        bool ascending,
        Action<string> sort)
    {
        var label = new TextBlock
        {
            Text = activeField == field ? $"{title} {(ascending ? "\u2191" : "\u2193")}" : title,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button
        {
            Content = label,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        button.Click += (_, _) => sort(field);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
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
        ScanAllButton.IsEnabled = false;
        ScanAllLabel.Text = "Starting scans…";
        ShowStatus("Starting scans for all enabled libraries…", autoHide: false);
        try
        {
            await ViewModel.ScanAllCommand.ExecuteAsync(null);
            ShowStatus(ViewModel.ErrorMessage ?? ViewModel.StatusMessage ?? "Scan all libraries started.");
        }
        finally
        {
            ScanAllLabel.Text = "Scan All Libraries";
            ScanAllButton.IsEnabled = true;
        }
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

    private FrameworkElement BuildLibraryDialogTitle(Library? library)
    {
        var librarySymbol = library?.Type switch
        {
            "movies" or "series" => Symbol.Video,
            "audiobooks" => Symbol.Audio,
            "ebooks" => Symbol.PreviewLink,
            "manga" => Symbol.Library,
            "podcasts" => Symbol.Microphone,
            "mixed" => Symbol.AllApps,
            _ => Symbol.Library,
        };
        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xC0, 0x84, 0xFC)),
            Child = new SymbolIcon
            {
                Symbol = librarySymbol,
                Foreground = _accentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var copy = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = library != null ? "Edit Library" : "Add Library",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = _primaryText,
        });
        copy.Children.Add(new TextBlock
        {
            Text = library != null
                ? $"Configure how \u201c{library.Name}\u201d is scanned and matched."
                : "Set up a new library from folders on your server.",
            FontSize = 12,
            Foreground = _tertiaryText,
        });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(icon);
        header.Children.Add(copy);
        return header;
    }

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getBody, getProviderChain, getPosterFile) = BuildLibraryForm(null);
        var (dialog, submitButton, cancelButton, errorText) = BuildLibraryEditorDialog(null, formContent);
        submitButton.Click += async (_, _) =>
        {
            var body = getBody();
            if (body == null) return;
            submitButton.IsEnabled = false;
            submitButton.Content = "Creating\u2026";
            errorText.Visibility = Visibility.Collapsed;
            var created = await ViewModel.CreateLibraryForEditorAsync(body);
            if (created == null)
            {
                ShowLibraryEditorError(errorText, ViewModel.ErrorMessage ?? "Unable to create library.");
                submitButton.Content = "Create Library";
                submitButton.IsEnabled = true;
                return;
            }

            var chain = getProviderChain();
            if (chain != null && !await ViewModel.SetLibraryProvidersAsync(created.Id, chain))
            {
                ShowLibraryEditorError(errorText, ViewModel.ErrorMessage ?? "Library created, but provider priority could not be saved.");
                submitButton.Content = "Create Library";
                submitButton.IsEnabled = true;
                return;
            }

            var poster = getPosterFile();
            if (poster.Bytes != null && poster.Name != null && poster.ContentType != null)
            {
                try
                {
                    var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                    await adminApi.SetLibraryPosterAsync(created.Id, poster.Bytes, poster.Name, poster.ContentType);
                }
                catch (Exception ex)
                {
                    ShowLibraryEditorError(errorText, $"Library created, but poster upload failed: {ex.Message}");
                    submitButton.Content = "Create Library";
                    submitButton.IsEnabled = true;
                    return;
                }
            }
            ShowStatus(ViewModel.StatusMessage ?? "Library created.");
            dialog.Hide();
        };
        cancelButton.Click += (_, _) => dialog.Hide();

        await dialog.ShowAsync();
    }

    // ===================================================================
    //  Edit Dialog
    // ===================================================================

    private async Task OpenEditDialogAsync(Library lib)
    {
        var (formContent, getBody, getProviderChain, getPosterFile) = BuildLibraryForm(lib);
        var (dialog, submitButton, cancelButton, errorText) = BuildLibraryEditorDialog(lib, formContent);
        submitButton.Click += async (_, _) =>
        {
            var body = getBody();
            if (body == null) return;
            submitButton.IsEnabled = false;
            submitButton.Content = "Saving\u2026";
            errorText.Visibility = Visibility.Collapsed;
            if (!await ViewModel.UpdateLibraryForEditorAsync(lib.Id, body))
            {
                ShowLibraryEditorError(errorText, ViewModel.ErrorMessage ?? "Unable to save library.");
                submitButton.Content = "Save Changes";
                submitButton.IsEnabled = true;
                return;
            }

            var chain = getProviderChain();
            if (chain != null && !await ViewModel.SetLibraryProvidersAsync(lib.Id, chain))
            {
                ShowLibraryEditorError(errorText, ViewModel.ErrorMessage ?? "Library saved, but provider priority could not be saved.");
                submitButton.Content = "Save Changes";
                submitButton.IsEnabled = true;
                return;
            }

            var poster = getPosterFile();
            if (poster.Bytes != null && poster.Name != null && poster.ContentType != null)
            {
                try
                {
                    var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                    await adminApi.SetLibraryPosterAsync(lib.Id, poster.Bytes, poster.Name, poster.ContentType);
                }
                catch (Exception ex)
                {
                    ShowLibraryEditorError(errorText, $"Library saved, but poster upload failed: {ex.Message}");
                    submitButton.Content = "Save Changes";
                    submitButton.IsEnabled = true;
                    return;
                }
            }
            ShowStatus(ViewModel.StatusMessage ?? "Library updated.");
            dialog.Hide();
        };
        cancelButton.Click += (_, _) => dialog.Hide();

        await dialog.ShowAsync();
    }

    private (ContentDialog Dialog, Button SubmitButton, Button CancelButton, TextBlock ErrorText) BuildLibraryEditorDialog(
        Library? library, FrameworkElement formContent)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot };

        // WinUI's stock ContentDialog caps itself at roughly 548 px.  The
        // current WebUI editor is `sm:max-w-3xl` (768 px), with a 176 px
        // navigation rail.  Override both the local properties and theme
        // resources so the presenter cannot squeeze the form into slivers.
        dialog.Width = 768;
        dialog.MinWidth = 768;
        dialog.MaxWidth = 768;
        dialog.Resources["ContentDialogMinWidth"] = 768d;
        dialog.Resources["ContentDialogMaxWidth"] = 768d;

        var shell = new Grid { Width = 720 };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 16),
            Child = BuildLibraryDialogTitle(library),
        };
        Grid.SetRow(header, 0);
        shell.Children.Add(header);

        Grid.SetRow(formContent, 1);
        shell.Children.Add(formContent);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 7, 14, 7),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
        };
        var submitButton = new Button
        {
            Content = library != null ? "Save Changes" : "Create Library",
            Padding = new Thickness(14, 7, 14, 7),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        var footerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        footerButtons.Children.Add(cancelButton);
        footerButtons.Children.Add(submitButton);
        var errorText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(DestructiveColor),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        var footerGrid = new Grid { ColumnSpacing = 16 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.Children.Add(errorText);
        Grid.SetColumn(footerButtons, 1);
        footerGrid.Children.Add(footerButtons);
        var footer = new Border
        {
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 16, 0, 0),
            Child = footerGrid,
        };
        Grid.SetRow(footer, 2);
        shell.Children.Add(footer);
        dialog.Content = shell;

        return (dialog, submitButton, cancelButton, errorText);
    }

    private static void ShowLibraryEditorError(TextBlock errorText, string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
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
            Content = $"On the next scan of \"{lib.Name}\", remove items from roots that are reachable but still empty? Unreachable roots remain protected.",
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

    private async Task<IReadOnlyList<string>> BrowseServerFolderAsync(
        string initialPath,
        IReadOnlyCollection<string> existingPaths)
    {
        var existingPathSet = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentPath = !string.IsNullOrWhiteSpace(initialPath) && initialPath.Trim().StartsWith('/')
            ? initialPath.Trim()
            : "/";
        var pathBox = new TextBox { Text = currentPath, PlaceholderText = "/mnt/media", FontFamily = new FontFamily("Consolas"), FontSize = 13 };
        var currentLabel = new TextBlock { Text = currentPath, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = _secondaryText, TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { FontSize = 12, Foreground = _tertiaryText, TextWrapping = TextWrapping.Wrap };
        var selectionStatus = new TextBlock { FontSize = 11, Foreground = _tertiaryText };
        var entriesPanel = new StackPanel { Spacing = 2 };
        var upButton = new Button { Content = "Up", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        var refreshButton = new Button { Content = new SymbolIcon(Symbol.Refresh), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        var browseButton = new Button { Content = "Browse", Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
        ContentDialog? dialog = null;

        void UpdateSelectionStatus()
        {
            selectionStatus.Text = selectedPaths.Count > 0
                ? $"{selectedPaths.Count} folder{(selectedPaths.Count == 1 ? "" : "s")} selected"
                : "Select folders or use current";
            if (dialog == null) return;
            dialog.PrimaryButtonText = selectedPaths.Count > 0
                ? $"Add {selectedPaths.Count} Folder{(selectedPaths.Count == 1 ? "" : "s")}"
                : "Use Current Folder";
            dialog.IsPrimaryButtonEnabled = selectedPaths.Count > 0 || !existingPathSet.Contains(currentPath);
        }

        async Task LoadPathAsync(string path)
        {
            status.Foreground = _tertiaryText;
            status.Text = "Loading server folders…";
            entriesPanel.Children.Clear();
            upButton.IsEnabled = false;
            try
            {
                var response = await ViewModel.BrowseFilesystemAsync(path);
                currentPath = response.Path;
                pathBox.Text = response.Path;
                currentLabel.Text = response.Path;
                upButton.Tag = response.Parent;
                upButton.IsEnabled = response.Parent != response.Path;
                status.Text = response.Entries.Count == 0 ? "No subfolders found here." : "";
                foreach (var entry in response.Entries)
                {
                    var captured = entry.Path;
                    var row = new Grid { ColumnSpacing = 4 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var isExisting = existingPathSet.Contains(captured);
                    var check = new CheckBox
                    {
                        IsChecked = isExisting || selectedPaths.Contains(captured),
                        IsEnabled = !isExisting,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0),
                    };
                    check.Checked += (_, _) =>
                    {
                        if (!isExisting) selectedPaths.Add(captured);
                        UpdateSelectionStatus();
                    };
                    check.Unchecked += (_, _) =>
                    {
                        selectedPaths.Remove(captured);
                        UpdateSelectionStatus();
                    };
                    var folderContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    folderContent.Children.Add(new FontIcon { Glyph = "\uED25", FontSize = 14, Foreground = _tertiaryText });
                    folderContent.Children.Add(new TextBlock { Text = entry.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                    var navigateButton = new Button
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = new SolidColorBrush(Colors.Transparent),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(4, 7, 8, 7),
                        Content = folderContent,
                    };
                    navigateButton.Click += async (_, _) => await LoadPathAsync(captured);
                    Grid.SetColumn(check, 0);
                    Grid.SetColumn(navigateButton, 1);
                    row.Children.Add(check);
                    row.Children.Add(navigateButton);
                    entriesPanel.Children.Add(row);
                }
                UpdateSelectionStatus();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                status.Foreground = new SolidColorBrush(DestructiveColor);
            }
        }

        browseButton.Click += async (_, _) =>
        {
            var requested = pathBox.Text.Trim();
            if (!requested.StartsWith('/'))
            {
                status.Text = "Use an absolute server path that starts with /.";
                status.Foreground = new SolidColorBrush(DestructiveColor);
                return;
            }
            await LoadPathAsync(requested);
        };
        upButton.Click += async (_, _) =>
        {
            if (upButton.Tag is string parent) await LoadPathAsync(parent);
        };
        refreshButton.Click += async (_, _) => await LoadPathAsync(currentPath);

        var pathRow = new Grid { ColumnSpacing = 8 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pathBox, 0); Grid.SetColumn(browseButton, 1);
        pathRow.Children.Add(pathBox); pathRow.Children.Add(browseButton);
        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(currentLabel, 0);
        toolbar.Children.Add(currentLabel);
        var toolbarActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        toolbarActions.Children.Add(upButton);
        toolbarActions.Children.Add(refreshButton);
        Grid.SetColumn(toolbarActions, 1);
        toolbar.Children.Add(toolbarActions);
        var content = new StackPanel { Width = 600, Spacing = 10 };
        content.Children.Add(pathRow); content.Children.Add(status); content.Children.Add(toolbar);
        content.Children.Add(new Border
        {
            Height = 300,
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new ScrollViewer { Content = entriesPanel },
        });
        content.Children.Add(selectionStatus);
        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Browse Library Folders",
            Content = content,
            PrimaryButtonText = "Use Current Folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        await LoadPathAsync(currentPath);
        UpdateSelectionStatus();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return [];
        return selectedPaths.Count > 0 ? selectedPaths.ToList() : [currentPath];
    }

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
        var nameError = new TextBlock
        {
            Text = "Name is required.",
            FontSize = 11,
            Foreground = new SolidColorBrush(DestructiveColor),
            Visibility = Visibility.Collapsed,
        };
        nameBox.TextChanged += (_, _) => nameError.Visibility = string.IsNullOrWhiteSpace(nameBox.Text)
            ? nameError.Visibility
            : Visibility.Collapsed;

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
        typeCombo.Items.Add(new ComboBoxItem { Content = "Manga", Tag = "manga" });
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

        // The WebUI exposes every library type as a card instead of hiding the
        // most important choice in a dropdown.
        var typeSelector = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var i = 0; i < 5; i++)
            typeSelector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        typeSelector.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        typeSelector.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var typeButtons = new List<Button>();
        var typeChoices = new (string Value, string Label, Symbol Icon)[]
        {
            ("movies", "Movies", Symbol.Video),
            ("series", "Series", Symbol.Video),
            ("mixed", "Mixed", Symbol.AllApps),
            ("audiobooks", "Audiobooks", Symbol.Audio),
            ("ebooks", "Ebooks", Symbol.PreviewLink),
            ("manga", "Manga", Symbol.Library),
            ("podcasts", "Podcasts", Symbol.Microphone),
        };
        void RefreshTypeCards()
        {
            var selectedType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "movies";
            foreach (var button in typeButtons)
            {
                var selected = string.Equals(button.Tag as string, selectedType, StringComparison.OrdinalIgnoreCase);
                button.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x1A, 0xC0, 0x84, 0xFC))
                    : (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
                button.BorderBrush = selected ? _accentBrush : _borderBrush;
                button.Foreground = selected ? _primaryText : _secondaryText;
            }
        }
        for (var i = 0; i < typeChoices.Length; i++)
        {
            var choice = typeChoices[i];
            var content = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new SymbolIcon { Symbol = choice.Icon, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = choice.Label, FontSize = 11, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button
            {
                Tag = choice.Value,
                Content = content,
                MinWidth = 0,
                MinHeight = 62,
                Padding = new Thickness(8, 10, 8, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
            };
            button.Click += (_, _) =>
            {
                typeCombo.SelectedItem = typeCombo.Items.Cast<ComboBoxItem>()
                    .First(item => string.Equals(item.Tag as string, choice.Value, StringComparison.OrdinalIgnoreCase));
                RefreshTypeCards();
            };
            typeButtons.Add(button);
            Grid.SetColumn(button, i % 5);
            Grid.SetRow(button, i / 5);
            typeSelector.Children.Add(button);
        }
        RefreshTypeCards();
        var typeChangeWarning = new TextBlock
        {
            Text = "Changing the type of an existing library may require a full rescan to rematch items.",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["WarningBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        void RefreshTypeChangeWarning()
        {
            var selectedType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            typeChangeWarning.Visibility = editingLib != null &&
                !string.Equals(selectedType, editingLib.Type, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

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

            var rowGrid = new Grid { ColumnSpacing = 6 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var folderIcon = new FontIcon
            {
                Glyph = "\uED25",
                FontSize = 16,
                Foreground = _tertiaryText,
                VerticalAlignment = VerticalAlignment.Center,
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

            Grid.SetColumn(folderIcon, 0);
            Grid.SetColumn(tb, 1);
            Grid.SetColumn(deleteBtn, 2);
            rowGrid.Children.Add(folderIcon);
            rowGrid.Children.Add(tb);
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
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"]
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

        var browsePathsBtn = new Button
        {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
        };
        var browsePathsContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        browsePathsContent.Children.Add(new FontIcon { Glyph = "\uE8B7", FontSize = 13 });
        browsePathsContent.Children.Add(new TextBlock { Text = "Browse", FontSize = 12 });
        browsePathsBtn.Content = browsePathsContent;
        browsePathsBtn.Click += async (_, _) =>
        {
            var initialPath = pathInputs.Select(input => input.Text.Trim()).FirstOrDefault(path => path.Length > 0) ?? "/";
            var existingPaths = pathInputs
                .Select(input => input.Text.Trim())
                .Where(path => path.Length > 0)
                .ToList();
            var selectedPaths = await BrowseServerFolderAsync(initialPath, existingPaths);
            foreach (var selected in selectedPaths)
            {
                var emptyInput = pathInputs.FirstOrDefault(input => string.IsNullOrWhiteSpace(input.Text));
                if (emptyInput != null)
                {
                    emptyInput.Text = selected;
                }
                else if (!pathInputs.Any(input => string.Equals(input.Text.Trim(), selected, StringComparison.OrdinalIgnoreCase)))
                {
                    AddPathRow(selected);
                }
            }
            RefreshDeleteButtons();
        };
        var pathActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pathActions.Children.Add(browsePathsBtn);
        pathActions.Children.Add(addPathBtn);

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
            RefreshTypeChangeWarning();
            _ = LoadDefaultProvidersAsync(newType, providerSection, levelChains, () => chainDirty = true);
        };

        // Current WebUI editor uses a left section rail and four focused pages.
        StackPanel MakeSection(string title, string description)
        {
            var panel = new StackPanel { Spacing = 14, Padding = new Thickness(20, 16, 20, 20) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = _primaryText });
            panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = _tertiaryText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -8, 0, 6) });
            return panel;
        }
        var generalPanel = MakeSection("General", "Name this library and choose the kind of media it holds.");
        var foldersPanel = MakeSection("Folders", "Silo scans these folders on the server and watches them for changes.");
        var metadataPanel = MakeSection("Metadata", "Control where artwork and descriptions come from, and in which language.");
        var advancedPanel = MakeSection("Advanced", "Optional background processing for this library.");

        var nameGroup = new StackPanel { Spacing = 6 };
        nameGroup.Children.Add(MakeFormLabel("Name"));
        nameGroup.Children.Add(nameBox);
        nameGroup.Children.Add(nameError);
        generalPanel.Children.Add(nameGroup);

        // Row 2: Paths
        var pathsGroup = new StackPanel { Spacing = 6 };
        pathsGroup.Children.Add(pathsPanel);
        var pathsError = new TextBlock
        {
            Text = "Add at least one server folder.",
            FontSize = 11,
            Foreground = new SolidColorBrush(DestructiveColor),
            Visibility = Visibility.Collapsed,
        };
        pathsGroup.Children.Add(pathsError);
        pathsGroup.Children.Add(pathActions);
        foldersPanel.Children.Add(pathsGroup);

        // Poster file picker state (for upload after save)
        byte[]? posterFileBytes = null;
        string? posterFileName = null;
        string? posterContentType = null;

        // Row 3: Type, followed by Enabled and Poster exactly as the WebUI.
        var typeRow = new Grid { RowSpacing = 12 };
        typeRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        typeRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var typeGroup = new StackPanel { Spacing = 6 };
        typeGroup.Children.Add(MakeFormLabel("Type"));
        typeGroup.Children.Add(typeSelector);
        typeGroup.Children.Add(typeChangeWarning);
        Grid.SetRow(typeGroup, 0);
        typeRow.Children.Add(typeGroup);

        StackPanel? posterGroup = null;
        // Poster section (only when editing)
        if (editingLib != null)
        {
            posterGroup = new StackPanel { Spacing = 6 };
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
                Content = string.IsNullOrEmpty(editingLib.PosterUrl) ? "Upload" : "Replace",
                Height = 32,
                MinHeight = 32,
                Padding = new Thickness(12, 6, 12, 6),
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
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
                        pickPosterBtn.IsEnabled = false;
                        pickPosterBtn.Content = "...";
                        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                        await api.SetLibraryPosterAsync(editingLib.Id, posterFileBytes, posterFileName, posterContentType);
                        posterFileBytes = null;
                        posterFileName = null;
                        posterContentType = null;
                        pickPosterBtn.Content = "Replace";
                        ShowStatus("Library poster updated.");
                    }
                }
                catch (Exception ex)
                {
                    pickPosterBtn.Content = string.IsNullOrEmpty(editingLib.PosterUrl) ? "Upload" : "Replace";
                    ShowStatus($"Poster upload failed: {ex.Message}");
                }
                finally
                {
                    pickPosterBtn.IsEnabled = true;
                }
            };
            posterRow.Children.Add(pickPosterBtn);
            if (!string.IsNullOrEmpty(editingLib.PosterUrl))
            {
                var removePosterBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 13, Foreground = new SolidColorBrush(DestructiveColor) },
                    Width = 32, Height = 32, MinWidth = 32, MinHeight = 32,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                };
                ToolTipService.SetToolTip(removePosterBtn, "Remove poster");
                removePosterBtn.Click += async (_, _) =>
                {
                    removePosterBtn.IsEnabled = false;
                    try
                    {
                        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                        await api.DeleteLibraryPosterAsync(editingLib.Id);
                        ShowStatus("Library poster removed.");
                    }
                    catch (Exception ex) { ShowStatus($"Poster removal failed: {ex.Message}"); }
                };
                posterRow.Children.Add(removePosterBtn);
            }

            posterGroup.Children.Add(posterRow);
        }

        generalPanel.Children.Add(typeRow);

        var enabledCopy = new StackPanel { Spacing = 2 };
        enabledCopy.Children.Add(new TextBlock { Text = "Enabled", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _primaryText });
        enabledCopy.Children.Add(new TextBlock
        {
            Text = "Disabled libraries are hidden from browsing and skipped by scans.",
            FontSize = 11,
            Foreground = _tertiaryText,
            TextWrapping = TextWrapping.Wrap,
        });
        var enabledRow = new Grid { ColumnSpacing = 12 };
        enabledRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        enabledRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        enabledRow.Children.Add(enabledCopy);
        enabledSwitch.OnContent = null;
        enabledSwitch.OffContent = null;
        Grid.SetColumn(enabledSwitch, 1);
        enabledRow.Children.Add(enabledSwitch);
        generalPanel.Children.Add(new Border
        {
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = enabledRow,
        });
        if (posterGroup != null)
            generalPanel.Children.Add(posterGroup);

        // Metadata Language selector (webui LibraryForm: language select)
        var langCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8), FontSize = 13, Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (var language in MediaLanguageCatalog.All)
        {
            var item = new ComboBoxItem { Content = language.Label, Tag = language.Code };
            if (language.Code == (editingLib?.MetadataLanguage ?? "en")) item.IsSelected = true;
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
        langField.Children.Add(new TextBlock
        {
            Text = "Preferred language for titles, summaries, and artwork fetched from providers.",
            FontSize = 11,
            Foreground = _tertiaryText,
            TextWrapping = TextWrapping.Wrap,
        });
        metadataPanel.Children.Add(langField);

        var autoTranslateToggle = new ToggleSwitch
        {
            IsOn = editingLib?.AutoTranslateMetadata ?? false,
            OnContent = null,
            OffContent = null,
        };
        var autoTranslateCopy = new StackPanel { Spacing = 2 };
        autoTranslateCopy.Children.Add(new TextBlock
        {
            Text = "Auto-translate descriptions",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = _primaryText,
        });
        autoTranslateCopy.Children.Add(new TextBlock
        {
            Text = "When providers have no translation for this library's language, translate descriptions with AI after each refresh. Requires AI description translation in Admin Settings → AI Services.",
            FontSize = 11,
            Foreground = _tertiaryText,
            TextWrapping = TextWrapping.Wrap,
        });
        var autoTranslateRow = new Grid { ColumnSpacing = 12 };
        autoTranslateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        autoTranslateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        autoTranslateRow.Children.Add(autoTranslateCopy);
        Grid.SetColumn(autoTranslateToggle, 1);
        autoTranslateRow.Children.Add(autoTranslateToggle);
        metadataPanel.Children.Add(autoTranslateRow);

        Border MakeAdvancedSettingCard(string title, string description, ToggleSwitch toggle, string? warning = null)
        {
            toggle.OnContent = null;
            toggle.OffContent = null;
            var copy = new StackPanel { Spacing = 2 };
            copy.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = _primaryText,
            });
            copy.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = _tertiaryText,
                TextWrapping = TextWrapping.Wrap,
            });
            if (!string.IsNullOrWhiteSpace(warning))
            {
                copy.Children.Add(new TextBlock
                {
                    Text = warning,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["WarningBrush"],
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(copy);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            return new Border
            {
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                Background = _surfaceBrush,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Child = row,
            };
        }

        // Chapter Thumbnails toggle (webui LibraryForm: chapter thumbnails switch)
        var chapterThumbnailsSupported = editingLib?.ChapterThumbnailsSupported
            ?? ViewModel.Libraries.FirstOrDefault()?.ChapterThumbnailsSupported
            ?? true;
        var chapterThumbToggle = new ToggleSwitch
        {
            IsOn = editingLib?.ChapterThumbnailsEnabled ?? false,
            IsEnabled = chapterThumbnailsSupported,
        };
        advancedPanel.Children.Add(MakeAdvancedSettingCard(
            "Generate chapter thumbnails",
            "Stores chapter preview images in the configured public asset S3 bucket. Chapter markers and chapter menus still work without thumbnails.",
            chapterThumbToggle,
            chapterThumbnailsSupported ? null : "Public asset S3 storage is required before this can be enabled."));

        var introDetectionToggle = new ToggleSwitch
        {
            IsOn = editingLib?.IntroDetectionEnabled ?? false,
        };
        advancedPanel.Children.Add(MakeAdvancedSettingCard(
            "Detect intro markers",
            "Runs background audio analysis for episodes in this library. Embedded intro chapters are used when available.",
            introDetectionToggle));

        var trailerKinds = new[]
        {
            ("trailer", "Trailers"),
            ("teaser", "Teasers"),
            ("clip", "Clips"),
            ("featurette", "Featurettes"),
            ("behind_the_scenes", "Behind the scenes"),
            ("interview", "Interviews"),
            ("deleted_scene", "Deleted scenes"),
            ("short", "Shorts"),
        };
        var selectedTrailerKinds = editingLib == null
            ? trailerKinds.Select(x => x.Item1).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : editingLib.TrailerKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trailerPanel = new StackPanel { Spacing = 4 };
        trailerPanel.Children.Add(MakeFormLabel("Trailer & extras types"));
        trailerPanel.Children.Add(new TextBlock
        {
            Text = "Video types fetched from metadata providers during refresh. Uncheck everything to disable remote trailers for this library.",
            FontSize = 11,
            Foreground = _tertiaryText,
            TextWrapping = TextWrapping.Wrap,
        });
        var trailerGrid = new Grid { ColumnSpacing = 8, RowSpacing = 2 };
        trailerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trailerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (trailerKinds.Length + 1) / 2; row++)
            trailerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var trailerChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < trailerKinds.Length; i++)
        {
            var (kind, label) = trailerKinds[i];
            var check = new CheckBox { Content = label, IsChecked = selectedTrailerKinds.Contains(kind), FontSize = 12 };
            trailerChecks[kind] = check;
            Grid.SetColumn(check, i % 2);
            Grid.SetRow(check, i / 2);
            trailerGrid.Children.Add(check);
        }
        trailerPanel.Children.Add(trailerGrid);
        metadataPanel.Children.Add(trailerPanel);

        // Row 4: Provider Priority
        var providerWrapper = new StackPanel { Spacing = 6 };
        providerWrapper.Children.Add(new TextBlock
        {
            Text = "Provider Priority",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = _primaryText,
        });
        providerWrapper.Children.Add(new TextBlock
        {
            Text = "Providers are asked in order from top to bottom. Uncheck a provider to skip it for that level.",
            FontSize = 11,
            Foreground = _tertiaryText,
            TextWrapping = TextWrapping.Wrap,
        });
        providerWrapper.Children.Add(providerSection);
        metadataPanel.Children.Add(providerWrapper);

        var sectionHost = new Grid();
        generalPanel.Visibility = Visibility.Visible;
        foldersPanel.Visibility = metadataPanel.Visibility = advancedPanel.Visibility = Visibility.Collapsed;
        sectionHost.Children.Add(generalPanel);
        sectionHost.Children.Add(foldersPanel);
        sectionHost.Children.Add(metadataPanel);
        sectionHost.Children.Add(advancedPanel);
        var sectionScroll = new ScrollViewer { Content = sectionHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var nav = new StackPanel { Spacing = 4, Padding = new Thickness(10, 12, 10, 12) };
        var navButtons = new List<(Button Button, StackPanel Target)>();
        void SelectSection(StackPanel selected)
        {
            generalPanel.Visibility = selected == generalPanel ? Visibility.Visible : Visibility.Collapsed;
            foldersPanel.Visibility = selected == foldersPanel ? Visibility.Visible : Visibility.Collapsed;
            metadataPanel.Visibility = selected == metadataPanel ? Visibility.Visible : Visibility.Collapsed;
            advancedPanel.Visibility = selected == advancedPanel ? Visibility.Visible : Visibility.Collapsed;
            foreach (var entry in navButtons)
            {
                var active = entry.Target == selected;
                entry.Button.Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Colors.Transparent);
                entry.Button.Foreground = active ? _primaryText : _secondaryText;
            }
        }
        Button AddNavButton(string label, string glyph, StackPanel target)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
            content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
            content.Children.Add(new TextBlock { Text = label, FontSize = 13 });
            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 8),
            };
            button.Click += (_, _) => SelectSection(target);
            nav.Children.Add(button);
            navButtons.Add((button, target));
            return button;
        }
        AddNavButton("General", "\uE713", generalPanel);
        AddNavButton("Folders", "\uED25", foldersPanel);
        AddNavButton("Metadata", "\uE8B7", metadataPanel);
        AddNavButton("Advanced", "\uE9F5", advancedPanel);
        SelectSection(generalPanel);
        var form = new Grid
        {
            Width = 720,
            Height = 480,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(nav, 0); Grid.SetColumn(sectionScroll, 1);
        form.Children.Add(nav);
        var sectionBorder = new Border
        {
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = sectionScroll,
        };
        Grid.SetColumn(sectionBorder, 1);
        form.Children.Add(sectionBorder);

        // GetBody func
        object? GetBody()
        {
            string name = nameBox.Text.Trim();
            nameError.Visibility = string.IsNullOrEmpty(name) ? Visibility.Visible : Visibility.Collapsed;

            string selectedType = "movies";
            if (typeCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string tag)
                selectedType = tag;

            var paths = pathInputs
                .Select(tb => tb.Text.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            pathsError.Visibility = paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrEmpty(name))
            {
                SelectSection(generalPanel);
                nameBox.Focus(FocusState.Programmatic);
                return null;
            }
            if (paths.Count == 0)
            {
                SelectSection(foldersPanel);
                pathInputs[0].Focus(FocusState.Programmatic);
                return null;
            }

            var selectedLang = (langCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["type"] = selectedType,
                ["enabled"] = enabledSwitch.IsOn,
                ["paths"] = paths,
                ["metadata_language"] = selectedLang,
                ["auto_translate_metadata"] = autoTranslateToggle.IsOn,
                ["chapter_thumbnails_enabled"] = chapterThumbToggle.IsOn,
                ["intro_detection_enabled"] = introDetectionToggle.IsOn,
                ["trailer_kinds"] = trailerChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToArray(),
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
                        PluginInstallationId = e.PluginInstallationId,
                        CapabilityId = e.CapabilityId,
                        Priority = i,
                        Enabled = e.Enabled,
                    }).ToList()
                )
            };
            return request;
        }

        (byte[]? Bytes, string? Name, string? ContentType) GetPosterFile() => (posterFileBytes, posterFileName, posterContentType);

        return (form, GetBody, GetProviderChain, GetPosterFile);
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

        // Fill missing levels with defaults computed by the current server.
        var defaults = await ViewModel.GetLibraryProviderDefaultsAsync(libraryType);
        var levels = GetContentLevelsForType(libraryType);
        foreach (var level in levels)
        {
            if (!levelChains.ContainsKey(level))
            {
                levelChains[level] = defaults?.Levels.GetValueOrDefault(level)?.OrderBy(e => e.Priority).ToList() ?? [];
            }
        }

        DispatcherQueue.TryEnqueue(() => RebuildProviderSection(container, levelChains, levels, onDirty));
    }

    private async Task LoadDefaultProvidersAsync(
        string libraryType, StackPanel container,
        Dictionary<string, List<LibraryProviderChainEntry>> levelChains,
        Action onDirty)
    {
        levelChains.Clear();
        var levels = GetContentLevelsForType(libraryType);
        var defaults = await ViewModel.GetLibraryProviderDefaultsAsync(libraryType);
        foreach (var level in levels)
            levelChains[level] = defaults?.Levels.GetValueOrDefault(level)?.OrderBy(e => e.Priority).ToList() ?? [];

        DispatcherQueue.TryEnqueue(() => RebuildProviderSection(container, levelChains, levels, onDirty));
    }

    private void RebuildProviderSection(
        StackPanel container,
        Dictionary<string, List<LibraryProviderChainEntry>> levelChains,
        string[] levels,
        Action onDirty)
    {
        container.Children.Clear();

        if (levels.Length == 0 || !levelChains.Values.Any(items => items.Count > 0))
        {
            container.Children.Add(new TextBlock
            {
                Text = "No metadata provider plugins are installed. Install one under Admin → Plugins to fetch artwork and descriptions.",
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
        "manga" => ["manga"],
        "podcasts" => ["podcast", "podcast_episode"],
        "mixed" => ["movie", "series", "season", "episode", "audiobook", "ebook"],
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

    private void ShowStatus(string message, bool autoHide = true)
    {
        _statusHideTimer?.Stop();
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;

        if (!autoHide) return;
        _statusHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _statusHideTimer.Tick += (_, _) =>
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            _statusHideTimer?.Stop();
        };
        _statusHideTimer.Start();
    }
}
