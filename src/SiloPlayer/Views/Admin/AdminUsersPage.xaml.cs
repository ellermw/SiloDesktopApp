using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Specialized;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage : Page
{
    public AdminUsersViewModel ViewModel { get; }
    public AdminInviteCodesViewModel InviteCodesViewModel { get; }
    private readonly AdminApi _adminApi;
    private readonly List<Invitation> _invitations = [];
    private bool _invitationsLoaded;
    private bool _inviteCodesLoaded;
    private bool _updatingSignupToggle;
    private bool _usersSubscribed;
    private bool _inviteCodesSubscribed;

    // B54: Playback quality options now live in SiloPlayer.Core.Helpers.PlaybackQuality.
    private static readonly (string Value, string Label, string Description)[] PlaybackQualityOptions
        = Core.Helpers.PlaybackQuality.Options;

    private bool _rebuildPending;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public AdminUsersPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminUsersViewModel>();
        InviteCodesViewModel = App.Services.GetRequiredService<AdminInviteCodesViewModel>();
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        SizeChanged += AdminUsersPage_SizeChanged;
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            BuildUserRows();
        };
    }

    private void AdminUsersPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 760;
        var gutter = e.NewSize.Width < 600 ? 16 : compact ? 24 : 40;
        UsersPageShell.Padding = new Thickness(gutter, compact ? 24 : 32, gutter, 40);
        UsersHeaderGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
        Grid.SetRow(UsersHeaderActions, compact ? 1 : 0);
        Grid.SetColumn(UsersHeaderActions, compact ? 0 : 1);
        Grid.SetColumnSpan(UsersHeaderActions, compact ? 2 : 1);
        UsersHeaderActions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SubscribeToCollections();
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_usersSubscribed)
        {
            ViewModel.Users.CollectionChanged -= Users_CollectionChanged;
            _usersSubscribed = false;
        }

        if (_inviteCodesSubscribed)
        {
            InviteCodesViewModel.InviteCodes.CollectionChanged -= InviteCodes_CollectionChanged;
            _inviteCodesSubscribed = false;
        }
        _searchTimer.Stop();

        _rebuildPending = false;
        _inviteCodesRebuildPending = false;
    }

    private void SubscribeToCollections()
    {
        if (!_usersSubscribed)
        {
            ViewModel.Users.CollectionChanged += Users_CollectionChanged;
            _usersSubscribed = true;
        }

        if (_inviteCodesLoaded && !_inviteCodesSubscribed)
        {
            InviteCodesViewModel.InviteCodes.CollectionChanged += InviteCodes_CollectionChanged;
            _inviteCodesSubscribed = true;
        }
    }

    private void Users_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScheduleRebuild();

    private void InviteCodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScheduleInviteCodesRebuild();

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
        InvitationsTabContent.Visibility = Visibility.Collapsed;
        InviteCodesTabContent.Visibility = Visibility.Collapsed;
        TabUsersIndicator.Visibility = Visibility.Visible;
        TabInvitationsIndicator.Visibility = Visibility.Collapsed;
        TabInviteCodesIndicator.Visibility = Visibility.Collapsed;
        TabUsersText.Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        TabUsersText.FontWeight = FontWeights.SemiBold;
        TabInviteCodesText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabInviteCodesText.FontWeight = FontWeights.Normal;
        TabInvitationsText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabInvitationsText.FontWeight = FontWeights.Normal;
        AddUserButton.Visibility = Visibility.Visible;
        AccessGroupsButton.Visibility = Visibility.Visible;
    }

    private async void TabInvitations_Click(object sender, RoutedEventArgs e)
    {
        UsersTabContent.Visibility = Visibility.Collapsed;
        InvitationsTabContent.Visibility = Visibility.Visible;
        InviteCodesTabContent.Visibility = Visibility.Collapsed;
        TabUsersIndicator.Visibility = Visibility.Collapsed;
        TabInvitationsIndicator.Visibility = Visibility.Visible;
        TabInviteCodesIndicator.Visibility = Visibility.Collapsed;
        TabInvitationsText.Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        TabInvitationsText.FontWeight = FontWeights.SemiBold;
        TabUsersText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabUsersText.FontWeight = FontWeights.Normal;
        TabInviteCodesText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabInviteCodesText.FontWeight = FontWeights.Normal;
        AddUserButton.Visibility = Visibility.Collapsed;
        AccessGroupsButton.Visibility = Visibility.Collapsed;

        if (!_invitationsLoaded)
            await LoadInvitationsAsync();
    }

    private async void TabInviteCodes_Click(object sender, RoutedEventArgs e)
    {
        UsersTabContent.Visibility = Visibility.Collapsed;
        InvitationsTabContent.Visibility = Visibility.Collapsed;
        InviteCodesTabContent.Visibility = Visibility.Visible;
        TabUsersIndicator.Visibility = Visibility.Collapsed;
        TabInvitationsIndicator.Visibility = Visibility.Collapsed;
        TabInviteCodesIndicator.Visibility = Visibility.Visible;
        TabInviteCodesText.Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        TabInviteCodesText.FontWeight = FontWeights.SemiBold;
        TabUsersText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabUsersText.FontWeight = FontWeights.Normal;
        TabInvitationsText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        TabInvitationsText.FontWeight = FontWeights.Normal;
        AddUserButton.Visibility = Visibility.Collapsed;
        AccessGroupsButton.Visibility = Visibility.Collapsed;

        if (!_inviteCodesLoaded)
        {
            _inviteCodesLoaded = true;
            SubscribeToCollections();
            InviteCodesLoading.IsActive = true;
            InviteCodesLoading.Visibility = Visibility.Visible;
            try { await InviteCodesViewModel.LoadCommand.ExecuteAsync(null); }
            catch (Exception ex) { ShowError($"Error loading invite codes: {ex.Message}"); }
            finally
            {
                InviteCodesLoading.IsActive = false;
                InviteCodesLoading.Visibility = Visibility.Collapsed;
            }

            _updatingSignupToggle = true;
            SignupEnabledToggle.IsOn = InviteCodesViewModel.SignupEnabled;
            SignupEnabledText.Text = InviteCodesViewModel.SignupEnabled ? "Enabled" : "Disabled";
            _updatingSignupToggle = false;
        }
    }

    // ===== Emailed invitations =====

    private sealed record InvitationOption(string Label, object? Value)
    {
        public override string ToString() => Label;
    }

    private async Task LoadInvitationsAsync()
    {
        InvitationsLoading.IsActive = true;
        InvitationsLoading.Visibility = Visibility.Visible;
        InviteSomeoneButton.IsEnabled = false;
        try
        {
            var invitations = await _adminApi.GetInvitationsAsync();
            _invitations.Clear();
            _invitations.AddRange(invitations);
            _invitationsLoaded = true;
            BuildInvitationRows();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load invitations: {ex.Message}");
        }
        finally
        {
            InvitationsLoading.IsActive = false;
            InvitationsLoading.Visibility = Visibility.Collapsed;
            InviteSomeoneButton.IsEnabled = true;
        }
    }

    private void BuildInvitationRows()
    {
        InvitationsPanel.Children.Clear();
        InvitationsEmptyText.Visibility = _invitations.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var invitation in _invitations)
        {
            if (InvitationsPanel.Children.Count > 0)
                InvitationsPanel.Children.Add(new Border
                {
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0),
                });

            var row = new Grid { Padding = new Thickness(20, 12, 20, 12), ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

            var recipient = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            recipient.Children.Add(new TextBlock
            {
                Text = invitation.Email,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrWhiteSpace(invitation.InvitedByName))
                recipient.Children.Add(new TextBlock
                {
                    Text = $"Invited by {invitation.InvitedByName}",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
            row.Children.Add(recipient);

            var role = new TextBlock
            {
                Text = invitation.Role,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(role, 1);
            row.Children.Add(role);

            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var statusLabel = invitation.Status switch
            {
                "pending" => "Sent",
                "accepted" => "Accepted",
                "expired" => "Expired",
                "revoked" => "Revoked",
                _ => invitation.Status,
            };
            statusPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                Background = invitation.Status == "pending"
                    ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = invitation.Status == "pending" ? new Thickness(0) : new Thickness(1),
                Child = new TextBlock
                {
                    Text = statusLabel,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = invitation.Status == "pending"
                        ? (Brush)Application.Current.Resources["AccentBrush"]
                        : (Brush)Application.Current.Resources["PrimaryTextBrush"],
                },
            });
            if (invitation.Status == "pending")
                statusPanel.Children.Add(new TextBlock
                {
                    Text = $"expires {FormatInvitationDate(invitation.ExpiresAt)}",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                });
            Grid.SetColumn(statusPanel, 2);
            row.Children.Add(statusPanel);

            var sent = new TextBlock
            {
                Text = FormatInvitationDate(invitation.CreatedAt),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(sent, 3);
            row.Children.Add(sent);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (invitation.Status is "pending" or "expired")
            {
                var resend = MakeInvitationActionButton("\uE72C", "Resend with a fresh link");
                resend.Click += async (_, _) => await ResendInvitationAsync(invitation, resend);
                actions.Children.Add(resend);
            }
            if (invitation.Status == "pending")
            {
                var revoke = MakeInvitationActionButton("\uE74D", "Revoke this link");
                revoke.Click += async (_, _) => await RevokeInvitationAsync(invitation);
                actions.Children.Add(revoke);
            }
            Grid.SetColumn(actions, 4);
            row.Children.Add(actions);
            InvitationsPanel.Children.Add(row);
        }
    }

    private static Button MakeInvitationActionButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private static string FormatInvitationDate(DateTimeOffset value)
        => SiloPlayer.Helpers.DateTimeDisplay.FormatDate(value, medium: true);

    private async void InviteSomeone_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<AccessGroup> accessGroups;
        try
        {
            accessGroups = await _adminApi.GetAccessGroupsAsync();
            if (ViewModel.Libraries.Count == 0)
            {
                foreach (var library in await _adminApi.GetAdminLibrariesAsync())
                    ViewModel.Libraries.Add(library);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Invitation options could not be loaded: {ex.Message}");
            return;
        }

        var email = new TextBox { PlaceholderText = "them@example.com", HorizontalAlignment = HorizontalAlignment.Stretch };
        var role = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        role.Items.Add(new InvitationOption("User", "user"));
        role.Items.Add(new InvitationOption("Admin", "admin"));
        role.SelectedIndex = 0;

        var accessGroup = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var defaultGroup = accessGroups.FirstOrDefault(group => group.IsDefault);
        accessGroup.Items.Add(new InvitationOption(
            defaultGroup is null ? "Server default" : $"{defaultGroup.Name} (default)", null));
        foreach (var group in accessGroups.Where(group => !group.IsDefault))
            accessGroup.Items.Add(new InvitationOption(group.Name, group.Id));
        accessGroup.SelectedIndex = 0;

        var libraryChecks = new List<(int Id, CheckBox Check)>();
        var libraryList = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        foreach (var library in ViewModel.Libraries.DistinctBy(candidate => candidate.Id))
        {
            var check = new CheckBox { Content = library.Name, IsChecked = true };
            libraryChecks.Add((library.Id, check));
            libraryList.Children.Add(check);
        }
        var allLibraries = new ToggleSwitch
        {
            Header = "Library access",
            OnContent = "All libraries",
            OffContent = "Selected libraries",
            IsOn = true,
        };
        allLibraries.Toggled += (_, _) => libraryList.Visibility = allLibraries.IsOn
            ? Visibility.Collapsed
            : Visibility.Visible;

        var note = new TextBox
        {
            PlaceholderText = "Hey — set yourself up whenever.",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
        };
        var createProfile = new ToggleSwitch
        {
            Header = "Create their first profile",
            OnContent = "Yes",
            OffContent = "No",
            IsOn = true,
        };
        var showTour = new ToggleSwitch
        {
            Header = "Show the feature tour on first sign-in",
            OnContent = "Yes",
            OffContent = "No",
            IsOn = true,
        };
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Invite someone" };
        var root = new StackPanel { Spacing = 16, MinWidth = 500 };
        root.Children.Add(new TextBlock
        {
            Text = "They get an email with a link. Their username is their email address, so all they pick is a password.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(MakeInvitationField("Email address", email,
            "This becomes both the destination and their sign-in username."));
        var twoColumns = new Grid { ColumnSpacing = 12 };
        twoColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        twoColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var groupField = MakeInvitationField("Access group", accessGroup);
        var roleField = MakeInvitationField("Role", role);
        Grid.SetColumn(roleField, 1);
        twoColumns.Children.Add(groupField);
        twoColumns.Children.Add(roleField);
        root.Children.Add(twoColumns);
        root.Children.Add(allLibraries);
        root.Children.Add(new ScrollViewer { Content = libraryList, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(MakeInvitationField("Personal note (optional)", note, "Appears in the email. Plain text."));
        root.Children.Add(createProfile);
        root.Children.Add(showTour);
        root.Children.Add(error);

        var footer = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        footer.Children.Add(new TextBlock
        {
            Text = "Link expires in 7 days · single use",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        var send = new Button { Content = "Send invite", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        cancel.Click += (_, _) => dialog.Hide();
        send.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(email.Text))
            {
                error.Text = "Email address is required.";
                error.Visibility = Visibility.Visible;
                return;
            }

            send.IsEnabled = false;
            send.Content = "Sending...";
            error.Visibility = Visibility.Collapsed;
            try
            {
                var response = await _adminApi.CreateInvitationAsync(new CreateInvitationRequest
                {
                    Email = email.Text.Trim(),
                    Role = (role.SelectedItem as InvitationOption)?.Value as string ?? "user",
                    AccessGroupId = (accessGroup.SelectedItem as InvitationOption)?.Value as long?,
                    LibraryIds = allLibraries.IsOn
                        ? null
                        : libraryChecks.Where(item => item.Check.IsChecked == true).Select(item => item.Id).ToList(),
                    CreateProfile = createProfile.IsOn,
                    ShowTour = showTour.IsOn,
                    Note = string.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim(),
                });
                ShowInvitationClaimResult(dialog, root, response, freshLink: false);
                await LoadInvitationsAsync();
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
                send.IsEnabled = true;
                send.Content = "Send invite";
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(send);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        root.Children.Add(footer);
        dialog.Content = new ScrollViewer { Content = root, MaxHeight = 650, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        await dialog.ShowAsync();
    }

    private static StackPanel MakeInvitationField(string label, Control field, string? help = null)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(field);
        if (!string.IsNullOrWhiteSpace(help))
            panel.Children.Add(new TextBlock
            {
                Text = help,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        return panel;
    }

    private void ShowInvitationClaimResult(
        ContentDialog dialog,
        StackPanel root,
        SendInvitationResponse response,
        bool freshLink)
    {
        dialog.Title = freshLink ? "Fresh invitation link" : "Invite someone";
        root.Children.Clear();
        root.Children.Add(new TextBlock
        {
            Text = freshLink
                ? response.EmailSent
                    ? $"Emailed to {response.Invitation.Email}. You can also copy the link and send it to them directly."
                    : "Email isn't configured on this server, so nothing was sent — deliver this link yourself."
                : response.EmailSent
                    ? "Invitation emailed. You can also copy the link and send it to them directly:"
                    : "Email isn't configured on this server, so nothing was sent. The invitation was created — deliver this link yourself:",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        var claimUrl = response.ClaimUrl ?? "";
        root.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Child = new TextBlock { Text = claimUrl, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis },
        });
        root.Children.Add(new TextBlock
        {
            Text = freshLink
                ? "The link works once; any previous link for this invitation has stopped working."
                : "The link works once and expires in 7 days. Resending later mints a fresh link and kills this one.",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Content = "Copy link", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(claimUrl);
            Clipboard.SetContent(package);
            ShowStatus("Copied to clipboard");
        };
        var done = new Button { Content = "Done", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        done.Click += (_, _) => dialog.Hide();
        footer.Children.Add(copy);
        footer.Children.Add(done);
        root.Children.Add(footer);
    }

    private async Task ResendInvitationAsync(Invitation invitation, Button button)
    {
        button.IsEnabled = false;
        try
        {
            var response = await _adminApi.ResendInvitationAsync(invitation.Id);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Fresh invitation link" };
            var root = new StackPanel { Spacing = 16, MinWidth = 460 };
            ShowInvitationClaimResult(dialog, root, response, freshLink: true);
            dialog.Content = root;
            await LoadInvitationsAsync();
            await dialog.ShowAsync();
        }
        catch (Exception ex) { ShowError($"Failed to resend invitation: {ex.Message}"); }
        finally { button.IsEnabled = true; }
    }

    private async Task RevokeInvitationAsync(Invitation invitation)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Revoke invitation",
            Content = $"Revoke the invitation for {invitation.Email}? Their link will stop working immediately.",
            PrimaryButtonText = "Revoke",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await _adminApi.RevokeInvitationAsync(invitation.Id);
            ShowStatus("Invitation revoked");
            await LoadInvitationsAsync();
        }
        catch (Exception ex) { ShowError($"Failed to revoke invitation: {ex.Message}"); }
    }

    private async void SignupEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingSignupToggle || !_inviteCodesLoaded) return;

        var requested = SignupEnabledToggle.IsOn;
        SignupEnabledToggle.IsEnabled = false;
        var saved = await InviteCodesViewModel.SetSignupEnabledAsync(requested);
        _updatingSignupToggle = true;
        SignupEnabledToggle.IsOn = saved ? requested : InviteCodesViewModel.SignupEnabled;
        SignupEnabledText.Text = SignupEnabledToggle.IsOn ? "Enabled" : "Disabled";
        _updatingSignupToggle = false;
        SignupEnabledToggle.IsEnabled = true;

        if (saved)
            ShowStatus(requested ? "Public signups enabled." : "Public signups disabled.");
        else if (!string.IsNullOrWhiteSpace(InviteCodesViewModel.ErrorMessage))
            ShowError(InviteCodesViewModel.ErrorMessage);
    }

    // ===== Table Builder =====

    private string _searchQuery = "";

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text ?? "";
        SearchClearButton.Visibility = string.IsNullOrEmpty(_searchQuery)
            ? Visibility.Collapsed : Visibility.Visible;
        _userPage = 0;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
    }

    private int _userPageSize = 25;
    private int _userPage;
    private string _sortColumn = "username";
    private bool _sortAscending = true;

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
        filtered = (_sortColumn, _sortAscending) switch
        {
            ("email", true) => filtered.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase),
            ("email", false) => filtered.OrderByDescending(u => u.Email, StringComparer.OrdinalIgnoreCase),
            ("role", true) => filtered.OrderBy(u => u.Role, StringComparer.OrdinalIgnoreCase),
            ("role", false) => filtered.OrderByDescending(u => u.Role, StringComparer.OrdinalIgnoreCase),
            ("status", true) => filtered.OrderByDescending(u => u.Enabled),
            ("status", false) => filtered.OrderBy(u => u.Enabled),
            ("created", true) => filtered.OrderBy(u => ParseDate(u.CreatedAt) is null).ThenBy(u => ParseDate(u.CreatedAt)),
            ("created", false) => filtered.OrderBy(u => ParseDate(u.CreatedAt) is null).ThenByDescending(u => ParseDate(u.CreatedAt)),
            ("last_active", true) => filtered.OrderBy(u => ParseDate(u.LastActiveAt) is null).ThenBy(u => ParseDate(u.LastActiveAt)),
            ("last_active", false) => filtered.OrderBy(u => ParseDate(u.LastActiveAt) is null).ThenByDescending(u => ParseDate(u.LastActiveAt)),
            (_, true) => filtered.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(u => u.Username, StringComparer.OrdinalIgnoreCase),
        };
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
                var item = new ComboBoxItem { Content = $"{ps} rows", Tag = ps };
                if (ps == _userPageSize) item.IsSelected = true;
                pageSizeCombo.Items.Add(item);
            }
            pageSizeCombo.SelectionChanged += (_, _) =>
            {
                if (pageSizeCombo.SelectedItem is ComboBoxItem sel && sel.Tag is int ps)
                { _userPageSize = ps; _userPage = 0; BuildUserRows(); }
            };
            UserPaginationBar.Children.Add(pageSizeCombo);

            var prevBtn = new Button { Content = "Previous", Padding = new Thickness(12, 6, 12, 6), IsEnabled = _userPage > 0 };
            prevBtn.Click += (_, _) => { _userPage--; BuildUserRows(); };
            var nextBtn = new Button { Content = "Next", Padding = new Thickness(12, 6, 12, 6), IsEnabled = _userPage < totalPages - 1 };
            nextBtn.Click += (_, _) => { _userPage++; BuildUserRows(); };
            UserPaginationBar.Children.Add(prevBtn);
            UserPaginationBar.Children.Add(nextBtn);
        }
    }

    private void UserSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string column) return;
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
        _userPage = 0;
        UpdateUserSortIndicators();
        BuildUserRows();
    }

    private void UpdateUserSortIndicators()
    {
        var indicators = new Dictionary<string, FontIcon>
        {
            ["username"] = UsernameSortIcon,
            ["email"] = EmailSortIcon,
            ["role"] = RoleSortIcon,
            ["status"] = StatusSortIcon,
            ["created"] = CreatedSortIcon,
            ["last_active"] = LastActiveSortIcon,
        };

        foreach (var (column, icon) in indicators)
        {
            icon.Visibility = column == _sortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = _sortAscending ? "\uE70E" : "\uE70D";
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var date) ? date : null;

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

        var createdBlock = new TextBlock
        {
            Text = FormatCreated(user.CreatedAt),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        // ---- Last active ----
        var lastActiveBlock = new TextBlock
        {
            Text = FormatLastActive(user.LastActiveAt),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(createdBlock, FormatFullDate(user.CreatedAt));
        if (!string.IsNullOrWhiteSpace(user.LastActiveAt))
            ToolTipService.SetToolTip(lastActiveBlock, FormatFullDate(user.LastActiveAt));

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
        Grid.SetColumn(createdBlock, 4);
        Grid.SetColumn(lastActiveBlock, 5);
        Grid.SetColumn(actionsPanel, 6);

        row.Children.Add(userCell);
        row.Children.Add(emailBlock);
        row.Children.Add(roleBadge);
        row.Children.Add(statusBadge);
        row.Children.Add(createdBlock);
        row.Children.Add(lastActiveBlock);
        row.Children.Add(actionsPanel);

        return row;
    }

    private void AccessGroupsButton_Click(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(AdminAccessGroupsPage));

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
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var (request, _) = getRequest();
            var error = ValidateCreateUser(request);
            if (error == null) return;
            args.Cancel = true;
            ShowError(error);
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var (createRequest, _) = getRequest();
            if (createRequest == null) return;

            try
            {
                await ViewModel.CreateUserCommand.ExecuteAsync(createRequest);
                ShowUserMutationResult("User created.");
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
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var (_, request) = getRequest();
            var error = ValidateUpdateUser(request);
            if (error == null) return;
            args.Cancel = true;
            ShowError(error);
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var (_, updateRequest) = getRequest();
            if (updateRequest == null) return;

            try
            {
                await ViewModel.UpdateUserCommand.ExecuteAsync((user.Id, updateRequest));
                ShowUserMutationResult("User updated.");
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
                ShowUserMutationResult("User deleted.");
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
            IsOn = editingUser?.DownloadTranscodeAllowed ?? true,
            OnContent = "Allowed",
            OffContent = "Not allowed"
        };

        var assignedPermissions = new HashSet<string>(
            editingUser?.Permissions ?? ["marker_edit"],
            StringComparer.OrdinalIgnoreCase);
        var markerEditSwitch = new ToggleSwitch
        {
            IsOn = assignedPermissions.Contains("marker_edit"),
            OnContent = "",
            OffContent = ""
        };
        var metadataCurationSwitch = new ToggleSwitch
        {
            IsOn = assignedPermissions.Contains("metadata_curation"),
            OnContent = "",
            OffContent = ""
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

        accessTab.Children.Add(MakeSwitchRow(
            "Marker Editing",
            "Edit intro, recap, credits, and preview markers within assigned libraries.",
            markerEditSwitch));
        accessTab.Children.Add(MakeSwitchRow(
            "Metadata Curation",
            "Edit, refresh, and rematch metadata within assigned libraries.",
            metadataCurationSwitch));

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
        var transcodeAllowedSwitch = new ToggleSwitch
        {
            IsOn = editingUser?.TranscodeAllowed ?? true,
            OnContent = "Video allowed",
            OffContent = "Video disabled",
        };
        var audioTranscodeSwitch = new ToggleSwitch
        {
            IsOn = editingUser?.AudioTranscodeAllowed ?? true,
            OnContent = "Allowed",
            OffContent = "Not allowed",
        };
        var audioTranscodeRow = MakeSwitchRow("Audio transcodes", audioTranscodeSwitch);
        void SyncTranscodeControls()
        {
            maxTranscodesBox.IsEnabled = transcodeAllowedSwitch.IsOn;
            audioTranscodeRow.Visibility = transcodeAllowedSwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
        }
        transcodeAllowedSwitch.Toggled += (_, _) => SyncTranscodeControls();

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
        transcodesGroup.Children.Add(transcodeAllowedSwitch);
        transcodesGroup.Children.Add(new TextBlock
        {
            Text = "Disable video transcoding independently; audio-only conversion can remain available.",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        Grid.SetColumn(transcodesGroup, 1);

        limitsGrid.Children.Add(streamsGroup);
        limitsGrid.Children.Add(transcodesGroup);
        limitsTab.Children.Add(limitsGrid);
        limitsTab.Children.Add(audioTranscodeRow);
        SyncTranscodeControls();

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
            bool transcodeAllowed = transcodeAllowedSwitch.IsOn;
            bool audioTranscodeAllowed = audioTranscodeSwitch.IsOn;
            int maxProfiles = double.IsNaN(maxProfilesBox.Value) ? 5 : Math.Max(1, (int)maxProfilesBox.Value);
            bool downloadAllowed = downloadSwitch.IsOn;
            bool downloadTranscodeAllowed = downloadTranscodeSwitch.IsOn;
            var permissions = new HashSet<string>(assignedPermissions, StringComparer.OrdinalIgnoreCase);
            if (markerEditSwitch.IsOn) permissions.Add("marker_edit"); else permissions.Remove("marker_edit");
            if (metadataCurationSwitch.IsOn) permissions.Add("metadata_curation"); else permissions.Remove("metadata_curation");
            var permissionList = permissions.OrderBy(value => value, StringComparer.Ordinal).ToList();

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
                    Permissions = permissionList,
                    LibraryIds = libraryIds,
                    LibraryIdsSpecified = true,
                    MaxStreams = maxStreams,
                    MaxTranscodes = maxTranscodes,
                    TranscodeAllowed = transcodeAllowed,
                    AudioTranscodeAllowed = audioTranscodeAllowed,
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
                    Permissions = permissionList,
                    CreateDefaultProfile = true,
                    LibraryIds = libraryIds,
                    MaxStreams = maxStreams,
                    MaxTranscodes = maxTranscodes,
                    TranscodeAllowed = transcodeAllowed,
                    AudioTranscodeAllowed = audioTranscodeAllowed,
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

    // B54: All three helpers delegate to SiloPlayer.Core.Helpers.PlaybackQuality.
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

    private static Border MakeSwitchRow(string label, string description, ToggleSwitch toggle)
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
        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        copy.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(copy);
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        // Code + copy action.
        var codeCell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
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
        codeCell.Children.Add(codeBorder);
        var copyButton = MakeGhostIconButton("\uE8C8", $"Copy invite code {code.Code}");
        copyButton.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(code.Code);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ShowStatus("Copied to clipboard.");
        };
        codeCell.Children.Add(copyButton);
        Grid.SetColumn(codeCell, 0);
        row.Children.Add(codeCell);

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

        // Usage is one field in the WebUI.
        var usage = new TextBlock
        {
            Text = $"{code.UseCount:N0} / {code.MaxUses:N0}",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources[
                code.MaxUses > 0 && code.UseCount >= code.MaxUses ? "ErrorBrush" : "SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(usage, 2);
        row.Children.Add(usage);

        // Status switch + badge.
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
        var capturedCode = code;
        var statusToggle = new ToggleSwitch
        {
            IsOn = code.Enabled,
            OnContent = "",
            OffContent = "",
            MinWidth = 44,
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusToggle.Toggled += async (_, _) =>
        {
            statusToggle.IsEnabled = false;
            await InviteCodesViewModel.ToggleInviteCodeCommand.ExecuteAsync(capturedCode);
            if (!string.IsNullOrWhiteSpace(InviteCodesViewModel.ErrorMessage))
            {
                ShowError(InviteCodesViewModel.ErrorMessage);
                BuildInviteCodeRows();
            }
            else
            {
                ShowStatus(InviteCodesViewModel.StatusMessage ?? (capturedCode.Enabled ? "Code disabled." : "Code enabled."));
            }
        };
        var statusCell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusCell.Children.Add(statusToggle);
        statusCell.Children.Add(statusBadge);
        Grid.SetColumn(statusCell, 3);
        row.Children.Add(statusCell);

        // Created date
        string createdText = "\u2014";
        if (!string.IsNullOrEmpty(code.CreatedAt) && DateTimeOffset.TryParse(code.CreatedAt, out var dt))
            createdText = SiloPlayer.Helpers.DateTimeDisplay.FormatDate(dt);
        var created = new TextBlock
        {
            Text = createdText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(created, 4);
        row.Children.Add(created);

        // Actions: top-up + delete. Enable/disable lives in the Status cell.
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        var topUpBtn = MakeGhostIconButton("\uE710", $"Top up invite code {code.Code}");
        topUpBtn.Click += async (_, _) => await OpenTopUpInviteCodeDialogAsync(capturedCode);
        actions.Children.Add(topUpBtn);

        var deleteBtn = MakeGhostIconButton("\uE74D", $"Delete invite code {code.Code}");
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
                if (!string.IsNullOrWhiteSpace(InviteCodesViewModel.ErrorMessage))
                    ShowError(InviteCodesViewModel.ErrorMessage);
                else
                    ShowStatus(InviteCodesViewModel.StatusMessage ?? "Invite code deleted.");
            }
        };
        actions.Children.Add(deleteBtn);

        Grid.SetColumn(actions, 5);
        row.Children.Add(actions);

        return row;
    }

    private async void CreateInviteCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var codeBox = new TextBox
        {
            PlaceholderText = "e.g. BETA2026",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };
        var labelBox = new TextBox
        {
            PlaceholderText = "e.g. Beta testers",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };
        var maxUsesBox = new NumberBox
        {
            Value = 10,
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var form = new StackPanel { Width = 512, Spacing = 16 };
        AddInviteCodeFormField(form, "Code (optional, auto-generated if empty)", codeBox);
        AddInviteCodeFormField(form, "Label", labelBox);
        AddInviteCodeFormField(form, "Max uses", maxUsesBox);

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

        var created = await InviteCodesViewModel.CreateInviteCodeAsync(new CreateInviteCodeRequest
        {
            Code = string.IsNullOrWhiteSpace(codeBox.Text) ? null : codeBox.Text.Trim().ToUpperInvariant(),
            Label = labelBox.Text.Trim(),
            MaxUses = double.IsNaN(maxUsesBox.Value) ? 10 : (int)maxUsesBox.Value
        });
        if (created != null)
            ShowStatus(InviteCodesViewModel.StatusMessage ?? "Invite code created.");
        else if (!string.IsNullOrWhiteSpace(InviteCodesViewModel.ErrorMessage))
            ShowError(InviteCodesViewModel.ErrorMessage);
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
        form.Children.Add(new TextBlock
        {
            Text = $"Add extra uses to {code.Code}. Current usage is {code.UseCount:N0} / {code.MaxUses:N0}.",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        AddInviteCodeFormField(form, "Additional uses", additionalUsesBox);

        var dialog = new ContentDialog
        {
            Title = "Top Up Invite Code",
            PrimaryButtonText = "Add Uses",
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
        else if (!string.IsNullOrWhiteSpace(InviteCodesViewModel.ErrorMessage))
        {
            ShowError(InviteCodesViewModel.ErrorMessage);
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
        App.Services.GetRequiredService<ToastService>().Success(message);
    }

    private void ShowError(string message)
    {
        App.Services.GetRequiredService<ToastService>().Error(message);
    }

    private void ShowUserMutationResult(string fallback)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
            ShowError(ViewModel.ErrorMessage);
        else
            ShowStatus(ViewModel.StatusMessage ?? fallback);
    }

    private static string? ValidateCreateUser(CreateUserRequest? request)
    {
        if (request == null) return "Unable to read the user form.";
        if (string.IsNullOrWhiteSpace(request.Username)) return "Username is required.";
        if (!IsValidEmail(request.Email)) return "Enter a valid email address.";
        if (string.IsNullOrWhiteSpace(request.Password)) return "Password is required.";
        return null;
    }

    private static string? ValidateUpdateUser(UpdateUserRequest? request)
    {
        if (request == null) return "Unable to read the user form.";
        if (string.IsNullOrWhiteSpace(request.Username)) return "Username is required.";
        if (!IsValidEmail(request.Email)) return "Enter a valid email address.";
        return null;
    }

    private static bool IsValidEmail(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && System.Net.Mail.MailAddress.TryCreate(value, out _);

    private static string FormatFullDate(string? value)
        => DateTimeOffset.TryParse(value, out var date)
            ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(date, seconds: true)
            : "";

    private static string FormatLastActive(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "Never"
              : Core.Helpers.TimeAgo.FormatShort(value);

    private static string FormatCreated(string? value)
        => DateTimeOffset.TryParse(value, out var created)
            ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(created)
            : value ?? "—";
}
