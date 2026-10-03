using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class RequestDetailActionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-detail-actions.invalid"); client.SetProfile("fixture-profile");
        using var local = new ServiceCollection().AddSingleton(client).AddSingleton(new RequestsApi(client)).AddSingleton(new CatalogApi(client)).AddSingleton(new SettingsApi(client)).AddSingleton(new ToastService()).AddSingleton(new UICustomizationService(new SettingsApi(client))).AddSingleton<WatchlistViewModel>().BuildServiceProvider();
        field.SetValue(null, new Services(local, original));
        var toast = new ToastContainer(); parent.Children.Add(toast); local.GetRequiredService<ToastService>().Register(toast, parent.DispatcherQueue);
        var page = new RequestDetailPage { Width = 500, Height = 800 }; parent.Children.Add(page);
        try
        {
            var features = new RequestFeatureStatus { Allowed = true, RequestsEnabled = true, FollowSupported = true, WatchlistTitlesSupported = true, SeasonRequestsSupported = true, DownloadProgressSupported = true };
            handler.Item.Request.Download = new() { Phase = "downloading", Percent = 42 };
            Set(page, "_features", features); Set(page, "_navigation", new RequestDetailNavigation("series", 41));
            void Render() { Set(page, "_item", handler.Item); Invoke(page, "Render", handler.Item); ((FrameworkElement)page.FindName("LoadingLayer")).Visibility = Visibility.Collapsed; page.UpdateLayout(); }
            Button Action(string id) => ((SiloPlayer.Controls.WrapPanel)page.FindName("ActionsPanel")).Children.OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id);
            Render(); await Task.Delay(100);
            foreach (var action in new[] { "follow", "watchlist" })
            {
                handler.Reject = true; handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); var button = Action(action); Click(button);
                await UntilAsync(() => handler.Active == 1);
                if (button.IsEnabled || button.Content is not ProgressRing { IsActive: true }) throw new InvalidOperationException("Actual request action doesn't expose pending state.");
                await (Task)Invoke(page, "RefreshDownloadAsync")!;
                var refreshed = Action(action);
                if (ReferenceEquals(refreshed, button) || refreshed.IsEnabled || refreshed.Content is not ProgressRing { IsActive: true })
                    throw new InvalidOperationException("Download polling replaces a pending request action with an enabled duplicate-submit target.");
                Invoke(page, action == "follow" ? "Follow_Click" : "Watchlist_Click", refreshed, new RoutedEventArgs());
                await Task.Delay(80);
                if (handler.Active != 1) throw new InvalidOperationException("Rebuilt request action dispatched a duplicate mutation while pending.");
                handler.Gate.SetResult(); await UntilAsync(() => button.IsEnabled);
                if (handler.Item.Request.Following == true || handler.Item.InWatchlist == true || !Descendants(toast).OfType<TextBlock>().Any(text => text.Text.Contains("Fixture rejection"))) throw new InvalidOperationException("Rejected actual request mutation changes state or loses visible feedback.");
                handler.Reject = false; handler.Gate = null; var before = handler.Writes; button = Action(action); Click(button);
                await UntilAsync(() => handler.Writes == before + 1 && AutomationProperties.GetHelpText(Action(action)) == "On");
                Click(Action(action)); await UntilAsync(() => AutomationProperties.GetHelpText(Action(action)) == "Off");
            }
            handler.Item.Request = new() { RequestedByViewer = true, RequestId = "fixture-owned", Status = "pending", State = "pending" }; Render(); await UntilAsync(() => ((SiloPlayer.Controls.WrapPanel)page.FindName("ActionsPanel")).Children.OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "cancel"));
            var cancel = Action("cancel"); Click(cancel); ContentDialog? dialog = null; await UntilAsync(() => (dialog = Dialog(parent)) != null);
            dialog!.Hide(); await Task.Delay(100); if (handler.Cancels != 0) throw new InvalidOperationException("Keeping the request invokes cancellation.");
            handler.Reject = true; Click(cancel); await UntilAsync(() => (dialog = Dialog(parent)) != null); Primary(dialog!); await UntilAsync(() => handler.Cancels == 1 && cancel.IsEnabled);
            if (handler.Item.Request.RequestId != "fixture-owned") throw new InvalidOperationException("Rejected cancellation discards the owned request.");
            handler.Reject = false; handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); Click(cancel); await UntilAsync(() => (dialog = Dialog(parent)) != null); Primary(dialog!); await UntilAsync(() => handler.Active == 1);
            if (cancel.IsEnabled || cancel.Content is not ProgressRing) throw new InvalidOperationException("Actual cancellation permits another request while pending.");
            handler.Gate.SetResult(); await UntilAsync(() => string.IsNullOrWhiteSpace(handler.Item.Request.RequestId) && cancel.IsEnabled && Dialog(parent) == null);
            handler.Gate = null; handler.Item.Request = new() { Requestable = true }; Render();
            var requestButton = Action("request");
            Click(requestButton); await UntilAsync(() => (dialog = Dialog(parent)) != null);
            var toggles = Descendants(dialog!).OfType<ToggleSwitch>().Where(toggle => toggle.Tag is int).ToDictionary(toggle => (int)toggle.Tag);
            if (toggles[3].IsEnabled || !toggles[1].IsEnabled || !toggles[2].IsEnabled) throw new InvalidOperationException("Actual season picker doesn't gate already requested seasons.");
            var all = Descendants(dialog!).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == "All seasons");
            if (all.IsOn) ((IToggleProvider)new ToggleSwitchAutomationPeer(all).GetPattern(PatternInterface.Toggle)).Toggle();
            if (!toggles[2].IsOn) ((IToggleProvider)new ToggleSwitchAutomationPeer(toggles[2]).GetPattern(PatternInterface.Toggle)).Toggle();
            Primary(dialog!); await UntilAsync(() => handler.Seasons != null && Dialog(parent) == null && requestButton.IsEnabled);
            if (!handler.Seasons!.SequenceEqual([2])) throw new InvalidOperationException("Actual mixed-season selection submits all/default/already-requested seasons.");
            Program.Log("PASS: REQUEST_DETAIL_ACTIONS_COMPLETED actual Follow/Watchlist pending/rejection/retry/remove, cancel confirmation/Keep/rejection/pending/retry and exact mixed-season ToggleSwitch submission.");
        }
        finally { handler.Gate?.TrySetResult(); parent.Children.Remove(page); local.GetRequiredService<ToastService>().Unregister(); parent.Children.Remove(toast); await Task.Delay(150); field.SetValue(null, original); }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static ContentDialog? Dialog(StackPanel parent) => VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child))).OfType<ContentDialog>().FirstOrDefault();
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Primary(ContentDialog dialog) => Click(Descendants(dialog).OfType<Button>().Single(button => (string?)button.Content == dialog.PrimaryButtonText));
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Request detail action did not settle."); }
    private sealed class Services(IServiceProvider local, IServiceProvider original) : IServiceProvider { public object? GetService(Type type) => local.GetService(type) ?? original.GetService(type); }
    private sealed class Handler : HttpMessageHandler
    {
        internal RequestMediaDetail Item = new() { MediaType = "series", TmdbId = 41, Title = "Request action fixture", Availability = "missing", Request = new() { Reason = "already_requested", RequestedByViewer = false, Status = "pending", State = "pending" }, Seasons = [new() { SeasonNumber = 1, AirDate = "2020-01-01", EpisodeCount = 8 }, new() { SeasonNumber = 2, AirDate = "2025-01-01", EpisodeCount = 8 }, new() { SeasonNumber = 3, Requested = true, EpisodeCount = 8 }] };
        internal bool Reject; internal int Active, Writes, Cancels; internal List<int>? Seasons; internal TaskCompletionSource? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/v2/requests/detail/")) return Json(Item);
            if (path == "/api/v2/requests/fixture-owned") return Json(new MediaRequest { Id = "fixture-owned", Status = "pending", Outcome = "active" });
            if (request.Method == HttpMethod.Get) throw new InvalidOperationException("Unexpected request action route " + path);
            Writes++; Active++; if (path.EndsWith("/cancel")) Cancels++;
            try
            {
                if (Gate != null) await Gate.Task.WaitAsync(ct);
                if (Reject) return new(HttpStatusCode.Conflict) { Content = new StringContent("{\"title\":\"Fixture rejection\",\"detail\":\"Fixture rejection\"}", Encoding.UTF8, "application/problem+json") };
                if (path.Contains("/follows/")) { Item.Request.Following = request.Method == HttpMethod.Put; return Json(new { }); }
                if (path.Contains("/watchlist/titles/")) { Item.InWatchlist = request.Method == HttpMethod.Put; return Json(new WatchlistTitleEntry()); }
                if (path.EndsWith("/cancel")) { Item.Request = new() { Requestable = true }; return Json(new MediaRequest { Id = "fixture-owned", Status = "cancelled" }); }
                if (path == "/api/v2/requests" && request.Method == HttpMethod.Post) { using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Seasons = body.RootElement.GetProperty("seasons").EnumerateArray().Select(value => value.GetInt32()).ToList(); Item.Request = new() { Status = "pending", State = "pending" }; return Json(new MediaRequest { Id = "fixture-created", Status = "pending" }); }
                throw new InvalidOperationException("Unexpected mutation " + path);
            }
            finally { Active--; }
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
    }
}
