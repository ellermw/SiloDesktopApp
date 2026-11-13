using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class ShellLocalizationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var main = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var previousMain = main.GetValue(null);
        var locales = LocaleService.Shared;
        var previousLocale = locales.Current;
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://shell-localization.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "My Account العربية", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "My Profile 日本語", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "shell-localization"));
        var preferences = local.Load(); preferences.SiloCastEnabled = false; local.Save(preferences);
        var languageCoordinator = new AppLocaleCoordinator(new AppLanguagePreference(new SettingsApi(client)), locales, local,
            () => ["en-US"], "en-US", AppLocalization.PostTo(parent.DispatcherQueue));
        await using var provider = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(local).AddSingleton(http).AddSingleton(languageCoordinator)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "shell-localization-images")))
            .AddSingleton<ItemDetailPrefetchCache>().AddSingleton<LoginViewModel>().AddSingleton<SignupViewModel>()
            .AddSingleton<ProfileSelectViewModel>().AddSingleton<ServerSelectViewModel>().AddSingleton<NotificationsViewModel>()
            .AddSingleton<NotificationSettingsViewModel>().AddSingleton<ActivateDeviceViewModel>().AddSingleton<InviteClaimViewModel>().BuildServiceProvider();
        field.SetValue(null, provider);
        MainWindow? window = null;
        var host = new Grid { Width = 900, Height = 740 };
        parent.Children.Add(host);
        var failures = new List<string>();
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        async Task Show(Page page, string key, Func<Page, string> read)
        {
            Apply("en"); host.Children.Clear(); host.Children.Add(page); await Settle(page);
            Apply("de"); await Settle(page);
            Check(read(page) == AppStrings.Get(key), page.GetType().Name + " German retained text: " + read(page));
            Apply("ar"); await Settle(page);
            Check(read(page) == AppStrings.Get(key), page.GetType().Name + " Arabic retained text: " + read(page));
            if (page is ActivateDevicePage) Check(DocumentTitle.FromPageType(typeof(ActivateDevicePage)) == DocumentTitle.FromLabel(AppStrings.Get("web.auth.activate.documentTitle")), "Activation window title is not translated");
            Check(page.FlowDirection == FlowDirection.RightToLeft, page.GetType().Name + " RTL root missing");
            Program.Log("TRACE: switched actual " + page.GetType().Name + " controls through de/ar.");
        }
        try
        {
            await Show(new LoginPage(), "web.auth.signIn", page => (string)All<Button>(page).Single(button => button.Command == ((LoginPage)page).ViewModel.LoginCommand).Content);
            var login = provider.GetRequiredService<LoginViewModel>();
            Apply("de");
            var start = await login.BeginOAuthAsync(new() { InstallationId = 7, NativeStartPath = "/api/v2/auth/oauth/7/native/start" });
            var state = start.Query.Split('&').Single(part => part.StartsWith("app_state=", StringComparison.Ordinal)).Split('=')[1];
            Check(await login.AcceptNativeOAuthCallbackAsync("org.siloserver.silo:/auth/callback?iss=https%3A%2F%2Fshell-localization.invalid&server=fixture-server&state=" + state + "&error=account_disabled"), "synthetic OAuth refusal was not accepted");
            Check(login.ErrorMessage == AppStrings.Get("web.auth.oauthFailure.accountDisabled"), "OAuth refusal was not localized in German");
            Apply("ar"); await Settle(host);
            Check(login.ErrorMessage == AppStrings.Get("web.auth.oauthFailure.accountDisabled"), "OAuth refusal did not refresh to Arabic");
            login.ProvidersReady = true;
            Apply("de"); await login.StartDeviceLoginAsync(); await Settle(host);
            Check(login.ErrorMessage == AppStrings.Get("web.auth.login.device.denied"), "phone sign-in denial was not localized in German");
            Apply("ar"); await Settle(host);
            Check(login.ErrorMessage == AppStrings.Get("web.auth.login.device.denied"), "phone sign-in denial did not refresh to Arabic");
            login.CancelDeviceLogin();
            Program.Log("TRACE: retained OAuth refusal and phone sign-in denial refresh through actual auth wire.");
            await Show(new SignupPage(), "web.auth.createAccount", page => (string)All<Button>(page).Single(button => Localized.GetContent(button) == "web.auth.createAccount" || Equals(button.Content, "Create account")).Content);
            var profilePage = new ProfileSelectPage();
            await Show(profilePage, "web.profiles.picker.title", page => ((TextBlock)page.FindName("ProfilePickerTitle")).Text);
            var profileVm = provider.GetRequiredService<ProfileSelectViewModel>();
            profileVm.Profiles.Add(new() { Id = "raw", Name = "My Profile العربية", IsPrimary = true });
            Check(profileVm.Profiles.Single().Name == "My Profile العربية", "profile saved name changed");
            var serverVm = provider.GetRequiredService<ServerSelectViewModel>();
            serverVm.IsAddingServer = true; serverVm.NewServerName = "Custom server 日本語"; serverVm.NewServerUrl = "https://custom.invalid";
            await Show(new ServerSelectPage(), "common.cancel", page => (string)All<Button>(page).Single(button => button.Command == serverVm.CancelAddServerCommand).Content);
            Check(serverVm.NewServerName == "Custom server 日本語" && serverVm.NewServerUrl == "https://custom.invalid", "server draft data changed on locale switch");
            var plugin = new PluginRoutePage();
            typeof(PluginRoutePage).GetMethod("ShowError", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plugin, ["Synthetic native plugin fallback"]);
            host.Children.Clear(); host.Children.Add(plugin); Apply("ar"); await Settle(plugin);
            Check(plugin.FlowDirection == FlowDirection.RightToLeft && ((TextBlock)plugin.FindName("ErrorText")).Text == "Synthetic native plugin fallback", "plugin fallback data/direction changed");
            await Show(new ServerSetupRequiredPage(), "common.retry", page => (string)All<Button>(page).Single(button => Localized.GetContent(button) == "common.retry" || Equals(button.Content, "Retry")).Content);
            var household = new HouseholdSetupPage();
            await (Task)typeof(HouseholdSetupPage).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(household, [CancellationToken.None])!;
            await Show(household, "web.onboarding.householdSetup.justMe", page => (string)((Button)page.FindName("SkipButton")).Content);
            provider.GetRequiredService<InviteClaimViewModel>().Invitation = new() { ServerName = "Raw server العربية", Email = "fixture@example.invalid" };
            var invitationPage = new InviteClaimPage();
            await Show(invitationPage, "web.auth.createAccount", page => (string)((Button)page.FindName("CreateAccountButton")).Content);
            provider.GetRequiredService<InviteClaimViewModel>().IsSubmitting = true;
            Apply("de"); await Settle(invitationPage);
            Check((string)((Button)invitationPage.FindName("CreateAccountButton")).Content == AppStrings.Get("web.auth.passwordRules.creatingAccount"), "busy invitation action reset during language change");
            provider.GetRequiredService<InviteClaimViewModel>().IsSubmitting = false;
            await Show(new ActivateDevicePage(), "web.ui.continue", page => (string)All<Button>(page).Single(button => Localized.GetContent(button) == "web.ui.continue" || Equals(button.Content, "Continue")).Content);
            var recovery = new PasswordRecoveryPage();
            var render = typeof(PasswordRecoveryPage).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object recoveryTitle = render.GetParameters()[1].ParameterType == typeof(string) ? "Reset password" : (Func<string>)(() => "Reset password");
            object recoveryDescription = render.GetParameters()[2].ParameterType == typeof(string) ? "Fixture request" : (Func<string>)(() => "Fixture request");
            render.Invoke(recovery, ["request", recoveryTitle, recoveryDescription, null]);
            await Show(recovery, "web.auth.forgotPassword.send", page => (string)All<Button>(page).Single(button => Localized.GetContent(button) == "web.auth.forgotPassword.send" || Equals(button.Content, "Send reset link")).Content);
            await Show(new ChoosePasswordPage(), "web.auth.passwordRules.savePassword", page => (string)All<Button>(page).Single(button => Localized.GetContent(button) == "web.auth.passwordRules.savePassword" || Equals(button.Content, "Save password")).Content);
            var notificationsPage = new NotificationsPage();
            await Show(notificationsPage, "web.notifications.inbox.title", page => ((TextBlock)((StackPanel)((Grid)page.FindName("HeaderGrid")).Children[0]).Children[1]).Text);
            var notifications = provider.GetRequiredService<NotificationsViewModel>();
            var rawRow = new AppNotification { Id = "raw-row", Type = "episode.available", SeriesTitle = "Raw Series العربية", EpisodeTitle = "Raw Episode 日本語", SeasonNumber = 2, EpisodeNumber = 3,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"), ReasonFlags = new() { Favorite = true, NextUp = true } };
            notifications.ApplyCreated(rawRow);
            notifications.ApplyCreated(new() { Id = "fallback-row", Type = "request.declined", ReasonFlags = new() { Reason = "Raw reason 日本語" }, CreatedAt = DateTimeOffset.UtcNow.ToString("O") });
            await Settle(notificationsPage);
            foreach (var language in new[] { "de", "ar" })
            {
                Apply(language); await Settle(notificationsPage);
                var row = All<Grid>(notificationsPage).Single(item => item.Name == "NotificationRowRoot" && item.DataContext is AppNotification { Id: "raw-row" });
                Check(((TextBlock)row.FindName("NotificationTitleText")).Text == "Raw Series العربية", "notification title data translated");
                var code = AppStrings.Get("web.notifications.queries.episodeCode", new Dictionary<string, object?> { ["season"] = "2", ["episode"] = "3" });
                Check(((TextBlock)row.FindName("NotificationSubtitleText")).Text == AppStrings.Get("web.notifications.inbox.episodeLine", new Dictionary<string, object?> { ["code"] = code, ["title"] = "Raw Episode 日本語" }), "episode notification line did not refresh");
                Check(((TextBlock)row.FindName("NotificationTimeText")).Text == AppStrings.Get("web.notifications.inbox.justNow"), "notification time did not refresh");
                Check(((System.Collections.IEnumerable)((ItemsControl)row.FindName("NotificationReasons")).ItemsSource).Cast<string>().SequenceEqual(new[] { AppStrings.Get("web.notifications.reason.favorite"), AppStrings.Get("web.notifications.reason.nextUp") }), "notification reasons did not refresh");
                var fallback = All<Grid>(notificationsPage).Single(item => item.Name == "NotificationRowRoot" && item.DataContext is AppNotification { Id: "fallback-row" });
                Check(((TextBlock)fallback.FindName("NotificationTitleText")).Text == AppStrings.Get("web.notifications.inbox.mediaRequest"), "notification fallback title did not refresh");
                Check(((TextBlock)fallback.FindName("NotificationSubtitleText")).Text == AppStrings.Get("web.notifications.inbox.requestDeclinedReason", new Dictionary<string, object?> { ["reason"] = "Raw reason 日本語" }), "notification raw decline reason lost");
                Check(((TextBlock)notificationsPage.FindName("UnreadButtonText")).Text == AppStrings.Get("web.notifications.inbox.filterUnreadCount", new Dictionary<string, object?> { ["count"] = notifications.UnreadCount.ToString(AppStrings.FormatCulture) }), "notification unread count did not refresh");
            }
            Program.Log("TRACE: actual notification rows preserve raw media/reasons and refresh labels, dates, reasons and counts.");
            wire.FailNotifications = true; await notifications.ReloadAsync();
            foreach (var language in new[] { "de", "ar" })
            {
                Apply(language); await Settle(notificationsPage);
                Check(((TextBlock)notificationsPage.FindName("ErrorText")).Text == "Failed to load notifications: " + AppStrings.Get("error.field.required"), "retained structured API field problem lost its context or did not refresh");
            }
            wire.FailNotifications = false;
            host.Children.Clear(); host.Children.Add(profilePage); await Settle(profilePage);
            var pinTask = (Task)typeof(ProfileSelectPage).GetMethod("ShowPinDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Profile), typeof(string)], null)!.Invoke(profilePage, [new Profile { Id = "pin", Name = "PIN Profile 日本語", HasPin = true }, "add"])!;
            ContentDialog? pinDialog = null;
            try
            {
                for (var attempt = 0; attempt < 30 && pinDialog == null; attempt++)
                { await Task.Delay(30); pinDialog = (ContentDialog?)typeof(ProfileSelectPage).GetField("_activePinDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(profilePage); }
                if (pinDialog == null) throw new InvalidOperationException("PIN dialog did not open");
                await Settle(pinDialog);
                var pinInput = All<PasswordBox>(pinDialog).Single(); pinInput.Password = "1234";
                foreach (var language in new[] { "de", "ar" })
                {
                    Apply(language); await Settle(pinDialog);
                    Check(Equals(pinDialog.Title, AppStrings.Get("web.profiles.pin.title", new Dictionary<string, object?> { ["name"] = "PIN Profile 日本語" })), "open PIN title did not refresh");
                    Check(pinInput.Password == "1234" && pinInput.PlaceholderText == AppStrings.Get("web.profiles.pin.placeholder"), "PIN input mutated or placeholder stale: " + language + "/" + pinInput.Password + "/" + pinInput.PlaceholderText);
                    Check(All<TextBlock>(pinDialog).Any(text => text.Text == AppStrings.Get("web.profiles.picker.addNeedsPin")), "PIN purpose did not refresh: " + language);
                }
            }
            finally { pinDialog?.Hide(); await pinTask; }

            host.Children.Clear();
            var tour = new FeatureTourDialog(new OnboardingFlow { Steps = [new OnboardingStep { Kind = "welcome", Title = "Raw server title العربية", Body = "Raw server message 日本語" }] }) { XamlRoot = parent.XamlRoot };
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tour.Opened += (_, _) => opened.TrySetResult();
            var shown = tour.ShowAsync();
            try
            {
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(5)); Apply("de"); await Settle(tour);
                Check(tour.PrimaryButtonText == AppStrings.Get("common.done"), "tour action did not switch");
                Apply("ar"); await Settle(tour);
                Check(tour.CloseButtonText == AppStrings.Get("web.onboarding.tour.skip") && tour.FlowDirection == FlowDirection.RightToLeft, "open tour text/direction did not switch");
                Check(All<TextBlock>(tour).Any(text => text.Text == "Raw server title العربية"), "raw tour title translated");
            }
            finally { tour.Hide(); await shown; }
            host.Children.Clear();
            var webhookControl = new SiloPlayer.Controls.NotificationSettingsControl();
            host.Children.Add(webhookControl); await Settle(webhookControl);
            var rawWebhook = new NotificationWebhook { Name = "Raw webhook العربية" };
            Apply("de");
            var webhookPending = (Task)typeof(SiloPlayer.Controls.NotificationSettingsControl).GetMethod("OpenWebhookDialogAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(webhookControl, new object[] { rawWebhook })!;
            ContentDialog? webhookDialog = null;
            for (var attempt = 0; attempt < 200 && webhookDialog == null; attempt++)
            {
                if (webhookPending.IsFaulted) await webhookPending;
                webhookDialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Select(popup => popup.Child).OfType<ContentDialog>().FirstOrDefault();
                if (webhookDialog == null) await Task.Delay(15);
            }
            if (webhookDialog == null) throw new InvalidOperationException("Webhook edit dialog did not open");
            try
            {
                var draft = ((Panel)webhookDialog.Content).Children.OfType<TextBox>().First();
                draft.Text = "Unsaved webhook draft";
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(webhookDialog);
                    Check(Equals(webhookDialog.Title, AppStrings.Get("web.settings.notifications.webhooks.editTitle", new Dictionary<string, object?> { ["name"] = rawWebhook.Name })), "Open webhook edit title did not translate");
                    Check(draft.Text == "Unsaved webhook draft" && rawWebhook.Name == "Raw webhook العربية", "Relabeling webhook title changed draft or saved name");
                }
            }
            finally { webhookDialog.Hide(); await webhookPending; }
            Program.Log("PASS: webhook edit title follows de/ar/ja while raw name and unsaved draft remain intact.");
            window = new MainWindow(); main.SetValue(null, window);
            var root = (FrameworkElement)window.Content;
            var nav = (NavigationView)root.FindName("NavView");
            var loaded = (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(RoutedEventHandler), window);
            nav.Loaded -= loaded;
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1280, 900)); window.AppWindow.Show(false);
            window.ShowMainNavigation();
            await languageCoordinator.RefreshAsync();
            await Settle(root);
            Apply("en"); await Settle(root);
            var librariesLabel = All<TextBlock>(root).Single(text => Localized.GetText(text) == "web.shell.sidebar.libraries");
            Program.Log($"TRACE uppercase en: actual={librariesLabel.Text}; expected={AppStrings.Get("web.shell.sidebar.libraries").ToUpper(System.Globalization.CultureInfo.GetCultureInfo(AppStrings.Language))}; language={AppStrings.Language}; formats={AppStrings.FormatCulture.Name}; enabled={Localized.GetUppercase(librariesLabel)}; loaded={librariesLabel.IsLoaded}.");
            Check(librariesLabel.Text == "LIBRARIES" && AppStrings.Language == "en", "English sidebar header lost uppercase styling");
            Apply("de"); await Settle(root);
            Program.Log($"TRACE uppercase de: actual={librariesLabel.Text}; expected={AppStrings.Get("web.shell.sidebar.libraries").ToUpper(System.Globalization.CultureInfo.GetCultureInfo(AppStrings.Language))}; language={AppStrings.Language}; formats={AppStrings.FormatCulture.Name}; enabled={Localized.GetUppercase(librariesLabel)}; loaded={librariesLabel.IsLoaded}.");
            Check(librariesLabel.Text == "MEDIATHEKEN" && AppStrings.Language == "de", "German sidebar header did not refresh with its interface language");
            Check((string)((NavigationViewItem)root.FindName("HomeNavItem")).Content == AppStrings.Get("web.shell.sidebar.home"), "shell navigation did not switch: " + ((NavigationViewItem)root.FindName("HomeNavItem")).Content + " loaded=" + ((NavigationViewItem)root.FindName("HomeNavItem")).IsLoaded);
            Apply("ar"); await Settle(root);
            Check(nav.FlowDirection == FlowDirection.RightToLeft, "shell navigation RTL missing");
            Check((string)((NavigationViewItem)root.FindName("HomeNavItem")).Content == AppStrings.Get("web.shell.sidebar.home"), "shell navigation did not switch to Arabic: " + ((NavigationViewItem)root.FindName("HomeNavItem")).Content);
            Check(((FrameworkElement)root.FindName("PlayerOverlayControl")).FlowDirection == FlowDirection.LeftToRight, "player surface mirrored");
            Check(auth.SelectedProfile?.Name == "My Profile 日本語", "owner profile name changed");
            var pinType = typeof(MainWindow).GetNestedType("SidebarPinNavTag", BindingFlags.NonPublic)!;
            var pinTag = Activator.CreateInstance(pinType, nonPublic: true)!;
            pinType.GetProperty("Label")!.SetValue(pinTag, "Raw saved pin 日本語");
            var pinned = (Grid)typeof(MainWindow).GetMethod("BuildPinnedSidebarContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new NavigationViewItem { Tag = pinTag }, pinTag])!;
            host.Children.Add(pinned);
            var unpin = pinned.Children.OfType<Button>().Single();
            window.SetDynamicTitle("Raw dynamic title العربية");
            var flyout = (Microsoft.UI.Xaml.Controls.Flyout)root.FindName("ProfileFooterFlyout");
            flyout.ShowAt(root);
            try
            {
                foreach (var language in new[] { "de", "ar" })
                {
                    Apply(language); await Settle(root);
                    Check(AutomationProperties.GetName(unpin) == AppStrings.Get("web.shell.sidebar.unpin") + " Raw saved pin 日本語" && Equals(ToolTipService.GetToolTip(unpin), AppStrings.Get("web.shell.sidebar.unpin")), "programmatic unpin accessibility/tooltip did not refresh");
                    Check(pinned.Children.OfType<TextBlock>().Single().Text == "Raw saved pin 日本語", "saved pin label translated");
                    var popup = (ScrollViewer)root.FindName("ProfileMenuScroller");
                    Check(popup.FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), "open profile popup direction did not refresh");
                    Check(All<TextBlock>((Button)root.FindName("ProfileSwitchButton")).Any(text => text.Text == AppStrings.Get("web.shell.sidebar.switchProfile")), "detached profile menu action did not refresh");
                    Check(window.AppWindow.Title == DocumentTitle.FromLabel("Raw dynamic title العربية"), "raw dynamic window title was replaced on locale switch");
                }
            }
            finally { flyout.Hide(); }
            wire.Language = "de";
            Check(await languageCoordinator.RefreshAsync(), "current owner language did not apply");
            await Settle(root);
            Check((string)((NavigationViewItem)root.FindName("HomeNavItem")).Content == AppStrings.Get("web.shell.sidebar.home") && AppStrings.Language == "de", "shell did not follow current owner language");
            wire.Language = "fr"; wire.HoldNext = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var oldRead = languageCoordinator.RefreshAsync();
            await wire.Held.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wire.Language = "ar";
            auth.SelectProfile("owner-next", profile: new() { Id = "owner-next", Name = "Current Owner 日本語", IsPrimary = true });
            Check(await languageCoordinator.RefreshAsync(), "new owner language did not apply");
            wire.HoldNext!.TrySetResult();
            Check(!await oldRead, "retired owner language was accepted");
            await Settle(root);
            Check(AppStrings.Language == "ar" && nav.FlowDirection == FlowDirection.RightToLeft && auth.SelectedProfile?.Name == "Current Owner 日本語", "late owner result changed shell language/current profile");
            Program.Log("TRACE: shell popup follows language while raw dynamic title survives; current-owner language wins over late retired response.");
            if (failures.Count != 0) throw new InvalidOperationException(string.Join(" | ", failures));
            Program.Log("PASS: actual shell/auth/recovery/onboarding/profile/notification/tour retained controls switch languages; raw account/server data survives and Arabic mirrors only navigation.");
        }
        finally
        {
            host.Children.Clear(); parent.Children.Remove(host);
            window?.Close(); main.SetValue(null, previousMain); field.SetValue(null, previous);
            locales.Apply(locales.Create(previousLocale.Resolution));
        }
    }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Apply(string language) => LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    private static async Task Settle(FrameworkElement element) { await Task.Delay(120); element.UpdateLayout(); await Task.Delay(60); }
    private sealed class Wire : HttpMessageHandler
    {
        internal string Language { get; set; } = "en";
        internal bool FailNotifications { get; set; }
        internal TaskCompletionSource? HoldNext { get; set; }
        internal TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _holding;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "shell-localization.invalid") throw new InvalidOperationException("External fixture request refused");
            var route = request.RequestUri.AbsolutePath;
            if (route == "/api/v2/system/identity") return Json("""{"server_id":"fixture-server"}""");
            if (route == "/api/v2/auth/device/start") return Json("""{"device_code":"synthetic-device","user_code":"ABCD-EFGH","verification_uri_complete":"https://shell-localization.invalid/activate?code=synthetic","interval":1,"expires_in":600}""");
            if (route == "/api/v2/auth/device/poll") return Json("""{"status":"denied"}""");
            if (FailNotifications && route == "/api/v2/notifications") return new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"type":"https://silo.invalid/docs/api/v2/problems/validation_failed","detail":"Raw English validation detail","errors":[{"location":"profile","code":"required","detail":"A profile is required"}]}""") };
            if (route == "/api/v2/settings/contract/capabilities") return Json("""{"api_version":1,"manifest_revision":28,"supports_batched_effective":true}""");
            if (route == "/api/v2/settings/values/effective")
            {
                var language = Language;
                if (HoldNext is { } hold && !_holding)
                { _holding = true; Held.TrySetResult(); await hold.Task; }
                return Json($$"""{"items":[{"key":"ui.app_language","value":"{{language}}","source":"profile"}],"revision":1}""");
            }
            return Json(route.EndsWith("/capabilities") ? """{"capabilities":{}}""" : """{"items":[],"profiles":[],"page":{"has_more":false}}""");
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
