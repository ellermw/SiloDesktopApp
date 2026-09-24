using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;

internal static class SubtitleDialogFixture
{
    internal static async Task RunAsync(FrameworkElement owner)
    {
        var originalWidth = owner.Width;
        var originalHeight = owner.Height;
        owner.Width = 1280;
        owner.Height = 900;
        owner.Measure(new Windows.Foundation.Size(1280, 900));
        owner.Arrange(new Windows.Foundation.Rect(0, 0, 1280, 900));
        owner.UpdateLayout();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalServices = servicesField.GetValue(null);
        try
        {
            foreach (var playerMode in new[] { true, false })
            foreach (var status in new[] { "enabled", "disabled", "failed", "delayed-enabled" })
            {
                var handler = new StatusHandler(status);
                using var http = new HttpClient(handler);
                var client = new SiloApiClient(http);
                client.SetBaseUrl("https://subtitle-fixture.invalid");
                using var services = new ServiceCollection()
                    .AddSingleton(client).AddSingleton(new PlaybackApi(client)).BuildServiceProvider();
                servicesField.SetValue(null, services);
                var dialog = new SubtitleSearchDialog(1, playerMode: playerMode) { XamlRoot = owner.XamlRoot };
                var showing = dialog.ShowAsync();
                try
                {
                    var loaded = typeof(SubtitleSearchDialog).GetField("_providerStatusLoaded", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    if (status == "delayed-enabled")
                    {
                        await WaitUntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(dialog.XamlRoot), dialog.FindName("UploadLanguageComboBox")));
                        Require(((Button)dialog.FindName("BrowseUploadButton")).Focus(FocusState.Keyboard), "user moves focus during provider read");
                        handler.CompleteStatus();
                    }
                    await WaitUntilAsync(() => (bool)loaded.GetValue(dialog)!);
                    var expected = status == "disabled" ? Visibility.Collapsed : Visibility.Visible;
                    foreach (var name in new[] { "OnlineSearchHeading", "OnlineSearchControls", "OnlineSearchResults" })
                        Require(((FrameworkElement)dialog.FindName(name)).Visibility == expected, $"{name} visibility");
                    var upload = (Button)dialog.FindName("BrowseUploadButton");
                    var uploadLanguage = (ComboBox)dialog.FindName("UploadLanguageComboBox");
                    Require(upload.IsEnabled && upload.Visibility == Visibility.Visible && uploadLanguage.IsEnabled,
                        "upload remains available");
                    Control expectedFocus = status == "delayed-enabled" ? upload : uploadLanguage;
                    await Task.Delay(100);
                    var focused = FocusManager.GetFocusedElement(dialog.XamlRoot);
                    Program.Log($"subtitle focus {status}: actual={focused?.GetType().Name}/{(focused as FrameworkElement)?.Name}, expected={expectedFocus.Name}, upload={uploadLanguage.FocusState}, search={((ComboBox)dialog.FindName("LanguageComboBox")).FocusState}, targetLoaded={expectedFocus.IsLoaded}, targetSize={expectedFocus.ActualWidth}x{expectedFocus.ActualHeight}, targetVisibility={expectedFocus.Visibility}, rootSize={dialog.XamlRoot.Size}, ownerSize={owner.ActualWidth}x{owner.ActualHeight}");
                    Require(ReferenceEquals(focused, expectedFocus), "focus lands on the visible language input");

                    // Invoke the real event path, including its disabled-provider guard.
                    typeof(SubtitleSearchDialog).GetMethod("SearchButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(dialog, [dialog.FindName("SearchButton"), new RoutedEventArgs()]);
                    if (status != "disabled") await WaitUntilAsync(() => handler.SearchRequests == 1);
                    Require(handler.SearchRequests == (status == "disabled" ? 0 : 1), "online search dispatch");
                    Program.Log($"PASS: subtitle dialog {(playerMode ? "player" : "detail")} / {status}: visibility, upload, focus and search dispatch.");
                }
                finally
                {
                    dialog.Hide();
                    await showing;
                }
            }
        }
        finally
        {
            servicesField.SetValue(null, originalServices);
            owner.Width = originalWidth;
            owner.Height = originalHeight;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Subtitle fixture: " + message);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Subtitle fixture did not finish loading.");
            await Task.Delay(20);
        }
    }

    private sealed class StatusHandler(string status) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _status = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SearchRequests { get; private set; }
        public void CompleteStatus() => _status.SetResult(Json("{\"enabled\":true}"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/subtitles/search")
            {
                SearchRequests++;
                return Task.FromResult(Json("{\"results\":[]}"));
            }
            if (path != "/api/v2/subtitles/providers/status")
                throw new InvalidOperationException("Unexpected subtitle fixture request.");
            if (status == "delayed-enabled") return _status.Task;
            return Task.FromResult(status == "failed"
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") }
                : Json(status == "disabled" ? "{\"enabled\":false}" : "{\"enabled\":true}"));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }
}
