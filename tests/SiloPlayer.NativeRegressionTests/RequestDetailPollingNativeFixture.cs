using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static class RequestDetailPollingNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-detail-poll-fixture.invalid"); client.SetProfile("fixture-profile");
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(new RequestsApi(client)).AddSingleton(new SettingsApi(client)).AddSingleton(new UICustomizationService(new SettingsApi(client))).BuildServiceProvider();
        field.SetValue(null, new ScopedServices(services, (IServiceProvider)previous!)); var pages = new List<RequestDetailPage>();
        try
        {
            RequestDetailPage Create(int? percent = 42)
            {
                var page = new RequestDetailPage { Width = 500, Height = 800 }; pages.Add(page);
                var item = Item(percent);
                Set(page, "_navigation", new RequestDetailNavigation("movie", 41)); Set(page, "_item", item);
                Set(page, "_features", new RequestFeatureStatus { Allowed = true, RequestsEnabled = true, DownloadProgressSupported = true });
                Invoke(page, "Render", item); ((FrameworkElement)page.FindName("LoadingLayer")).Visibility = Visibility.Collapsed;
                parent.Children.Add(page); page.UpdateLayout(); return page;
            }
            var page = Create(); await Task.Delay(100);
            var timer = typeof(RequestDetailPage).GetField("_downloadRefreshTimer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) as DispatcherTimer;
            if (timer?.IsEnabled != true || timer.Interval != TimeSpan.FromSeconds(30)) throw new InvalidOperationException("Mounted external detail doesn't schedule 30-second refreshes while a download exists.");
            timer.Interval = TimeSpan.FromMilliseconds(30); timer.Stop(); timer.Start();
            await UntilAsync(() => handler.Calls == 1); await Task.Delay(100);
            if (handler.Calls != 1 || handler.MaximumActive != 1) throw new InvalidOperationException("Download detail poll overlaps a pending detail request.");
            handler.Delay!.TrySetResult();
            await UntilAsync(() => Progress(page) == 75);
            handler.Fail = true; await UntilAsync(() => handler.Failures > 0);
            if (Progress(page) != 75) throw new InvalidOperationException("Transient detail polling failure clears previously rendered progress.");
            handler.Fail = false; handler.Completed = true;
            await UntilAsync(() => !timer.IsEnabled);
            var completedCalls = handler.Calls; await Task.Delay(100);
            if (handler.Calls != completedCalls) throw new InvalidOperationException("Detail polling continues after the server removes the download.");
            await MediaParityNativeFixture.CaptureAsync(page, "requests-detail-download-completed.png");
            parent.Children.Remove(page);

            handler.Completed = false; handler.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var detached = Create(null); await Task.Delay(100);
            var detachedTimer = (DispatcherTimer)typeof(RequestDetailPage).GetField("_downloadRefreshTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(detached)!;
            if (detachedTimer.IsEnabled) throw new InvalidOperationException("A title without a download schedules polling.");
            // Mutations replace the detail and rebuild its actions without
            // re-rendering metadata or remounting the page.
            var started = Item(42); Set(detached, "_item", started); Invoke(detached, "BuildActions", started);
            if (!detachedTimer.IsEnabled) throw new InvalidOperationException("A download appearing after an action rebuild does not begin polling.");
            detachedTimer.Interval = TimeSpan.FromMilliseconds(30); detachedTimer.Stop(); detachedTimer.Start();
            await UntilAsync(() => handler.Active == 1);
            parent.Children.Remove(detached); await UntilAsync(() => handler.Cancelled > 0 && handler.Active == 0);
            var unloadedCalls = handler.Calls; await Task.Delay(100);
            if (detachedTimer.IsEnabled || handler.Calls != unloadedCalls) throw new InvalidOperationException("Unloaded detail retains its poll owner or creates new requests.");
            Program.Log("PASS: REQUEST_DETAIL_POLLING_COMPLETED 30-second mounted download refresh, mutation activation, no overlap, retained progress on rejection, completion stop and unload cancellation.");
        }
        finally { foreach (var page in pages) parent.Children.Remove(page); field.SetValue(null, previous); }
    }
    private static RequestMediaDetail Item(int? percent) => new() { TmdbId = 41, MediaType = "movie", Title = "Download polling fixture", Request = new() { State = percent == null ? "pending" : "processing", Status = percent == null ? "pending" : "processing", Download = percent == null ? null : new() { Phase = "downloading", Percent = percent } } };
    private static double Progress(RequestDetailPage page) => Descendants((DependencyObject)page.FindName("DownloadProgressHost")).OfType<ProgressBar>().Single().Value;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Request detail poll did not settle."); }
    private sealed class ScopedServices(IServiceProvider local, IServiceProvider previous) : IServiceProvider
    {
        // Retain the outer isolated shared-control authorities for queued
        // PosterCard unloads; every polling/API dependency resolves locally.
        public object? GetService(Type type) => local.GetService(type) ?? previous.GetService(type);
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls, Active, MaximumActive, Failures, Cancelled;
        internal bool Fail, Completed;
        internal TaskCompletionSource? Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.StartsWith("/api/v2/requests/detail/")) throw new InvalidOperationException("Unexpected isolated polling route.");
            Calls++; Active++; MaximumActive = Math.Max(MaximumActive, Active);
            try
            {
                if (Delay != null) await Delay.Task.WaitAsync(ct);
                if (Fail) { Failures++; return new(HttpStatusCode.BadGateway) { Content = new StringContent("{\"title\":\"Fixture rejection\"}", Encoding.UTF8, "application/problem+json") }; }
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Item(Completed ? null : 75), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
            }
            catch (OperationCanceledException) { Cancelled++; throw; }
            finally { Active--; }
        }
    }
}
