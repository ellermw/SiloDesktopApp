using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.ViewModels.Admin;
using System.Collections.Specialized;

namespace SiloPlayer.Views.Admin;

public sealed record AdminPlaybackHistoryFilter(int? UserId = null, string? ProfileId = null);

public sealed partial class AdminPlaybackHistoryPage : Page
{
    public AdminPlaybackHistoryViewModel ViewModel { get; }

    // Track whether we're programmatically updating comboboxes to avoid feedback loops
    private bool _suppressFilterEvents;
    private bool _rebuildItemsPending;
    private bool _rebuildUsersPending;
    private bool _rebuildProfilesPending;

    // Polling timer for periodic refresh (no dedicated event channel for playback history)
    private DispatcherTimer? _refreshTimer;

    // Pagination state (client-side, mirrors web UI behavior)
    private int _page;
    private int _pageSize = 25;

    // Static filter persistence — restored when navigating back
    private static int? _persistedUserId;
    private static string? _persistedProfileId;
    private static string? _persistedCompletionFilter;

    public AdminPlaybackHistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminPlaybackHistoryViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Accept user_id/profile_id filter, or a bare user_id for legacy callers.
        if (e.Parameter is AdminPlaybackHistoryFilter filter)
        {
            ViewModel.SelectedUserId = filter.UserId;
            ViewModel.SelectedProfileId = filter.ProfileId;
        }
        else if (e.Parameter is int userId)
            ViewModel.SelectedUserId = userId;
        else if (e.Parameter is string paramStr && int.TryParse(paramStr, out var uid))
            ViewModel.SelectedUserId = uid;
        else
        {
            // Restore persisted filters when navigating back without explicit params
            ViewModel.SelectedUserId = _persistedUserId;
            ViewModel.SelectedProfileId = _persistedProfileId;
            ViewModel.CompletionFilter = _persistedCompletionFilter;
        }
    }

    protected override void OnNavigatingFrom(Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        // Persist current filters for when user navigates back
        _persistedUserId = ViewModel.SelectedUserId;
        _persistedProfileId = ViewModel.SelectedProfileId;
        _persistedCompletionFilter = ViewModel.CompletionFilter;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Responsive title sizing: clamp(32, 3vw, 48)
        this.SizeChanged += (_, args) =>
        {
            double w = args.NewSize.Width;
            double fs = Math.Clamp(w * 0.03, 32, 48);
            PageTitle.FontSize = fs;
        };

        ViewModel.Items.CollectionChanged += (_, _) => ScheduleRebuildItems();
        ViewModel.Users.CollectionChanged += (_, _) => ScheduleRebuildUsers();
        ViewModel.Profiles.CollectionChanged += (_, _) => ScheduleRebuildProfiles();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.IsLoading))
                RefreshStatusText.Text = ViewModel.IsLoading ? "Refreshing..." : "Auto-refreshing";
        };

        await ViewModel.LoadCommand.ExecuteAsync(null);
        RebuildAll();

        // Start a 30-second polling timer (no dedicated event channel for playback history)
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) =>
        {
            try { await ViewModel.LoadCommand.ExecuteAsync(null); }
            catch { }
        };
        _refreshTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    private void ScheduleRebuildItems()
    {
        if (_rebuildItemsPending) return;
        _rebuildItemsPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildItemsPending = false;
            UpdateStatCards();
            RebuildHistoryTable();
        });
    }

    private void ScheduleRebuildUsers()
    {
        if (_rebuildUsersPending) return;
        _rebuildUsersPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildUsersPending = false;
            RebuildUserComboBox();
        });
    }

    private void ScheduleRebuildProfiles()
    {
        if (_rebuildProfilesPending) return;
        _rebuildProfilesPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildProfilesPending = false;
            RebuildProfileComboBox();
        });
    }

    private void RebuildAll()
    {
        RebuildUserComboBox();
        RebuildProfileComboBox();
        UpdateStatCards();
        RebuildHistoryTable();
        UpdateResetButton();
    }

    // ===== Stat Cards =====

    private void UpdateStatCards()
    {
        TotalCountText.Text = ViewModel.TotalCount.ToString();
        CompletedCountText.Text = ViewModel.CompletedCount.ToString();
        PartialCountText.Text = ViewModel.PartialCount.ToString();
    }

    // ===== User ComboBox =====

    private void RebuildUserComboBox()
    {
        _suppressFilterEvents = true;
        try
        {
            UserComboBox.Items.Clear();
            UserComboBox.Items.Add(new ComboBoxItem { Content = "All users", Tag = (object)"" });
            foreach (var user in ViewModel.Users)
                UserComboBox.Items.Add(new ComboBoxItem { Content = user.Username, Tag = (object)user.Id });

            // Restore selection
            if (ViewModel.SelectedUserId.HasValue)
            {
                foreach (ComboBoxItem item in UserComboBox.Items)
                {
                    if (item.Tag is int id && id == ViewModel.SelectedUserId.Value)
                    {
                        UserComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                UserComboBox.SelectedIndex = 0;
            }
        }
        finally { _suppressFilterEvents = false; }
    }

    private void RebuildProfileComboBox()
    {
        _suppressFilterEvents = true;
        try
        {
            ProfileComboBox.Items.Clear();
            ProfileComboBox.Items.Add(new ComboBoxItem { Content = "All profiles", Tag = (object)"" });
            foreach (var profile in ViewModel.Profiles)
                ProfileComboBox.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = (object)profile.Id });

            // Enable/disable based on user selection
            ProfileComboBox.IsEnabled = ViewModel.SelectedUserId.HasValue;
            if (!ViewModel.SelectedUserId.HasValue)
                ProfileComboBox.PlaceholderText = "Choose a user first";
            else
                ProfileComboBox.PlaceholderText = "All profiles";

            // Self-heal: if selected profile is no longer in the list, clear it
            if (!string.IsNullOrEmpty(ViewModel.SelectedProfileId)
                && !ViewModel.Profiles.Any(p => p.Id == ViewModel.SelectedProfileId))
            {
                ViewModel.SelectedProfileId = null;
            }

            // Restore selection
            if (!string.IsNullOrEmpty(ViewModel.SelectedProfileId))
            {
                foreach (ComboBoxItem item in ProfileComboBox.Items)
                {
                    if (item.Tag is string sid && sid == ViewModel.SelectedProfileId)
                    {
                        ProfileComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                ProfileComboBox.SelectedIndex = 0;
            }
        }
        finally { _suppressFilterEvents = false; }
    }

    // ===== Reset Button =====

    private void UpdateResetButton()
    {
        // Webui always shows Reset button (disabled when no filters active)
        ResetButton.Visibility = Visibility.Visible;
        ResetButton.IsEnabled = ViewModel.HasActiveFilters;
    }

    // ===== Filter event handlers =====

    private async void UserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (UserComboBox.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedUserId = item.Tag is int id ? id : (int?)null;
            _page = 0;
            // Rebuild profile dropdown after ViewModel loads profiles
            await ViewModel.LoadCommand.ExecuteAsync(null);
            RebuildProfileComboBox();
            UpdateResetButton();
        }
    }

    private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (ProfileComboBox.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedProfileId = item.Tag is string sid && !string.IsNullOrEmpty(sid) ? sid : null;
            _page = 0;
            await ViewModel.LoadCommand.ExecuteAsync(null);
            UpdateResetButton();
        }
    }

    private async void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (StatusComboBox.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            ViewModel.CompletionFilter = tag is "true" or "false" ? tag : null;
            _page = 0;
            await ViewModel.LoadCommand.ExecuteAsync(null);
            UpdateResetButton();
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilterEvents = true;
        try
        {
            ViewModel.ResetFiltersCommand.Execute(null);
            UserComboBox.SelectedIndex = 0;
            ProfileComboBox.SelectedIndex = 0;
            ProfileComboBox.IsEnabled = false;
            ProfileComboBox.PlaceholderText = "Choose a user first";
            StatusComboBox.SelectedIndex = 0;
        }
        finally { _suppressFilterEvents = false; }

        await ViewModel.LoadCommand.ExecuteAsync(null);
        RebuildProfileComboBox();
        UpdateResetButton();
    }

    // ===== History Table =====

    private void RebuildHistoryTable()
    {
        HistoryRowsPanel.Children.Clear();

        var allItems = ViewModel.Items;
        if (allItems.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            PaginationBar.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        // Clamp page to valid range
        int maxPage = Math.Max(0, (allItems.Count - 1) / _pageSize);
        if (_page > maxPage) _page = maxPage;
        if (_page < 0) _page = 0;

        int start = _page * _pageSize;
        int end = Math.Min(start + _pageSize, allItems.Count);

        bool first = true;
        for (int i = start; i < end; i++)
        {
            var item = allItems[i];
            if (!first)
            {
                HistoryRowsPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            first = false;
            HistoryRowsPanel.Children.Add(BuildHistoryRow(item, this));
        }

        // Pagination bar
        PaginationBar.Visibility = Visibility.Visible;
        PageRangeText.Text = $"Showing {start + 1}-{end} of {allItems.Count}";
        PrevPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = end < allItems.Count;
    }

    private void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 0)
        {
            _page--;
            RebuildHistoryTable();
        }
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        RebuildHistoryTable();
    }

    private void PageSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var size))
        {
            _pageSize = size;
            _page = 0;
            RebuildHistoryTable();
        }
    }

    private static FrameworkElement BuildHistoryRow(AdminPlaybackHistoryItem item, AdminPlaybackHistoryPage page)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 14, 20, 14),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        // Col 0: Media (title + type · session short)
        string title = !string.IsNullOrEmpty(item.MediaTitle)
            ? item.MediaTitle
            : !string.IsNullOrEmpty(item.MediaItemId)
                ? item.MediaItemId
                : $"File #{item.MediaFileId}";
        string sessionShort = item.SessionId.Length >= 8 ? item.SessionId[..8] : item.SessionId;

        var mediaStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        mediaStack.Children.Add(!string.IsNullOrWhiteSpace(item.MediaItemId)
            ? BuildLinkButton(
                title,
                13,
                FontWeights.SemiBold,
                (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                () => page.NavigateToItem(item.MediaItemId))
            : new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        mediaStack.Children.Add(new TextBlock
        {
            Text = $"{(string.IsNullOrEmpty(item.MediaType) ? "unknown" : item.MediaType)} · session {sessionShort}",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(mediaStack, 0);

        // Col 1: Username
        var userBlock = BuildLinkButton(
            !string.IsNullOrEmpty(item.Username) ? item.Username : $"User #{item.UserId}",
            13,
            FontWeights.Medium,
            (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            () => page.NavigateToUser(item.UserId));
        Grid.SetColumn(userBlock, 1);

        // Col 2: Profile (name + full profile_id as subtitle)
        var profileStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        profileStack.Children.Add(BuildLinkButton(
            !string.IsNullOrEmpty(item.ProfileName) ? item.ProfileName : item.ProfileId,
            13,
            FontWeights.Normal,
            (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            () => page.NavigateToProfileHistory(item.UserId, item.ProfileId)));
        // Always show profile_id as subline (webui always renders it)
        if (!string.IsNullOrEmpty(item.ProfileId))
        {
            profileStack.Children.Add(new TextBlock
            {
                Text = item.ProfileId,  // full profile_id per web UI
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(profileStack, 2);

        // Col 3: Play Method badge (secondary style = muted)
        var methodBadge = BuildMethodBadge(item.PlayMethod);
        methodBadge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(methodBadge, 3);

        // Col 4: Watch Time (formatDuration-style: Xh Ym | Ym Xs | Xs)
        var watchTimeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        watchTimeStack.Children.Add(new TextBlock
        {
            Text = FormatDuration(item.WatchedSeconds),
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        if (item.DurationSeconds.HasValue && item.DurationSeconds.Value > 0)
        {
            watchTimeStack.Children.Add(new TextBlock
            {
                Text = $"of {FormatDuration(item.DurationSeconds.Value)}",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }
        Grid.SetColumn(watchTimeStack, 4);

        // Col 5: Status badge (default=filled for Completed, outline=border-only for Partial)
        var statusBadge = BuildStatusBadge(item.Completed);
        statusBadge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(statusBadge, 5);

        // Col 6: Ended date + "started X ago"
        var endedStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        endedStack.Children.Add(new TextBlock
        {
            Text = AdminPlaybackHistoryViewModel.FormatDateTime(item.EndedAt),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        endedStack.Children.Add(new TextBlock
        {
            Text = $"started {AdminPlaybackHistoryViewModel.FormatRelative(item.StartedAt)}",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(endedStack, 6);

        // Col 7: Logs — "View Logs" and "FFmpeg Logs" links
        var logsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var capturedSessionId = item.SessionId;

        var viewLogsBtn = new Button
        {
            Content = "View Logs",
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"]
        };
        viewLogsBtn.Click += (_, _) => page.NavigateToLogs(capturedSessionId, false);

        var accentColor = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        var ffmpegLogsBtn = new Button
        {
            Content = "FFmpeg Logs",
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            // Webui: text-primary/80 — accent at 80% opacity
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, accentColor.R, accentColor.G, accentColor.B))
        };
        ffmpegLogsBtn.Click += (_, _) => page.NavigateToLogs(capturedSessionId, true);

        logsPanel.Children.Add(viewLogsBtn);
        logsPanel.Children.Add(ffmpegLogsBtn);
        Grid.SetColumn(logsPanel, 7);

        row.Children.Add(mediaStack);
        row.Children.Add(userBlock);
        row.Children.Add(profileStack);
        row.Children.Add(methodBadge);
        row.Children.Add(watchTimeStack);
        row.Children.Add(statusBadge);
        row.Children.Add(endedStack);
        row.Children.Add(logsPanel);

        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); };
        return row;
    }

    private static Button BuildLinkButton(string text, double fontSize, Windows.UI.Text.FontWeight fontWeight, Brush foreground, Action onClick)
    {
        var button = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = foreground,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void NavigateToItem(string mediaItemId)
    {
        if (string.IsNullOrWhiteSpace(mediaItemId)) return;
        App.MainWindowInstance?.RestoreMainPane();
        App.Services.GetRequiredService<SiloPlayer.Helpers.NavigationService>()
            .Navigate<SiloPlayer.Views.ItemDetailPage>(mediaItemId);
    }

    private void NavigateToUser(int userId)
    {
        if (userId <= 0) return;
        Frame.Navigate(typeof(AdminUserDetailPage), userId);
    }

    private void NavigateToProfileHistory(int userId, string profileId)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(profileId)) return;
        Frame.Navigate(typeof(AdminPlaybackHistoryPage), new AdminPlaybackHistoryFilter(userId, profileId));
    }

    // Navigate to AdminLogsPage with the given playback session ID
    private void NavigateToLogs(string sessionId, bool ffmpegFilter)
    {
        var param = ffmpegFilter ? $"{sessionId}|ffmpeg" : sessionId;
        Frame.Navigate(typeof(AdminLogsPage), param);
    }

    private static Border BuildMethodBadge(string? playMethod)
    {
        // Webui uses a single uniform "secondary" variant for all method badges —
        // no per-method color coding. Surface bg + secondary text.
        string label = playMethod?.ToLowerInvariant() switch
        {
            "direct" => "direct",
            "remux" => "remux",
            "transcode" => "transcode",
            _ => playMethod ?? "unknown",
        };

        var badge = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 2, 10, 2),
        };
        badge.Child = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        };
        return badge;
    }

    private static Border BuildStatusBadge(bool completed)
    {
        if (completed)
        {
            // Webui variant="default" — filled accent/blue badge
            var badge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"],
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 2, 10, 2),
            };
            badge.Child = new TextBlock
            {
                Text = "Completed",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            };
            return badge;
        }
        else
        {
            // Webui variant="outline" — transparent bg + 1px border
            var badge = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 2, 10, 2),
            };
            badge.Child = new TextBlock
            {
                Text = "Partial",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            };
            return badge;
        }
    }

    // formatDuration equivalent: Xh Ym | Ym Xs | Xs
    private static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds <= 0) return "0m";
        int rounded = Math.Max(0, (int)Math.Floor(seconds));
        int hours = rounded / 3600;
        int minutes = (rounded % 3600) / 60;
        int secs = rounded % 60;

        if (hours > 0) return $"{hours}h {minutes}m";
        if (minutes > 0) return $"{minutes}m {secs}s";
        return $"{secs}s";
    }
}
