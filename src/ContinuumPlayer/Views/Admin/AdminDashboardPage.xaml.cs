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
            titleText = session.SeriesName!;
            subtitleText = $"S{session.SeasonNumber} \u00b7 E{session.EpisodeNumber}";
            if (!string.IsNullOrEmpty(session.MediaTitle))
                subtitleText += $" \u2014 {session.MediaTitle}";
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
            CornerRadius = new CornerRadius(24),
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
        Grid.SetColumn(posterBorder, 0);

        // Right info column
        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

        // Title (text-sm font-bold = 14px bold)
        var titleBlock = new TextBlock
        {
            Text = titleText,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        infoStack.Children.Add(titleBlock);

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
        nameStack.Children.Add(new TextBlock
        {
            Text = $"{lib.Type ?? "unknown"} \u00b7 {pathCount} {(pathCount == 1 ? "path" : "paths")}",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });

        // Scan button (RefreshCw ghost icon, 28x28)
        var scanButton = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon
            {
                Glyph = "\uE72C", // Refresh icon
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            },
            CornerRadius = new CornerRadius(4)
        };
        var libId = lib.Id;
        scanButton.Click += async (s, e) =>
        {
            try
            {
                await ViewModel.ScanLibraryAsync(libId);
            }
            catch { }
        };

        // Enabled dot (8px circle, green if enabled, gray if not)
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(lib.Enabled
                ? Color.FromArgb(255, 34, 197, 94)  // bg-green-500
                : Color.FromArgb(77, 160, 160, 160)),  // bg-muted-foreground/30
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
        return cardBorder;
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
        iconBox.Child = new FontIcon
        {
            Glyph = "\uE8B7", // Library icon
            FontSize = 16,
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

    private static FrameworkElement BuildUserRow(AdminUser user)
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 0, 6) };
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
        var title = !string.IsNullOrEmpty(session.MediaTitle) ? session.MediaTitle : $"File #{session.MediaFileId}";
        var username = !string.IsNullOrEmpty(session.Username) ? session.Username : $"User #{session.UserId}";

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
        textLine.Inlines.Add(titleRun);

        infoStack.Children.Add(textLine);

        // Line 2: timestamp (text-[10px] mt-0.5)
        var timeBlock = new TextBlock
        {
            Text = AdminDashboardViewModel.GetTimeAgo(session.StartedAt),
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 2, 0, 0)
        };
        infoStack.Children.Add(timeBlock);

        Grid.SetColumn(iconBorder, 0);
        Grid.SetColumn(infoStack, 1);
        row.Children.Add(iconBorder);
        row.Children.Add(infoStack);

        outerBorder.Child = row;
        return outerBorder;
    }
}
