using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class ServerOriginSecurityNativeFixture
{
    private const string Origin = "http://credential-target.invalid:8096";
    private const string Display = "credential-target.invalid:8096 · Not secure (HTTP)";

    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Origin);
        var api = new AuthApi(client); using var auth = new AuthService(client, api);
        var settingsApi = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "origin-settings"));
        var navigation = new NavigationService();
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(api).AddSingleton(auth)
            .AddSingleton(settingsApi).AddSingleton(settings).AddSingleton(navigation)
            .AddSingleton(new ThemeService(settings, settingsApi))
            .AddTransient<LoginViewModel>().AddTransient<InviteClaimViewModel>().BuildServiceProvider();
        var frame = new Frame { Width = 900, Height = 740 }; navigation.Frame = frame;
        var viewport = new Border
        {
            Child = frame, Width = 900, Height = 740,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"]
        };
        parent.Children.Add(viewport); field.SetValue(null, services);
        try
        {
            var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_ORIGIN_CASE");
            var parameter = mode == "accessibility" ? new ServerEntry { Url = Origin, Name = "Trusted-looking branding" } : null;
            Check(frame.Navigate(typeof(LoginPage), parameter), "Login navigation failed.");
            await Ready();
            var login = (LoginPage)frame.Content;
            var label = (TextBlock)login.FindName("ServerOriginText");
            Check(label.Text == Display && label.Visibility == Visibility.Visible,
                "Parameterless login hides or misidentifies the configured credential destination.");
            if (mode != "parameterless") CheckAccessible(label);
            if (mode is "accessibility" or "parameterless") return;

            // Navigation metadata must not override where AuthService actually sends credentials.
            Check(frame.Navigate(typeof(LoginPage), new ServerEntry { Url = "https://branding-only.invalid", Name = "Other branding" }), "Login renavigation failed.");
            await Ready(); login = (LoginPage)frame.Content;
            Check(((TextBlock)login.FindName("ServerOriginText")).Text == Display, "Login trusts navigation branding over the active credential target.");
            login.ViewModel.Username = "synthetic-user"; login.ViewModel.Password = "synthetic-password";
            await login.ViewModel.LoginCommand.ExecuteAsync(null);
            Check(wire.LoginTarget == new Uri(Origin).GetLeftPart(UriPartial.Authority), "Displayed origin differs from the actual password request destination.");

            foreach (var width in new[] { 900, 460 })
            {
                frame.Width = viewport.Width = width; viewport.UpdateLayout(); await Task.Delay(100);
                label = (TextBlock)login.FindName("ServerOriginText"); CheckAccessible(label); CheckBounds(label, login);
                await MediaParityNativeFixture.CaptureAsync(login, $"security-login-origin-{width}.png");
            }
            Check(frame.Navigate(typeof(InviteClaimPage), new InviteClaimNavigation(Origin, "synthetic-invite")), "Invite navigation failed.");
            await Until(() => frame.Content is InviteClaimPage invite && !invite.ViewModel.IsLoading && invite.ViewModel.Invitation != null);
            var page = (InviteClaimPage)frame.Content;
            Check(page.ViewModel.PasswordHint.StartsWith("At least 12 characters."), "The invite form uses the server password policy.");
            page.ViewModel.Password = page.ViewModel.ConfirmPassword = "synthetic-password";
            await page.ViewModel.AcceptCommand.ExecuteAsync(null);
            Check(page.ViewModel.PasswordError == "Include at least one capital letter." && wire.Acceptances == 0,
                "An invite password policy refusal is shown at the field before submission.");
            foreach (var width in new[] { 900, 460 })
            {
                frame.Width = viewport.Width = width; viewport.UpdateLayout(); await Task.Delay(100);
                label = (TextBlock)page.FindName("ServerOriginText");
                Check(label.Text == Display, "Invitation omits its actual destination or HTTP warning.");
                CheckAccessible(label); CheckBounds(label, page);
                await MediaParityNativeFixture.CaptureAsync(page, $"security-invite-origin-{width}.png");
            }
            var passwordGroup = (StackPanel)((PasswordBox)page.FindName("PasswordBox")).Parent;
            await Until(() => passwordGroup.Children.OfType<TextBlock>().Any(text => text.Text == page.ViewModel.PasswordError && text.Visibility == Visibility.Visible));
            foreach (var width in new[] { 1440, 460 })
            {
                frame.Width = viewport.Width = width; viewport.UpdateLayout(); await Task.Delay(100);
                await MediaParityNativeFixture.CaptureAsync(viewport, $"pw-invite-{width}.png");
            }
            wire.EmailRequired = true;
            await page.ViewModel.LoadAsync(new InviteClaimNavigation(Origin, "synthetic-invite"));
            await Until(() => ((TextBox)page.FindName("EmailBox")).IsEnabled);
            var email = (TextBox)page.FindName("EmailBox");
            email.Text = "typed@example.invalid";
            await Until(() => page.ViewModel.Email == email.Text);
            page.ViewModel.ErrorMessage = "Synthetic state refresh";
            await Task.Delay(100);
            Check(email.Text == "typed@example.invalid", "A state refresh overwrote the editable address.");
            Check(((TextBlock)page.FindName("InvitationNoteText")).Text == "Synthetic invitation note", "Link invitation note is missing.");
            Check(email.PlaceholderText.Length > 0, "Link invitation email placeholder is missing.");
            page.ViewModel.Password = "Synthetic-password";
            page.ViewModel.ConfirmPassword = "Synthetic-password";
            wire.AcceptanceBody = "{}";
            await page.ViewModel.AcceptCommand.ExecuteAsync(null);
            await Until(() => page.ViewModel.RequiresReload);
            Check(!page.ViewModel.AccountCreated && wire.Acceptances == 1, "Malformed success was shown as account creation or replayed.");
            await page.ViewModel.LoadAsync(new InviteClaimNavigation(Origin, "synthetic-invite"));
            page.ViewModel.Email = "typed@example.invalid";
            page.ViewModel.Password = "Synthetic-password";
            page.ViewModel.ConfirmPassword = "Synthetic-password";
            wire.AcceptanceBody = "{\"status\":\"accepted\",\"login_status\":\"sign_in_required\",\"username\":\"synthetic-viewer\"}";
            await page.ViewModel.AcceptCommand.ExecuteAsync(null);
            await Until(() => page.ViewModel.AccountCreated && ((StackPanel)page.FindName("ClaimFormFields")).Visibility == Visibility.Collapsed);
            Check(page.ViewModel.ErrorMessage == null && page.ViewModel.StatusMessage?.Contains("synthetic-viewer", StringComparison.Ordinal) == true,
                "Confirmed creation lacks neutral localized status.");
            Check(wire.Acceptances == 2, "Invitation acceptance was replayed.");
            await MediaParityNativeFixture.CaptureAsync(page, "security-invitation-created.png");
            Program.Log("PASS: invitation editing, note, malformed-success recovery and neutral confirmed-creation status.");
            Program.Log("PASS: parameterless/mismatched login and invitation expose the actual credential target, HTTP warning and native accessible name at narrow/desktop widths; synthetic password request matches the displayed origin.");
        }
        finally { frame.Content = null; parent.Children.Remove(viewport); field.SetValue(null, previous); }

        Task Ready() => Until(() => frame.Content is LoginPage p && p.ViewModel.ProvidersReady && p.ActualWidth > 0);
    }

    private static void CheckAccessible(TextBlock label)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(label) ?? new TextBlockAutomationPeer(label);
        Check(peer.GetName().Contains(Display, StringComparison.Ordinal), "UI Automation omits the credential destination or HTTP warning.");
    }
    private static void CheckBounds(FrameworkElement label, FrameworkElement page)
    {
        var point = label.TransformToVisual(page).TransformPoint(new(0, 0));
        Check(label.ActualWidth > 0 && label.ActualHeight > 0 && point.X >= -1 && point.X + label.ActualWidth <= page.ActualWidth + 1,
            "Credential destination is clipped horizontally.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 150 && !ready(); i++) await Task.Delay(20); Check(ready(), "Origin fixture did not settle."); }
    private sealed class Wire : HttpMessageHandler
    {
        public string? LoginTarget;
        public bool EmailRequired;
        public int Acceptances;
        public string AcceptanceBody = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "credential-target.invalid") throw new InvalidOperationException("Origin fixture attempted unintended networking.");
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.Contains("login", StringComparison.Ordinal))
            { LoginTarget = request.RequestUri.GetLeftPart(UriPartial.Authority); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }); }
            if (request.Method == HttpMethod.Post && path.EndsWith("/accept", StringComparison.Ordinal))
            {
                Acceptances++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AcceptanceBody, System.Text.Encoding.UTF8, "application/json") });
            }
            var body = path == "/api/v2/auth/password-policy" ? """{"minimum_length":12,"maximum_bytes":72,"require_uppercase":true,"require_special":true}"""
                : path.EndsWith("/invitations/capabilities", StringComparison.Ordinal) ? "{\"state\":\"available\",\"token_in_body\":true}"
                : path.Contains("invitations/", StringComparison.Ordinal) && EmailRequired ? "{\"email_required\":true,\"note\":\"Synthetic invitation note\",\"server_name\":\"Trusted-looking branding\",\"acceptance_available\":true}"
                : path.Contains("invitations/", StringComparison.Ordinal) ? """{"email":"fixture@example.invalid","server_name":"Trusted-looking branding","acceptance_available":true}"""
                : path.Contains("providers", StringComparison.Ordinal) ? "[]"
                : path.Contains("branding", StringComparison.Ordinal) ? """{"server_name":"Trusted-looking branding"}"""
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
