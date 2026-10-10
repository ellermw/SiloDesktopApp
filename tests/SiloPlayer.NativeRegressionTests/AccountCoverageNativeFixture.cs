using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage;

// Runs the real navigation/load seam, complementing the smaller direct-control gate.
internal static class AccountCoverageNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        Program.Log("TRACE account coverage entry");
        var serviceField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = serviceField.GetValue(null);
        var visualFailures = new List<string>();
        var resourceColors = ResourceColors(Application.Current.Resources).ToArray();
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://account-coverage-fixture.invalid");
        var authApi = new AuthApi(client); var settingsApi = new SettingsApi(client); var catalogApi = new CatalogApi(client);
        using var auth = new AuthService(client, authApi);
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "account-coverage-settings"));
        var theme = new ThemeService(local, settingsApi); var accessibility = new AccessibilityService(local, theme);
        var history = new HistoryImportApi(client); var providers = new WatchProvidersApi(client);
        var vm = new SettingsViewModel(settingsApi, catalogApi, authApi, history, providers, auth, theme, local, accessibility);
        var navigation = new NavigationService();
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authApi)
            .AddSingleton(settingsApi).AddSingleton(catalogApi).AddSingleton(history).AddSingleton(providers)
            .AddSingleton(local).AddSingleton(theme).AddSingleton(accessibility).AddSingleton(vm).AddSingleton(navigation)
            .AddSingleton(new RequestsApi(client)).AddSingleton(new RecommendationsApi(client))
            .AddSingleton(new CollectionsApi(client))
            .AddSingleton(new CardOverlayService(settingsApi)).AddSingleton(new UICustomizationService(settingsApi))
            .AddSingleton(new ToastService()).AddSingleton<ProfileSelectViewModel>().AddSingleton<TasteSeedViewModel>()
            .BuildServiceProvider();
        var frame = new Frame { Width = 1400, Height = 740 };
        Program.Log("TRACE account coverage fake services ready; constructing isolated Window");
        var window = new Window { Content = frame };
        frame.Tag = window;
        serviceField.SetValue(null, services); navigation.Frame = frame;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1400, 740));
            window.AppWindow.Show(false); await Task.Delay(160);
            Program.Log("TRACE account coverage Window acquired XamlRoot=" + (frame.XamlRoot is not null));
            if (Environment.GetEnvironmentVariable("SILO_ACCOUNT_COVERAGE_CASE") == "required-route")
            {
                auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user", PasswordChangeRequired = true });
                Program.Log("TRACE required-password before actual Frame.Navigate");
                if (!frame.Navigate(typeof(ChoosePasswordPage))) throw new InvalidOperationException("Required-password route was rejected.");
                await UntilAsync(() => frame.Content is ChoosePasswordPage);
                if (Read(frame.Content, "_flow") is null) throw new InvalidOperationException("Actual required-password navigation did not initialize the guarded transition.");
                await CaptureAsync(frame, "account-required-password-actual-route-1400x740.png");
                Program.Log("PASS actual required-password Frame navigation and guarded transition initialization");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_ACCOUNT_COVERAGE_CASE") == "recovery")
            {
                await RecoveryAsync(frame, wire, visualFailures);
                if (visualFailures.Count > 0) throw new InvalidOperationException(string.Join("; ", visualFailures));
                return;
            }
            if (!frame.Navigate(typeof(SettingsPage), "Playback")) throw new InvalidOperationException("Actual settings navigation was rejected.");
            await UntilAsync(() => frame.Content is SettingsPage page && (bool)Read(page, "_pageInitialized")!);
            var page = (SettingsPage)frame.Content;
            if (!vm.SeekSettingsAvailable || vm.ErrorMessage is not null) throw new InvalidOperationException("Actual Settings load did not establish usable contract state: " + vm.ErrorMessage);
            if (!SiloPlayer.Helpers.PageBackdrop.GetEnabled(page) || page.Background is not RadialGradientBrush)
                throw new InvalidOperationException("Navigated Settings page did not attach the standard body backdrop.");
            if (Environment.GetEnvironmentVariable("SILO_ACCOUNT_COVERAGE_CASE") == "advanced")
            {
                wire.AdvancedActive = true;
                await AccountAdvancedNativeFixture.RunAsync(frame, page, vm, wire.Advanced);
                return;
            }
            var seeks = Descendants<ComboBox>((DependencyObject)page.FindName("PlaybackPanel"))
                .Where(combo => combo.Header?.ToString()?.EndsWith("(seconds)") == true).ToArray();
            if (seeks.Length != 4 || seeks.Any(combo => !combo.IsEnabled || combo.SelectedItem is not int))
                throw new InvalidOperationException("Actual loaded Playback lacks four enabled numeric seek interval pickers.");
            foreach (var (tag, name) in new[] { ("Playback", "playback"), ("Subtitles", "subtitle-appearance"), ("Accessibility", "accessibility"), ("CardOverlays", "card-overlays"), ("Personalize", "personalize"), ("ConnectApps", "connect-apps") })
            {
                Invoke(page, "ShowSettingsDetail", page.FindName(tag == "Subtitles" ? "SubtitlesTab" : tag + "Tab"));
                await Task.Delay(250);
                foreach (var width in new[] { 1400, 460 })
                {
                    frame.Width = width; await LayoutAsync(frame);
                    await CaptureAsync(frame, $"account-settings-{name}-{width}x740.png");
                    var panel = (DependencyObject)page.FindName(tag + "Panel");
                    var label = tag switch { "Subtitles" => "Subtitles", "CardOverlays" => "Card Overlays", "ConnectApps" => "Connect Apps", _ => tag };
                    var title = Descendants<TextBlock>(panel).Single(text => text.Text == label && text.FontSize >= 20);
                    var expectedFont = tag == "Personalize" ? 20 : width < 640 ? 24 : 30;
                    Program.Log($"TRACE actual settings title {name} {width} font={title.FontSize}, expected={expectedFont}, size={title.ActualWidth}x{title.ActualHeight}");
                    if (Math.Abs(title.FontSize - expectedFont) > .1) visualFailures.Add($"{name}@{width}: heading{title.FontSize} versus source{expectedFont}");
                }
                frame.Width = 1400; await LayoutAsync(frame);
            }
            Invoke(page, "ShowSettingsOverview"); await LayoutAsync(frame);
            var search = (TextBox)page.FindName("SettingsSearchBox");
            search.Text = "seek"; await UntilAsync(() => ((TextBlock)page.FindName("SettingsSearchStatus")).Text != "Press Ctrl+K to search");
            if (!Descendants<Button>((DependencyObject)page.FindName("SettingsOverviewPanel")).Any(button => Equals(button.Tag, "Playback") && button.Visibility == Visibility.Visible))
                throw new InvalidOperationException("Seek search cannot discover Playback from the actual overview.");
            search.Text = "fixture-no-match-xyz"; await UntilAsync(() => ((TextBlock)page.FindName("SettingsSearchStatus")).Text == "No matching settings");
            InvokeButton(Descendants<Button>(page).Single(button => AutomationProperties.GetName(button) == "Clear search"));
            await UntilAsync(() => search.Text == "" && ((TextBlock)page.FindName("SettingsSearchStatus")).Text == "Press Ctrl+K to search");
            await CaptureAsync(frame, "account-settings-overview-primary-1400x740.png");
            auth.SelectProfile("child", profile: new() { Id = "child", Name = "Restricted", IsPrimary = false });
            wire.RequestsEnabled = false; Set(page, "_requestsAvailable", false); Invoke(page, "BuildSettingsOverview");
            foreach (var tab in new[] { "AccountPasswordTab", "ProfilesTab", "RequestsTab" })
            {
                if (((Button)page.FindName(tab)).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Restricted settings navigation exposes " + tab);
                Invoke(page, "ShowSettingsDetail", page.FindName(tab));
                if (((FrameworkElement)page.FindName("SettingsOverviewPanel")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Restricted deep detail bypasses the overview guard.");
            }
            search.Text = "profiles"; await LayoutAsync(frame);
            if (Descendants<Button>((DependencyObject)page.FindName("SettingsOverviewPanel")).Any(button => Equals(button.Tag, "Profiles") && button.Visibility == Visibility.Visible))
                throw new InvalidOperationException("Restricted profile entry remains discoverable in search.");
            frame.Width = 460; await LayoutAsync(frame); await CaptureAsync(frame, "account-settings-overview-restricted-460x740.png");
            auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true }); wire.RequestsEnabled = true;
            Set(page, "_requestsAvailable", true); Invoke(page, "BuildSettingsOverview"); search.Text = ""; await LayoutAsync(frame);
            try { await ProviderAsync(page, frame, vm, wire); }
            catch (InvalidOperationException ex) { visualFailures.Add(ex.Message); Program.Log("RED provider: " + ex.Message); }
            frame.Navigate(typeof(ProfileSelectPage));
            var profileVm = services.GetRequiredService<ProfileSelectViewModel>();
            await UntilAsync(() => !profileVm.IsLoading && profileVm.Profiles.Count == 3);
            foreach (var width in new[] { 900, 460 })
            {
                frame.Width = width; await LayoutAsync(frame); await CaptureAsync(frame, $"account-profiles-{width}x740.png");
                var title = (TextBlock)((ProfileSelectPage)frame.Content).FindName("ProfilePickerTitle");
                var expected = Math.Clamp(width * .07, 35.2, 72);
                Program.Log($"TRACE actual profiles{width}: titleFont={title.FontSize}, expected={expected}");
                if (Math.Abs(title.FontSize - expected) > .1) visualFailures.Add($"profiles@{width}: title{title.FontSize} versus source{expected}");
            }
            frame.Navigate(typeof(TasteSeedPage), true);
            var taste = services.GetRequiredService<TasteSeedViewModel>();
            await UntilAsync(() => !taste.IsLoading && taste.Items.Count == 15);
            var tastePage = (TasteSeedPage)frame.Content;
            foreach (var (width, columns) in new[] { (1400, 7), (1100, 6), (900, 5), (700, 4), (460, 3) })
            {
                frame.Width = width; await LayoutAsync(frame);
                var repeater = (ItemsRepeater)tastePage.FindName("TasteItemsGrid");
                if (((UniformGridLayout)repeater.Layout).MaximumRowsOrColumns != columns)
                    throw new InvalidOperationException("Actual taste column breakpoint mismatch at " + width);
                if (width is 900 or 460)
                {
                    await CaptureAsync(frame, $"account-taste-{width}x740.png");
                    var title = Descendants<TextBlock>(tastePage).Single(text => text.Text == "Pick what you love");
                    var actions = (StackPanel)tastePage.FindName("TasteHeaderActions");
                    var expectedFont = width < 640 ? 20 : 24;
                    var header = ((Grid)tastePage.Content).Children.OfType<Border>().First();
                    Program.Log($"TRACE taste header{width}: title={title.FontSize}, actionsRow={Grid.GetRow(actions)}, padding={header.Padding}, titleHeight={title.ActualHeight}");
                    if (title.FontSize != expectedFont || Grid.GetRow(actions) != 0 || header.Padding.Left != (width < 640 ? 16 : 24) || header.Padding.Top != 16)
                        visualFailures.Add($"taste@{width}: title/header/actions geometry differs from current source");
                }
            }
            foreach (var item in taste.Items.Take(3)) taste.Toggle(item);
            if (!taste.CanSubmit) throw new InvalidOperationException("Taste requires more than the source three selections.");
            wire.TasteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            InvokeButton((Button)tastePage.FindName("DoneButton"));
            await UntilAsync(() => wire.TasteWrites == 1 && taste.IsSaving);
            if (taste.CanSubmit) throw new InvalidOperationException("Taste allows duplicate submissions while pending.");
            wire.TasteGate.SetResult(Reply(new { title = "Fixture taste rejection", detail = "Fixture taste rejection" }, HttpStatusCode.UnprocessableEntity));
            await UntilAsync(() => !taste.IsSaving && taste.ErrorMessage is not null);
            if (!ReferenceEquals(frame.Content, tastePage) || taste.SelectedCount != 3 || !taste.CanSubmit) throw new InvalidOperationException("Taste rejection lost its selection or dismissed the page.");
            await CaptureAsync(frame, "account-taste-rejected-460x740.png");
            wire.TasteGate = null;
            InvokeButton((Button)tastePage.FindName("DoneButton"));
            await UntilAsync(() => frame.Content is SettingsPage && wire.TasteWrites == 2);
            if (((FrameworkElement)((SettingsPage)frame.Content).FindName("PlaybackPanel")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Settings taste success does not return to Playback.");
            frame.Navigate(typeof(TasteSeedPage), true); await UntilAsync(() => !taste.IsLoading && frame.Content is TasteSeedPage);
            InvokeButton((Button)((TasteSeedPage)frame.Content).FindName("SkipButton"));
            await UntilAsync(() => frame.Content is SettingsPage);
            if (wire.TasteWrites != 2) throw new InvalidOperationException("Taste Cancel performed a submit.");
            await RecoveryAsync(frame, wire, visualFailures);
            if (visualFailures.Count > 0) throw new InvalidOperationException("Actual settings presentation mismatches: " + string.Join("; ", visualFailures));
            Program.Log("PASS actual navigated settings load/seek/search/restricted guards, static wide/narrow captures, profile picker, taste breakpoints/pending/rejection/retry/settings return/Cancel no-write.");
        }
        finally
        {
            wire.TasteGate?.TrySetCanceled(); wire.ProviderGate?.TrySetCanceled(); wire.RecoveryRequestGate?.TrySetCanceled(); wire.RecoverySaveGate?.TrySetCanceled(); navigation.Frame = null; frame.Content = null; window.Close();
            // Drain Unloaded while this fixture's provider is still alive.
            await Task.Delay(100);
            serviceField.SetValue(null, previous);
            foreach (var (brush, color) in resourceColors) brush.Color = color;
            SiloPlayer.Helpers.PageBackdrop.RefreshAll(); SiloPlayer.Helpers.AuthBackdrop.RefreshAll();
        }
    }
    private static async Task RecoveryAsync(Frame frame, Wire wire, List<string> failures)
    {
        Program.Log("TRACE recovery independent unsigned scope entry");
        // Public recovery starts unsigned. Do not sign the preceding synthetic
        // shell out merely to prepare this fixture's independent public state.
        var serviceField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = serviceField.GetValue(null);
        using var http = new HttpClient(wire, disposeHandler: false);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://account-coverage-fixture.invalid");
        var authApi = new AuthApi(client); using var unsigned = new AuthService(client, authApi);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(unsigned).AddSingleton(authApi)
            .AddSingleton(new SettingsApi(client)).AddSingleton(App.Services.GetRequiredService<NavigationService>()).BuildServiceProvider();
        serviceField.SetValue(null, services);
        Program.Log("TRACE recovery unsigned scope registered");
        try
        {
        Program.Log("TRACE recovery before direct PasswordRecoveryPage constructor");
        var constructed = new PasswordRecoveryPage();
        Program.Log("TRACE recovery direct constructor completed content=" + constructed.Content?.GetType().Name);
        if (Environment.GetEnvironmentVariable("SILO_ACCOUNT_RECOVERY_ACTIVATION") == "direct")
        {
            frame.Content = constructed;
            Program.Log("TRACE recovery direct instance assigned to actual Frame");
            await (Task)typeof(PasswordRecoveryPage).GetMethod("AvailabilityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(constructed, null)!;
            Program.Log("TRACE recovery direct public availability completed");
        }
        else
        {
        Program.Log("TRACE recovery before Frame.Navigate PasswordRecoveryPage");
        frame.Navigate(typeof(PasswordRecoveryPage));
        Program.Log("TRACE recovery after Frame.Navigate content=" + frame.Content?.GetType().Name);
        }
        var requestPage = (PasswordRecoveryPage)frame.Content;
        var request = (Button)Read(requestPage, "_request")!; var login = (TextBox)Read(requestPage, "_login")!;
        await UntilAsync(() => request.IsEnabled && !(bool)Read(requestPage, "_busy")!);
        Program.Log("TRACE recovery initial available form ready");
        foreach (var width in new[] { 900, 460 })
        {
            frame.Width = width; await LayoutAsync(frame); await CaptureAsync(frame, $"account-recovery-request-{width}x740.png");
            var card = Descendants<Border>(requestPage).Single(border => border.Child is StackPanel && border.MaxWidth >= 384);
            var title = (TextBlock)Read(requestPage, "_title")!;
            Program.Log($"TRACE actual recovery request{width}: card={card.ActualWidth}x{card.ActualHeight}, radius={card.CornerRadius.TopLeft}, title={title.Text}/{title.FontSize}");
            if (Math.Abs(card.ActualWidth - 384) > 1 || Math.Abs(card.ActualHeight - 366.5) > 2 || card.CornerRadius.TopLeft != 12 || title.FontSize != 30 || title.Text != "Reset password") failures.Add($"recovery request@{width}: current AuthCard geometry/title mismatch ({card.ActualWidth}x{card.ActualHeight})");
            if (Math.Abs(login.ActualWidth - 280) > 1 || Math.Abs(login.ActualHeight - 36) > 1) failures.Add("Recovery username field does not match source280x36");
        }
        login.Text = "fixture-viewer"; wire.RecoveryRequestGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            InvokeButton(request); await UntilAsync(() => wire.RecoveryRequests == 1);
            if (request.IsEnabled || login.IsEnabled || !Equals(request.Content, "Sending...")) failures.Add("Recovery request leaves Send/input enabled or lacks Sending while pending");
            await CaptureAsync(frame, "account-recovery-request-pending-460x740.png");
        }
        finally
        {
            wire.RecoveryRequestGate.TrySetResult(Reply(new { error = "too_many_requests", message = "Fixture rate limit" }, HttpStatusCode.TooManyRequests));
            await UntilAsync(() => !(bool)Read(requestPage, "_busy")!); wire.RecoveryRequestGate = null;
        }
        if (login.Text != "fixture-viewer" || !request.IsEnabled || !((TextBlock)Read(requestPage, "_status")!).Text.Contains("Too many requests from this network")) throw new InvalidOperationException("Recovery429 loses its source error/request draft/retry.");
        await CaptureAsync(frame, "account-recovery-request-rate-limited-460x740.png");
        InvokeButton(request); await UntilAsync(() => wire.RecoveryRequests == 2 && !(bool)Read(requestPage, "_busy")!);
        if (((TextBlock)Read(requestPage, "_title")!).Text != "Check your email") failures.Add("Accepted reset request does not show separate Check your email state");
        await CaptureAsync(frame, "account-recovery-request-sent-460x740.png");
        frame.Navigate(typeof(PasswordRecoveryPage), "fixture-token");
        var reset = (PasswordRecoveryPage)frame.Content; var save = (Button)Read(reset, "_save")!;
        await UntilAsync(() => save.Visibility == Visibility.Visible && !(bool)Read(reset, "_busy")!);
        var password = (PasswordBox)Read(reset, "_password")!; var confirmation = (PasswordBox)Read(reset, "_confirmation")!;
        foreach (var width in new[] { 900, 460 })
        {
            frame.Width = width; await LayoutAsync(frame); await CaptureAsync(frame, $"account-recovery-reset-{width}x740.png");
            var card = Descendants<Border>(reset).Single(border => border.Child is StackPanel && border.MaxWidth >= 384);
            if (Math.Abs(card.ActualWidth - 384) > 1 || Math.Abs(card.ActualHeight - 500.5) > 2 || card.CornerRadius.TopLeft != 12 || ((TextBlock)Read(reset, "_title")!).Text != "Choose a new password") failures.Add($"Recovery reset@{width} source AuthCard mismatch ({card.ActualWidth}x{card.ActualHeight})");
            if (Math.Abs(password.ActualWidth - 280) > 1 || Math.Abs(password.ActualHeight - 36) > 1 || Math.Abs(confirmation.ActualWidth - 280) > 1) failures.Add("Recovery reset fields do not match source280x36");
        }
        password.Password = confirmation.Password = "fixture-new-password"; wire.RecoverySaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            InvokeButton(save); await UntilAsync(() => wire.RecoverySaves == 1);
            if (save.IsEnabled || password.IsEnabled || confirmation.IsEnabled || !Equals(save.Content, "Saving...")) failures.Add("Recovery reset does not disable fields/show Saving while pending");
            await CaptureAsync(frame, "account-recovery-reset-pending-460x740.png");
        }
        finally
        {
            wire.RecoverySaveGate.TrySetResult(Reply(new { error = "validation_failed", message = "Fixture password rejection" }, HttpStatusCode.UnprocessableEntity));
            await UntilAsync(() => !(bool)Read(reset, "_busy")!); wire.RecoverySaveGate = null;
        }
        if (password.Password != "fixture-new-password" || !save.IsEnabled) throw new InvalidOperationException("Reset rejected password is not retained for retry.");
        await CaptureAsync(frame, "account-recovery-reset-rejected-460x740.png");
        InvokeButton(save); await UntilAsync(() => wire.RecoverySaves == 2 && !(bool)Read(reset, "_busy")!);
        if (((TextBlock)Read(reset, "_title")!).Text != "Password changed" || password.Password.Length != 0 || confirmation.Password.Length != 0) failures.Add("Completed reset does not render separate Password changed state/clear secrets");
        await CaptureAsync(frame, "account-recovery-reset-completed-460x740.png");
        await RecoveryEdgesAsync(frame, wire, unsigned, failures);
        Program.Log("PASS actual public recovery request/reset wire,429/422 deferred rejection retention/retry, availability/404/uncertain/signout; presentation/pending failures aggregated separately.");
        }
        finally { frame.Content = null; serviceField.SetValue(null, previous); }
    }
    private static async Task RecoveryEdgesAsync(Frame frame, Wire wire, AuthService auth, List<string> failures)
    {
        wire.RecoveryAvailabilityStatus = HttpStatusCode.ServiceUnavailable;
        frame.Navigate(typeof(PasswordRecoveryPage));
        var page = (PasswordRecoveryPage)frame.Content;
        await UntilAsync(() => !(bool)Read(page, "_busy")! && Equals(Read(page, "_state"), "availability-failed"));
        await CaptureAsync(frame, "account-recovery-availability-failed-460x740.png");
        wire.RecoveryAvailabilityStatus = HttpStatusCode.OK;
        InvokeButton((Button)Read(page, "_retryAvailability")!);
        await UntilAsync(() => Equals(Read(page, "_state"), "request") && !(bool)Read(page, "_busy")!);
        wire.RecoveryAvailable = false;
        frame.Navigate(typeof(PasswordRecoveryPage)); page = (PasswordRecoveryPage)frame.Content;
        await UntilAsync(() => Equals(Read(page, "_state"), "unavailable") && !(bool)Read(page, "_busy")!);
        if (((Button)Read(page, "_request")!).Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Unavailable email recovery exposes a request action.");
        await CaptureAsync(frame, "account-recovery-unavailable-460x740.png");

        wire.RecoveryAvailable = true; wire.RecoveryLookupStatus = HttpStatusCode.NotFound;
        frame.Navigate(typeof(PasswordRecoveryPage), "fixture-token"); page = (PasswordRecoveryPage)frame.Content;
        await UntilAsync(() => Equals(Read(page, "_state"), "link-unavailable") && !(bool)Read(page, "_busy")!);
        if (((TextBlock)Read(page, "_title")!).Text != "Link unavailable" || !Equals(((Button)Read(page, "_signIn")!).Content, "Request a new link"))
            throw new InvalidOperationException("Expired reset link lacks its separate request-new-link fallback.");
        await CaptureAsync(frame, "account-recovery-expired-460x740.png");
        wire.RecoveryLookupStatus = HttpStatusCode.ServiceUnavailable;
        frame.Navigate(typeof(PasswordRecoveryPage), "fixture-token"); page = (PasswordRecoveryPage)frame.Content;
        await UntilAsync(() => Equals(Read(page, "_state"), "lookup-failed") && !(bool)Read(page, "_busy")!);
        wire.RecoveryLookupStatus = HttpStatusCode.OK;
        InvokeButton((Button)Read(page, "_lookup")!);
        await UntilAsync(() => Equals(Read(page, "_state"), "reset") && !(bool)Read(page, "_busy")!);
        var password = (PasswordBox)Read(page, "_password")!; var confirmation = (PasswordBox)Read(page, "_confirmation")!;
        var save = (Button)Read(page, "_save")!;
        password.Password = confirmation.Password = "fixture-new-password";
        wire.RecoveryCompleteStatus = HttpStatusCode.ServiceUnavailable;
        var before = wire.RecoverySaves; InvokeButton(save);
        await UntilAsync(() => wire.RecoverySaves == before + 1 && !(bool)Read(page, "_busy")!);
        if (save.IsEnabled || ((Button)Read(page, "_lookup")!).Visibility != Visibility.Visible || password.Password.Length == 0)
            throw new InvalidOperationException("Uncertain completion permits resubmit without reloading its link or discards recovery input.");
        await CaptureAsync(frame, "account-recovery-uncertain-460x740.png");
        wire.RecoveryCompleteStatus = HttpStatusCode.OK;
        InvokeButton((Button)Read(page, "_lookup")!);
        await UntilAsync(() => save.IsEnabled && !(bool)Read(page, "_busy")!);
        if (wire.RecoverySaves != before + 1) throw new InvalidOperationException("Reloading a reset link repeated the password write.");
        InvokeButton(save); await UntilAsync(() => Equals(Read(page, "_state"), "completed") && !(bool)Read(page, "_busy")!);

        // This AuthService has no credential store and only a strict fake HTTP
        // transport; sign out cannot touch installed-app credentials or a server.
        auth.SetTokens("fixture-signed-access", "fixture-signed-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture-signed", Username = "Fixture signed in", Role = "user" });
        var lookups = wire.RecoveryLookups;
        frame.Navigate(typeof(PasswordRecoveryPage), "fixture-token"); page = (PasswordRecoveryPage)frame.Content;
        await UntilAsync(() => Equals(Read(page, "_state"), "signed-in"));
        if (wire.RecoveryLookups != lookups || ((PasswordBox)Read(page, "_password")!).Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Signed-in recovery performed public lookup or exposed password replacement before sign out.");
        await CaptureAsync(frame, "account-recovery-signed-in-460x740.png");
        InvokeButton((Button)Read(page, "_signOut")!);
        await UntilAsync(() => !auth.IsLoggedIn && frame.Content is PasswordRecoveryPage next && !ReferenceEquals(next, page)
            && Equals(Read(next, "_state"), "reset") && !(bool)Read(next, "_busy")!);
        if (wire.RecoveryLogouts != 1 || wire.RecoveryLookups != lookups + 1)
            failures.Add("Recovery Sign out did not resume the same link exactly once against the fake authority.");
        Program.Log("PASS actual recovery unavailable/retry/404/reload/uncertain completion/sign-out handoff; no credential store attached.");
    }
    private static async Task ProviderAsync(SettingsPage page, Frame frame, SettingsViewModel vm, Wire wire)
    {
        Invoke(page, "ShowSettingsDetail", page.FindName("WatchProvidersTab"));
        await UntilAsync(() => vm.WatchProviderCards.Count == 1 && !vm.IsLoadingWatchProviders);
        var provider = vm.WatchProviderCards.Single();
        if (!provider.UsesApiKey || !provider.CredentialsConfigured) throw new InvalidOperationException("Provider fixture did not establish its actual API-key contract.");
        InvokeButton(Descendants<Button>((DependencyObject)page.FindName("WatchProvidersCardsPanel")).Single(button => Equals(button.Content, "Connect")));
        await UntilAsync(() => Descendants<ProviderSchemaForm>(page).Count() == 1);
        var form = Descendants<ProviderSchemaForm>(page).Single();
        var key = Descendants<PasswordBox>(page).Single(input => input.PlaceholderText == "API key");
        var secret = Descendants<PasswordBox>(form).Single(); var limit = Descendants<TextBox>(form).Single();
        var action = Descendants<Button>((DependencyObject)page.FindName("WatchProvidersCardsPanel")).Single(button => Equals(button.Content, "Connect"));
        key.Password = "fixture-api-key"; await LayoutAsync(frame);
        if (action.IsEnabled) throw new InvalidOperationException("Required provider schema permits missing required values.");
        secret.Password = "fixture-secret"; limit.Text = "7";
        await UntilAsync(() => action.IsEnabled);
        var toggle = Descendants<ToggleSwitch>(form).Single();
        if (!toggle.IsOn) throw new InvalidOperationException("Provider default boolean is not visible.");
        toggle.IsOn = false; await LayoutAsync(frame);
        if (((FrameworkElement)limit.Parent).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Provider default-driven condition did not hide its number field.");
        toggle.IsOn = true; await LayoutAsync(frame);
        foreach (var width in new[] { 1400, 460 }) { frame.Width = width; await LayoutAsync(frame); await CaptureAsync(frame, $"account-provider-required-{width}x740.png"); }
        wire.ProviderGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingMatchesSource = false;
        try
        {
            InvokeButton(action); await UntilAsync(() => wire.ProviderWrites == 1 && provider.IsBusy);
            pendingMatchesSource = !action.IsEnabled && key.IsEnabled && secret.IsEnabled && limit.IsEnabled;
            await CaptureAsync(frame, "account-provider-pending-460x740.png");
        }
        finally
        {
            wire.ProviderGate.TrySetResult(Reply(new { title = "Fixture provider rejection", detail = "Fixture provider rejection" }, HttpStatusCode.UnprocessableEntity));
            await UntilAsync(() => !provider.IsBusy); wire.ProviderGate = null;
        }
        await LayoutAsync(frame);
        if (!provider.ApiKeyPromptVisible || provider.ApiKey != "fixture-api-key" || !provider.ConnectionConfigDraft["connection"].ContainsKey("limit"))
            throw new InvalidOperationException("Provider rejection lost its native input draft.");
        await CaptureAsync(frame, "account-provider-rejected-460x740.png");
        var actual = wire.ProviderPayload!.Value.GetProperty("connection_config").GetProperty("connection");
        if (actual.GetProperty("limit").ValueKind != JsonValueKind.Number || actual.GetProperty("limit").GetInt32() != 7 || !actual.GetProperty("enabled").GetBoolean())
            throw new InvalidOperationException("Actual provider UI sent strings instead of its typed integer/boolean schema.");
        if (!pendingMatchesSource) throw new InvalidOperationException("Actual provider pending state must disable duplicate Connect while retaining the editable source draft.");
        InvokeButton(Descendants<Button>((DependencyObject)page.FindName("WatchProvidersCardsPanel")).Single(button => Equals(button.Content, "Connect")));
        await UntilAsync(() => wire.ProviderWrites == 2 && provider.Connected);
        Program.Log("PASS actual native provider required schema/default condition/typed API-key payload/pending/rejection draft/retry.");
    }
    private static IEnumerable<(SolidColorBrush Brush, Windows.UI.Color Color)> ResourceColors(ResourceDictionary dictionary)
    {
        foreach (var item in dictionary.Values) if (item is SolidColorBrush brush) yield return (brush, brush.Color);
        foreach (var child in dictionary.MergedDictionaries) foreach (var item in ResourceColors(child)) yield return item;
    }
    private static object? Read(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
    private static void Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private static void InvokeButton(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 160; i++) { if (ready()) return; await Task.Delay(25); } throw new TimeoutException("Account coverage native state timed out."); }
    private static async Task LayoutAsync(FrameworkElement value)
    {
        // The host's accumulating StackPanel can crop centered auth content in
        // RTB. Own the actual client viewport, as the modal acceptance gates do.
        if (value is Frame { Tag: Window owner })
        {
            owner.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)value.Width, 740));
            if (value.Clip is RectangleGeometry clip) clip.Rect = new Windows.Foundation.Rect(0, 0, value.Width, 740);
        }
        value.UpdateLayout(); await Task.Delay(150); value.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject { if (root is T item) yield return item; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static async Task CaptureAsync(FrameworkElement value, string name)
    {
        // Fast fake replies can finish before the native Frame entrance
        // transition. RTB then includes that moving page's off-client ink.
        // Let the actual navigation/layout settle without altering its scale.
        value.UpdateLayout(); await Task.Delay(350); value.UpdateLayout();
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(value);
        Program.Log($"TRACE account viewport {name}: requested={value.Width}x740, actual={value.ActualWidth}x{value.ActualHeight}, bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}");
        if (value is Frame frame && bitmap.PixelHeight > 740 && frame.ActualHeight == 740)
        {
            if (frame.Content is FrameworkElement content)
            {
                Program.Log($"TRACE client ink overflow content={content.GetType().Name} actual={content.ActualWidth}x{content.ActualHeight}");
                foreach (var child in Descendants<FrameworkElement>(content).Where(child => child.ActualHeight > 740).Take(8))
                    Program.Log($"TRACE overflowing descendant {child.GetType().Name}/{child.Name} actual={child.ActualWidth}x{child.ActualHeight}");
            }
            // RTB includes ink beyond a root element's layout slot. A real
            // native client clips that ink at its Window boundary; capture that
            // same viewport without scaling the content to a different size.
            frame.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, frame.ActualWidth, frame.ActualHeight) };
            frame.UpdateLayout(); await Task.Delay(80);
            await bitmap.RenderAsync(frame);
            Program.Log($"TRACE actual client-boundary clip bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}");
        }
        if (Math.Abs(value.ActualWidth - value.Width) > 1 || Math.Abs(value.ActualHeight - 740) > 1 || bitmap.PixelWidth != (int)value.Width || bitmap.PixelHeight != 740) throw new InvalidOperationException("Account coverage capture is not the requested client viewport.");
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
    }
    private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture\""); return response;
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly AccountAdvancedNativeFixture.State Advanced = new();
        internal bool AdvancedActive;
        internal bool RequestsEnabled = true; internal int TasteWrites; internal TaskCompletionSource<HttpResponseMessage>? TasteGate;
        internal int ProviderWrites; internal JsonElement? ProviderPayload; internal TaskCompletionSource<HttpResponseMessage>? ProviderGate;
        internal int RecoveryRequests, RecoverySaves, RecoveryLookups, RecoveryLogouts;
        internal bool RecoveryAvailable = true;
        internal HttpStatusCode RecoveryAvailabilityStatus = HttpStatusCode.OK, RecoveryLookupStatus = HttpStatusCode.OK, RecoveryCompleteStatus = HttpStatusCode.OK;
        internal TaskCompletionSource<HttpResponseMessage>? RecoveryRequestGate, RecoverySaveGate;
        private static readonly JsonElement Schema = JsonSerializer.Deserialize<JsonElement>("""
            [{"key":"connection","title":"Connection options","required":true,"json_schema":"{\"type\":\"object\",\"properties\":{\"enabled\":{\"type\":\"boolean\",\"default\":true},\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100},\"secret\":{\"type\":\"string\",\"minLength\":3}},\"required\":[\"limit\",\"secret\"]}","admin_form":{"fields":[{"key":"enabled","label":"Enable synchronization","control":"SWITCH","default_value":true},{"key":"limit","label":"Import limit","control":"NUMBER","required":true,"validation":{"has_min":true,"min":1,"has_max":true,"max":100},"show_when":[{"field":"enabled","equals":["true"]}]},{"key":"secret","label":"Connection secret","control":"PASSWORD","required":true,"secret":true,"validation":{"min_length":3}}]}}]
            """);
        private readonly Dictionary<string, JsonElement> _defaults;
        internal Wire()
        {
            using var stream = typeof(DeviceSettingDisplay).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Settings.device-settings.json")!;
            using var document = JsonDocument.Parse(stream); _defaults = document.RootElement.EnumerateArray().ToDictionary(row => row.GetProperty("key").GetString()!, row => row.GetProperty("default_value").Clone());
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (AdvancedActive && Advanced.Handle(request, ct) is { } advanced) return advanced;
            var path = request.RequestUri!.AbsolutePath; var method = request.Method.Method;
            if (path == "/api/v2/recommendations/taste-seed" && method == "POST") { TasteWrites++; return TasteGate?.Task.WaitAsync(ct) ?? Task.FromResult(Reply(new { added = 3 })); }
            if (path == "/api/v2/watch-providers/fixture/auth/api-key" && method == "POST") return ProviderWriteAsync(request, ct);
            if (path == "/api/v2/password-resets" && method == "POST") { RecoveryRequests++; return RecoveryRequestGate?.Task.WaitAsync(ct) ?? Task.FromResult(Reply(new { })); }
            if (path == "/api/v2/auth/logout" && method == "POST") { RecoveryLogouts++; return Task.FromResult(Reply(new { })); }
            if (path == "/api/v2/password-resets/fixture-token/complete" && method == "POST") { RecoverySaves++; return RecoverySaveGate?.Task.WaitAsync(ct) ?? Task.FromResult(RecoveryCompleteStatus == HttpStatusCode.OK ? Reply(new { status = "completed", username = "Fixture", login_status = "sign_in_required" }) : Reply(new { error = "internal_error", message = "Fixture ambiguous completion" }, RecoveryCompleteStatus)); }
            if (path == "/api/v2/capabilities/password-reset") return Task.FromResult(RecoveryAvailabilityStatus == HttpStatusCode.OK ? Reply(new { state = RecoveryAvailable ? "available" : "disabled" }) : Reply(new { error = "unavailable", message = "Fixture capability failure" }, RecoveryAvailabilityStatus));
            if (path == "/api/v2/password-resets/fixture-token") { RecoveryLookups++; return Task.FromResult(RecoveryLookupStatus == HttpStatusCode.OK ? Reply(new { username = "Fixture", server_name = "Fixture Silo", expires_at = "2026-10-01T12:00:00Z" }) : Reply(new { error = "unavailable", message = "Fixture lookup failure" }, RecoveryLookupStatus)); }
            object body = new { items = Array.Empty<object>(), page = new { has_more = false } };
            if (path == "/api/v2/profiles") body = new { items = new[] { new { id = "fixture", name = "Primary", is_primary = true, has_pin = false }, new { id = "child", name = "Restricted", is_primary = false, has_pin = true }, new { id = "third", name = "Long profile name", is_primary = false, has_pin = false } }, avatar_upload_enabled = false, max_advisory_age_supported = true, require_advisory_age_supported = true };
            else if (path == "/api/v2/capabilities/password-reset") body = new { state = "available" };
            else if (path == "/api/v2/theme/branding") body = new { server_name = "Fixture Silo" };
            else if (path == "/api/v2/password-resets/fixture-token") body = new { username = "Fixture", server_name = "Fixture Silo", expires_at = "2026-10-01T12:00:00Z" };
            else if (path == "/api/v2/requests/status") body = new { requests_enabled = RequestsEnabled };
            else if (path == "/api/v2/settings/contract/capabilities") body = new { api_version = 1, manifest_revision = 15, supports_batched_effective = true, supports_idempotent_writes = true, supports_atomic_shortcuts = true, client_families = new[] { "web", "desktop" } };
            else if (path == "/api/v2/settings/values/effective")
            {
                var keys = request.RequestUri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("keys=")).Select(part => Uri.UnescapeDataString(part[5..]));
                body = new { items = keys.Select(key => new { key, value = _defaults.GetValueOrDefault(key, JsonSerializer.SerializeToElement<object?>(null)), source = "default", constrained = false }).ToArray(), revision = 15 };
            }
            else if (path.StartsWith("/api/v2/settings/values/")) { var key = Uri.UnescapeDataString(path[24..]); body = new { key, value = _defaults.GetValueOrDefault(key, JsonSerializer.SerializeToElement<object?>(null)), source = "default" }; }
            else if (path == "/api/v2/theme/admin-css") body = new { vars = JsonSerializer.Serialize(new Dictionary<string, string> { ["background"] = "#101722", ["foreground"] = "#F4F8FF", ["card"] = "#151E2B", ["card-foreground"] = "#F4F8FF", ["popover"] = "#121A25", ["popover-foreground"] = "#F4F8FF", ["primary"] = "#78AEFC", ["primary-foreground"] = "#0F1722", ["secondary"] = "#1B2634", ["secondary-foreground"] = "#D7E2F1", ["muted"] = "#151E2B", ["muted-foreground"] = "#90A0B5", ["surface"] = "#151E2B", ["surface-raised"] = "#223245", ["surface-hover"] = "#1D2A3B", ["accent"] = "#203043", ["accent-foreground"] = "#F4F8FF", ["border"] = "#28384D", ["input"] = "#182231", ["ring"] = "#78AEFC", ["destructive"] = "#EF6B73", ["destructive-foreground"] = "#FFFFFF", ["ambient"] = "#78AEFC" }), raw_css = "" };
            else if (path == "/api/v2/recommendations/taste-seed/items") body = new { items = Enumerable.Range(1, 15).Select(index => new { id = "taste-" + index, content_id = "taste-" + index, type = "movie", title = "Fixture title " + index, year = 2000 + index, user_state = new { is_favorite = false } }).ToArray(), page = new { has_more = false } };
            else if (path == "/api/v2/compat/connect-info") body = new { jellyfin = new { enabled = false, pending_restart = false, public_url = "", server_name = "Fixture Silo" }, account = new { password_login_available = true } };
            else if (path == "/api/v2/card-overlays/config") body = new { enabled = true, preset = "standard", items = new { }, quick_actions_enabled = true, quick_actions = "both" };
            else if (path == "/api/v2/recommendations/taste-profile") body = new { genre_preferences = Array.Empty<object>(), total_ratings = 0, total_favorites = 0 };
            else if (path == "/api/v2/watch-providers") body = new { items = new[] { new { key = "fixture", display_name = "Fixture provider", capabilities = new { import_watched = true, import_progress = true, export_watched = true, export_unwatched = true, scrobble_playback = true }, connection_config_schema = Schema } } };
            else if (path == "/api/v2/watch-providers/fixture/connection") body = new { provider = "fixture", display_name = "Fixture provider", auth_method = "api_key", connected = ProviderWrites >= 2, credentials_configured = true, capabilities = new { import_watched = true, import_progress = true, export_watched = true, export_unwatched = true, scrobble_playback = true }, connection_config_schema = Schema };
            Program.Log("TRACE account coverage " + method + " " + path);
            return Task.FromResult(Reply(body));
        }
        private async Task<HttpResponseMessage> ProviderWriteAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ProviderPayload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); ProviderWrites++;
            return ProviderGate is not null ? await ProviderGate.Task.WaitAsync(ct) : Reply(new { connected = true });
        }
    }
}

