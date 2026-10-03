using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer;

internal static class RequestsLifecycleNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://requests-lifecycle.invalid");
        var outer = App.Services; var api = new RequestsApi(client); var vm = new RequestsViewModel(api);
        var navigation = new NavigationService();
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(vm).AddSingleton(navigation)
            .AddSingleton(new UICustomizationService(new SettingsApi(client))).AddSingleton(new CardOverlayService(new SettingsApi(client)))
            .AddSingleton(outer.GetRequiredService<AuthService>()).AddSingleton(outer.GetRequiredService<SettingsService>())
            .AddSingleton(outer.GetRequiredService<ImageService>()).BuildServiceProvider();
        var field = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        field.SetValue(null, services);
        var frame = new Frame { Width = 900, Height = 800 }; parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            navigation.NavigateImmediately(typeof(RequestsPage)); var page = (RequestsPage)frame.Content;
            await Until(() => page.IsLoaded && vm.MyRequests.Count == 1 && vm.Genres.Count == 1);
            Invoke((Button)page.FindName("YoursTabButton"));
            await Until(() => ((FrameworkElement)page.FindName("MyRequestsSection")).Visibility == Visibility.Visible);
            if (!vm.IsLoadingDiscovery || vm.IsLoadingMine || vm.IsLoading)
                throw new InvalidOperationException("Actual Requests hides ready account rows behind pending discovery.");
            await MediaParityNativeFixture.CaptureAsync(page, "requests-independent-yours.png");
            Invoke((Button)page.FindName("DiscoverTabButton"));
            if (!((StackPanel)page.FindName("GenresPanel")).Children.Any() || ((FrameworkElement)page.FindName("DiscoverySection")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Actual independent brand result is hidden behind pending discovery.");
            await MediaParityNativeFixture.CaptureAsync(page, "requests-independent-discovery.png");
            var timer = (DispatcherTimer)typeof(RequestsPage).GetField("_downloadRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            if (!timer.IsEnabled || timer.Interval != TimeSpan.FromSeconds(30)) throw new InvalidOperationException("Mounted download timer lacks the WebUI30-second interval.");
            timer.Interval = TimeSpan.FromMilliseconds(50);
            await Until(() => wire.MineReads >= 2 && vm.MyRequests[0].Download?.Percent == 78);
            timer.Stop();
            if (!vm.IsLoadingDiscovery) throw new InvalidOperationException("Fixture failed to keep independent discovery pending during live account polling.");
            wire.Discovery.SetResult(Response("{\"items\":[{\"key\":\"trending_movies\",\"title\":\"Trending movies\",\"results\":[{\"media_type\":\"movie\",\"tmdb_id\":42,\"title\":\"Retained rail\",\"request\":{\"requestable\":true}}]}]}"));
            await Until(() => !vm.IsLoadingDiscovery && ((StackPanel)page.FindName("DiscoveryList")).Children.Count == 1);
            var rail = ((StackPanel)page.FindName("DiscoveryList")).Children[0];
            var before = wire.MineReads;
            var original = vm.MyRequests[0]; wire.PendingMine = new(TaskCreationOptions.RunContinuationsAsynchronously);
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Start();
            await Until(() => wire.MineReads == before + 1); await Task.Delay(150);
            if (wire.MineReads != before + 1) throw new InvalidOperationException("Mounted refresh overlaps an in-flight account request.");
            timer.Stop();
            wire.PendingMine.SetResult(new(HttpStatusCode.ServiceUnavailable)); wire.PendingMine = null;
            await Until(() => !(bool)typeof(RequestsPage).GetField("_downloadTickInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
            if (!ReferenceEquals(original, vm.MyRequests[0]) || vm.MineError != null)
                throw new InvalidOperationException("Mounted failed background refresh drops the last successful account rows.");
            timer.Start();
            await Until(() => wire.MineReads >= before + 2 && vm.MyRequests[0].Download?.Percent == 78);
            if (!ReferenceEquals(rail, ((StackPanel)page.FindName("DiscoveryList")).Children[0]))
                throw new InvalidOperationException("Account download refresh rebuilds the unrelated discovery carousel.");
            wire.Finished = true;
            await Until(() => !vm.HasDownloadingRequests);
            var last = wire.MineReads; await Task.Delay(160);
            if (last != wire.MineReads) throw new InvalidOperationException("Mounted refresh continues querying after download completion.");
            navigation.NavigateImmediately(typeof(Page)); await Task.Delay(50);
            if (timer.IsEnabled || typeof(RequestsPage).GetField("_downloadRefreshOwner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page) != null)
                throw new InvalidOperationException("Navigating away leaves Requests polling ownership active.");
            Program.Log("PASS: REQUESTS_LIFECYCLE_COMPLETED independent mine/discovery/brand rendering, mounted30-second polling, no overlap, rejection recovery, retained carousel, completion and navigation stop.");
        }
        finally
        {
            wire.Discovery.TrySetResult(Response("{\"items\":[]}")); wire.PendingMine?.TrySetResult(Response("{\"items\":[]}"));
            frame.Navigate(typeof(Page)); parent.Children.Remove(frame); navigation.Frame = null;
            await Task.Delay(100); field.SetValue(null, outer);
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready)
    { var end = DateTime.UtcNow.AddSeconds(5); while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException("Requests lifecycle did not settle."); await Task.Delay(15); } }
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<HttpResponseMessage> Discovery = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<HttpResponseMessage>? PendingMine;
        internal int MineReads; internal bool Finished;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "requests-lifecycle.invalid") throw new InvalidOperationException("Unexpected remote Requests fixture request.");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/discover")) return await Discovery.Task.WaitAsync(ct);
            if (path.EndsWith("/mine"))
            {
                MineReads++; if (PendingMine is { } pending) return await pending.Task.WaitAsync(ct);
                return Response("{\"items\":[{\"id\":\"mine\",\"title\":\"Live download\",\"status\":\"processing\"" +
                    (Finished ? "" : ",\"download\":{\"phase\":\"downloading\",\"percent\":" + (MineReads == 1 ? "42" : "78") + "}") + "}]}");
            }
            return Response(path.EndsWith("/status") ? "{\"requests_enabled\":true}" : path.EndsWith("/genres")
                ? "{\"items\":[{\"slug\":\"drama\",\"display_name\":\"Drama\"}]}" : "{\"items\":[]}");
        }
    }
}
