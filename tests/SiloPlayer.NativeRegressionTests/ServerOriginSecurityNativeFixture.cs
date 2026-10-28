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
        parent.Children.Add(frame); field.SetValue(null, services);
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
                frame.Width = width; frame.UpdateLayout(); await Task.Delay(100);
                label = (TextBlock)login.FindName("ServerOriginText"); CheckAccessible(label); CheckBounds(label, login);
                await MediaParityNativeFixture.CaptureAsync(login, $"security-login-origin-{width}.png");
            }
            Check(frame.Navigate(typeof(InviteClaimPage), new InviteClaimNavigation(Origin, "synthetic-invite")), "Invite navigation failed.");
            await Until(() => frame.Content is InviteClaimPage invite && !invite.ViewModel.IsLoading && invite.ViewModel.Invitation != null);
            var page = (InviteClaimPage)frame.Content;
            foreach (var width in new[] { 900, 460 })
            {
                frame.Width = width; frame.UpdateLayout(); await Task.Delay(100);
                label = (TextBlock)page.FindName("ServerOriginText");
                Check(label.Text == Display, "Invitation omits its actual destination or HTTP warning.");
                CheckAccessible(label); CheckBounds(label, page);
                await MediaParityNativeFixture.CaptureAsync(page, $"security-invite-origin-{width}.png");
            }
            Program.Log("PASS: parameterless/mismatched login and invitation expose the actual credential target, HTTP warning and native accessible name at narrow/desktop widths; synthetic password request matches the displayed origin.");
        }
        finally { frame.Content = null; parent.Children.Remove(frame); field.SetValue(null, previous); }

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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "credential-target.invalid") throw new InvalidOperationException("Origin fixture attempted unintended networking.");
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.Contains("login", StringComparison.Ordinal))
            { LoginTarget = request.RequestUri.GetLeftPart(UriPartial.Authority); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }); }
            var body = path.Contains("invitations/", StringComparison.Ordinal) ? """{"email":"fixture@example.invalid","server_name":"Trusted-looking branding","acceptance_available":true}"""
                : path.Contains("providers", StringComparison.Ordinal) ? "[]"
                : path.Contains("branding", StringComparison.Ordinal) ? """{"server_name":"Trusted-looking branding"}"""
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
