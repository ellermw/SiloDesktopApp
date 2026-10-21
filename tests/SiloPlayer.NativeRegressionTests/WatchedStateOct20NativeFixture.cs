using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class WatchedStateOct20NativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://watched-native-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        var catalog = new CatalogApi(client);
        slot.SetValue(null, new Services(original, client, auth, catalog));
        try
        {
            foreach (var type in new[] { "series", "season", "episode" })
                await VerifyAsync(parent, type, wire);
        }
        finally { slot.SetValue(null, original); }
    }

    private static async Task VerifyAsync(StackPanel parent, string type, Wire wire)
    {
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        var vm = page.ViewModel;
        var handler = (PropertyChangedEventHandler)typeof(ItemDetailPage)
            .GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(PropertyChangedEventHandler), page);
        using var navigation = new CancellationTokenSource();
        vm.Item = new MediaItemDetail { ContentId = "fixture-" + type, Type = type, Title = "Watched fixture",
            EpisodeCount = type == "series" ? 4 : 2, UserData = new() { UnplayedCount = type == "series" ? 4 : 2 }, UserState = new() };
        vm.IsSeries = type == "series";
        vm.SelectedSeasonNumber = 1;
        if (type == "series") vm.Seasons.Add(new Season { ContentId = "fixture-season", SeasonNumber = 1,
            Title = "Season 1", EpisodeCount = 2, UserData = new() { UnplayedCount = 2 } });
        if (type == "series") vm.Seasons.Add(new Season { ContentId = "fixture-season-2", SeasonNumber = 2,
            Title = "Season 2", EpisodeCount = 2, UserData = new() { UnplayedCount = 2 } });
        if (type != "episode")
            for (var i = 1; i <= 2; ++i) vm.Episodes.Add(new Episode { ContentId = "fixture-episode-" + i,
                Title = "Episode " + i, SeasonNumber = 1, EpisodeNumber = i, Runtime = 20, UserData = new() });
        Set(page, "_currentContentId", vm.Item.ContentId); Set(page, "_navigationCts", navigation);
        vm.PropertyChanged += handler;
        parent.Children.Add(page);
        try
        {
            await Task.Delay(60);
            ((UIElement)page.FindName("ContentScroll")).Visibility = Visibility.Visible;
            if (type == "series")
            {
                // Navigation reveals the season rail only for multi-season
                // series. This fixture seeds the real builders directly.
                ((UIElement)page.FindName("SeasonsSection")).Visibility = Visibility.Visible;
                ((UIElement)page.FindName("SeasonsScrollViewer")).Visibility = Visibility.Visible;
                ((UIElement)page.FindName("SeasonsLoadingSkeleton")).Visibility = Visibility.Collapsed;
            }
            Call(page, "BuildSeasonCards"); Call(page, "BuildEpisodeRows"); Call(page, "UpdateWatchedButton");
            page.UpdateLayout();
            var mounted = vm.Item;
            var startWrites = wire.Writes;
            Invoke((Button)page.FindName("WatchedButton"));
            await WaitAsync(() => wire.Writes == startWrites + 1 && vm.ToggleWatchedCommand.ExecutionTask?.IsCompleted == true);
            await Task.Delay(120); page.UpdateLayout();
            Check(ReferenceEquals(mounted, vm.Item), "Watched action must preserve the mounted page model.");
            Check(vm.IsWatched && vm.Item.UserData!.Played, "Successful native watched action must update detail user data.");
            Check(((TextBlock)page.FindName("WatchedText")).Text.Contains("Unwatched"), "Native header must offer the reverse action.");
            if (type != "episode")
            {
                Check(vm.Episodes.All(e => e.UserData?.Played == true), "Successful container mark must update every loaded episode.");
                Check(WatchedBadges(page) == 2, "Both native episode check badges must repaint without navigation.");
            }
            if (type == "series")
            {
                Check(vm.Seasons.All(s => s.UserData?.Played == true), "Accepted mark must update both season models.");
                Check(SeasonHasCheck(page), "Season completion check must repaint without navigation.");
            }
            await MediaParityNativeFixture.CaptureAsync(page, "oct20-watched-" + type + ".png");

            Invoke((Button)page.FindName("WatchedButton"));
            await WaitAsync(() => wire.Writes == startWrites + 2 && vm.ToggleWatchedCommand.ExecutionTask?.IsCompleted == true);
            await Task.Delay(100);
            Check(!vm.IsWatched && vm.Item.UserData?.Played == false && WatchedBadges(page) == 0,
                "Native unwatched action must remove header/episode completion state.");
            if (type == "series") Check(!SeasonHasCheck(page), "Native unwatched action must remove season completion check.");

            wire.Reject = true;
            Invoke((Button)page.FindName("WatchedButton"));
            await WaitAsync(() => wire.Writes == startWrites + 3 && vm.ToggleWatchedCommand.ExecutionTask?.IsCompleted == true);
            await Task.Delay(80); wire.Reject = false;
            Check(!vm.IsWatched && vm.Item.UserData?.Played == false && WatchedBadges(page) == 0,
                "Rejected native mark must roll back the action and preserve descendant states.");

            if (type == "series")
            {
                var episodes = (Panel)page.FindName("EpisodesPanel");
                var quick = Descendants<Button>(episodes).First(button => AutomationProperties.GetName(button) == "Mark Episode Watched");
                Invoke(quick);
                await WaitAsync(() => wire.Writes == startWrites + 4 && vm.Episodes[0].UserData?.Played == true);
                await Task.Delay(100);
                Check(vm.Seasons[0].UserData?.WatchedCount == 1 && !vm.IsWatched, "Child quick mark must update partial season count without completing parent.");
                Check(Descendants<TextBlock>((Panel)page.FindName("SeasonsPanel")).Any(text => text.Text == "1 of 2 episodes"),
                    "Partial watched count must repaint on the actual native season card.");
                Check(WatchedBadges(page) == 1, "Child quick mark must repaint exactly one episode badge.");
                await MediaParityNativeFixture.CaptureAsync(page, "oct20-watched-series-partial.png");
            }
            Program.Log($"PASS: October20 native {type} mark/unmark/rejection updates mounted detail, header and descendant badges/counts.");
        }
        finally
        {
            vm.PropertyChanged -= handler;
            WeakReferenceMessenger.Default.UnregisterAll(vm);
            parent.Children.Remove(page);
        }
    }

    private static int WatchedBadges(ItemDetailPage page) => Descendants<Grid>((Panel)page.FindName("EpisodesPanel"))
        .Count(border => AutomationProperties.GetName(border) == "Watched" && border.Visibility == Visibility.Visible);
    private static bool SeasonHasCheck(ItemDetailPage page) => Descendants<FontIcon>((Panel)page.FindName("SeasonsPanel")).Any(icon => icon.Glyph == "\uE73E");
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static async Task WaitAsync(Func<bool> condition)
    { for (var i = 0; i < 100; ++i) { if (condition()) return; await Task.Delay(20); } throw new TimeoutException("Native watched operation did not complete."); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    { if (node is T own) yield return own; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); ++i) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth, CatalogApi catalog) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : type == typeof(CatalogApi) ? catalog :
            type == typeof(SettingsApi) ? new SettingsApi(client) : type == typeof(PlaybackApi) ? new PlaybackApi(client) :
            type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, new ItemDetailPrefetchCache(catalog, auth)) :
            type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal int Writes; internal bool Reject;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "watched-native-fixture.invalid") throw new InvalidOperationException("Unexpected network origin.");
            if (request.Method != HttpMethod.Get)
            {
                Check(request.RequestUri.AbsolutePath.StartsWith("/api/v2/watched/"), "Only fixture watched writes are authorized.");
                ++Writes;
                return Task.FromResult(new HttpResponseMessage(Reject ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent));
            }
            object body = request.RequestUri.AbsolutePath.Contains("progress/capabilities") ? new { clear_resume_position = false } :
                request.RequestUri.AbsolutePath.Contains("not-interested") ? new { state = "available" } : new { items = Array.Empty<object>() };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
        }
    }
}
