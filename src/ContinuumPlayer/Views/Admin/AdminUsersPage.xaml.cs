using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminUsersPage : Page
{
    public AdminUsersViewModel ViewModel { get; }
    public AdminInviteCodesViewModel InviteCodesViewModel { get; }
    private bool _inviteCodesLoaded;

    // B54: Playback quality options now live in ContinuumPlayer.Core.Helpers.PlaybackQuality.
    private static readonly (string Value, string Label, string Description)[] PlaybackQualityOptions
        = Core.Helpers.PlaybackQuality.Options;

    private bool _rebuildPending;

    public AdminUsersPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminUsersViewModel>();
        InviteCodesViewModel = App.Services.GetRequiredService<AdminInviteCodesViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Users.CollectionChanged += (_, _) => ScheduleRebuild();
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildPending = false;
            BuildUserRows();
        });
    }

    // ===== Tab Switching =====

    private void TabUsers_Click(object sender, RoutedEventArgs e)
    {
        UsersTabContent.Visibility = Visibility.Visible;
        InviteCodesTabContent.Visibility = Visibility.Collapsed;
        TabUsersIndicator.Visibility = Visibility.Visible;
        TabInviteCodesIndicator.Visibility = Visibility.Collapsed;
        TabUsersText.Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        TabUsersText.FontWeight = FontWeights.SemiBold;
        TabInviteCodesText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabInviteCodesText.FontWeight = FontWeights.Normal;
    }

    private async void TabInviteCodes_Click(object sender, RoutedEventArgs e)
    {
        UsersTabContent.Visibility = Visibility.Collapsed;
        InviteCodesTabContent.Visibility = Visibility.Visible;
        TabUsersIndicator.Visibility = Visibility.Collapsed;
        TabInviteCodesIndicator.Visibility = Visibility.Visible;
        TabInviteCodesText.Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        TabInviteCodesText.FontWeight = FontWeights.SemiBold;
        TabUsersText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabUsersText.FontWeight = FontWeights.Normal;

        if (!_inviteCodesLoaded)
        {
            _inviteCodesLoaded = true;
            InviteCodesViewModel.InviteCodes.CollectionChanged += (_, _) => ScheduleInviteCodesRebuild();
            InviteCodesLoading.IsActive = true;
            InviteCodesLoading.Visibility = Visibility.Visible;
            try { await InviteCodesViewModel.LoadCommand.ExecuteAsync(null); }
            catch (Exception ex) { ShowStatus($"Error loading invite codes: {ex.Message}"); }
            finally
            {
                InviteCodesLoading.IsActive = false;
                InviteCodesLoading.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ===== Table Builder =====

    private string _searchQuery = "";

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text ?? "";
        SearchClearButton.Visibility = string.IsNullOrEmpty(_searchQuery)
            ? Visibility.Collapsed : Visibility.Visible;
        BuildUserRows();
    }

    private void SearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
    }

    private int _userPageSize = 25;
    private int _userPage;

    private void BuildUserRows()
    {
        UsersPanel.Children.Clear();

        // Filter by search (username or email, case-insensitive) — matches web UI
        IEnumerable<AdminUser> filtered = ViewModel.Users;
        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            var q = _searchQuery.ToLowerInvariant();
            filtered = ViewModel.Users.Where(u =>
                (!string.IsNullOrEmpty(u.Username) && u.Username.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrEmpty(u.Email) && u.Email.ToLowerInvariant().Contains(q)));
        }
        var list = filtered.ToList();

        if (list.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            UserPaginationBar.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        // Paginate
        int totalPages = Math.Max(1, (int)Math.Ceiling(list.Count / (double)_userPageSize));
        if (_userPage >= totalPages) _userPage = totalPages - 1;
        if (_userPage < 0) _userPage = 0;
        int start = _userPage * _userPageSize;
        int end = Math.Min(start + _userPageSize, list.Count);
        var pageUsers = list.Skip(start).Take(_userPageSize).ToList();

        bool isFirst = true;
        foreach (var user in pageUsers)
        {
            if (!isFirst)
            {
                UsersPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            UsersPanel.Children.Add(BuildUserRow(user));
        }

        // Pagination bar
        UserPaginationBar.Children.Clear();
        UserPaginationBar.Visibility = list.Count > _userPageSize ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count > _userPageSize)
        {
            UserPaginationBar.Children.Add(new TextBlock
            {
                Text = $"Showing {start + 1}-{end} of {list.Count}",
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });

            var pageSizeCombo = new ComboBox { Width = 70, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            foreach (var ps in new[] { 25, 50, 100 })
            {
                var item = new ComboBoxItem { Content = ps.ToString(), Tag = ps };
                if (ps == _userPageSize) item.IsSelected = true;
                pageSizeCombo.Items.Add(item);
            }
            pageSizeCombo.SelectionChanged += (_, _) =>
            {
                if (pageSizeCombo.SelectedItem is ComboBoxItem sel && sel.Tag is int ps)
                { _userPageSize = ps; _userPage = 0; BuildUserRows(); }
            };
            UserPaginationBar.Children.Add(pageSizeCombo);

            var prevBtn = new Button { Content = new FontIcon { Glyph = "\uE76B", FontSize = 12 }, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(6), IsEnabled = _userPage > 0 };
            prevBtn.Click += (_, _) => { _userPage--; BuildUserRows(); };
            var nextBtn = new Button { Content = new FontIcon { Glyph = "\uE76C", FontSize = 12 }, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(6), IsEnabled = _userPage < totalPages - 1 };
            nextBtn.Click += (_, _) => { _userPage++; BuildUserRows(); };
            UserPaginationBar.Children.Add(prevBtn);
            UserPaginationBar.Children.Add(nextBtn);
        }
    }

    private FrameworkElement BuildUserRow(AdminUser user)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 12, 20, 12),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        // ---- Username as clickable link ----
        var capturedUser = user;
        var userNameLink = new HyperlinkButton
        {
            Content = user.Username,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            FontSize = 13,
            FontWeight = FontWeights.Medium
        };
        userNameLink.Click += (_, _) => Frame.Navigate(typeof(AdminUserDetailPage), capturedUser.Id);

        var userCell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        userCell.Children.Add(userNameLink);

        // ---- Email column ----
        var emailBlock = new TextBlock
        {
            Text = user.Email,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // ---- Role badge ----
        // admin = AccentBackgroundBrush bg / AccentBrush fg (default variant)
        // user = SurfaceBrush bg / SecondaryTextBrush fg (secondary variant)
        bool isAdmin = user.Role?.ToLowerInvariant() == "admin";

        var roleBadge = new Border
        {
            Background = isAdmin
                ? (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"]
                : (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        roleBadge.Child = new TextBlock
        {
            Text = user.Role ?? "user",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = isAdmin
                ? (SolidColorBrush)Application.Current.Resources["AccentBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

        // ---- Status badge ----
        // Active = outline style (BorderBrush border, transparent bg)
        // Disabled = ErrorBrush background
        Border statusBadge;
        if (user.Enabled)
        {
            statusBadge = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Active",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
            };
        }
        else
        {
            statusBadge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["ErrorBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Disabled",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            };
        }

        // ---- Last active ----
        var lastActiveBlock = new TextBlock
        {
            Text = FormatLastActive(user.LastActiveAt),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // ---- Actions: 28x28 ghost-style icon buttons ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var historyBtn = MakeGhostIconButton("\uE81C", "View history");
        var editBtn = MakeGhostIconButton("\uE70F", "Edit user");
        var deleteBtn = MakeGhostIconButton("\uE74D", "Delete user");

        // Wire up click handlers
        historyBtn.Click += (_, _) => Frame.Navigate(typeof(AdminPlaybackHistoryPage), capturedUser.Id);
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedUser);
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedUser);

        actionsPanel.Children.Add(historyBtn);
        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(userCell, 0);
        Grid.SetColumn(emailBlock, 1);
        Grid.SetColumn(roleBadge, 2);
        Grid.SetColumn(statusBadge, 3);
        Grid.SetColumn(lastActiveBlock, 4);
        Grid.SetColumn(actionsPanel, 5);

        row.Children.Add(userCell);
        row.Children.Add(emailBlock);
        row.Children.Add(roleBadge);
        row.Children.Add(statusBadge);
        row.Children.Add(lastActiveBlock);
        row.Children.Add(actionsPanel);

        return row;
    }

    /// <summary>
    /// Creates a 28x28 ghost-style icon button (transparent bg, no border).
    /// </summary>
    private static Button MakeGhostIconButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Width = 28,
            Height = 28,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    // ===== Header Button Handlers =====

    private async void AddUserButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    private async void UserDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var content = BuildUserDefaultsForm();

        var dialog = new ContentDialog
        {
            Title = "Default New User Settings",
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
            Content = content
        };

        await dialog.ShowAsync();
    }

    // ===== Create Dialog =====

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getRequest) = BuildUserForm(null);

        var dialog = new ContentDialog
        {
            Title = "Create User",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var (createRequest, _) = getRequest();
            if (createRequest == null) return;

            try
            {
                await ViewModel.CreateUserCommand.ExecuteAsync(createRequest);
                ShowStatus(ViewModel.StatusMessage ?? "User created.");
            }
            catch { }
        }
    }

    // ===== Edit Dialog =====

    private async Task OpenEditDialogAsync(AdminUser user)
    {
        var (formContent, getRequest) = BuildUserForm(user);

        var dialog = new ContentDialog
        {
            Title = "Edit User",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var (_, updateRequest) = getRequest();
            if (updateRequest == null) return;

            try
            {
                await ViewModel.UpdateUserCommand.ExecuteAsync((user.Id, updateRequest));
                ShowStatus(ViewModel.StatusMessage ?? "User updated.");
            }
            catch { }
        }
    }

    // ===== Delete Dialog =====

    private async Task OpenDeleteDialogAsync(AdminUser user)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete user",
            Content = $"Delete user \"{user.Username}\"? This action cannot be undone.",
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
                await ViewModel.DeleteUserCommand.ExecuteAsync(user.Id);
                ShowStatus(ViewModel.StatusMessage ?? "User deleted.");
            }
            catch { }
        }
    }

    // ===== Form Builder =====

    /// <summary>
    /// Builds the create/edit user form with 3 tabs: Account, Access, Limits.
    /// Matches web UI's UserForm component.
    /// </summary>
    private (FrameworkElement Content, Func<(CreateUserRequest?, UpdateUserRequest?)> GetRequest)
        BuildUserForm(AdminUser? editingUser)
    {
        bool isEdit = editingUser != null;

        // ---- Account tab fields ----
        var usernameBox = new TextBox
        {
            PlaceholderText = "Username",
            Text = editingUser?.Username ?? "",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var emailBox = new TextBox
        {
            PlaceholderText = "Email",
            Text = editingUser?.Email ?? "",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var passwordBox = new PasswordBox
        {
            PlaceholderText = isEdit ? "Leave blank to keep current" : "Password",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var roleCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        roleCombo.Items.Add("user");
        roleCombo.Items.Add("admin");
        roleCombo.SelectedItem = editingUser?.Role ?? "user";

        var enabledSwitch = new ToggleSwitch
        {
            IsOn = editingUser?.Enabled ?? true,
            OnContent = "Enabled",
            OffContent = "Disabled"
        };

        // Account tab — 2-column grid for Username/Email/Password/Role
        var accountTab = new StackPanel { Spacing = 14 };

        var accountGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        accountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accountGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        accountGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Username (row 0, col 0)
        var usernameGroup = new StackPanel { Spacing = 6 };
        usernameGroup.Children.Add(MakeFormLabel("Username"));
        usernameGroup.Children.Add(usernameBox);
        Grid.SetRow(usernameGroup, 0);
        Grid.SetColumn(usernameGroup, 0);

        // Email (row 0, col 1)
        var emailGroup = new StackPanel { Spacing = 6 };
        emailGroup.Children.Add(MakeFormLabel("Email"));
        emailGroup.Children.Add(emailBox);
        Grid.SetRow(emailGroup, 0);
        Grid.SetColumn(emailGroup, 1);

        // Password (row 1, col 0)
        var passwordGroup = new StackPanel { Spacing = 6 };
        passwordGroup.Children.Add(MakeFormLabel(isEdit ? "Password (leave blank to keep current)" : "Password"));
        passwordGroup.Children.Add(passwordBox);
        Grid.SetRow(passwordGroup, 1);
        Grid.SetColumn(passwordGroup, 0);

        // Role (row 1, col 1)
        var roleGroup = new StackPanel { Spacing = 6 };
        roleGroup.Children.Add(MakeFormLabel("Role"));
        roleGroup.Children.Add(roleCombo);
        Grid.SetRow(roleGroup, 1);
        Grid.SetColumn(roleGroup, 1);

        accountGrid.Children.Add(usernameGroup);
        accountGrid.Children.Add(emailGroup);
        accountGrid.Children.Add(passwordGroup);
        accountGrid.Children.Add(roleGroup);
        accountTab.Children.Add(accountGrid);

        // Account status card with toggle (edit only)
        if (isEdit)
        {
            var statusRow = new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var statusRowContent = new Grid();
            statusRowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusRowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var statusDesc = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            statusDesc.Children.Add(new TextBlock
            {
                Text = "Account status",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
            });
            statusDesc.Children.Add(new TextBlock
            {
                Text = "Disable access without deleting the user.",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });

            Grid.SetColumn(statusDesc, 0);
            Grid.SetColumn(enabledSwitch, 1);
            statusRowContent.Children.Add(statusDesc);
            statusRowContent.Children.Add(enabledSwitch);
            statusRow.Child = statusRowContent;
            accountTab.Children.Add(statusRow);
        }

        // ---- Access tab fields ----
        var downloadSwitch = new ToggleSwitch
        {
            IsOn = editingUser?.DownloadAllowed ?? true,
            OnContent = "Allowed",
            OffContent = "Not allowed"
        };

        var downloadTranscodeSwitch = new ToggleSwitch
        {
            IsOn = editingUser?.DownloadTranscodeAllowed ?? false,
            OnContent = "Allowed",
            OffContent = "Not allowed"
        };

        var accessTab = new StackPanel { Spacing = 14 };

        // Library access selector with "All libraries" toggle + per-library checkboxes
        var libraryGroup = new StackPanel { Spacing = 6 };
        libraryGroup.Children.Add(MakeFormLabel("Library Access"));

        var allLibsToggle = new CheckBox
        {
            Content = "All Libraries (default)",
            IsChecked = editingUser?.LibraryIds == null,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        libraryGroup.Children.Add(allLibsToggle);

        var libraryCheckboxPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        var libraryCheckboxes = new Dictionary<int, CheckBox>();

        foreach (var lib in ViewModel.Libraries)
        {
            bool isChecked = editingUser?.LibraryIds == null || (editingUser.LibraryIds?.Contains(lib.Id) == true);
            var cb = new CheckBox
            {
                Content = lib.Name,
                IsChecked = isChecked,
                Tag = lib.Id,
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                IsEnabled = editingUser?.LibraryIds != null
            };
            libraryCheckboxes[lib.Id] = cb;
            libraryCheckboxPanel.Children.Add(cb);
        }

        allLibsToggle.Checked += (_, _) =>
        {
            foreach (var cb in libraryCheckboxes.Values) cb.IsEnabled = false;
        };
        allLibsToggle.Unchecked += (_, _) =>
        {
            foreach (var cb in libraryCheckboxes.Values) cb.IsEnabled = true;
        };

        libraryGroup.Children.Add(libraryCheckboxPanel);
        accessTab.Children.Add(libraryGroup);

        // Downloads Allowed and Download Transcode Allowed in bordered cards
        var downloadRow = MakeSwitchRow("Downloads Allowed", downloadSwitch);
        var downloadTranscodeRow = MakeSwitchRow("Download Transcode Allowed", downloadTranscodeSwitch);
        accessTab.Children.Add(downloadRow);
        accessTab.Children.Add(downloadTranscodeRow);

        // ---- Limits tab fields ----
        var maxStreamsBox = new NumberBox
        {
            Value = editingUser?.MaxStreams ?? 0,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var maxTranscodesBox = new NumberBox
        {
            Value = editingUser?.MaxTranscodes ?? 0,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Upstream commit e2428e7: floor raised to 1, default 5.
        var maxProfilesBox = new NumberBox
        {
            Value = (editingUser?.MaxProfiles ?? 0) <= 0 ? 5 : editingUser!.MaxProfiles,
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Max Playback Quality dropdown
        var qualityCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        string currentPreset = PlaybackQualityPresetFromValue(editingUser?.MaxPlaybackQuality);
        int selectedQualityIndex = 0;
        for (int i = 0; i < PlaybackQualityOptions.Length; i++)
        {
            qualityCombo.Items.Add(PlaybackQualityOptions[i].Label);
            if (PlaybackQualityOptions[i].Value == currentPreset)
                selectedQualityIndex = i;
        }
        qualityCombo.SelectedIndex = selectedQualityIndex;

        var qualityDescription = new TextBlock
        {
            Text = PlaybackQualityOptions[selectedQualityIndex].Description,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        };
        qualityCombo.SelectionChanged += (_, _) =>
        {
            if (qualityCombo.SelectedIndex >= 0 && qualityCombo.SelectedIndex < PlaybackQualityOptions.Length)
                qualityDescription.Text = PlaybackQualityOptions[qualityCombo.SelectedIndex].Description;
        };

        var limitsTab = new StackPanel { Spacing = 14 };

        // 2-column grid for Max Streams / Max Transcodes
        var limitsGrid = new Grid { ColumnSpacing = 12 };
        limitsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        limitsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var streamsGroup = new StackPanel { Spacing = 4 };
        streamsGroup.Children.Add(MakeFormLabel("Max Streams"));
        streamsGroup.Children.Add(maxStreamsBox);
        streamsGroup.Children.Add(new TextBlock
        {
            Text = "0 = unlimited",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        Grid.SetColumn(streamsGroup, 0);

        var transcodesGroup = new StackPanel { Spacing = 4 };
        transcodesGroup.Children.Add(MakeFormLabel("Max Transcodes"));
        transcodesGroup.Children.Add(maxTranscodesBox);
        transcodesGroup.Children.Add(new TextBlock
        {
            Text = "0 = unlimited",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        Grid.SetColumn(transcodesGroup, 1);

        limitsGrid.Children.Add(streamsGroup);
        limitsGrid.Children.Add(transcodesGroup);
        limitsTab.Children.Add(limitsGrid);

        // Max Profiles — full width
        var profilesGroup = new StackPanel { Spacing = 4 };
        profilesGroup.Children.Add(MakeFormLabel("Max Profiles"));
        profilesGroup.Children.Add(maxProfilesBox);
        profilesGroup.Children.Add(new TextBlock
        {
            Text = "0 = unlimited",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        limitsTab.Children.Add(profilesGroup);

        // Max Playback Quality — full width
        var qualityGroup = new StackPanel { Spacing = 4 };
        qualityGroup.Children.Add(MakeFormLabel("Max Playback Quality"));
        qualityGroup.Children.Add(qualityCombo);
        qualityGroup.Children.Add(qualityDescription);
        limitsTab.Children.Add(qualityGroup);

        // ---- Tab container using Pivot (line-style tabs) ----
        var pivot = new Pivot
        {
            Margin = new Thickness(-12, 0, -12, 0),
            FontSize = 13
        };

        var accountItem = new PivotItem { Header = "Account", Content = accountTab };
        var accessItem = new PivotItem { Header = "Access", Content = accessTab, Margin = new Thickness(0, 8, 0, 0) };
        var limitsItem = new PivotItem { Header = "Limits", Content = limitsTab, Margin = new Thickness(0, 8, 0, 0) };

        pivot.Items.Add(accountItem);
        pivot.Items.Add(accessItem);
        pivot.Items.Add(limitsItem);

        var container = new StackPanel { Width = 520, Spacing = 0 };
        container.Children.Add(pivot);

        // ---- GetRequest func ----
        (CreateUserRequest?, UpdateUserRequest?) GetRequest()
        {
            string username = usernameBox.Text.Trim();
            string email = emailBox.Text.Trim();
            string password = passwordBox.Password;
            string role = roleCombo.SelectedItem as string ?? "user";
            int maxStreams = double.IsNaN(maxStreamsBox.Value) ? 0 : (int)maxStreamsBox.Value;
            int maxTranscodes = double.IsNaN(maxTranscodesBox.Value) ? 0 : (int)maxTranscodesBox.Value;
            int maxProfiles = double.IsNaN(maxProfilesBox.Value) ? 5 : Math.Max(1, (int)maxProfilesBox.Value);
            bool downloadAllowed = downloadSwitch.IsOn;
            bool downloadTranscodeAllowed = downloadTranscodeSwitch.IsOn;

            // Get playback quality value from selected preset
            string qualityValue = "";
            if (qualityCombo.SelectedIndex >= 0 && qualityCombo.SelectedIndex < PlaybackQualityOptions.Length)
                qualityValue = PlaybackQualityValueFromPreset(PlaybackQualityOptions[qualityCombo.SelectedIndex].Value);

            List<int>? libraryIds = null;
            if (allLibsToggle.IsChecked != true)
            {
                libraryIds = libraryCheckboxes
                    .Where(kv => kv.Value.IsChecked == true)
                    .Select(kv => kv.Key)
                    .ToList();
            }

            if (isEdit)
            {
                var req = new UpdateUserRequest
                {
                    Username = username,
                    Email = email,
                    Role = role,
                    Enabled = enabledSwitch.IsOn,
                    LibraryIds = libraryIds,
                    MaxStreams = maxStreams,
                    MaxTranscodes = maxTranscodes,
                    MaxProfiles = maxProfiles,
                    MaxPlaybackQuality = qualityValue,
                    DownloadAllowed = downloadAllowed,
                    DownloadTranscodeAllowed = downloadTranscodeAllowed
                };
                if (!string.IsNullOrEmpty(password)) req.Password = password;
                return (null, req);
            }
            else
            {
                var req = new CreateUserRequest
                {
                    Username = username,
                    Email = email,
                    Password = password,
                    Role = role,
                    LibraryIds = libraryIds,
                    MaxStreams = maxStreams,
                    MaxTranscodes = maxTranscodes,
                    MaxProfiles = maxProfiles,
                    MaxPlaybackQuality = string.IsNullOrEmpty(qualityValue) ? null : qualityValue,
                    DownloadAllowed = downloadAllowed,
                    DownloadTranscodeAllowed = downloadTranscodeAllowed
                };
                return (req, null);
            }
        }

        return (container, GetRequest);
    }

    // ===== User Defaults Form =====

    private FrameworkElement BuildUserDefaultsForm()
    {
        var maxStreamsBox = new NumberBox
        {
            Value = 6,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var maxTranscodesBox = new NumberBox
        {
            Value = 2,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var maxProfilesBox = new NumberBox
        {
            Value = 0,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var qualityCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        for (int i = 0; i < PlaybackQualityOptions.Length; i++)
            qualityCombo.Items.Add(PlaybackQualityOptions[i].Label);
        qualityCombo.SelectedIndex = 0; // "Any" by default

        var qualityDescription = new TextBlock
        {
            Text = PlaybackQualityOptions[0].Description,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        };
        qualityCombo.SelectionChanged += (_, _) =>
        {
            if (qualityCombo.SelectedIndex >= 0 && qualityCombo.SelectedIndex < PlaybackQualityOptions.Length)
                qualityDescription.Text = PlaybackQualityOptions[qualityCombo.SelectedIndex].Description;
        };

        var downloadSwitch = new ToggleSwitch
        {
            IsOn = true,
            OnContent = "Allowed",
            OffContent = "Not allowed"
        };

        var downloadTranscodeSwitch = new ToggleSwitch
        {
            IsOn = false,
            OnContent = "Allowed",
            OffContent = "Not allowed"
        };

        var form = new StackPanel { Width = 420, Spacing = 14 };

        // Subtitle
        form.Children.Add(new TextBlock
        {
            Text = "These defaults will pre-fill the form when creating new users.",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        // Max Streams / Max Transcodes in 2-column grid
        var limitsGrid = new Grid { ColumnSpacing = 12 };
        limitsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        limitsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var streamsGroup = new StackPanel { Spacing = 4 };
        streamsGroup.Children.Add(MakeFormLabel("Max Streams"));
        streamsGroup.Children.Add(maxStreamsBox);
        streamsGroup.Children.Add(new TextBlock { Text = "0 = unlimited", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
        Grid.SetColumn(streamsGroup, 0);

        var transcodesGroup = new StackPanel { Spacing = 4 };
        transcodesGroup.Children.Add(MakeFormLabel("Max Transcodes"));
        transcodesGroup.Children.Add(maxTranscodesBox);
        transcodesGroup.Children.Add(new TextBlock { Text = "0 = unlimited", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
        Grid.SetColumn(transcodesGroup, 1);

        limitsGrid.Children.Add(streamsGroup);
        limitsGrid.Children.Add(transcodesGroup);
        form.Children.Add(limitsGrid);

        // Max Profiles — full width
        var profilesGroup = new StackPanel { Spacing = 4 };
        profilesGroup.Children.Add(MakeFormLabel("Max Profiles"));
        profilesGroup.Children.Add(maxProfilesBox);
        profilesGroup.Children.Add(new TextBlock { Text = "0 = unlimited", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
        form.Children.Add(profilesGroup);

        // Max Playback Quality — full width
        var qualityGroup = new StackPanel { Spacing = 4 };
        qualityGroup.Children.Add(MakeFormLabel("Max Playback Quality"));
        qualityGroup.Children.Add(qualityCombo);
        qualityGroup.Children.Add(qualityDescription);
        form.Children.Add(qualityGroup);

        // Downloads toggles
        form.Children.Add(MakeSwitchRow("Downloads Allowed", downloadSwitch));
        form.Children.Add(MakeSwitchRow("Download Transcode Allowed", downloadTranscodeSwitch));

        return form;
    }

    // ===== Playback Quality Helpers =====

    // B54: All three helpers delegate to ContinuumPlayer.Core.Helpers.PlaybackQuality.
    private static string PlaybackQualityPresetFromValue(string? value)
        => Core.Helpers.PlaybackQuality.PresetFromValue(value);

    private static string PlaybackQualityValueFromPreset(string preset)
        => Core.Helpers.PlaybackQuality.ValueFromPreset(preset);

    private static string CanonicalPlaybackQuality(string? value)
        => Core.Helpers.PlaybackQuality.Canonical(value);

    // ===== Helpers =====

    private static TextBlock MakeFormLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
    };

    private static Border MakeSwitchRow(string label, ToggleSwitch toggle)
    {
        var border = new Border
        {
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(toggle);

        border.Child = grid;
        return border;
    }

    // ===== Invite Codes =====

    private bool _inviteCodesRebuildPending;

    private void ScheduleInviteCodesRebuild()
    {
        if (_inviteCodesRebuildPending) return;
        _inviteCodesRebuildPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _inviteCodesRebuildPending = false;
            BuildInviteCodeRows();
        });
    }

    private void BuildInviteCodeRows()
    {
        InviteCodesPanel.Children.Clear();

        if (InviteCodesViewModel.InviteCodes.Count == 0)
        {
            InviteCodesEmptyState.Visibility = Visibility.Visible;
            return;
        }

        InviteCodesEmptyState.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var code in InviteCodesViewModel.InviteCodes)
        {
            if (!first)
            {
                InviteCodesPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            first = false;
            InviteCodesPanel.Children.Add(BuildInviteCodeRow(code));
        }
    }

    private FrameworkElement BuildInviteCodeRow(InviteCode code)
    {
        var row = new Grid { Padding = new Thickness(20, 14, 20, 14), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        // Code (monospace)
        var codeBorder = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        codeBorder.Child = new TextBlock
        {
            Text = code.Code,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        Grid.SetColumn(codeBorder, 0);
        row.Children.Add(codeBorder);

        // Label
        var label = new TextBlock
        {
            Text = code.Label,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        // Max uses
        var maxUses = new TextBlock
        {
            Text = code.MaxUses > 0 ? code.MaxUses.ToString() : "\u221E",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(maxUses, 2);
        row.Children.Add(maxUses);

        // Use count
        var useCount = new TextBlock
        {
            Text = code.UseCount.ToString(),
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(useCount, 3);
        row.Children.Add(useCount);

        // Status badge
        Border statusBadge;
        if (code.Enabled)
        {
            statusBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Active",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
            };
        }
        else
        {
            statusBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Disabled",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160))
            };
        }
        Grid.SetColumn(statusBadge, 4);
        row.Children.Add(statusBadge);

        // Created date
        string createdText = "\u2014";
        if (!string.IsNullOrEmpty(code.CreatedAt) && DateTime.TryParse(code.CreatedAt, out var dt))
            createdText = dt.ToLocalTime().ToString("d");
        var created = new TextBlock
        {
            Text = createdText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(created, 5);
        row.Children.Add(created);

        // Actions: toggle + delete
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        var capturedCode = code;

        var topUpBtn = MakeGhostIconButton("\uE710", "Add uses");
        topUpBtn.Click += async (_, _) => await OpenTopUpInviteCodeDialogAsync(capturedCode);
        actions.Children.Add(topUpBtn);

        var toggleBtn = MakeGhostIconButton(code.Enabled ? "\uE8FB" : "\uE73E", code.Enabled ? "Disable" : "Enable");
        toggleBtn.Click += async (_, _) =>
        {
            await InviteCodesViewModel.ToggleInviteCodeCommand.ExecuteAsync(capturedCode);
            ShowStatus(capturedCode.Enabled ? "Code disabled." : "Code enabled.");
        };
        actions.Children.Add(toggleBtn);

        var deleteBtn = MakeGhostIconButton("\uE74D", "Delete");
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Invite Code",
                Content = $"Delete invite code \"{capturedCode.Code}\"?",
                PrimaryButtonText = "Delete",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await InviteCodesViewModel.DeleteInviteCodeCommand.ExecuteAsync(capturedCode.Id);
                ShowStatus("Invite code deleted.");
            }
        };
        actions.Children.Add(deleteBtn);

        Grid.SetColumn(actions, 6);
        row.Children.Add(actions);

        return row;
    }

    private async void CreateInviteCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var codeBox = new TextBox
        {
            PlaceholderText = "Leave blank to auto-generate",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };
        var labelBox = new TextBox
        {
            PlaceholderText = "e.g. Friends & Family",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };
        var maxUsesBox = new NumberBox
        {
            Value = 1,
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var form = new StackPanel { Width = 512, Spacing = 16 };
        AddInviteCodeFormField(form, "Code (optional)", codeBox);
        AddInviteCodeFormField(form, "Label", labelBox);
        AddInviteCodeFormField(form, "Max Uses", maxUsesBox);

        var dialog = new ContentDialog
        {
            Title = "Create Invite Code",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await InviteCodesViewModel.CreateInviteCodeAsync(new CreateInviteCodeRequest
        {
            Code = string.IsNullOrWhiteSpace(codeBox.Text) ? null : codeBox.Text.Trim(),
            Label = labelBox.Text.Trim(),
            MaxUses = double.IsNaN(maxUsesBox.Value) ? 1 : (int)maxUsesBox.Value
        });
        ShowStatus("Invite code created.");
    }

    private async Task OpenTopUpInviteCodeDialogAsync(InviteCode code)
    {
        var additionalUsesBox = new NumberBox
        {
            Value = 1,
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var form = new StackPanel { Width = 420, Spacing = 16 };
        AddInviteCodeFormField(form, "Additional Uses", additionalUsesBox);

        var dialog = new ContentDialog
        {
            Title = "Add Invite Uses",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var additionalUses = double.IsNaN(additionalUsesBox.Value) ? 0 : (int)additionalUsesBox.Value;
        var updated = await InviteCodesViewModel.TopUpInviteCodeAsync(code, additionalUses);
        if (updated != null)
        {
            ShowStatus(InviteCodesViewModel.StatusMessage ?? "Invite code updated.");
        }
    }

    private static void AddInviteCodeFormField(StackPanel form, string label, FrameworkElement control)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        group.Children.Add(control);
        form.Children.Add(group);
    }

    // ===== Helpers =====

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;

        // Auto-hide after 4 seconds
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    private static string FormatLastActive(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "Never"
            : Core.Helpers.TimeAgo.FormatShort(value);
}
