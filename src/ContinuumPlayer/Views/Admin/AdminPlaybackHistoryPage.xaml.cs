using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;
using System.Collections.Specialized;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminPlaybackHistoryPage : Page
{
    public AdminPlaybackHistoryViewModel ViewModel { get; }

    // Track whether we're programmatically updating comboboxes to avoid feedback loops
    private bool _suppressFilterEvents;
    private bool _rebuildItemsPending;
    private bool _rebuildUsersPending;
    private bool _rebuildProfilesPending;

    public AdminPlaybackHistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminPlaybackHistoryViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
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
        ResetButton.Visibility = ViewModel.HasActiveFilters ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Filter event handlers =====

    private async void UserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (UserComboBox.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedUserId = item.Tag is int id ? id : (int?)null;
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

        var items = ViewModel.Items;
        if (items.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var item in items)
        {
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
        mediaStack.Children.Add(new TextBlock
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
        var userBlock = new TextBlock
        {
            Text = !string.IsNullOrEmpty(item.Username) ? item.Username : $"User #{item.UserId}",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(userBlock, 1);

        // Col 2: Profile (name + full profile_id as subtitle)
        var profileStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        profileStack.Children.Add(new TextBlock
        {
            Text = !string.IsNullOrEmpty(item.ProfileName) ? item.ProfileName : item.ProfileId,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(item.ProfileName) && !string.IsNullOrEmpty(item.ProfileId))
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

        var ffmpegLogsBtn = new Button
        {
            Content = "FFmpeg Logs",
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
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

        return row;
    }

    // Navigate to AdminLogsPage with the given playback session ID
    private void NavigateToLogs(string sessionId, bool ffmpegFilter)
    {
        var param = ffmpegFilter ? $"{sessionId}|ffmpeg" : sessionId;
        Frame.Navigate(typeof(AdminLogsPage), param);
    }

    private static Border BuildMethodBadge(string? playMethod)
    {
        Color bg;
        Color fg;
        string label;

        switch (playMethod?.ToLowerInvariant())
        {
            case "direct":
                bg = Color.FromArgb(40, 63, 185, 80);
                fg = Color.FromArgb(255, 63, 185, 80);
                label = "direct";
                break;
            case "remux":
                bg = Color.FromArgb(40, 56, 139, 253);
                fg = Color.FromArgb(255, 56, 139, 253);
                label = "remux";
                break;
            case "transcode":
                bg = Color.FromArgb(40, 219, 109, 40);
                fg = Color.FromArgb(255, 219, 109, 40);
                label = "transcode";
                break;
            default:
                bg = Color.FromArgb(60, 120, 120, 120);
                fg = Color.FromArgb(255, 160, 160, 160);
                label = playMethod ?? "unknown";
                break;
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3)
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

    private static Border BuildStatusBadge(bool completed)
    {
        Color bg;
        Color fg;
        string label;

        if (completed)
        {
            // variant="default" — filled accent
            bg = Color.FromArgb(40, 63, 185, 80);
            fg = Color.FromArgb(255, 63, 185, 80);
            label = "Completed";
        }
        else
        {
            // variant="outline" — subtle / muted
            bg = Color.FromArgb(60, 120, 120, 120);
            fg = Color.FromArgb(255, 160, 160, 160);
            label = "Partial";
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3)
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
