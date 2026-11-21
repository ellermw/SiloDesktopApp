using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

internal static partial class AdminDashboardNativeFixture
{
    private static int ChartSeriesCount(FrameworkElement widget, string property)
    {
        var chart = Descendants(widget).FirstOrDefault(e => e.GetType().Name == "DashboardLineChart");
        return chart?.GetType().GetProperty(property)?.GetValue(chart) is System.Collections.IEnumerable values ? values.Cast<object>().Count() : -1;
    }

    private static async Task ConcurrentCorrectionAsync(Window window, Server server)
    {
        server.IncludeMethodSplit = true;
        server.PersistLayout = true;
        var frame = new Frame();
        window.Content = frame;
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Dashboard));
        var shell = (AdminShellPage)frame.Content;
        var inner = Descendants(shell).OfType<Frame>().Single();
        var page = (AdminDashboardPage)inner.Content;
        page.SetHostActivity(visible: true, focused: true);
        await Until(() => page.LayoutSync?.IsLoaded == true);
        page.LayoutSync!.AddWidget("concurrent-streams-24h");
        await Until(() => Descendants(page).Any(e => e.GetType().Name == "ConcurrentStreamsWidget"));
        var widget = Descendants(page).First(e => e.GetType().Name == "ConcurrentStreamsWidget");
        await Task.Delay(300);
        foreach (var width in new[] { 1600, 800 })
        {
            window.AppWindow.ResizeClient(new(width, 1000));
            await Task.Delay(120);
            widget.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await Task.Delay(80);
            Button? Toggle(string name) => Descendants(widget).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b) == name);
            var lines = Toggle("Show each playback method as a line");
            var total = Toggle("Show total streams only");
            Check(lines != null && total != null, $"{width}: concurrent streams offers current saved views");
            if (total != null)
            {
                Invoke(total);
                await Task.Delay(80);
                Check(DashboardLayoutDocument.EffectiveView(page.LayoutSync.Entries.Single(e => e.Id == "concurrent-streams-24h")) == "total", $"{width}: current view updates the placement");
                Check(ChartSeriesCount(widget, "Overlays") == 0 && ChartSeriesCount(widget, "Bands") == 0, $"{width}: Total draws only total streams");
                Invoke(Toggle("Show each playback method as a line")!);
                await Until(() => ChartSeriesCount(widget, "Overlays") == 3);
                Check(ChartSeriesCount(widget, "Overlays") == 3, $"{width}: Lines draws independent method peaks");
                await CaptureAsync(widget, $"concurrent-lines-{width}.png");
                var stacked = Toggle("Show playback methods stacked");
                Check(stacked != null, $"{width}: measured peak-minute split offers Stacked");
                if (stacked != null)
                {
                    Invoke(stacked);
                    await Until(() => ChartSeriesCount(widget, "Bands") >= 3);
                    Check(ChartSeriesCount(widget, "Bands") >= 3, $"{width}: Stacked draws measured methods");
                    await CaptureAsync(widget, $"concurrent-stacked-{width}.png");
                }
            }
            await CaptureAsync((FrameworkElement)widget, $"concurrent-current-{width}.png");
        }
        // An older replica (no capability, no per-point split), then a server that advertises the capability while this window's
        // points carry no split: both draw Lines, hide Stacked, and leave the stored Stacked choice alone.
        var previous = page;
        foreach (var (advertised, label) in new[] { (false, "older replica hides Stacked"), (true, "advertised capability without per-point split hides Stacked") })
        {
            server.IncludeMethodSplit = false;
            server.AdvertiseMethodSplit = advertised;
            previous.LayoutSync!.FlushNow();
            await previous.LayoutSync.PendingWrites;
            // Re-enter with a fresh store, keeping the persisted placement and its Stacked choice.
            frame.Navigate(typeof(Page));
            await Until(() => !previous.IsLoaded);
            frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Dashboard));
            var successor = (AdminDashboardPage)Descendants((AdminShellPage)frame.Content).OfType<Frame>().Single().Content;
            successor.SetHostActivity(visible: true, focused: true);
            await Until(() => successor.LayoutSync?.IsLoaded == true);
            if (!successor.LayoutSync!.Entries.Any(e => e.Id == "concurrent-streams-24h")) successor.LayoutSync.AddWidget("concurrent-streams-24h");
            successor.LayoutSync.SetWidgetView("concurrent-streams-24h", "stacked");
            await Until(() => Descendants(successor).Any(e => e.GetType().Name == "ConcurrentStreamsWidget"));
            var legacy = Descendants(successor).First(e => e.GetType().Name == "ConcurrentStreamsWidget");
            await Until(() => ChartSeriesCount(legacy, "Overlays") == 3);
            await Task.Delay(200);
            Check(ChartSeriesCount(legacy, "Overlays") == 3 && !Descendants(legacy).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Show playback methods stacked"), $"{label} and renders Lines");
            var entry = successor.LayoutSync.Entries.Single(e => e.Id == "concurrent-streams-24h");
            Check(DashboardLayoutDocument.EffectiveView(entry) == "stacked" && entry.View == "stacked", $"{label} without discarding the stored choice (still Stacked after rendering)");
            successor.LayoutSync.FlushNow();
            await successor.LayoutSync.PendingWrites;
            Check(server.StoredLayout?.Contains("\"stacked\"", StringComparison.Ordinal) == true, $"{label}: the server-stored layout still says Stacked");
            previous = successor;
        }
        server.AdvertiseMethodSplit = null;
    }
}
