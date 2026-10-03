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
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;
using SiloPlayer.ViewModels;

internal static class RequestDetailParityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-detail-fixture.invalid"); client.SetProfile("fixture-profile");
        var api = new RequestsApi(client);
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(client).AddSingleton(new SettingsApi(client)).AddSingleton(new CatalogApi(client))
            .AddSingleton(new ToastService()).AddSingleton(new NavigationService { Frame = new Frame() }).AddSingleton(new UICustomizationService(new SettingsApi(client))).AddSingleton<WatchlistViewModel>().BuildServiceProvider();
        field.SetValue(null, services); var pages = new List<RequestDetailPage>();
        var toast = new ToastContainer(); parent.Children.Add(toast);
        services.GetRequiredService<ToastService>().Register(toast, parent.DispatcherQueue);
        try
        {
            foreach (var width in new[] { 1280d, 900d, 500d })
            {
                var page = new RequestDetailPage { Width = width, Height = 800 }; pages.Add(page); parent.Children.Add(page);
                var item = new RequestMediaDetail { MediaType = "movie", TmdbId = 41, Title = "An External Title With A Long Name", Overview = string.Join(" ", Enumerable.Repeat("A fixture overview to verify description overflow and accessible actions.", 8)), Request = new() { Requestable = true }, Recommendations = [new() { MediaType = "series", TmdbId = 42, Title = "Shared recommendation", Request = new() { Requestable = true } }] };
                Set(page, "_item", item); Set(page, "_features", Features()); Invoke(page, "Render", item);
                ((FrameworkElement)page.FindName("LoadingLayer")).Visibility = Visibility.Collapsed;
                await LayoutAsync(page, width, 800);
                var poster = (FrameworkElement)page.FindName("PosterBorder");
                if (poster.Width != (width < 640 ? 170 : 220) || ((FrameworkElement)page.FindName("AutoRequestExplanation")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("External title hero sizes or visible automatic-request explanation differ.");
                var watchlist = ((WrapPanel)page.FindName("ActionsPanel")).Children.OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "watchlist");
                if (watchlist.Content is not StackPanel || watchlist.Height != 44 || !Descendants(watchlist).OfType<TextBlock>().Any(text => text.Text.Contains("watchlist", StringComparison.OrdinalIgnoreCase)) || ((StackPanel)page.FindName("RecommendationsPanel")).Children.Single() is not Grid)
                    throw new InvalidOperationException("External title secondary actions or recommendations don't use shared controls.");
                await MediaParityNativeFixture.CaptureAsync(page, $"requests-external-detail-{width:0}.png");
                item.Request = new() { Status = "processing", State = "processing", Download = new() { Phase = "downloading", Percent = 42 } };
                Invoke(page, "BuildActions", item);
                var progress = Descendants((FrameworkElement)page.FindName("DownloadProgressHost")).OfType<ProgressBar>().Single();
                if (progress.IsIndeterminate || progress.Value != 42) throw new InvalidOperationException("External title flattens actual download progress into text.");
                await LayoutAsync(page, width, 800);
                var status = ((WrapPanel)page.FindName("ActionsPanel")).Children.OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "request-state");
                var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
                if (status.IsEnabled || status.Background is not SolidColorBrush background || background.Color != accent.Color)
                    throw new InvalidOperationException("Processing state isn't the disabled primary accent action.");
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_REQUEST_DETAIL_ACTION_CASE") == "svg-padding")
                {
                    watchlist = ((WrapPanel)page.FindName("ActionsPanel")).Children.OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "watchlist");
                    if (status.Padding.Left != 12 || status.Padding.Right != 12 || watchlist.Padding.Left != 12 || watchlist.Padding.Right != 12)
                        throw new InvalidOperationException($"Actual icon actions differ from computed official has-svg padding12: Processing={status.Padding.Left}/{status.Padding.Right}, Watchlist={watchlist.Padding.Left}/{watchlist.Padding.Right}.");
                }
                var overview = (TextBlock)page.FindName("OverviewText");
                var download = (StackPanel)page.FindName("DownloadProgressHost");
                var hero = (FrameworkElement)page.FindName("HeroInfo");
                if (Math.Abs(overview.TransformToVisual(hero).TransformPoint(new Windows.Foundation.Point()).X) > 1 || Math.Abs(download.TransformToVisual(hero).TransformPoint(new Windows.Foundation.Point()).X) > 1)
                    throw new InvalidOperationException("Bounded overview or download progress is centered inside the wider hero information column.");
                await MediaParityNativeFixture.CaptureAsync(page, $"requests-external-progress-{width:0}.png");
                parent.Children.Remove(page);
            }
            var series = new RequestMediaDetail { MediaType = "series", TmdbId = 42, Title = "Upcoming external series", Availability = "missing", Request = new() { Requestable = true }, Seasons = [new() { SeasonNumber = 1, AirDate = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"), EpisodeCount = 8 }] };
            handler.Item = series;
            var detail = new RequestDetailPage { Width = 500, Height = 800 }; pages.Add(detail); parent.Children.Add(detail);
            Set(detail, "_item", series); Set(detail, "_features", Features()); Invoke(detail, "Render", series);
            await LayoutAsync(detail, 500, 800);
            var request = ((WrapPanel)detail.FindName("ActionsPanel")).Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Request series");
            handler.FailCreate = true;
            Invoke(detail, "Request_Click", request, new RoutedEventArgs());
            ContentDialog? dialog = null;
            try { await UntilAsync(() => (dialog = OpenDialog(parent)) != null); }
            catch { Program.Log("Request entry diagnostic: enabled=" + request.IsEnabled + "; creates=" + handler.Creates + "; toast=" + string.Join(" | ", Descendants(toast).OfType<TextBlock>().Select(text => text.Text))); throw; }
            if (!dialog!.IsPrimaryButtonEnabled || dialog.PrimaryButtonText != "Request series") throw new InvalidOperationException("Upcoming-only external series cannot request whole series without choosing a season.");
            await MediaParityNativeFixture.CaptureAsync(dialog, "requests-seasons-upcoming-whole-series.png");
            ClickPrimary(dialog);
            await UntilAsync(() => handler.Creates == 1 && dialog.IsPrimaryButtonEnabled && Descendants(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Request failed:")));
            if (!ReferenceEquals(OpenDialog(parent), dialog)) throw new InvalidOperationException("Rejected season request closes the picker and loses selection.");
            await MediaParityNativeFixture.CaptureAsync(dialog, "requests-seasons-rejected.png");
            handler.FailCreate = false; handler.DelayCreate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ClickPrimary(dialog); await UntilAsync(() => handler.Creates == 2);
            if (dialog.IsPrimaryButtonEnabled || !Descendants(dialog).OfType<ProgressRing>().Any(ring => ring.IsActive)) throw new InvalidOperationException("Pending season request doesn't disable submission and show progress.");
            handler.DelayCreate.SetResult();
            await UntilAsync(() => OpenDialog(parent) == null && request.IsEnabled);
            using var submitted = JsonDocument.Parse(handler.Bodies.Last());
            if (submitted.RootElement.TryGetProperty("seasons", out var seasons) && seasons.ValueKind != JsonValueKind.Null) throw new InvalidOperationException("Untouched upcoming external series names seasons instead of leaving the whole-series choice to the server.");
            Program.Log("PASS: actual external-title responsive hero, icon actions, visible auto-request explanation, shared recommendations and real download progress.");
            Program.Log("PASS: actual upcoming whole-series request entry/payload, rejected picker remains open, pending spinner/deduplication and successful retry.");
            await EditablePendingSeasonsAsync(parent);
        }
        finally { services.GetRequiredService<ToastService>().Unregister(); parent.Children.Remove(toast); foreach (var page in pages) parent.Children.Remove(page); field.SetValue(null, previous); }
    }
    private static RequestFeatureStatus Features() => new() { RequestsEnabled = true, Allowed = true, State = "available", SeasonRequestsSupported = true, WatchlistTitlesSupported = true, WatchlistRequests = true, FollowSupported = true, DownloadProgressSupported = true };
    private static async Task EditablePendingSeasonsAsync(StackPanel parent)
    {
        var item = new RequestMediaDetail { Title = "Editable season draft", MediaType = "series", Availability = "partial", Request = new() { Requestable = true },
            Seasons = [new() { SeasonNumber = 1, Availability = "available", AirDate = "2020-01-01", EpisodeCount = 8 },
                new() { SeasonNumber = 2, Availability = "missing", AirDate = "2021-01-01", EpisodeCount = 8 },
                new() { SeasonNumber = 3, Availability = "missing", AirDate = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"), EpisodeCount = 8 }] };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var submissions = new List<List<int>?>();
        var choosing = SiloPlayer.Views.Dialogs.RequestSeasonsDialog.PickAsync(parent.XamlRoot, item, seasons =>
        { submissions.Add(seasons?.ToList()); return gate.Task; });
        ContentDialog? dialog = null;
        try
        {
            await UntilAsync(() => (dialog = OpenDialog(parent)) != null);
            var checks = Descendants(dialog!).OfType<ToggleSwitch>().Where(toggle => toggle.Tag is int).ToArray();
            var second = checks.Single(toggle => (int)toggle.Tag == 2);
            ((IToggleProvider)new ToggleSwitchAutomationPeer(second).GetPattern(PatternInterface.Toggle)).Toggle();
            ((IToggleProvider)new ToggleSwitchAutomationPeer(second).GetPattern(PatternInterface.Toggle)).Toggle();
            ClickPrimary(dialog!); await UntilAsync(() => submissions.Count == 1);
            if (!second.IsEnabled || !submissions[0]!.SequenceEqual(new[] { 2 })) throw new InvalidOperationException("Pending season request disabled an editable choice or lost the submitted draft.");
            var upcoming = Descendants(dialog!).OfType<Button>().Single(button => Equals(button.Content, "Upcoming seasons"));
            ((IInvokeProvider)new ButtonAutomationPeer(upcoming).GetPattern(PatternInterface.Invoke)).Invoke();
            if (!checks.Single(toggle => (int)toggle.Tag == 3).IsOn || second.IsOn || dialog!.IsPrimaryButtonEnabled)
                throw new InvalidOperationException("Pending quick season edit failed, or re-enabled duplicate submission.");
            gate.SetException(new InvalidOperationException("Fixture rejection"));
            await UntilAsync(() => dialog!.IsPrimaryButtonEnabled && Descendants(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Request failed:")));
            if (!checks.Single(toggle => (int)toggle.Tag == 3).IsOn) throw new InvalidOperationException("Rejected season request discarded the edited draft.");
            gate = new(TaskCreationOptions.RunContinuationsAsynchronously); ClickPrimary(dialog!); await UntilAsync(() => submissions.Count == 2);
            if (!submissions[1]!.SequenceEqual(new[] { 3 })) throw new InvalidOperationException("Season retry did not submit the edited draft.");
            var latest = Descendants(dialog!).OfType<Button>().Single(button => Equals(button.Content, "Latest season"));
            ((IInvokeProvider)new ButtonAutomationPeer(latest).GetPattern(PatternInterface.Invoke)).Invoke();
            if (!second.IsOn || dialog!.IsPrimaryButtonEnabled) throw new InvalidOperationException("Latest season edits do not remain enabled under duplicate-submit protection.");
            gate.SetResult(); var result = await choosing;
            if (result?.Seasons == null || !result.Seasons.SequenceEqual(new[] { 3 })) throw new InvalidOperationException("Successful season result describes an unsubmitted newer draft.");
            Program.Log("PASS: actual pending season controls remain editable; quick edits cannot re-enable Submit, rejection retains the newer draft, retry/acknowledgment use the submitted snapshot.");
        }
        finally { gate.TrySetResult(); dialog?.Hide(); await choosing; }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static async Task LayoutAsync(FrameworkElement element, double width, double height) { element.Measure(new Windows.Foundation.Size(width, height)); element.Arrange(new Windows.Foundation.Rect(0, 0, width, height)); element.UpdateLayout(); await Task.Delay(100); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) { var child = VisualTreeHelper.GetChild(root, index); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static ContentDialog? OpenDialog(StackPanel parent) => VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child))).OfType<ContentDialog>().FirstOrDefault();
    private static void ClickPrimary(ContentDialog dialog) { var button = Descendants(dialog).OfType<Button>().Single(button => (string?)button.Content == dialog.PrimaryButtonText); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); }
    private static async Task UntilAsync(Func<bool> ready) { for (var attempt = 0; attempt < 200 && !ready(); attempt++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Native request-detail fixture did not settle."); }
    private sealed class Handler : HttpMessageHandler
    {
        internal RequestMediaDetail Item = new(); internal bool FailCreate; internal int Creates; internal TaskCompletionSource? DelayCreate;
        internal List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/requests" && request.Method == HttpMethod.Post)
            {
                Creates++; Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (DelayCreate != null) await DelayCreate.Task.WaitAsync(cancellationToken);
                if (FailCreate) return new(HttpStatusCode.Conflict) { Content = new StringContent("{\"title\":\"Fixture rejection\"}", Encoding.UTF8, "application/problem+json") };
                Item.Request = new() { Status = "pending", State = "pending" };
                return Json(new MediaRequest { Id = "fixture-created", Status = "pending" });
            }
            if (path.StartsWith("/api/v2/requests/detail/", StringComparison.Ordinal)) return Json(Item);
            throw new InvalidOperationException("Unexpected request-detail fixture route: " + path);
        }
        private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
    }
}
