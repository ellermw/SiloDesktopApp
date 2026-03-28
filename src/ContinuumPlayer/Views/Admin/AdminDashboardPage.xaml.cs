using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminDashboardPage : Page
{
    public AdminDashboardViewModel ViewModel { get; }

    public AdminDashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminDashboardViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            BuildContent();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void BuildContent()
    {
        UpdateStats();
        BuildStreamCards();
        BuildLibraryRows();
        BuildUserRows();
        BuildActivityItems();
    }

    private void UpdateStats()
    {
        var stats = ViewModel.Stats;
        var sessionCount = ViewModel.SessionCount;

        StatActiveStreams.Text = sessionCount.ToString();
        StatActiveStreamsSub.Text = $"{sessionCount} session{(sessionCount != 1 ? "s" : "")}";

        if (stats != null)
        {
            StatMovies.Text = stats.TotalMovies.ToString();
            StatMoviesSub.Text = $"of {stats.TotalItems} items";

            StatShows.Text = stats.TotalShows.ToString();
            StatShowsSub.Text = $"{stats.TotalFiles} files total";

            StatUsers.Text = stats.TotalUsers.ToString();
            StatUsersSub.Text = $"{stats.TotalUsers} registered";

            StatStorage.Text = ViewModel.StorageDisplay;
            StatStorageSub.Text = $"{stats.TotalFiles} files";
        }
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
    }

    private Border BuildStreamCard(AdminSession session)
    {
        // Play method badge color
        var playMethodBg = session.PlayMethod?.ToLowerInvariant() switch
        {
            "direct"    => Color.FromArgb(40, 63, 185, 80),
            "remux"     => Color.FromArgb(40, 56, 139, 253),
            "transcode" => Color.FromArgb(40, 210, 153, 34),
            _           => Color.FromArgb(40, 120, 120, 120)
        };
        var playMethodFg = session.PlayMethod?.ToLowerInvariant() switch
        {
            "direct"    => Color.FromArgb(255, 63, 185, 80),
            "remux"     => Color.FromArgb(255, 88, 166, 255),
            "transcode" => Color.FromArgb(255, 210, 153, 34),
            _           => Color.FromArgb(255, 160, 160, 160)
        };

        // Subtitle line
        string subtitleLine;
        if (session.MediaType?.ToLowerInvariant() == "episode" && session.SeriesName != null)
            subtitleLine = $"{session.SeriesName} · S{session.SeasonNumber:D2}E{session.EpisodeNumber:D2}";
        else
            subtitleLine = "Movie";

        // Avatar initial
        string initial = session.Username.Length > 0 ? session.Username[0].ToString().ToUpper() : "?";

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 21, 30, 43)), // CardBackgroundColor
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(14),
            BorderThickness = new Thickness(0)
        };

        var outerStack = new StackPanel { Spacing = 10 };

        // Top row: poster + info
        var topRow = new Grid { ColumnSpacing = 12 };
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Poster placeholder (70 wide, 2:3 = 105 tall)
        var posterBorder = new Border
        {
            Width = 70,
            Height = 105,
            CornerRadius = new CornerRadius(8),
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"]
        };
        var posterIcon = new FontIcon
        {
            Glyph = "\uE768",
            FontSize = 20,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        posterBorder.Child = posterIcon;
        Grid.SetColumn(posterBorder, 0);

        // Right info column
        var infoStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top };

        var titleBlock = new TextBlock
        {
            Text = session.MediaTitle,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        };

        var subtitleBlock = new TextBlock
        {
            Text = subtitleLine,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

        // Tags row
        var tagsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        // Play method badge
        var pmBadge = new Border
        {
            Background = new SolidColorBrush(playMethodBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3)
        };
        pmBadge.Child = new TextBlock
        {
            Text = session.PlayMethod ?? "unknown",
            FontSize = 11,
            Foreground = new SolidColorBrush(playMethodFg),
            FontWeight = FontWeights.SemiBold
        };
        tagsRow.Children.Add(pmBadge);

        // Node badge
        if (!string.IsNullOrEmpty(session.NodeDisplayName ?? session.ReportingNode))
        {
            var nodeBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 100, 100, 100)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3)
            };
            nodeBadge.Child = new TextBlock
            {
                Text = session.NodeDisplayName ?? session.ReportingNode,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            };
            tagsRow.Children.Add(nodeBadge);
        }

        // Profile badge
        if (!string.IsNullOrEmpty(session.ProfileName))
        {
            var profileBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 100, 100, 100)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3)
            };
            profileBadge.Child = new TextBlock
            {
                Text = session.ProfileName,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            };
            tagsRow.Children.Add(profileBadge);
        }

        // Bottom row: avatar + username + elapsed
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
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var usernameBlock = new TextBlock
        {
            Text = session.Username,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var elapsedBlock = new TextBlock
        {
            Text = AdminDashboardViewModel.GetTimeAgo(session.StartedAt),
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(avatarBorder, 0);
        Grid.SetColumn(usernameBlock, 1);
        Grid.SetColumn(elapsedBlock, 2);
        bottomRow.Children.Add(avatarBorder);
        bottomRow.Children.Add(usernameBlock);
        bottomRow.Children.Add(elapsedBlock);

        infoStack.Children.Add(titleBlock);
        infoStack.Children.Add(subtitleBlock);
        infoStack.Children.Add(tagsRow);
        infoStack.Children.Add(bottomRow);

        Grid.SetColumn(infoStack, 1);
        topRow.Children.Add(posterBorder);
        topRow.Children.Add(infoStack);

        outerStack.Children.Add(topRow);
        card.Child = outerStack;
        return card;
    }

    private void BuildLibraryRows()
    {
        LibrariesPanel.Children.Clear();
        foreach (var lib in ViewModel.Libraries)
        {
            LibrariesPanel.Children.Add(BuildLibraryRow(lib));
        }
    }

    private static FrameworkElement BuildLibraryRow(Library lib)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Library icon
        string glyph = lib.Type?.ToLowerInvariant() switch
        {
            "movie"  => "\uE8B2",
            "series" => "\uE7F4",
            _        => "\uE8B7"
        };
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Name + type
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = lib.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = lib.Type ?? "unknown",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });

        // Enabled dot
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(255, 63, 185, 80)),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(nameStack, 1);
        Grid.SetColumn(dot, 3);
        row.Children.Add(icon);
        row.Children.Add(nameStack);
        row.Children.Add(dot);

        return row;
    }

    private void BuildUserRows()
    {
        UsersPanel.Children.Clear();
        foreach (var user in ViewModel.Users.Take(8))
        {
            UsersPanel.Children.Add(BuildUserRow(user));
        }
    }

    private static FrameworkElement BuildUserRow(AdminUser user)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar circle
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
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Username + email
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
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Role badge
        var roleBg = user.Role?.ToLowerInvariant() == "admin"
            ? Color.FromArgb(40, 120, 174, 252)
            : Color.FromArgb(40, 100, 100, 100);
        var roleFg = user.Role?.ToLowerInvariant() == "admin"
            ? Color.FromArgb(255, 120, 174, 252)
            : Color.FromArgb(255, 160, 160, 160);

        var roleBadge = new Border
        {
            Background = new SolidColorBrush(roleBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        roleBadge.Child = new TextBlock
        {
            Text = user.Role ?? "user",
            FontSize = 11,
            Foreground = new SolidColorBrush(roleFg),
            FontWeight = FontWeights.SemiBold
        };

        // Status badge
        var statusBg = user.Enabled
            ? Color.FromArgb(40, 63, 185, 80)
            : Color.FromArgb(40, 200, 70, 70);
        var statusFg = user.Enabled
            ? Color.FromArgb(255, 63, 185, 80)
            : Color.FromArgb(255, 220, 90, 90);

        var statusBadge = new Border
        {
            Background = new SolidColorBrush(statusBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBadge.Child = new TextBlock
        {
            Text = user.Enabled ? "active" : "disabled",
            FontSize = 11,
            Foreground = new SolidColorBrush(statusFg),
            FontWeight = FontWeights.SemiBold
        };

        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(nameStack, 1);
        Grid.SetColumn(roleBadge, 2);
        Grid.SetColumn(statusBadge, 3);
        row.Children.Add(avatar);
        row.Children.Add(nameStack);
        row.Children.Add(roleBadge);
        row.Children.Add(statusBadge);

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

    private static FrameworkElement BuildActivityItem(AdminSession session)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Play icon in bordered box
        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = new FontIcon
        {
            Glyph = "\uE768",
            FontSize = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Activity text
        var activityText = new TextBlock
        {
            Text = $"{session.Username} started watching {session.MediaTitle}",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Timestamp
        var timeBlock = new TextBlock
        {
            Text = AdminDashboardViewModel.GetTimeAgo(session.StartedAt),
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(iconBorder, 0);
        Grid.SetColumn(activityText, 1);
        Grid.SetColumn(timeBlock, 2);
        row.Children.Add(iconBorder);
        row.Children.Add(activityText);
        row.Children.Add(timeBlock);

        return row;
    }
}
