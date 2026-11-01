using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class PasswordSessionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = servicesField.GetValue(null);
        using var handler = new Wire();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://password-sessions-fixture.invalid");
        var authApi = new AuthApi(client);
        using var auth = new AuthService(client, authApi);
        auth.SetTokens("fixture-current-session", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("primary-fixture", profile: new() { Id = "primary-fixture", Name = "Primary", IsPrimary = true });
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "password-session-settings"));
        var api = new SettingsApi(client);
        var theme = new ThemeService(settings, api);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authApi)
            .AddTransient(_ => new SettingsViewModel(api, new CatalogApi(client), authApi,
                new HistoryImportApi(client), new WatchProvidersApi(client), auth, theme, settings,
                new AccessibilityService(settings, theme))).BuildServiceProvider();
        servicesField.SetValue(null, new FixtureServices(services, (IServiceProvider)previous!));
        SettingsPage? page = null;
        try
        {
            page = new SettingsPage { Width = 900, Height = 740 };
            parent.Children.Add(page);
            await Task.Delay(100);
            Invoke(page, "BuildSettingsOverview");
            var vm = (AccountPasswordViewModel)typeof(SettingsPage).GetField("_accountPassword", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            async Task OpenAccount()
            {
                Invoke(page, "ShowSettingsDetail", page.FindName("AccountPasswordTab"));
                await Until(() => !vm.IsLoading);
            }
            var option = (CheckBox)page.FindName("AccountSignOutOtherSessions");
            var group = (StackPanel)page.FindName("AccountSignOutOtherSessionsGroup");
            var description = (TextBlock)page.FindName("AccountSignOutOtherSessionsDescription");
            var submit = (Button)page.FindName("AccountPasswordSubmit");
            var current = (PasswordBox)page.FindName("AccountCurrentPassword");
            var replacement = (PasswordBox)page.FindName("AccountNewPassword");
            var confirmation = (PasswordBox)page.FindName("AccountConfirmPassword");
            var message = (InfoBar)page.FindName("AccountPasswordMessage");
            foreach (var support in new bool?[] { null, false, true })
            {
                handler.Support = support;
                await OpenAccount();
                Check(group.Visibility == (support == true ? Visibility.Visible : Visibility.Collapsed), "Capability option visibility");
                Check(option.IsChecked == false, "Option starts unchecked on each visit");
            }
            Check(option.IsEnabled && AutomationProperties.GetHelpText(option).Contains("API keys"), "Option enabled with accessible explanation");
            var explanation = description.Text;
            description.Text = "Synthetic alternate session explanation.";
            await Until(() => AutomationProperties.GetHelpText(option) == description.Text);
            description.Text = explanation;
            await Until(() => AutomationProperties.GetHelpText(option) == description.Text);
            // Force a compact form to prove native text wrapping and ordering.
            var form = (StackPanel)page.FindName("AccountPasswordForm");
            form.Width = 300;
            page.UpdateLayout();
            Check(description.TextWrapping == TextWrapping.Wrap && description.ActualHeight > 24 &&
                description.ActualWidth <= 268 && group.ActualWidth <= 300, "Wrapped option description fits narrow form");
            var top = option.TransformToVisual(form).TransformPoint(new(0, 0)).Y;
            var next = submit.TransformToVisual(form).TransformPoint(new(0, 0)).Y;
            Check(top >= confirmation.TransformToVisual(form).TransformPoint(new(0, 0)).Y + confirmation.ActualHeight &&
                next >= top + group.ActualHeight, "Option layout does not overlap passwords or submit");

            async Task Fill()
            {
                current.Password = "fixture old password";
                replacement.Password = confirmation.Password = "fixture new password";
                await Task.Delay(100);
            }
            await Fill();
            Invoke(page, "AccountPasswordSubmit_Click", submit, new RoutedEventArgs());
            await Until(() => !vm.IsSubmitting);
            Check(!handler.Body.TryGetProperty("sign_out_other_sessions", out _), "Unchecked advertised option omits member");
            ((IToggleProvider)new CheckBoxAutomationPeer(option).GetPattern(PatternInterface.Toggle)).Toggle();
            Check(vm.SignOutOtherSessions, "Actual CheckBox toggle reaches view model");
            await Fill();
            handler.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Invoke(page, "AccountPasswordSubmit_Click", submit, new RoutedEventArgs());
            Check(vm.IsSubmitting && !option.IsEnabled && !submit.IsEnabled && !current.IsEnabled, "Pending submission disables actual controls");
            Check(handler.Body.GetProperty("sign_out_other_sessions").GetBoolean(), "Opted-in request sends true");
            handler.Pending.SetResult(new(HttpStatusCode.NoContent));
            await Until(() => !vm.IsSubmitting);
            handler.Pending = null;
            Check(option.IsChecked == false && current.Password.Length == 0 && replacement.Password.Length == 0 && confirmation.Password.Length == 0,
                "Success resets selection and native sensitive inputs");
            Check(message.Message == "Password changed. Other devices are signed out." && client.AccessToken == "fixture-current-session",
                "Success reflects sent choice and preserves current sign-in");
            option.IsChecked = true;
            handler.Allowed = false;
            await OpenAccount();
            Check(group.Visibility == Visibility.Collapsed && !option.IsEnabled && !submit.IsEnabled && option.IsChecked == false,
                "Read-only capability hides and clears option");
            handler.Allowed = true;
            await OpenAccount();
            foreach (var mode in new[] { "network", "credentials" })
                foreach (var scenario in new[] { "choice", "submit", "departure" })
                    await DelayedLinkRefresh(mode, scenario);

            async Task DelayedLinkRefresh(string mode, string scenario)
            {
                await OpenAccount();
                await Until(() => (bool)typeof(SettingsPage).GetField("_signInLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
                var provider = new AuthProvider { InstallationId = 5, DisplayName = "Fixture Provider", Mode = mode };
                Invoke(page, "ShowAccountConnectForm", provider);
                var body = (StackPanel)page.FindName("AccountSignInBody");
                All<PasswordBox>(body).Single(box => Equals(box.Header, "Silo password")).Password = "fixture linking password";
                if (mode == "credentials")
                {
                    All<TextBox>(body).Single().Text = "fixture user";
                    All<PasswordBox>(body).Single(box => Equals(box.Header, "Fixture Provider password")).Password = "fixture provider password";
                }
                var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.IdentityPending = delayed;
                var reads = handler.DelayedIdentityReads;
                var connect = All<Button>(body).Single(button => Equals(button.Content, "Connect"));
                ((IInvokeProvider)new ButtonAutomationPeer(connect).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => handler.DelayedIdentityReads == reads + 1);
                handler.IdentityPending = null;
                Invoke(page, "ShowSettingsOverview");
                if (scenario != "departure")
                {
                    await OpenAccount();
                    option.IsChecked = true;
                    await Fill();
                    if (scenario == "submit")
                    {
                        handler.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Invoke(page, "AccountPasswordSubmit_Click", submit, new RoutedEventArgs());
                        Check(vm.IsSubmitting, "New visit must have a pending opted-in password request");
                    }
                }
                var capabilityReads = handler.CapabilityReads;
                delayed.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("""{"items":[],"can_unlink":false}""") });
                // Drain the old async-void control callback after its canceled
                // identity read returns; all transport is in memory.
                await Task.Delay(150);
                Check(handler.CapabilityReads == capabilityReads,
                    $"Delayed {mode} link refresh restarted password loading after {scenario}");
                if (scenario == "departure")
                    Check(!vm.CanChangePassword && !vm.SignOutOtherSessions, "Departed link refresh revived the password form");
                else
                {
                    Check(option.IsChecked == true && vm.SignOutOtherSessions && current.Password == "fixture old password",
                        $"Delayed {mode} link refresh cleared the newer visit's choice or inputs");
                    if (scenario == "submit")
                    {
                        Check(vm.IsSubmitting && !option.IsEnabled, "Delayed link refresh canceled the newer password submit");
                        handler.Pending!.SetResult(new(HttpStatusCode.NoContent));
                        await Until(() => !vm.IsSubmitting);
                        handler.Pending = null;
                        Check(message.Message == "Password changed. Other devices are signed out." &&
                            handler.Body.GetProperty("sign_out_other_sessions").GetBoolean(), "New visit lost request-specific success feedback");
                    }
                }
            }
            await OpenAccount();
            option.IsChecked = true;
            parent.Children.Remove(page);
            await Task.Delay(100);
            Check(!vm.SignOutOtherSessions && option.IsChecked == false, "Unload clears actual checkbox and model");
            Program.Log("PASS: password sessions actual visibility/default/toggle/payload/pending/reset/layout/current-session checks.");
        }
        finally
        {
            handler.Pending?.TrySetResult(new(HttpStatusCode.NoContent));
            handler.IdentityPending?.TrySetResult(new(HttpStatusCode.OK) { Content = new StringContent("""{"items":[],"can_unlink":false}""") });
            if (page is not null) parent.Children.Remove(page);
            // Allow native Unloaded handlers to release the fixture account
            // before restoring or disposing its service provider, including failures.
            await Task.Delay(100);
            servicesField.SetValue(null, previous);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private sealed class FixtureServices(IServiceProvider primary, IServiceProvider fallback) : IServiceProvider
    {
        public object? GetService(Type type) => primary.GetService(type) ?? fallback.GetService(type);
    }
    private static void Invoke(SettingsPage page, string name, params object[] args)
        => typeof(SettingsPage).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++) await Task.Delay(20);
        Check(done(), "Password session fixture did not settle");
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool? Support;
        internal bool Allowed = true;
        internal JsonElement Body;
        internal TaskCompletionSource<HttpResponseMessage>? Pending;
        internal TaskCompletionSource<HttpResponseMessage>? IdentityPending;
        internal int CapabilityReads, DelayedIdentityReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri?.Host == "password-sessions-fixture.invalid", "Unexpected fixture origin");
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v2/account/password/capability")
            {
                CapabilityReads++;
                var capability = new Dictionary<string, object>
                {
                    ["state"] = "available", ["allowed"] = Allowed, ["minimum_password_length"] = 8, ["maximum_password_bytes"] = 72
                };
                if (Support is { } support) capability["sign_out_other_sessions"] = support;
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(capability)) };
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/account/password")
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Body = document.RootElement.Clone();
                return Pending is { } pending ? await pending.Task : new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v2/account/identities")
            {
                if (IdentityPending is { } pending)
                {
                    DelayedIdentityReads++;
                    return await pending.Task; // Deliberately ignores cancellation to expose stale callbacks.
                }
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"items":[],"can_unlink":false}""") };
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath is "/api/v2/account/identities/link-network" or "/api/v2/account/identities/link-credentials")
                return new(HttpStatusCode.Created) { Content = new StringContent("""{"id":"fixture-link","installation_id":"5","provider_name":"Fixture Provider"}""") };
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v2/auth/providers")
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"items":[]}""") };
            throw new InvalidOperationException("Unexpected password-session fixture request");
        }
    }
}
