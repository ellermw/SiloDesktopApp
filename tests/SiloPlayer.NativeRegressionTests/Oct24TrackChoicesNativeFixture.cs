using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;

internal static class Oct24TrackChoicesNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://oct24-track-choices.invalid"); api.SetProfile("fixture");
        var playback = new PlaybackApi(api);
        slot.SetValue(null, new Services(previous, api, new CatalogApi(api), playback));
        var page = new ItemDetailPage { Width = 900, Height = 700 };
        Set(page, "_detailNetwork", (Func<(string, bool, string)>)(() => ("fixture", false, "ethernet")));
        var version = new FileVersion
        {
            FileId = 42, EffectiveAudioTrackIndex = 0,
            AudioTracks = [new() { Language = "en", Default = true }, new() { Language = "jpn", Title = "Main", EmbeddedTitle = "Original", Codec = "flac", Channels = 6, Layout = "5.1" }],
            SubtitleTracks = [new() { Index = 0, Language = "eng", Codec = "srt" }, new() { Index = 1, Language = "spa", Codec = "srt" }],
        };
        var detail = new WatchDetailResponse { ContentId = "episode", SeriesId = "series", Versions = [version], EffectiveSubtitleLanguage = "es", EffectiveSubtitleMode = "auto", EffectiveShowForcedSubtitles = true };
        Set(page, "_selectedVersion", version); Set(page, "_watchDetail", detail);
        parent.Children.Add(page);
        try
        {
            await Task.Delay(100);
            bool VersionPinned() => (bool)typeof(ItemDetailPage).GetProperty("IsSelectedVersionPinned", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Require(!VersionPinned(), "Detail's automatic default version was treated as a viewer pin.");
            var otherVersion = new FileVersion { FileId = 43, Resolution = "720p", AudioTracks = [new() { Language = "en", Default = true }] };
            var versions = new List<FileVersion> { otherVersion, version };
            Call(page, "BuildVersionFlyout", versions, false, null, null);
            var versionFlyout = (MenuFlyout)page.FindName("VersionFlyout"); versionFlyout.ShowAt(page); await Task.Delay(60);
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(versionFlyout.Items.OfType<MenuFlyoutItem>().Single(item => item.Tag is int id && id == 43)).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(VersionPinned() && Get<FileVersion>(page, "_selectedVersion")?.FileId == 43,
                "Actual detail version picker did not record an explicit viewer choice.");
            Require(((FileVersion)Call(page, "RetainExplicitVersion", versions, version)!).FileId == 43,
                "A later automatic watch response replaced the viewer's explicit version.");
            versionFlyout.ShowAt(page); await Task.Delay(60);
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(versionFlyout.Items.OfType<MenuFlyoutItem>().Single(item => item.Tag is string tag && tag == "auto")).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(!VersionPinned() && ((TextBlock)page.FindName("VersionSummaryText")).Text.StartsWith("Auto ·", StringComparison.Ordinal),
                "Actual Auto menu command did not clear the viewer pin and show automatic quality.");
            Call(page, "SelectExplicitVersion", otherVersion);
            api.SetProfile("replacement");
            Require(!VersionPinned(), "A previous profile's version pin leaked into a new owner.");
            api.SetProfile("fixture"); Set(page, "_selectedVersion", version);
            Call(page, "RetainExplicitVersion", new List<FileVersion> { version }, version);
            Require(!VersionPinned(), "An automatic default after owner retirement retained an old pin.");
            Program.Log("PASS: detail automatic default stays adaptable; real version picker pins; asynchronous default refresh preserves the pick; profile retirement clears its authority.");
            Call(page, "BuildAudioTracksFlyout", version);
            var flyout = (Flyout)page.FindName("AudioTracksFlyout");
            var panel = (StackPanel)page.FindName("AudioTracksFlyoutContent");
            flyout.ShowAt((FrameworkElement)page.FindName("AudioTracksButton")); await Task.Delay(60);
            Invoke(panel.Children.OfType<Button>().ElementAt(2));
            await Until(() => wire.Audio.Count == 1 && Get<int?>(page, "_selectedAudioTrackIndex") == 1);
            Call(page, "BuildAudioTracksFlyout", version);
            Require(Get<int?>(page, "_selectedAudioTrackIndex") == 1, "Same-file repaint erased the explicit audio choice.");
            Call(page, "BuildAudioTracksFlyout", otherVersion);
            Require(Get<int?>(page, "_selectedAudioTrackIndex") == null, "Different-file repaint retained an invalid audio index.");
            Call(page, "BuildAudioTracksFlyout", version);
            var put = wire.Audio[0]; Require(put.Method == HttpMethod.Put && put.Body!.Value.GetProperty("track_signature").GetProperty("embedded_title").GetString() == "Original", "Actual detail audio click did not save the selected signature.");
            flyout.ShowAt((FrameworkElement)page.FindName("AudioTracksButton")); await Task.Delay(60);
            Invoke(panel.Children.OfType<Button>().First());
            await Until(() => wire.Audio.Count == 2 && Get<int?>(page, "_selectedAudioTrackIndex") == null);
            Require(wire.Audio[1].Method == HttpMethod.Delete && ((TextBlock)page.FindName("AudioTracksSummary")).Text.StartsWith("Auto:", StringComparison.Ordinal), "Actual Auto audio click did not delete the saved preference.");
            wire.FailNext = true;
            flyout.ShowAt((FrameworkElement)page.FindName("AudioTracksButton")); await Task.Delay(60);
            Invoke(panel.Children.OfType<Button>().ElementAt(2));
            await Until(() => wire.Audio.Count == 3 && Get<int?>(page, "_selectedAudioTrackIndex") == null);
            Require(((TextBlock)page.FindName("AudioTracksSummary")).Text.StartsWith("Auto:", StringComparison.Ordinal), "Failed native audio write left an unsaved selection visible.");

            var automatic = (VersionSubtitleTrack?)Call(page, "ResolveAutoSubtitle", version.SubtitleTracks);
            Require(automatic?.Language == "spa", "Native Auto preview compared English profile/audio instead of Spanish subtitle preference.");
            detail.EffectiveSubtitleLanguage = "";
            Require(Call(page, "ResolveAutoSubtitle", version.SubtitleTracks) == null, "Explicit empty subtitle language was replaced by profile defaults.");
            detail.EffectiveSubtitleLanguage = "es";
            var signature = new SubtitleTrackSignature { Source = "embedded", Language = "spa", Codec = "srt", Label = "Spanish" };
            await (Task)Call(page, "PersistPrePlaySubtitlePreferenceAsync", signature, 1, "always")!;
            Require(wire.Subtitles.Count == 1 && !wire.Subtitles[0].TryGetProperty("show_forced_subtitles", out _), "Detail subtitle pick pinned inherited forced-subtitle state.");
            wire.GlobalForced = false;
            Require(!wire.EffectiveForced, "A later global forced-subtitle change did not reach a series without an override.");
            await playback.SaveSubtitlePrefsAsync("series", new() { SubtitleMode = "always", SubtitleTrackIndex = 1, ShowForcedSubtitles = true });
            await (Task)Call(page, "PersistPrePlaySubtitlePreferenceAsync", signature, 1, "always")!;
            Require(wire.EffectiveForced, "A subtitle pick removed a deliberate forced-subtitle override.");
            Set(page, "_selectedSubtitleIndex", (int?)1);
            Set(page, "_selectedSubtitleSignature", signature);
            Call(page, "BuildSubtitlesPopoverFlyout", version, true);
            Require(Get<int?>(page, "_selectedSubtitleIndex") == 1, "Same-file repaint erased an explicit subtitle choice.");
            version.Resolution = "2160p"; version.EditionKey = "";
            otherVersion.EditionKey = "";
            var extended = new FileVersion { FileId = 44, Resolution = "1080p", EditionKey = "extended" };
            var allFiles = new List<FileVersion> { version, otherVersion, extended };
            var variants = new List<PlaybackVariant>
            {
                new() { EditionKey = "", Parts = [new() { PartIndex = 0, Versions = [version, otherVersion] }] },
                new() { EditionKey = "extended", EditionRaw = "Extended", Parts = [new() { PartIndex = 0, Versions = [extended] }] },
            };
            detail.Versions = allFiles; detail.PlaybackVariants = variants;
            Call(page, "EnsureDetailVersionOwner");
            var selection = Get<DetailVersionSelection>(page, "_detailVersions")!;
            selection.SetQuality("720p", true);
            Call(page, "ConfigureVersionSelectors", allFiles, variants, version, false);
            var editions = (MenuFlyout)page.FindName("EditionFlyout");
            editions.ShowAt(page); await Task.Delay(60);
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(editions.Items.OfType<MenuFlyoutItem>().Single(item => item.Tag is string key && key == "extended")).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(!VersionPinned() && Get<FileVersion>(page, "_selectedVersion")?.FileId == 44 && selection.EditionKey == "extended",
                $"Choosing an edition failed: pinned={VersionPinned()}, file={Get<FileVersion>(page, "_selectedVersion")?.FileId}, edition={selection.EditionKey}, current={ReferenceEquals(selection, Get<DetailVersionSelection>(page, "_detailVersions"))}.");
            Require(Get<int?>(page, "_selectedSubtitleIndex") == null, "Edition change retained the previous file's subtitle index.");
            editions.ShowAt(page); await Task.Delay(60);
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(editions.Items.OfType<MenuFlyoutItem>().Single(item => item.Tag is string key && key == "")).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(!VersionPinned() && Get<FileVersion>(page, "_selectedVersion")?.FileId == 43,
                "Standard-edition Auto ignored the resolution cap.");
            selection.SetQuality("auto", true);
            Call(page, "UpdatePlayButton");
            Require(Get<FileVersion>(page, "_selectedVersion")?.FileId == 42 && selection.EditionKey == "",
                "A quality refresh changed the selected edition instead of its Auto file.");
            Program.Log("PASS: real Auto and edition menu commands preserve edition without pinning; capped Auto refreshes its file; same-file track choices survive and changed-file choices reset.");
            Set(page, "_playableContentId", "episode");
            await (Task)Call(page, "RefreshWatchDetailPresentationAsync", detail)!;
            var inventoryBefore = wire.InventoryReads;
            wire.HasDownloadedSubtitle = true;
            Set(page, "_selectedSubtitleIndex", (int?)1);
            Set(page, "_selectedSubtitleSignature", signature);
            await (Task)Call(page, "RefreshWatchDetailPresentationAsync", detail)!;
            Require(wire.InventoryReads == inventoryBefore + 1 && Get<List<SubtitleEntry>>(page, "_downloadedSubtitles")?.Single().Id == 901,
                "Same-file watch refresh did not fetch the newly downloaded subtitle.");
            Require(Get<int?>(page, "_selectedSubtitleIndex") == 1, "Inventory refresh erased the current pre-play subtitle choice.");
            Set(page, "_selectedAudioTrackIndex", (int?)1);
            Call(page, "RetirePrePlayTrackChoices");
            await (Task)Call(page, "RefreshWatchDetailPresentationAsync", detail)!;
            Require(Get<int?>(page, "_selectedAudioTrackIndex") == null && Get<int?>(page, "_selectedSubtitleIndex") == null,
                "Playback return retained stale pre-play track overrides.");
            Require(((string)Call(page, "DetailVersionSummary", new FileVersion { FileId = 99 })!).EndsWith("99", StringComparison.Ordinal),
                "Metadata-free Auto summary ended in an empty separator.");
            var originalItem = page.ViewModel.Item;
            page.ViewModel.Item = new SiloPlayer.Core.Models.Catalog.MediaItemDetail
            {
                ContentId = "episode", Type = "movie", Versions = allFiles,
                UserData = new() { PositionSeconds = 42, DurationSeconds = 200 },
            };
            Set(page, "_watchDetail", null);
            Call(page, "ChooseDetailAuto", allFiles, variants, "");
            Require(((MenuFlyout)page.FindName("VersionFlyout")).Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Play from Start"),
                "Pre-watch Auto selection dropped the catalog resume action.");
            page.ViewModel.Item = new SiloPlayer.Core.Models.Catalog.MediaItemDetail { ContentId = "book", Type = "audiobook" };
            ((FrameworkElement)page.FindName("QualityBadgesPanel")).Visibility = Visibility.Collapsed;
            Call(page, "RepaintDetailVersions");
            Require(((FrameworkElement)page.FindName("QualityBadgesPanel")).Visibility == Visibility.Collapsed,
                "Quality refresh exposed movie badges on the audiobook surface.");
            page.ViewModel.Item = originalItem;
            Set(page, "_watchDetail", detail);
            await VerifyQualityReadsAsync(page, api, wire);
            Program.Log("PASS: same-file download inventory refreshes without losing choices; playback return retires them; catalog resume and metadata-free Auto labels remain usable; audiobook badges stay hidden; real settings reads reject stale revisions, content and owners and recover after failure.");
            var range = typeof(ItemDetailPage).GetMethod("FormatTrackDynamicRange", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [new VersionVideoTrack { DolbyVision = "Profile 8", VideoRangeType = "DOVIWithHDR10Plus", Hdr10Plus = true }]) as string;
            Require(range?.Contains("HDR10 compatible", StringComparison.Ordinal) == true && range.Contains("HDR10+", StringComparison.Ordinal), "Dolby Vision/HDR10+ detail omits HDR10 compatibility.");
            Program.Log("PASS: actual native audio click saves signature, Auto deletes, failed save rolls back; subtitle preview respects Spanish/empty preferences; detail save omits inherited forced state and preserves intentional override; DV/HDR10+ detail includes HDR10 compatibility.");
        }
        finally
        {
            ((Flyout)page.FindName("AudioTracksFlyout")).Hide(); parent.Children.Remove(page); slot.SetValue(null, previous);
        }
    }
    private static async Task VerifyQualityReadsAsync(ItemDetailPage page, SiloApiClient api, Wire wire)
    {
        Call(page, "StopObservingVersionSelection");
        using var navigation = new CancellationTokenSource();
        Set(page, "_navigationCts", navigation);
        Set(page, "_currentContentId", "episode");
        DetailVersionSelection Fresh()
        {
            Call(page, "EnsureDetailVersionOwner");
            var state = new DetailVersionSelection();
            Set(page, "_detailVersions", state);
            return state;
        }
        Task Refresh() => (Task)Call(page, "RefreshVersionQualityAsync")!;
        TaskCompletionSource<HttpResponseMessage> Queue()
        {
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.QualityReads.Enqueue(response);
            return response;
        }
        try
        {
            var state = Fresh();
            var failed = Queue(); failed.SetResult(new(HttpStatusCode.BadRequest) { Content = new StringContent("{}") });
            await Refresh(); Require(!state.QualitySettled && state.EditionKey == null, "Failed settings read fixed an edition.");
            var recovered = Queue(); recovered.SetResult(QualityResponse("1080p"));
            await Refresh(); Require(state.QualitySettled && state.Quality == "1080p", "Successful settings retry did not settle quality.");

            state = Fresh();
            var old = Queue(); var older = Refresh();
            var latest = Queue(); latest.SetResult(QualityResponse("720p"));
            await Refresh(); old.SetResult(QualityResponse("2160p")); await older;
            Require(state.Quality == "720p", "Older settings revision replaced the latest quality.");

            state = Fresh();
            var retiredContent = Queue(); var loadingContent = Refresh();
            Set(page, "_currentContentId", "replacement");
            retiredContent.SetResult(QualityResponse("2160p")); await loadingContent;
            Require(!state.QualitySettled, "Retired content accepted a late settings response.");
            Set(page, "_currentContentId", "episode");

            state = Fresh();
            var retiredOwner = Queue(); var loadingOwner = Refresh();
            api.SetProfile("another-profile");
            retiredOwner.SetResult(QualityResponse("2160p")); await loadingOwner;
            Require(!state.QualitySettled, "Retired profile accepted a late settings response.");
            api.SetProfile("fixture");
            var metered = false;
            Set(page, "_detailNetwork", (Func<(string, bool, string)>)(() => (metered ? "mobile" : "wired", metered, metered ? "cellular" : "ethernet")));
            state = Fresh(); state.SelectEdition("extended");
            var main = Queue(); main.SetResult(QualityResponse("2160p")); await Refresh();
            Require(state.Quality == "2160p", "Unmetered detail did not use main quality.");
            metered = true; var mobile = Queue(); mobile.SetResult(QualityResponse("2160p"));
            Call(page, "VersionNetworkChanged", new object());
            await Until(() => state.QualitySettled && state.Quality == "720p");
            Require(state.EditionKey == "extended", "Metered transition replaced the selected edition.");
            var cellularSetting = Queue(); cellularSetting.SetResult(QualityResponse("2160p", "480p"));
            Call(page, "VersionSettingChanged", "playback.cellular_preferred_quality");
            await Until(() => state.QualitySettled && state.Quality == "480p");
            state.SelectFile(new FileVersion { FileId = 55, EditionKey = "extended" });
            metered = false; var back = Queue(); back.SetResult(QualityResponse("2160p"));
            Call(page, "VersionNetworkChanged", new object());
            await Until(() => state.QualitySettled && state.Quality == "2160p");
            Require(state.ManualFileId == 55 && state.EditionKey == "extended", "Network transition discarded an explicit file pin.");
            state = Fresh(); var retiredNetwork = Queue(); var loadingNetwork = Refresh();
            metered = true; retiredNetwork.SetResult(QualityResponse("2160p")); await loadingNetwork;
            Require(!state.QualitySettled, "A settings response from a departed network was accepted.");
            Program.Log("PASS: real network/settings handlers switch main/cellular limits, retain edition and manual pin, and reject a departed-network response.");
        }
        finally { Set(page, "_navigationCts", null); }
    }
    private static HttpResponseMessage QualityResponse(string quality, string cellular = "720p") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { items = new[] { new { key = "playback.preferred_quality", value = quality }, new { key = "playback.cellular_preferred_quality", value = cellular } } })),
    };
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 150 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native track choice did not settle."); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static T? Get<T>(object target, string name) => (T?)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private sealed class Services(IServiceProvider fallback, SiloApiClient api, CatalogApi catalog, PlaybackApi playback) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(SiloApiClient) ? api : type == typeof(CatalogApi) ? catalog : type == typeof(PlaybackApi) ? playback : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        public List<(HttpMethod Method, JsonElement? Body)> Audio = [];
        public List<JsonElement> Subtitles = [];
        public bool FailNext, GlobalForced = true;
        public bool HasDownloadedSubtitle;
        public int InventoryReads;
        public Queue<TaskCompletionSource<HttpResponseMessage>> QualityReads = new();
        private bool? _forcedOverride;
        public bool EffectiveForced => _forcedOverride ?? GlobalForced;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "oct24-track-choices.invalid") throw new InvalidOperationException("Native track fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            JsonElement? body = request.Content == null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
            if (path == "/api/v2/settings/contract/capabilities")
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"api_version":1,"manifest_revision":20,"supports_batched_effective":true}""") };
            if (path == "/api/v2/settings/values/effective")
                return QualityReads.Count > 0 ? await QualityReads.Dequeue().Task.WaitAsync(ct) : QualityResponse("auto");
            if (request.Method == HttpMethod.Get && path.StartsWith("/api/v2/subtitles/", StringComparison.Ordinal))
            {
                InventoryReads++;
                return new(HttpStatusCode.OK) { Content = new StringContent(HasDownloadedSubtitle
                    ? "{\"subtitles\":[{\"id\":901,\"media_file_id\":42,\"language\":\"es\",\"format\":\"srt\",\"release_name\":\"Fresh subtitle\"}]}" : "{\"subtitles\":[]}") };
            }
            if (path.Contains("/audio-prefs/", StringComparison.Ordinal))
            {
                Audio.Add((request.Method, body));
                if (FailNext) { FailNext = false; return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"code\":\"fixture_refused\",\"message\":\"Fixture refused\"}}") }; }
            }
            if (path.Contains("/subtitle-prefs/", StringComparison.Ordinal) && body.HasValue)
            {
                Subtitles.Add(body.Value);
                if (body.Value.TryGetProperty("show_forced_subtitles", out var forced)) _forcedOverride = forced.GetBoolean();
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
