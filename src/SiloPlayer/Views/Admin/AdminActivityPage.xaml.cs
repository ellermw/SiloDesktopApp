using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels.Admin;
using System.Collections.Specialized;
using System.Net.WebSockets;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminActivityPage : Page
{
    public AdminActivityViewModel ViewModel { get; }
    private bool _rebuildStreamPending;
    private bool _rebuildIpPending;

    // Event channel subscription for realtime refresh
    private IDisposable? _eventSubscription;
    private EventChannelClient? _eventChannel;
    private DateTime _lastEventRefresh = DateTime.MinValue;

    // Web UI colors
    private static readonly Color Green400 = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color Green500 = Color.FromArgb(255, 34, 197, 94);
    private static readonly Color Blue400 = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color Blue500 = Color.FromArgb(255, 59, 130, 246);
    private static readonly Color Amber400 = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color Amber500 = Color.FromArgb(255, 245, 158, 11);

    public AdminActivityPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminActivityViewModel>();
        this.InitializeComponent();
    }

    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width);
        AdminPageContent.Width = Math.Min(1640, width);
        var horizontalPadding = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
        var verticalPadding = width >= 1024 ? 32 : 16;
        AdminPageContent.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, 40);
        var contentWidth = Math.Max(0, width - (horizontalPadding * 2));

        var wrapHeader = contentWidth < 760;
        Grid.SetColumn(PageHeaderCopy, 0);
        Grid.SetColumnSpan(PageHeaderCopy, wrapHeader ? 2 : 1);
        Grid.SetColumn(PageHeaderActions, wrapHeader ? 0 : 1);
        Grid.SetColumnSpan(PageHeaderActions, wrapHeader ? 2 : 1);
        Grid.SetRow(PageHeaderActions, wrapHeader ? 1 : 0);
        PageHeaderActions.HorizontalAlignment = wrapHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        UpdateResponsiveTitle(contentWidth);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += ActivityPage_SizeChanged;
        UpdateResponsiveTitle(ActualWidth);
        ViewModel.FilteredSessions.CollectionChanged += (_, _) => ScheduleRebuildStream();
        ViewModel.IPLookupResults.CollectionChanged += (_, _) => ScheduleRebuildIp();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            RebuildAll();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }

        // Subscribe to realtime session events for live refresh
        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventChannel.EventReceived += OnEventReceived;
            _eventChannel.StateChanged += OnConnectionStateChanged;
            _eventSubscription = _eventChannel.Subscribe("sessions");
            // Set initial connection state
            UpdateConnectionState(_eventChannel.State);
        }
        catch { }

        // Set initial sort indicator (default: started desc)
        UpdateSortIndicators();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_eventChannel != null)
        {
            _eventChannel.EventReceived -= OnEventReceived;
            _eventChannel.StateChanged -= OnConnectionStateChanged;
        }
        _eventSubscription?.Dispose();
        _eventSubscription = null;
        SizeChanged -= ActivityPage_SizeChanged;
    }

    private void ActivityPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveTitle(e.NewSize.Width);
        StreamsScrollViewer.MaxHeight = Math.Max(200, e.NewSize.Height - 420);
    }

    private void UpdateResponsiveTitle(double width)
        => PageTitle.FontSize = Math.Clamp(width * 0.04, 32, 52);

    private async void ActivityRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var started = DateTime.UtcNow;
        ActivityRefreshButton.IsEnabled = false;
        ActivityRefreshLabel.Text = "Refreshing…";
        await ViewModel.LoadCommand.ExecuteAsync(null);
        RebuildAll();
        var remaining = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - started);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        ActivityRefreshLabel.Text = "Refresh";
        ActivityRefreshButton.IsEnabled = true;
    }

    private void OnEventReceived(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (channel != "sessions") return;
        if ((DateTime.UtcNow - _lastEventRefresh).TotalMilliseconds < 1000) return;
        _lastEventRefresh = DateTime.UtcNow;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try { await ViewModel.RefreshSilentAsync(); RebuildAll(); }
            catch { }
        });
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SearchText))
        {
            SearchClearButton.Visibility = string.IsNullOrEmpty(ViewModel.SearchText)
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ScheduleRebuildStream()
    {
        if (_rebuildStreamPending) return;
        _rebuildStreamPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildStreamPending = false;
            RebuildStreamTable();
            UpdateFilterBar();
        });
    }

    private void ScheduleRebuildIp()
    {
        if (_rebuildIpPending) return;
        _rebuildIpPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildIpPending = false;
            RebuildIpResults();
        });
    }

    private void RebuildAll()
    {
        UpdateHeader();
        UpdateSummaryStrip();
        RebuildStreamTable();
        UpdateFilterBar();
        UpdateSortIndicators();
    }

    // ===== Header =====

    private void UpdateHeader()
    {
        var total = ViewModel.TotalCount;
        LiveBadgeText.Text = $"{total} live";
        LiveBadge.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Subtitle: "{count} active stream(s) across {nodeCount} node(s)" or "No active streams"
        if (total == 0)
        {
            SubtitleText.Text = "No active streams";
        }
        else
        {
            var nodes = ViewModel.GetNodeCounts();
            int nodeCount = nodes.Count;
            SubtitleText.Text = $"{total} active stream{(total != 1 ? "s" : "")} across {nodeCount} node{(nodeCount != 1 ? "s" : "")}";
        }
    }

    // ===== Connection State =====

    private void OnConnectionStateChanged(WebSocketState state)
    {
        DispatcherQueue.TryEnqueue(() => UpdateConnectionState(state));
    }

    private void UpdateConnectionState(WebSocketState state)
    {
        string text = state switch
        {
            WebSocketState.Open => "Live",
            WebSocketState.Connecting => "Connecting",
            _ => "Disconnected"
        };
        ConnectionStateText.Text = text;
    }

    // ===== Sort Headers =====

    private void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string field)
        {
            ViewModel.ToggleSort(field);
            UpdateSortIndicators();
        }
    }

    private void UpdateSortIndicators()
    {
        var indicators = new (string Field, TextBlock Indicator, Button Btn)[]
        {
            ("username", SortUserIndicator, SortUser),
            ("media", SortStreamIndicator, SortStream),
            ("method", SortVideoIndicator, SortVideo),
            ("node", SortNodeIndicator, SortNode),
            ("started", SortTimeIndicator, SortTime)
        };

        var accentBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var tertiaryBrush = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"];
        var primaryBrush = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];

        foreach (var (field, indicator, btn) in indicators)
        {
            bool isActive = ViewModel.SortField == field;
            indicator.Text = isActive ? (ViewModel.SortAscending ? "\u25B2" : "\u25BC") : "";

            // Highlight active sort header text
            if (btn.Content is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock headerText)
            {
                headerText.Foreground = isActive ? primaryBrush : tertiaryBrush;
            }
        }
    }

    // ===== Summary Strip (Method Distribution + Node Breakdown) =====

    private void UpdateSummaryStrip()
    {
        var total = ViewModel.TotalCount;
        SummaryStrip.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (total == 0) return;

        UpdateMethodBar();
        UpdateMethodButtons();
        UpdateNodeBreakdown();
    }

    private void UpdateMethodBar()
    {
        MethodBar.Children.Clear();
        MethodBar.ColumnDefinitions.Clear();

        int total = ViewModel.TotalCount;
        if (total == 0)
        {
            MethodBar.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
            return;
        }

        MethodBar.Background = new SolidColorBrush(Colors.Transparent);
        var methods = ViewModel.GetMethodCounts();
        // Sort alphabetically like web
        var sorted = methods.OrderBy(kv => kv.Key).ToList();

        int colIdx = 0;
        foreach (var (method, count) in sorted)
        {
            double pct = (double)count / total;
            MethodBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });

            var seg = new Border
            {
                Background = new SolidColorBrush(GetMethodBarColor(method)),
                Opacity = 0,
            };
            Grid.SetColumn(seg, colIdx++);
            MethodBar.Children.Add(seg);

            // Smooth fade-in transition (webui uses 500ms width animation)
            var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(400)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
            };
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, seg);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);
            sb.Begin();
        }
    }

    private void UpdateMethodButtons()
    {
        MethodButtonsPanel.Children.Clear();

        var methods = ViewModel.GetMethodCounts();
        var sorted = methods.OrderBy(kv => kv.Key).ToList();

        foreach (var (method, count) in sorted)
        {
            var btn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Tag = method
            };
            btn.Click += MethodButton_Click;

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            // Colored dot: h-2 w-2 rounded-full = 8px circle
            sp.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(GetMethodDotColor(method)),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Method name: capitalize, font-medium, text-[11px]
            sp.Children.Add(new TextBlock
            {
                Text = char.ToUpper(method[0]) + method[1..],
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });

            // Count: text-muted-foreground tabular-nums, text-[11px]
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });

            btn.Content = sp;

            // Apply opacity-30 if a filter is active and this method doesn't match
            if (ViewModel.MethodFilter != null && ViewModel.MethodFilter != method)
                btn.Opacity = 0.3;

            MethodButtonsPanel.Children.Add(btn);
        }
    }

    private void UpdateNodeBreakdown()
    {
        var nodes = ViewModel.GetNodeCounts();
        if (nodes.Count <= 1)
        {
            NodeBreakdownPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NodeBreakdownPanel.Visibility = Visibility.Visible;
        NodeButtonsPanel.Children.Clear();

        // Sort by count descending
        var sorted = nodes.OrderByDescending(kv => kv.Value).ToList();

        foreach (var (node, count) in sorted)
        {
            bool isActive = ViewModel.NodeFilter == node;
            bool hasFilter = ViewModel.NodeFilter != null;

            var btn = new Button
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Tag = node,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
            };

            if (isActive)
            {
                // border-primary/40 bg-primary/10 text-primary
                btn.Background = new SolidColorBrush(Color.FromArgb(26, 120, 174, 252));
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 120, 174, 252));
                btn.Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            }
            else
            {
                btn.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
                btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
                btn.Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
                if (hasFilter) btn.Opacity = 0.3;
            }

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            sp.Children.Add(new TextBlock
            {
                Text = node,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
            btn.Content = sp;
            btn.Click += NodeButton_Click;

            // Hover: border-primary/20 equivalent
            if (!isActive)
            {
                var defaultBorder = btn.BorderBrush;
                btn.PointerEntered += (s, _) =>
                {
                    if (s is Button b)
                        b.BorderBrush = new SolidColorBrush(Color.FromArgb(51, 120, 174, 252));
                };
                btn.PointerExited += (s, _) =>
                {
                    if (s is Button b)
                        b.BorderBrush = defaultBorder;
                };
            }

            NodeButtonsPanel.Children.Add(btn);
        }
    }

    // ===== Filter Bar =====

    private void UpdateFilterBar()
    {
        int filtered = ViewModel.FilteredSessions.Count;
        int total = ViewModel.TotalCount;

        bool hasFilters = ViewModel.HasActiveFilters;
        BtnClearFilters.Visibility = ViewModel.ActiveFilterCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        // "Showing X of Y": only visible when filters active (search or any filter)
        if (!string.IsNullOrEmpty(ViewModel.SearchText) || ViewModel.ActiveFilterCount > 0)
        {
            FilterCountText.Text = $"Showing {filtered} of {total} streams";
            FilterCountText.Visibility = Visibility.Visible;
        }
        else
        {
            FilterCountText.Visibility = Visibility.Collapsed;
        }

        UpdateTypeButtonStyles();
        // Update method buttons opacity
        UpdateMethodButtons();
        // Update node buttons
        UpdateNodeBreakdown();
    }

    private void UpdateTypeButtonStyles()
    {
        SetTypeToggleActive(BtnTypeMovie, ViewModel.TypeFilter == "movie");
        SetTypeToggleActive(BtnTypeSeries, ViewModel.TypeFilter == "series");
    }

    private static void SetTypeToggleActive(Button btn, bool active)
    {
        if (active)
        {
            // border-primary/40 bg-primary/10 text-primary
            btn.Background = new SolidColorBrush(Color.FromArgb(26, 120, 174, 252));
            btn.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 120, 174, 252));
            btn.Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            // border-border bg-surface text-muted-foreground
            btn.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
            btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
            btn.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        }
    }

    // ===== Stream Table =====

    private void RebuildStreamTable()
    {
        StreamsPanel.Children.Clear();

        var sessions = ViewModel.FilteredSessions;
        if (sessions.Count == 0)
        {
            StreamsTable.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;

            // Contextual empty state: Filter icon when filters active, Play icon otherwise
            if (ViewModel.HasActiveFilters)
            {
                EmptyStateIcon.Glyph = "\uE71C"; // Filter icon
                EmptyStateText.Text = "No streams match your filters";
            }
            else
            {
                EmptyStateIcon.Glyph = "\uE768"; // Play icon
                EmptyStateText.Text = "No active streams";
            }
            return;
        }

        StreamsTable.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        for (int i = 0; i < sessions.Count; i++)
        {
            StreamsPanel.Children.Add(BuildStreamRow(sessions[i], i));
        }
    }

    private FrameworkElement BuildStreamRow(AdminSession session, int index)
    {
        // Even rows transparent, odd rows bg-surface/20. Hover state via background.
        // Web: border-border/30 border-b, even="" odd="bg-surface/20"
        bool isOdd = index % 2 != 0;

        var row = new Grid
        {
            Padding = new Thickness(12, 10, 12, 10),
            ColumnSpacing = 8,
            Background = isOdd
                ? new SolidColorBrush(Color.FromArgb(51, 21, 30, 43)) // bg-surface/20
                : new SolidColorBrush(Colors.Transparent),
            // Bottom border for row separation
            BorderBrush = new SolidColorBrush(Color.FromArgb(77, 40, 56, 77)), // border-border/30
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 120 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star), MinWidth = 190 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.9, GridUnitType.Star), MinWidth = 220 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star), MinWidth = 90 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 125 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star), MinWidth = 220 });

        // Col 0: User — avatar 24px (h-6 w-6), text-[9px] initial, username text-[13px] font-medium, IP text-[10px]
        var userCol = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        userCol.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        userCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string username = session.Username ?? $"User #{session.UserId}";
        string initial = username.Length > 0 ? username[0].ToString().ToUpper() : "?";
        var avatar = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        avatar.Child = new TextBlock
        {
            Text = initial,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var userStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // Current WebUI links the stream user to the admin user detail page.
        var capturedUserId = session.UserId;
        var userLink = new HyperlinkButton
        {
            Content = username,
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        };
        userLink.Click += (_, _) => Frame.Navigate(typeof(AdminUserDetailPage), capturedUserId);
        userStack.Children.Add(userLink);
        var profileDisplay = session.ProfileName ?? session.ProfileId;
        if (!string.IsNullOrWhiteSpace(profileDisplay))
        {
            userStack.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                BorderBrush = new SolidColorBrush(Color.FromArgb(76, 120, 174, 252)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock { Text = profileDisplay, FontSize = 10, Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"], TextTrimming = TextTrimming.CharacterEllipsis },
            });
        }
        var clientMeta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var clientLabel = AdminActivityViewModel.GetSessionClientLabel(session);
        if (!string.IsNullOrWhiteSpace(clientLabel))
        {
            var label = new TextBlock { Text = clientLabel, FontSize = 10, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 128 };
            ToolTipService.SetToolTip(label, session.ClientUserAgent ?? clientLabel);
            clientMeta.Children.Add(label);
        }
        if (!string.IsNullOrWhiteSpace(clientLabel) && !string.IsNullOrWhiteSpace(session.ClientIp))
            clientMeta.Children.Add(new TextBlock { Text = "·", FontSize = 10, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
        if (!string.IsNullOrWhiteSpace(session.ClientIp))
        {
            var capturedIp = session.ClientIp.Trim();
            var ipLink = new HyperlinkButton { Content = capturedIp, Padding = new Thickness(0), FontSize = 10, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] };
            ipLink.Click += async (_, _) => await RunIpLookupAsync(capturedIp);
            clientMeta.Children.Add(ipLink);
        }
        if (clientMeta.Children.Count > 0) userStack.Children.Add(clientMeta);

        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(userStack, 1);
        userCol.Children.Add(avatar);
        userCol.Children.Add(userStack);
        Grid.SetColumn(userCol, 0);

        // Col 1: Stream — title text-[13px] font-medium, subtitle text-[10px], meta text-[10px]
        // Episode handling: check SeriesName != null && SeasonNumber != null && EpisodeNumber != null
        string title = AdminActivityViewModel.GetDisplayTitle(session);
        string? subtitle = AdminActivityViewModel.GetDisplaySubtitle(session);

        string sourceContainer = session.SourceContainer?.Trim().ToUpperInvariant() ?? "";
        string streamBitrate = AdminActivityViewModel.FormatSessionBitrate(session.StreamBitrateKbps);
        string streamMeta = string.Join(" \u00b7 ", new[] { sourceContainer, streamBitrate }.Where(s => !string.IsNullOrEmpty(s)));

        var streamStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // Stream title as clickable link -> ItemDetailPage when content_id is available
        if (!string.IsNullOrEmpty(session.ContentId))
        {
            var capturedContentId = session.ContentId;
            var titleLink = new HyperlinkButton
            {
                Content = title,
                Padding = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            };
            titleLink.Click += (_, _) =>
            {
                // Navigate to ItemDetailPage via main NavigationService (main frame, not admin sub-frame)
                var nav = App.Services.GetRequiredService<NavigationService>();
                nav.Navigate<ItemDetailPage>(capturedContentId);
            };
            streamStack.Children.Add(titleLink);
        }
        else
        {
            streamStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        if (!string.IsNullOrEmpty(subtitle))
        {
            streamStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        if (!string.IsNullOrEmpty(streamMeta))
        {
            streamStack.Children.Add(new TextBlock
            {
                Text = streamMeta,
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(streamStack, 1);

        // Col 2: Video — decision badge text-[9px] with border, summary text-[12px] font-medium, detail text-[10px]
        string videoDecision = session.VideoDecision ?? session.PlayMethod;

        // Col 3: Audio — same pattern as video
        string audioDecision = session.AudioDecision ?? (session.TranscodeAudio ? "transcode" : session.PlayMethod);
        var playbackStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var playbackHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var transcodeMode = AdminActivityViewModel.FormatTranscodeMode(session);
        if (!string.IsNullOrWhiteSpace(transcodeMode))
            playbackHeader.Children.Add(BuildTranscodeModeBadge(transcodeMode));
        var detailsToggle = new Button
        {
            Content = "Details  ⌄",
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        };
        playbackHeader.Children.Add(detailsToggle);
        playbackStack.Children.Add(playbackHeader);
        playbackStack.Children.Add(BuildPlaybackSummaryLine("Container", session.PlayMethod, AdminActivityViewModel.FormatDeliveredContainer(session)));
        playbackStack.Children.Add(BuildPlaybackSummaryLine("Video", videoDecision, AdminActivityViewModel.FormatDeliveredVideo(session)));
        playbackStack.Children.Add(BuildPlaybackSummaryLine("Audio", audioDecision, AdminActivityViewModel.FormatDeliveredAudio(session)));
        Grid.SetColumn(playbackStack, 2);

        // Col 4: Node — text-[12px] muted (not bold), profile text-[10px]
        var nodeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string nodeName = session.NodeDisplayName ?? session.ReportingNode ?? "\u2014";
        nodeStack.Children.Add(new TextBlock
        {
            Text = nodeName,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(nodeStack, 3);

        // Col 5: Time — monospace text-[12px] right-aligned, muted
        var timeBlock = new TextBlock
        {
            Text = $"Session active {AdminActivityViewModel.GetElapsed(session.StartedAt)}",
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas")
        };
        Grid.SetColumn(timeBlock, 4);

        // Col 6: Action overflow menu + deep links
        var capturedSession = session;
        var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
        var toastService = App.Services.GetRequiredService<ToastService>();
        bool isPaused = session.IsPaused;

        // Build MenuFlyout with Pause/Resume, Stop, Message, Terminate
        var flyout = new MenuFlyout();

        // Pause/Resume toggle
        var pauseItem = new MenuFlyoutItem
        {
            Text = isPaused ? "Resume" : "Pause",
            Icon = new FontIcon { Glyph = isPaused ? "\uE768" : "\uE769" }
        };
        pauseItem.Click += async (_, _) =>
        {
            try
            {
                if (isPaused)
                    await adminApi.ResumeSessionAsync(capturedSession.SessionId);
                else
                    await adminApi.PauseSessionAsync(capturedSession.SessionId);
                toastService.Success(isPaused ? "Session resumed" : "Session paused");
                await ViewModel.LoadCommand.ExecuteAsync(null);
            }
            catch (Exception ex) { toastService.Error($"Failed: {ex.Message}"); }
        };

        // Stop
        var stopItem = new MenuFlyoutItem
        {
            Text = "Stop",
            Icon = new FontIcon { Glyph = "\uE71A" }
        };
        stopItem.Click += async (_, _) =>
        {
            try
            {
                await adminApi.StopSessionAsync(capturedSession.SessionId);
                toastService.Success("Session stopped");
                await Task.Delay(500);
                await ViewModel.LoadCommand.ExecuteAsync(null);
            }
            catch (Exception ex) { toastService.Error($"Stop failed: {ex.Message}"); }
        };

        // Message
        var msgItem = new MenuFlyoutItem
        {
            Text = "Message",
            Icon = new FontIcon { Glyph = "\uE8BD" }
        };
        msgItem.Click += async (_, _) =>
        {
            var msgBox = new TextBox { PlaceholderText = "Message to display", CornerRadius = new CornerRadius(8), FontSize = 13 };
            var dlg = new ContentDialog
            {
                Title = "Send Message",
                PrimaryButtonText = "Send",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                Content = msgBox,
                DefaultButton = ContentDialogButton.Primary
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(msgBox.Text))
            {
                try
                {
                    await adminApi.MessageSessionAsync(capturedSession.SessionId, msgBox.Text.Trim());
                    toastService.Success("Message sent");
                }
                catch (Exception ex) { toastService.Error($"Message failed: {ex.Message}"); }
            }
        };
        var viewLogsItem = new MenuFlyoutItem { Text = "View Logs", Icon = new FontIcon { Glyph = "\uE8A5" } };
        viewLogsItem.Click += (_, _) => Frame.Navigate(typeof(AdminLogsPage), capturedSession.SessionId);
        flyout.Items.Add(viewLogsItem);
        var ffmpegLogsItem = new MenuFlyoutItem { Text = "FFmpeg Logs", Icon = new FontIcon { Glyph = "\uE756" } };
        ffmpegLogsItem.Click += (_, _) => Frame.Navigate(typeof(AdminLogsPage), $"{capturedSession.SessionId}|ffmpeg");
        flyout.Items.Add(ffmpegLogsItem);
        flyout.Items.Add(msgItem);

        // Terminate (destructive — red text + confirmation dialog)
        var terminateItem = new MenuFlyoutItem
        {
            Text = "Terminate",
            Icon = new FontIcon { Glyph = "\uE74D" },
            Foreground = (SolidColorBrush)Application.Current.Resources["ErrorBrush"]
        };
        terminateItem.Click += async (_, _) =>
        {
            var confirmDlg = new ContentDialog
            {
                Title = "Terminate Session",
                Content = "This will forcefully terminate the session. This action cannot be undone.",
                PrimaryButtonText = "Terminate",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirmDlg.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    await adminApi.TerminateSessionAsync(capturedSession.SessionId);
                    toastService.Success("Session terminated");
                    await ViewModel.LoadCommand.ExecuteAsync(null);
                }
                catch (Exception ex) { toastService.Error($"Terminate failed: {ex.Message}"); }
            }
        };

        // Action button that opens the flyout
        var actionBtn = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Content = new FontIcon
            {
                Glyph = "\uE712", // More (ellipsis) icon
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            },
            Flyout = flyout
        };
        ToolTipService.SetToolTip(actionBtn, "Actions");

        var controlPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        // Wrap time and controls vertically
        var timeControlStack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var stateColor = session.IsPaused ? Color.FromArgb(255, 252, 211, 77) : Color.FromArgb(255, 52, 211, 153);
        timeControlStack.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, stateColor.R, stateColor.G, stateColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock { Text = session.IsPaused ? "Paused" : "Playing", FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(stateColor) },
        });
        timeControlStack.Children.Add(new TextBlock
        {
            Text = AdminActivityViewModel.FormatPlaybackPosition(session),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        timeControlStack.Children.Add(timeBlock);
        Grid.SetColumn(timeControlStack, 4);

        row.Children.Add(userCol);
        row.Children.Add(streamStack);
        row.Children.Add(playbackStack);
        row.Children.Add(nodeStack);
        row.Children.Add(timeControlStack);

        // FFmpeg inline log panel — collapsible, loads on demand
        var detailsPanel = BuildPlaybackDetailsPanel(session);
        var expandedPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(16, 8, 16, 12),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0x15, 0x1E, 0x2B)),
        };
        expandedPanel.Children.Add(detailsPanel);
        detailsToggle.Click += (_, _) =>
        {
            expandedPanel.Visibility = expandedPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            detailsToggle.Content = expandedPanel.Visibility == Visibility.Visible ? "Details  ⌃" : "Details  ⌄";
        };

        var ffmpegPanel = new StackPanel
        {
            Spacing = 4,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(16, 8, 16, 12),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0x15, 0x1E, 0x2B)),
        };
        expandedPanel.Children.Add(ffmpegPanel);

        // Add FFmpeg toggle to the control panel (next to logs links)
        var capturedSessionId = session.SessionId;
        var ffmpegToggle = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var ffmpegContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        ffmpegContent.Children.Add(new FontIcon
        {
            Glyph = "\uE756", // Terminal
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
        });
        ffmpegContent.Children.Add(new TextBlock
        {
            Text = "FFmpeg",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
        });
        var ffmpegChevron = new FontIcon
        {
            Glyph = "\uE70D", // ChevronDown
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
        };
        ffmpegContent.Children.Add(ffmpegChevron);
        ffmpegToggle.Content = ffmpegContent;

        bool ffmpegLoaded = false;
        ffmpegToggle.Click += async (_, _) =>
        {
            bool isNowOpen = ffmpegPanel.Visibility == Visibility.Collapsed;
            if (isNowOpen) expandedPanel.Visibility = Visibility.Visible;
            ffmpegPanel.Visibility = isNowOpen ? Visibility.Visible : Visibility.Collapsed;
            ffmpegChevron.Glyph = isNowOpen ? "\uE70E" : "\uE70D"; // ChevronUp / ChevronDown

            if (isNowOpen && !ffmpegLoaded)
            {
                ffmpegLoaded = true;
                ffmpegPanel.Children.Clear();
                ffmpegPanel.Children.Add(new TextBlock
                {
                    Text = "Loading ffmpeg output...",
                    FontSize = 11,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                    FontFamily = new FontFamily("Consolas"),
                });

                try
                {
                    var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                    var resp = await adminApi.GetAppLogsAsync(
                        playbackSessionId: capturedSessionId, component: "ffmpeg", limit: 12);
                    ffmpegPanel.Children.Clear();

                    if (resp.Entries.Count == 0)
                    {
                        ffmpegPanel.Children.Add(new TextBlock
                        {
                            Text = "No ffmpeg rows yet for this session.",
                            FontSize = 11,
                            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                            FontFamily = new FontFamily("Consolas"),
                        });
                    }
                    else
                    {
                        // Header pill
                        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
                        var pill = new Border
                        {
                            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x60, 0xA5, 0xFA)),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(8, 2, 8, 2),
                        };
                        pill.Child = new TextBlock
                        {
                            Text = "FFMPEG",
                            FontSize = 10, FontWeight = FontWeights.SemiBold,
                            CharacterSpacing = 200,
                            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                        };
                        headerRow.Children.Add(pill);
                        headerRow.Children.Add(new TextBlock
                        {
                            Text = "Live transcode console",
                            FontSize = 11,
                            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                        ffmpegPanel.Children.Add(headerRow);

                        foreach (var entry in resp.Entries)
                        {
                            var logRow = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 3, 0, 3) };
                            logRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                            logRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                            var timeCol = new TextBlock
                            {
                                Text = entry.Timestamp != null ? DateTime.Parse(entry.Timestamp).ToLocalTime().ToString("HH:mm:ss") : "",
                                FontSize = 10, FontFamily = new FontFamily("Consolas"),
                                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                            };
                            Grid.SetColumn(timeCol, 0);
                            logRow.Children.Add(timeCol);

                            var msgCol = new TextBlock
                            {
                                Text = entry.Message ?? "",
                                FontSize = 11, FontFamily = new FontFamily("Consolas"),
                                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                                TextWrapping = TextWrapping.Wrap,
                            };
                            Grid.SetColumn(msgCol, 1);
                            logRow.Children.Add(msgCol);

                            ffmpegPanel.Children.Add(logRow);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ffmpegPanel.Children.Clear();
                    ffmpegPanel.Children.Add(new TextBlock
                    {
                        Text = $"Failed to load: {ex.Message}",
                        FontSize = 11,
                        Foreground = (SolidColorBrush)Application.Current.Resources["ErrorBrush"],
                        FontFamily = new FontFamily("Consolas"),
                    });
                }
            }
        };
        var inlineTerminate = new Button
        {
            Content = "Terminate",
            Height = 28,
            Padding = new Thickness(9, 0, 9, 0),
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["ErrorBrush"],
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
        };
        inlineTerminate.Click += async (_, _) =>
        {
            try
            {
                await adminApi.TerminateSessionAsync(capturedSession.SessionId);
                toastService.Success("Session terminated");
                await ViewModel.LoadCommand.ExecuteAsync(null);
            }
            catch (Exception ex) { toastService.Error($"Terminate failed: {ex.Message}"); }
        };
        controlPanel.Children.Add(ffmpegToggle);
        controlPanel.Children.Add(inlineTerminate);
        controlPanel.Children.Add(actionBtn);
        Grid.SetColumn(controlPanel, 5);
        row.Children.Add(controlPanel);

        // Wrap row + ffmpeg panel in a container
        var wrapper = new StackPanel { Spacing = 0 };
        wrapper.Children.Add(row);
        wrapper.Children.Add(expandedPanel);

        return wrapper;
    }

    private static FrameworkElement BuildPlaybackSummaryLine(string label, string? decision, string value)
    {
        var line = new Grid { ColumnSpacing = 6 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 60,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = BuildDecisionBadge(decision);
        badge.Margin = new Thickness(0);
        badge.Padding = new Thickness(5, 1, 5, 1);
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(badge, 1);
        Grid.SetColumn(valueBlock, 2);
        line.Children.Add(labelBlock);
        line.Children.Add(badge);
        line.Children.Add(valueBlock);
        return line;
    }

    private static Border BuildTranscodeModeBadge(string label)
    {
        var software = label.Equals("SW", StringComparison.OrdinalIgnoreCase)
            || label.Equals("Audio SW", StringComparison.OrdinalIgnoreCase);
        var color = software ? Color.FromArgb(255, 248, 113, 113) : Color.FromArgb(255, 165, 243, 252);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(52, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
            },
        };
    }

    private FrameworkElement BuildPlaybackDetailsPanel(AdminSession session)
    {
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = $"Playback · {session.SessionId}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        var grid = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cards = new[]
        {
            BuildPlaybackDetailCard("Container", session.PlayMethod, AdminActivityViewModel.FormatSourceContainer(session), AdminActivityViewModel.FormatDeliveredContainer(session), AdminActivityViewModel.FormatContainerDetail(session), null),
            BuildPlaybackDetailCard("Video", session.VideoDecision ?? session.PlayMethod, AdminActivityViewModel.FormatVideoSummary(session), AdminActivityViewModel.FormatDeliveredVideo(session), AdminActivityViewModel.FormatVideoDetail(session), AdminActivityViewModel.FormatTranscodeMode(session)),
            BuildPlaybackDetailCard("Audio", session.AudioDecision ?? (session.TranscodeAudio ? "transcode" : session.PlayMethod), AdminActivityViewModel.FormatAudioSummary(session), AdminActivityViewModel.FormatDeliveredAudio(session), AdminActivityViewModel.FormatAudioDetail(session), session.VideoDecision == "transcode" ? null : AdminActivityViewModel.FormatTranscodeMode(session)),
        };
        for (var i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i); grid.Children.Add(cards[i]); }
        root.Children.Add(grid);
        return root;
    }

    private Border BuildPlaybackDetailCard(string label, string decision, string source, string delivered, string detail, string? mode)
    {
        var panel = new StackPanel { Spacing = 4 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
        header.Children.Add(BuildDecisionBadge(decision));
        panel.Children.Add(header);
        AddPlaybackDetailLine(panel, "Source", source);
        AddPlaybackDetailLine(panel, "Delivered", delivered);
        if (!string.IsNullOrWhiteSpace(mode)) AddPlaybackDetailLine(panel, "Mode", mode);
        AddPlaybackDetailLine(panel, "Detail", detail);
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 80, 100, 125)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = panel,
        };
    }

    private void AddPlaybackDetailLine(StackPanel panel, string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var name = new TextBlock { Text = label, FontSize = 10, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] };
        var text = new TextBlock { Text = value, FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"], TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(name, 0); Grid.SetColumn(text, 1);
        grid.Children.Add(name); grid.Children.Add(text);
        panel.Children.Add(grid);
    }

    private static Button MakeSmallIconButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Content = new FontIcon
            {
                Glyph = glyph, FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    /// <summary>
    /// Decision badge: text-[9px] with border, semi-transparent bg + colored text.
    /// Direct: bg-green-500/10 text-green-400 border-green-500/15
    /// Remux: bg-blue-500/10 text-blue-400 border-blue-500/15
    /// Transcode: bg-amber-500/10 text-amber-400 border-amber-500/15
    /// </summary>
    private static Border BuildDecisionBadge(string? decision)
    {
        Color bg, fg, border;
        string label;

        switch (decision?.ToLowerInvariant())
        {
            case "direct":
            case "copy":
                // bg-green-500/10 text-green-400 border-green-500/15
                bg = Color.FromArgb(26, 34, 197, 94);       // green-500 at 10%
                fg = Green400;
                border = Color.FromArgb(38, 34, 197, 94);   // green-500 at 15%
                label = "Direct";
                break;
            case "transcode":
                // bg-amber-500/10 text-amber-400 border-amber-500/15
                bg = Color.FromArgb(26, 245, 158, 11);      // amber-500 at 10%
                fg = Amber400;
                border = Color.FromArgb(38, 245, 158, 11);  // amber-500 at 15%
                label = "Transcode";
                break;
            case "remux":
                // bg-blue-500/10 text-blue-400 border-blue-500/15
                bg = Color.FromArgb(26, 59, 130, 246);      // blue-500 at 10%
                fg = Blue400;
                border = Color.FromArgb(38, 59, 130, 246);  // blue-500 at 15%
                label = "Remux";
                break;
            default:
                // Webui: neutral theme badge (muted-foreground)
                var mutedColor = ((SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]).Color;
                bg = Color.FromArgb(26, mutedColor.R, mutedColor.G, mutedColor.B);
                fg = mutedColor;
                border = Color.FromArgb(38, mutedColor.R, mutedColor.G, mutedColor.B);
                label = AdminActivityViewModel.FormatDecision(decision);
                break;
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 2)
        };
        badge.Child = new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg)
        };
        return badge;
    }

    // ===== IP Results =====

    private void RebuildIpResults()
    {
        var results = ViewModel.IPLookupResults;

        // Show "Searching..." while loading
        if (ViewModel.IpLookupLoading)
        {
            IpResultsPanel.Visibility = Visibility.Visible;
            IpResultsRows.Children.Clear();
            IpResultsRows.Children.Add(new TextBlock
            {
                Text = "Searching...",
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        // Empty state: search was performed (IP text non-empty) but no results
        if (results.Count == 0 && !string.IsNullOrWhiteSpace(ViewModel.IpLookupText))
        {
            IpResultsPanel.Visibility = Visibility.Visible;
            IpResultsRows.Children.Clear();
            IpResultsRows.Children.Add(new TextBlock
            {
                Text = $"No users found for {ViewModel.IpLookupText} in the last 30 days.",
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        if (results.Count == 0)
        {
            IpResultsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        IpResultsPanel.Visibility = Visibility.Visible;
        IpResultsRows.Children.Clear();

        foreach (var entry in results)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            // User column — clickable link to AdminUserDetailPage
            var capturedEntryUserId = entry.UserId;
            var userLink = new HyperlinkButton
            {
                Content = entry.Username ?? $"User #{entry.UserId}",
                Padding = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            userLink.Click += (_, _) => Frame.Navigate(typeof(AdminUserDetailPage), capturedEntryUserId);
            Grid.SetColumn(userLink, 0);
            row.Children.Add(userLink);

            // First Seen / Last Seen: full locale datetime (not relative time)
            row.Children.Add(MakeCell(1, AdminActivityViewModel.FormatLocaleDateTime(entry.FirstSeen), false));
            row.Children.Add(MakeCell(2, AdminActivityViewModel.FormatLocaleDateTime(entry.LastSeen), false));

            var reqBlock = new TextBlock
            {
                Text = entry.RequestCount.ToString("N0"),
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(reqBlock, 3);
            row.Children.Add(reqBlock);

            IpResultsRows.Children.Add(row);
        }
    }

    private static TextBlock MakeCell(int col, string text, bool bold)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = bold ? FontWeights.Medium : FontWeights.Normal,
            Foreground = bold
                ? (SolidColorBrush)Application.Current.Resources["AccentBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    // ===== Color Helpers =====

    private static Color GetMethodBarColor(string method) => method switch
    {
        "direct" => Green500,
        "remux" => Blue500,
        "transcode" => Amber500,
        _ => Color.FromArgb(255, 100, 100, 100)
    };

    private static Color GetMethodDotColor(string method) => method switch
    {
        "direct" => Green400,
        "remux" => Blue400,
        "transcode" => Amber400,
        _ => Color.FromArgb(255, 100, 100, 100)
    };

    // ===== Filter button handlers =====

    private void MethodButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string method)
        {
            ViewModel.MethodFilter = ViewModel.MethodFilter == method ? null : method;
            UpdateSummaryStrip();
            UpdateFilterBar();
        }
    }

    private void NodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string node)
        {
            ViewModel.NodeFilter = ViewModel.NodeFilter == node ? null : node;
            UpdateSummaryStrip();
            UpdateFilterBar();
        }
    }

    private void BtnTypeMovie_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TypeFilter = ViewModel.TypeFilter == "movie" ? null : "movie";
        UpdateFilterBar();
    }

    private void BtnTypeSeries_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TypeFilter = ViewModel.TypeFilter == "series" ? null : "series";
        UpdateFilterBar();
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearFiltersCommand.Execute(null);
        UpdateSummaryStrip();
        UpdateFilterBar();
    }

    private void SearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchText = "";
        SearchBox.Text = "";
    }

    private async void IpLookupBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await RunIpLookupAsync(IpLookupBox.Text);
        }
    }

    private void IpLookupBox_TextChanged(object sender, TextChangedEventArgs e)
        => IpLookupButton.IsEnabled = !ViewModel.IpLookupLoading && !string.IsNullOrWhiteSpace(IpLookupBox.Text);

    private async void IpLookupButton_Click(object sender, RoutedEventArgs e)
        => await RunIpLookupAsync(IpLookupBox.Text);

    private async Task RunIpLookupAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        IpLookupExpander.IsExpanded = true;
        ViewModel.IpLookupText = ip.Trim();
        IpLookupButton.IsEnabled = false;
        await ViewModel.LookupIPCommand.ExecuteAsync(null);
        IpLookupButton.IsEnabled = !string.IsNullOrWhiteSpace(IpLookupBox.Text);
        RebuildIpResults();
    }
}
