using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// Checks the actual ui_lag.txt timer during a block just after Start on Favorites and during a
/// slow Frame navigation away from Favorites. Both use a synthetic server and the result directory.
/// Dispatch: SILO_NATIVE_TEST_ONLY=ui-stall-log.
/// </summary>
internal static class UiStallNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://ui-stall.invalid");
        var catalog = new CatalogApi(client);
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(CatalogApi)] = () => catalog, [typeof(HttpClient)] = () => http,
            [typeof(FavoritesViewModel)] = () => new FavoritesViewModel(catalog),
        }));
        var frame = new Frame { Width = 1000, Height = 700 }; parent.Children.Add(frame);
        var navigation = new NavigationService { Frame = frame };
        var monitor = new UiStallMonitor(UiStallLog.Shared, () => frame, () => "idle");
        var path = Path.Combine(Program.ResultDirectory, "ui_lag.txt");
        try
        {
            var before = Lines(path).Count;
            navigation.Navigate(typeof(FavoritesPage));
            await Until(() => frame.Content is FavoritesPage { IsLoaded: true }, "the Favorites page did not load");
            await Task.Delay(300);
            var favorites = new UiStallLog.Location(nameof(FavoritesPage));
            UiStallLog.Shared.RecordOperation("before-start", 20_000, favorites);
            monitor.Start();
            // The first tick must not report the much longer operation from before monitoring.
            using (UiStallLog.Shared.Measure("synthetic-block", favorites)) Thread.Sleep(950);
            await Task.Delay(700);
            string? line = null;
            await Until(() => (line = Lines(path).Skip(before).LastOrDefault(text => text.Contains("longest_op=synthetic-block"))) != null,
                "no ui_lag.txt line named the synthetic block");
            var stall = line![line.IndexOf("delay_ms=", StringComparison.Ordinal)..];
            Check(stall.Contains("| page=FavoritesPage |") && stall.Contains("| admin=- |") && stall.Contains("| last=navigate:FavoritesPage ")
                && stall.Contains("| playback=idle |") && stall.Contains("| op_owner=FavoritesPage |") && stall.Contains("breadcrumb="),
                "the stall line must name the sampled page, the operation owner, the navigation and playback: " + stall);
            var delay = long.Parse(stall["delay_ms=".Length..stall.IndexOf(' ')]);
            Check(delay >= 900, $"the stall line must carry the raw tick gap: {delay}ms");
            Program.Log("PASS: a synthetic interface block on the Favorites page wrote: " + stall);

            var beforeNavigation = Lines(path).Count;
            Microsoft.UI.Xaml.Navigation.NavigatingCancelEventHandler blockNavigation = (_, _) => Thread.Sleep(950);
            frame.Navigating += blockNavigation;
            try { Check(navigation.Navigate<Page>("fixture-route-argument"), "slow navigation was refused"); }
            finally { frame.Navigating -= blockNavigation; }
            string? transition = null;
            await Until(() => (transition = Lines(path).Skip(beforeNavigation).LastOrDefault(text =>
                text.Contains("| op_owner=FavoritesPage | op_target=Page |"))) != null, "no ui_lag.txt line named the slow transition");
            var age = System.Text.RegularExpressions.Regex.Match(transition!, @"\| last=navigate:Page (\d+)ms ago \|");
            Check(transition!.Contains("| page=Page |") && age.Success && long.Parse(age.Groups[1].Value) >= 900 &&
                !transition.Contains("fixture-route-argument"), "the transition must name both pages and the start age without route arguments: " + transition);
            Program.Log("PASS: a slow Frame navigation wrote: " + transition);
        }
        finally
        {
            monitor.Stop();
            parent.Children.Remove(frame); await Task.Delay(100);
            field.SetValue(null, previous);
        }
    }

    internal static List<string> Lines(string path)
    {
        if (!File.Exists(path)) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } text) lines.Add(text);
        return lines;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("UI stall log: " + message); }
    private static async Task Until(Func<bool> ready, string message)
    {
        for (var attempt = 0; attempt < 200; attempt++) { if (ready()) return; await Task.Delay(25); }
        if (!ready()) throw new TimeoutException("UI stall log: " + message);
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider
    { public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "ui-stall.invalid") throw new InvalidOperationException("UI stall fixture attempted external network.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"items\":[],\"total\":0,\"total_exact\":true,\"page\":{\"has_more\":false}}", System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
