using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Services;

/// <summary>The official WebUI's arrival toasts, shown through the production presenter, toast service and container.</summary>
internal static class NotificationToastNativeFixture
{
    internal static async Task RunAsync(Window window)
    {
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        var service = new ToastService(); var container = new ToastContainer();
        var host = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] }; host.Children.Add(container);
        var previous = window.Content; window.Content = host;
        try
        {
            service.Register(container, host.DispatcherQueue);
            await Task.Delay(200);
            Show(service, """{"id":"a","type":"episode.available","series_title":"Severance","season_number":2,"episode_number":5,"episode_title":"Trojan Horse"}""");
            Show(service, """{"id":"b","type":"watchlist.available","series_title":"Andor"}""");
            Check(!Show(service, """{"id":"c","type":"media.updated","series_title":"Nope"}"""), "a type without a toast shows nothing");
            await Until(() => Texts(host).Any(t => t.Contains("S2E5 — Trojan Horse")) && Texts(host).Any(t => t.Contains("Andor is now available")));
            var texts = Texts(host).ToArray();
            Check(texts.Any(t => t.Contains("New episode of Severance") && t.Contains("S2E5 — Trojan Horse")), "new-episode toast reads title then code and episode title");
            Check(texts.Any(t => t.Contains("Andor is now available") && t.Contains("A title on your watchlist has arrived in the library.")), "watchlist toast reads title then description");
            Check(!texts.Any(t => t.Contains("Nope")), "an unknown type leaves no toast");
            await MediaParityNativeFixture.CaptureAsync(host, "notification-toast-1440.png");
            Program.Log("PASS: NOTIFICATION_TOAST_COMPLETED new-episode and watchlist-arrival toasts through the production presenter at 1440 wide.");
        }
        finally { service.Unregister(); window.Content = previous; }
    }
    private static bool Show(ToastService service, string json)
    {
        using var document = JsonDocument.Parse(json);
        return NotificationToastPresenter.Show(service, document.RootElement.Clone());
    }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Notification toast: " + message); }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Notification toast did not appear."); }
    private static IEnumerable<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(t => t.Text);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
