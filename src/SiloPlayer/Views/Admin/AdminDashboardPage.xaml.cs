using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminDashboardPage : Page
{
    public AdminDashboardViewModel ViewModel { get; }

    // Event channel subscription for realtime refresh
    private IDisposable? _eventSubscription;
    private IDisposable? _scanEventSubscription;
    private EventChannelClient? _eventChannel;
    private DateTime _lastEventRefresh = DateTime.MinValue;
    private bool _loaded;
    private DispatcherTimer? _autoRefreshTimer;

    public AdminDashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminDashboardViewModel>();
        this.InitializeComponent();
    }

    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => AdminPageContent.Width = Math.Min(1640, Math.Max(0, e.NewSize.Width));

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        if (ViewModel.HasCachedData)
        {
            BuildContent();
            LastUpdatedText.Text = "Updating…";
            LastUpdatedText.Visibility = Visibility.Visible;
        }
        else
        {
            BuildLoadingState();
        }

        // Subscribe to realtime session events for live Now Playing refresh
        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventChannel.SnapshotReceived += OnSnapshotReceived;
            _eventChannel.EventReceived += OnEventReceived;
            _eventSubscription = _eventChannel.Subscribe("sessions");
            _scanEventSubscription = _eventChannel.Subscribe("scans");
            if (_eventChannel.TryGetLatestSnapshot("scans", out var cachedScans))
                OnSnapshotReceived("scans", cachedScans);
        }
        catch { }

        await LoadDashboardProgressivelyAsync(manual: false);
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _autoRefreshTimer.Tick += async (_, _) =>
        {
            if (!ViewModel.IsLoading) await LoadDashboardProgressivelyAsync(manual: false);
        };
        _autoRefreshTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _autoRefreshTimer?.Stop();
        if (_eventChannel != null)
        {
            _eventChannel.SnapshotReceived -= OnSnapshotReceived;
            _eventChannel.EventReceived -= OnEventReceived;
        }
        _eventSubscription?.Dispose();
        _eventSubscription = null;
        _scanEventSubscription?.Dispose();
        _scanEventSubscription = null;
    }

    private async Task LoadDashboardProgressivelyAsync(bool manual)
    {
        var started = DateTime.UtcNow;
        ViewModel.IsLoading = true;
        ViewModel.ErrorMessage = null;
        if (manual) RefreshButtonLabel.Text = "Refreshing…";
        var errors = new List<string>();

        async Task LoadSectionAsync(Func<Task> load, Action render, string label)
        {
            try
            {
                await load();
                render();
            }
            catch (Exception ex) { errors.Add($"{label}: {ex.Message}"); }
        }

        await Task.WhenAll(
            LoadSectionAsync(ViewModel.LoadStatsSectionAsync, () => { UpdateStats(); BuildTraktActivity(); }, "Stats"),
            LoadSectionAsync(ViewModel.LoadSessionsSectionAsync, () => { UpdateStats(); BuildStreamCards(); BuildActivityItems(); }, "Sessions"),
            LoadSectionAsync(ViewModel.LoadLibrariesSectionAsync, BuildLibraryRows, "Libraries"),
            LoadSectionAsync(ViewModel.LoadUsersSectionAsync, BuildUserRows, "Users"));

        if (manual)
        {
            var remaining = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            RefreshButtonLabel.Text = "Refresh";
        }
        ViewModel.ErrorMessage = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
        ViewModel.IsLoading = false;
        LastUpdatedText.Text = "Updated less than 1 minute ago";
        LastUpdatedText.Visibility = Visibility.Visible;
        ScanAllButton.IsEnabled = ViewModel.Libraries.Count > 0;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLoading) return;
        RefreshButton.IsEnabled = false;
        await LoadDashboardProgressivelyAsync(manual: true);
        RefreshButton.IsEnabled = true;
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
        => await LoadDashboardProgressivelyAsync(manual: true);

    private async void ScanAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Libraries.Count == 0) return;
        ScanAllButton.IsEnabled = false;
        ScanAllButtonLabel.Text = "Starting scans…";
        await ViewModel.ScanAllCommand.ExecuteAsync(null);
        ScanAllButtonLabel.Text = "Scan All Libraries";
        ScanAllButton.IsEnabled = true;
    }

    private void ActivityLink_Tapped(object sender, TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(AdminActivityPage));

    private void ManageLibrariesLink_Tapped(object sender, TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(AdminLibrariesPage));

    private void ManageUsersLink_Tapped(object sender, TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(AdminUsersPage));

    private static readonly JsonSerializerOptions _scanJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private void OnSnapshotReceived(string channel, JsonElement data)
    {
        if (channel != "scans" || data.ValueKind != JsonValueKind.Array) return;
        try
        {
            var scans = data.Deserialize<List<AdminScanRun>>(_scanJsonOptions) ?? [];
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.ActiveScans = scans.Where(IsActiveScan).ToList();
                BuildLibraryRows();
            });
        }
        catch { }
    }

    private void OnEventReceived(string channel, string eventName, JsonElement data)
    {
        if (channel == "scans")
        {
            List<AdminScanRun>? snapshot = null;
            AdminScanRun? updated = null;
            try
            {
                if (eventName == "snapshot" && data.ValueKind == JsonValueKind.Array)
                    snapshot = data.Deserialize<List<AdminScanRun>>(_scanJsonOptions) ?? [];
                else if (data.ValueKind == JsonValueKind.Object)
                    updated = data.Deserialize<AdminScanRun>(_scanJsonOptions);
            }
            catch { return; }

            if (snapshot == null && updated == null) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (snapshot != null)
                    ViewModel.ActiveScans = snapshot.Where(IsActiveScan).ToList();
                else if (updated != null)
                {
                    ViewModel.ActiveScans = ViewModel.ActiveScans.Where(scan => scan.Id != updated.Id).ToList();
                    if (IsActiveScan(updated)) ViewModel.ActiveScans.Add(updated);
                }
                BuildLibraryRows();
            });
            return;
        }
        if (channel != "sessions") return;
        if ((DateTime.UtcNow - _lastEventRefresh).TotalMilliseconds < 1000) return;
        _lastEventRefresh = DateTime.UtcNow;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await ViewModel.RefreshSessionsOnlyAsync();
                UpdateStats();
                BuildStreamCards();
                BuildActivityItems();
            }
            catch { }
        });
    }

    private static bool IsActiveScan(AdminScanRun scan)
        => scan.Status is "accepted" or "running";

    private void BuildContent()
    {
        UpdateStats();
        BuildStreamCards();
        BuildLibraryRows();
        BuildUserRows();
        BuildActivityItems();
    }

    private void BuildLoadingState()
    {
        StatsGrid.Visibility = Visibility.Collapsed;
        StatsLoadingGrid.Visibility = Visibility.Visible;
        foreach (var value in new[] { StatActiveStreams, StatMovies, StatShows, StatUsers, StatStorage })
            value.Text = "";
        foreach (var detail in new[] { StatActiveStreamsSub, StatMoviesSub, StatShowsSub, StatUsersSub, StatStorageSub })
            detail.Text = "";

        TraktActivityCard.Visibility = Visibility.Collapsed;
        NowPlayingSection.Visibility = Visibility.Visible;
        StreamCardsGrid.Children.Clear();
        StreamCardsGrid.RowDefinitions.Clear();
        StreamCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < 2; column++)
        {
            var skeleton = new SiloPlayer.Controls.SkeletonBox
            {
                Height = 120,
                CornerRadius = new CornerRadius(16),
            };
            Grid.SetColumn(skeleton, column);
            StreamCardsGrid.Children.Add(skeleton);
        }
        RecentActivitySection.Visibility = Visibility.Collapsed;
        LibrariesPanel.Children.Clear();
        UsersPanel.Children.Clear();
        for (var i = 0; i < 5; i++)
        {
            LibrariesPanel.Children.Add(new SiloPlayer.Controls.SkeletonBox
            {
                Height = 64,
                CornerRadius = new CornerRadius(6),
            });
            UsersPanel.Children.Add(new SiloPlayer.Controls.SkeletonBox
            {
                Height = 42,
                CornerRadius = new CornerRadius(6),
            });
        }
    }

    private void UpdateStats()
    {
        StatsLoadingGrid.Visibility = Visibility.Collapsed;
        StatsGrid.Visibility = Visibility.Visible;
        var stats = ViewModel.Stats;
        var sessionCount = ViewModel.SessionCount;

        StatActiveStreams.Text = sessionCount.ToString();
        StatActiveStreamsSub.Text = $"{sessionCount} session{(sessionCount != 1 ? "s" : "")}";

        if (stats != null)
        {
            StatMovies.Text = stats.TotalMovies.ToString("N0");
            StatMoviesSub.Text = FormatFileCount(stats.TotalMovieFiles);

            StatShows.Text = stats.TotalShows.ToString("N0");
            StatShowsSub.Text = FormatFileCount(stats.TotalShowFiles);

            StatUsers.Text = stats.TotalUsers.ToString();
            StatUsersSub.Text = $"{stats.TotalUsers} registered";

            StatStorage.Text = ViewModel.StorageDisplay;
            StatStorageSub.Text = FormatFileCount(stats.TotalFiles);
        }
    }

    private static string FormatFileCount(long count)
        => count == 1 ? "1 file" : $"{count:N0} files";

    private void BuildTraktActivity()
    {
        TraktActivityContent.Children.Clear();
        var activity = ViewModel.Stats?.WatchProviderActivity;
        if (activity == null || (activity.TraktConnectedProfiles == 0 && activity.SyncRuns24h == 0 && activity.PendingExports == 0 && activity.OpenScrobbles == 0))
        {
            TraktActivityCard.Visibility = Visibility.Collapsed;
            return;
        }
        TraktActivityCard.Visibility = Visibility.Visible;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Trakt Activity", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] };
        var details = new HyperlinkButton { Content = "Task details ›", Padding = new Thickness(0), FontSize = 11 };
        details.Click += (_, _) => Frame.Navigate(typeof(AdminTaskDetailPage), "sync_watch_providers");
        Grid.SetColumn(title, 0); Grid.SetColumn(details, 1);
        header.Children.Add(title); header.Children.Add(details);
        TraktActivityContent.Children.Add(header);

        var metrics = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 4; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lastSync = string.IsNullOrWhiteSpace(activity.LastSyncCompletedAt) ? "Never" : AdminDashboardViewModel.GetTimeAgo(activity.LastSyncCompletedAt);
        var cards = new[]
        {
            MakeTraktMetric("Connected", activity.TraktConnectedProfiles.ToString("N0"), $"{activity.TraktEnabledProfiles:N0} enabled"),
            MakeTraktMetric("Last sync", lastSync, $"{activity.SyncRuns24h:N0} runs in 24h"),
            MakeTraktMetric("Imported", activity.ImportedWatched24h.ToString("N0"), $"{activity.ImportedProgress24h:N0} progress updates"),
            MakeTraktMetric("Exported", activity.ExportedWatched24h.ToString("N0"), $"{activity.PendingExports:N0} pending"),
        };
        for (var i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i); metrics.Children.Add(cards[i]); }
        TraktActivityContent.Children.Add(metrics);
        var errors = activity.SyncErrors24h + activity.FailedExports;
        var footer = new Grid
        {
            Padding = new Thickness(0, 12, 0, 0),
            ColumnSpacing = 8
        };
        for (var i = 0; i < 3; i++) footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.Children.Add(MakeTraktFooterMetric("Export enabled:", activity.TraktExportEnabled.ToString("N0"), false, 0));
        footer.Children.Add(MakeTraktFooterMetric("Scrobbling:", activity.TraktScrobbleEnabled.ToString("N0"), false, 1));
        footer.Children.Add(MakeTraktFooterMetric("Errors:", errors.ToString("N0"), errors > 0, 2));
        TraktActivityContent.Children.Add(new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footer
        });
    }

    private static FrameworkElement MakeTraktFooterMetric(string label, string value, bool destructive, int column)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = destructive
                ? new SolidColorBrush(Color.FromArgb(255, 220, 90, 90))
                : (Brush)Application.Current.Resources["PrimaryTextBrush"]
        });
        Grid.SetColumn(panel, column);
        return panel;
    }

    private Border MakeTraktMetric(string label, string value, string detail)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
        panel.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
        panel.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
        return new Border { BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = panel };
    }

    private void BuildStreamCards()
    {
        StreamCardsGrid.Children.Clear();
        StreamCardsGrid.RowDefinitions.Clear();

        var sessions = ViewModel.Sessions;
        if (sessions.Count == 0)
        {
            NowPlayingSection.Visibility = Visibility.Collapsed;
            return;
        }

        NowPlayingSection.Visibility = Visibility.Visible;
        NowPlayingViewAll.Text = $"View all {sessions.Count} stream{(sessions.Count != 1 ? "s" : "")} \u203a";

        var displayed = sessions.Take(4).ToList();
        int rows = (int)Math.Ceiling(displayed.Count / 2.0);
        for (int r = 0; r < rows; r++)
        {
            StreamCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < displayed.Count; i++)
        {
            var session = displayed[i];
            var card = BuildStreamCard(session);
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            StreamCardsGrid.Children.Add(card);
        }

        // Overflow link: "+X more active streams" when > 4 sessions
        if (sessions.Count > 4)
        {
            var overflowRow = rows;
            StreamCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var overflowLink = new HyperlinkButton
            {
                Content = new TextBlock
                {
                    Text = $"+{sessions.Count - 4} more active stream{(sessions.Count - 4 != 1 ? "s" : "")}",
                    FontSize = 13,
                    Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                },
                Padding = new Thickness(0),
                Margin = new Thickness(0, 8, 0, 0),
            };
            overflowLink.Click += (_, _) =>
            {
                var nav = App.Services.GetRequiredService<SiloPlayer.Helpers.NavigationService>();
                nav.Navigate<AdminActivityPage>();
            };
            Grid.SetRow(overflowLink, overflowRow);
            Grid.SetColumnSpan(overflowLink, 2);
            StreamCardsGrid.Children.Add(overflowLink);
        }
    }

    private Border BuildStreamCard(AdminSession session)
    {
        // Play method badge colors
        var playMethodBg = session.PlayMethod?.ToLowerInvariant() switch
        {
            "direct"    => Color.FromArgb(26, 34, 197, 94),   // bg-green-500/10
            "remux"     => Color.FromArgb(26, 59, 130, 246),   // bg-blue-500/10
            "transcode" => Color.FromArgb(26, 245, 158, 11),   // bg-amber-500/10
            _           => Color.FromArgb(40, 120, 120, 120)
        };
        var playMethodFg = session.PlayMethod?.ToLowerInvariant() switch
        {
            "direct"    => Color.FromArgb(255, 74, 222, 128),  // text-green-400
            "remux"     => Color.FromArgb(255, 96, 165, 250),  // text-blue-400
            "transcode" => Color.FromArgb(255, 251, 191, 36),  // text-amber-400
            _           => Color.FromArgb(255, 160, 160, 160)
        };
        var playMethodBorder = session.PlayMethod?.ToLowerInvariant() switch
        {
            "direct"    => Color.FromArgb(38, 34, 197, 94),    // border-green-500/15
            "remux"     => Color.FromArgb(38, 59, 130, 246),   // border-blue-500/15
            "transcode" => Color.FromArgb(38, 245, 158, 11),   // border-amber-500/15
            _           => Color.FromArgb(40, 120, 120, 120)
        };

        // Determine if episode based on SeriesName presence (matching web: session.series_name && season_number != null && episode_number != null)
        bool isEpisode = !string.IsNullOrEmpty(session.SeriesName)
                         && session.SeasonNumber != null
                         && session.EpisodeNumber != null;

        // Title and subtitle
        string titleText;
        string subtitleText;
        if (isEpisode)
        {
            titleText = !string.IsNullOrWhiteSpace(session.EpisodeName)
                ? session.EpisodeName
                : $"S{session.SeasonNumber}E{session.EpisodeNumber}";
            subtitleText = $"S{session.SeasonNumber} \u00b7 E{session.EpisodeNumber}";
            if (!string.IsNullOrEmpty(session.SeriesName))
                subtitleText += $" \u2014 {session.SeriesName}";
        }
        else
        {
            titleText = !string.IsNullOrEmpty(session.MediaTitle) ? session.MediaTitle : $"File #{session.MediaFileId}";
            subtitleText = session.MediaType?.ToLowerInvariant() == "movie" ? "Movie" : "Series";
        }

        // Avatar initial
        string username = !string.IsNullOrEmpty(session.Username) ? session.Username : $"User #{session.UserId}";
        string initial = username.Length > 0 ? username[0].ToString().ToUpper() : "?";

        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(14),
            BorderThickness = new Thickness(0)
        };

        // Main horizontal layout: poster + info with gap-3.5 (14px)
        var mainRow = new Grid { ColumnSpacing = 14 };
        mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Poster (70 wide, 2:3 = 105 tall)
        var posterBorder = new Border
        {
            Width = 70,
            Height = 105,
            CornerRadius = new CornerRadius(8),
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1)
        };

        if (!string.IsNullOrEmpty(session.PosterUrl))
        {
            try
            {
                var img = new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(session.PosterUrl)),
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                posterBorder.Child = img;
            }
            catch
            {
                posterBorder.Child = new FontIcon
                {
                    Glyph = "\uE768",
                    FontSize = 20,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
        else
        {
            posterBorder.Child = new FontIcon
            {
                Glyph = "\uE768",
                FontSize = 20,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (session.IsPaused && posterBorder.Child is FrameworkElement posterContent)
        {
            posterBorder.Child = null;
            posterContent.Opacity = 0.45;
            var pausedOverlay = new Grid();
            pausedOverlay.Children.Add(posterContent);
            pausedOverlay.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 16, 23, 34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 144, 160, 181)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 3, 7, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Paused",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                },
            });
            posterBorder.Child = pausedOverlay;
        }
        Grid.SetColumn(posterBorder, 0);

        // Right info column
        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

        // Title — clickable link to item detail when content_id exists (webui: <Link to="/item/{content_id}">)
        if (!string.IsNullOrEmpty(session.ContentId))
        {
            var contentId = session.ContentId;
            var titleLink = new HyperlinkButton
            {
                Content = new TextBlock
                {
                    Text = titleText,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1,
                },
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                Background = new SolidColorBrush(Colors.Transparent),
            };
            titleLink.Click += (_, _) =>
            {
                var nav = App.Services.GetRequiredService<SiloPlayer.Helpers.NavigationService>();
                nav.Navigate<SiloPlayer.Views.ItemDetailPage>(contentId);
            };
            infoStack.Children.Add(titleLink);
        }
        else
        {
            var titleBlock = new TextBlock
            {
                Text = titleText,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };
            infoStack.Children.Add(titleBlock);
        }

        // Subtitle (text-xs = 12px, mb-1.5 = 6px bottom margin)
        var subtitleBlock = new TextBlock
        {
            Text = subtitleText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 0, 0, 6)
        };
        infoStack.Children.Add(subtitleBlock);

        // Tags row (mb-1.5 = 6px bottom margin, gap-1 = 4px)
        var tagsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 6) };

        // Play method badge (text-[9px], rounded border px-1.5 py-0.5)
        var pmBadge = new Border
        {
            Background = new SolidColorBrush(playMethodBg),
            BorderBrush = new SolidColorBrush(playMethodBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2)
        };
        pmBadge.Child = new TextBlock
        {
            Text = session.PlayMethod ?? "unknown",
            FontSize = 9,
            Foreground = new SolidColorBrush(playMethodFg),
            FontWeight = FontWeights.SemiBold
        };
        tagsRow.Children.Add(pmBadge);

        var clientLabel = AdminActivityViewModel.GetSessionClientLabel(session);
        if (!string.IsNullOrWhiteSpace(clientLabel))
        {
            var clientBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(38, 128, 128, 128)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(96, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                MaxWidth = 144,
                Child = new TextBlock
                {
                    Text = clientLabel,
                    FontSize = 9,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            ToolTipService.SetToolTip(clientBadge, session.ClientUserAgent ?? clientLabel);
            tagsRow.Children.Add(clientBadge);
        }

        // Node badge (border-primary/10 bg-primary/5 text-primary)
        if (!string.IsNullOrEmpty(session.NodeDisplayName ?? session.ReportingNode))
        {
            var accentColor = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
            var nodeBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(13, accentColor.R, accentColor.G, accentColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(26, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            nodeBadge.Child = new TextBlock
            {
                Text = session.NodeDisplayName ?? session.ReportingNode,
                FontSize = 9,
                Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                FontWeight = FontWeights.SemiBold
            };
            tagsRow.Children.Add(nodeBadge);
        }

        // Profile badge (border-border bg-surface text-muted-foreground)
        if (!string.IsNullOrEmpty(session.ProfileName ?? session.ProfileId))
        {
            var profileBadge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            profileBadge.Child = new TextBlock
            {
                Text = session.ProfileName ?? session.ProfileId,
                FontSize = 9,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                FontWeight = FontWeights.SemiBold
            };
            tagsRow.Children.Add(profileBadge);
        }

        infoStack.Children.Add(tagsRow);

        // Bottom row: avatar + username + elapsed (mt-auto via stretching)
        var bottomRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatarBorder = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 0, 6, 0)
        };
        avatarBorder.Child = new TextBlock
        {
            Text = initial,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var usernameBlock = new TextBlock
        {
            Text = username,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var elapsedBlock = new TextBlock
        {
            Text = AdminDashboardViewModel.GetTimeAgo(session.StartedAt),
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(avatarBorder, 0);
        Grid.SetColumn(usernameBlock, 1);
        Grid.SetColumn(elapsedBlock, 2);
        bottomRow.Children.Add(avatarBorder);
        bottomRow.Children.Add(usernameBlock);
        bottomRow.Children.Add(elapsedBlock);

        infoStack.Children.Add(bottomRow);

        Grid.SetColumn(infoStack, 1);
        mainRow.Children.Add(posterBorder);
        mainRow.Children.Add(infoStack);

        card.Child = mainRow;
        return card;
    }

    private void BuildLibraryRows()
    {
        LibrariesPanel.Children.Clear();
        if (ViewModel.Libraries.Count == 0)
        {
            LibrariesPanel.Children.Add(new TextBlock
            {
                Text = "No libraries configured.",
                FontSize = 14,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 16)
            });
            return;
        }
        foreach (var lib in ViewModel.Libraries)
        {
            LibrariesPanel.Children.Add(BuildLibraryRow(lib));
        }
    }

    private FrameworkElement BuildLibraryRow(Library lib)
    {
        // Outer card: bg-surface border-border rounded-md border p-3
        var cardBorder = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12)
        };

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // icon/poster
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name + subtitle
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // scan button
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // enabled dot

        // Library icon or poster
        FrameworkElement iconElement;
        if (!string.IsNullOrEmpty(lib.PosterUrl))
        {
            try
            {
                var img = new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(lib.PosterUrl)),
                    Stretch = Stretch.UniformToFill,
                    Width = 56,
                    Height = 32
                };
                var imgBorder = new Border
                {
                    Width = 56,
                    Height = 32,
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    Child = img
                };
                iconElement = imgBorder;
            }
            catch
            {
                iconElement = BuildLibraryIconBox();
            }
        }
        else
        {
            iconElement = BuildLibraryIconBox();
        }
        iconElement.VerticalAlignment = VerticalAlignment.Center;

        // Name + subtitle "{lib.type} · {lib.paths.length} paths"
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = lib.Name,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var pathCount = lib.Paths?.Count ?? 0;
        var metadataRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        metadataRow.Children.Add(new TextBlock
        {
            Text = $"{lib.Type ?? "unknown"} \u00b7 {pathCount} {(pathCount == 1 ? "path" : "paths")}",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        var activeScans = ViewModel.ActiveScans.Where(scan => scan.LibraryId == lib.Id).ToList();
        if (activeScans.Count > 0)
        {
            var leading = activeScans.FirstOrDefault(scan => scan.Status == "running") ?? activeScans[0];
            metadataRow.Children.Add(new TextBlock
            {
                Text = "\u00b7",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            metadataRow.Children.Add(new TextBlock
            {
                Text = FormatDashboardLibraryScanProgress(leading, activeScans.Count),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 252, 211, 77)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 352,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        nameStack.Children.Add(metadataRow);

        // One stateful scan/stop button, matching the current WebUI.
        var scanButton = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = activeScans.Count > 0
                ? new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                : new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new SymbolIcon
            {
                Symbol = activeScans.Count > 0 ? Symbol.Stop : Symbol.SyncFolder,
                Foreground = activeScans.Count > 0
                    ? new SolidColorBrush(Colors.White)
                    : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            },
            CornerRadius = new CornerRadius(4)
        };
        var libId = lib.Id;
        ToolTipService.SetToolTip(scanButton, activeScans.Count > 0 ? "Stop Library Scans" : "Scan Library");
        scanButton.Click += async (s, e) =>
        {
            scanButton.IsEnabled = false;
            if (activeScans.Count > 0) await ViewModel.CancelLibraryScansAsync(libId);
            else await ViewModel.ScanLibraryAsync(libId);
            scanButton.IsEnabled = true;
        };

        // Active work is amber in the WebUI; otherwise enabled is green and disabled is muted.
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(activeScans.Count > 0
                ? Color.FromArgb(255, 245, 158, 11)
                : lib.Enabled
                    ? Color.FromArgb(255, 34, 197, 94)
                    : Color.FromArgb(77, 160, 160, 160)),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(iconElement, 0);
        Grid.SetColumn(nameStack, 1);
        Grid.SetColumn(scanButton, 2);
        Grid.SetColumn(dot, 3);
        row.Children.Add(iconElement);
        row.Children.Add(nameStack);
        row.Children.Add(scanButton);
        row.Children.Add(dot);

        cardBorder.Child = row;

        // Hover state
        cardBorder.PointerEntered += (s, _) => { if (s is Border b) b.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        cardBorder.PointerExited += (s, _) => { if (s is Border b) b.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"]; };

        return cardBorder;
    }

    private static string FormatDashboardScanMode(AdminScanRun scan) => scan.Mode switch
    {
        "library" => "Full library scan",
        "subtree" => "Subtree scan",
        "file" => "Single file scan",
        _ => scan.Mode,
    };

    private static string FormatDashboardLibraryScanProgress(AdminScanRun scan, int activeScanCount)
    {
        var status = scan.Status == "running" ? "Scanning" : "Queued";
        var detail = "";
        if (scan.Result is { } result)
        {
            if (result.TotalFiles > 0 && result.FilesProcessed > 0)
            {
                var percent = Math.Clamp(
                    (int)Math.Round(result.FilesProcessed * 100d / result.TotalFiles), 0, 100);
                detail = $"{(string.IsNullOrWhiteSpace(result.Message) ? "Processing files" : result.Message)} \u00b7 "
                    + $"{result.FilesProcessed:N0} / {result.TotalFiles:N0} ({percent}%)";
            }
            else
            {
                detail = result.Message ?? "";
            }
        }

        if (string.IsNullOrWhiteSpace(detail))
            detail = scan.Status == "running" ? FormatDashboardScanMode(scan) : "Waiting for capacity";

        var extra = activeScanCount > 1 ? $" + {activeScanCount - 1} more" : "";
        return $"{status}: {detail}{extra}";
    }

    private static Border BuildLibraryIconBox()
    {
        // 40x40 rounded-lg bordered icon (bg-primary/5 border-primary/10)
        var accentColor = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        var iconBox = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(13, accentColor.R, accentColor.G, accentColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(26, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1)
        };
        iconBox.Child = new SymbolIcon
        {
            Symbol = Symbol.Library,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return iconBox;
    }

    private void BuildUserRows()
    {
        UsersPanel.Children.Clear();
        if (ViewModel.Users.Count == 0)
        {
            UsersPanel.Children.Add(new TextBlock
            {
                Text = "No users.",
                FontSize = 14,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 16)
            });
            return;
        }

        // Table header row
        var headerRow = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 0, 0, 8) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerUser = new TextBlock
        {
            Text = "User",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var headerRole = new TextBlock
        {
            Text = "Role",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Width = 60,
            TextAlignment = TextAlignment.Center
        };
        var headerStatus = new TextBlock
        {
            Text = "Status",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Width = 70,
            TextAlignment = TextAlignment.Center
        };

        Grid.SetColumn(headerUser, 0);
        Grid.SetColumn(headerRole, 1);
        Grid.SetColumn(headerStatus, 2);
        headerRow.Children.Add(headerUser);
        headerRow.Children.Add(headerRole);
        headerRow.Children.Add(headerStatus);
        UsersPanel.Children.Add(headerRow);

        // Separator after header
        UsersPanel.Children.Add(new Border
        {
            Height = 1,
            Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var user in ViewModel.Users.Take(8))
        {
            UsersPanel.Children.Add(BuildUserRow(user));
        }
    }

    private FrameworkElement BuildUserRow(AdminUser user)
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 0, 6), Background = new SolidColorBrush(Colors.Transparent) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // User cell: avatar + username + email (gap-2.5 = 10px)
        var userCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };

        // Avatar circle (28px, text-[10px])
        string initial = user.Username.Length > 0 ? user.Username[0].ToString().ToUpper() : "?";
        var avatar = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        avatar.Child = new TextBlock
        {
            Text = initial,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Username + email stacked
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = user.Username,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = user.Email,
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        userCell.Children.Add(avatar);
        userCell.Children.Add(nameStack);

        // Role badge: "admin" = default variant (accent), "user" = secondary variant (muted)
        var isAdmin = user.Role?.ToLowerInvariant() == "admin";
        var roleBg = isAdmin
            ? ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color
            : Color.FromArgb(255, 50, 50, 55);
        var roleFg = isAdmin
            ? ((SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"]).Color
            : Color.FromArgb(255, 180, 180, 180);

        var roleBadge = new Border
        {
            Background = new SolidColorBrush(roleBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 60,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        roleBadge.Child = new TextBlock
        {
            Text = user.Role ?? "user",
            FontSize = 11,
            Foreground = new SolidColorBrush(roleFg),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Status badge: enabled = "Active" outline, disabled = "Disabled" destructive
        Border statusBadge;
        if (user.Enabled)
        {
            statusBadge = new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Colors.Transparent),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 70,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Active",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }
        else
        {
            statusBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 185, 28, 28)),  // destructive red
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 70,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Disabled",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        Grid.SetColumn(userCell, 0);
        Grid.SetColumn(roleBadge, 1);
        Grid.SetColumn(statusBadge, 2);
        row.Children.Add(userCell);
        row.Children.Add(roleBadge);
        row.Children.Add(statusBadge);

        // Clickable → user detail (webui: row navigates to /admin/users/{id})
        var capturedUserId = user.Id;
        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new SolidColorBrush(Colors.Transparent); };
        row.Tapped += (_, _) =>
        {
            var frame = App.Services.GetRequiredService<SiloPlayer.Helpers.NavigationService>();
            frame.Navigate<AdminUserDetailPage>(capturedUserId);
        };

        return row;
    }

    private void BuildActivityItems()
    {
        ActivityPanel.Children.Clear();

        var sessions = ViewModel.Sessions;
        if (sessions.Count == 0)
        {
            RecentActivitySection.Visibility = Visibility.Collapsed;
            return;
        }

        RecentActivitySection.Visibility = Visibility.Visible;

        foreach (var session in sessions.Take(10))
        {
            ActivityPanel.Children.Add(BuildActivityItem(session));
        }
    }

    private FrameworkElement BuildActivityItem(AdminSession session)
    {
        var isEpisode = !string.IsNullOrWhiteSpace(session.SeriesName)
            && session.SeasonNumber != null
            && session.EpisodeNumber != null;
        var title = isEpisode
            ? (!string.IsNullOrWhiteSpace(session.EpisodeName)
                ? session.EpisodeName
                : $"S{session.SeasonNumber}E{session.EpisodeNumber}")
            : (!string.IsNullOrEmpty(session.MediaTitle) ? session.MediaTitle : $"File #{session.MediaFileId}");
        var username = !string.IsNullOrEmpty(session.Username) ? session.Username : $"User #{session.UserId}";
        var profileDisplay = session.ProfileName ?? session.ProfileId;
        var clientLabel = AdminActivityViewModel.GetSessionClientLabel(session);

        // Outer container with bottom border (border-b border-border/30 py-2.5)
        var outerBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(77, 128, 128, 128)), // border-border/30
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 10, 0, 10)
        };

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Play icon in 30x30 rounded-lg bordered box (bg-primary/5 border-primary/10)
        var accentColor = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        var iconBorder = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(13, accentColor.R, accentColor.G, accentColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(26, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top
        };
        iconBorder.Child = new FontIcon
        {
            Glyph = "\uE768",
            FontSize = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Info column: two lines
        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        // Line 1: rich text "{username} started watching {title}"
        // Using a horizontal StackPanel with inline TextBlocks since RichTextBlock is complex
        var textLine = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // Username span (text-foreground font-semibold)
        var usernameRun = new Microsoft.UI.Xaml.Documents.Run
        {
            Text = username,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        textLine.Inlines.Add(usernameRun);

        if (!string.IsNullOrWhiteSpace(profileDisplay))
        {
            textLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"  {profileDisplay}",
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            });
        }

        // " started watching " span (text-muted-foreground text-xs)
        var middleRun = new Microsoft.UI.Xaml.Documents.Run
        {
            Text = " started watching ",
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };
        textLine.Inlines.Add(middleRun);

        // Title span (text-foreground font-semibold)
        var titleRun = new Microsoft.UI.Xaml.Documents.Run
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        var historyLink = new Microsoft.UI.Xaml.Documents.Hyperlink();
        historyLink.Inlines.Add(titleRun);
        var historyFilter = new AdminPlaybackHistoryFilter(session.UserId,
            string.IsNullOrWhiteSpace(session.ProfileId) ? null : session.ProfileId);
        historyLink.Click += (_, _) => Frame.Navigate(typeof(AdminPlaybackHistoryPage), historyFilter);
        textLine.Inlines.Add(historyLink);

        infoStack.Children.Add(textLine);

        // Line 2: timestamp (text-[10px] mt-0.5)
        var timeBlock = new TextBlock
        {
            Text = string.Join(" \u00b7 ", new[]
            {
                AdminDashboardViewModel.GetTimeAgo(session.StartedAt),
                clientLabel,
            }.Where(value => !string.IsNullOrWhiteSpace(value))),
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 2, 0, 0)
        };
        infoStack.Children.Add(timeBlock);

        // AdminSessionActions — compact MenuFlyout matching webui.
        // Pause/Resume, Stop, Message, Terminate (destructive).
        var actionBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE712", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] },
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var flyout = new MenuFlyout();

        var capturedSession = session;
        var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();

        var pauseItem = new MenuFlyoutItem
        {
            Text = session.IsPaused ? "Resume" : "Pause",
            Icon = new FontIcon { Glyph = session.IsPaused ? "\uE768" : "\uE769" },
        };
        pauseItem.Click += async (_, _) =>
        {
            try
            {
                if (capturedSession.IsPaused)
                    await adminApi.ResumeSessionAsync(capturedSession.SessionId);
                else
                    await adminApi.PauseSessionAsync(capturedSession.SessionId);
            }
            catch { }
        };
        flyout.Items.Add(pauseItem);

        var stopItem = new MenuFlyoutItem
        {
            Text = "Stop",
            Icon = new FontIcon { Glyph = "\uE71A" },
        };
        stopItem.Click += async (_, _) =>
        {
            try { await adminApi.StopSessionAsync(capturedSession.SessionId); }
            catch { }
        };
        flyout.Items.Add(stopItem);

        var msgItem = new MenuFlyoutItem
        {
            Text = "Message",
            Icon = new FontIcon { Glyph = "\uE8BD" },
        };
        msgItem.Click += async (_, _) =>
        {
            var msgBox = new TextBox { PlaceholderText = "Message to display on player", AcceptsReturn = true, Height = 80 };
            var dlg = new ContentDialog
            {
                Title = "Send Message",
                Content = msgBox,
                PrimaryButtonText = "Send",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(msgBox.Text))
            {
                try { await adminApi.MessageSessionAsync(capturedSession.SessionId, msgBox.Text.Trim()); }
                catch { }
            }
        };
        flyout.Items.Add(msgItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var terminateItem = new MenuFlyoutItem
        {
            Text = "Terminate",
            Icon = new FontIcon { Glyph = "\uE74D" },
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 90, 90)),
        };
        terminateItem.Click += async (_, _) =>
        {
            var dlg = new ContentDialog
            {
                Title = "Terminate Session",
                Content = "This will forcefully terminate the session. This action cannot be undone.",
                PrimaryButtonText = "Terminate",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                try { await adminApi.TerminateSessionAsync(capturedSession.SessionId); }
                catch { }
            }
        };
        flyout.Items.Add(terminateItem);

        actionBtn.Flyout = flyout;

        Grid.SetColumn(iconBorder, 0);
        Grid.SetColumn(infoStack, 1);
        Grid.SetColumn(actionBtn, 2);
        row.Children.Add(iconBorder);
        row.Children.Add(infoStack);
        row.Children.Add(actionBtn);

        outerBorder.Child = row;
        return outerBorder;
    }
}
