using System.Reflection;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Player;
using SiloPlayer.Controls;
using SiloPlayer.Views;

internal static partial class EpisodeQuickPlayNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(StackPanel parent)
    {
        using var fixture = new Fixture();
        await fixture.Spoilers.EnsureLoadedAsync();
        var frame = new Frame { Height = 1 };
        parent.Children.Add(frame); frame.Navigate(typeof(Page)); fixture.Navigation.Frame = frame;
        var navigations = 0;
        frame.Navigated += (_, _) => navigations++;
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        page.ViewModel.Item = new() { ContentId = "fixture-series", Type = "series" };
        typeof(ItemDetailViewModel).GetProperty("LibraryId")!.SetValue(page.ViewModel, 73);
        using var navigation = new CancellationTokenSource();
        typeof(ItemDetailPage).GetField("_navigationCts", Private)!.SetValue(page, navigation);
        typeof(ItemDetailPage).GetField("_currentContentId", Private)!.SetValue(page, "fixture-series");
        parent.Children.Add(page);
        var create = typeof(ItemDetailPage).GetMethod("CreateEpisodeCard", Private)!;
        var episode = new Episode
        {
            ContentId = "fixture-episode", Title = "Fixture episode", SeasonNumber = 1, EpisodeNumber = 2,
            Files = [new() { FileId = 42, Unreadable = false }],
            UserData = new() { PositionSeconds = 91, DurationSeconds = 1200, IsInProgress = true },
        };
        FrameworkElement Card(Episode item)
        {
            var result = (FrameworkElement)create.Invoke(page, [item, false])!;
            result.Width = 220; parent.Children.Add(result); return result;
        }
        var card = Card(episode);
        try
        {
            await Task.Delay(100); card.UpdateLayout();
            var play = Descendants<Button>(card).SingleOrDefault(button => AutomationProperties.GetName(button).StartsWith("Resume "));
            Check(play != null, "Playable actual episode still is missing its separate accessible Resume action.");
            var detail = Descendants<Button>(card).Single(button => button.Tag as string == episode.ContentId);
            Check(!Descendants<Button>(detail).Contains(play!), "Play action is nested inside detail navigation.");
            foreach (var width in new[] { 128d, 220d, 400d })
            {
                card.Width = width; card.UpdateLayout(); await Task.Delay(50);
                play!.Focus(FocusState.Keyboard); await Task.Delay(30);
                var layer = (Grid)VisualTreeHelper.GetParent(play);
                Check(layer.Opacity == 1 && layer.IsHitTestVisible, "Keyboard focus did not reveal episode actions.");
                var bounds = play.TransformToVisual(card).TransformBounds(new(0, 0, play.ActualWidth, play.ActualHeight));
                Check(bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= width + .5 && bounds.Bottom <= width * 9 / 16 + .5,
                    "Quick play escapes the actual still bounds on a narrow/tall card.");
                foreach (var other in layer.Children.OfType<Button>().Where(button => button != play))
                {
                    var otherBounds = other.TransformToVisual(card).TransformBounds(new(0, 0, other.ActualWidth, other.ActualHeight));
                    Check(bounds.Right <= otherBounds.Left || bounds.Left >= otherBounds.Right || bounds.Bottom <= otherBounds.Top || bounds.Top >= otherBounds.Bottom,
                        "Narrow episode play overlaps watched/more actions.");
                    Program.Log($"Episode rect/{width}: play={bounds}; {AutomationProperties.GetName(other)}={otherBounds}; no intersection.");
                }
                await MediaParityNativeFixture.CaptureAsync(card, $"episode-quickplay-{width}.png");
            }
            Check(fixture.Wire.Watches.Count == 0, "Focus or layout started playback.");
            EnsureNativeResolver();
            using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null");
            typeof(PlayerService).GetField("_mpv", Private)!.SetValue(fixture.Player, mpv);
            Invoke(play!);
            await Until(() => fixture.Wire.Starts.Count == 1 && play!.IsEnabled);
            Check(fixture.Wire.Watches.Single().Query == "?library_id=73", "Episode playback lost its captured library scope.");
            var start = fixture.Wire.Starts.Single();
            Check(start.GetProperty("file_id").GetString() == "42" && start.GetProperty("profile_id").GetString() == "fixture" &&
                start.GetProperty("start_position").GetDouble() == 91, "Quick play bypassed authorized profile/file selection or normal resume.");
            Check(navigations == 0, "Play unexpectedly entered detail navigation.");
            // An untouched episode must omit a zero override so the server can resume it.
            fixture.Wire.ResumePosition = 0;
            typeof(PlayerService).GetField("_watchDetailPrefetches", Private)!.GetValue(fixture.Player)!.GetType().GetMethod("Clear")!.Invoke(
                typeof(PlayerService).GetField("_watchDetailPrefetches", Private)!.GetValue(fixture.Player), null);
            fixture.Player.SetWatchTogetherPlayback(true);
            Invoke(play!); await Until(() => fixture.Wire.Starts.Count == 2 && play!.IsEnabled);
            Check(!fixture.Wire.Starts[1].TryGetProperty("start_position", out var zero) || zero.ValueKind == JsonValueKind.Null,
                "Default quick play sent a zero position that overrides server resume.");
            Check(fixture.Player.IsWatchTogetherPlayback && fixture.Player.IsPaused, "Quick play changed room authority or its pause intent.");
            fixture.Player.SetWatchTogetherPlayback(false);
            typeof(PlayerService).GetField("_mpv", Private)!.SetValue(fixture.Player, null);

            foreach (var files in new List<EpisodeFile>?[] { null, [], [new() { Unreadable = true }], [new() { Unreadable = null }], [new() { Unreadable = true }, new() { Unreadable = false }] })
            {
                var available = Card(new() { ContentId = "availability", Title = "Availability", Files = files! });
                await Task.Delay(30); available.UpdateLayout();
                var action = Descendants<Button>(available).SingleOrDefault(button => AutomationProperties.GetName(button).StartsWith("Play "));
                Check((action != null && action.IsEnabled && action.Visibility == Visibility.Visible) == (files?.Any(file => file.Unreadable != true) == true),
                    "Quick play does not follow authorized nullable unreadable file fields.");
                parent.Children.Remove(available);
            }

            var watchesBeforeMissingFiles = fixture.Wire.Watches.Count;
            var originalFiles = episode.Files;
            episode.Files = null!;
            Invoke(play!); await Task.Delay(50);
            Check(play!.IsEnabled && fixture.Wire.Watches.Count == watchesBeforeMissingFiles,
                "A missing file array at click time started playback or left the action disabled.");
            episode.Files = originalFiles;

            var spoiler = new Episode { ContentId = "spoiler-fixture", Title = "Protected episode", Overview = "Synthetic secret.",
                Files = [new() { FileId = 42 }], StillUrl = "https://quickplay.fixture.invalid/still" };
            var hiddenCard = Card(spoiler);
            await Until(() => Descendants<Image>(hiddenCard).Any(image => image.Source != null));
            Check(Descendants<EpisodeCardCaption>(hiddenCard).Single().OverviewText.Visibility == Visibility.Collapsed,
                "Play overlay exposed an unstarted spoiler overview.");
            var contrast = typeof(UpstreamMediaNativeFixture).GetMethod("ContrastAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var hiddenContrast = await (Task<double>)contrast.Invoke(null, [Descendants<Image>(hiddenCard).Single()])!;
            spoiler.UserData = new() { Played = true };
            var clearCard = Card(spoiler);
            await Until(() => Descendants<Image>(clearCard).Any(image => image.Source != null));
            var clearContrast = await (Task<double>)contrast.Invoke(null, [Descendants<Image>(clearCard).Single()])!;
            Check(hiddenContrast < clearContrast * .8, "Play overlay did not preserve blurred spoiler artwork.");
            await MediaParityNativeFixture.CaptureAsync(hiddenCard, "episode-quickplay-spoiler.png");
            Program.Log($"PASS: actual episode quick-play card retains hidden overview and blurred pixels ({hiddenContrast:F3} vs watched {clearContrast:F3}).");
            parent.Children.Remove(hiddenCard); parent.Children.Remove(clearCard);

            var count = fixture.Wire.Watches.Count;
            parent.Children.Remove(card); await Task.Delay(40); parent.Children.Add(card); await Task.Delay(40);
            Invoke(play!); await Task.Delay(80);
            Check(fixture.Wire.Watches.Count == count, "An unloaded/reloaded card revived its retired play callback.");
            parent.Children.Remove(card);

            foreach (var change in new[] { "profile", "account", "item", "library", "navigation" })
            {
                card = Card(episode); await Task.Delay(40); card.UpdateLayout();
                var old = Descendants<Button>(card).Single(button => AutomationProperties.GetName(button).StartsWith("Resume "));
                var gate = (SemaphoreSlim)typeof(PlayerService).GetField("_authoritySwitchGate", Private)!.GetValue(fixture.Player)!;
                await gate.WaitAsync(); Invoke(old); await Task.Delay(40);
                if (change == "profile") fixture.Auth.SelectProfile("profile-" + change, profile: new() { Id = "profile-" + change, Name = "Fixture" });
                else if (change == "account") { fixture.Auth.SetTokens("fixture-other", "fixture-refresh", 86400); fixture.Auth.SetCurrentUser(new() { Id = "other-user", Role = "user" }); }
                else if (change == "item") page.ViewModel.Item = new() { ContentId = "other-series", Type = "series" };
                else if (change == "library") { typeof(ItemDetailViewModel).GetProperty("LibraryId")!.SetValue(page.ViewModel, 74); page.ViewModel.IsLoading = !page.ViewModel.IsLoading; }
                else navigation.Cancel();
                gate.Release(); await Until(() => old.IsEnabled);
                Invoke(old); await Task.Delay(50);
                Check(fixture.Wire.Watches.Count == count, $"Queued/stale {change} episode callback prepared playback.");
                parent.Children.Remove(card);
                page.ViewModel.Item = new() { ContentId = "fixture-series", Type = "series" };
                typeof(ItemDetailViewModel).GetProperty("LibraryId")!.SetValue(page.ViewModel, 73);
            }
            await VerifyBuildersAsync(parent, episode);
            Invoke(detail); await Until(() => navigations == 1);
            Check(frame.Content is ItemDetailPage destination && destination.ViewModel.LibraryId == 73,
                "The separate primary episode action lost detail navigation/library scope.");
            Program.Log("PASS: actual episode stills preserve separate play/detail actions, keyboard reveal/invoke, narrow bounds, server availability, library/profile resume and zero omission; retired/queued navigation, account, profile, item and library callbacks do no work.");
        }
        finally { frame.Navigate(typeof(Page)); fixture.Navigation.Frame = null; parent.Children.Remove(frame); parent.Children.Remove(card); parent.Children.Remove(page); }
    }

    private static async Task VerifyBuildersAsync(StackPanel parent, Episode episode)
    {
        foreach (var type in new[] { "series", "season", "episode" })
        {
            var surface = new ItemDetailPage { Width = 900, Height = 720 };
            surface.ViewModel.Item = new() { ContentId = "builder-" + type, Type = type };
            using var lifetime = new CancellationTokenSource();
            typeof(ItemDetailPage).GetField("_navigationCts", Private)!.SetValue(surface, lifetime);
            typeof(ItemDetailPage).GetField("_currentContentId", Private)!.SetValue(surface, "builder-" + type);
            parent.Children.Add(surface);
            try
            {
                await Task.Delay(40);
                ((FrameworkElement)surface.FindName("ContentScroll")).Visibility = Visibility.Visible;
                Panel host;
                if (type == "episode")
                {
                    ((List<Episode>)typeof(ItemDetailPage).GetField("_siblingEpisodes", Private)!.GetValue(surface)!).Add(episode);
                    ((FrameworkElement)surface.FindName("SiblingEpisodesSection")).Visibility = Visibility.Visible;
                    ((FrameworkElement)surface.FindName("SiblingEpisodesScrollViewer")).Visibility = Visibility.Visible;
                    typeof(ItemDetailPage).GetMethod("BuildSiblingEpisodeCards", Private)!.Invoke(surface, null);
                    host = (Panel)surface.FindName("SiblingEpisodesPanel");
                }
                else
                {
                    surface.ViewModel.Episodes.Add(episode);
                    typeof(ItemDetailPage).GetMethod("BuildEpisodeRows", Private)!.Invoke(surface, null);
                    host = (Panel)surface.FindName("EpisodesPanel");
                }
                surface.UpdateLayout(); await Task.Delay(80);
                Check(Descendants<Button>(host).Any(button => AutomationProperties.GetName(button).StartsWith("Resume ") && button.ActualWidth > 0),
                    $"The actual {type} episode builder did not mount quick play.");
                Program.Log($"PASS: actual {type} episode builder mounts a measured separate Resume action.");
            }
            finally { lifetime.Cancel(); parent.Children.Remove(surface); }
        }
    }

    internal static async Task VerifyQueuedOwnerAsync()
    {
        using var fixture = new Fixture();
        var gate = (SemaphoreSlim)typeof(PlayerService).GetField("_authoritySwitchGate", Private)!.GetValue(fixture.Player)!;
        await gate.WaitAsync();
        var owner = fixture.Client.CaptureContext();
        var method = typeof(PlayerService).GetMethod("PlayAsync")!;
        var args = method.GetParameters().Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : null).ToArray();
        args[0] = "fixture-episode";
        var expected = Array.FindIndex(method.GetParameters(), parameter => parameter.Name == "expectedContext");
        if (expected >= 0) args[expected] = owner;
        var generation = typeof(PlayerService).GetField("_playRequestGeneration", Private)!;
        var before = (long)generation.GetValue(fixture.Player)!;
        var pending = (Task)method.Invoke(fixture.Player, args)!;
        fixture.Auth.SelectProfile("other", profile: new() { Id = "other", Name = "Other fixture" });
        gate.Release();
        await pending;
        Check(fixture.Wire.Watches.Count == 0 && (long)generation.GetValue(fixture.Player)! == before,
            "A queued departed profile canceled playback or prepared its episode under a new owner.");
        Program.Log("PASS: held actual authority gate retires a departed profile before cancellation or playback preparation.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FieldInfo _slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly IServiceProvider _original;
        internal Wire Wire { get; } = new();
        private readonly HttpClient _http;
        internal SiloApiClient Client { get; }
        internal AuthService Auth { get; }
        internal PlayerService Player { get; }
        internal CatalogApi Catalog { get; }
        internal NavigationService Navigation { get; } = new();
        internal ItemDetailPrefetchCache Cache { get; }
        internal EpisodeSpoilerPreferences Spoilers { get; }
        private readonly ImageService _images = new(Path.Combine(Program.ResultDirectory, "quickplay-images"));
        internal Fixture()
        {
            _original = (IServiceProvider)_slot.GetValue(null)!;
            _http = new(Wire); Client = new(_http); Client.SetBaseUrl("https://quickplay.fixture.invalid");
            Auth = new(Client, new AuthApi(Client), credentialStore: null);
            Auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
            Auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
            Catalog = new(Client); Cache = new(Catalog, Auth);
            var settings = new SettingsApi(Client);
            Spoilers = new(settings);
            Player = new(new PlaybackApi(Client), Catalog, Auth, Client,
                new SettingsService(Path.Combine(Program.ResultDirectory, "quickplay-settings")), settings);
            _slot.SetValue(null, new Services(_original, new()
            {
                [typeof(SiloApiClient)] = Client, [typeof(AuthService)] = Auth,
                [typeof(CatalogApi)] = Catalog, [typeof(SettingsApi)] = settings,
                [typeof(PlayerService)] = Player, [typeof(NavigationService)] = Navigation,
                [typeof(ItemDetailPrefetchCache)] = Cache,
                [typeof(EpisodeSpoilerPreferences)] = Spoilers,
                [typeof(HttpClient)] = _http, [typeof(ImageService)] = _images,
                [typeof(UICustomizationService)] = new UICustomizationService(settings),
            }, () => new(Catalog, Cache)));
        }
        public void Dispose()
        {
            Player.Dispose(); Spoilers.Dispose(); _images.Dispose(); Auth.Dispose(); _http.Dispose(); _slot.SetValue(null, _original);
        }
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<ItemDetailViewModel> factory) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(ItemDetailViewModel) ? factory() : values.GetValueOrDefault(type) ?? fallback.GetService(type);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool SubtitleAiEnabled;
        internal bool SubtitleAiTranslationEnabled = true, SubtitleAiTranscriptionEnabled;
        internal Func<CancellationToken, Task>? SubtitleAiStatusBoundary;
        internal List<Uri> Watches { get; } = [];
        internal List<JsonElement> Starts { get; } = [];
        internal double ResumePosition = 91;
        internal int WatchFailuresRemaining;
        internal string? HoldWatch;
        internal TaskCompletionSource WatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseWatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HoldCapability, HoldSettings, HoldStart, HoldStage;
        internal TaskCompletionSource BoundaryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseBoundary = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<(string Path, string? Profile)> Requests = [];
        internal bool AcceptStart;
        internal int Stops, Suggestions, BoxSetReads, MembershipReads;
        internal bool HoldStop, BoxSetPlayed, HoldMembership;
        internal TaskCompletionSource StopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource MembershipStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseMembership = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<JsonElement> Stages = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "quickplay.fixture.invalid", "External quick-play fixture request refused.");
            var path = request.RequestUri.AbsolutePath;
            if (SubtitleAiEnabled && path == "/api/v2/subtitles/ai/status")
            {
                if (SubtitleAiStatusBoundary is { } boundary) await boundary(ct);
                return Json(JsonSerializer.Serialize(new
                {
                    enabled = SubtitleAiTranslationEnabled, transcribe_enabled = SubtitleAiTranscriptionEnabled,
                }));
            }
            if (SubtitleAiEnabled && path == "/api/v2/subtitles/providers/status")
                return Json("""{"enabled":false}""");
            lock (Requests) Requests.Add((path, request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : null));
            if (path == "/still") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "audiobook-cover.png"))) };
            if (path == "/api/v2/catalog/search/capabilities") return Json("""{"box_sets":true}""");
            if (path == "/api/v2/user/libraries") return Json("""{"items":[{"id":73,"box_sets_enabled":true}]}""");
            if (path == "/api/v2/catalog/items/boxset-film")
                return Json("""{"content_id":"boxset-film","type":"movie","title":"Fixture movie","user_state":{}}""");
            if (path is "/api/v2/catalog/box-sets/7" or "/api/v2/catalog/items/boxset-film/box-set")
            {
                var membership = path.EndsWith("/box-set", StringComparison.Ordinal);
                if (membership)
                {
                    ++MembershipReads;
                    if (HoldMembership) { MembershipStarted.TrySetResult(); await ReleaseMembership.Task.WaitAsync(ct); }
                }
                else ++BoxSetReads;
                var detail = """{"id":"7","library_id":"73","title":"Shell refresh collection","movie_count":1,"played_count":COUNT,"items":[{"content_id":"boxset-film","type":"movie","title":"Fixture movie","user_state":{"played":PLAYED}}]}"""
                    .Replace("COUNT", BoxSetPlayed ? "1" : "0").Replace("PLAYED", BoxSetPlayed ? "true" : "false");
                return Json(membership ? "{\"box_set\":" + detail + "}" : detail);
            }
            if (path == "/api/v2/settings/contract/capabilities") return Json("""{"api_version":1,"manifest_revision":17,"supports_batched_effective":true}""");
            if (path == "/api/v2/settings/values/effective")
            {
                if (HoldSettings && request.RequestUri.Query.Contains("playback.preferred_quality", StringComparison.Ordinal))
                { BoundaryStarted.TrySetResult(); await ReleaseBoundary.Task.WaitAsync(ct); }
                return Json("""{"items":[{"key":"catalog.hide_unwatched_episode_images","value":true},{"key":"catalog.hide_unwatched_episode_overviews","value":true}]}""");
            }
            if (path.StartsWith("/api/v2/watch/"))
            {
                Watches.Add(request.RequestUri);
                if (path.EndsWith("/" + HoldWatch)) { WatchStarted.TrySetResult(); await ReleaseWatch.Task.WaitAsync(ct); }
                if (WatchFailuresRemaining > 0)
                {
                    --WatchFailuresRemaining;
                    return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"session-retiring\"}", Encoding.UTF8, "application/json") };
                }
                return Json("""{"content_id":"CONTENT","type":"episode","title":"Fixture episode","versions":[{"file_id":42,"duration":1200,"resolution":"1080p","container":"mkv","codec_video":"h264","codec_audio":"aac"}],"user_data":{"position_seconds":POSITION,"duration_seconds":1200,"is_in_progress":true}}""".Replace("\"type\":\"episode\"", path.EndsWith("/boxset-film") ? "\"type\":\"movie\"" : "\"type\":\"episode\"").Replace("CONTENT", path.Split('/')[^1]).Replace("POSITION", ResumePosition.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            if (path.EndsWith("/start"))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Starts.Add(json.RootElement.Clone());
                if (HoldStart) { BoundaryStarted.TrySetResult(); await ReleaseBoundary.Task.WaitAsync(ct); }
                if (AcceptStart) return Json("""{"protocol_version":3,"outcome":"playable","session_id":"fixture-session","playback_plan":{"plan_id":"fixture-plan","plan_attempt_key":"fixture-key","session_id":"fixture-session","effective_media_file_id":42,"delivery":"original_http","active_quality":"original","stream":{"url":"https://quickplay.fixture.invalid/stream/direct/fixture","protocol":"http_progressive"},"timeline":{"can_seek_anywhere":true},"source":{"bitrate_kbps":1536,"duration_seconds":1200},"selected_tracks":{"audio":{"id":"file:42:audio:0","index":0}},"subtitle":{"mode":"off","inventory":[]}}}""");
                // Record the real authorized request, then end before any media load.
                return new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\":\"fixture-denied\"}", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Delete)
            {
                ++Stops;
                if (HoldStop) { StopStarted.TrySetResult(); await ReleaseStop.Task.WaitAsync(ct); BoxSetPlayed = true; }
                return Json("""{"outcome":"stopped"}""");
            }
            if (path.Contains("/suggestions") && request.Method == HttpMethod.Post) { ++Suggestions; return Json("""{"suggestions":[]}"""); }
            if (path.EndsWith("/staged-selection"))
            {
                using var stage = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Stages.Add(stage.RootElement.Clone());
                if (HoldStage) { BoundaryStarted.TrySetResult(); await ReleaseBoundary.Task.WaitAsync(ct); }
                return Json("""{"room":{"room_id":"fixture-room","phase":"lobby","selected_content_id":"room-b","selection_mode":"host_pick","selection_revision":4,"self_can_manage_room":true}}""");
            }
            if (path == "/api/v2/playback/capabilities")
            {
                if (HoldCapability) { BoundaryStarted.TrySetResult(); await ReleaseBoundary.Task.WaitAsync(ct); }
                return Json("""{"allowed":true,"state":"available","installation_id":"fixture-install","protocol_versions":[3],"features":["fixed_media_file_v1"]}""");
            }
            return Json("""{"items":[],"settings":[],"sections":[],"profiles":[]}""");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var value2 in Descendants<T>(child)) yield return value2;
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(20);
        Check(done(), "Episode playback action did not finish.");
    }
}
