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
        var wire = new Wire { CompanionArtwork = await CreateCompanionArtworkAsync() };
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
                    foreach (var metadataAction in new[] { "translation", "edit" })
                    {
                        var retainedSeasons = vm.Seasons.ToArray();
                        var retainedEpisodes = vm.Episodes.ToArray();
                        var selection = vm.SelectedSeasonNumber;
                        var episodeSource = vm.EpisodesSeasonContentId;
                        if (metadataAction == "translation")
                        {
                            vm.Item.PendingTranslationLanguage = "fr";
                            await (Task)typeof(ItemDetailPage).GetMethod("TranslateOverviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .Invoke(page, [vm.Item])!;
                        }
                        else
                        {
                            await vm.ReloadAsync(vm.Item.ContentId);
                            Call(page, "UpdateUI");
                        }
                        await Task.Delay(100); page.UpdateLayout();
                        Check(vm.Item.Overview == "Refreshed metadata" && vm.Item.PendingTranslationLanguage == null,
                            "Mounted metadata reload did not commit the translated/edited description.");
                        Check(retainedSeasons.SequenceEqual(vm.Seasons) && retainedEpisodes.SequenceEqual(vm.Episodes)
                            && vm.SelectedSeasonNumber == selection && vm.EpisodesSeasonContentId == episodeSource,
                            "Translation/editing emptied or replaced the mounted rows/selection.");
                        var cardsAfterMetadata = episodePanel.Children.ToArray();
                        wire.LoadedPlayed = metadataAction == "translation";
                        refresh.ReceiveRealtime("user_state", "user_state.changed", loadedData.RootElement);
                        await UntilAsync(() => loadedEpisode.UserData?.Played == wire.LoadedPlayed
                            && HasWatchedBadge(page, "loaded") == wire.LoadedPlayed);
                        Check(cardsAfterMetadata.SequenceEqual(episodePanel.Children),
                            "A remote event after translation/editing failed to refresh the retained native cards in place.");
                    }
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
            await VerifySeriesLayoutRefreshAsync(parent, wire, client);
            await VerifyLegacySeriesActionAsync(parent, wire, client);
            await VerifyCardArtworkRecoveryAsync(parent, wire, client);
            await VerifyMatchedParentBreadcrumbAsync(parent, wire, client);
            await VerifyRemappedSeasonRowsAsync(parent, wire, client);
            foreach (var type in new[] { "series", "season" })
            {
                await VerifyTranslationReconnectAsync(parent, wire, client, type);
                await VerifyTranslationReconnectAsync(parent, wire, client, type, overview: true);
                await VerifyTitleArtReconnectAsync(parent, wire, client, type);
                await VerifyCompanionReconnectAsync(parent, wire, client, type);
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

    private static async Task VerifySeriesLayoutRefreshAsync(StackPanel parent, Wire wire, SiloApiClient client)
    {
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        var vm = page.ViewModel;
        using var navigation = new CancellationTokenSource();
        vm.Item = new() { ContentId = "series", Type = "series", Title = "Changing seasons", PlayContentId = "new-target" };
        vm.IsSeries = true;
        vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1 });
        Set(page, "_currentContentId", "series"); Set(page, "_navigationCts", navigation);
        var handler = (PropertyChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(PropertyChangedEventHandler), page);
        vm.PropertyChanged += handler;
        parent.Children.Add(page);
        Visibility Visible(string name) => ((UIElement)page.FindName(name)).Visibility;
        Task Refresh() => vm.RefreshMediaStateAsync(new(client.CaptureContext(),
            new HashSet<string>(["never-loaded"]), new HashSet<string>(["series"])));
        async Task MultiSeason()
        {
            wire.SeasonCount = 2;
            await Refresh();
            await UntilAsync(() => Visible("SeasonsSection") == Visibility.Visible
                && Visible("SeasonsScrollViewer") == Visibility.Visible && Visible("EpisodesSection") == Visibility.Collapsed);
            Check(((Panel)page.FindName("SeasonsPanel")).Children.OfType<Button>().Any(card => card.Tag as string == "second-season"),
                "The added season is not reachable from the mounted series.");
        }
        try
        {
            wire.SeasonCount = 1;
            await (Task)typeof(ItemDetailPage).GetMethod("UpdateSeriesLayoutAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, ["series", navigation.Token, true])!;
            var mounted = vm.Item;
            Check(Visible("EpisodesSection") == Visibility.Visible && Visible("SeasonsSection") == Visibility.Collapsed,
                "Initial single-season navigation did not show the episode grid.");
            await MultiSeason();
            wire.SeasonCount = 1;
            await Refresh();
            await UntilAsync(() => Visible("EpisodesSection") == Visibility.Visible && Visible("EpisodesPanel") == Visibility.Visible
                && Visible("SeasonsSection") == Visibility.Collapsed && Visible("SeasonsScrollViewer") == Visibility.Collapsed);
            Check(vm.EpisodesSeasonContentId == "season" && ((Panel)page.FindName("EpisodesPanel")).Children.Count > 0,
                "Returning to one season did not load and mount canonical episode rows.");
            Check(ReferenceEquals(mounted, vm.Item), "Changing season layouts remounted the detail.");

            await MultiSeason();
            var retainedEpisodes = vm.Episodes.ToArray();
            wire.HeldCompanion = "episodes";
            wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.ReleaseCompanion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.SeasonCount = 1;
            await Refresh();
            await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await MultiSeason();
            wire.HeldCompanion = null;
            wire.ReleaseCompanion.TrySetResult();
            await UntilAsync(() => page.CanPreserveOnReconnect);
            Check(Visible("EpisodesSection") == Visibility.Collapsed && retainedEpisodes.SequenceEqual(vm.Episodes),
                "A retired single-season read repopulated or revealed the multi-season grid.");

            wire.EmptyEpisodes = true; wire.SeasonCount = 1;
            await Refresh();
            await UntilAsync(() => vm.Episodes.Count == 0 && ((Panel)page.FindName("EpisodesPanel")).Children.Count == 0);
            Check(Visible("SeasonsSection") == Visibility.Collapsed, "An empty single season left the old season rail visible.");
            wire.SeasonCount = 0;
            await Refresh();
            await UntilAsync(() => Visible("SeasonsSection") == Visibility.Collapsed && Visible("EpisodesSection") == Visibility.Collapsed);
        }
        finally
        {
            wire.SeasonCount = 1; wire.EmptyEpisodes = false; wire.HeldCompanion = null; wire.ReleaseCompanion?.TrySetResult();
            navigation.Cancel(); vm.PropertyChanged -= handler; vm.CancelPendingLoads();
            WeakReferenceMessenger.Default.UnregisterAll(vm); parent.Children.Remove(page);
        }
    }

    private static async Task VerifyCardArtworkRecoveryAsync(StackPanel parent, Wire wire, SiloApiClient client)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        var cache = Path.Combine(Path.GetTempPath(), "SiloPlayer-native-card-art-" + Guid.NewGuid().ToString("N"));
        using var images = new ImageService(cache);
        using var http = new HttpClient(wire, disposeHandler: false);
        slot.SetValue(null, new TitleArtServices(original, images, http));
        using var navigation = new CancellationTokenSource();
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        var still = new Border(); var poster = new Border();
        page.ViewModel.Item = new() { ContentId = "season", Type = "season", Title = "Card artwork recovery" };
        Set(page, "_currentContentId", "season"); Set(page, "_navigationCts", navigation);
        parent.Children.Add(page); parent.Children.Add(still); parent.Children.Add(poster);
        var episode = new Episode { ContentId = "card-episode", UserData = new() { Played = true } };
        var season = new Season { ContentId = "card-season" };
        var attempt = 0;
        Task Read(bool episodeStill)
        {
            var url = "https://realtime-native.invalid/companion-poster.png?card=" + ++attempt;
            if (episodeStill) episode.StillUrl = url; else season.PosterUrl = url;
            return (Task)typeof(ItemDetailPage).GetMethod(episodeStill ? "LoadEpisodeStillAsync" : "LoadSeasonPosterAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, episodeStill
                    ? new object[] { still, episode } : new object[] { poster, season })!;
        }
        try
        {
            await UntilAsync(() => still.IsLoaded && poster.IsLoaded);
            wire.CompanionFailure = "poster"; wire.CompanionStatus = HttpStatusCode.NotFound;
            await Read(true); await Read(false);
            Check(page.CanPreserveOnReconnect, "A missing episode still or season poster permanently latched remounting.");

            wire.CompanionStatus = HttpStatusCode.ServiceUnavailable;
            await Read(true); await Read(false);
            Check(!page.CanPreserveOnReconnect, "Transient card failures must retain reconnect recovery.");
            wire.CompanionFailure = null;
            await Read(true);
            Check(!page.CanPreserveOnReconnect, "One successful card retry cleared another card's failure.");
            await Read(false);
            Check(page.CanPreserveOnReconnect, "Successful card retries did not clear their recovery state.");

            foreach (var newerFinishesFirst in new[] { true, false })
            {
                wire.HeldCompanion = "poster";
                wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var olderRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.ReleaseCompanion = olderRelease;
                var older = Read(true);
                await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var newerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.ReleaseCompanion = newerRelease;
                var newer = Read(true);
                await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (newerFinishesFirst) { newerRelease.SetResult(); await newer; olderRelease.SetResult(); await older; }
                else { olderRelease.SetResult(); await older; newerRelease.SetResult(); await newer; }
                Check(page.CanPreserveOnReconnect, "A superseded still latched recovery despite its successful replacement.");
            }
            wire.HeldCompanion = "poster";
            wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.ReleaseCompanion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var retired = Read(true);
            await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            client.InvalidateAccessContext();
            wire.ReleaseCompanion.SetResult(); await retired;
            Check(!page.CanPreserveOnReconnect, "An access-retired still lost its remount recovery requirement.");
            wire.HeldCompanion = null;
            await Read(true);
            Check(page.CanPreserveOnReconnect, "A later owned still could not recover an access-retired load.");
        }
        finally
        {
            wire.CompanionFailure = null; wire.HeldCompanion = null; wire.ReleaseCompanion?.TrySetResult(); navigation.Cancel();
            page.ViewModel.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(page.ViewModel);
            parent.Children.Remove(page); parent.Children.Remove(still); parent.Children.Remove(poster);
            slot.SetValue(null, original); Directory.Delete(cache, recursive: true);
        }
    }

    private static async Task VerifyMatchedParentBreadcrumbAsync(StackPanel parent, Wire wire, SiloApiClient client)
    {
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        var vm = page.ViewModel;
        using var navigation = new CancellationTokenSource();
        vm.Item = new() { ContentId = "season", Type = "season", Title = "Season 1", SeriesId = "old-series", SeriesTitle = "Old series" };
        Set(page, "_currentContentId", "season"); Set(page, "_navigationCts", navigation);
        var handler = (PropertyChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(PropertyChangedEventHandler), page);
        vm.PropertyChanged += handler;
        parent.Children.Add(page);
        HyperlinkButton SeriesLink() => ((Panel)page.FindName("BreadcrumbPanel")).Children.OfType<HyperlinkButton>().First();
        var resolve = typeof(ItemDetailPage).GetMethod("ResolvePlayableContentIdForPlaybackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void SeedOldAction()
        {
            vm.EpisodesSeasonContentId = "season";
            vm.Episodes.Add(new() { ContentId = "old-parent-episode", SeasonNumber = 1 });
            Call(page, "BuildEpisodeRows");
            Call(page, "ApplySeasonAction");
            Check(((Button)page.FindName("PrimaryPlayButton")).IsEnabled,
                "The parent-remap fixture did not establish a playable old-parent fallback.");
        }
        async Task VerifyRetiredActionAsync()
        {
            await UntilAsync(() => ((UIElement)page.FindName("SplitPlayButton")).Visibility == Visibility.Collapsed
                && ((Panel)page.FindName("EpisodesPanel")).Children.Count == 0);
            Check(!((Button)page.FindName("PrimaryPlayButton")).IsEnabled
                && typeof(ItemDetailPage).GetField("_playableContentId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page) == null,
                "A parent remap retained the previous parent's actual native Play target.");
            // A click must revalidate even before the queued action repaint.
            Set(page, "_playableContentId", "old-parent-episode");
            Check(await (Task<string?>)resolve.Invoke(page, null)! == null,
                "Season playback reused a cached old-parent episode after remapping.");
        }
        try
        {
            wire.OmitTarget = true; wire.EmptyEpisodes = true;
            SeedOldAction();
            wire.ParentSeries = "matched-series";
            await vm.ReloadAfterMatchAsync("season");
            Call(page, "UpdateUI");
            Check(SeriesLink().Tag as string == "matched-series" && ((TextBlock)SeriesLink().Content).Text == "Matched series",
                "Same-ID Match left the breadcrumb linked to the old parent series.");
            await VerifyRetiredActionAsync();
            SeedOldAction();
            wire.ParentSeries = "newer-series";
            await vm.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(["season"]), new HashSet<string>()));
            await UntilAsync(() => SeriesLink().Tag as string == "newer-series");
            Check(vm.Item!.SeriesId == "newer-series", "A retained sweep did not adopt the fresh parent identity.");
            await VerifyRetiredActionAsync();
            // Legacy detail responses omit both parent fields. Metadata editing
            // and same-ID Match must keep the mounted parent link navigable.
            wire.ParentSeries = null;
            foreach (var afterMatch in new[] { false, true })
            {
                if (afterMatch) await vm.ReloadAfterMatchAsync("season");
                else await vm.ReloadAsync("season");
                Call(page, "UpdateUI");
                Check(SeriesLink().Tag as string == "newer-series" && ((TextBlock)SeriesLink().Content).Text == "Matched series",
                    "A legacy metadata reload dropped or changed the mounted parent-series breadcrumb.");
            }
        }
        finally
        {
            wire.ParentSeries = null; wire.OmitTarget = false; wire.EmptyEpisodes = false;
            vm.PropertyChanged -= handler; vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm);
            parent.Children.Remove(page);
        }
    }

    private static async Task VerifyRemappedSeasonRowsAsync(StackPanel parent, Wire wire, SiloApiClient client)
    {
        foreach (var source in new[] { "match", "realtime", "catch-up", "post-playback" })
        foreach (var omitTarget in new[] { true, false })
        {
            var page = new ItemDetailPage { Width = 900, Height = 720 };
            var vm = page.ViewModel;
            using var navigation = new CancellationTokenSource();
            var mounted = new MediaItemDetail
            {
                ContentId = "season", Type = "season", Title = "Season 1", SeasonNumber = 1,
                SeriesId = "old-series", SeriesTitle = "Old series", PlayContentId = "old-parent-episode"
            };
            vm.Item = mounted;
            vm.EpisodesSeasonContentId = "season";
            vm.Episodes.Add(new() { ContentId = "old-parent-episode", SeasonNumber = 1 });
            Set(page, "_currentContentId", "season"); Set(page, "_navigationCts", navigation);
            var handler = (PropertyChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(PropertyChangedEventHandler), page);
            vm.PropertyChanged += handler;
            parent.Children.Add(page);
            try
            {
                Call(page, "BuildEpisodeRows"); Call(page, "ApplySeasonAction");
                wire.ParentSeries = "matched-series"; wire.OmitTarget = omitTarget; wire.Target = "loaded";
                wire.EmptyEpisodes = false; wire.HeldCompanion = "episodes";
                wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.ReleaseCompanion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var refresh = source switch
                {
                    "match" => vm.ReloadAfterMatchAsync("season"),
                    "post-playback" => vm.RefreshWatchedStateAsync(Task.CompletedTask, navigation.Token),
                    _ => vm.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(["season"]),
                        new HashSet<string>(), CatchUp: source == "catch-up"))
                };
                await refresh.WaitAsync(TimeSpan.FromSeconds(5));
                if (source == "match") Call(page, "UpdateUI");
                // No playback click or navigation should be necessary to start
                // the new parent's canonical row read, even with a server target.
                await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(vm.IsEpisodesLoading && ((UIElement)page.FindName("EpisodesLoadingSkeleton")).Visibility == Visibility.Visible
                    && ((UIElement)page.FindName("EpisodesEmptyState")).Visibility == Visibility.Collapsed,
                    "A remapped season did not show loading state while its canonical rows were pending.");
                wire.HeldCompanion = null; wire.ReleaseCompanion.TrySetResult();
                var panel = (Panel)page.FindName("EpisodesPanel");
                await UntilAsync(() => !vm.IsEpisodesLoading && vm.EpisodesSeasonContentId == "season"
                    && vm.Episodes.Count == 2 && Descendants<Button>(panel).Any(button => button.Tag as string == "loaded")
                    && ((Button)page.FindName("PrimaryPlayButton")).IsEnabled
                    && ((UIElement)page.FindName("SplitPlayButton")).Visibility == Visibility.Visible);
                Check(ReferenceEquals(mounted, vm.Item) && mounted.SeriesId == "matched-series"
                    && mounted.SeriesTitle == "Matched series", "A remapped season lost its mounted identity or fresh parent.");
                Check(!vm.Episodes.Any(episode => episode.ContentId == "old-parent-episode")
                    && ((UIElement)page.FindName("EpisodesPanel")).Visibility == Visibility.Visible
                    && ((UIElement)page.FindName("EpisodesLoadingSkeleton")).Visibility == Visibility.Collapsed,
                    "The remapped season did not replace its retired rows with the canonical grid.");
                var resolve = typeof(ItemDetailPage).GetMethod("ResolvePlayableContentIdForPlaybackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Check(await (Task<string?>)resolve.Invoke(page, null)! == (omitTarget ? "sibling" : "loaded"),
                    "The remapped season did not restore the fresh fallback or authoritative Play action.");
                Program.Log($"PASS: {source} parent remap reloads mounted season rows and restores {(omitTarget ? "legacy" : "explicit")} Play without navigation.");
            }
            finally
            {
                wire.ParentSeries = null; wire.OmitTarget = false; wire.Target = "new-target";
                wire.HeldCompanion = null; wire.ReleaseCompanion?.TrySetResult(); navigation.Cancel();
                vm.PropertyChanged -= handler; vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm);
                parent.Children.Remove(page);
            }
        }
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

            // Replacing a season with a different ID but the same number must
            // not reuse rows from the retired season, including on a multi-season page.
            vm.Seasons.Clear();
            vm.Seasons.Add(new() { ContentId = "replacement-season", SeasonNumber = 1, EpisodeCount = 6,
                UserData = new() { UnplayedCount = 6 } });
            vm.Seasons.Add(new() { ContentId = "second-season", SeasonNumber = 2, EpisodeCount = 6,
                UserData = new() { UnplayedCount = 6 } });
            wire.EmptyEpisodes = true;
            await (Task)apply.Invoke(page, null)!;
            Check(vm.EpisodesSeasonContentId == "replacement-season" && vm.Episodes.Count == 0
                && !((Button)page.FindName("PrimaryPlayButton")).IsEnabled
                && await (Task<string?>)resolve.Invoke(page, null)! == null,
                "A legacy series action reused another season's rows solely because the season number matched.");
            wire.EmptyEpisodes = false;

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
            wire.OmitTarget = false; wire.EmptyEpisodes = false;
            vm.PropertyChanged -= handler; vm.CancelPendingLoads();
            WeakReferenceMessenger.Default.UnregisterAll(vm); parent.Children.Remove(page);
        }
    }

    private static async Task VerifyTranslationReconnectAsync(StackPanel parent, Wire wire, SiloApiClient client, string type, bool overview = false)
    {
        foreach (var phase in overview ? new[] { "status", "polling", "auto", "timeout" } : new[] { "status", "polling", "auto" })
        {
            var page = new ItemDetailPage { Width = 900, Height = 720 };
            var vm = page.ViewModel;
            using var navigation = new CancellationTokenSource();
            vm.Item = new() { ContentId = type, Type = type, Title = "Translation reconnect fixture",
                PendingTranslationLanguage = overview ? "fr" : null };
            vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1 });
            vm.Episodes.Add(new() { ContentId = "loaded", PendingTranslationLanguage = overview ? null : "fr" });
            Set(page, "_currentContentId", type); Set(page, "_navigationCts", navigation);
            parent.Children.Add(page);
            var button = (Button)page.FindName(overview ? "TranslateOverviewButton" : "TranslateEpisodesButton");
            Task? operation = null;
            wire.TranslationOnView = phase is "auto" or "timeout" ? "auto" : "button";
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
                operation = (Task)typeof(ItemDetailPage).GetMethod(overview ? "ConfigureOnViewTranslationAsync" : "ConfigureEpisodesTranslationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, overview ? new object?[] { vm.Item } : null)!;
                if (phase == "status")
                    await wire.CompanionStarted!.Task.WaitAsync(TimeSpan.FromSeconds(5));
                else
                {
                    if (phase == "polling")
                    {
                        await operation.WaitAsync(TimeSpan.FromSeconds(5));
                        Check(page.CanPreserveOnReconnect && button.IsEnabled && button.Visibility == Visibility.Visible,
                            "A configured idle translation button should allow preservation.");
                        operation = (Task)typeof(ItemDetailPage).GetMethod(overview ? "TranslateOverviewAsync" : "TranslateEpisodesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(page, overview ? new object?[] { vm.Item } : null)!;
                    }
                    await UntilAsync(() => !button.IsEnabled && button.Visibility == Visibility.Visible);
                    Check((string)button.Content == (overview ? "Translating\u2026" : "Translating descriptions\u2026"), "Translation polling did not start.");
                }
                Check(!page.CanPreserveOnReconnect, "Pending page-owned translation work must require a reconnect remount.");
                if (phase == "timeout")
                {
                    // Exercise the same cancellation path as the 45-second budget
                    // without making the native fixture wait for the full window.
                    ((CancellationTokenSource)typeof(ItemDetailPage).GetField("_translationCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!).Cancel();
                    await operation.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(button.IsEnabled && button.Visibility == Visibility.Collapsed && (string)button.Content == "Translate description",
                        "An auto-mode timeout retained the disabled Translating control.");
                    Check(page.CanPreserveOnReconnect, "A settled auto-mode timeout should not require a reconnect remount.");
                    continue;
                }
                client.InvalidateAccessContext();
                Check(!page.CanPreserveOnReconnect, "Retired translation work must not preserve a disabled or missing button.");
                wire.ReleaseCompanion?.TrySetResult();
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!page.CanPreserveOnReconnect,
                    "Translation work that settles before shell catch-up must still remount to clear its stale key/button.");
                if (overview && phase != "status")
                    Check(button.IsEnabled && (string)button.Content == "Translate description",
                        "Interrupted overview polling left a disabled Translating control.");
            }
            finally
            {
                wire.HeldCompanion = null; wire.ReleaseCompanion?.TrySetResult(); wire.TranslationOnView = "button";
                navigation.Cancel();
                if (operation != null) await operation.WaitAsync(TimeSpan.FromSeconds(5));
                vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm); parent.Children.Remove(page);
            }
            Program.Log($"PASS: mounted {type} reconnect during {(overview ? "overview" : "episode")} translation {phase} requires remount, including after stale work settles.");
        }
    }

    private static async Task VerifyTitleArtReconnectAsync(StackPanel parent, Wire wire, SiloApiClient client, string type)
    {
        foreach (var phase in new[] { "settings", "logo" })
        {
            var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
            var original = (IServiceProvider)slot.GetValue(null)!;
            var cache = Path.Combine(Path.GetTempPath(), "SiloPlayer-native-title-art-" + Guid.NewGuid().ToString("N"));
            using var images = new ImageService(cache);
            using var http = new HttpClient(wire, disposeHandler: false);
            slot.SetValue(null, new TitleArtServices(original, images, http));
            using var navigation = new CancellationTokenSource();
            var page = new ItemDetailPage { Width = 900, Height = 720 };
            var vm = page.ViewModel;
            vm.Item = new() { ContentId = type, Type = type, Title = "Title-art reconnect fixture",
                LogoUrl = "https://realtime-native.invalid/title-art-logo.png" };
            Set(page, "_currentContentId", type); Set(page, "_navigationCts", navigation);
            parent.Children.Add(page);
            Task? operation = null;
            try
            {
                await Task.Delay(60);
                Check(page.CanPreserveOnReconnect, "An idle title-art surface should allow preservation.");
                ((TextBlock)page.FindName("TitleText")).Text = vm.Item.Title;
                ((UIElement)page.FindName("TitleText")).Visibility = Visibility.Collapsed;
                ((UIElement)page.FindName("TitleArtPending")).Visibility = Visibility.Visible;
                wire.HeldTitleArt = phase;
                wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.ReleaseCompanion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var load = typeof(ItemDetailPage).GetMethod("LoadTitleArtPreferenceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                operation = (Task)load.Invoke(page, [vm.Item, navigation.Token])!;
                await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!page.CanPreserveOnReconnect, "Pending title-art settings/download must require a reconnect remount.");
                client.InvalidateAccessContext();
                wire.ReleaseCompanion.TrySetResult();
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!page.CanPreserveOnReconnect,
                    "Retired title-art work that settles before shell catch-up must still require a remount.");
                Check(((UIElement)page.FindName("TitleText")).Visibility == Visibility.Collapsed,
                    "A retired title-art response unexpectedly painted the old page.");
                Check(((UIElement)page.FindName("TitleArtPending")).Visibility ==
                    (phase == "settings" ? Visibility.Visible : Visibility.Collapsed),
                    "The fixture did not interrupt the requested title-art phase.");

                wire.HeldTitleArt = null;
                var replacement = new ItemDetailPage { Width = 900, Height = 720 };
                replacement.ViewModel.Item = new() { ContentId = type, Type = type, Title = vm.Item.Title, LogoUrl = vm.Item.LogoUrl };
                Set(replacement, "_currentContentId", type); Set(replacement, "_navigationCts", navigation);
                ((TextBlock)replacement.FindName("TitleText")).Text = replacement.ViewModel.Item.Title;
                parent.Children.Add(replacement);
                try
                {
                    await (Task)load.Invoke(replacement, [replacement.ViewModel.Item, navigation.Token])!;
                    Check(((UIElement)replacement.FindName("TitleArtPending")).Visibility == Visibility.Collapsed
                        && ((UIElement)replacement.FindName("TitleText")).Visibility == Visibility.Visible,
                        "The reconnect remount did not resolve the missing logo to a visible text title.");
                    Check(replacement.CanPreserveOnReconnect, "A settled replacement title should allow preservation.");
                }
                finally
                {
                    replacement.ViewModel.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(replacement.ViewModel);
                    parent.Children.Remove(replacement);
                }
                Program.Log($"PASS: mounted {type} reconnect during title-art {phase} requires remount before and after retirement; replacement resolves its title.");
            }
            finally
            {
                wire.HeldTitleArt = null; wire.ReleaseCompanion?.TrySetResult(); navigation.Cancel();
                try { if (operation != null) await operation.WaitAsync(TimeSpan.FromSeconds(5)); }
                finally
                {
                    vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm); parent.Children.Remove(page);
                    slot.SetValue(null, original);
                    Directory.Delete(cache, recursive: true);
                }
            }
        }
    }

    private static async Task<byte[]> CreateCompanionArtworkAsync()
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
            1, 1, 96, 96, [0, 0, 255, 255]);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[(int)stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task VerifyCompanionReconnectAsync(StackPanel parent, Wire wire, SiloApiClient client, string type)
    {
        var phases = type == "series" ? new[] { "backdrop", "poster", "request-status", "request-detail", "advisory" }
            : new[] { "backdrop", "poster", "advisory" };
        foreach (var phase in phases)
        {
            var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
            var original = (IServiceProvider)slot.GetValue(null)!;
            var cache = Path.Combine(Path.GetTempPath(), "SiloPlayer-native-companion-" + Guid.NewGuid().ToString("N"));
            using var images = new ImageService(cache);
            using var http = new HttpClient(wire, disposeHandler: false);
            slot.SetValue(null, new TitleArtServices(original, images, http));
            using var navigation = new CancellationTokenSource();
            ItemDetailPage Page()
            {
                var result = new ItemDetailPage { Width = 900, Height = 720 };
                result.ViewModel.Item = new() { ContentId = type, Type = type, Title = "Companion recovery", TmdbId = "42",
                    AdvisoryAge = 18, BackdropUrl = "https://realtime-native.invalid/companion-backdrop.png",
                    PosterUrl = "https://realtime-native.invalid/companion-poster.png" };
                Set(result, "_currentContentId", type); Set(result, "_navigationCts", navigation);
                return result;
            }
            Task Read(ItemDetailPage target)
            {
                var method = phase == "backdrop" ? "LoadBackdropAsync" : phase == "poster" ? "LoadHeroPosterAsync"
                    : phase == "advisory" ? "LoadAdvisoryDisplayAsync" : "LoadSeriesRequestActionAsync";
                return (Task)typeof(ItemDetailPage).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(target, [phase == "poster" ? (object)target.ViewModel.Item!.PosterUrl! : target.ViewModel.Item!, navigation.Token])!;
            }
            var page = Page();
            parent.Children.Add(page);
            Task? operation = null;
            try
            {
                await Task.Delay(60);
                Check(page.CanPreserveOnReconnect, "Idle companion fixture should permit preservation.");
                wire.CompanionFailure = phase; wire.CompanionStatus = HttpStatusCode.ServiceUnavailable;
                wire.HeldCompanion = phase;
                wire.CompanionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.ReleaseCompanion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                operation = Read(page);
                await wire.CompanionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!page.CanPreserveOnReconnect, "A pending hero/request/advisory read must require remounting.");
                wire.HeldCompanion = null; wire.ReleaseCompanion.TrySetResult();
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!page.CanPreserveOnReconnect, "A failed companion that settles before reconnect lost its recovery requirement.");
                client.InvalidateAccessContext();
                await page.ViewModel.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(), new HashSet<string>(), CatchUp: true));
                Check(!page.CanPreserveOnReconnect, "State catch-up cannot recover missing hero/request/advisory content.");

                wire.CompanionFailure = null;
                var replacement = Page();
                parent.Children.Add(replacement);
                try
                {
                    await Read(replacement).WaitAsync(TimeSpan.FromSeconds(5));
                    Check(replacement.CanPreserveOnReconnect, "A successfully recovered companion should permit preservation.");
                    if (phase is "backdrop" or "poster")
                    {
                        var image = (Image)replacement.FindName(phase == "backdrop" ? "BackdropImage" : "HeroPosterImage");
                        Check(image.Source != null, "Reconnect remount did not paint the missing hero artwork.");
                        // A definitive absence is healthy, not an interrupted download.
                        replacement.ViewModel.Item!.BackdropUrl += "?v=missing";
                        replacement.ViewModel.Item.PosterUrl += "?v=missing";
                        wire.CompanionFailure = phase; wire.CompanionStatus = HttpStatusCode.NotFound;
                        await Read(replacement).WaitAsync(TimeSpan.FromSeconds(5));
                        Check(replacement.CanPreserveOnReconnect, "A definitive artwork 404 incorrectly requires another remount.");
                    }
                    else if (phase == "advisory")
                        Check(((UIElement)replacement.FindName("AdvisoryAgeBadge")).Visibility == Visibility.Visible,
                            "Reconnect remount did not recover the advisory badge.");
                    else
                        Check(((MenuFlyout)replacement.FindName("MoreFlyout")).Items.OfType<MenuFlyoutItem>()
                            .Any(item => item.Text == "Request seasons"), "Reconnect remount did not recover Request seasons.");
                }
                finally
                {
                    replacement.ViewModel.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(replacement.ViewModel);
                    parent.Children.Remove(replacement);
                }
                Program.Log($"PASS: mounted {type} failed {phase} read requires reconnect remount and recovers its content.");
            }
            finally
            {
                wire.CompanionFailure = null; wire.HeldCompanion = null; wire.ReleaseCompanion?.TrySetResult(); navigation.Cancel();
                try { if (operation != null) await operation.WaitAsync(TimeSpan.FromSeconds(5)); }
                finally
                {
                    page.ViewModel.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(page.ViewModel); parent.Children.Remove(page);
                    slot.SetValue(null, original); Directory.Delete(cache, recursive: true);
                }
            }
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
            type == typeof(RequestsApi) ? new RequestsApi(client) :
            type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, new ItemDetailPrefetchCache(catalog, auth)) :
            type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : original.GetService(type);
    }

    private sealed class TitleArtServices(IServiceProvider original, ImageService images, HttpClient http) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(ImageService) ? images : type == typeof(HttpClient) ? http : original.GetService(type);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal int Count = 2;
        internal int SeasonCount = 1;
        internal bool EmptyEpisodes;
        internal string? Target = "new-target";
        internal bool OmitTarget;
        internal bool FailEpisodes;
        internal bool Favorite;
        internal string? ParentSeries;
        internal bool LoadedPlayed;
        internal string TranslationOnView = "button";
        internal double LoadedPosition = 31;
        internal string? HeldCompanion;
        internal string? HeldTitleArt;
        internal string? CompanionFailure;
        internal HttpStatusCode CompanionStatus;
        internal byte[] CompanionArtwork = [];
        internal TaskCompletionSource? CompanionStarted;
        internal TaskCompletionSource? ReleaseCompanion;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var translate = request.Method == HttpMethod.Post && path is "/api/v2/catalog/items/season/translate-description" or "/api/v2/catalog/items/series/translate-description";
            if (request.RequestUri.Host != "realtime-native.invalid" || (request.Method != HttpMethod.Get && !translate))
                throw new InvalidOperationException("Only synthetic fixture reads and episode translations are authorized.");
            if (translate) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            var titleArtPhase = path == "/title-art-logo.png" ? "logo"
                : path == "/api/v2/settings/values/effective" && request.RequestUri.Query.Contains("keys=ui.title_art", StringComparison.Ordinal) ? "settings" : null;
            if (titleArtPhase != null)
            {
                var titleArtResponse = titleArtPhase == "logo" ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[{\"key\":\"ui.title_art\",\"value\":true}]}", Encoding.UTF8, "application/json") };
                if (HeldTitleArt == titleArtPhase && ReleaseCompanion is { } titleArtRelease)
                {
                    CompanionStarted?.TrySetResult();
                    return ReleaseAsync(titleArtResponse, titleArtRelease.Task, ct);
                }
                return Task.FromResult(titleArtResponse);
            }
            var companionPhase = path == "/companion-backdrop.png" ? "backdrop" : path == "/companion-poster.png" ? "poster"
                : path == "/api/v2/requests/status" ? "request-status"
                : path == "/api/v2/requests/detail/series/42" ? "request-detail"
                : path == "/api/v2/settings/values/effective" && request.RequestUri.Query.Contains("keys=catalog.show_advisory_age", StringComparison.Ordinal) ? "advisory" : null;
            if (companionPhase != null)
            {
                var status = CompanionFailure == companionPhase ? CompanionStatus : HttpStatusCode.OK;
                var companionResponse = new HttpResponseMessage(status);
                if (status != HttpStatusCode.OK)
                    companionResponse.Content = new StringContent("{\"error\":{\"code\":\"service_unavailable\",\"message\":\"Synthetic companion failure\"}}", Encoding.UTF8, "application/json");
                else if (companionPhase is "backdrop" or "poster")
                    companionResponse.Content = new ByteArrayContent(CompanionArtwork);
                else
                    companionResponse.Content = new StringContent(companionPhase == "request-status"
                        ? "{\"requests_enabled\":true,\"allowed\":true,\"season_requests_supported\":true,\"missing_seasons_requestable\":true}"
                        : companionPhase == "request-detail" ? "{\"request\":{\"requestable\":true},\"seasons\":[{\"season_number\":2}]}"
                        : "{\"items\":[{\"key\":\"catalog.show_advisory_age\",\"value\":true}]}", Encoding.UTF8, "application/json");
                if (HeldCompanion == companionPhase && ReleaseCompanion is { } companionRelease)
                {
                    CompanionStarted?.TrySetResult();
                    return ReleaseAsync(companionResponse, companionRelease.Task, ct);
                }
                return Task.FromResult(companionResponse);
            }
            if (FailEpisodes && path.EndsWith("/episodes"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    { Content = new StringContent("{\"error\":{\"code\":\"service_unavailable\",\"message\":\"Synthetic companion failure\"}}", Encoding.UTF8, "application/json") });
            object body = path == "/api/v2/capabilities/metadata-ai" ? new { state = "available", allowed = true, on_view = TranslationOnView } :
                path.EndsWith("/episodes") && EmptyEpisodes ? new { items = Array.Empty<object>() } :
                path.EndsWith("/episodes") ? new { items = new[] {
                    new { content_id = "sibling", season_number = 1, user_data = new { played = false, position_seconds = 31d, duration_seconds = 1200d } },
                    new { content_id = "loaded", season_number = 1, user_data = new { played = LoadedPlayed, position_seconds = LoadedPosition, duration_seconds = 1200d } } } } :
                path.StartsWith("/api/v2/catalog/items/") ?
                new Dictionary<string, object?> { ["content_id"] = path.Split('/').Last(), ["type"] = path.Split('/').Last(),
                    ["play_content_id"] = Target, ["play_season_number"] = 1, ["episode_count"] = 6, ["season_count"] = SeasonCount,
                    ["title"] = "Edited title", ["overview"] = "Refreshed metadata", ["season_number"] = 1,
                    ["user_data"] = new { watched_count = Count, unplayed_count = 6 - Count, in_progress_count = 1, played = false, position_seconds = 900 },
                    ["user_state"] = new { is_favorite = Favorite } } :
                path.EndsWith("/seasons") ? new { items = Enumerable.Range(1, SeasonCount).Select(number => new {
                    content_id = number == 1 ? "season" : "second-season", season_number = number, episode_count = 6,
                    user_data = new { watched_count = Count, unplayed_count = 6 - Count, in_progress_count = 1, played = false } }).ToArray() } :
                path.Contains("progress/capabilities") ? new { clear_resume_position = true } : new { items = Array.Empty<object>() };
            if (OmitTarget && body is Dictionary<string, object?> detail) detail.Remove("play_content_id");
            if (ParentSeries != null && body is Dictionary<string, object?> parentDetail)
            { parentDetail["series_id"] = ParentSeries; parentDetail["series_title"] = "Matched series"; }
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
