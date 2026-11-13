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
using SiloPlayer.Core.Localization;
using SiloPlayer.Helpers;

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
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(scopeButtons, L("web.settings.device.household.label", "Whose devices to show"));
            var mine = new Button { Content = L("web.settings.device.household.mine", "Just mine"), MinHeight = 42 };
            var everyone = new Button { Content = L("web.settings.device.household.everyone", "Everyone"), MinHeight = 42 };
            scopeButtons.Children.Add(mine);
            if (_canManageProfiles)
                scopeButtons.Children.Add(everyone);
            DevicesContentHost.Children.Add(scopeButtons);

            var search = new TextBox { PlaceholderText = L("web.settings.deviceList.searchLabel", "Search devices"), MinHeight = 42, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
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
                reveal.Content = showDormant ? L("web.settings.deviceList.hideUnused", "Hide inactive devices")
            : L("web.settings.deviceList.showUnused", $"Show {dormantCount} inactive devices", Args(("count", dormantCount), ("count_text", Count(dormantCount))));
                reveal.Visibility = dormantCount > 0 && string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
                search.PlaceholderText = L("web.settings.deviceList.searchPlaceholder", $"Search {devices.Count} devices", Args(("count", devices.Count), ("count_text", Count(devices.Count))));
                if (visible.Count == 0)
                {
                    listHost.Children.Add(SecondaryText(devices.Count == 0
                        ? L("web.settings.device.empty", "No devices yet. They appear here as you sign in on them.")
                        : L("web.settings.deviceList.emptySearch", "No devices match this search.")));
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
                        var changed = device.ChangedCount == 0 ? L("web.settings.deviceList.nothingChanged", "Nothing changed")
            : L("web.settings.deviceList.changedCount", $"{device.ChangedCount} setting{(device.ChangedCount == 1 ? "" : "s")} changed here", Args(("count", device.ChangedCount), ("count_text", Count(device.ChangedCount))));
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
                                    new TextBlock { Text = device.DisplayName + owner, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
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
                AddGroup(L("web.settings.deviceList.group.current", "Using now"), visible.Where(device => device.IsCurrentDevice));
                AddGroup(L("web.settings.deviceList.group.week", "This week"), visible.Where(device => !device.IsCurrentDevice && now - ParseTimestamp(device.LastSeenAt) <= TimeSpan.FromDays(7)));
                AddGroup(L("web.settings.deviceList.group.earlier", "Earlier"), visible.Where(device => !device.IsCurrentDevice && now - ParseTimestamp(device.LastSeenAt) > TimeSpan.FromDays(7)));
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
                    everyone.Content = useHousehold && devices.Count > 0
            ? L("web.settings.device.household.everyoneCount", $"Everyone ({devices.Count})", Args(("count", Count(devices.Count))))
            : L("web.settings.device.household.everyone", "Everyone");
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
            DevicesContentHost.Children.Add(ErrorText($"Devices could not be loaded: {ex.DisplayMessage()}"));
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
                    L("web.settings.device.capabilitiesFailed.title", "Couldn't check settings compatibility"),
                    L("web.settings.device.capabilitiesFailed.body", "Device controls stay unavailable until Silo confirms which settings this server supports."));
                var retry = new Button { Content = L("web.settings.device.capabilitiesFailed.retry", "Retry compatibility check"), HorizontalAlignment = HorizontalAlignment.Left };
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
                Text = device.DisplayName,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = PrimaryBrush(),
                TextWrapping = TextWrapping.Wrap,
            });
            if (!string.IsNullOrWhiteSpace(device.CustomName) && !string.Equals(device.CustomName, device.DeviceName, StringComparison.Ordinal))
                nameStack.Children.Add(SecondaryText(device.DeviceName));
            nameStack.Children.Add(SecondaryText(DeviceSummary(device)));
            heading.Children.Add(nameStack);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (capabilities.SupportsDeviceRename) actions.Children.Add(BuildDeviceRenameButton(api, device, summaryStack, context, generation));
            if (device.ChangedCount > 0)
            {
                var clear = new Button { Content = L("web.settings.device.useProfileSettings", "Use profile settings") };
                clear.Click += async (_, _) =>
                {
                    var noun = device.ChangedCount == 1 ? "setting" : "settings";
                    if (!await ConfirmAsync("Use profile settings?",
                            $"Use {ownerLabel} profile settings on {device.DisplayName}? This removes the {device.ChangedCount} {noun} changed on this device. Settings that only apply to devices go back to the app default.")) return;
                    try
                    {
                        await api.ClearUserDeviceSettingsAsync(device.DeviceId, device.ProfileId);
                        Toast(L("web.settings.device.resetDone", "Removed the changes on this device"));
                        await LoadDevicesAsync();
                    }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<ToastService>().Error(ex);
                    }
                };
                actions.Children.Add(clear);
            }
            if (!device.IsCurrentDevice)
            {
                var forget = new Button { Content = L("web.settings.device.forget", "Forget") };
                forget.Click += async (_, _) =>
                {
                    if (!await ConfirmAsync("Forget device?",
                            L("web.settings.device.forgetConfirm", $"Forget {device.DisplayName}? Its settings are removed and it disappears from this list until it's used again.", Args(("device", device.DisplayName))))) return;
                    try
                    {
                        await api.ForgetUserDeviceAsync(device.DeviceId, device.ProfileId);
                        Toast(L("web.settings.device.forgotten", "Device forgotten"));
                        await LoadDevicesAsync();
                    }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<ToastService>().Error(ex);
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

            // Group names are the definitions' stable English group ids; only the headings translate.
            foreach (var groupName in new[] { "Picture", "Sound", "Subtitles", "Episodes" })
            {
                var visible = definitions.Where(definition => definition.Group == groupName
                    && values.TryGetValue(definition.Key, out var entry)
                    && (definition.AppliesTo(device.DevicePlatform) || entry.Scope == "profile_device")).ToList();
                if (visible.Count == 0) continue;
                var groupHeading = groupName switch { "Picture" => L("web.settings.deviceGroups.picture.title", "Picture"), "Sound" => L("web.settings.deviceGroups.sound.title", "Sound"), "Subtitles" => L("web.settings.deviceGroups.subtitles.title", "Subtitles"), _ => L("web.settings.deviceGroups.episodes.title", "Episodes") };
                var group = CreateSettingsGroup(groupHeading, groupName switch { "Picture" => L("web.settings.deviceGroups.picture.description", "How video looks on this device"), "Sound" => L("web.settings.deviceGroups.sound.description", "Audio on this device"), "Subtitles" => L("web.settings.deviceGroups.subtitles.description", "On this device"), _ => L("web.settings.deviceGroups.episodes.description", "What happens between episodes") });
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
            host.Children.Add(ErrorText($"Device settings could not be loaded: {ex.DisplayMessage()}"));
        }
    }

    private Border BuildProfileLaunchGroup()
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        var group = CreateSettingsGroup("This app", "Applies only to this app. Your other devices keep their own choice.");
        var content = (StackPanel)group.Child;
        content.Children.Add(new TextBlock { Text = L("web.settings.profileLaunch.label", "Profile at launch"), FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        var choices = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var hint = SecondaryText("");
        void Refresh()
        {
            var ask = settings.Load().ProfileLaunchMode == "ask";
            hint.Text = ask ? "Each new launch starts at “Who's watching?”, and a PIN-protected profile needs its PIN again."
                : "New launches open the profile last used here.";
        }
        foreach (var (mode, label) in new[] { ("remember", L("web.settings.profileLaunch.remember", "Remember last profile")), ("ask", L("web.settings.profileLaunch.ask", "Ask who's watching")) })
        {
            var button = new RadioButton { Content = label, GroupName = "ProfileLaunch", IsChecked = settings.Load().ProfileLaunchMode == mode };
            button.Checked += (_, _) => { var local = settings.Load(); local.ProfileLaunchMode = mode; settings.Save(local); Refresh(); };
            choices.Children.Add(button);
        }
        content.Children.Add(choices); content.Children.Add(hint); Refresh();
        return group;
    }

    private CancellationTokenSource? _connectAppsCancellation;
    private bool _connectAppsLifetimeAttached;
    private async Task LoadConnectAppsAsync()
    {
        if (!_connectAppsLifetimeAttached)
        {
            _connectAppsLifetimeAttached = true;
            Unloaded += (_, _) => { _connectAppsCancellation?.Cancel(); _connectAppsCancellation?.Dispose(); _connectAppsCancellation = null; ConnectAppsContentHost.Children.Clear(); };
        }
        _connectAppsCancellation?.Cancel();
        _connectAppsCancellation?.Dispose();
        var owner = new CancellationTokenSource(); _connectAppsCancellation = owner;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        var currentAuth = App.Services.GetRequiredService<AuthService>();
        var session = currentAuth.SessionGeneration;
        bool Current() => ReferenceEquals(owner, _connectAppsCancellation) && !owner.IsCancellationRequested && client.IsCurrentContext(context) && currentAuth.SessionGeneration == session;
        ConnectAppsLoadingRing.IsActive = true;
        ConnectAppsContentHost.Children.Clear();
        try
        {
            var api = App.Services.GetRequiredService<SettingsApi>();
            var auth = App.Services.GetRequiredService<AuthService>();
            var compatTask = api.GetCompatConnectInfoAsync(owner.Token);
            var profilesTask = api.GetProfilesAsync(owner.Token);
            await Task.WhenAll(compatTask, profilesTask);
            if (!Current()) return;

            AuthProvidersResponse? discovery = null;
            AccountIdentityCollection? identities = null;
            var signInFailed = false;
            try
            {
                discovery = await client.GetUnauthenticatedAsync<AuthProvidersResponse>("/api/v2/auth/providers", owner.Token);
                if (!Current()) return;
                if (!(compatTask.Result.Account.PasswordLoginAvailable != false && discovery.PasswordLogin))
                    identities = await App.Services.GetRequiredService<AuthApi>().GetAccountIdentitiesAsync(context, owner.Token);
            }
            catch (OperationCanceledException) { throw; }
            catch { signInFailed = true; }
            if (!Current()) return;

            var compat = compatTask.Result;
            var profiles = profilesTask.Result.Profiles;
            Profile? selectedProfile = profiles.FirstOrDefault(profile => profile.Id == auth.SelectedProfileId)
                ?? profiles.FirstOrDefault();
            var accountName = auth.CurrentUser?.Username ?? "";

            var picker = new Grid { ColumnSpacing = 8, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, L("web.settings.connectApps.appTypeLabel", "App type"));
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var siloButton = new Button { Content = L("web.settings.connectApps.appType.silo", "Silo app or website"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 46 };
            var jellyfinButton = new Button { Content = L("web.settings.connectApps.appType.jellyfin", "Jellyfin-compatible app"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 46 };
            picker.Children.Add(siloButton);
            Grid.SetColumn(jellyfinButton, 1);
            picker.Children.Add(jellyfinButton);
            var details = new StackPanel { Spacing = 12 };
            ConnectAppsContentHost.Children.Add(picker);
            ConnectAppsContentHost.Children.Add(details);

            void ShowSilo()
            {
                if (!Current()) return;
                details.Children.Clear();
                details.Children.Add(InfoCallout(L("web.settings.connectApps.scope.siloTitle", "For Silo's own apps"),
                    "Silo for iPhone, Apple TV, Android, Android TV, and this Windows app. Don't add a # to either field here."));
                if (signInFailed || discovery == null)
                {
                    details.Children.Add(InfoCallout(L("web.settings.connectApps.loadFailed.title", "Couldn't load your sign-in details"), "Reload this page to load the server's sign-in options.")); return;
                }
                var choice = ConnectAppsSignIn.Resolve(compat.Account.PasswordLoginAvailable != false, discovery.PasswordLogin, discovery.Providers, identities?.Items ?? []);
                var card = CreateSettingsGroup("Sign-in details", "Use these values only on a Silo sign-in screen.");
                AddCredentialRow(card, L("web.settings.connectApps.field.server", "Server"), auth.ConfiguredServerUrl, "The address of this Silo server.");
                if (choice.Kind is "password" or "directory")
                {
                    AddCredentialRow(card, L("web.settings.connectApps.field.username", "Username"), choice.Kind == "directory" && !string.IsNullOrWhiteSpace(choice.Username) ? choice.Username : accountName,
                        choice.Kind == "directory" ? L("web.settings.connectApps.username.directoryHint", $"Your {choice.Provider!.DisplayName} username.", Args(("provider", choice.Provider!.DisplayName))) : L("web.settings.connectApps.username.siloHint", "Just your account name."));
                    AddCredentialRow(card, L("web.settings.connectApps.field.password", "Password"), L("web.settings.connectApps.password.placeholder", "your password"), choice.Kind == "directory"
                        ? $"Your {choice.Provider!.DisplayName} password. The profile PIN is asked for separately, in the app."
                        : L("web.settings.connectApps.password.siloHint", "Your account password. The profile PIN is asked for separately, in the app."), canCopy: false);
                }
                else if (choice.Kind == "provider")
                {
                    AddCredentialRow(card, L("web.settings.connectApps.field.signInWith", "Sign in with"), choice.Provider!.DisplayName, string.IsNullOrEmpty(choice.Provider.NativeStartPath)
                        ? "Sign-in cannot start until an administrator sets this server's public address."
                        : $"Choose {choice.Provider.DisplayName} on the sign-in screen and sign in there.", canCopy: false);
                    details.Children.Add(InfoCallout("Signing in on a TV", $"Scan the code shown by your TV with your phone, or enter it at {auth.ConfiguredServerUrl.TrimEnd('/')}/activate and approve the TV there."));
                }
                else if (choice.Kind == "network")
                    AddCredentialRow(card, L("web.settings.connectApps.field.signInWith", "Sign in with"), choice.ProviderName, $"On a device connected to {choice.ProviderName}, open this server through that network and choose Continue as on the sign-in screen.", canCopy: false);
                else
                    details.Children.Add(InfoCallout("This account has no available Silo app sign-in", compat.Account.PasswordLoginAvailable == true
                        ? $"Password sign-in is off. This recovery account can still use {auth.ConfiguredServerUrl.TrimEnd('/')}/login?local=1 in a browser, then approve a TV code at /activate."
                        : "Ask an administrator to connect an enabled sign-in provider or allow password sign-in for this account."));
                details.Children.Add(card);
                details.Children.Add(InfoCallout("Profiles come next",
                    L("web.settings.connectApps.profilePickerNote", "After signing in you'll choose a profile from the profile picker, and PIN-protected profiles prompt for their PIN there.")));
            }

            void ShowJellyfin()
            {
                if (!Current()) return;
                details.Children.Clear();
                details.Children.Add(InfoCallout(L("web.settings.connectApps.scope.jellyfinTitle", "For Jellyfin-compatible apps only"),
                    L("web.settings.connectApps.scope.jellyfinBody", "Infuse, Swiftfin, JellyCon, Findroid, Jellyfin Media Player. These credentials will not work on a Silo sign-in screen.")));

                if (compat.Account.PasswordLoginAvailable == false)
                {
                    details.Children.Add(InfoCallout(L("web.settings.connectApps.noJellyfinSignIn.title", "This account can't sign in to a Jellyfin app"),
                        "Password sign-in is unavailable for this account or turned off on this server. The compatibility API only accepts Silo passwords. Use a Silo app, or ask an administrator about password sign-in."));
                    return;
                }
                if (!compat.Jellyfin.Enabled)
                {
                    details.Children.Add(InfoCallout(
                        compat.Jellyfin.PendingRestart
                            ? L("web.settings.connectApps.compatOff.pendingRestartTitle", "The Jellyfin compatibility API isn't running yet")
                            : L("web.settings.connectApps.compatOff.title", "The Jellyfin compatibility API is turned off"),
                        compat.Jellyfin.PendingRestart
                            ? L("web.settings.connectApps.compatOff.pendingRestartBody", "An administrator has turned it on, but the server has to restart before it starts accepting connections.")
                            : L("web.settings.connectApps.compatOff.body", "Third-party Jellyfin apps can't reach this server until an administrator enables it in Admin → Settings → Compatibility.")));
                    return;
                }

                var profileBlock = new StackPanel { Spacing = 8 };
                profileBlock.Children.Add(new TextBlock
                {
                    Text = AppStrings.Uppercase(L("web.settings.connectApps.profilePicker.title", "Which profile are you signing in as?")),
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
                    if (!Current()) return;
                    credentialHost.Children.Clear();
                    var profileName = selectedProfile?.Name?.Trim() ?? "";
                    var profileNameIssue = profileName.Contains('#')
                        ? L("web.settings.connectApps.usernameIssue", "This profile's name contains a #, which Jellyfin apps can't sign in with. Rename it in Settings → Profiles to use it from one.")
                        : null;
                    var username = string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(profileName)
                        ? accountName
                        : $"{accountName}#{profileName}";
                    var loopback = IsLoopbackUrl(compat.Jellyfin.PublicUrl);
                    var card = CreateSettingsGroup("", "");
                    var stack = (StackPanel)card.Child;
                    stack.Children.Clear();
                    AddCredentialRow(card, AppStrings.Uppercase(L("web.settings.connectApps.field.server", "Server")), compat.Jellyfin.PublicUrl,
                        loopback
                            ? L("web.settings.connectApps.server.loopbackHint", "This address only works on the server itself, so phones and TVs can't reach it. An administrator needs to set the compatibility API's public address.")
                            : L("web.settings.connectApps.server.compatHint", "The compatibility API listens on its own address — not the one this page is on."),
                        canCopy: !loopback);
                    AddCredentialRow(card, AppStrings.Uppercase(L("web.settings.connectApps.field.username", "Username")), username,
                        profileNameIssue ?? $"Your account name, then #, then the profile name — not just “{accountName}”.",
                        canCopy: profileNameIssue == null);
                    AddCredentialRow(card, AppStrings.Uppercase(L("web.settings.connectApps.field.password", "Password")), L("web.settings.connectApps.password.placeholder", "your password"),
                        selectedProfile?.HasPin == true
                            ? L("web.settings.connectApps.passwordHint.pin", $"{profileName} has a PIN, so append # and the PIN to your account password.", Args(("name", profileName)))
                            : L("web.settings.connectApps.passwordHint.noPin", $"{profileName} has no PIN — just your account password, nothing appended.", Args(("name", profileName))),
                        canCopy: false);
                    stack.Children.Add(SecondaryText(
                        L("web.settings.connectApps.jellyfinFormatNote", "Jellyfin apps offer only two boxes and never prompt for a profile, so the profile name and PIN are appended here. This format is rejected on a Silo sign-in screen.")));
                    credentialHost.Children.Add(card);

                    var faq = new Expander
                    {
                        Header = L("web.settings.connectApps.troubleshooting.title", "A Jellyfin app says my username or password is wrong"),
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
                        var everyProfile = CreateSettingsGroup(L("web.settings.connectApps.glance.title", "Every profile at a glance"), "");
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!Current()) return;
            ConnectAppsContentHost.Children.Clear();
            ConnectAppsContentHost.Children.Add(InfoCallout(
                L("web.settings.connectApps.loadFailed.title", "Couldn't load your sign-in details"),
                L("web.settings.connectApps.loadFailed.body", "Reload the page to try again. Credentials are withheld rather than guessed, so nothing here is stale or wrong.")));
            System.Diagnostics.Debug.WriteLine($"Connect Apps load failed: {ex}");
        }
        finally
        {
            if (Current()) ConnectAppsLoadingRing.IsActive = false;
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
            var copy = new Button { Content = L("web.settings.webhooks.detail.copy", "Copy"), MinWidth = 64 };
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(value);
                Clipboard.SetContent(package);
                Toast(L("web.settings.webhooks.copied", "Copied to clipboard"));
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
            if (changed) copy.Children.Add(SecondaryText(L("web.settings.deviceSettingGroups.changedHere", "Changed here")));
            if (entry.Constrained)
            {
                copy.Children.Add(SecondaryText(L("web.settings.deviceSettingGroups.householdLimit", "Household limit")));
                copy.Children.Add(SecondaryText(DeviceSettingDisplay.ConstraintExplanation(entry)));
            }
            else if (!changed && !profileWide && DeviceSettingDisplay.InheritedSource(entry, ownerLabel) is { } inherited)
                copy.Children.Add(SecondaryText(inherited));
            if (profileWide)
            {
                var choice = retainedHere ? DeviceSettingDefinition.Scalar(retainedOnDevice[definition.Key]) switch
                    { "true" => L("web.settings.library.on", "On"), "false" => L("web.settings.library.off", "Off"), var other => other } : "";
                copy.Children.Add(SecondaryText((retainedHere
                    ? L("web.settings.deviceSettingGroups.profileWideRetained", $"Set for all devices on this profile, so this device's own choice ({choice}) isn't used right now. Turn off “Apply to all devices” to choose per device.", Args(("value", choice)))
                    : L("web.settings.deviceSettingGroups.profileWide", "Set for all devices on this profile. Turn off “Apply to all devices” to choose per device."))));
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
            catch (Exception ex) { Toast($"Could not save {definition.Label}: {ex.DisplayMessage()}", error: true); }
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
                var options = definition.Type == "language_tag" ? DeviceLanguageOptions(definition.Key.Contains("subtitle") ? L("settingsManifest.playback.subtitle_language.unsetLabel", "None") : L("settingsManifest.playback.audio_language.unsetLabel", "No preference")) : definition.Options;
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
                AddDevicePanelButton(group, api, device, values, definition.Key, definition.Label, definition.Description, L("web.settings.deviceSettingGroups.changeAppearance", "Change how they look"));
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

    private void AddDevicePanelButton(Border group, SettingsApi api, UserDevice device,
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key,
        string label, string description, string buttonText)
    {
        var row = SettingsRow(label, description, out var controls);
        AddChangedHereIndicator(row, controls, api, device, values, key);
        var button = new Button { Content = buttonText };
        button.Click += async (_, _) =>
        {
            var appearanceContext = api.CaptureContext();
            async Task<string?> LoadAppearanceAsync()
            {
                var response = await api.GetContractEffectiveSettingsAsync(appearanceContext, [key], device.DeviceId, device.ProfileId);
                return response.Settings.FirstOrDefault(setting => setting.Key == key)?.Value.GetRawText();
            }

            var dialog = new SubtitleAppearanceDialog
            {
                XamlRoot = XamlRoot,
                ApplyToLocalPlayer = device.IsCurrentDevice && (string.IsNullOrEmpty(device.ProfileId) || device.ProfileId == appearanceContext.ProfileId),
                CanReset = values.TryGetValue(key, out var entry)
                    && string.Equals(entry.Scope, "profile_device", StringComparison.OrdinalIgnoreCase),
                LoadOverrideAsync = LoadAppearanceAsync,
                SaveOverrideAsync = async json =>
                {
                    using var document = JsonDocument.Parse(json);
                    await api.SetContractSettingValueAsync(appearanceContext, key, "profile_device", document.RootElement.Clone(),
                        device.DeviceId, device.ProfileId);
                },
                ResetOverrideAsync = () => api.DeleteContractSettingValueAsync(appearanceContext, key, "profile_device",
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
            catch (Exception ex) { Toast($"Could not reset setting: {ex.DisplayMessage()}", error: true); }
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

    private Button BuildDeviceRenameButton(SettingsApi api, UserDevice device, StackPanel summary,
        ApiRequestContext context, int generation)
    {
        var rename = new Button { Content = L("web.settings.device.rename", "Rename") };
        rename.Click += (_, _) =>
        {
            if (generation != _deviceDetailGeneration || !api.IsCurrentContext(context)) return;
            rename.IsEnabled = false;
            var editor = new StackPanel { Spacing = 10 };
            var input = new TextBox { Header = L("web.settings.device.nameLabel", "Device name"), Text = device.CustomName ?? device.DeviceName, MaxLength = 240,
                HorizontalAlignment = HorizontalAlignment.Stretch };
            var error = ErrorText("");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var save = new Button { Content = L("common.save", "Save") };
            var cancel = new Button { Content = L("common.cancel", "Cancel") };
            var reset = new Button { Content = "Use reported name", Visibility = string.IsNullOrWhiteSpace(device.CustomName) ? Visibility.Collapsed : Visibility.Visible };
            buttons.Children.Add(save); buttons.Children.Add(cancel); buttons.Children.Add(reset);
            editor.Children.Add(input); editor.Children.Add(error); editor.Children.Add(buttons);
            summary.Children.Add(editor);
            cancel.Click += (_, _) => { summary.Children.Remove(editor); rename.IsEnabled = true; };
            async Task ApplyAsync(string? value)
            {
                if (!save.IsEnabled || generation != _deviceDetailGeneration || !api.IsCurrentContext(context)) return;
                save.IsEnabled = cancel.IsEnabled = reset.IsEnabled = input.IsEnabled = false;
                error.Text = "";
                try
                {
                    await api.RenameUserDeviceAsync(context, device.DeviceId, value, device.ProfileId);
                    if (generation != _deviceDetailGeneration || !api.IsCurrentContext(context)) return;
                    Toast(value == null ? L("web.settings.device.nameReset", "Device name reset") : L("web.settings.device.renamed", "Device renamed"));
                    await LoadDevicesAsync();
                }
                catch (Exception ex)
                {
                    if (generation == _deviceDetailGeneration && api.IsCurrentContext(context)) error.Text = ex.DisplayMessage();
                }
                finally { save.IsEnabled = cancel.IsEnabled = reset.IsEnabled = input.IsEnabled = true; }
            }
            save.Click += async (_, _) => await ApplyAsync(input.Text.Trim());
            reset.Click += async (_, _) => await ApplyAsync(null);
            input.Focus(FocusState.Programmatic); input.SelectAll();
        };
        return rename;
    }

    private static string DeviceSelectorLabel(UserDevice device)
        => $"{device.DisplayName}{(device.IsCurrentDevice ? " · using now" : "")}";

    private static string DeviceSummary(UserDevice device)
    {
        var changed = device.ChangedCount == 0
            ? L("web.settings.device.nothingChanged", "nothing changed here")
            : L("web.settings.device.changedCount", $"{device.ChangedCount} {(device.ChangedCount == 1 ? "thing" : "things")} set differently", Args(("count", device.ChangedCount), ("count_text", Count(device.ChangedCount))));
        return $"{device.DevicePlatform} · {(device.IsCurrentDevice ? "using now" : "last seen " + RelativeTime(device.LastSeenAt))} · {changed}";
    }

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;

    private static string RelativeTime(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var timestamp)) return "earlier";
        var age = DateTimeOffset.UtcNow - timestamp;
        if (age.TotalMinutes < 2) return L("web.common.date.justNow", "just now");
        if (age.TotalHours < 1) return L("web.settings.deviceList.lastSeen.underAnHour", $"{(int)age.TotalMinutes} minutes ago");
        if (age.TotalDays < 1) return L("web.settings.deviceList.lastSeen.hours", $"{(int)age.TotalHours} hours ago", Args(("count", (int)age.TotalHours), ("count_text", Count((int)age.TotalHours))));
        return L("web.settings.deviceList.lastSeen.days", $"{(int)age.TotalDays} days ago", Args(("count", (int)age.TotalDays), ("count_text", Count((int)age.TotalDays))));
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = L("web.ui.continue", "Continue"),
            CloseButtonText = L("common.cancel", "Cancel"),
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
