using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

/// <summary>
/// Task5 settings/account/profile/editor localization through actual controls:
/// the Settings directory, sidebar entries and search; tab switching and a
/// retained tab refreshed while loaded; the real profile editor dialog in Arabic;
/// Webhook Sync and the metadata editor's labels. Synthetic wire only.
/// </summary>
internal static partial class SettingsLocalizationNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Host = "settings-localization.fixture.invalid";

    internal static async Task RunAsync(StackPanel parent)
    {
        var locales = LocaleService.Shared;
        var before = locales.Current;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://" + Host);
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "admin" });
        auth.SelectProfile("primary", profile: new() { Id = "primary", Name = "Primary", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "settings-localization"));
        var prefs = local.Load(); prefs.SiloCastEnabled = false; local.Save(prefs);
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(http).AddSingleton(auth).AddSingleton(local)
            .AddSingleton<PeopleApi>().AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<WebhookSyncApi>().AddSingleton<MediaMaintenanceApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>()
            .AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>().AddSingleton<ProfileSelectViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>().AddSingleton<NotificationSettingsViewModel>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "settings-localization-images")))
            .AddSingleton<ItemDetailPrefetchCache>()
            .AddSingleton(sp => new AppLanguagePreference(sp.GetRequiredService<SettingsApi>()))
            .AddSingleton(sp => new AppLocaleCoordinator(sp.GetRequiredService<AppLanguagePreference>(), LocaleService.Shared,
                sp.GetRequiredService<SettingsService>(), () => ["en-US"], "en-US", AppLocalization.PostTo(parent.DispatcherQueue)))
            .BuildServiceProvider();
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = slot.GetValue(null); slot.SetValue(null, services);
        try
        {
            Apply("en");
            var only = Environment.GetEnvironmentVariable("SILO_SETTINGS_LOCALIZATION_CASE");
            if (only is null or "review-followups") await ReviewFollowups(parent, client);
            if (only is null or "directory") await DirectoryCase(parent);
            if (only is null or "tabs") await Tabs(parent);
            if (only is null or "toggle-rows") await ToggleRows(parent);
            if (only is null or "profile-editor") await ProfileEditor(parent, client);
            if (only is null or "household") await Household(parent);
            if (only is null or "metadata") await Metadata(parent);
            if (only is null or "language-picker") { await RegionalLanguagePicker(parent); await LanguagePicker(parent); }
        }
        finally
        {
            slot.SetValue(null, previous);
            locales.Apply(locales.Create(before.Resolution));
        }
    }

    private static async Task DirectoryCase(StackPanel parent)
    {
        var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(100);
        try
        {
            Invoke(page, "ShowSettingsOverview"); await Settle(page);
            Apply("de"); await Settle(page);
            MediaLocalizationNativeFixture.CheckInlineChoiceLabels(page, "InterfacePosterSizeCombo", "QualityComboBox", "MaxBitrateComboBox", "SubtitleModeComboBox", "NextUpModeComboBox");
            var playbackTitle = AppStrings.Get("web.settings.playback.title");
            var yourDataTitle = AppStrings.Get("web.settings.yourData.title");
            Check(playbackTitle != "Playback" && yourDataTitle != "Your Data", "German catalog lacks the asserted settings titles.");
            var playbackCard = OverviewCard(page, "Playback");
            Check(CardLabel(playbackCard) == playbackTitle, "Directory card stayed English after language change: " + CardLabel(playbackCard));
            Check(AutomationProperties.GetName(playbackCard).StartsWith(playbackTitle, StringComparison.Ordinal), "Directory card accessible name stayed English.");
            Check(Equals(playbackCard.Tag, "Playback"), "Directory card routing tag changed.");
            var groupHeading = Descendants(page.FindName("SettingsOverviewGroups") as DependencyObject ?? page).OfType<TextBlock>()
                .Select(text => text.Text).FirstOrDefault();
            Check(string.Equals(groupHeading, AppStrings.Get("web.settings.nav.group.playback"), StringComparison.CurrentCultureIgnoreCase),
                "Directory group heading stayed English: " + groupHeading);
            var entry = page.SidebarEntries.Single(item => item.Section == "Playback");
            Check(entry.Label == playbackTitle && entry.Group == AppStrings.Get("web.settings.nav.group.playback"), "Shell sidebar entry label/group stayed English.");
            Check(page.SidebarEntries.Any(item => item.Section == "YourData" && item.Label == yourDataTitle), "Your Data sidebar label stayed English.");

            var search = (TextBox)page.FindName("SettingsSearchBox");
            search.Text = yourDataTitle; await Settle(page);
            Check(OverviewCard(page, "YourData").Visibility == Visibility.Visible && playbackCard.Visibility == Visibility.Collapsed,
                "Localized label search did not find the right directory card.");
            var status = ((TextBlock)page.FindName("SettingsSearchStatus")).Text;
            Check(status == AppStrings.Get("web.settings.search.matchCount", new Dictionary<string, object?> { ["count"] = 1, ["count_text"] = "1" }),
                "Search match count stayed English: " + status);
            search.Text = "export"; await Settle(page);
            Check(OverviewCard(page, "YourData").Visibility == Visibility.Visible, "English search terms no longer find Your Data.");
            search.Text = "zzzz-no-setting"; await Settle(page);
            Check(((TextBlock)page.FindName("SettingsSearchStatus")).Text == AppStrings.Get("web.settings.search.noMatches"), "No-match status stayed English.");
            search.Text = "";
            Check(search.PlaceholderText == AppStrings.Get("web.settings.search.placeholder"), "Search placeholder stayed English.");

            Apply("ar"); await Settle(page);
            playbackCard = OverviewCard(page, "Playback");
            Check(CardLabel(playbackCard) == AppStrings.Get("web.settings.playback.title"), "Loaded directory did not refresh to Arabic.");
            Check(page.SidebarEntries.Single(item => item.Section == "Playback").Label == AppStrings.Get("web.settings.playback.title"), "Sidebar entries did not refresh to Arabic.");
        }
        finally { parent.Children.Remove(page); Apply("en"); await Task.Delay(80); }
        Program.Log("PASS settings directory, sidebar entries and search localize with stable routing and English terms.");
    }

    private static async Task Tabs(StackPanel parent)
    {
        var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(100);
        try
        {
            page.SelectSettingsSection("Notifications");
            await Settle(page);
            Check(((ContentControl)page.FindName("NotificationsSettingsHost")).Content is SiloPlayer.Controls.NotificationSettingsControl, "Actual Settings Notifications panel did not construct");
            Apply("de"); await Settle(page);
            MediaLocalizationNativeFixture.CheckInlineChoiceLabels(page, "InterfacePosterSizeCombo", "QualityComboBox", "MaxBitrateComboBox", "SubtitleModeComboBox", "NextUpModeComboBox");
            page.SelectSettingsSection("Playback"); await Settle(page);
            var playbackNav = Descendants((Button)page.FindName("PlaybackTab")).OfType<TextBlock>().Last(text => text.Parent is StackPanel);
            Check(playbackNav.Text == AppStrings.Get("web.settings.playback.title"), "Sidebar tab text stayed English: " + playbackNav.Text);
            RequireText(page.FindName("PlaybackPanel") as DependencyObject ?? page, "web.settings.playback.quality.title", "Quality");

            page.SelectSettingsSection("Account"); await Settle(page);
            var accountPanel = (DependencyObject)page.FindName("AccountPasswordPanel");
            var password = RequireText(accountPanel, "web.settings.account.password.title", "Password");
            var change = Descendants(accountPanel).OfType<Button>().FirstOrDefault(button => Equals(button.Content, AppStrings.Get("web.settings.account.password.change")));
            Check(change != null, "Change password button stayed English.");

            Apply("ar"); await Settle(page);
            Check(password.Text == AppStrings.Get("web.settings.account.password.title"), "Retained Account tab did not refresh to Arabic: " + password.Text);
            Check(playbackNav.Text == AppStrings.Get("web.settings.playback.title"), "Retained sidebar tab did not refresh to Arabic.");
            Check(Equals(((Button)page.FindName("AccountPasswordTab")).Tag, "Account"), "Tab routing tag changed.");
            page.SelectSettingsSection("YourData"); await Settle(page);
            RequireText((DependencyObject)page.FindName("YourDataPanel"), "web.settings.yourData.title", "Your Data");
        }
        finally { parent.Children.Remove(page); Apply("en"); await Task.Delay(80); }
        Program.Log("PASS settings tab switching shows keyed tab content and retained tabs refresh while loaded.");
    }

    private static async Task ProfileEditor(StackPanel parent, SiloApiClient client)
    {
        var ctor = typeof(ProfileEditorDialog).GetConstructors(Private).Single();
        var profile = new Profile { Id = "secondary", Name = "مستخدم Alex", HasPin = true };
        var editor = ctor.Invoke([parent.XamlRoot, profile, Array.Empty<Library>(), false, new AuthApi(client), true, true, false]);
        var dialog = (ContentDialog)editor.GetType().GetField("_dialog", Private)!.GetValue(editor)!;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Opened += (_, _) => opened.TrySetResult();
        Apply("ar");
        var shown = dialog.ShowAsync();
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Settle(dialog);
            Check(dialog.FlowDirection == FlowDirection.RightToLeft && dialog.Language == "ar", "Profile editor did not open right-to-left in Arabic.");
            var heading = (StackPanel)dialog.Title;
            var title = (TextBlock)heading.Children[0];
            Check(title.Text == AppStrings.Get("web.profiles.editor.titleEdit"), "Profile editor title stayed English: " + title.Text);
            Check(dialog.PrimaryButtonText == AppStrings.Get("web.profiles.editor.save") && dialog.CloseButtonText == AppStrings.Get("common.cancel"),
                "Profile editor actions stayed English.");
            var nameBox = (TextBox)editor.GetType().GetField("_nameBox", Private)!.GetValue(editor)!;
            Check(nameBox.Text == "مستخدم Alex", "Profile editor translated or altered the saved name.");
            var pinLabel = (TextBlock)editor.GetType().GetField("_pinLabel", Private)!.GetValue(editor)!;
            Check(pinLabel.Text == AppStrings.Get("web.profiles.editor.newPin"), "PIN label stayed English: " + pinLabel.Text);
            RequireText((DependencyObject)dialog.Content, "web.profiles.editor.name", "Name");
            RequireText((DependencyObject)dialog.Content, "web.profiles.editor.accessSection", "Access");

            Apply("de"); await Settle(dialog);
            Check(dialog.FlowDirection == FlowDirection.LeftToRight, "Open profile editor kept Arabic direction after switching to German.");
            Check(title.Text == AppStrings.Get("web.profiles.editor.titleEdit"), "Open profile editor title did not refresh to German: " + title.Text);
            Check(pinLabel.Text == AppStrings.Get("web.profiles.editor.newPin"), "Open profile editor PIN label did not refresh to German: " + pinLabel.Text);
            RequireText((DependencyObject)dialog.Content, "web.profiles.editor.maxContentRating", "Maximum content rating");
            var rating = (ComboBox)editor.GetType().GetField("_ratingBox", Private)!.GetValue(editor)!;
            var kids = (ToggleSwitch)editor.GetType().GetField("_kidsToggle", Private)!.GetValue(editor)!;
            kids.IsOn = true;
            Check(rating.SelectedIndex == 2, "Language relabelling marked the rating as user-edited and suppressed the kids safe default.");
            RequireText((DependencyObject)dialog.Content, "web.profiles.editor.accessSection", "Access");
            Check(dialog.PrimaryButtonText == AppStrings.Get("web.profiles.editor.save"), "Open profile editor action did not refresh.");
        }
        finally { dialog.Hide(); await shown; Apply("en"); await Task.Delay(80); }
        Program.Log("PASS real profile editor opens RTL in Arabic, keeps the saved name raw and refreshes while open.");
    }

    private static async Task Household(StackPanel parent)
    {
        var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(100);
        try
        {
            Apply("de");
            page.SelectSettingsSection("WebhookSync");
            await Until(() => ((FrameworkElement)page.FindName("WebhookSyncPanel")).Visibility == Visibility.Visible);
            await Settle(page);
            var panel = (DependencyObject)page.FindName("WebhookSyncPanel");
            RequireText(panel, "web.settings.webhooks.add.title", "Add a connection");
            RequireText(panel, "web.settings.webhooks.servers.title", "Connected servers");
            var provider = (ComboBox)page.FindName("WebhookProviderCombo");
            Check(provider.Items.OfType<ComboBoxItem>().All(item => item.Tag is "plex" or "emby" or "jellyfin"), "Provider routing tags changed.");
            Check(page.SidebarEntries.Single(item => item.Section == "WebhookSync").Label == AppStrings.Get("web.settings.webhooks.title"), "Webhook Sync sidebar label stayed English.");
            var create = Descendants(panel).OfType<Button>().FirstOrDefault(button => Equals(button.Content, AppStrings.Get("web.settings.webhooks.add.create")));
            Check(create != null, "Create connection action stayed English.");
            var name = (TextBox)page.FindName("WebhookConnectionNameBox");
            await Until(() => name.Text == "Synthetic connection");
            name.Text = "Unsaved connection name";
            var profile = (ComboBox)page.FindName("WebhookConnectionProfileCombo");
            var selected = profile.SelectedItem;
            Apply("ja"); await Settle(page);
            Check(name.Text == "Unsaved connection name", "Language change discarded the Webhook Sync draft name.");
            Check(ReferenceEquals(selected, profile.SelectedItem), "Language change replaced the draft default profile selection.");
        }
        finally { parent.Children.Remove(page); Apply("en"); await Task.Delay(80); }
        Program.Log("PASS household Webhook Sync labels localize with stable provider tags.");
    }

    private static async Task Metadata(StackPanel parent)
    {
        Apply("de");
        var dialog = new EditMetadataDialog(new() { ContentId = "movie", Type = "movie", Title = "Der Film: Original" }) { XamlRoot = parent.XamlRoot };
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Opened += (_, _) => opened.TrySetResult();
        var showing = dialog.ShowAsync();
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var reset = Field<Button>(dialog, "_reset"); await Until(() => reset.IsEnabled);
            Check(Equals(reset.Content, AppStrings.Get("web.item.editMetadata.resetToProvider")), "Reset to Provider stayed English: " + reset.Content);
            Check(dialog.PrimaryButtonText == AppStrings.Get("web.item.editMetadata.saveChanges"), "Save Changes stayed English: " + dialog.PrimaryButtonText);
            RequireText((DependencyObject)dialog.Content, "web.item.editMetadata.sectionGeneral", "General");
            var title = Field<Dictionary<string, TextBox>>(dialog, "_textInputs")["title"];
            Check(title.Text == "Der Film: Original", "Metadata editor altered the saved title.");

            Apply("ar"); await Settle(dialog);
            Check(dialog.FlowDirection == FlowDirection.RightToLeft, "Open metadata editor did not follow Arabic direction.");
            Check(Equals(reset.Content, AppStrings.Get("web.item.editMetadata.resetToProvider")), "Open metadata editor did not refresh to Arabic.");
            Check(title.Text == "Der Film: Original", "Language change altered the editable title.");
        }
        finally { dialog.Hide(); await showing; Apply("en"); await Task.Delay(80); }
        Program.Log("PASS metadata editor labels/actions localize, follow Arabic direction while open and keep saved values raw.");
    }

    /// <summary>
    /// P3: an unfinished device clear from an earlier save, then a new profile PUT
    /// that fails. The picker must say this attempt failed, keep the device-clear
    /// recovery, follow a language change, and retry must only DELETE.
    /// </summary>
    private static async Task RegionalLanguagePicker(StackPanel parent)
    {
        foreach (var stored in new[] { "de-DE", "es-MX", "pt-PT", "zh-TW" })
        {
            using var wire = new LanguageWire { Stored = stored };
            using var http = new HttpClient(wire);
            var client = new SiloApiClient(http); client.SetBaseUrl("https://" + LanguageWire.Host); client.SetProfile("regional-profile");
            var preference = new AppLanguagePreference(new SettingsApi(client));
            var local = new SettingsService(Path.Combine(Program.ResultDirectory, "regional-picker-" + stored));
            var coordinator = new AppLocaleCoordinator(preference, LocaleService.Shared, local, () => ["en-US"], "en-US", AppLocalization.PostTo(parent.DispatcherQueue));
            var group = new SiloPlayer.Controls.AppLanguageSettingsGroup(preference, coordinator);
            parent.Children.Add(group);
            try
            {
                await group.EnsureLoadedAsync(); await Settle(group);
                foreach (var language in new[] { "de", "ar" })
                {
                    Apply(language); await Settle(group);
                    var matched = AppLanguageMatcher.MatchShipped(stored);
                    Check(group.OfferedTags.Count == MessageCatalog.ShippedLanguages.Count + (matched == null ? 2 : 1), "Regional picker duplicated a shipped option: " + stored);
                    Check((group.Picker.SelectedItem as ComboBoxItem)?.Tag as string == (matched ?? stored), "Regional picker selected the wrong option: " + stored);
                    Check(group.UnavailableNote.Visibility == (matched == null ? Visibility.Visible : Visibility.Collapsed), "Regional picker unsupported warning is wrong: " + stored);
                    Check(wire.Stored == stored && wire.Writes.Count == 0, "Showing/relabeling the regional picker wrote the stored tag: " + stored);
                }
            }
            finally { parent.Children.Remove(group); Apply("en"); await Task.Delay(40); }
        }
        Program.Log("PASS: regional picker maps de-DE/es-MX/pt-PT to shipped options, preserves unsupported zh-TW and writes no values while displaying/relabeling.");
    }
    private static async Task LanguagePicker(StackPanel parent)
    {
        var locales = LocaleService.Shared;
        var wire = new LanguageWire { Stored = "en", DeviceStored = "en", DeviceDeleteFails = true };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://" + LanguageWire.Host);
        client.SetProfile("profile-a");
        var preference = new AppLanguagePreference(new SettingsApi(client));
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "settings-localization-picker"));
        var coordinator = new AppLocaleCoordinator(preference, locales, local, () => ["en-US"], "en-US", AppLocalization.PostTo(parent.DispatcherQueue));
        var group = new SiloPlayer.Controls.AppLanguageSettingsGroup(preference, coordinator);
        parent.Children.Add(group);
        try
        {
            Apply("en");
            await group.EnsureLoadedAsync(); await Settle(group);
            group.Picker.SelectedIndex = group.OfferedTags.ToList().IndexOf("de");
            await Until(() => wire.Stored == "de" && group.Picker.IsEnabled);
            Check(group.StatusText.Visibility == Visibility.Visible && group.StatusText.Text.Contains("this device", StringComparison.Ordinal),
                "Earlier partial save did not show the device-clear warning.");

            wire.PutFails = true;
            var writes = wire.Writes.Count;
            group.Picker.SelectedIndex = group.OfferedTags.ToList().IndexOf("ja");
            await Until(() => wire.Writes.Count > writes && group.Picker.IsEnabled);
            var saveFailed = AppStrings.Get("web.settings.accessibility.appLanguageSaveFailed");
            Check(group.StatusText.Text.Contains(saveFailed, StringComparison.Ordinal) && group.StatusText.Text.Contains("this device", StringComparison.Ordinal),
                "Failed new save showed only the earlier device-clear warning: " + group.StatusText.Text);
            var retry = ((StackPanel)group.Group.Child).Children.OfType<Button>().Single();
            Check(retry.Visibility == Visibility.Visible, "Device-clear recovery disappeared after the failed save.");
            Check(wire.Stored == "de", "Failed save changed the stored profile value.");

            Apply("ar"); await Settle(group);
            Check(group.StatusText.Text.Contains(AppStrings.Get("web.settings.accessibility.appLanguageSaveFailed"), StringComparison.Ordinal) && saveFailed != AppStrings.Get("web.settings.accessibility.appLanguageSaveFailed"),
                "Retained picker status did not follow the language change.");

            wire.PutFails = false; wire.DeviceDeleteFails = false; writes = wire.Writes.Count;
            Click(retry);
            await Until(() => wire.Writes.Count > writes && group.Picker.IsEnabled);
            Check(wire.Writes.Skip(writes).All(write => write.StartsWith("DELETE ", StringComparison.Ordinal)), "Retry resent a profile value: " + string.Join(";", wire.Writes.Skip(writes)));
            Check(group.StatusText.Visibility == Visibility.Collapsed, "Recovered device clear still shows a warning.");
        }
        finally { parent.Children.Remove(group); Apply("en"); await Task.Delay(80); }
        Program.Log("PASS language picker explains a failed save while an earlier device clear is pending, follows language and retries only the clear.");
    }

    private static void Click(Button button)
        => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();

    private static Button OverviewCard(SettingsPage page, string tag)
        => Descendants((DependencyObject)page.FindName("SettingsOverviewGroups")).OfType<Button>().Single(button => Equals(button.Tag, tag));
    private static string CardLabel(Button card) => Descendants(card).OfType<TextBlock>().First(text => text.Parent is StackPanel).Text;
    private static TextBlock RequireText(DependencyObject root, string key, string english)
    {
        var expected = AppStrings.Get(key);
        Check(expected != english, "Catalog has no translation for " + key + " in " + AppStrings.Language);
        var match = Descendants(root).OfType<TextBlock>().FirstOrDefault(text => text.Text == expected);
        if (match == null)
        {
            var stale = Descendants(root).OfType<TextBlock>().Any(text => text.Text == english);
            throw new InvalidOperationException($"{key} not shown in {AppStrings.Language}; English still shown={stale}");
        }
        return match;
    }
    private static void Apply(string language) => LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    private static async Task Settle(FrameworkElement element)
    {
        await Task.Delay(120);
        element.UpdateLayout();
        await Task.Delay(40);
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 240 && !ready(); i++) await Task.Delay(20); Check(ready(), "Native settings localization operation did not settle."); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++)
        {
            var child = VisualTreeHelper.GetChild(owner, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }

    private sealed class LanguageWire : HttpMessageHandler
    {
        internal const string Host = "settings-language.fixture.invalid";
        public string? Stored { get; set; }
        public string? DeviceStored { get; set; }
        public bool DeviceDeleteFails { get; set; }
        public bool PutFails { get; set; }
        private readonly List<string> _requests = [];
        public List<string> Writes { get { lock (_requests) return _requests.Where(request => !request.StartsWith("GET", StringComparison.Ordinal)).ToList(); } }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Check(uri.Host == Host, "External request blocked.");
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (_requests) _requests.Add($"{request.Method} {uri.PathAndQuery} {body}");
            string Value() => Stored == null ? "null" : "\"" + Stored + "\"";
            if (uri.AbsolutePath == "/api/v2/settings/contract/capabilities")
                return Raw("""{"api_version":1,"manifest_revision":28,"supports_batched_effective":true}""");
            if (uri.AbsolutePath == "/api/v2/settings/values/effective")
                return Raw($$"""{"items":[{"key":"ui.app_language","value":{{(DeviceStored == null ? Value() : "\"" + DeviceStored + "\"")}},"source":"profile"}],"revision":1}""");
            if (uri.AbsolutePath == "/api/v2/settings/values/ui.app_language")
            {
                if (request.Method == HttpMethod.Get)
                {
                    if (uri.Query.Contains("scope=profile_device"))
                        return DeviceStored == null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                            : Raw($$"""{"key":"ui.app_language","stored_value":"{{DeviceStored}}","source":"profile_device"}""");
                    return Stored == null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                        : Raw($$"""{"key":"ui.app_language","value":{{Value()}},"stored_value":{{Value()}},"source":"profile"}""");
                }
                if (request.Method == HttpMethod.Put)
                {
                    if (PutFails) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    using var document = JsonDocument.Parse(body);
                    var value = document.RootElement.GetProperty("value");
                    Stored = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    return Raw($$"""{"key":"ui.app_language","value":{{Value()}},"source":"profile"}""");
                }
                if (request.Method == HttpMethod.Delete)
                {
                    if (DeviceDeleteFails) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    DeviceStored = null;
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
            }
            throw new InvalidOperationException("Unexpected fixture route " + uri.PathAndQuery);
        }

        private static HttpResponseMessage Raw(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }

    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == Host, "External request blocked.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("/webhook-sync/"))
            {
                var connection = new { id = "c1", provider = "plex", server_name = "Synthetic connection", webhook_url = "/synthetic-secret" };
                if (path.EndsWith("/connections")) return Json(new { items = new[] { connection }, page = new { has_more = false } });
                return Json(new { items = Array.Empty<object>(), mappings = Array.Empty<object>(), actors = Array.Empty<object>() });
            }
            return Json(path switch
            {
                "/api/v2/subtitles/search" => new { results = Array.Empty<object>(), warnings = Array.Empty<object>() },
                "/api/v2/profiles" => new { items = new[] { new { id = "primary", name = "Primary", is_primary = true } } },
                "/api/v2/admin/catalog/capabilities" => new { item_metadata_reset = true },
                _ => (object)new { items = Array.Empty<object>(), state = "unavailable", sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() },
            });
        }
        private static Task<HttpResponseMessage> Json(object body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
    }
}
