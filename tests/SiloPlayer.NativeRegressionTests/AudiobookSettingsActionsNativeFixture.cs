using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

internal static class AudiobookSettingsActionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://audio-settings-actions.invalid"); client.SetProfile("fixture-profile");
        using var auth = new AuthService(client, new AuthApi(client));
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "audio-settings-actions"));
        using var player = new PlayerService(new PlaybackApi(client), new CatalogApi(client), auth, client, settings, new SettingsApi(client));
        await player.RefreshSeekPreferencesAsync();
        var flyout = (Flyout)typeof(PlayerService).Assembly.GetType("SiloPlayer.Controls.AudiobookSettingsFlyout")!.GetMethod("Create")!.Invoke(null, [player, settings])!;
        var panel = (StackPanel)flyout.Content; flyout.Content = null;
        var host = new Border { Child = panel, Padding = new Thickness(16, 14, 16, 14) }; parent.Children.Add(host); host.UpdateLayout(); await Task.Delay(100);
        try
        {
            var rows = panel.Children.OfType<StackPanel>().Where(row => row.Children.OfType<SiloPlayer.Controls.WrapPanel>().Any()).ToArray();
            var back = rows[0].Children.OfType<SiloPlayer.Controls.WrapPanel>().Single().Children.OfType<Button>().ToArray();
            var all = rows.SelectMany(row => row.Children.OfType<SiloPlayer.Controls.WrapPanel>().Single().Children.OfType<Button>()).ToArray();
            var choice = back.Single(button => (int)button.Tag == 15);
            handler.Reject = true; Click(choice); await UntilAsync(() => handler.Writes == 1 && all.All(button => !button.IsEnabled));
            handler.Gate!.SetResult(); await UntilAsync(() => all.All(button => button.IsEnabled));
            if (player.SeekIntervals.AudiobookBack != 10 || AutomationProperties.GetHelpText(choice) == "Selected") throw new InvalidOperationException("Rejected actual settings chip becomes selected or overwrites effective preferences.");
            handler.Reject = false; handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); Click(choice); await UntilAsync(() => handler.Writes == 2 && all.All(button => !button.IsEnabled));
            await Task.Delay(50); if (handler.Writes != 2) throw new InvalidOperationException("Pending settings chip duplicates a profile write.");
            handler.Gate.SetResult(); await UntilAsync(() => all.All(button => button.IsEnabled) && player.SeekIntervals.AudiobookBack == 15);
            if (AutomationProperties.GetHelpText(choice) != "Selected" || handler.Scope != "profile" || handler.Key != SeekPreferences.Keys[2] || handler.Value != 15) throw new InvalidOperationException("Retried actual settings chip doesn't save the exact profile key/value and select the effective result.");
            await MediaParityNativeFixture.CaptureAsync(host, "media-audiobook-settings-retried.png");
            var thirty = back.Single(button => (int)button.Tag == 30); handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); Click(thirty); await UntilAsync(() => handler.Writes == 3);
            client.SetProfile("different-profile"); handler.Gate.SetResult(); await UntilAsync(() => all.All(button => button.IsEnabled));
            if (player.SeekIntervals.AudiobookBack != 10 || AutomationProperties.GetHelpText(thirty) == "Selected") throw new InvalidOperationException("Late settings save is adopted by a replacement profile.");
            Program.Log("PASS: AUDIOBOOK_SETTINGS_ACTIONS_COMPLETED actual chip pending/disabled, rejection retains effective selection, exact profile key/value retry and late authority replacement.");
        }
        finally { parent.Children.Remove(host); flyout.Content = panel; }
    }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Audiobook settings action did not settle."); }
    private sealed class Handler : HttpMessageHandler
    {
        internal bool Reject; internal int Writes, Value, Effective = 10; internal string? Key, Scope; internal TaskCompletionSource? Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/settings/values/effective") return Json(new { items = new[] { new { key = SeekPreferences.Keys[2], value = Effective } } });
            if (request.Method != HttpMethod.Put || !path.StartsWith("/api/v2/settings/values/")) throw new InvalidOperationException("Unexpected isolated audiobook settings route " + path);
            Writes++; Key = Uri.UnescapeDataString(path.Split('/').Last()); Scope = new Windows.Foundation.WwwFormUrlDecoder(request.RequestUri.Query).FirstOrDefault(pair => pair.Name == "scope")?.Value;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Value = body.RootElement.GetProperty("value").GetInt32();
            if (Gate != null) await Gate.Task.WaitAsync(ct);
            if (Reject) return new(HttpStatusCode.Conflict) { Content = new StringContent("{\"title\":\"Fixture rejection\"}", Encoding.UTF8, "application/problem+json") };
            Effective = Value; return Json(new { key = Key, value = Value });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
