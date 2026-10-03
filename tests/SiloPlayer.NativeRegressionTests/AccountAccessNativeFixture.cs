using System.Net;
using System.Reflection;
using SiloPlayer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class AccountAccessNativeFixture
{
    internal static async Task RunAsync(Panel parent)
    {
        var servicesProperty = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = servicesProperty.GetValue(null);
        using var handler = new Wire(); var client = new SiloApiClient(new HttpClient(handler)); client.SetBaseUrl("https://account-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("fixture", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "1", Username = "Fixture", PasswordChangeRequired = true });
        using var provider = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(new NavigationService()).BuildServiceProvider();
        servicesProperty.SetValue(null, provider);
        try
        {
            var page = new ChoosePasswordPage { Width = 850, Height = 700 }; parent.Children.Add(page);
            Set(page, "_flow", new RequiredPasswordTransition(auth, client));
            Field<PasswordBox>(page, "_temporary").Password = "temporary";
            Field<PasswordBox>(page, "_password").Password = "new-password";
            Field<PasswordBox>(page, "_confirmation").Password = "new-password";
            InvokeSave(page); await WaitAsync(() => Field<Button>(page, "_save").Content?.ToString() == "Retry sign-in");
            if (Field<PasswordBox>(page, "_temporary").Visibility != Visibility.Collapsed || handler.PasswordWrites != 1)
                throw new InvalidOperationException("Native required-password retry retains temporary inputs or repeats a password write.");
            InvokeSave(page); await WaitAsync(() => Field<Button>(page, "_save").IsEnabled);
            if (handler.PasswordWrites != 1) throw new InvalidOperationException("Native retry resubmitted the password.");
            parent.Children.Remove(page);

            var constructor = typeof(ProfileEditorDialog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            var dialog = constructor.Invoke([parent.XamlRoot, new Profile { Name = "Future", MaxContentRating = "future-ceiling", MaxAdvisoryAge = 12 },
                Array.Empty<Library>(), false, new AuthApi(client), true, false]);
            var rating = Field<ComboBox>(dialog, "_ratingBox");
            if (rating.SelectedItem?.ToString() != "future-ceiling (saved limit)" || Field<ComboBox>(dialog, "_advisoryBox").SelectedIndex != 12)
                throw new InvalidOperationException("Native profile editor lost a saved ceiling or advisory age.");
            if (Field<ToggleSwitch>(dialog, "_requireAdvisory").Parent != null)
                throw new InvalidOperationException("Native profile editor exposes unadvertised require-age capability.");
            Program.Log("PASS native required-password save/retry and unknown profile rating/advisory capability preservation");
        }
        finally { servicesProperty.SetValue(null, previous); }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
    private static void InvokeSave(ChoosePasswordPage page) => page.GetType().GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [Field<Button>(page, "_save"), new RoutedEventArgs()]);
    private static async Task WaitAsync(Func<bool> ready)
    { for (var i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Native account form did not settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class Wire : HttpMessageHandler
    {
        public int PasswordWrites;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/account/password")
            { PasswordWrites++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("{\"message\":\"Fixture renewal unavailable\"}") });
        }
    }
}
