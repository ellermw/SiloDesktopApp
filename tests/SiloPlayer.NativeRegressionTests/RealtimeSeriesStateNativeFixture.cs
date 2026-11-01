using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class RealtimeSeriesStateNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        var wire = new Wire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://realtime-native.invalid"); client.SetProfile("fixture-profile");
        using var auth = new AuthService(client, new AuthApi(client));
        var catalog = new CatalogApi(client);
        slot.SetValue(null, new Services(original, client, auth, catalog));
        try
        {
            foreach (var type in new[] { "series", "season" })
            {
                wire.Count = 2; wire.Target = "new-target";
                var page = new ItemDetailPage { Width = 900, Height = 720 };
                var vm = page.ViewModel;
                using var navigation = new CancellationTokenSource();
                vm.Item = new() { ContentId = type, Type = type, SeriesId = type == "season" ? "series" : null,
                    Title = "Realtime fixture", EpisodeCount = 4, PlayContentId = type == "season" ? null : "old-target", UserData = new() { UnplayedCount = 4 }, UserState = new() };
                vm.IsSeries = type == "series"; vm.SelectedSeasonNumber = 1;
                vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1, Title = "Season 1", EpisodeCount = 4, UserData = new() { UnplayedCount = 4 } });
                vm.Episodes.Add(new() { ContentId = "sibling", Title = "Untouched sibling", SeasonNumber = 1, EpisodeNumber = 1,
                    Runtime = 20, UserData = new() { PositionSeconds = 31 } });
                Set(page, "_currentContentId", vm.Item.ContentId); Set(page, "_navigationCts", navigation);
                var handler = (PropertyChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .CreateDelegate(typeof(PropertyChangedEventHandler), page);
                vm.PropertyChanged += handler;
                parent.Children.Add(page);
                using var refresh = new RealtimeMediaStateRefresh(client, action => page.DispatcherQueue.TryEnqueue(() => action()));
                try
                {
                    await Task.Delay(60);
                    ((UIElement)page.FindName("ContentScroll")).Visibility = Visibility.Visible;
                    if (type == "series")
                    {
                        ((UIElement)page.FindName("SeasonsSection")).Visibility = Visibility.Visible;
                        ((UIElement)page.FindName("SeasonsScrollViewer")).Visibility = Visibility.Visible;
                        ((UIElement)page.FindName("SeasonsLoadingSkeleton")).Visibility = Visibility.Collapsed;
                    }
                    Call(page, "BuildSeasonCards"); Call(page, "BuildEpisodeRows"); Call(page, "UpdateWatchedButton");
                    // Initial navigation hides a season action when the server
                    // explicitly supplies null, even with loaded episode rows.
                    if (type == "season") ((UIElement)page.FindName("SplitPlayButton")).Visibility = Visibility.Collapsed;
                    var mounted = vm.Item;
                    using var data = JsonDocument.Parse("""
                        {"profile_id":"fixture-profile","content_id":"never-loaded","series_id":"series","change":"watched","played":true}
                        """);
                    refresh.ReceiveRealtime("user_state", "user_state.changed", data.RootElement);
                    await UntilAsync(() => vm.Item.UserData?.WatchedCount == 2);
                    await Task.Delay(100); page.UpdateLayout();
                    Check(ReferenceEquals(mounted, vm.Item), "Realtime refresh replaced the mounted detail.");
                    Check(vm.Item.UserData?.InProgressCount == 1 && !vm.IsWatched, "Authoritative aggregate state was not retained.");
                    Check(vm.Episodes[0].UserData?.Played == false && vm.Episodes[0].UserData?.PositionSeconds == 31,
                        "Unknown episode's scalar reached a sibling.");
                    Check(!Descendants<Grid>((Panel)page.FindName("EpisodesPanel")).Any(grid => AutomationProperties.GetName(grid) == "Watched" && grid.Visibility == Visibility.Visible),
                        "Unknown episode's scalar painted a sibling watched badge.");
                    if (type == "series") Check(Descendants<TextBlock>((Panel)page.FindName("SeasonsPanel")).Any(text => text.Text == "2 of 4 episodes"),
                        "Actual native season card did not repaint authoritative watched count.");
                    if (type == "series") Check(vm.Item.PlayContentId == "new-target" && ((TextBlock)page.FindName("PlayButtonText")).Text == "Resume",
                        "Actual native Resume action did not refresh its authoritative target/label.");
                    if (type == "season")
                    {
                        var action = (UIElement)page.FindName("SplitPlayButton");
                        Check(action.Visibility == Visibility.Visible && vm.Item.PlayContentId == "new-target",
                            "A refreshed playable season target leaves its actual native Play action hidden.");
                        wire.Target = null; wire.Count = 3;
                        refresh.ReceiveRealtime("user_state", "user_state.changed", data.RootElement);
                        await UntilAsync(() => vm.Item.UserData?.WatchedCount == 3);
                        await Task.Delay(100);
                        Check(action.Visibility == Visibility.Collapsed && vm.Item.PlayContentId == null,
                            "An explicit null season target leaves an unusable native Play action visible.");
                        wire.Target = "new-target"; wire.Count = 4;
                        refresh.ReceiveRealtime("user_state", "user_state.changed", data.RootElement);
                        await UntilAsync(() => vm.Item.UserData?.WatchedCount == 4);
                        await Task.Delay(100);
                        Check(action.Visibility == Visibility.Visible, "A season Play action cannot recover after its target returns.");
                    }
                    await MediaParityNativeFixture.CaptureAsync(page, "realtime-state-" + type + ".png");
                    Check(page.CanPreserveOnReconnect, "A healthy mounted detail should survive reconnect.");
                    vm.Seasons.Clear(); vm.Episodes.Clear();
                    wire.HeldCompanion = type == "series" ? "seasons" : "episodes";
                    wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.ReleaseCompanion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var initialCompanion = type == "series"
                        ? vm.LoadSeasonsCommand.ExecuteAsync(null)
                        : (Task)typeof(ItemDetailPage).GetMethod("LoadSeasonEpisodesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(page, ["season", 1, navigation.Token])!;
                    try
                    {
                        await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        Check(!page.CanPreserveOnReconnect, "A held initial companion must remount on reconnect, even with empty rows.");
                        await vm.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(),
                            new HashSet<string>(), CatchUp: true));
                        Check(!page.CanPreserveOnReconnect, "Detail catch-up alone cannot make a pre-reconnect companion safe to preserve.");
                    }
                    finally
                    {
                        wire.HeldCompanion = null;
                        wire.ReleaseCompanion.TrySetResult();
                        await initialCompanion.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    Check(page.CanPreserveOnReconnect, "Settled successful companions should allow preservation again.");
                    vm.IsEpisodesLoading = true;
                    Check(!page.CanPreserveOnReconnect, "A pending selected-season query must remount on reconnect.");
                    vm.IsEpisodesLoading = false;
                    vm.SeasonsLoadFailed = true;
                    Check(!page.CanPreserveOnReconnect, "A failed seasons query must remount on reconnect.");
                    vm.SeasonsLoadFailed = false;
                    vm.EpisodesLoadFailed = true;
                    Check(!page.CanPreserveOnReconnect, "A failed episodes query must remount on reconnect.");
                    vm.EpisodesLoadFailed = false;
                    vm.SimilarLoadFailed = true;
                    Check(!page.CanPreserveOnReconnect, "A failed similar query must remount on reconnect.");
                    vm.SimilarLoadFailed = false;
                    wire.FailEpisodes = true;
                    var loaded = await (Task<bool>)typeof(ItemDetailPage).GetMethod("LoadSeasonEpisodesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(page, ["season", 1, navigation.Token])!;
                    Check(!loaded, "Synthetic companion failure unexpectedly succeeded.");
                    if (type == "season")
                        typeof(ItemDetailPage).GetMethod("ShowSeasonEpisodesFatalError", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [vm.Item]);
                    else
                        ((UIElement)page.FindName("EpisodesLoadError")).Visibility = Visibility.Visible;
                    Check(!page.CanPreserveOnReconnect, "A failed canonical episode row must remount on reconnect, including the season fatal error.");
                    wire.FailEpisodes = false;
                    Program.Log($"PASS: captured unknown-episode event refreshes mounted native {type} aggregate/season counts and leaves sibling state/badge untouched.");
                }
                finally { vm.PropertyChanged -= handler; vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm); parent.Children.Remove(page); }
            }
            foreach (var type in new[] { "movie", "episode" })
            {
                var page = new ItemDetailPage();
                var vm = page.ViewModel;
                vm.Item = new() { ContentId = type, Type = type, UserData = new() { PositionSeconds = 31 } };
                var mountedWatch = new WatchDetailResponse
                    { ContentId = type, Type = type, UserData = new() { PositionSeconds = 31 } };
                Set(page, "_watchDetail", mountedWatch);
                try
                {
                    await vm.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(),
                        new HashSet<string>(), CatchUp: true));
                    Check(vm.Item.UserData?.PositionSeconds == 31 && mountedWatch.UserData.PositionSeconds == 31,
                        "A leaf catch-up must not partially update catalog state while its mounted /watch and resume controls remain unchanged.");
                    Check(!page.CanPreserveOnReconnect,
                        "A playable leaf must remount to refresh its stale /watch position and resume controls.");
                    Set(page, "_watchDetail", null!);
                    ((UIElement)page.FindName("SiblingEpisodesSection")).Visibility = Visibility.Collapsed;
                    Check(!page.CanPreserveOnReconnect,
                        "A missing /watch response or collapsed failed sibling rail must not survive reconnect.");
                }
                finally { vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm); }
            }
        }
        finally { slot.SetValue(null, original); }
    }

    private static async Task UntilAsync(Func<bool> condition)
    { for (var i = 0; i < 250; i++) { if (condition()) return; await Task.Delay(20); } throw new TimeoutException("Native realtime state did not refresh."); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    { if (node is T own) yield return own; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child; }

    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth, CatalogApi catalog) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : type == typeof(CatalogApi) ? catalog :
            type == typeof(SettingsApi) ? new SettingsApi(client) : type == typeof(PlaybackApi) ? new PlaybackApi(client) :
            type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, new ItemDetailPrefetchCache(catalog, auth)) :
            type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : original.GetService(type);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal int Count = 2;
        internal string? Target = "new-target";
        internal bool FailEpisodes;
        internal string? HeldCompanion;
        internal TaskCompletionSource? CompanionStarted;
        internal TaskCompletionSource? ReleaseCompanion;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "realtime-native.invalid" || request.Method != HttpMethod.Get)
                throw new InvalidOperationException("Only synthetic local read fixtures are authorized.");
            var path = request.RequestUri.AbsolutePath;
            if (FailEpisodes && path.EndsWith("/episodes"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    { Content = new StringContent("{\"error\":{\"code\":\"service_unavailable\",\"message\":\"Synthetic companion failure\"}}", Encoding.UTF8, "application/json") });
            object body = path.StartsWith("/api/v2/catalog/items/") ?
                new { content_id = path.Split('/').Last(), type = path.Split('/').Last(),
                    play_content_id = Target, play_season_number = 1,
                    user_data = new { watched_count = Count, unplayed_count = 2, in_progress_count = 1, played = false, position_seconds = 900 }, user_state = new { } } :
                path.EndsWith("/seasons") ? new { items = new[] { new { content_id = "season", season_number = 1, episode_count = 4,
                    user_data = new { watched_count = 2, unplayed_count = 2, in_progress_count = 1, played = false } } } } :
                path.Contains("progress/capabilities") ? new { clear_resume_position = false } : new { items = Array.Empty<object>() };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            if (HeldCompanion is { } companion && path.EndsWith("/" + companion) && ReleaseCompanion is { } release)
            {
                CompanionStarted?.TrySetResult();
                return ReleaseAsync(response, release.Task, ct);
            }
            return Task.FromResult(response);
        }

        private static async Task<HttpResponseMessage> ReleaseAsync(HttpResponseMessage response, Task release, CancellationToken ct)
        {
            try { await release.WaitAsync(ct); return response; }
            catch { response.Dispose(); throw; }
        }
    }
}
