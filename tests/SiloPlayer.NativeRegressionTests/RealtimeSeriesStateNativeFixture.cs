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
                wire.Count = 2; wire.Target = "new-target"; wire.LoadedPlayed = false; wire.LoadedPosition = 0; wire.Favorite = false;
                var page = new ItemDetailPage { Width = 900, Height = 720 };
                var vm = page.ViewModel;
                using var navigation = new CancellationTokenSource();
                vm.Item = new() { ContentId = type, Type = type, SeriesId = type == "season" ? "series" : null,
                    Title = "Realtime fixture", EpisodeCount = 4, SeasonCount = 2, PlayContentId = type == "season" ? null : "old-target", UserData = new() { UnplayedCount = 4 }, UserState = new() };
                vm.IsSeries = type == "series"; vm.SelectedSeasonNumber = 1;
                vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1, Title = "Season 1", EpisodeCount = 4, UserData = new() { UnplayedCount = 4 } });
                vm.Episodes.Add(new() { ContentId = "sibling", Title = "Untouched sibling", SeasonNumber = 1, EpisodeNumber = 1,
                    Runtime = 20, UserData = new() { PositionSeconds = 31 } });
                vm.Episodes.Add(new() { ContentId = "loaded", Title = "Loaded episode", SeasonNumber = 1, EpisodeNumber = 2,
                    Runtime = 20, UserData = new() { PositionSeconds = 0, DurationSeconds = 1200 } });
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
                    page.UpdateLayout();
                    await Task.Delay(100); // Settle initial responsive reflow before retaining card identities.
                    // Initial navigation hides a season action when the server
                    // explicitly supplies null, even with loaded episode rows.
                    if (type == "season") ((UIElement)page.FindName("SplitPlayButton")).Visibility = Visibility.Collapsed;
                    var mounted = vm.Item;
                    var episodePanel = (Panel)page.FindName("EpisodesPanel");
                    var mountedCards = episodePanel.Children.ToArray();
                    var loadedButton = Descendants<Button>(episodePanel).Single(button => button.Tag as string == "loaded");
                    var mountedFlyout = loadedButton.ContextFlyout;
                    var watchedAction = ((MenuFlyout)mountedFlyout).Items.OfType<MenuFlyoutItem>()
                        .Single(action => action.Text == "Mark Watched");
                    var mountedCaption = Descendants<FrameworkElement>(loadedButton).Single(element => element.Name == "EpisodeCardCaption");
                    await VerifyProgressActionsAsync((MenuFlyout)mountedFlyout, loadedButton, restart: false, reset: false);
                    var seasonPanel = (Panel)page.FindName("SeasonsPanel");
                    var mountedSeasonCard = seasonPanel.Children[0];
                    using var data = JsonDocument.Parse("""
                        {"profile_id":"fixture-profile","content_id":"never-loaded","series_id":"series","change":"watched","played":true}
                        """);
                    refresh.ReceiveRealtime("user_state", "user_state.changed", data.RootElement);
                    await UntilAsync(() => vm.Item.UserData?.WatchedCount == 2);
                    await Task.Delay(100); page.UpdateLayout();
                    Check(ReferenceEquals(mounted, vm.Item), "Realtime refresh replaced the mounted detail.");
                    Check(mountedCards.SequenceEqual(episodePanel.Children), "Aggregate-only refresh destroyed mounted episode cards.");
                    Check(ReferenceEquals(mountedSeasonCard, seasonPanel.Children[0]), "State refresh destroyed the mounted season card/poster.");
                    Check(vm.Item.EpisodeCount == 6, "Realtime refresh discarded the authoritative episode total.");
                    if (type == "series")
                    {
                        Check(vm.Item.SeasonCount == 1 && ((TextBlock)page.FindName("SeasonCountText")).Text == "1 SEASON",
                            "Mounted series season-count badge did not refresh.");
                        Check(((TextBlock)page.FindName("EpisodeCountText")).Text == "6 EPISODES",
                            "Mounted series episode-count badge did not refresh.");
                    }
                    else Check(((TextBlock)page.FindName("RuntimeText")).Text == "6 EPISODES",
                        "Mounted season episode-count badge did not refresh.");
                    Check(vm.Item.UserData?.InProgressCount == 1 && !vm.IsWatched, "Authoritative aggregate state was not retained.");
                    Check(vm.Episodes[0].UserData?.Played == false && vm.Episodes[0].UserData?.PositionSeconds == 31,
                        "Unknown episode's scalar reached a sibling.");
                    Check(!Descendants<Grid>((Panel)page.FindName("EpisodesPanel")).Any(grid => AutomationProperties.GetName(grid) == "Watched" && grid.Visibility == Visibility.Visible),
                        "Unknown episode's scalar painted a sibling watched badge.");
                    if (type == "series") Check(Descendants<TextBlock>((Panel)page.FindName("SeasonsPanel")).Any(text => text.Text == "2 of 6 episodes"),
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
                    var loadedEpisode = vm.Episodes.Single(episode => episode.ContentId == "loaded");
                    Check(!HasWatchedBadge(page, "loaded"), "The loaded episode was already painted watched before its remote event.");
                    wire.LoadedPosition = 600;
                    using var progressData = JsonDocument.Parse("""
                        {"profile_id":"fixture-profile","content_id":"loaded","series_id":"series","change":"progress"}
                        """);
                    refresh.ReceiveRealtime("user_state", "user_state.changed", progressData.RootElement);
                    await UntilAsync(() => loadedEpisode.UserData?.PositionSeconds == 600);
                    await VerifyProgressActionsAsync((MenuFlyout)mountedFlyout, loadedButton, restart: true, reset: true);
                    Check(ReferenceEquals(mountedFlyout, loadedButton.ContextFlyout) && mountedCards.SequenceEqual(episodePanel.Children),
                        "Gaining partial progress replaced the retained card or context flyout.");
                    var beforeReconnect = client.CaptureContext();
                    var reconnectContext = client.InvalidateAccessContext();
                    Check(!client.IsCurrentContext(beforeReconnect), "Reconnect did not retire the menu's original request context.");
                    await vm.RefreshMediaStateAsync(new(reconnectContext, new HashSet<string>(), new HashSet<string>(), CatchUp: true));
                    await VerifyProgressActionsAsync((MenuFlyout)mountedFlyout, loadedButton, restart: true, reset: true);
                    Check(ReferenceEquals(mounted, vm.Item) && ReferenceEquals(mountedFlyout, loadedButton.ContextFlyout)
                        && mountedCards.SequenceEqual(episodePanel.Children),
                        "Reconnect must keep Reset Progress available without replacing the mounted detail, episode cards or flyout.");
                    wire.LoadedPlayed = true; wire.LoadedPosition = 0;
                    using var loadedData = JsonDocument.Parse("""
                        {"profile_id":"fixture-profile","content_id":"loaded","series_id":"series","change":"watched","played":true}
                        """);
                    refresh.ReceiveRealtime("user_state", "user_state.changed", loadedData.RootElement);
                    await UntilAsync(() => loadedEpisode.UserData?.Played == true && HasWatchedBadge(page, "loaded"));
                    Check(ReferenceEquals(loadedEpisode, vm.Episodes.Single(episode => episode.ContentId == "loaded")),
                        "Realtime refresh replaced the retained episode instead of updating its state.");
                    Check(!HasWatchedBadge(page, "sibling") && vm.Episodes[0].UserData?.PositionSeconds == 31,
                        "A loaded episode's remote change repainted an unrelated sibling as watched.");
                    Check(mountedCards.SequenceEqual(episodePanel.Children), "Episode state refresh destroyed focusable cards.");
                    Check(ReferenceEquals(mountedFlyout, loadedButton.ContextFlyout) &&
                        ReferenceEquals(mountedCaption, Descendants<FrameworkElement>(loadedButton).Single(element => element.Name == "EpisodeCardCaption")),
                        "Episode refresh replaced the mounted flyout or caption instead of patching state in place.");
                    mountedFlyout.ShowAt(loadedButton);
                    try
                    {
                        await UntilAsync(() => watchedAction.Text == "Mark Unwatched");
                        Check(AutomationProperties.GetName(watchedAction) == "Mark Unwatched"
                            && watchedAction.Icon is FontIcon { Glyph: "\uE711" },
                            "The retained episode menu did not refresh its watched label, accessible name and icon on opening.");
                        var actions = ((MenuFlyout)mountedFlyout).Items.OfType<MenuFlyoutItem>();
                        Check(actions.Any(action => action.Text == "Play from Beginning" && action.Visibility == Visibility.Visible)
                            && !actions.Any(action => action.Text == "Reset Progress" && action.Visibility == Visibility.Visible),
                            "A watched episode with no resume position must offer restart but not Reset Progress.");
                    }
                    finally { mountedFlyout.Hide(); }
                    wire.LoadedPlayed = false;
                    refresh.ReceiveRealtime("user_state", "user_state.changed", loadedData.RootElement);
                    await UntilAsync(() => loadedEpisode.UserData?.Played == false && !HasWatchedBadge(page, "loaded"));
                    await VerifyProgressActionsAsync((MenuFlyout)mountedFlyout, loadedButton, restart: false, reset: false);
                    if (type == "series")
                    {
                        vm.Seasons.Add(new() { ContentId = "second-season", SeasonNumber = 2 });
                        Call(page, "ApplyEpisodeSpoilers");
                        Call(page, "LayoutEpisodeGrid");
                        Check(mountedCards.SequenceEqual(episodePanel.Children), "Hidden auto-selected episode rows were rebuilt on a multi-season series.");
                        vm.Seasons.RemoveAt(1);
                    }
                    await MediaParityNativeFixture.CaptureAsync(page, "realtime-state-" + type + ".png");
                    page.Width = 1200;
                    page.UpdateLayout();
                    await Task.Delay(100);
                    if (type == "series") Check(((UIElement)page.FindName("FavoriteButton")).Visibility == Visibility.Visible,
                        "The wide-layout series hero Favorite button must be visible for catch-up verification.");
                    Call(page, "UpdateFavoriteButton");
                    var favoriteIcon = (FontIcon)page.FindName("FavoriteIcon");
                    var outlineHeart = favoriteIcon.Glyph;
                    wire.Favorite = true;
                    refresh.CatchUp(client.CaptureContext());
                    await UntilAsync(() => vm.IsFavorite && favoriteIcon.Glyph != outlineHeart);
                    Check(ReferenceEquals(mounted, vm.Item), "Favorite catch-up replaced the mounted detail.");
                    wire.Favorite = false;
                    refresh.CatchUp(client.CaptureContext());
                    await UntilAsync(() => !vm.IsFavorite && favoriteIcon.Glyph == outlineHeart);
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
                        wire.Count = 5; wire.LoadedPlayed = false; wire.LoadedPosition = 600;
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
                    Check(vm.Episodes.Single(episode => episode.ContentId == "loaded").UserData is { Played: false, PositionSeconds: 600 },
                        "A remote invalidation during the initial episode load left its pre-event row state mounted.");
                    if (type == "series") Check(vm.Seasons.Single().UserData?.WatchedCount == 5,
                        "A remote invalidation during the initial seasons load left its pre-event count mounted.");
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
            await VerifyLegacySeriesActionAsync(parent, wire, client);
            foreach (var type in new[] { "series", "season" })
                await VerifyTranslationReconnectAsync(parent, wire, client, type);
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

    private static async Task VerifyLegacySeriesActionAsync(StackPanel parent, Wire wire, SiloApiClient client)
    {
        wire.OmitTarget = true; wire.Count = 0; wire.Favorite = false;
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        var vm = page.ViewModel;
        using var navigation = new CancellationTokenSource();
        vm.Item = JsonSerializer.Deserialize<MediaItemDetail>("""
            {"content_id":"series","type":"series","title":"Legacy series","user_data":{"unplayed_count":6}}
            """, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        vm.IsSeries = true;
        vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1, EpisodeCount = 6,
            UserData = new() { UnplayedCount = 6 } });
        Set(page, "_currentContentId", "series"); Set(page, "_navigationCts", navigation);
        var handler = (PropertyChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(PropertyChangedEventHandler), page);
        vm.PropertyChanged += handler;
        parent.Children.Add(page);
        var apply = typeof(ItemDetailPage).GetMethod("ApplySeriesActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolve = typeof(ItemDetailPage).GetMethod("ResolvePlayableContentIdForPlaybackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            await (Task)apply.Invoke(page, null)!;
            Check(!vm.Item.HasAuthoritativePlayTarget && vm.Episodes.Count == 2,
                "The legacy action did not load its season episodes, or invented an authoritative target.");
            Check(((TextBlock)page.FindName("PlayButtonText")).Text == "Start From Episode 1"
                && ((Button)page.FindName("PrimaryPlayButton")).IsEnabled
                && ((UIElement)page.FindName("SplitPlayButton")).Visibility == Visibility.Visible,
                "An omitted legacy play target disabled the initial series fallback action.");
            Check(await (Task<string?>)resolve.Invoke(page, null)! == "sibling",
                "Legacy series playback did not resolve the fallback episode.");

            wire.Count = 1;
            await vm.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(["loaded"]),
                new HashSet<string>(["series"])));
            await UntilAsync(() => ((TextBlock)page.FindName("PlayButtonText")).Text == "Play Latest");
            Check(!vm.Item.HasAuthoritativePlayTarget && ((Button)page.FindName("PrimaryPlayButton")).IsEnabled,
                "A legacy realtime response disabled the seasons-derived series action.");
            Check(await (Task<string?>)resolve.Invoke(page, null)! == "loaded",
                "The refreshed legacy action did not advance its fallback episode.");

            wire.OmitTarget = false; wire.Target = null;
            await vm.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(),
                new HashSet<string>(["series"])));
            await UntilAsync(() => ((TextBlock)page.FindName("PlayButtonText")).Text == "Browse Series");
            Check(!((Button)page.FindName("PrimaryPlayButton")).IsEnabled
                && await (Task<string?>)resolve.Invoke(page, null)! == null,
                "An explicit null target incorrectly reused the legacy fallback episode.");
            Program.Log("PASS: omitted legacy series targets retain enabled initial/realtime fallback actions; explicit null still disables playback.");
        }
        finally
        {
            wire.OmitTarget = false;
            vm.PropertyChanged -= handler; vm.CancelPendingLoads();
            WeakReferenceMessenger.Default.UnregisterAll(vm); parent.Children.Remove(page);
        }
    }

    private static async Task VerifyTranslationReconnectAsync(StackPanel parent, Wire wire, SiloApiClient client, string type)
    {
        foreach (var phase in new[] { "status", "polling", "auto" })
        {
            var page = new ItemDetailPage { Width = 900, Height = 720 };
            var vm = page.ViewModel;
            using var navigation = new CancellationTokenSource();
            vm.Item = new() { ContentId = type, Type = type, Title = "Translation reconnect fixture" };
            vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1 });
            vm.Episodes.Add(new() { ContentId = "loaded", PendingTranslationLanguage = "fr" });
            Set(page, "_currentContentId", type); Set(page, "_navigationCts", navigation);
            parent.Children.Add(page);
            var button = (Button)page.FindName("TranslateEpisodesButton");
            Task? operation = null;
            wire.TranslationOnView = phase == "auto" ? "auto" : "button";
            try
            {
                await Task.Delay(60);
                Check(page.CanPreserveOnReconnect, "An idle translation surface should allow preservation.");
                if (phase == "status")
                {
                    wire.HeldCompanion = "metadata-ai";
                    wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.ReleaseCompanion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                operation = (Task)typeof(ItemDetailPage).GetMethod("ConfigureEpisodesTranslationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, null)!;
                if (phase == "status")
                    await wire.CompanionStarted!.Task.WaitAsync(TimeSpan.FromSeconds(5));
                else
                {
                    if (phase == "polling")
                    {
                        await operation.WaitAsync(TimeSpan.FromSeconds(5));
                        Check(page.CanPreserveOnReconnect && button.IsEnabled && button.Visibility == Visibility.Visible,
                            "A configured idle translation button should allow preservation.");
                        operation = (Task)typeof(ItemDetailPage).GetMethod("TranslateEpisodesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(page, null)!;
                    }
                    await UntilAsync(() => !button.IsEnabled && button.Visibility == Visibility.Visible);
                    Check((string)button.Content == "Translating descriptions\u2026", "Translation polling did not start.");
                }
                Check(!page.CanPreserveOnReconnect, "Pending page-owned translation work must require a reconnect remount.");
                client.InvalidateAccessContext();
                Check(!page.CanPreserveOnReconnect, "Retired translation work must not preserve a disabled or missing button.");
                wire.ReleaseCompanion?.TrySetResult();
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!page.CanPreserveOnReconnect,
                    "Translation work that settles before shell catch-up must still remount to clear its stale key/button.");
            }
            finally
            {
                wire.HeldCompanion = null; wire.ReleaseCompanion?.TrySetResult(); wire.TranslationOnView = "button";
                navigation.Cancel();
                if (operation != null) await operation.WaitAsync(TimeSpan.FromSeconds(5));
                vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm); parent.Children.Remove(page);
            }
            Program.Log($"PASS: mounted {type} reconnect during episode translation {phase} requires remount, including after stale work settles.");
        }
    }

    private static async Task VerifyProgressActionsAsync(MenuFlyout flyout, Button owner, bool restart, bool reset)
    {
        bool Visible(string label) => flyout.Items.OfType<MenuFlyoutItem>()
            .Any(item => item.Text == label && item.Visibility == Visibility.Visible);
        flyout.ShowAt(owner);
        try
        {
            await UntilAsync(() => Visible("Play from Beginning") == restart && Visible("Reset Progress") == reset);
        }
        finally { flyout.Hide(); }
    }

    private static bool HasWatchedBadge(ItemDetailPage page, string contentId)
    {
        var row = Descendants<Button>((Panel)page.FindName("EpisodesPanel"))
            .SingleOrDefault(button => button.Tag as string == contentId);
        return row != null && Descendants<Grid>(row).Any(grid =>
            AutomationProperties.GetName(grid) == "Watched" && grid.Visibility == Visibility.Visible);
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
        internal bool OmitTarget;
        internal bool FailEpisodes;
        internal bool Favorite;
        internal bool LoadedPlayed;
        internal string TranslationOnView = "button";
        internal double LoadedPosition = 31;
        internal string? HeldCompanion;
        internal TaskCompletionSource? CompanionStarted;
        internal TaskCompletionSource? ReleaseCompanion;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var translate = request.Method == HttpMethod.Post && path == "/api/v2/catalog/items/season/translate-description";
            if (request.RequestUri.Host != "realtime-native.invalid" || (request.Method != HttpMethod.Get && !translate))
                throw new InvalidOperationException("Only synthetic fixture reads and episode translations are authorized.");
            if (translate) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            if (FailEpisodes && path.EndsWith("/episodes"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    { Content = new StringContent("{\"error\":{\"code\":\"service_unavailable\",\"message\":\"Synthetic companion failure\"}}", Encoding.UTF8, "application/json") });
            object body = path == "/api/v2/capabilities/metadata-ai" ? new { state = "available", allowed = true, on_view = TranslationOnView } :
                path.EndsWith("/episodes") ? new { items = new[] {
                    new { content_id = "sibling", season_number = 1, user_data = new { played = false, position_seconds = 31d, duration_seconds = 1200d } },
                    new { content_id = "loaded", season_number = 1, user_data = new { played = LoadedPlayed, position_seconds = LoadedPosition, duration_seconds = 1200d } } } } :
                path.StartsWith("/api/v2/catalog/items/") ?
                new Dictionary<string, object?> { ["content_id"] = path.Split('/').Last(), ["type"] = path.Split('/').Last(),
                    ["play_content_id"] = Target, ["play_season_number"] = 1, ["episode_count"] = 6, ["season_count"] = 1,
                    ["user_data"] = new { watched_count = Count, unplayed_count = 6 - Count, in_progress_count = 1, played = false, position_seconds = 900 },
                    ["user_state"] = new { is_favorite = Favorite } } :
                path.EndsWith("/seasons") ? new { items = new[] { new { content_id = "season", season_number = 1, episode_count = 6,
                    user_data = new { watched_count = Count, unplayed_count = 6 - Count, in_progress_count = 1, played = false } } } } :
                path.Contains("progress/capabilities") ? new { clear_resume_position = true } : new { items = Array.Empty<object>() };
            if (OmitTarget && body is Dictionary<string, object?> detail) detail.Remove("play_content_id");
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
