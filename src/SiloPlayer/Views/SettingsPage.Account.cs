using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private int _deviceDetailGeneration;
    private int _deviceListGeneration;
    private void DeactivateDevices() { ++_deviceListGeneration; ++_deviceDetailGeneration; }
    private async Task LoadDevicesAsync()
    {
        var generation = ++_deviceListGeneration;
        ++_deviceDetailGeneration;
        var api = App.Services.GetRequiredService<SettingsApi>();
        var context = api.CaptureContext();
        bool Current() => generation == _deviceListGeneration && api.IsCurrentContext(context);
        DevicesLoadingRing.IsActive = true;
        DevicesContentHost.Children.Clear();
        try
        {
            DevicesContentHost.Children.Add(BuildProfileLaunchGroup());
            var scopeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(scopeButtons, "Whose devices to show");
            var mine = new Button { Content = "Just mine", MinHeight = 42 };
            var everyone = new Button { Content = "Everyone", MinHeight = 42 };
            scopeButtons.Children.Add(mine);
            if (_canManageProfiles)
                scopeButtons.Children.Add(everyone);
            DevicesContentHost.Children.Add(scopeButtons);

            var search = new TextBox { PlaceholderText = "Search devices", MinHeight = 42, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
            DevicesContentHost.Children.Add(search);

            var profileFilter = new ComboBox
            {
                PlaceholderText = "All profiles",
                MinHeight = 42,
                MinWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = Visibility.Collapsed,
            };
            DevicesContentHost.Children.Add(profileFilter);

            var browser = new Grid { ColumnSpacing = 18 };
            browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var listSurface = SettingsSurface();
            var listScroller = new ScrollViewer { Content = listSurface, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var listHost = (StackPanel)listSurface.Child;
            var detailScroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var detailHost = new StackPanel { Spacing = 12 };
            var backToList = new Button { Content = "Back to devices", Visibility = Visibility.Collapsed };
            detailScroller.Content = new StackPanel { Spacing = 12, Children = { backToList, detailHost } };
            browser.Children.Add(listScroller);
            Grid.SetColumn(detailScroller, 1);
            browser.Children.Add(detailScroller);
            DevicesContentHost.Children.Add(browser);

            List<UserDevice> devices = [];
            UserDevice? selected = null;
            var household = false;
            string? selectedProfileId = null;
            var suppressProfileFilter = false;
            var showDormant = false;
            var reveal = new Button { Content = "Show inactive devices", HorizontalAlignment = HorizontalAlignment.Left };
            DevicesContentHost.Children.Add(reveal);
            var detailOpen = false;
            var scopeRevision = 0;
            void AdaptBrowser()
            {
                var narrow = browser.ActualWidth < 900;
                backToList.Visibility = narrow && detailOpen ? Visibility.Visible : Visibility.Collapsed;
                browser.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(280);
                browser.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(detailScroller, narrow ? 0 : 1);
                listScroller.Visibility = narrow && detailOpen ? Visibility.Collapsed : Visibility.Visible;
                detailScroller.Visibility = narrow && !detailOpen ? Visibility.Collapsed : Visibility.Visible;
            }
            backToList.Click += (_, _) => { detailOpen = false; AdaptBrowser(); };
            browser.SizeChanged += (_, _) => AdaptBrowser();

            async Task RenderListAsync(bool chooseDefault)
            {
                if (!Current()) return;
                var query = search.Text?.Trim() ?? "";
                var visible = devices.Where(device =>
                    (string.IsNullOrWhiteSpace(selectedProfileId)
                     || string.Equals(device.ProfileId, selectedProfileId, StringComparison.Ordinal))
                    && (showDormant || !string.IsNullOrEmpty(query) || !DeviceSettingDisplay.IsDormant(device, DateTimeOffset.UtcNow))
                    && (string.IsNullOrEmpty(query)
                        || DeviceSelectorLabel(device).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || device.DevicePlatform.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || device.ProfileName.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
                listHost.Children.Clear();
                var dormantCount = devices.Count(device => DeviceSettingDisplay.IsDormant(device, DateTimeOffset.UtcNow));
                reveal.Content = showDormant ? "Hide inactive devices" : $"Show {dormantCount} inactive devices";
                reveal.Visibility = dormantCount > 0 && string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
                search.PlaceholderText = $"Search {devices.Count} devices";
                if (visible.Count == 0)
                {
                    listHost.Children.Add(SecondaryText(devices.Count == 0
                        ? "No devices yet. They appear here as you sign in on them."
                        : "No devices match this search."));
                    detailHost.Children.Clear();
                    return;
                }
                var loadSelected = chooseDefault || selected == null || !visible.Any(device => DeviceSelection.Key(device) == DeviceSelection.Key(selected));
                selected = DeviceSelection.Select(visible, chooseDefault ? null : selected == null ? null : DeviceSelection.Key(selected),
                    App.Services.GetRequiredService<AuthService>().SelectedProfileId);

                void AddGroup(string title, IEnumerable<UserDevice> group)
                {
                    var groupItems = group.ToList();
                    if (groupItems.Count == 0) return;
                    listHost.Children.Add(new TextBlock
                    {
                        Text = title.ToUpperInvariant(),
                        FontSize = 10,
                        CharacterSpacing = 130,
                        Foreground = SecondaryText("").Foreground,
                        Margin = new Thickness(0, 8, 0, 0),
                    });
                    foreach (var device in groupItems)
                    {
                        var changed = device.ChangedCount == 0 ? "Nothing changed" : $"{device.ChangedCount} setting{(device.ChangedCount == 1 ? "" : "s")} changed here";
                        var owner = household && !string.IsNullOrWhiteSpace(device.ProfileName) ? $" · {device.ProfileName}" : "";
                        var button = new Button
                        {
                            Tag = device,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Padding = new Thickness(10),
                            BorderBrush = DeviceSelection.Key(device) == DeviceSelection.Key(selected!)
                                ? (Brush)Application.Current.Resources["AccentBrush"] : (Brush)Application.Current.Resources["BorderBrush"],
                            BorderThickness = new Thickness(1),
                            Content = new StackPanel
                            {
                                Spacing = 3,
                                Children =
                                {
                                    new TextBlock { Text = (string.IsNullOrWhiteSpace(device.DeviceName) ? "Unknown device" : device.DeviceName) + owner, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                                    SecondaryText(device.IsCurrentDevice ? "This is the device you're on" : RelativeTime(device.LastSeenAt)),
                                    SecondaryText(changed),
                                },
                            },
                        };
                        button.Click += async (_, _) =>
                        {
                            selected = device;
                            await LoadDeviceDetailAsync(device, detailHost);
                            await RenderListAsync(false);
                            detailOpen = true;
                            AdaptBrowser();
                        };
                        listHost.Children.Add(button);
                    }
                }

                var now = DateTimeOffset.UtcNow;
                if (household && string.IsNullOrWhiteSpace(selectedProfileId))
                {
                    foreach (var owner in visible.GroupBy(device => device.ProfileId).OrderByDescending(group => group.Key == App.Services.GetRequiredService<AuthService>().SelectedProfileId))
                        AddGroup($"{owner.First().ProfileName} ({owner.Count()})", owner);
                }
                else
                {
                AddGroup("Using now", visible.Where(device => device.IsCurrentDevice));
                AddGroup("This week", visible.Where(device => !device.IsCurrentDevice && now - ParseTimestamp(device.LastSeenAt) <= TimeSpan.FromDays(7)));
                AddGroup("Earlier", visible.Where(device => !device.IsCurrentDevice && now - ParseTimestamp(device.LastSeenAt) > TimeSpan.FromDays(7)));
                }

                if (loadSelected)
                {
                    if (selected == null) return;
                    await LoadDeviceDetailAsync(selected, detailHost);
                }
            }

            async Task LoadScopeAsync(bool useHousehold)
            {
                if (!Current()) return;
                if (useHousehold && !_canManageProfiles)
                    return;
                var revision = ++scopeRevision;
                household = useHousehold;
                mine.IsEnabled = useHousehold;
                everyone.IsEnabled = !useHousehold;
                try
                {
                    var response = await api.GetUserDevicesAsync(useHousehold);
                    if (!Current() || revision != scopeRevision) return;
                    devices = response.Devices
                        .OrderByDescending(device => device.IsCurrentDevice)
                        .ThenByDescending(device => ParseTimestamp(device.LastSeenAt))
                        .ToList();
                    suppressProfileFilter = true;
                    selectedProfileId = null;
                    profileFilter.Items.Clear();
                    profileFilter.Items.Add(new ComboBoxItem { Content = "All profiles", Tag = "" });
                    foreach (var profile in devices
                                 .Where(device => !string.IsNullOrWhiteSpace(device.ProfileId))
                                 .GroupBy(device => device.ProfileId, StringComparer.Ordinal)
                                 .Select(group => group.First())
                                 .OrderByDescending(device => device.ProfileId == App.Services.GetRequiredService<AuthService>().SelectedProfileId)
                                 .ThenBy(device => device.ProfileName, StringComparer.CurrentCultureIgnoreCase))
                    {
                        profileFilter.Items.Add(new ComboBoxItem
                        {
                            Content = $"{(string.IsNullOrWhiteSpace(profile.ProfileName) ? "Unknown profile" : profile.ProfileName)} ({devices.Count(device => device.ProfileId == profile.ProfileId)})",
                            Tag = profile.ProfileId,
                        });
                    }
                    profileFilter.SelectedIndex = 0;
                    profileFilter.Visibility = useHousehold && profileFilter.Items.Count > 2
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    suppressProfileFilter = false;
                    everyone.Content = useHousehold && devices.Count > 0 ? $"Everyone ({devices.Count})" : "Everyone";
                    selected = null;
                    await RenderListAsync(true);
                }
                catch when (useHousehold)
                {
                    if (!Current() || revision != scopeRevision) return;
                    everyone.IsEnabled = false;
                    Toast("Household devices are not available for this account", error: true);
                }
            }

            reveal.Click += async (_, _) => { showDormant = !showDormant; await RenderListAsync(false); };
            search.TextChanged += async (_, _) => await RenderListAsync(false);
            profileFilter.SelectionChanged += async (_, _) =>
            {
                if (suppressProfileFilter) return;
                selectedProfileId = profileFilter.SelectedItem is ComboBoxItem { Tag: string profileId }
                    && !string.IsNullOrWhiteSpace(profileId)
                        ? profileId
                        : null;
                selected = null;
                await RenderListAsync(true);
            };
            mine.Click += async (_, _) => await LoadScopeAsync(false);
            everyone.Click += async (_, _) => await LoadScopeAsync(true);
            await LoadScopeAsync(false);
        }
        catch (Exception ex)
        {
            if (!Current()) return;
            DevicesContentHost.Children.Add(ErrorText($"Devices could not be loaded: {ex.Message}"));
        }
        finally
        {
            if (generation == _deviceListGeneration) DevicesLoadingRing.IsActive = false;
        }
    }

    private async Task LoadDeviceDetailAsync(UserDevice device, StackPanel host)
    {
        var generation = ++_deviceDetailGeneration;
        host.Children.Clear();
        var loading = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(loading);
        try
        {
            var api = App.Services.GetRequiredService<SettingsApi>();
            var context = api.CaptureContext();
            var auth = App.Services.GetRequiredService<AuthService>();
            var forSomeoneElse = !string.IsNullOrWhiteSpace(device.ProfileId)
                && !string.Equals(device.ProfileId, auth.SelectedProfileId, StringComparison.Ordinal);
            var ownerLabel = forSomeoneElse && !string.IsNullOrWhiteSpace(device.ProfileName)
                ? $"{device.ProfileName}'s"
                : "your";
            SettingsContractCapabilities? capabilities = null;
            try
            {
                capabilities = await api.GetContractCapabilitiesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings capability check failed: {ex}");
            }
            if (generation != _deviceDetailGeneration) return;
            if (capabilities is not
                {
                    ApiVersion: 1,
                    Revision: >= 1,
                    SupportsBatchedEffective: true,
                    SupportsIdempotentWrites: true,
                })
            {
                host.Children.Clear();
                var unavailable = CreateSettingsGroup(
                    "Couldn't check settings compatibility",
                    "Device controls stay unavailable until Silo confirms which settings this server supports.");
                var retry = new Button { Content = "Retry compatibility check", HorizontalAlignment = HorizontalAlignment.Left };
                retry.Click += async (_, _) => await LoadDeviceDetailAsync(device, host);
                ((StackPanel)unavailable.Child).Children.Add(retry);
                host.Children.Add(unavailable);
                return;
            }
            var definitions = DeviceSettingDisplay.ForRevision(capabilities.Revision);
            var effective = await api.GetContractEffectiveSettingsAsync(definitions.Select(definition => definition.Key), device.DeviceId, device.ProfileId);
            if (generation != _deviceDetailGeneration || !api.IsCurrentContext(context)) return;
            var values = effective.Settings.ToDictionary(value => value.Key, StringComparer.Ordinal);
            var retainedOnDevice = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var definition in definitions.Where(definition => definition.ProfileFirst &&
                values.TryGetValue(definition.Key, out var entry) && entry.Source == "profile"))
            {
                try
                {
                    var stored = await api.GetContractStoredSettingAsync(definition.Key, "profile_device", device.DeviceId, device.ProfileId);
                    if (generation != _deviceDetailGeneration || !api.IsCurrentContext(context)) return;
                    retainedOnDevice[definition.Key] = stored.Value.Clone();
                }
                catch (ApiException ex) when (ex.StatusCode == 404) { }
                if (generation != _deviceDetailGeneration || !api.IsCurrentContext(context)) return;
            }


            var summary = SettingsSurface();
            var summaryStack = (StackPanel)summary.Child;
            var heading = new Grid { ColumnSpacing = 12 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameStack = new StackPanel { Spacing = 3 };
            nameStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(device.DeviceName) ? "Unknown device" : device.DeviceName,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = PrimaryBrush(),
                TextWrapping = TextWrapping.Wrap,
            });
            nameStack.Children.Add(SecondaryText(DeviceSummary(device)));
            heading.Children.Add(nameStack);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (device.ChangedCount > 0)
            {
                var clear = new Button { Content = "Use profile settings" };
                clear.Click += async (_, _) =>
                {
                    var noun = device.ChangedCount == 1 ? "setting" : "settings";
                    if (!await ConfirmAsync("Use profile settings?",
                            $"Use {ownerLabel} profile settings on {device.DeviceName}? This removes the {device.ChangedCount} {noun} changed on this device. Settings that only apply to devices go back to the app default.")) return;
                    try
                    {
                        await api.ClearUserDeviceSettingsAsync(device.DeviceId, device.ProfileId);
                        Toast("Removed the changes on this device");
                        await LoadDevicesAsync();
                    }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<ToastService>().Error(ex.Message);
                    }
                };
                actions.Children.Add(clear);
            }
            if (!device.IsCurrentDevice)
            {
                var forget = new Button { Content = "Forget" };
                forget.Click += async (_, _) =>
                {
                    if (!await ConfirmAsync("Forget device?",
                            $"Forget {device.DeviceName}? Its settings are removed and it disappears from this list until it's used again.")) return;
                    try
                    {
                        await api.ForgetUserDeviceAsync(device.DeviceId, device.ProfileId);
                        Toast("Device forgotten");
                        await LoadDevicesAsync();
                    }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<ToastService>().Error(ex.Message);
                    }
                };
                actions.Children.Add(forget);
            }
            Grid.SetColumn(actions, 1);
            heading.Children.Add(actions);
            summaryStack.Children.Add(heading);
            host.Children.Clear();
            host.Children.Add(summary);

            if (forSomeoneElse)
            {
                var actingBanner = InfoCallout(
                    $"You're changing {device.ProfileName}'s settings, not your own.",
                    $"{device.ProfileName} will see these changes on this device.");
                host.Children.Add(actingBanner);
            }

            var scope = SettingsSurface();
            ((StackPanel)scope.Child).Children.Add(SecondaryText(
                $"These apply to this device, for {ownerLabel} profile only. " +
                $"{(forSomeoneElse ? ownerLabel : "Your")} other devices, and anyone else who uses this one, are unaffected." +
                (device.IsCurrentDevice ? "" : " This device picks up the changes the next time it's used.")));
            host.Children.Add(scope);

            foreach (var groupName in new[] { "Picture", "Sound", "Subtitles", "Episodes" })
            {
                var visible = definitions.Where(definition => definition.Group == groupName
                    && values.TryGetValue(definition.Key, out var entry)
                    && (definition.AppliesTo(device.DevicePlatform) || entry.Scope == "profile_device")).ToList();
                if (visible.Count == 0) continue;
                var group = CreateSettingsGroup(groupName, groupName switch { "Picture" => "How video looks on this device", "Sound" => "Audio on this device", "Subtitles" => "On this device", _ => "What happens between episodes" });
                foreach (var definition in visible) AddDefinedDeviceControl(group, api, device, values, definition, retainedOnDevice);
                host.Children.Add(group);
            }

            if (forSomeoneElse)
            {
                host.Children.Add(InfoCallout(
                    "What you can't see here.",
                    "This page shows how Silo is set up on each device — not what anyone watched. Viewing history stays private to each profile."));
            }
        }
        catch (Exception ex)
        {
            if (generation != _deviceDetailGeneration) return;
            host.Children.Clear();
            host.Children.Add(ErrorText($"Device settings could not be loaded: {ex.Message}"));
        }
    }

    private Border BuildProfileLaunchGroup()
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        var group = CreateSettingsGroup("This app", "Applies only to this app. Your other devices keep their own choice.");
        var content = (StackPanel)group.Child;
        content.Children.Add(new TextBlock { Text = "Profile at launch", FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        var choices = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var hint = SecondaryText("");
        void Refresh()
        {
            var ask = settings.Load().ProfileLaunchMode == "ask";
            hint.Text = ask ? "Each new launch starts at “Who's watching?”, and a PIN-protected profile needs its PIN again."
                : "New launches open the profile last used here.";
        }
        foreach (var (mode, label) in new[] { ("remember", "Remember last profile"), ("ask", "Ask who's watching") })
        {
            var button = new RadioButton { Content = label, GroupName = "ProfileLaunch", IsChecked = settings.Load().ProfileLaunchMode == mode };
            button.Checked += (_, _) => { var local = settings.Load(); local.ProfileLaunchMode = mode; settings.Save(local); Refresh(); };
            choices.Children.Add(button);
        }
        content.Children.Add(choices); content.Children.Add(hint); Refresh();
        return group;
    }

    private async Task LoadConnectAppsAsync()
    {
        ConnectAppsLoadingRing.IsActive = true;
        ConnectAppsContentHost.Children.Clear();
        try
        {
            var api = App.Services.GetRequiredService<SettingsApi>();
            var auth = App.Services.GetRequiredService<AuthService>();
            var compatTask = api.GetCompatConnectInfoAsync();
            var profilesTask = api.GetProfilesAsync();
            await Task.WhenAll(compatTask, profilesTask);

            var compat = compatTask.Result;
            var profiles = profilesTask.Result.Profiles;
            Profile? selectedProfile = profiles.FirstOrDefault(profile => profile.Id == auth.SelectedProfileId)
                ?? profiles.FirstOrDefault();
            var accountName = auth.CurrentUser?.Username ?? "";

            var picker = new Grid { ColumnSpacing = 8, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, "App type");
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var siloButton = new Button { Content = "Silo app or website", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 46 };
            var jellyfinButton = new Button { Content = "Jellyfin-compatible app", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 46 };
            picker.Children.Add(siloButton);
            Grid.SetColumn(jellyfinButton, 1);
            picker.Children.Add(jellyfinButton);
            var details = new StackPanel { Spacing = 12 };
            ConnectAppsContentHost.Children.Add(picker);
            ConnectAppsContentHost.Children.Add(details);

            void ShowSilo()
            {
                details.Children.Clear();
                details.Children.Add(InfoCallout("For Silo's own apps",
                    "Silo for iPhone, Apple TV, Android, Android TV, and this Windows app. Don't add a # to either field here."));
                var card = CreateSettingsGroup("Sign-in details", "Use these values only on a Silo sign-in screen.");
                AddCredentialRow(card, "Server", auth.ConfiguredServerUrl, "The address of this Silo server.");
                AddCredentialRow(card, "Username", accountName, "Just your account name.");
                AddCredentialRow(card, "Password", "your password",
                    "Your account password. The profile PIN is asked for separately, in the app.", canCopy: false);
                details.Children.Add(card);
                details.Children.Add(InfoCallout("Profiles come next",
                    "After signing in you'll choose a profile from the profile picker, and PIN-protected profiles prompt for their PIN there."));
            }

            void ShowJellyfin()
            {
                details.Children.Clear();
                details.Children.Add(InfoCallout("For Jellyfin-compatible apps only",
                    "Infuse, Swiftfin, JellyCon, Findroid, Jellyfin Media Player. These credentials will not work on a Silo sign-in screen."));

                if (compat.Account.PasswordLoginAvailable == false)
                {
                    details.Children.Add(InfoCallout("This account can't sign in to a Jellyfin app",
                        "It signs in through an external provider rather than a Silo password, and the compatibility API only accepts Silo passwords. Use a Silo app, or ask an administrator about an account with password sign-in."));
                    return;
                }
                if (!compat.Jellyfin.Enabled)
                {
                    details.Children.Add(InfoCallout(
                        compat.Jellyfin.PendingRestart
                            ? "The Jellyfin compatibility API isn't running yet"
                            : "The Jellyfin compatibility API is turned off",
                        compat.Jellyfin.PendingRestart
                            ? "An administrator has turned it on, but the server has to restart before it starts accepting connections."
                            : "Third-party Jellyfin apps can't reach this server until an administrator enables it in Admin → Settings → Compatibility."));
                    return;
                }

                var profileBlock = new StackPanel { Spacing = 8 };
                profileBlock.Children.Add(new TextBlock
                {
                    Text = "WHICH PROFILE ARE YOU SIGNING IN AS?",
                    FontSize = 11,
                    CharacterSpacing = 140,
                    Foreground = SecondaryText("").Foreground,
                });
                var profileButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                profileBlock.Children.Add(profileButtons);
                details.Children.Add(profileBlock);

                var credentialHost = new StackPanel { Spacing = 12 };
                details.Children.Add(credentialHost);

                void RenderCredentials()
                {
                    credentialHost.Children.Clear();
                    var profileName = selectedProfile?.Name?.Trim() ?? "";
                    var profileNameIssue = profileName.Contains('#')
                        ? "This profile's name contains a #, which Jellyfin apps can't sign in with. Rename it in Settings → Profiles to use it from one."
                        : null;
                    var username = string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(profileName)
                        ? accountName
                        : $"{accountName}#{profileName}";
                    var loopback = IsLoopbackUrl(compat.Jellyfin.PublicUrl);
                    var card = CreateSettingsGroup("", "");
                    var stack = (StackPanel)card.Child;
                    stack.Children.Clear();
                    AddCredentialRow(card, "SERVER", compat.Jellyfin.PublicUrl,
                        loopback
                            ? "This address only works on the server itself, so phones and TVs can't reach it. An administrator needs to set the compatibility API's public address."
                            : "The compatibility API listens on its own address — not the one this page is on.",
                        canCopy: !loopback);
                    AddCredentialRow(card, "USERNAME", username,
                        profileNameIssue ?? $"Your account name, then #, then the profile name — not just “{accountName}”.",
                        canCopy: profileNameIssue == null);
                    AddCredentialRow(card, "PASSWORD", "your password",
                        selectedProfile?.HasPin == true
                            ? $"{profileName} has a PIN, so append # and the PIN to your account password."
                            : $"{profileName} has no PIN — just your account password, nothing appended.",
                        canCopy: false);
                    stack.Children.Add(SecondaryText(
                        "Jellyfin apps offer only two boxes and never prompt for a profile, so the profile name and PIN are appended here. This format is rejected on a Silo sign-in screen."));
                    credentialHost.Children.Add(card);

                    var faq = new Expander
                    {
                        Header = "A Jellyfin app says my username or password is wrong",
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Content = new TextBlock
                        {
                            Text = $"You left the profile off the username — signing in as plain “{accountName}” only works if a profile is named “{accountName}”, or exactly one profile has no PIN. Adding #ProfileName always works.\n\nThe profile has a PIN and you didn't append it — PIN-protected profiles need password#PIN. There is no second prompt.\n\nYour account password itself contains a # — type it in full and append #PIN anyway. Silo splits at the last # only.\n\nTwo profiles share a name — profile names are matched without case sensitivity, so duplicates are ambiguous. Rename one in Settings → Profiles.\n\nYou used the Silo app's address — the compatibility API is a separate address: {compat.Jellyfin.PublicUrl}",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = SecondaryText("").Foreground,
                            Margin = new Thickness(12),
                        },
                    };
                    credentialHost.Children.Add(faq);

                    if (profiles.Count > 1)
                    {
                        var everyProfile = CreateSettingsGroup("Every profile at a glance", "");
                        var everyProfileStack = (StackPanel)everyProfile.Child;
                        everyProfileStack.Children.RemoveAt(everyProfileStack.Children.Count - 1);
                        foreach (var profile in profiles)
                        {
                            var issue = profile.Name.Contains('#');
                            everyProfileStack.Children.Add(new TextBlock
                            {
                                Text = issue
                                    ? $"{profile.Name}  ·  rename to use from a Jellyfin app"
                                    : $"{accountName}#{profile.Name}{(profile.HasPin ? "  ·  needs #PIN" : "")}",
                                FontFamily = new FontFamily("Cascadia Mono"),
                                FontSize = 13,
                                Foreground = issue ? SecondaryText("").Foreground : PrimaryBrush(),
                                TextWrapping = TextWrapping.Wrap,
                            });
                        }
                        credentialHost.Children.Add(everyProfile);
                    }
                }

                foreach (var profile in profiles)
                {
                    var profileButton = new Button { Content = profile.Name, MinHeight = 40 };
                    profileButton.Click += (_, _) =>
                    {
                        selectedProfile = profile;
                        RenderCredentials();
                    };
                    profileButtons.Children.Add(profileButton);
                }
                RenderCredentials();
            }

            siloButton.Click += (_, _) => ShowSilo();
            jellyfinButton.Click += (_, _) => ShowJellyfin();
            ShowJellyfin();
        }
        catch (Exception ex)
        {
            ConnectAppsContentHost.Children.Clear();
            ConnectAppsContentHost.Children.Add(InfoCallout(
                "Couldn't load your sign-in details",
                "Reload the page to try again. Credentials are withheld rather than guessed, so nothing here is stale or wrong."));
            System.Diagnostics.Debug.WriteLine($"Connect Apps load failed: {ex}");
        }
        finally
        {
            ConnectAppsLoadingRing.IsActive = false;
        }
    }

    private static bool IsLoopbackUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.Trim('[', ']').ToLowerInvariant();
        return host is "localhost" or "::1" or "0.0.0.0"
            || host.EndsWith(".localhost", StringComparison.Ordinal)
            || host.StartsWith("127.", StringComparison.Ordinal);
    }

    private static Border InfoCallout(string title, string description)
    {
        var callout = SettingsSurface();
        callout.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 96, 165, 250));
        callout.BorderThickness = new Thickness(1);
        var stack = (StackPanel)callout.Child;
        stack.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = PrimaryBrush() });
        stack.Children.Add(SecondaryText(description));
        return callout;
    }

    private void AddCredentialRow(Border group, string label, string value, string hint, bool canCopy = true)
    {
        var row = SettingsRow(label, hint, out var controls);
        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "Not available" : value,
            FontFamily = new FontFamily("Cascadia Mono"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        };
        controls.Children.Add(text);
        if (canCopy && !string.IsNullOrWhiteSpace(value))
        {
            var copy = new Button { Content = "Copy", MinWidth = 64 };
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(value);
                Clipboard.SetContent(package);
                Toast("Copied to clipboard");
            };
            controls.Children.Add(copy);
        }
        ((StackPanel)group.Child).Children.Add(row);
    }

    private void AddDefinedDeviceControl(Border group, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, DeviceSettingDefinition definition,
        IReadOnlyDictionary<string, JsonElement> retainedOnDevice)
    {
        var entry = values[definition.Key];
        var row = SettingsRow(definition.Label, definition.Description, out var controls);
        row.Tag = definition.Control == "switch" ? "device-inline-row" : "device-value-row";
        var writable = DeviceSettingDisplay.CanWrite(entry);
        var profileWide = definition.ProfileFirst && entry.Source == "profile";
        var retainedHere = profileWide && retainedOnDevice.ContainsKey(definition.Key);
        var mainWritable = writable && !profileWide;
        var context = api.CaptureContext();
        var generation = _deviceDetailGeneration;
        if (row.Children[0] is StackPanel copy)
        {
            var changed = entry.Scope == "profile_device" || retainedHere;
            var auth = App.Services.GetRequiredService<AuthService>();
            var ownerLabel = device.ProfileId != auth.SelectedProfileId && !string.IsNullOrWhiteSpace(device.ProfileName)
                ? $"{device.ProfileName}'s" : "your";
            if (changed) copy.Children.Add(SecondaryText("Changed here"));
            if (entry.Constrained)
            {
                copy.Children.Add(SecondaryText("Household limit"));
                copy.Children.Add(SecondaryText(DeviceSettingDisplay.ConstraintExplanation(entry)));
            }
            else if (!changed && !profileWide && DeviceSettingDisplay.InheritedSource(entry, ownerLabel) is { } inherited)
                copy.Children.Add(SecondaryText(inherited));
            if (profileWide)
            {
                var choice = retainedHere ? DeviceSettingDefinition.Scalar(retainedOnDevice[definition.Key]) switch
                    { "true" => "On", "false" => "Off", var other => other } : "";
                copy.Children.Add(SecondaryText((retainedHere
                    ? $"Set for all devices on this profile, so this device's own choice ({choice}) isn't used right now. "
                    : "Set for all devices on this profile. ") + "Turn off “Apply to all devices” to choose per device."));
            }
        }
        AddChangedHereIndicator(row, controls, api, device, values, definition.Key, retainedHere);
        var busy = false;
        async Task SaveAsync(object? value)
        {
            if (!mainWritable || busy || generation != _deviceDetailGeneration || !api.IsCurrentContext(context)) return;
            busy = true; SetSettingsEnabled(row, false);
            try
            {
                if (value == null) await api.DeleteContractSettingValueAsync(context, definition.Key, "profile_device", device.DeviceId, device.ProfileId);
                else await api.SetContractSettingValueAsync(context, definition.Key, "profile_device", value, device.DeviceId, device.ProfileId);
                if (generation == _deviceDetailGeneration && api.IsCurrentContext(context) && definition.Key == "ui.title_art") ViewModel.PublishTitleArtPreferenceChanged();
            }
            catch (Exception ex) { Toast($"Could not save {definition.Label}: {ex.Message}", error: true); }
            finally
            {
                busy = false;
                // Re-resolve inheritance and constraints after both success and rejection.
                if (generation == _deviceDetailGeneration && api.IsCurrentContext(context) && group.Parent is StackPanel currentHost) await LoadDeviceDetailAsync(device, currentHost);
                else SetSettingsEnabled(row, mainWritable);
            }
        }
        var current = DeviceSettingDefinition.Scalar(entry.Value);
        switch (definition.Control)
        {
            case "switch":
                var toggle = new ToggleSwitch { IsOn = current == "true", OnContent = "", OffContent = "" };
                toggle.Toggled += async (_, _) => await SaveAsync(toggle.IsOn);
                controls.Children.Add(toggle);
                break;
            case "select":
                var combo = new ComboBox { MinWidth = 180, MaxWidth = 280 };
                var options = definition.Type == "language_tag" ? DeviceLanguageOptions(definition.Key.Contains("subtitle") ? "None" : "No preference") : definition.Options;
                foreach (var option in options) combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
                SelectComboBoxByTag(combo, current);
                if (definition.Type == "language_tag") ConfigureLanguageCombo(combo);
                combo.SelectionChanged += async (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem { Tag: string value } || value == "__other") return;
                    await SaveAsync(value.Length == 0 ? null : definition.Type is "integer" or "number" || definition.Schema.TryGetProperty("values", out var members) && members.EnumerateArray().Any(member => member.GetProperty("value").ValueKind == JsonValueKind.Number)
                        ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : value);
                };
                controls.Children.Add(combo);
                break;
            case "slider":
            case "stepper":
                var box = new NumberBox { Minimum = definition.Minimum, Maximum = definition.Maximum, SmallChange = definition.Step,
                    Width = 150, Header = definition.Unit, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                    Value = double.TryParse(current, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : double.Parse(definition.Default, System.Globalization.CultureInfo.InvariantCulture) };
                box.LostFocus += async (_, _) =>
                {
                    if (double.IsFinite(box.Value)) await SaveAsync(definition.Type == "integer" ? (object)(int)box.Value : box.Value);
                };
                controls.Children.Add(box);
                break;
            case "panel" when definition.Key == "playback.subtitle_appearance":
                AddDevicePanelButton(group, api, device, values, definition.Key, definition.Label, definition.Description, "Change how they look");
                if (((StackPanel)group.Child).Children.LastOrDefault() is Grid panelRow) SetSettingsEnabled(panelRow, writable);
                return;
            default:
                var text = new TextBox { Text = current, MinWidth = 180, MaxWidth = 280 };
                text.LostFocus += async (_, _) => { if (text.Text != current) await SaveAsync(text.Text); };
                controls.Children.Add(text);
                break;
        }
        SetSettingsEnabled(row, writable);
        if (profileWide) foreach (var editor in controls.Children.OfType<Control>().Where(editor => editor is not Button)) editor.IsEnabled = false;
        ((StackPanel)group.Child).Children.Add(row);
    }

    private void AddDeviceToggle(Border group, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key,
        string label, string description, bool fallback)
    {
        var row = SettingsRow(label, description, out var controls);
        AddChangedHereIndicator(row, controls, api, device, values, key);
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", IsOn = ReadDeviceBool(values, key, fallback) };
        toggle.Toggled += async (_, _) =>
        {
            toggle.IsEnabled = false;
            try { await api.SetContractSettingValueAsync(key, "profile_device", toggle.IsOn, device.DeviceId, device.ProfileId); }
            catch (Exception ex) { Toast($"Could not save {label}: {ex.Message}", error: true); }
            finally { toggle.IsEnabled = true; }
        };
        controls.Children.Add(toggle);
        ((StackPanel)group.Child).Children.Add(row);
    }

    private void AddDeviceSelect(Border group, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key,
        string label, string description, (string Value, string Label)[] options, bool numeric = false)
    {
        var row = SettingsRow(label, description, out var controls);
        AddChangedHereIndicator(row, controls, api, device, values, key);
        var combo = new ComboBox { MinWidth = 220 };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        var current = ReadDeviceScalar(values, key);
        SelectComboBoxByTag(combo, current);
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string selected }) return;
            combo.IsEnabled = false;
            try
            {
                if (numeric && string.IsNullOrEmpty(selected))
                    await api.DeleteContractSettingValueAsync(key, "profile_device", device.DeviceId, device.ProfileId);
                else
                    await api.SetContractSettingValueAsync(key, "profile_device",
                        numeric && int.TryParse(selected, out var number) ? number : selected, device.DeviceId, device.ProfileId);
            }
            catch (Exception ex) { Toast($"Could not save {label}: {ex.Message}", error: true); }
            finally { combo.IsEnabled = true; }
        };
        controls.Children.Add(combo);
        ((StackPanel)group.Child).Children.Add(row);
    }

    private void AddDeviceNumber(Border group, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key,
        string label, string description, double minimum, double maximum, string unit = "Milliseconds")
    {
        var row = SettingsRow(label, description, out var controls);
        AddChangedHereIndicator(row, controls, api, device, values, key);
        var box = new NumberBox
        {
            Minimum = minimum,
            Maximum = maximum,
            SmallChange = 50,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = double.TryParse(ReadDeviceScalar(values, key), out var value) ? value : 0,
            Width = 150,
            Header = unit,
        };
        box.ValueChanged += async (_, args) =>
        {
            if (double.IsNaN(args.NewValue)) return;
            box.IsEnabled = false;
            try { await api.SetContractSettingValueAsync(key, "profile_device", (int)args.NewValue, device.DeviceId, device.ProfileId); }
            catch (Exception ex) { Toast($"Could not save {label}: {ex.Message}", error: true); }
            finally { box.IsEnabled = true; }
        };
        controls.Children.Add(box);
        ((StackPanel)group.Child).Children.Add(row);
    }

    private void AddDeviceDecimal(Border group, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key,
        string label, string description, double minimum, double maximum, double step, string unit)
    {
        var row = SettingsRow(label, description, out var controls);
        AddChangedHereIndicator(row, controls, api, device, values, key);
        var box = new NumberBox
        {
            Minimum = minimum,
            Maximum = maximum,
            SmallChange = step,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = double.TryParse(ReadDeviceScalar(values, key), out var value) ? value : 1,
            Width = 150,
            Header = unit,
        };
        box.ValueChanged += async (_, args) =>
        {
            if (double.IsNaN(args.NewValue)) return;
            box.IsEnabled = false;
            try { await api.SetContractSettingValueAsync(key, "profile_device", args.NewValue, device.DeviceId, device.ProfileId); }
            catch (Exception ex) { Toast($"Could not save {label}: {ex.Message}", error: true); }
            finally { box.IsEnabled = true; }
        };
        controls.Children.Add(box);
        ((StackPanel)group.Child).Children.Add(row);
    }

    private void AddDevicePanelButton(Border group, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key,
        string label, string description, string buttonText)
    {
        var row = SettingsRow(label, description, out var controls);
        AddChangedHereIndicator(row, controls, api, device, values, key);
        var button = new Button { Content = buttonText };
        button.Click += async (_, _) =>
        {
            async Task<string?> LoadAppearanceAsync()
            {
                var response = await api.GetContractEffectiveSettingsAsync([key], device.DeviceId, device.ProfileId);
                return response.Settings.FirstOrDefault(setting => setting.Key == key)?.Value.GetRawText();
            }

            var dialog = new SubtitleAppearanceDialog
            {
                XamlRoot = XamlRoot,
                ApplyToLocalPlayer = device.IsCurrentDevice,
                CanReset = values.TryGetValue(key, out var entry)
                    && string.Equals(entry.Scope, "profile_device", StringComparison.OrdinalIgnoreCase),
                LoadOverrideAsync = LoadAppearanceAsync,
                SaveOverrideAsync = async json =>
                {
                    using var document = JsonDocument.Parse(json);
                    await api.SetContractSettingValueAsync(key, "profile_device", document.RootElement.Clone(),
                        device.DeviceId, device.ProfileId);
                },
                ResetOverrideAsync = () => api.DeleteContractSettingValueAsync(key, "profile_device",
                    device.DeviceId, device.ProfileId),
            };
            await dialog.ShowAsync();
            if (DevicesContentHost.Visibility == Visibility.Visible || DevicesPanel.Visibility == Visibility.Visible)
                await LoadDevicesAsync();
        };
        controls.Children.Add(button);
        ((StackPanel)group.Child).Children.Add(row);
    }

    private void AddChangedHereIndicator(Grid row, StackPanel controls, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key, bool retainedHere = false)
    {
        if (!values.TryGetValue(key, out var entry) || (!retainedHere && !string.Equals(entry.Scope, "profile_device", StringComparison.OrdinalIgnoreCase))) return;
        if (!DeviceSettingDisplay.CanWrite(entry)) return;

        var auth = App.Services.GetRequiredService<AuthService>();
        var ownerLabel = !string.IsNullOrWhiteSpace(device.ProfileId)
                         && !string.Equals(device.ProfileId, auth.SelectedProfileId, StringComparison.Ordinal)
                         && !string.IsNullOrWhiteSpace(device.ProfileName)
            ? $"{device.ProfileName}'s"
            : "your";
        var reset = new Button { Content = $"Use {ownerLabel} setting", FontSize = 12 };
        var resetContext = api.CaptureContext();
        var resetGeneration = _deviceDetailGeneration;
        reset.Click += async (_, _) =>
        {
            if (!reset.IsEnabled || resetGeneration != _deviceDetailGeneration || !api.IsCurrentContext(resetContext)) return;
            reset.IsEnabled = false;
            try
            {
                await api.DeleteContractSettingValueAsync(resetContext, key, "profile_device", device.DeviceId, device.ProfileId);
                if (resetGeneration != _deviceDetailGeneration || !api.IsCurrentContext(resetContext)) return;
                if (key == "ui.title_art") ViewModel.PublishTitleArtPreferenceChanged();
                Toast("Device override cleared");
                if (DevicesContentHost.Visibility == Visibility.Visible || DevicesPanel.Visibility == Visibility.Visible)
                    await LoadDevicesAsync();
            }
            catch (Exception ex) { Toast($"Could not reset setting: {ex.Message}", error: true); }
            finally { reset.IsEnabled = true; }
        };
        if (row.Children[0] is StackPanel copy) copy.Children.Add(reset);
        else controls.Children.Add(reset);
    }

    private static (string Value, string Label)[] DeviceLanguageOptions(string emptyLabel)
        => [("", emptyLabel), .. MediaLanguageCatalog.All.Select(language => (language.Code, language.Label))];

    private static Grid SettingsRow(string label, string description, out StackPanel controls)
    {
        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 12, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = PrimaryBrush() });
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(SecondaryText(description));
        row.Children.Add(copy);
        controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(controls, 1);
        row.Children.Add(controls);
        var controlHost = controls;
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.SizeChanged += (_, args) =>
        {
            var compact = row.Tag is "device-inline-row" ? false
                : args.NewSize.Width < (row.Tag is "device-value-row" ? 512 : 680);
            row.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
            Grid.SetRow(controlHost, compact ? 1 : 0);
            Grid.SetColumn(controlHost, compact ? 0 : 1);
            controlHost.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            controlHost.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        };
        return row;
    }

    private static Border SettingsSurface() => new()
    {
        Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(27.2),
        Padding = new Thickness(24, 20, 24, 20),
        Child = new StackPanel { Spacing = 8 },
    };

    private static Border CreateSettingsGroup(string title, string description)
    {
        var border = SettingsSurface();
        var stack = (StackPanel)border.Child;
        stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = PrimaryBrush() });
        stack.Children.Add(SecondaryText(description));
        return border;
    }

    private static TextBlock SecondaryText(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock ErrorText(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
        TextWrapping = TextWrapping.Wrap,
    };

    private static Brush PrimaryBrush() => (Brush)Application.Current.Resources["PrimaryTextBrush"];

    private static string DeviceSelectorLabel(UserDevice device)
        => $"{(string.IsNullOrWhiteSpace(device.DeviceName) ? "Unknown device" : device.DeviceName)}{(device.IsCurrentDevice ? " · using now" : "")}";

    private static string DeviceSummary(UserDevice device)
    {
        var changed = device.ChangedCount == 0
            ? "nothing changed here"
            : $"{device.ChangedCount} {(device.ChangedCount == 1 ? "thing" : "things")} set differently";
        return $"{device.DevicePlatform} · {(device.IsCurrentDevice ? "using now" : "last seen " + RelativeTime(device.LastSeenAt))} · {changed}";
    }

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;

    private static string RelativeTime(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var timestamp)) return "earlier";
        var age = DateTimeOffset.UtcNow - timestamp;
        if (age.TotalMinutes < 2) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} minutes ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} hours ago";
        return $"{(int)age.TotalDays} days ago";
    }

    private static string ReadDeviceScalar(IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key)
    {
        if (!values.TryGetValue(key, out var entry)) return "";
        return entry.Value.ValueKind switch
        {
            JsonValueKind.String => entry.Value.GetString() ?? "",
            JsonValueKind.Number => entry.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static bool ReadDeviceBool(IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key, bool fallback)
        => values.TryGetValue(key, out var entry) && entry.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? entry.Value.GetBoolean()
            : fallback;

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static void Toast(string message, bool error = false)
    {
        var toast = App.Services.GetRequiredService<ToastService>();
        if (error) toast.Error(message); else toast.Success(message);
    }
}
