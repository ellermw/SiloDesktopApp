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
    private static readonly string[] DeviceSettingKeys =
    [
        "playback.preferred_quality",
        "playback.max_bitrate_kbps",
        "playback.audio_language",
        "playback.subtitle_language",
        "playback.subtitle_mode",
        "playback.show_forced_subtitles",
        "playback.subtitle_appearance",
        "playback.auto_skip_intro",
        "playback.auto_skip_credits",
        "playback.auto_skip_recap",
        "playback.auto_play_next",
        "playback.auto_play_next_preview",
        "playback.next_up_prompt_seconds",
        "player.hdr_enabled",
        "player.dolby_vision_enabled",
        "player.dv_profile7_hdr10_fallback",
        "player.match_frame_rate",
        "player.video_gravity",
        "player.orientation_mode",
        "player.seek_cache_enabled",
        "player.audio_sync_ms",
        "player.playback_speed",
        "player.subtitle_sync_ms",
        "player.sleep_timer_default_minutes",
    ];

    private async Task LoadDevicesAsync()
    {
        DevicesLoadingRing.IsActive = true;
        DevicesContentHost.Children.Clear();
        try
        {
            var api = App.Services.GetRequiredService<SettingsApi>();
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
            var listHost = (StackPanel)listSurface.Child;
            var detailScroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var detailHost = new StackPanel { Spacing = 12 };
            detailScroller.Content = detailHost;
            browser.Children.Add(listSurface);
            Grid.SetColumn(detailScroller, 1);
            browser.Children.Add(detailScroller);
            DevicesContentHost.Children.Add(browser);

            List<UserDevice> devices = [];
            UserDevice? selected = null;
            var household = false;
            string? selectedProfileId = null;
            var suppressProfileFilter = false;

            async Task RenderListAsync(bool chooseDefault)
            {
                var query = search.Text?.Trim() ?? "";
                var visible = devices.Where(device =>
                    (string.IsNullOrWhiteSpace(selectedProfileId)
                     || string.Equals(device.ProfileId, selectedProfileId, StringComparison.Ordinal))
                    && (string.IsNullOrEmpty(query)
                        || DeviceSelectorLabel(device).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || device.DevicePlatform.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || device.ProfileName.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
                listHost.Children.Clear();
                search.PlaceholderText = $"Search {devices.Count} devices";
                if (visible.Count == 0)
                {
                    listHost.Children.Add(SecondaryText(devices.Count == 0
                        ? "No devices yet. They appear here as you sign in on them."
                        : "No devices match this search."));
                    detailHost.Children.Clear();
                    return;
                }

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
                        };
                        listHost.Children.Add(button);
                    }
                }

                var now = DateTimeOffset.UtcNow;
                AddGroup("Using now", visible.Where(device => device.IsCurrentDevice));
                AddGroup("This week", visible.Where(device => !device.IsCurrentDevice && now - ParseTimestamp(device.LastSeenAt) <= TimeSpan.FromDays(7)));
                AddGroup("Earlier", visible.Where(device => !device.IsCurrentDevice && now - ParseTimestamp(device.LastSeenAt) > TimeSpan.FromDays(7)));

                if (chooseDefault || selected is null || !visible.Any(device => device.DeviceId == selected.DeviceId && device.ProfileId == selected.ProfileId))
                {
                    selected = visible.FirstOrDefault(device => device.IsCurrentDevice) ?? visible[0];
                    await LoadDeviceDetailAsync(selected, detailHost);
                }
            }

            async Task LoadScopeAsync(bool useHousehold)
            {
                if (useHousehold && !_canManageProfiles)
                    return;
                household = useHousehold;
                mine.IsEnabled = useHousehold;
                everyone.IsEnabled = !useHousehold;
                try
                {
                    devices = (await api.GetUserDevicesAsync(useHousehold)).Devices
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
                                 .OrderBy(device => device.ProfileName, StringComparer.CurrentCultureIgnoreCase))
                    {
                        profileFilter.Items.Add(new ComboBoxItem
                        {
                            Content = string.IsNullOrWhiteSpace(profile.ProfileName) ? "Unknown profile" : profile.ProfileName,
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
                    everyone.IsEnabled = false;
                    Toast("Household devices are not available for this account", error: true);
                }
            }

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
            DevicesContentHost.Children.Add(ErrorText($"Devices could not be loaded: {ex.Message}"));
        }
        finally
        {
            DevicesLoadingRing.IsActive = false;
        }
    }

    private async Task LoadDeviceDetailAsync(UserDevice device, StackPanel host)
    {
        host.Children.Clear();
        var loading = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(loading);
        try
        {
            var api = App.Services.GetRequiredService<SettingsApi>();
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
            var effective = await api.GetContractEffectiveSettingsAsync(DeviceSettingKeys, device.DeviceId, device.ProfileId);
            var values = effective.Settings.ToDictionary(value => value.Key, StringComparer.Ordinal);

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
                var clear = new Button { Content = "Clear all changes" };
                clear.Click += async (_, _) =>
                {
                    var noun = device.ChangedCount == 1 ? "change" : "changes";
                    if (!await ConfirmAsync("Clear all changes?",
                            $"Clear all {device.ChangedCount} {noun} on {device.DeviceName}?")) return;
                    try
                    {
                        await api.ClearUserDeviceSettingsAsync(device.DeviceId, device.ProfileId);
                        Toast("Settings cleared on this device");
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

            var picture = CreateSettingsGroup("Picture", "How video looks on this device");
            AddDeviceSelect(picture, api, device, values, "playback.max_bitrate_kbps", "Maximum bitrate",
                "Cap how much bandwidth playback may use. No cap means Silo picks for the chosen resolution.",
                [("", "No limit"), ("1500", "1.5 Mbps"), ("2000", "2 Mbps"), ("4000", "4 Mbps"), ("6000", "6 Mbps"), ("10000", "10 Mbps"), ("15000", "15 Mbps"), ("20000", "20 Mbps"), ("40000", "40 Mbps"), ("60000", "60 Mbps"), ("100000", "100 Mbps"), ("200000", "200 Mbps")], numeric: true);
            AddDeviceSelect(picture, api, device, values, "playback.preferred_quality", "Preferred quality",
                "Pick the quality Silo should prefer.",
                [("auto", "Auto"), ("original", "Original quality"), ("2160p", "2160p / 4K"), ("1080p", "1080p"), ("720p", "720p"), ("480p", "480p")]);
            AddDeviceToggle(picture, api, device, values, "player.dolby_vision_enabled", "Dolby Vision", "Allow Dolby Vision output on this device.", true);
            AddDeviceToggle(picture, api, device, values, "player.dv_profile7_hdr10_fallback", "Dolby Vision Profile 7 fallback", "Play Profile 7 sources as HDR10 when this device cannot decode them natively.", false);
            AddDeviceToggle(picture, api, device, values, "player.hdr_enabled", "HDR", "Allow HDR output on this device.", true);
            AddDeviceToggle(picture, api, device, values, "player.match_frame_rate", "Match content frame rate", "Switch the display refresh rate to match what is playing.", false);
            AddDeviceSelect(picture, api, device, values, "player.orientation_mode", "Screen orientation",
                "Whether the player rotates with the device.", [("landscapeLocked", "Landscape"), ("rotateFreely", "Rotate freely")]);
            AddDeviceToggle(picture, api, device, values, "player.seek_cache_enabled", "Seek cache", "Keep recently played segments buffered for faster seeking.", true);
            AddDeviceSelect(picture, api, device, values, "player.video_gravity", "Video sizing",
                "How video fills the screen on this device.", [("fit", "Fit"), ("fill", "Fill"), ("stretch", "Stretch")]);
            host.Children.Add(picture);

            var sound = CreateSettingsGroup("Sound", "Audio on this device");
            AddDeviceSelect(sound, api, device, values, "playback.audio_language", "Preferred audio language",
                "Choose which spoken language Silo should prefer first.", DeviceLanguageOptions("No preference"));
            AddDeviceNumber(sound, api, device, values, "player.audio_sync_ms", "Audio sync offset", "Shift audio earlier or later to correct lip sync on this device.", -5000, 5000);
            AddDeviceDecimal(sound, api, device, values, "player.playback_speed", "Playback speed", "Default playback speed on this device.", 0.25, 3, 0.05, "x");
            host.Children.Add(sound);

            var subtitles = CreateSettingsGroup("Subtitles", "On this device");
            AddDeviceToggle(subtitles, api, device, values, "playback.show_forced_subtitles", "Show forced subtitles", "Show subtitles for foreign-language dialogue even when subtitles are off.", true);
            AddDevicePanelButton(subtitles, api, device, values, "playback.subtitle_appearance",
                "Subtitle appearance", "How subtitles are drawn during playback.", "Change how they look");
            AddDeviceSelect(subtitles, api, device, values, "playback.subtitle_language", "Preferred subtitle language",
                "Choose which subtitle language Silo should prefer first.", DeviceLanguageOptions("None"));
            AddDeviceSelect(subtitles, api, device, values, "playback.subtitle_mode", "Subtitles",
                "When Silo should turn subtitles on.", [("auto", "Auto"), ("always", "Always on"), ("off", "Off")]);
            AddDeviceNumber(subtitles, api, device, values, "player.subtitle_sync_ms", "Subtitle sync offset", "Shift subtitles earlier or later on this device.", -10000, 10000);
            host.Children.Add(subtitles);

            var episodes = CreateSettingsGroup("Episodes", "What happens between episodes");
            AddDeviceToggle(episodes, api, device, values, "playback.auto_play_next", "Auto-play next episode", "Continue to the next episode automatically.", true);
            AddDeviceToggle(episodes, api, device, values, "playback.auto_play_next_preview", "Preview next episode", "Show a preview of the next episode while credits play.", false);
            AddDeviceToggle(episodes, api, device, values, "playback.auto_skip_credits", "Auto-skip credits", "Move through end credits automatically when a skip is available.", false);
            AddDeviceToggle(episodes, api, device, values, "playback.auto_skip_intro", "Auto-skip intros", "Jump past intros automatically when Silo can detect them.", false);
            AddDeviceToggle(episodes, api, device, values, "playback.auto_skip_recap", "Auto-skip recaps", "Skip \"previously on\" recaps automatically when Silo can detect them.", false);
            AddDeviceNumber(episodes, api, device, values, "playback.next_up_prompt_seconds", "Next up prompt", "How long before the end of an episode the next-up prompt appears.", 0, 120, "Seconds");
            AddDeviceNumber(episodes, api, device, values, "player.sleep_timer_default_minutes", "Default sleep timer", "Duration the sleep timer starts on when you turn it on. 0 leaves it off.", 0, 240, "Minutes");
            host.Children.Add(episodes);

            if (forSomeoneElse)
            {
                host.Children.Add(InfoCallout(
                    "What you can't see here.",
                    "This page shows how Silo is set up on each device — not what anyone watched. Viewing history stays private to each profile."));
            }
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(ErrorText($"Device settings could not be loaded: {ex.Message}"));
        }
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
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key)
    {
        if (!values.TryGetValue(key, out var entry) || !string.Equals(entry.Scope, "profile_device", StringComparison.OrdinalIgnoreCase)) return;
        var labelHost = row.Children.OfType<StackPanel>().FirstOrDefault();
        if (labelHost?.Children.FirstOrDefault() is TextBlock label)
            label.Text += "  ·  CHANGED HERE";

        var auth = App.Services.GetRequiredService<AuthService>();
        var ownerLabel = !string.IsNullOrWhiteSpace(device.ProfileId)
                         && !string.Equals(device.ProfileId, auth.SelectedProfileId, StringComparison.Ordinal)
                         && !string.IsNullOrWhiteSpace(device.ProfileName)
            ? $"{device.ProfileName}'s"
            : "your";
        var reset = new Button { Content = $"Use {ownerLabel} setting", FontSize = 12 };
        reset.Click += async (_, _) =>
        {
            reset.IsEnabled = false;
            try
            {
                await api.DeleteContractSettingValueAsync(key, "profile_device", device.DeviceId, device.ProfileId);
                Toast("Device override cleared");
                if (DevicesContentHost.Visibility == Visibility.Visible || DevicesPanel.Visibility == Visibility.Visible)
                    await LoadDevicesAsync();
            }
            catch (Exception ex) { Toast($"Could not reset setting: {ex.Message}", error: true); }
            finally { reset.IsEnabled = true; }
        };
        controls.Children.Add(reset);
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
        return row;
    }

    private static Border SettingsSurface() => new()
    {
        Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(18),
        Child = new StackPanel { Spacing = 8 },
    };

    private static Border CreateSettingsGroup(string title, string description)
    {
        var border = SettingsSurface();
        var stack = (StackPanel)border.Child;
        stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = PrimaryBrush() });
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
