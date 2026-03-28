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

    public AdminUsersPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminUsersViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Users.CollectionChanged += (_, _) => BuildUserRows();
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    // ===== Table Builder =====

    private void BuildUserRows()
    {
        UsersPanel.Children.Clear();

        if (ViewModel.Users.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var user in ViewModel.Users)
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

        // ---- User column: avatar + username ----
        string initial = user.Username.Length > 0 ? user.Username[0].ToString().ToUpper() : "?";

        var avatarBorder = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        avatarBorder.Child = new TextBlock
        {
            Text = initial,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var userNameBlock = new TextBlock
        {
            Text = user.Username,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var userCell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        userCell.Children.Add(avatarBorder);
        userCell.Children.Add(userNameBlock);

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
        bool isAdmin = user.Role?.ToLowerInvariant() == "admin";
        var roleBg = isAdmin
            ? Color.FromArgb(40, 120, 174, 252)
            : Color.FromArgb(40, 100, 100, 100);
        var roleFg = isAdmin
            ? Color.FromArgb(255, 120, 174, 252)
            : Color.FromArgb(255, 160, 160, 160);

        var roleBadge = new Border
        {
            Background = new SolidColorBrush(roleBg),
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
            Foreground = new SolidColorBrush(roleFg)
        };

        // ---- Status badge ----
        var statusBg = user.Enabled
            ? Color.FromArgb(0, 0, 0, 0)
            : Color.FromArgb(40, 200, 70, 70);
        var statusFg = user.Enabled
            ? Color.FromArgb(255, 63, 185, 80)
            : Color.FromArgb(255, 220, 90, 90);
        var statusBorderColor = user.Enabled
            ? Color.FromArgb(120, 63, 185, 80)
            : Color.FromArgb(120, 200, 70, 70);

        var statusBadge = new Border
        {
            Background = new SolidColorBrush(statusBg),
            BorderBrush = new SolidColorBrush(statusBorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBadge.Child = new TextBlock
        {
            Text = user.Enabled ? "Active" : "Disabled",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(statusFg)
        };

        // ---- Actions ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var historyBtn = MakeIconButton("\uE81C", "View history");
        var editBtn = MakeIconButton("\uE70F", "Edit user");
        var deleteBtn = MakeIconButton("\uE74D", "Delete user");

        // Wire up click handlers capturing user
        var capturedUser = user;
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedUser);
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedUser);

        actionsPanel.Children.Add(historyBtn);
        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(userCell, 0);
        Grid.SetColumn(emailBlock, 1);
        Grid.SetColumn(roleBadge, 2);
        Grid.SetColumn(statusBadge, 3);
        Grid.SetColumn(actionsPanel, 4);

        row.Children.Add(userCell);
        row.Children.Add(emailBlock);
        row.Children.Add(roleBadge);
        row.Children.Add(statusBadge);
        row.Children.Add(actionsPanel);

        return row;
    }

    private static Button MakeIconButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
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
        var content = BuildUserDefaultsForm(out _);

        var dialog = new ContentDialog
        {
            Title = "User Defaults",
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
            Title = "Add User",
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
            Title = "Delete User",
            Content = $"Delete user \"{user.Username}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
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
    /// Builds the create/edit form.
    /// Returns (content element, getRequest func).
    /// getRequest returns (CreateUserRequest?, UpdateUserRequest?) — one is non-null depending on mode.
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

        var accountTab = new StackPanel { Spacing = 14 };

        // Username
        var usernameGroup = new StackPanel { Spacing = 6 };
        usernameGroup.Children.Add(MakeFormLabel("Username"));
        usernameGroup.Children.Add(usernameBox);
        accountTab.Children.Add(usernameGroup);

        // Email
        var emailGroup = new StackPanel { Spacing = 6 };
        emailGroup.Children.Add(MakeFormLabel("Email"));
        emailGroup.Children.Add(emailBox);
        accountTab.Children.Add(emailGroup);

        // Password
        var passwordGroup = new StackPanel { Spacing = 6 };
        passwordGroup.Children.Add(MakeFormLabel(isEdit ? "Password (leave blank to keep current)" : "Password"));
        passwordGroup.Children.Add(passwordBox);
        accountTab.Children.Add(passwordGroup);

        // Role
        var roleGroup = new StackPanel { Spacing = 6 };
        roleGroup.Children.Add(MakeFormLabel("Role"));
        roleGroup.Children.Add(roleCombo);
        accountTab.Children.Add(roleGroup);

        // Enabled toggle (edit only)
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
                Text = "Account Status",
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

        var limitsTab = new StackPanel { Spacing = 14 };

        var streamsGroup = new StackPanel { Spacing = 6 };
        streamsGroup.Children.Add(MakeFormLabel("Max Streams"));
        streamsGroup.Children.Add(maxStreamsBox);
        streamsGroup.Children.Add(new TextBlock
        {
            Text = "0 = unlimited",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        limitsTab.Children.Add(streamsGroup);

        var transcodesGroup = new StackPanel { Spacing = 6 };
        transcodesGroup.Children.Add(MakeFormLabel("Max Transcodes"));
        transcodesGroup.Children.Add(maxTranscodesBox);
        transcodesGroup.Children.Add(new TextBlock
        {
            Text = "0 = unlimited",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        limitsTab.Children.Add(transcodesGroup);

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

        // Library checkboxes
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

        var downloadRow = MakeSwitchRow("Downloads Allowed", downloadSwitch);
        var downloadTranscodeRow = MakeSwitchRow("Download Transcode Allowed", downloadTranscodeSwitch);
        accessTab.Children.Add(downloadRow);
        accessTab.Children.Add(downloadTranscodeRow);

        // ---- Tab container ----
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

        var container = new StackPanel { Width = 460, Spacing = 0 };
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
            bool downloadAllowed = downloadSwitch.IsOn;
            bool downloadTranscodeAllowed = downloadTranscodeSwitch.IsOn;

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
                    DownloadAllowed = downloadAllowed,
                    DownloadTranscodeAllowed = downloadTranscodeAllowed
                };
                return (req, null);
            }
        }

        return (container, GetRequest);
    }

    // ===== User Defaults Form =====

    private FrameworkElement BuildUserDefaultsForm(out Func<Task> saveAction)
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

        var form = new StackPanel { Width = 380, Spacing = 14 };

        form.Children.Add(new TextBlock
        {
            Text = "These defaults will pre-fill the form when creating new users.",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        var streamsGroup = new StackPanel { Spacing = 6 };
        streamsGroup.Children.Add(MakeFormLabel("Max Streams"));
        streamsGroup.Children.Add(maxStreamsBox);
        streamsGroup.Children.Add(new TextBlock { Text = "0 = unlimited", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
        form.Children.Add(streamsGroup);

        var transcodesGroup = new StackPanel { Spacing = 6 };
        transcodesGroup.Children.Add(MakeFormLabel("Max Transcodes"));
        transcodesGroup.Children.Add(maxTranscodesBox);
        transcodesGroup.Children.Add(new TextBlock { Text = "0 = unlimited", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"] });
        form.Children.Add(transcodesGroup);

        form.Children.Add(MakeSwitchRow("Downloads Allowed", downloadSwitch));
        form.Children.Add(MakeSwitchRow("Download Transcode Allowed", downloadTranscodeSwitch));

        saveAction = () => Task.CompletedTask; // Defaults form is informational; real server settings requires AdminApi.UpdateAdminSettingAsync
        return form;
    }

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
}
