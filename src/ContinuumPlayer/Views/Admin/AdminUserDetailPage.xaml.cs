using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminUserDetailPage : Page
{
    public AdminUserDetailViewModel ViewModel { get; }

    private int _userId;

    // Tab names
    private static readonly string[] TabNames = ["Overview", "Profiles", "Watch History", "IP History"];
    private readonly Button[] _tabButtons = new Button[4];
    private readonly UIElement[] _tabPanels;
    private int _activeTabIndex;

    public AdminUserDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminUserDetailViewModel>();
        this.InitializeComponent();

        _tabPanels = [OverviewPanel, ProfilesPanel, HistoryPanel, IPPanel];

        RetryButton.Click += async (_, _) => await LoadAsync();
        BackButton.Click  += (_, _) => GoBack();
        EditButton.Click  += async (_, _) => await OpenEditDialogAsync();
        DeleteButton.Click += async (_, _) => await OpenDeleteDialogAsync();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _userId = e.Parameter switch
        {
            int id  => id,
            string s when int.TryParse(s, out var id) => id,
            _ => 0
        };

        BuildTabBar();
        await LoadAsync();
    }

    // ===== Load =====

    private async Task LoadAsync()
    {
        if (_userId == 0) return;

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(_userId);

            if (ViewModel.User != null)
            {
                BuildHeader(ViewModel.User);
                BuildOverviewTab(ViewModel.User);
                BuildProfilesTab();
                BuildHistoryTab();
                BuildIPTab();
                SwitchTab(0);
            }
        }
        catch { }
    }

    // ===== Header =====

    private void BuildHeader(AdminUser user)
    {
        TitleBadgeRow.Children.Clear();

        TitleBadgeRow.Children.Add(new TextBlock
        {
            Text = user.Username,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        // Role badge
        bool isAdmin = user.Role?.ToLowerInvariant() == "admin";
        var roleBg  = isAdmin ? Color.FromArgb(40, 120, 174, 252) : Color.FromArgb(40, 100, 100, 100);
        var roleFg  = isAdmin ? Color.FromArgb(255, 120, 174, 252) : Color.FromArgb(255, 160, 160, 160);
        TitleBadgeRow.Children.Add(MakeBadge(user.Role ?? "user", roleBg, roleFg));

        // Status badge
        var statusBg     = user.Enabled ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(40, 200, 70, 70);
        var statusFg     = user.Enabled ? Color.FromArgb(255, 63, 185, 80) : Color.FromArgb(255, 220, 90, 90);
        var statusBorder = user.Enabled ? Color.FromArgb(120, 63, 185, 80) : Color.FromArgb(120, 200, 70, 70);
        var statusBadge  = MakeBadge(user.Enabled ? "Active" : "Disabled", statusBg, statusFg);
        statusBadge.BorderBrush = new SolidColorBrush(statusBorder);
        statusBadge.BorderThickness = new Thickness(1);
        TitleBadgeRow.Children.Add(statusBadge);

        EmailSubtitle.Text = user.Email;
    }

    // ===== Tab bar =====

    private void BuildTabBar()
    {
        TabBar.Children.Clear();
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Content = TabNames[i],
                Style   = (Style)Resources["TabButtonStyle"]
            };
            btn.Click += (_, _) => SwitchTab(idx);
            _tabButtons[i] = btn;
            TabBar.Children.Add(btn);
        }
    }

    private void SwitchTab(int index)
    {
        _activeTabIndex = index;

        var accentBg = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var secondFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        for (int i = 0; i < _tabButtons.Length; i++)
        {
            bool active = i == index;
            if (_tabButtons[i] == null) continue;
            _tabButtons[i].Background = active ? accentBg : new SolidColorBrush(Colors.Transparent);
            if (_tabButtons[i].Content is TextBlock tb)
                tb.Foreground = active ? accentFg : secondFg;
            else
                _tabButtons[i].Foreground = active ? accentFg : secondFg;
        }

        for (int i = 0; i < _tabPanels.Length; i++)
        {
            if (_tabPanels[i] != null)
                _tabPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ===== Overview tab =====

    private void BuildOverviewTab(AdminUser user)
    {
        AccountRows.Children.Clear();
        PermissionsRows.Children.Clear();

        // Account rows
        AddDetailRow(AccountRows, "Username", user.Username);
        AddDetailRow(AccountRows, "Email",    user.Email);
        AddDetailRow(AccountRows, "Role",     user.Role ?? "user");
        AddDetailRow(AccountRows, "Status",   user.Enabled ? "Active" : "Disabled");
        AddDetailRow(AccountRows, "Created",  AdminUserDetailViewModel.FormatDate(user.CreatedAt));
        AddDetailRow(AccountRows, "Updated",  AdminUserDetailViewModel.FormatDate(user.UpdatedAt));

        // Library access
        string libraryAccess = user.LibraryIds == null
            ? "All libraries"
            : user.LibraryIds.Count == 0
                ? "None"
                : string.Join(", ", user.LibraryIds.Select(id => $"#{id}"));

        AddDetailRow(PermissionsRows, "Library Access",       libraryAccess);
        AddDetailRow(PermissionsRows, "Max Playback Quality", string.IsNullOrEmpty(user.MaxPlaybackQuality) ? "Default" : user.MaxPlaybackQuality);
        AddDetailRow(PermissionsRows, "Max Streams",          user.MaxStreams == 0 ? "Unlimited" : user.MaxStreams.ToString());
        AddDetailRow(PermissionsRows, "Max Transcodes",       user.MaxTranscodes == 0 ? "Unlimited" : user.MaxTranscodes.ToString());
        AddDetailRow(PermissionsRows, "Downloads",            user.DownloadAllowed ? "Allowed" : "Not allowed");
        AddDetailRow(PermissionsRows, "Download Transcode",   user.DownloadTranscodeAllowed ? "Allowed" : "Not allowed");
    }

    private void AddDetailRow(StackPanel parent, string label, string value)
    {
        bool addSeparator = parent.Children.Count > 0;
        if (addSeparator)
        {
            parent.Children.Add(new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0)
            });
        }

        var row = new Grid { Padding = new Thickness(20, 10, 20, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text       = label,
            FontSize   = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var valueBlock = new TextBlock
        {
            Text       = value,
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 220,
            TextAlignment = TextAlignment.Right
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);

        parent.Children.Add(row);
    }

    // ===== Profiles tab =====

    private void BuildProfilesTab()
    {
        ProfilesContent.Children.Clear();

        if (ViewModel.Profiles.Count == 0)
        {
            ProfilesContent.Children.Add(new Border
            {
                Background    = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
                CornerRadius  = new CornerRadius(22),
                Padding       = new Thickness(20, 40, 20, 40),
                Child = new TextBlock
                {
                    Text = "No profiles found for this user.",
                    FontSize = 13,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });
            return;
        }

        // Grid-like layout using a WrapPanel-style approach via a UniformGrid-like StackPanel
        // WinUI 3 doesn't have a WrapPanel, use a scroll with a horizontal wrap via ItemsRepeater trick:
        // Build rows of 3 items manually.
        int cols = 3;
        var profiles = ViewModel.Profiles.ToList();

        for (int rowStart = 0; rowStart < profiles.Count; rowStart += cols)
        {
            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (int c = 0; c < cols; c++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (c < cols - 1)
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); // gap
            }

            for (int c = 0; c < cols; c++)
            {
                int idx = rowStart + c;
                int colIdx = c * 2;

                if (idx < profiles.Count)
                {
                    var card = BuildProfileCard(profiles[idx]);
                    Grid.SetColumn(card, colIdx);
                    rowGrid.Children.Add(card);
                }
                else
                {
                    // Empty spacer
                    var spacer = new Border { Visibility = Visibility.Collapsed };
                    Grid.SetColumn(spacer, colIdx);
                    rowGrid.Children.Add(spacer);
                }
            }

            ProfilesContent.Children.Add(rowGrid);
        }
    }

    private FrameworkElement BuildProfileCard(AdminUserProfile profile)
    {
        var card = new Border
        {
            Background      = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius    = new CornerRadius(16),
            BorderBrush     = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(16, 12, 16, 12)
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };

        // Circle icon
        var iconCircle = new Border
        {
            Width        = 36,
            Height       = 36,
            CornerRadius = new CornerRadius(18),
            Background   = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
        };
        iconCircle.Child = new FontIcon
        {
            Glyph      = "\uE77B",
            FontSize   = 18,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text       = profile.Name,
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        textStack.Children.Add(new TextBlock
        {
            Text       = profile.Id,
            FontSize   = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            FontFamily = new FontFamily("Consolas")
        });

        content.Children.Add(iconCircle);
        content.Children.Add(textStack);
        card.Child = content;
        return card;
    }

    // ===== Watch History tab =====

    private void BuildHistoryTab()
    {
        HistoryRows.Children.Clear();

        if (ViewModel.History.Count == 0)
        {
            HistoryEmpty.Visibility = Visibility.Visible;
            return;
        }

        HistoryEmpty.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var item in ViewModel.History)
        {
            if (!isFirst)
            {
                HistoryRows.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            HistoryRows.Children.Add(BuildHistoryRow(item));
        }
    }

    private FrameworkElement BuildHistoryRow(AdminPlaybackHistoryItem item)
    {
        var row = new Grid
        {
            Padding       = new Thickness(20, 10, 20, 10),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        // Media column
        string title = !string.IsNullOrEmpty(item.MediaTitle) ? item.MediaTitle
                     : !string.IsNullOrEmpty(item.MediaItemId) ? item.MediaItemId
                     : $"File #{item.MediaFileId}";

        var mediaStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        mediaStack.Children.Add(new TextBlock
        {
            Text         = title,
            FontSize     = 13,
            FontWeight   = FontWeights.SemiBold,
            Foreground   = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        mediaStack.Children.Add(new TextBlock
        {
            Text       = !string.IsNullOrEmpty(item.MediaType) ? item.MediaType : "unknown",
            FontSize   = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });

        // Profile column
        var profileBlock = new TextBlock
        {
            Text = !string.IsNullOrEmpty(item.ProfileName) ? item.ProfileName : item.ProfileId,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // Method badge
        var (methodBg, methodFg) = item.PlayMethod?.ToLowerInvariant() switch
        {
            "direct"    => (Color.FromArgb(40, 63, 185, 80),   Color.FromArgb(255, 63, 185, 80)),
            "remux"     => (Color.FromArgb(40, 120, 174, 252),  Color.FromArgb(255, 120, 174, 252)),
            "transcode" => (Color.FromArgb(40, 250, 160, 80),   Color.FromArgb(255, 250, 160, 80)),
            _           => (Color.FromArgb(40, 120, 120, 120),  Color.FromArgb(255, 160, 160, 160))
        };
        var methodBadge = new Border
        {
            Background    = new SolidColorBrush(methodBg),
            CornerRadius  = new CornerRadius(4),
            Padding       = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text       = item.PlayMethod ?? "",
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(methodFg)
            }
        };

        // Watch time
        string watched = AdminUserDetailViewModel.FormatWatchTime(item.WatchedSeconds);
        string total   = item.DurationSeconds.HasValue
            ? AdminUserDetailViewModel.FormatWatchTime(item.DurationSeconds.Value)
            : "—";

        var watchStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        watchStack.Children.Add(new TextBlock
        {
            Text       = watched,
            FontSize   = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        watchStack.Children.Add(new TextBlock
        {
            Text       = $"of {total}",
            FontSize   = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });

        // Status badge
        var statusBg = item.Completed
            ? Color.FromArgb(40, 63, 185, 80)
            : Color.FromArgb(40, 120, 120, 120);
        var statusFg = item.Completed
            ? Color.FromArgb(255, 63, 185, 80)
            : Color.FromArgb(255, 160, 160, 160);

        var statusBadge = new Border
        {
            Background   = new SolidColorBrush(statusBg),
            CornerRadius = new CornerRadius(4),
            Padding      = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text       = item.Completed ? "Completed" : "Partial",
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(statusFg)
            }
        };

        // Ended
        var endedStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        endedStack.Children.Add(new TextBlock
        {
            Text       = AdminUserDetailViewModel.FormatDateTime(item.EndedAt),
            FontSize   = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        endedStack.Children.Add(new TextBlock
        {
            Text       = $"started {AdminUserDetailViewModel.FormatRelative(item.StartedAt)}",
            FontSize   = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });

        Grid.SetColumn(mediaStack,   0);
        Grid.SetColumn(profileBlock, 1);
        Grid.SetColumn(methodBadge,  2);
        Grid.SetColumn(watchStack,   3);
        Grid.SetColumn(statusBadge,  4);
        Grid.SetColumn(endedStack,   5);

        row.Children.Add(mediaStack);
        row.Children.Add(profileBlock);
        row.Children.Add(methodBadge);
        row.Children.Add(watchStack);
        row.Children.Add(statusBadge);
        row.Children.Add(endedStack);

        return row;
    }

    // ===== IP History tab =====

    private void BuildIPTab()
    {
        IPRows.Children.Clear();

        if (ViewModel.IPs.Count == 0)
        {
            IPEmpty.Visibility = Visibility.Visible;
            return;
        }

        IPEmpty.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var entry in ViewModel.IPs)
        {
            if (!isFirst)
            {
                IPRows.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            IPRows.Children.Add(BuildIPRow(entry));
        }
    }

    private FrameworkElement BuildIPRow(UserIPEntry entry)
    {
        var row = new Grid
        {
            Padding       = new Thickness(20, 10, 20, 10),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var ipBlock = new TextBlock
        {
            Text       = entry.ClientIp,
            FontSize   = 13,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var firstSeenBlock = new TextBlock
        {
            Text       = AdminUserDetailViewModel.FormatDateTime(entry.FirstSeen),
            FontSize   = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var lastSeenBlock = new TextBlock
        {
            Text       = AdminUserDetailViewModel.FormatDateTime(entry.LastSeen),
            FontSize   = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var requestsBlock = new TextBlock
        {
            Text              = entry.RequestCount.ToString("N0"),
            FontSize          = 13,
            Foreground        = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Grid.SetColumn(ipBlock,        0);
        Grid.SetColumn(firstSeenBlock, 1);
        Grid.SetColumn(lastSeenBlock,  2);
        Grid.SetColumn(requestsBlock,  3);

        row.Children.Add(ipBlock);
        row.Children.Add(firstSeenBlock);
        row.Children.Add(lastSeenBlock);
        row.Children.Add(requestsBlock);

        return row;
    }

    // ===== Navigation =====

    private void GoBack()
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    // ===== Edit dialog =====

    private async Task OpenEditDialogAsync()
    {
        if (ViewModel.User == null) return;

        var user = ViewModel.User;
        var (formContent, getRequest) = BuildEditForm(user);

        var dialog = new ContentDialog
        {
            Title            = "Edit User",
            PrimaryButtonText = "Save",
            CloseButtonText  = "Cancel",
            XamlRoot         = this.XamlRoot,
            Content          = formContent,
            DefaultButton    = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var req = getRequest();
            if (req == null) return;
            try
            {
                await ViewModel.UpdateUserAsync(user.Id, req);
                await LoadAsync();
            }
            catch { }
        }
    }

    private (FrameworkElement Content, Func<UpdateUserRequest?> GetRequest) BuildEditForm(AdminUser user)
    {
        var usernameBox = new TextBox
        {
            PlaceholderText = "Username",
            Text            = user.Username,
            CornerRadius    = new CornerRadius(8),
            FontSize        = 13
        };

        var emailBox = new TextBox
        {
            PlaceholderText = "Email",
            Text            = user.Email,
            CornerRadius    = new CornerRadius(8),
            FontSize        = 13
        };

        var passwordBox = new PasswordBox
        {
            PlaceholderText = "Leave blank to keep current",
            CornerRadius    = new CornerRadius(8),
            FontSize        = 13
        };

        var roleCombo = new ComboBox
        {
            CornerRadius    = new CornerRadius(8),
            FontSize        = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        roleCombo.Items.Add("user");
        roleCombo.Items.Add("admin");
        roleCombo.SelectedItem = user.Role ?? "user";

        var enabledToggle = new ToggleSwitch
        {
            IsOn       = user.Enabled,
            OnContent  = "Enabled",
            OffContent = "Disabled"
        };

        var maxStreamsBox = new NumberBox
        {
            Value = user.MaxStreams,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var maxTranscodesBox = new NumberBox
        {
            Value = user.MaxTranscodes,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var downloadSwitch = new ToggleSwitch
        {
            IsOn       = user.DownloadAllowed,
            OnContent  = "Allowed",
            OffContent = "Not allowed"
        };

        var downloadTranscodeSwitch = new ToggleSwitch
        {
            IsOn       = user.DownloadTranscodeAllowed,
            OnContent  = "Allowed",
            OffContent = "Not allowed"
        };

        // Build form content
        var pivot = new Pivot
        {
            Margin   = new Thickness(-12, 0, -12, 0),
            FontSize = 13
        };

        // Account tab
        var accountTab = new StackPanel { Spacing = 14 };
        accountTab.Children.Add(MakeLabeledField("Username", usernameBox));
        accountTab.Children.Add(MakeLabeledField("Email", emailBox));
        accountTab.Children.Add(MakeLabeledField("Password (leave blank to keep current)", passwordBox));
        accountTab.Children.Add(MakeLabeledField("Role", roleCombo));
        accountTab.Children.Add(MakeSwitchRow("Account Status", enabledToggle));

        // Limits tab
        var limitsTab = new StackPanel { Spacing = 14 };
        limitsTab.Children.Add(MakeLabeledField("Max Streams (0 = unlimited)", maxStreamsBox));
        limitsTab.Children.Add(MakeLabeledField("Max Transcodes (0 = unlimited)", maxTranscodesBox));

        // Access tab
        var accessTab = new StackPanel { Spacing = 14 };
        accessTab.Children.Add(MakeSwitchRow("Downloads Allowed", downloadSwitch));
        accessTab.Children.Add(MakeSwitchRow("Download Transcode Allowed", downloadTranscodeSwitch));

        pivot.Items.Add(new PivotItem { Header = "Account", Content = accountTab });
        pivot.Items.Add(new PivotItem { Header = "Limits",  Content = limitsTab, Margin = new Thickness(0, 8, 0, 0) });
        pivot.Items.Add(new PivotItem { Header = "Access",  Content = accessTab, Margin = new Thickness(0, 8, 0, 0) });

        var container = new StackPanel { Width = 460 };
        container.Children.Add(pivot);

        UpdateUserRequest? GetRequest()
        {
            return new UpdateUserRequest
            {
                Username                 = usernameBox.Text.Trim(),
                Email                    = emailBox.Text.Trim(),
                Password                 = string.IsNullOrEmpty(passwordBox.Password) ? null : passwordBox.Password,
                Role                     = roleCombo.SelectedItem as string ?? "user",
                Enabled                  = enabledToggle.IsOn,
                MaxStreams                = double.IsNaN(maxStreamsBox.Value) ? 0 : (int)maxStreamsBox.Value,
                MaxTranscodes            = double.IsNaN(maxTranscodesBox.Value) ? 0 : (int)maxTranscodesBox.Value,
                DownloadAllowed          = downloadSwitch.IsOn,
                DownloadTranscodeAllowed = downloadTranscodeSwitch.IsOn,
                LibraryIds               = user.LibraryIds
            };
        }

        return (container, GetRequest);
    }

    // ===== Delete dialog =====

    private async Task OpenDeleteDialogAsync()
    {
        if (ViewModel.User == null) return;

        var dialog = new ContentDialog
        {
            Title             = "Delete User",
            Content           = $"Delete user \"{ViewModel.User.Username}\"? This action cannot be undone.",
            PrimaryButtonText  = "Delete",
            CloseButtonText   = "Cancel",
            XamlRoot          = this.XamlRoot,
            DefaultButton     = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.DeleteCommand.ExecuteAsync(null);
                GoBack();
            }
            catch { }
        }
    }

    // ===== Helpers =====

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background    = new SolidColorBrush(bg),
            CornerRadius  = new CornerRadius(4),
            Padding       = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text       = text,
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg)
            }
        };
    }

    private static FrameworkElement MakeLabeledField(string label, FrameworkElement field)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text       = label,
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        stack.Children.Add(field);
        return stack;
    }

    private static Border MakeSwitchRow(string label, ToggleSwitch toggle)
    {
        var border = new Border
        {
            BorderBrush     = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(12, 8, 12, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text              = label,
            FontSize          = 13,
            Foreground        = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(toggle,     1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(toggle);

        border.Child = grid;
        return border;
    }
}
