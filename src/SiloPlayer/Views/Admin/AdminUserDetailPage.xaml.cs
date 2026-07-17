using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels.Admin;
using SiloPlayer.Views;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUserDetailPage : Page
{
    public AdminUserDetailViewModel ViewModel { get; }

    private int _userId;

    // B54: Playback quality options now live in SiloPlayer.Core.Helpers.PlaybackQuality.
    private static readonly (string Value, string Label, string Description)[] PlaybackQualityOptions
        = Core.Helpers.PlaybackQuality.Options;

    // Tab names
    private static readonly string[] TabNames = ["Overview", "Settings", "Devices", "Profiles", "Watch History", "IP History"];
    private readonly Button[] _tabButtons = new Button[6];
    private readonly Border[] _tabIndicators = new Border[6];
    private readonly UIElement[] _tabPanels;
    private int _activeTabIndex;

    public AdminUserDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminUserDetailViewModel>();
        this.InitializeComponent();
        SizeChanged += (_, _) => ApplyResponsiveLayout();

        _tabPanels = [OverviewPanel, SettingsPanel, DevicesPanel, ProfilesPanel, HistoryPanel, IPPanel];

        RetryButton.Click += async (_, _) => await LoadAsync();
        BackButton.Click  += (_, _) => GoBack();
        BreadcrumbAdmin.Click += (_, _) => Frame.Navigate(typeof(AdminDashboardPage));
        BreadcrumbUsers.Click += (_, _) => Frame.Navigate(typeof(AdminUsersPage));
        ImpersonateButton.Click += async (_, _) => await OpenImpersonateDialogAsync();
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
        ApplyResponsiveLayout();
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
                UpdateImpersonateButton(ViewModel.User);
                BuildOverviewTab(ViewModel.User);
                SwitchTab(0);
            }
        }
        catch { }
    }

    // ===== Header =====

    private void BuildHeader(AdminUser user)
    {
        TitleBadgeRow.Children.Clear();

        // Username as page title
        TitleBadgeRow.Children.Add(new TextBlock
        {
            Text = user.Username,
            FontSize = 48,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        // Role badge: admin=default(AccentBg/AccentFg), user=secondary(SurfaceBg/SecondaryFg)
        bool isAdmin = user.Role?.ToLowerInvariant() == "admin";
        var roleBadge = new Border
        {
            Background = isAdmin
                ? (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"]
                : (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        roleBadge.Child = new TextBlock
        {
            Text = user.Role ?? "user",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = isAdmin
                ? (SolidColorBrush)Application.Current.Resources["AccentBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };
        TitleBadgeRow.Children.Add(roleBadge);

        // Status badge: Active=outline, Disabled=destructive
        Border statusBadge;
        if (user.Enabled)
        {
            statusBadge = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Active",
                FontSize = 12,
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
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.Child = new TextBlock
            {
                Text = "Disabled",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            };
        }
        TitleBadgeRow.Children.Add(statusBadge);

        EmailSubtitle.Text = user.Email;
        BreadcrumbUsername.Text = user.Username;
    }

    private void ApplyResponsiveLayout()
    {
        var width = ActualWidth;
        var compact = width < 760;
        var side = width < 640 ? 16 : width < 1024 ? 24 : 40;
        UserDetailPageShell.Padding = new Thickness(side, width < 640 ? 16 : 28, side, 40);

        Grid.SetColumn(UserDetailActions, compact ? 1 : 2);
        Grid.SetRow(UserDetailActions, compact ? 1 : 0);
        UserDetailActions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        OverviewPanel.ColumnDefinitions.Clear();
        OverviewPanel.RowDefinitions.Clear();
        if (compact)
        {
            OverviewPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            OverviewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            OverviewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            OverviewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(AccountCard, 0); Grid.SetRow(AccountCard, 0);
            Grid.SetColumn(PermissionsCard, 0); Grid.SetRow(PermissionsCard, 2);
        }
        else
        {
            OverviewPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            OverviewPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            OverviewPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            OverviewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(AccountCard, 0); Grid.SetRow(AccountCard, 0);
            Grid.SetColumn(PermissionsCard, 2); Grid.SetRow(PermissionsCard, 0);
        }
    }

    private void UpdateImpersonateButton(AdminUser user)
    {
        // Disabled for admin users or disabled users
        bool disabled = user.Role?.ToLowerInvariant() == "admin" || !user.Enabled;
        ImpersonateButton.IsEnabled = !disabled;
    }

    // ===== Tab bar (line-style) =====

    private void BuildTabBar()
    {
        TabBar.Children.Clear();
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;

            // Each tab is a StackPanel containing the button and an underline indicator
            var tabContainer = new StackPanel { Spacing = 0 };

            var btn = new Button
            {
                Content = TabNames[i],
                Style = (Style)Resources["LineTabButtonStyle"]
            };
            btn.Click += (_, _) => SwitchTab(idx);
            _tabButtons[i] = btn;

            var indicator = new Border
            {
                Height = 2,
                Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(4, 0, 4, 0),
                Visibility = Visibility.Collapsed
            };
            _tabIndicators[i] = indicator;

            tabContainer.Children.Add(btn);
            tabContainer.Children.Add(indicator);
            TabBar.Children.Add(tabContainer);
        }
    }

    private async void SwitchTab(int index)
    {
        _activeTabIndex = index;

        var primaryFg = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        var secondFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        for (int i = 0; i < _tabButtons.Length; i++)
        {
            bool active = i == index;
            if (_tabButtons[i] == null) continue;

            _tabButtons[i].Foreground = active ? primaryFg : secondFg;
            _tabButtons[i].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;

            if (_tabIndicators[i] != null)
                _tabIndicators[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        for (int i = 0; i < _tabPanels.Length; i++)
        {
            if (_tabPanels[i] != null)
                _tabPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        await EnsureTabLoadedAsync(index);
    }

    private async Task EnsureTabLoadedAsync(int index)
    {
        if (_userId == 0 || index == 0) return;
        ShowTabLoading(index);
        try
        {
            switch (index)
            {
                case 1:
                    await ViewModel.LoadUserSettingsAsync(_userId);
                    BuildUserSettingsTab();
                    break;
                case 2:
                    await ViewModel.LoadDeviceSettingsAsync(_userId);
                    BuildDeviceOverridesTab();
                    break;
                case 3:
                    await ViewModel.LoadProfilesAsync(_userId);
                    BuildProfilesTab();
                    break;
                case 4:
                    await ViewModel.LoadHistoryAsync(_userId);
                    BuildHistoryTab();
                    break;
                case 5:
                    await ViewModel.LoadIPsAsync(_userId);
                    BuildIPTab();
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowTabError(index, ex.Message);
            App.Services.GetRequiredService<ToastService>().Error(ex.Message);
        }
    }

    private void ShowTabLoading(int index)
    {
        var loading = new TextBlock
        {
            Text = index switch
            {
                1 => "Loading settings...",
                2 => "Loading device overrides...",
                3 => "Loading profiles...",
                4 => "Loading watch history...",
                _ => "Loading IP history..."
            },
            FontSize = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 32, 0, 32)
        };
        switch (index)
        {
            case 1: UserSettingsRows.Children.Clear(); UserSettingsRows.Children.Add(loading); break;
            case 2: DeviceOverridesHost.Children.Clear(); DeviceOverridesHost.Children.Add(loading); DeviceOverridesSummary.Text = ""; break;
            case 3: ProfilesContent.Children.Clear(); ProfilesContent.Children.Add(loading); break;
            case 4: HistoryRows.Children.Clear(); HistoryRows.Children.Add(loading); HistoryEmpty.Visibility = Visibility.Collapsed; break;
            case 5: IPRows.Children.Clear(); IPRows.Children.Add(loading); IPEmpty.Visibility = Visibility.Collapsed; break;
        }
    }

    private void ShowTabError(int index, string message)
    {
        var error = new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 32, 0, 32)
        };
        switch (index)
        {
            case 1: UserSettingsRows.Children.Clear(); UserSettingsRows.Children.Add(error); break;
            case 2: DeviceOverridesHost.Children.Clear(); DeviceOverridesHost.Children.Add(error); break;
            case 3: ProfilesContent.Children.Clear(); ProfilesContent.Children.Add(error); break;
            case 4: HistoryRows.Children.Clear(); HistoryRows.Children.Add(error); break;
            case 5: IPRows.Children.Clear(); IPRows.Children.Add(error); break;
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

        // Library access — resolve names from ViewModel.Libraries
        string libraryAccess;
        if (user.LibraryIds == null)
        {
            libraryAccess = "All libraries";
        }
        else if (user.LibraryIds.Count == 0)
        {
            libraryAccess = "None";
        }
        else
        {
            var names = user.LibraryIds.Select(id =>
            {
                var lib = ViewModel.Libraries.FirstOrDefault(l => l.Id == id);
                return lib != null ? lib.Name : $"#{id}";
            });
            libraryAccess = string.Join(", ", names);
        }

        var accessGroupName = user.AccessGroupId is long accessGroupId
            ? ViewModel.AccessGroups.FirstOrDefault(group => group.Id == accessGroupId)?.Name ?? $"#{accessGroupId}"
            : "No group";
        AddDetailRow(PermissionsRows, "Group",                accessGroupName == "No group" ? "None" : accessGroupName);
        AddDetailRow(PermissionsRows, "Library Access",       libraryAccess);
        AddDetailRow(PermissionsRows, "Marker Editing",       user.Permissions.Contains("marker_edit") ? "Allowed" : "Not allowed");
        AddDetailRow(PermissionsRows, "Metadata Curation",    user.Permissions.Contains("metadata_curation") ? "Allowed" : "Not allowed");
        AddDetailRow(PermissionsRows, "Max Playback Quality", FormatPlaybackQualityPreset(user.MaxPlaybackQuality));
        AddDetailRow(PermissionsRows, "Max Streams",          user.MaxStreams == 0 ? "Unlimited" : user.MaxStreams.ToString());
        AddDetailRow(PermissionsRows, "Max Transcodes",       !user.TranscodeAllowed ? "Disabled" : user.MaxTranscodes == 0 ? "Unlimited" : user.MaxTranscodes.ToString());
        if (!user.TranscodeAllowed)
            AddDetailRow(PermissionsRows, "Audio Transcodes", user.AudioTranscodeAllowed ? "Allowed" : "Not allowed");
        AddDetailRow(PermissionsRows, "Max Profiles",         user.MaxProfiles.ToString());
        AddDetailRow(PermissionsRows, "Downloads",            user.DownloadAllowed ? "Allowed" : "Not allowed");
        AddDetailRow(PermissionsRows, "Download Transcode",   user.DownloadTranscodeAllowed ? "Allowed" : "Not allowed");
    }

    /// <summary>
    /// DetailRow: px-4 py-2.5 = 16px horiz, 10px vert. Label=14px muted, Value=14px medium. divide-y separators.
    /// </summary>
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

        var row = new Grid { Padding = new Thickness(16, 10, 16, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text       = label,
            FontSize   = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var valueBlock = new TextBlock
        {
            Text          = value,
            FontSize      = 14,
            FontWeight    = FontWeights.Medium,
            Foreground    = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping  = TextWrapping.Wrap,
            MaxWidth      = 260,
            TextAlignment = TextAlignment.Right
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);

        parent.Children.Add(row);
    }

    // ===== Account settings tab =====

    private void BuildUserSettingsTab()
    {
        UserSettingsRows.Children.Clear();
        if (ViewModel.UserSettings.Count == 0)
        {
            UserSettingsRows.Children.Add(EmptyTabMessage("No account-wide settings are stored for this user."));
            return;
        }

        var first = true;
        foreach (var setting in ViewModel.UserSettings)
        {
            if (!first)
                UserSettingsRows.Children.Add(new Border { BorderBrush = ResourceBrush("BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0) });
            first = false;
            UserSettingsRows.Children.Add(BuildUserSettingRow(setting));
        }
    }

    private FrameworkElement BuildUserSettingRow(AdminUserSetting setting)
    {
        var definition = AdminDeviceSettingDefinition.All.FirstOrDefault(item => item.Key == setting.Key);
        var row = new Grid { Padding = new Thickness(0, 16, 0, 16), ColumnSpacing = 18 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = definition?.Label ?? setting.Key, FontSize = 14, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new Border
        {
            BorderBrush = ResourceBrush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 2, 7, 2), Child = new TextBlock { Text = "Explicit", FontSize = 10, Foreground = ResourceBrush("SecondaryTextBrush") }
        });
        copy.Children.Add(title);
        copy.Children.Add(new TextBlock
        {
            Text = definition?.Description ?? "Stored account preference.", FontSize = 12,
            Foreground = ResourceBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap
        });
        copy.Children.Add(new TextBlock { Text = $"Current: {FormatSettingValue(definition, setting.Value)}", FontSize = 11, Foreground = ResourceBrush("SecondaryTextBrush") });
        row.Children.Add(copy);

        var actions = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(BuildSettingEditor(definition, setting.Value, async value =>
        {
            try
            {
                await ViewModel.SaveUserSettingAsync(_userId, setting, value);
                App.Services.GetRequiredService<ToastService>().Success($"{definition?.Label ?? setting.Key} updated.");
                BuildUserSettingsTab();
            }
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        }));
        var reset = new Button { Content = "↻  Reset", Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
        reset.Click += async (_, _) =>
        {
            try
            {
                await ViewModel.ResetUserSettingAsync(_userId, setting);
                App.Services.GetRequiredService<ToastService>().Success($"{definition?.Label ?? setting.Key} reset.");
                BuildUserSettingsTab();
            }
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        };
        actions.Children.Add(reset);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    // ===== Device overrides tab =====

    private void BuildDeviceOverridesTab()
    {
        DeviceOverridesHost.Children.Clear();
        var settings = ViewModel.DeviceSettings.ToList();
        var deviceGroups = settings.GroupBy(item => item.DeviceId, StringComparer.Ordinal).ToList();
        var profileCount = settings.Select(item => string.IsNullOrWhiteSpace(item.ProfileId) ? "unknown" : item.ProfileId).Distinct(StringComparer.Ordinal).Count();
        DeviceOverridesSummary.Text = deviceGroups.Count == 0
            ? ""
            : $"{deviceGroups.Count} {(deviceGroups.Count == 1 ? "device" : "devices")}  ·  {settings.Count} {(settings.Count == 1 ? "override" : "overrides")}  ·  {profileCount} {(profileCount == 1 ? "profile" : "profiles")}";

        if (deviceGroups.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4 };
            empty.Children.Add(new TextBlock { Text = "No device overrides", FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = "Overrides appear here as soon as this user tunes a per-device playback setting.", FontSize = 12, Foreground = ResourceBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420 });
            DeviceOverridesHost.Children.Add(new Border { Background = ResourceBrush("CardBackgroundBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(24, 40, 24, 40), Child = empty });
            return;
        }

        foreach (var deviceGroup in deviceGroups)
            DeviceOverridesHost.Children.Add(BuildDeviceOverrideCard(deviceGroup.Key, deviceGroup.ToList()));
    }

    private FrameworkElement BuildDeviceOverrideCard(string deviceId, List<AdminDeviceSetting> settings)
    {
        var first = settings[0];
        var root = new StackPanel();
        var header = new Grid { Padding = new Thickness(18, 14, 18, 14), ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 3 };
        copy.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(first.DeviceName) ? "Unnamed device" : first.DeviceName, FontSize = 14, FontWeight = FontWeights.SemiBold });
        var updated = settings.Where(item => item.UpdatedAt.HasValue).Select(item => item.UpdatedAt!.Value).DefaultIfEmpty().Max();
        var meta = $"{ShortId(deviceId)}  ·  {PlatformLabel(first.DevicePlatform)}";
        if (updated != default) meta += $"  ·  updated {Core.Helpers.TimeAgo.FormatShort(updated.ToString("O"))}";
        copy.Children.Add(new TextBlock { Text = meta, FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = ResourceBrush("SecondaryTextBrush") });
        var profiles = settings.GroupBy(item => string.IsNullOrWhiteSpace(item.ProfileId) ? "unknown" : item.ProfileId, StringComparer.Ordinal).ToList();
        copy.Children.Add(new TextBlock { Text = $"{profiles.Count} {(profiles.Count == 1 ? "profile" : "profiles")}  ·  {settings.Count} {(settings.Count == 1 ? "override" : "overrides")}", FontSize = 11, Foreground = ResourceBrush("SecondaryTextBrush") });
        header.Children.Add(copy);
        var open = new Button { Content = "Open device ↗", Padding = new Thickness(10, 6, 10, 6), VerticalAlignment = VerticalAlignment.Top };
        open.Click += (_, _) => Frame.Navigate(typeof(AdminDevicesPage), new AdminDeviceNavigationTarget(_userId, deviceId));
        Grid.SetColumn(open, 1);
        header.Children.Add(open);
        root.Children.Add(header);
        root.Children.Add(new Border { BorderBrush = ResourceBrush("BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0) });

        foreach (var profile in profiles)
            root.Children.Add(BuildDeviceProfileSection(deviceId, profile.Key, profile.ToList()));

        return new Border { Background = ResourceBrush("CardBackgroundBrush"), CornerRadius = new CornerRadius(12), Child = root };
    }

    private FrameworkElement BuildDeviceProfileSection(string deviceId, string profileId, List<AdminDeviceSetting> settings)
    {
        var root = new StackPanel { Spacing = 10, Padding = new Thickness(18, 14, 18, 18) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var profileName = settings[0].ProfileName;
        header.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(profileName) ? profileId : profileName, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var resetAll = new Button { Content = "Reset all", Foreground = ResourceBrush("ErrorBrush"), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
        resetAll.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = string.IsNullOrWhiteSpace(profileName) ? "Reset profile overrides" : $"Reset overrides for {profileName}?",
                Content = "Every override for this profile on this device will be cleared. Playback falls back to account or default values.",
                PrimaryButtonText = "Reset all", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"]
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                await ViewModel.ResetDeviceProfileAsync(_userId, profileId, deviceId);
                BuildDeviceOverridesTab();
            }
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        };
        Grid.SetColumn(resetAll, 1); header.Children.Add(resetAll); root.Children.Add(header);

        foreach (var setting in settings)
            root.Children.Add(BuildDeviceSettingRow(setting));
        return root;
    }

    private FrameworkElement BuildDeviceSettingRow(AdminDeviceSetting setting)
    {
        var definition = AdminDeviceSettingDefinition.All.FirstOrDefault(item => item.Key == setting.Key);
        var row = new Grid { ColumnSpacing = 18, Padding = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = definition?.Label ?? setting.Key, FontSize = 13, FontWeight = FontWeights.SemiBold });
        copy.Children.Add(new TextBlock { Text = definition?.Description ?? setting.Key, FontSize = 11, Foreground = ResourceBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        row.Children.Add(copy);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(BuildSettingEditor(definition, setting.Value, async value =>
        {
            try
            {
                await ViewModel.SaveDeviceSettingAsync(_userId, setting, value);
                App.Services.GetRequiredService<ToastService>().Success($"{definition?.Label ?? setting.Key} updated.");
                BuildDeviceOverridesTab();
            }
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        }));
        var reset = new Button { Content = "↻", Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(6), FontSize = 13 };
        ToolTipService.SetToolTip(reset, "Reset this override");
        reset.Click += async (_, _) =>
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Reset this override?", Content = "The override will be removed and the device will fall back to the profile default.", PrimaryButtonText = "Reset override", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"] };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try { await ViewModel.ResetDeviceSettingAsync(_userId, setting); BuildDeviceOverridesTab(); }
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        };
        actions.Children.Add(reset); Grid.SetColumn(actions, 1); row.Children.Add(actions);
        return row;
    }

    private FrameworkElement BuildSettingEditor(AdminDeviceSettingDefinition? definition, string value, Func<string, Task> save)
    {
        if (definition?.Control == "switch")
        {
            var toggle = new ToggleSwitch { IsOn = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase), OnContent = "", OffContent = "", MinWidth = 44 };
            toggle.Toggled += async (_, _) => await save(toggle.IsOn ? "true" : "false");
            return toggle;
        }
        if (definition?.Control == "select")
        {
            var combo = new ComboBox { Width = 190, SelectedValuePath = "Tag" };
            foreach (var option in definition.Options) combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            combo.SelectedValue = value;
            combo.SelectionChanged += async (_, _) => { if (combo.SelectedValue is string selected && selected != value) await save(selected); };
            return combo;
        }
        if (definition?.Control == "json")
        {
            var edit = new Button { Content = "Edit JSON", Padding = new Thickness(10, 6, 10, 6) };
            edit.Click += async (_, _) =>
            {
                var box = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), MinHeight = 260, Width = 560 };
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = definition.Label, Content = box, PrimaryButtonText = "Save override", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary) await save(box.Text);
            };
            return edit;
        }
        var text = new TextBox { Text = value, Width = 190, TextAlignment = TextAlignment.Left };
        text.LostFocus += async (_, _) => { if (text.Text != value) await save(text.Text); };
        return text;
    }

    private static string FormatSettingValue(AdminDeviceSettingDefinition? definition, string value)
        => definition?.Options.FirstOrDefault(option => option.Value == value)?.Label
           ?? (definition?.Control == "switch" ? (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "On" : "Off") : value);

    private static TextBlock EmptyTabMessage(string text) => new()
    {
        Text = text, FontSize = 14, Foreground = ResourceBrush("SecondaryTextBrush"),
        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 28), TextWrapping = TextWrapping.Wrap
    };

    private static SolidColorBrush ResourceBrush(string key) => (SolidColorBrush)Application.Current.Resources[key];
    private static string ShortId(string value) => value.Length <= 8 ? value : value[..8] + "…";
    private static string PlatformLabel(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown platform" : value;

    // ===== Profiles tab =====

    private void BuildProfilesTab()
    {
        ProfilesContent.Children.Clear();

        if (ViewModel.Profiles.Count == 0)
        {
            // Empty state: surface-panel rounded-[1.6rem] borderless
            ProfilesContent.Children.Add(new Border
            {
                Background    = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
                CornerRadius  = new CornerRadius(16),
                BorderThickness = new Thickness(0),
                Padding       = new Thickness(20, 40, 20, 40),
                Child = new TextBlock
                {
                    Text = "No profiles found for this user.",
                    FontSize = 14,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });
            return;
        }

        // Grid of cards: sm:grid-cols-2 lg:grid-cols-3 gap-3 (12px)
        int cols = 3;
        var profiles = ViewModel.Profiles.ToList();

        for (int rowStart = 0; rowStart < profiles.Count; rowStart += cols)
        {
            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (int c = 0; c < cols; c++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (c < cols - 1)
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); // gap-3
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
                    var spacer = new Border { Visibility = Visibility.Collapsed };
                    Grid.SetColumn(spacer, colIdx);
                    rowGrid.Children.Add(spacer);
                }
            }

            ProfilesContent.Children.Add(rowGrid);
        }
    }

    /// <summary>
    /// Profile card: surface-panel rounded-[1.4rem] border-0 px-4 py-3
    /// UserCircle icon 32px muted + name 14px medium + id 12px muted
    /// </summary>
    private FrameworkElement BuildProfileCard(AdminUserProfile profile)
    {
        var card = new Border
        {
            Background      = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius    = new CornerRadius(16),  // rounded-[1.4rem] = 22.4
            BorderThickness = new Thickness(0),       // border-0
            Padding         = new Thickness(16, 12, 16, 12) // px-4 py-3
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };

        // UserCircle icon (32px, muted) - no background circle, just the icon
        var icon = new FontIcon
        {
            Glyph      = "\uE77B",
            FontSize   = 28,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Width = 32,
            Height = 32
        };

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text       = profile.Name,
            FontSize   = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        textStack.Children.Add(new TextBlock
        {
            Text       = profile.Id,
            FontSize   = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            FontFamily = new FontFamily("Consolas")
        });

        content.Children.Add(icon);
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
            Padding       = new Thickness(16, 10, 16, 10),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        // Media column: title (font-medium) + type (12px muted)
        string title = !string.IsNullOrEmpty(item.MediaTitle) ? item.MediaTitle
                     : !string.IsNullOrEmpty(item.MediaItemId) ? item.MediaItemId
                     : $"File #{item.MediaFileId}";

        var mediaStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        mediaStack.Children.Add(!string.IsNullOrWhiteSpace(item.MediaItemId)
            ? BuildLinkButton(
                title,
                14,
                FontWeights.Medium,
                (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                () => NavigateToItem(item.MediaItemId))
            : new TextBlock
            {
                Text         = title,
                FontSize     = 14,
                FontWeight   = FontWeights.Medium,
                Foreground   = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        mediaStack.Children.Add(new TextBlock
        {
            Text       = !string.IsNullOrEmpty(item.MediaType) ? item.MediaType : "unknown",
            FontSize   = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });

        // Profile column
        var profileBlock = BuildLinkButton(
            !string.IsNullOrEmpty(item.ProfileName) ? item.ProfileName : item.ProfileId,
            14,
            FontWeights.Normal,
            (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            () => NavigateToProfileHistory(item.ProfileId));

        // Method badge: secondary variant (SurfaceBrush bg / SecondaryTextBrush fg)
        var methodBadge = new Border
        {
            Background    = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius  = new CornerRadius(4),
            Padding       = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text       = item.PlayMethod ?? "",
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };

        // Watch time: duration + "of {total}" (12px muted)
        string watched = AdminUserDetailViewModel.FormatWatchTime(item.WatchedSeconds);
        string total   = item.DurationSeconds.HasValue
            ? AdminUserDetailViewModel.FormatWatchTime(item.DurationSeconds.Value)
            : "\u2014";

        var watchStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        watchStack.Children.Add(new TextBlock
        {
            Text       = watched,
            FontSize   = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        watchStack.Children.Add(new TextBlock
        {
            Text       = $"of {total}",
            FontSize   = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });

        // Status badge: Completed=default(AccentBg/AccentFg), Partial=outline(BorderBrush border)
        Border statusBadge;
        if (item.Completed)
        {
            statusBadge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Completed",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"]
                }
            };
        }
        else
        {
            statusBadge = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Partial",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
                }
            };
        }

        // Ended: datetime + "started Xm ago" (12px muted)
        var endedStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        endedStack.Children.Add(new TextBlock
        {
            Text       = AdminUserDetailViewModel.FormatDateTime(item.EndedAt),
            FontSize   = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        endedStack.Children.Add(new TextBlock
        {
            Text       = $"started {AdminUserDetailViewModel.FormatRelative(item.StartedAt)}",
            FontSize   = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
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

    private void NavigateToProfileHistory(string profileId)
    {
        if (_userId <= 0 || string.IsNullOrWhiteSpace(profileId)) return;
        Frame.Navigate(typeof(AdminPlaybackHistoryPage), new AdminPlaybackHistoryFilter(_userId, profileId));
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
            Padding       = new Thickness(16, 10, 16, 10),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        // IP Address: monospace text-sm
        var ipBlock = new TextBlock
        {
            Text       = entry.ClientIp,
            FontSize   = 14,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var firstSeenBlock = new TextBlock
        {
            Text       = AdminUserDetailViewModel.FormatDateTime(entry.FirstSeen),
            FontSize   = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var lastSeenBlock = new TextBlock
        {
            Text       = AdminUserDetailViewModel.FormatDateTime(entry.LastSeen),
            FontSize   = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // Requests: right-aligned
        var requestsBlock = new TextBlock
        {
            Text              = entry.RequestCount.ToString("N0"),
            FontSize          = 14,
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

    // ===== Impersonate dialog =====

    private async Task OpenImpersonateDialogAsync()
    {
        if (ViewModel.User == null) return;

        var dialog = new ContentDialog
        {
            Title             = "Impersonate user",
            Content           = $"Continue as \"{ViewModel.User.Username}\"? Admin access will be removed until you end impersonation.",
            PrimaryButtonText  = "Impersonate",
            CloseButtonText   = "Cancel",
            XamlRoot          = this.XamlRoot,
            DefaultButton     = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                var impResult = await adminApi.ImpersonateUserAsync(ViewModel.User.Id);
                var authService = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>();
                var playerService = App.Services.GetRequiredService<SiloPlayer.Services.PlayerService>();
                await playerService.CloseAsync();
                authService.BeginImpersonation(
                    impResult,
                    $"/admin/users/{ViewModel.User.Id}");

                if (App.MainWindowInstance != null)
                {
                    App.MainWindowInstance.HideMainNavigation();
                    App.Services.GetRequiredService<NavigationService>()
                        .Navigate<ProfileSelectPage>();
                }
            }
            catch (Exception ex)
            {
                App.Services.GetRequiredService<ToastService>().Error(ex.Message);
            }
        }
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
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var request = getRequest();
            if (request == null || string.IsNullOrWhiteSpace(request.Username))
            {
                args.Cancel = true;
                App.Services.GetRequiredService<ToastService>().Error("Username is required.");
            }
            else if (string.IsNullOrWhiteSpace(request.Email) || !System.Net.Mail.MailAddress.TryCreate(request.Email, out var _parsedEmail))
            {
                args.Cancel = true;
                App.Services.GetRequiredService<ToastService>().Error("Enter a valid email address.");
            }
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
                App.Services.GetRequiredService<ToastService>().Success("User updated.");
            }
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        }
    }

    /// <summary>
    /// Builds the Edit User form with 3 tabs (Account/Access/Limits) matching the web UI EditUserForm.
    /// </summary>
    private (FrameworkElement Content, Func<UpdateUserRequest?> GetRequest) BuildEditForm(AdminUser user)
    {
        // ---- Account tab fields ----
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

        var enabledSwitch = new ToggleSwitch
        {
            IsOn       = user.Enabled,
            OnContent  = "Enabled",
            OffContent = "Disabled"
        };

        // Account tab: 2-column grid for Username/Email/Password/Role
        var accountTab = new StackPanel { Spacing = 14 };

        var accountGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        accountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accountGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        accountGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var usernameGroup = new StackPanel { Spacing = 6 };
        usernameGroup.Children.Add(MakeFormLabel("Username"));
        usernameGroup.Children.Add(usernameBox);
        Grid.SetRow(usernameGroup, 0);
        Grid.SetColumn(usernameGroup, 0);

        var emailGroup = new StackPanel { Spacing = 6 };
        emailGroup.Children.Add(MakeFormLabel("Email"));
        emailGroup.Children.Add(emailBox);
        Grid.SetRow(emailGroup, 0);
        Grid.SetColumn(emailGroup, 1);

        var passwordGroup = new StackPanel { Spacing = 6 };
        passwordGroup.Children.Add(MakeFormLabel("Password (leave blank to keep current)"));
        passwordGroup.Children.Add(passwordBox);
        Grid.SetRow(passwordGroup, 1);
        Grid.SetColumn(passwordGroup, 0);

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

        // Account status card — always shown (not conditional for edit)
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

        var enabledLabel = new TextBlock
        {
            Text = "Enabled",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var enabledPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        enabledPanel.Children.Add(enabledLabel);
        enabledPanel.Children.Add(enabledSwitch);

        Grid.SetColumn(statusDesc, 0);
        Grid.SetColumn(enabledPanel, 1);
        statusRowContent.Children.Add(statusDesc);
        statusRowContent.Children.Add(enabledPanel);
        statusRow.Child = statusRowContent;
        accountTab.Children.Add(statusRow);

        // ---- Access tab ----
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
        var assignedPermissions = new HashSet<string>(user.Permissions, StringComparer.OrdinalIgnoreCase);
        var markerEditSwitch = new ToggleSwitch { IsOn = assignedPermissions.Contains("marker_edit"), OnContent = "", OffContent = "" };
        var metadataCurationSwitch = new ToggleSwitch { IsOn = assignedPermissions.Contains("metadata_curation"), OnContent = "", OffContent = "" };

        var accessTab = new StackPanel { Spacing = 14 };

        var accessGroupCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };
        accessGroupCombo.Items.Add(new ComboBoxItem { Content = "No group", Tag = null });
        var selectedAccessGroupIndex = 0;
        foreach (var group in ViewModel.AccessGroups)
        {
            accessGroupCombo.Items.Add(new ComboBoxItem
            {
                Content = group.IsDefault ? $"{group.Name} (default)" : group.Name,
                Tag = group.Id
            });
            if (user.AccessGroupId == group.Id)
                selectedAccessGroupIndex = accessGroupCombo.Items.Count - 1;
        }
        accessGroupCombo.SelectedIndex = selectedAccessGroupIndex;
        var accessGroupPanel = new StackPanel { Spacing = 6 };
        accessGroupPanel.Children.Add(MakeFormLabel("Access Group"));
        accessGroupPanel.Children.Add(accessGroupCombo);
        accessGroupPanel.Children.Add(new TextBlock
        {
            Text = "Group defaults are intersected with this user's own restrictions.",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
        accessTab.Children.Add(accessGroupPanel);

        // Library access selector
        var libraryGroup = new StackPanel { Spacing = 6 };
        libraryGroup.Children.Add(MakeFormLabel("Library Access"));

        var allLibsToggle = new CheckBox
        {
            Content = "All Libraries (default)",
            IsChecked = user.LibraryIds == null,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        libraryGroup.Children.Add(allLibsToggle);

        var libraryCheckboxPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        var libraryCheckboxes = new Dictionary<int, CheckBox>();

        foreach (var lib in ViewModel.Libraries)
        {
            bool isChecked = user.LibraryIds == null || (user.LibraryIds?.Contains(lib.Id) == true);
            var cb = new CheckBox
            {
                Content = lib.Name,
                IsChecked = isChecked,
                Tag = lib.Id,
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                IsEnabled = user.LibraryIds != null
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

        accessTab.Children.Add(MakeSwitchRow("Marker Editing",
            "Edit intro, recap, credits, and preview markers within assigned libraries.", markerEditSwitch));
        accessTab.Children.Add(MakeSwitchRow("Metadata Curation",
            "Edit, refresh, and rematch metadata within assigned libraries.", metadataCurationSwitch));

        // Downloads toggles in bordered cards
        accessTab.Children.Add(MakeSwitchRow("Downloads Allowed", downloadSwitch));
        accessTab.Children.Add(MakeSwitchRow("Download Transcode Allowed", downloadTranscodeSwitch));

        // ---- Limits tab ----
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
        var transcodeAllowedSwitch = new ToggleSwitch
        {
            IsOn = user.TranscodeAllowed,
            OnContent = "Video allowed",
            OffContent = "Video disabled",
        };
        var audioTranscodeSwitch = new ToggleSwitch
        {
            IsOn = user.AudioTranscodeAllowed,
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

        // Upstream commit e2428e7: floor raised from 0 (unlimited) to 1. The
        // server now rejects max_profiles < 1 with a 400 and migration 093
        // upgrades any legacy 0 rows to 5.
        var maxProfilesBox = new NumberBox
        {
            Value = user.MaxProfiles <= 0 ? 5 : user.MaxProfiles,
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
        string currentPreset = PlaybackQualityPresetFromValue(user.MaxPlaybackQuality);
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

        // Max Playback Quality - full width
        var qualityGroup = new StackPanel { Spacing = 4 };
        qualityGroup.Children.Add(MakeFormLabel("Max Playback Quality"));
        qualityGroup.Children.Add(qualityCombo);
        qualityGroup.Children.Add(qualityDescription);
        limitsTab.Children.Add(qualityGroup);

        // ---- Tab container using Pivot ----
        var pivot = new Pivot
        {
            Margin   = new Thickness(-12, 0, -12, 0),
            FontSize = 13
        };

        var accountItem = new PivotItem { Header = "Account", Content = accountTab };
        var accessItem  = new PivotItem { Header = "Access",  Content = accessTab, Margin = new Thickness(0, 8, 0, 0) };
        var limitsItem  = new PivotItem { Header = "Limits",  Content = limitsTab, Margin = new Thickness(0, 8, 0, 0) };

        pivot.Items.Add(accountItem);
        pivot.Items.Add(accessItem);
        pivot.Items.Add(limitsItem);

        var container = new StackPanel { Width = 520 };
        container.Children.Add(pivot);

        UpdateUserRequest? GetRequest()
        {
            List<int>? libraryIds = null;
            if (allLibsToggle.IsChecked != true)
            {
                libraryIds = libraryCheckboxes
                    .Where(kv => kv.Value.IsChecked == true)
                    .Select(kv => kv.Key)
                    .ToList();
            }

            string qualityValue = "";
            if (qualityCombo.SelectedIndex >= 0 && qualityCombo.SelectedIndex < PlaybackQualityOptions.Length)
                qualityValue = PlaybackQualityValueFromPreset(PlaybackQualityOptions[qualityCombo.SelectedIndex].Value);

            var permissions = new HashSet<string>(assignedPermissions, StringComparer.OrdinalIgnoreCase);
            if (markerEditSwitch.IsOn) permissions.Add("marker_edit"); else permissions.Remove("marker_edit");
            if (metadataCurationSwitch.IsOn) permissions.Add("metadata_curation"); else permissions.Remove("metadata_curation");

            var req = new UpdateUserRequest
            {
                Username                 = usernameBox.Text.Trim(),
                Email                    = emailBox.Text.Trim(),
                Password                 = string.IsNullOrEmpty(passwordBox.Password) ? null : passwordBox.Password,
                Role                     = roleCombo.SelectedItem as string ?? "user",
                Enabled                  = enabledSwitch.IsOn,
                Permissions              = permissions.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                MaxStreams                = double.IsNaN(maxStreamsBox.Value) ? 0 : (int)maxStreamsBox.Value,
                MaxTranscodes            = double.IsNaN(maxTranscodesBox.Value) ? 0 : (int)maxTranscodesBox.Value,
                TranscodeAllowed          = transcodeAllowedSwitch.IsOn,
                AudioTranscodeAllowed     = audioTranscodeSwitch.IsOn,
                MaxProfiles              = double.IsNaN(maxProfilesBox.Value) ? 5 : Math.Max(1, (int)maxProfilesBox.Value),
                MaxPlaybackQuality       = qualityValue,
                DownloadAllowed          = downloadSwitch.IsOn,
                DownloadTranscodeAllowed = downloadTranscodeSwitch.IsOn,
                LibraryIds               = libraryIds,
                LibraryIdsSpecified      = true,
                AccessGroupId            = (accessGroupCombo.SelectedItem as ComboBoxItem)?.Tag is long groupId ? groupId : null,
                AccessGroupIdSpecified   = true
            };
            return req;
        }

        return (container, GetRequest);
    }

    // ===== Delete dialog =====

    private async Task OpenDeleteDialogAsync()
    {
        if (ViewModel.User == null) return;

        var dialog = new ContentDialog
        {
            Title             = "Delete user",
            Content           = $"Delete user \"{ViewModel.User.Username}\"? This cannot be undone.",
            PrimaryButtonText  = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
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
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        }
    }

    // ===== Playback Quality Helpers =====

    // B54: All four helpers delegate to SiloPlayer.Core.Helpers.PlaybackQuality.
    private static string PlaybackQualityPresetFromValue(string? value)
        => Core.Helpers.PlaybackQuality.PresetFromValue(value);

    private static string PlaybackQualityValueFromPreset(string preset)
        => Core.Helpers.PlaybackQuality.ValueFromPreset(preset);

    private static string CanonicalPlaybackQuality(string? value)
        => Core.Helpers.PlaybackQuality.Canonical(value);

    private static string FormatPlaybackQualityPreset(string? value)
        => Core.Helpers.PlaybackQuality.FormatPreset(value);

    private static string FormatLastActive(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "Never"
            : Core.Helpers.TimeAgo.FormatShort(value);

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

    private static Border MakeSwitchRow(string label, string description, ToggleSwitch toggle)
    {
        var border = new Border
        {
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
        copy.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(toggle, 1); grid.Children.Add(copy); grid.Children.Add(toggle); border.Child = grid;
        return border;
    }
}
