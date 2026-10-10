using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static class PlaybackScopeNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var completion = typeof(PlayerService).GetMethod("IsAtMediaEnd", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (position, duration, expected) in new[] {
            (3700.1, 3702d, true), (3698d, 3702d, false), (0d, 3702d, false),
            (19.1, 20d, false), (19.9, 20d, true), (0d, 1d, false), (0d, 0d, false) })
        {
            if ((bool)completion.Invoke(null, [position, duration])! != expected)
                throw new InvalidOperationException($"Natural EOF classification at {position}/{duration} expected {expected}; rounded server duration must not trigger recovery, while truncated playback must still recover.");
        }
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://playback-scope-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("fixture-profile");
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "playback-scope"));
        using var player = new PlayerService(new PlaybackApi(client), new CatalogApi(client), auth, client, settings, new SettingsApi(client));

        var manager = new PlaybackManager(new PlaybackApi(client), new CatalogApi(client), auth, client);
        manager.UseWatchDetail(new WatchDetailResponse { ContentId = "episode-one", SeriesId = "series", SeriesTitle = "Fixture series", SeasonNumber = 1, EpisodeNumber = 1 });
        Set(player, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(player, "episode-one");
        ((EpisodeNavigationState)typeof(PlayerService).GetField("_episodeNavigation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)!).PrepareFor("episode-one");
        await (Task)Invoke(player, "AutoDetectNextEpisodeAsync", CancellationToken.None)!;
        if (player.NextEpisodeContentId != "episode-two" || player.NextEpisodeRuntime != 3300)
            throw new InvalidOperationException($"Actual next-episode lookup must convert 55 runtime minutes to 3300 seconds; got {player.NextEpisodeRuntime}.");
        Set(player, "_playbackManager", null);
        manager.Dispose();

        var first = await player.GetOrFetchWatchDetailAsync("same-item", libraryId: 7, fileId: 42);
        var repeated = await player.GetOrFetchWatchDetailAsync("same-item", libraryId: 7, fileId: 42);
        await player.GetOrFetchWatchDetailAsync("same-item");
        await player.GetOrFetchWatchDetailAsync("same-item", libraryId: 8, fileId: 42);
        await player.GetOrFetchWatchDetailAsync("same-item", libraryId: 7, fileId: 43);
        if (!ReferenceEquals(first, repeated) || handler.WatchQueries.Count != 4 || first.PreparedLibraryId != 7 || first.PreparedFileId != 42
            || !handler.WatchQueries.SequenceEqual(new[] { "?library_id=7&file_id=42", "", "?library_id=8&file_id=42", "?library_id=7&file_id=43" }))
            throw new InvalidOperationException("Actual PlayerService cache does not coalesce identical preparation or isolate library/file scopes.");

        handler.HoldWatch = true;
        var pending = player.GetOrFetchWatchDetailAsync("held-item", libraryId: 7, fileId: 42);
        await handler.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var joined = player.GetOrFetchWatchDetailAsync("held-item", libraryId: 7, fileId: 42);
        handler.ReleaseWatch.TrySetResult();
        if (!ReferenceEquals(await pending, await joined) || handler.HeldWatchRequests != 1)
            throw new InvalidOperationException("Concurrent scoped consumers do not share their in-flight watch preparation.");

        handler.HoldWatch = true; handler.ResetWatchSignals();
        pending = player.GetOrFetchWatchDetailAsync("stale-item", libraryId: 7, fileId: 42);
        await handler.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        auth.SelectProfile("fixture-replacement"); handler.ReleaseWatch.TrySetResult();
        try { await pending; throw new InvalidOperationException("Old-profile watch preparation was consumed."); }
        catch (OperationCanceledException) { }

        var shuffle = new ShufflePlaybackController(new ShufflesApi(client), client);
        await shuffle.StartAsync(new("library", "7"));
        typeof(PlayerService).GetField("_shuffleController", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, shuffle);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(player, "current");
        if (!player.IsShufflePlayback || !player.HasNextEpisodeForCurrentPlayback || player.NextEpisodePosterUrl != "https://fixture.invalid/backdrop"
            || player.NextEpisodeSeriesTitle != "Next movie" || player.NextEpisodeRuntime != 1200 || player.ShuffleScopeLabel != "Movies")
            throw new InvalidOperationException("Actual native post-roll metadata does not project the server shuffle movie/backdrop/runtime/scope.");
        handler.Singleton = true; await player.RefreshShuffleNextAsync();
        if (player.HasNextEpisodeForCurrentPlayback || player.ShuffleNext != null)
            throw new InvalidOperationException("A singleton shuffle can restart itself through native next-item presentation.");
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)serviceField.GetValue(null)!;
        var navigation = new NavigationService { Frame = new Frame() };
        Type? destination = null; object? parameter = null;
        navigation.NavigationRequestHandler = (type, value) => { destination = type; parameter = value; return true; };
        serviceField.SetValue(null, new ReaderServices(original, client, navigation));
        try
        {
            var reader = new EbookReaderPage();
            Set(reader, "_libraryId", 7); Set(reader, "_item", new MediaItemDetail { Type = "ebook", SeriesId = "manga-series" });
            Invoke(reader, "Back_Click", reader, new RoutedEventArgs());
            if (destination != typeof(ItemDetailPage) || parameter is not ItemDetailNavigationArgs { ContentId: "manga-series", LibraryId: 7 })
                throw new InvalidOperationException("The actual manga reader Back handler loses its owning series or library hint.");
            Set(reader, "_nextMangaChapter", new MangaChapter { ContentId = "chapter-two" });
            Invoke(reader, "NextChapter_Click", reader, new RoutedEventArgs());
            if (destination != typeof(EbookReaderPage) || parameter is not EbookReaderNavigation { ContentId: "chapter-two", LibraryId: 7 })
                throw new InvalidOperationException("The actual next-chapter handler loses its library context.");
            var choose = typeof(EbookReaderPage).GetMethod("ChooseVersion", BindingFlags.NonPublic | BindingFlags.Static)!;
            FileVersion[] scopedFiles = [new() { FileId = 7, Container = "pdf" }, new() { FileId = 42, Container = "epub" }];
            if (((FileVersion?)choose.Invoke(null, [scopedFiles, 7]))?.FileId != 7
                || ((FileVersion?)choose.Invoke(null, [scopedFiles, null]))?.FileId != 42
                || ((FileVersion?)choose.Invoke(null, [new[] { scopedFiles[0] }, 42]))?.FileId != 7)
                throw new InvalidOperationException("Reader explicit/default file selection can escape the versions authorized by scoped detail.");
        }
        finally { serviceField.SetValue(null, original); }
        Program.Log("PASS: PLAYBACK_SCOPE_COMPLETED actual native watch-cache scope/coalescing/stale-profile rejection, shuffle metadata/singleton and reader route context.");
    }

    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private sealed class ReaderServices(IServiceProvider original, SiloApiClient client, NavigationService navigation) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(EbooksApi) ? new EbooksApi(client) : type == typeof(CatalogApi) ? new CatalogApi(client) : type == typeof(NavigationService) ? navigation : original.GetService(type);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<string> WatchQueries = [];
        internal bool HoldWatch, Singleton;
        internal int HeldWatchRequests;
        internal TaskCompletionSource WatchStarted = Signal(), ReleaseWatch = Signal();
        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ResetWatchSignals() { WatchStarted = Signal(); ReleaseWatch = Signal(); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/series/series/seasons"))
                return Reply(new { items = new[] { new Season { SeasonNumber = 1 } } });
            if (path.EndsWith("/series/series/seasons/1/episodes"))
                return Reply(new { items = new Episode[] {
                    new() { ContentId = "episode-one", SeasonNumber = 1, EpisodeNumber = 1, Runtime = 61, Files = [new() { FileId = 1 }] },
                    new() { ContentId = "episode-two", SeasonNumber = 1, EpisodeNumber = 2, Runtime = 55, Files = [new() { FileId = 2 }] } } });
            if (path.Contains("/watch/"))
            {
                WatchQueries.Add(request.RequestUri.Query);
                if (HoldWatch) { HoldWatch = false; HeldWatchRequests++; WatchStarted.TrySetResult(); await ReleaseWatch.Task; }
                return Reply(new WatchDetailResponse { ContentId = path.Split('/').Last(), Type = "movie", Title = "Fixture" });
            }
            if (path.Contains("/shuffles")) return Reply(new Shuffle
            {
                Id = "fixture-shuffle", Scope = new() { Kind = "library", Id = "7", Title = "Movies" },
                Current = new MediaItem { ContentId = "current", Type = "movie" },
                Next = new MediaItem { ContentId = Singleton ? "current" : "next", Type = "movie", Title = "Next movie", Runtime = 20, PosterUrl = "https://fixture.invalid/poster", BackdropUrl = "https://fixture.invalid/backdrop" }
            });
            throw new InvalidOperationException("Unexpected isolated playback-scope route " + path);
        }
        private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
    }
}
