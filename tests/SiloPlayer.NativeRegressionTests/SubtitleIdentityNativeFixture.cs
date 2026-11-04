using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static partial class SubtitleIdentityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, bool unresolvedOnly = false, bool regionalOnly = false)
    {
        var presentation = Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE");
        var aiCallback = presentation is "presentation-ai-post" or "presentation-ai-post-current" or "presentation-ai-post-access" or
            "presentation-ai-job" or "presentation-ai-job-current" or "presentation-ai-job-access" or "presentation-ai-refresh" or "presentation-ai-refresh-access" or
            "presentation-ai-prefetch" or "presentation-ai-prefetch-access" or "presentation-ai-cues" or "presentation-ai-cues-access";
        var presentationOnly = aiCallback || presentation is "presentation-flyout" or "presentation-osc" or "presentation-ai-menu" or "presentation-ai-dialog" or "presentation-ai-profile" or "presentation-ai-access";
        var assets = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity"));
        if (presentation == "review-existing-ai-retirement")
        {
            await AiSameFileReplanAsync(assets, retirement: "load");
            return;
        }
        if (presentation is "review-ai-load" or "review-ai-timeout-load" or "review-ai-aba" or "review-ai-same-file" or "review-owned-foreign-grant" or "review-cue-owned-cleanup" or "review-cue-positive" or "review-ai-newer-native-cue" or "review-ai-newer-off-cue" or "review-ai-newer-off-completion")
        {
            await ReviewOwnershipAsync(assets, presentation);
            return;
        }
        if (presentation is "preplay-legacy" or "preplay-legacy-retired")
        {
            await LegacyPrePlayAsync(assets, presentation == "preplay-legacy-retired");
            return;
        }
        using var subtitles = new SubtitleServer(File.ReadAllBytes(Path.Combine(assets, "english.srt")));
        if (!unresolvedOnly && !regionalOnly && !presentationOnly)
        {
            subtitles.MediaBytes = File.ReadAllBytes(Path.Combine(assets, "tracks.mkv"));
            await AiSameFileReplanAsync(assets);
            await AiSameFileReplanAsync(assets, retirement: "load");
            await AiSameFileReplanAsync(assets, retirement: "timeout");
            await RefreshedDownloadedSubtitlesAsync(assets);
            await RefreshedDownloadedSubtitlesAsync(assets, storedIdentity: false);
            await RefreshedDownloadedSubtitlesAsync(assets, storedIdentity: false, trackIdentity: false);
            await BurnedInTextSubtitlesAsync(assets);
            await FreshPrePlayWithoutRouteAsync(subtitles.Origin);
        }
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://subtitle-identity.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "subtitle-identity")), new SettingsApi(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var session = new PlaybackStartResponse { SessionId = "synthetic", ProtocolVersion = 3, MediaFileId = 42, PlayMethod = "direct" };
        session.SubtitleUrls = JsonSerializer.Deserialize<List<SubtitleTrackInfo>>("""
            [{"index":0,"language":"en","codec":"srt","source":"external"},
             {"index":1,"language":"en","codec":"webvtt","source":"embedded","stream_index":77,"container_track_id":"99"},
             {"index":2,"language":"en","codec":"subrip","source":"embedded","stream_index":1,"container_track_id":"2"},
             {"index":3,"language":"en","source":"embedded","delivery":"burn_in_only","unplayable":true},
             {"index":4,"language":"ja","codec":"subrip","source":"embedded","stream_index":2,"container_track_id":"3"},
             {"index":7,"language":"it","codec":"srt","source":"downloaded"}]
            """, options)!;
        foreach (var track in session.SubtitleUrls) track.Url = subtitles.Origin + "/captions.srt";
        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
        manager.UseWatchDetail(new() { ContentId = "movie", SeriesId = "series", Versions = [new() { FileId = 42 }] });
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "movie");
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); mpv.Pause();
        try
        {
            if (presentationOnly)
            {
                if (aiCallback || presentation is "presentation-ai-menu" or "presentation-ai-dialog" or "presentation-ai-profile" or "presentation-ai-access")
                    await VerifyAiPresentationAsync(parent, service, session, mpv, wire, presentation);
                else await VerifyPresentationAsync(parent, service, session, mpv, wire, presentation!);
                return;
            }
            if (regionalOnly)
            {
                var initial = session.SubtitleUrls.Single(t => t.Index == 4);
                await service.SetSubtitleTrackAndPersistAsync(5, initial.Language, initial);
                Require(mpv.GetPropertyDouble("sid") == 2, "Regional fixture did not establish the previous native choice.");
                var regionalWrites = wire.PreferenceWrites;
                var regional = new SubtitleTrackInfo { Index = 11, Source = "embedded", Codec = "subrip", Language = "en-GB", Url = subtitles.Origin + "/regional.srt" };
                session.SubtitleUrls.Add(regional);
                await service.SetSubtitleTrackAndPersistAsync(12, regional.Language, regional);
                var sidecars = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(sidecars.TryGetValue(11, out var regionalSid) && regionalSid > 2 && mpv.GetPropertyDouble("sid") == regionalSid &&
                    service.ActiveSubtitleServerIndex == 11 && wire.PreferenceWrites == regionalWrites + 1 && mpv.IsPaused,
                    "No-ID en-GB request substituted the unqualified loaded eng track instead of executing its authorized sidecar.");
                var noRoute = new SubtitleTrackInfo { Index = 12, Source = "embedded", Codec = "subrip", Language = "en-GB" };
                session.SubtitleUrls.Add(noRoute);
                regionalWrites = wire.PreferenceWrites;
                await service.SetSubtitleTrackAndPersistAsync(13, noRoute.Language, noRoute);
                Require(service.ActiveSubtitleServerIndex == 11 && mpv.GetPropertyDouble("sid") == regionalSid &&
                    wire.PreferenceWrites == regionalWrites && mpv.IsPaused,
                    "Regional metadata mismatch without a sidecar changed the previous executed route or preference.");
                Program.Log("PASS: no-ID regional request refuses unqualified native language, executes its authorized sidecar, and preserves that route/preferences/pause when the next mismatched choice has no sidecar.");
                return;
            }
            if (unresolvedOnly)
            {
                var previousTrack = session.SubtitleUrls.Single(t => t.Index == 2);
                await service.SetSubtitleTrackAndPersistAsync(3, previousTrack.Language, previousTrack);
                var noRoute = JsonSerializer.Deserialize<SubtitleTrackInfo>("""{"index":9,"source":"embedded","codec":"webvtt","language":"en","stream_index":99,"container_track_id":"99"}""", options)!;
                session.SubtitleUrls.Add(noRoute);
                var previousIndex = service.ActiveSubtitleServerIndex;
                var previousSid = mpv.GetPropertyDouble("sid"); var preferenceWrites = wire.PreferenceWrites;
                var noRouteReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var notice = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var retainedHighlight = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Notice(string[] message)
                {
                    if (message.FirstOrDefault() == "silo-identity-notice-ready") noRouteReady.TrySetResult();
                    if (message.FirstOrDefault() == "silo-identity-notice" && message.Length > 1) notice.TrySetResult(message[1]);
                    if (message.FirstOrDefault() == "silo-identity-retained" && message.Length > 1) retainedHighlight.TrySetResult(message[1]);
                }
                mpv.ScriptMessageReceived += Notice;
                try
                {
                    var noticeProbe = Path.Combine(Program.ResultDirectory, "identity-notice.lua");
                    await File.WriteAllTextAsync(noticeProbe, """
                        local mp = require 'mp'
                        mp.register_script_message('osc-show-notice', function(payload)
                            mp.commandv('script-message', 'silo-identity-notice', payload)
                        end)
                        mp.register_script_message('osc-set-active-subtitle', function(index)
                            mp.commandv('script-message', 'silo-identity-retained', index)
                        end)
                        mp.commandv('script-message', 'silo-identity-notice-ready')
                        """);
                    Call(mpv, "Command", (object)new[] { "load-script", noticeProbe });
                    await noRouteReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await service.SetSubtitleTrackAndPersistAsync(10, "en", noRoute);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 9, true)!;
                    Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid && wire.PreferenceWrites == preferenceWrites,
                        "Unresolved native identity without a supported URL changed the active choice or preferences.");
                    using var noticePayload = JsonDocument.Parse(await notice.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                    Require(await retainedHighlight.Task.WaitAsync(TimeSpan.FromSeconds(4)) == previousIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "Refused no-route subtitle did not restore the retained OSC highlight.");
                    Require(noticePayload.RootElement.GetProperty("message").GetString()!.Contains("another subtitle", StringComparison.OrdinalIgnoreCase),
                        "Refused no-route subtitle did not provide an actionable notice.");
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, true);
                    await service.SetSubtitleTrackAndPersistAsync(10, "en", noRoute);
                    Require(wire.PreferenceWrites == preferenceWrites && (int?)Get(service, "_pendingSubtitlePreferenceIndex") == 9,
                        "A choice made during loading persisted before native execution.");
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, false);
                    Call(service, "LoadSubtitles");
                    mpv.SetSubtitleTrack(0); // Replacement loads suppress unverified subtitles.
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 9, false)!;
                    Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid &&
                        wire.PreferenceWrites == preferenceWrites && manager.SubtitleSelectionForLoad == previousIndex &&
                        Get(service, "_pendingInitialServerSubtitleIndex") == null && Get(service, "_pendingSubtitlePreferenceIndex") == null,
                        "Load-time refusal failed to restore the last executed SID, intent and pending preference.");

                    var deferredJapanese = session.SubtitleUrls.Single(t => t.Index == 4);
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, true);
                    await service.SetSubtitleTrackAndPersistAsync(5, deferredJapanese.Language, deferredJapanese);
                    Require(wire.PreferenceWrites == preferenceWrites, "A valid deferred choice wrote its preference too early.");
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, false);
                    Call(service, "LoadSubtitles");
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 4, false)!;
                    Require(service.ActiveSubtitleServerIndex == 4 && mpv.GetPropertyDouble("sid") == 2 &&
                        wire.PreferenceWrites == preferenceWrites + 1, "A successful deferred choice did not persist exactly once after execution.");

                    await service.SetSubtitleTrackAndPersistAsync(3, previousTrack.Language, previousTrack);
                    preferenceWrites = wire.PreferenceWrites;
                    var previousUrl = previousTrack.Url;
                    previousTrack.Url = "";
                    Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("remux", subtitles.Origin + "/video.mkv"));
                    Call(service, "LoadSubtitles");
                    mpv.SetSubtitleTrack(0);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", previousIndex, false)!;
                    Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                        manager.SubtitleSelectionForLoad == previousIndex && wire.PreferenceWrites == preferenceWrites,
                        "A no-route remux retained a false highlight or erased the viewer's native subtitle intent.");
                    previousTrack.Url = previousUrl;
                    Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
                    Call(service, "LoadSubtitles");
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", manager.SubtitleSelectionForLoad, false)!;
                    Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid &&
                        wire.PreferenceWrites == preferenceWrites,
                        "Returning to direct play did not retry the retained native subtitle intent.");

                    var busy = new SubtitleTrackInfo { Index = 10, Source = "embedded", Codec = "subrip", Language = "en",
                        StreamIndex = 99, ContainerTrackId = "99", Url = subtitles.Origin + "/busy.srt" };
                    session.SubtitleUrls.Add(busy);
                    subtitles.Fail = true;
                    preferenceWrites = wire.PreferenceWrites;
                    var cache = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                    manager.RecordLocalSubtitleSelection(previousIndex, isAutomatic: true);
                    var automaticIntent = manager.CaptureLocalSubtitleIntent();
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", busy.Index, true)!;
                    Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid &&
                        wire.PreferenceWrites == preferenceWrites && !cache.ContainsKey(busy.Index) && manager.SubtitleSelectionForLoad == previousIndex &&
                        manager.CaptureLocalSubtitleIntent() == automaticIntent && manager.IsSubtitleSelectionAutomatic,
                        "Failed sidecar add cached or persisted an unchanged native SID.");

                    var castTracks = service.CaptureSiloCastState()["subtitleTracks"]!.AsArray();
                    Require(castTracks.All(t => t!["trackId"]!.GetValue<int>() is not (4 or 10)),
                        "SiloCast advertised an explicitly unplayable or unresolved native-only subtitle.");
                    Require(castTracks.Any(t => t!["trackId"]!.GetValue<int>() == previousIndex + 1) &&
                        castTracks.Any(t => t!["trackId"]!.GetValue<int>() == busy.Index + 1),
                        "SiloCast hid an executable native or supported sidecar route.");
                    foreach (var sid in new[] { 4, 10, 11 })
                        await ExpectUnavailableAsync(() => service.ApplySiloCastControlAsync(new() { ["name"] = "select_subtitle_track", ["trackId"] = sid }));
                    Require(service.ActiveSubtitleServerIndex == previousIndex && wire.PreferenceWrites == preferenceWrites,
                        "A refused SiloCast command changed the selected subtitle or preference.");
                    await service.SetSubtitleTrackAndPersistAsync(0, null, null);
                    preferenceWrites = wire.PreferenceWrites;
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", busy.Index, true)!;
                    Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                        wire.PreferenceWrites == preferenceWrites && !cache.ContainsKey(busy.Index),
                        "Failed sidecar add while Off guessed SID 1 or persisted the failed choice.");
                    manager.RecordLocalSubtitleSelection(null, isAutomatic: true);
                    automaticIntent = manager.CaptureLocalSubtitleIntent();
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, true);
                    await service.SetSubtitleTrackAndPersistAsync(busy.Index + 1, busy.Language, busy);
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, false);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", busy.Index, false)!;
                    Require(manager.CaptureLocalSubtitleIntent() == automaticIntent && manager.IsSubtitleSelectionAutomatic &&
                        service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                        Get(service, "_pendingSubtitlePreferenceIndex") == null && wire.PreferenceWrites == preferenceWrites,
                        "A refused deferred sidecar pick converted automatic Off into an explicit viewer choice.");
                    // A transient sidecar failure on reload must affect only this
                    // file's execution, not turn the viewer's session intent Off.
                    subtitles.Fail = false;
                    var reloadSidecar = session.SubtitleUrls.Single(t => t.Index == 0);
                    await service.SetSubtitleTrackAndPersistAsync(1, reloadSidecar.Language, reloadSidecar);
                    preferenceWrites = wire.PreferenceWrites;
                    subtitles.Fail = true;
                    Call(service, "LoadSubtitles");
                    mpv.SetSubtitleTrack(0);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", reloadSidecar.Index, false)!;
                    Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                        manager.SubtitleSelectionForLoad == reloadSidecar.Index && wire.PreferenceWrites == preferenceWrites,
                        "A transient reload sidecar failure replaced viewer intent with Off.");
                    subtitles.Fail = false;
                    Call(service, "LoadSubtitles");
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", manager.SubtitleSelectionForLoad, false)!;
                    Require(service.ActiveSubtitleServerIndex == reloadSidecar.Index &&
                        cache.TryGetValue(reloadSidecar.Index, out var retriedSid) && mpv.GetPropertyDouble("sid") == retriedSid &&
                        manager.SubtitleSelectionForLoad == reloadSidecar.Index && wire.PreferenceWrites == preferenceWrites,
                        "The next reload failed to execute the retained sidecar intent without changing preferences.");
                    Program.Log("PASS: no-route and failed-sidecar selections retain executed SID/intent/preferences, transient reload failures retry on the next file, deferred choices persist only after execution, and SiloCast refuses unavailable choices.");
                }
                finally { mpv.ScriptMessageReceived -= Notice; }
                return;
            }
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inventory = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var notices = 0;
            void Observe(string[] message)
            {
                if (message.FirstOrDefault() == "silo-identity-ready") ready.TrySetResult();
                if (message.FirstOrDefault() == "silo-identity-inventory" && message.Length > 1) inventory.TrySetResult(message[1]);
                if (message.FirstOrDefault() == "silo-identity-auto-notice") Interlocked.Increment(ref notices);
            }
            mpv.ScriptMessageReceived += Observe;
            try
            {
                var probe = Path.Combine(Program.ResultDirectory, "identity-observe.lua");
                await File.WriteAllTextAsync(probe, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-set-subtitles', function(payload)
                        mp.commandv('script-message', 'silo-identity-inventory', payload)
                    end)
                    mp.register_script_message('osc-show-notice', function(payload)
                        mp.commandv('script-message', 'silo-identity-auto-notice')
                    end)
                    mp.register_script_message('silo-identity-auto-drain', function()
                        mp.commandv('script-message', 'silo-identity-ready')
                    end)
                    mp.commandv('script-message', 'silo-identity-ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", probe });
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Call(service, "SendSubtitleListToOsc");
                using var payload = JsonDocument.Parse(await inventory.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                Require(payload.RootElement.GetArrayLength() == 6 && payload.RootElement.EnumerateArray().Single(t => t.GetProperty("index").GetInt32() == 3).GetProperty("unplayable").GetBoolean(),
                    "Production OSC payload lost unavailable row or its combined ordinal.");

                var originalInventory = session.SubtitleUrls;
                var automaticNoRoute = new SubtitleTrackInfo { Index = 9, Source = "embedded", Codec = "webvtt",
                    Language = "en", StreamIndex = 99, ContainerTrackId = "99" };
                var automaticWatch = new WatchDetailResponse { EffectiveSubtitleMode = "always", EffectiveSubtitleLanguage = "en",
                    EffectiveShowForcedSubtitles = false };
                var automaticVersion = new FileVersion { FileId = 42, AudioTracks = [new() { Language = "ja" }] };
                try
                {
                    // Blank labels are signature wildcards. An unavailable
                    // duplicate must not hide the playable row, including the
                    // older downloaded-to-external source alias.
                    foreach (var (requestedSource, playableIndex) in new[] { ("embedded", 2), ("downloaded", 7), ("downloaded", 0) })
                    {
                        var playable = originalInventory.Single(t => t.Index == playableIndex);
                        session.SubtitleUrls =
                        [
                            new() { Index = 8, Source = playable.Source, Language = playable.Language,
                                Codec = playable.Codec, Label = "Unavailable duplicate", Unplayable = true },
                            playable,
                        ];
                        var signature = new SubtitleTrackSignature { Source = requestedSource, Language = playable.Language,
                            Codec = playable.Codec, Label = "" };
                        Call(service, "ResolveInitialSubtitleSelection", automaticWatch, automaticVersion, session, null, null, signature);
                        Require((int?)Get(service, "_pendingInitialServerSubtitleIndex") == playableIndex &&
                            manager.SubtitleSelectionForLoad == playableIndex,
                            "Pre-play signature bound an unavailable duplicate instead of the playable row.");
                        Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", playableIndex, false)!,
                            "The playable pre-play signature choice did not execute.");
                        var prePlaySidecars = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                        Require(service.ActiveSubtitleServerIndex == playableIndex &&
                            (playableIndex == 2 ? mpv.GetPropertyDouble("sid") == 1 :
                                prePlaySidecars.TryGetValue(playableIndex, out var prePlaySid) && mpv.GetPropertyDouble("sid") == prePlaySid) &&
                            wire.PreferenceWrites == 0 && mpv.IsPaused,
                            "Pre-play signature lost its native selection, wrote a preference or changed viewer pause.");
                    }
                    foreach (var hasFallback in new[] { true, false })
                    {
                        session.SubtitleUrls = hasFallback
                            ? [automaticNoRoute, originalInventory.Single(t => t.Index == 2)] : [automaticNoRoute];
                        Call(service, "ResolveInitialSubtitleSelection", automaticWatch, automaticVersion, session, null, null, null);
                        // Both embedded candidates have equal priority: the first
                        // row wins before load, but only the second is executable.
                        Require((int?)Get(service, "_pendingInitialServerSubtitleIndex") == 9,
                            "Automatic fixture did not establish the unroutable provisional choice.");
                        var owner = new CancellationTokenSource();
                        Set(service, "_loadedMediaInitCts", owner);
                        await (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
                        ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Call(mpv, "Command", (object)new[] { "script-message", "silo-identity-auto-drain" });
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        Require(service.ActiveSubtitleServerIndex == (hasFallback ? 2 : -1) &&
                            (hasFallback ? mpv.GetPropertyDouble("sid") == 1 : mpv.GetPropertyDouble("sid") <= 0) &&
                            Volatile.Read(ref notices) == 0 && wire.PreferenceWrites == 0,
                            "Automatic startup did not skip unroutable candidates quietly and execute the next eligible track (or Off).");
                    }

                    // A replacement's rejected server refinement is not a local
                    // selection failure when native/sidecar/explicit Off executes.
                    // Missing identities and unroutable rows still need feedback.
                    foreach (var (requested, missing) in new[] { (2, false), (0, false), (-1, false), (9, false), (2, true) })
                    {
                        session.SubtitleUrls = [.. originalInventory, automaticNoRoute];
                        manager.RecordLocalSubtitleSelection(requested < 0 ? null : requested);
                        if (missing) session.SubtitleUrls = [originalInventory.Single(t => t.Index == 4)];
                        session.SubtitleReconciliationFailed = true;
                        var noticesBeforeLoad = Volatile.Read(ref notices);
                        var owner = new CancellationTokenSource();
                        Set(service, "_loadedMediaInitCts", owner);
                        await (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
                        ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Call(mpv, "Command", (object)new[] { "script-message", "silo-identity-auto-drain" });
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        var expectUnavailableNotice = requested == 9 || missing;
                        Require(!session.SubtitleReconciliationFailed &&
                            Volatile.Read(ref notices) == noticesBeforeLoad + (expectUnavailableNotice ? 1 : 0),
                            "Replacement reconciliation feedback did not reflect the actual local subtitle outcome.");
                        if (!expectUnavailableNotice)
                        {
                            var loadedSidecars = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                            Require(service.ActiveSubtitleServerIndex == requested &&
                                (requested == -1 ? mpv.GetPropertyDouble("sid") <= 0 :
                                    requested == 2 ? mpv.GetPropertyDouble("sid") == 1 :
                                    loadedSidecars.TryGetValue(requested, out var sid) && mpv.GetPropertyDouble("sid") == sid),
                                "Rejected server refinement did not execute the requested local subtitle or Off.");
                        }
                        Require(wire.PreferenceWrites == 0 && mpv.IsPaused,
                            "Replacement subtitle feedback changed preferences or viewer pause.");
                    }

                    var playablePrePlay = originalInventory.Single(t => t.Index == 2);
                    var retainedPrePlay = originalInventory.Single(t => t.Index == 4);
                    var unavailablePrePlay = new SubtitleTrackInfo { Index = 1, Source = playablePrePlay.Source,
                        Language = playablePrePlay.Language, Codec = playablePrePlay.Codec, Unplayable = true };
                    var explicitVersion = new FileVersion { FileId = 42, SubtitleTracks =
                        [new() { Language = playablePrePlay.Language, Codec = playablePrePlay.Codec }] };
                    session.SubtitleUrls = [unavailablePrePlay, playablePrePlay, retainedPrePlay];
                    Call(service, "ResolveInitialSubtitleSelection", automaticWatch, explicitVersion, session, null, 0, null);
                    Require((int?)Get(service, "_pendingInitialServerSubtitleIndex") == 2 && manager.SubtitleSelectionForLoad == 2,
                        "Pre-play index selected an unavailable ordinal instead of its playable metadata match.");
                    Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 2, false)! &&
                        service.ActiveSubtitleServerIndex == 2 && mpv.GetPropertyDouble("sid") == 1 && wire.PreferenceWrites == 0,
                        "The playable explicit pre-play fallback failed to execute without saving a preference.");

                    // A playable index-only row must not shift an explicit pick,
                    // and watch-detail array order is not playback index order.
                    foreach (var reordered in new[] { false, true })
                    {
                        session.SubtitleUrls = [originalInventory.Single(t => t.Index == 1), playablePrePlay, retainedPrePlay];
                        var englishRequest = new VersionSubtitleTrack { Language = playablePrePlay.Language, Codec = playablePrePlay.Codec };
                        var japaneseRequest = new VersionSubtitleTrack { Language = retainedPrePlay.Language, Codec = retainedPrePlay.Codec };
                        var reorderedVersion = new FileVersion { FileId = 42, SubtitleTracks = reordered
                            ? [japaneseRequest, englishRequest] : [englishRequest, japaneseRequest] };
                        var expected = reordered ? 4 : 2;
                        Call(service, "ResolveInitialSubtitleSelection", automaticWatch, reorderedVersion, session, null, 0, null);
                        Require((int?)Get(service, "_pendingInitialServerSubtitleIndex") == expected && manager.SubtitleSelectionForLoad == expected,
                            "Pre-play selection guessed a source-group ordinal with a playable extra/reordered watch inventory.");
                        Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", expected, false)! &&
                            service.ActiveSubtitleServerIndex == expected && mpv.GetPropertyDouble("sid") == (reordered ? 2 : 1) &&
                            wire.PreferenceWrites == 0 && mpv.IsPaused,
                            "The identity-resolved pre-play choice did not execute its own native SID without saving preferences.");
                    }

                    // When metadata duplicates, the watch row's absolute stream
                    // index can still attest identity; a combined ordinal cannot.
                    session.SubtitleUrls =
                    [
                        new() { Index = 1, Source = playablePrePlay.Source, Language = playablePrePlay.Language,
                            Codec = playablePrePlay.Codec, StreamIndex = 99 },
                        playablePrePlay, retainedPrePlay,
                    ];
                    var identifiedVersion = new FileVersion { FileId = 42, SubtitleTracks =
                        [new() { Index = playablePrePlay.StreamIndex, Language = playablePrePlay.Language, Codec = playablePrePlay.Codec }] };
                    Call(service, "ResolveInitialSubtitleSelection", automaticWatch, identifiedVersion, session, null, 0, null);
                    Require((int?)Get(service, "_pendingInitialServerSubtitleIndex") == 2 && manager.SubtitleSelectionForLoad == 2,
                        "Pre-play stream identity was replaced with a different playable metadata duplicate.");

                    // Without the matching row, filtering before taking the
                    // ordinal would incorrectly pick Japanese. Refuse instead,
                    // retaining the existing Auto intent rather than explicit Off.
                    session.SubtitleUrls = [unavailablePrePlay, retainedPrePlay];
                    manager.RecordLocalSubtitleSelection(4, isAutomatic: true);
                    Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 4, false)!,
                        "Pre-play refusal fixture did not establish its retained native choice.");
                    var retainedIntent = manager.CaptureLocalSubtitleIntent();
                    var noticesBeforeRefusal = Volatile.Read(ref notices);
                    foreach (var bySignature in new[] { false, true })
                    {
                        var signature = bySignature ? new SubtitleTrackSignature { Source = playablePrePlay.Source,
                            Language = playablePrePlay.Language, Codec = playablePrePlay.Codec } : null;
                        Call(service, "ResolveInitialSubtitleSelection", automaticWatch, explicitVersion, session, null,
                            bySignature ? null : (int?)0, signature);
                        Require(Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                            Get(service, "_pendingSubtitleSelection") == null && manager.CaptureLocalSubtitleIntent() == retainedIntent &&
                            manager.IsSubtitleSelectionAutomatic && service.ActiveSubtitleServerIndex == 4 &&
                            mpv.GetPropertyDouble("sid") == 2 && wire.PreferenceWrites == 0,
                            "Unavailable pre-play selection changed the retained route/Auto intent or recorded explicit Off.");
                    }
                    ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Call(mpv, "Command", (object)new[] { "script-message", "silo-identity-auto-drain" });
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(Volatile.Read(ref notices) == noticesBeforeRefusal + 2,
                        "Refused explicit pre-play choices did not show unavailable feedback.");
                }
                finally { session.SubtitleUrls = originalInventory; }
            }
            finally { mpv.ScriptMessageReceived -= Observe; }
            var english = session.SubtitleUrls.Single(t => t.Index == 2);
            await service.SetSubtitleTrackAndPersistAsync(3, english.Language, english);
            Require(mpv.GetPropertyDouble("sid") == 1, "Interleaved index-only inventory selected the wrong native SID instead of source container track 2.");
            Require(mpv.IsPaused, "Native subtitle selection resumed viewer-paused playback.");
            var restoreLock = Get(service, "_subtitleTrackStateLock")!;
            Func<bool> restoreOwner = () =>
            {
                Require(Monitor.IsEntered(restoreLock), "Subtitle restoration observed picker ownership without its state lock.");
                return true;
            };
            Require((bool)Call(service, "RestoreExecutedSubtitleSelection", false, null, restoreOwner)! &&
                service.ActiveSubtitleServerIndex == english.Index && mpv.GetPropertyDouble("sid") == 1,
                "Locked subtitle restoration failed to retain the executed row.");
            var republishedActive = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Republished(string[] message)
            {
                if (message.FirstOrDefault() == "silo-identity-active" && message.Length > 1) republishedActive.TrySetResult(message[1]);
            }
            mpv.ScriptMessageReceived += Republished;
            try
            {
                var activeProbe = Path.Combine(Program.ResultDirectory, "identity-active.lua");
                await File.WriteAllTextAsync(activeProbe, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-set-active-subtitle', function(index)
                        mp.commandv('script-message', 'silo-identity-active', index)
                    end)
                    mp.commandv('script-message', 'silo-identity-active', 'ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", activeProbe });
                Require(await republishedActive.Task.WaitAsync(TimeSpan.FromSeconds(4)) == "ready", "Active subtitle probe did not load.");
                republishedActive = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Call(service, "SendSubtitleListToOsc");
                Require(await republishedActive.Task.WaitAsync(TimeSpan.FromSeconds(4)) == "2" && mpv.GetPropertyDouble("sid") == 1,
                    "Republishing the subtitle inventory reported Off instead of the executed active track.");
            }
            finally { mpv.ScriptMessageReceived -= Republished; }
            var unavailable = session.SubtitleUrls.Single(t => t.Index == 3);
            var previous = service.ActiveSubtitleServerIndex; var writes = wire.PreferenceWrites;
            await service.SetSubtitleTrackAndPersistAsync(4, "en", unavailable);
            await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 3, true)!;
            Call(service, "SelectSubtitleTrack", unavailable);
            Require(service.ActiveSubtitleServerIndex == previous && wire.PreferenceWrites == writes && wire.Replans == 0 && mpv.GetPropertyDouble("sid") == 1,
                "Forged unavailable callbacks changed selection, preferences or transport.");
            var indexOnly = session.SubtitleUrls.Single(t => t.Index == 1);
            await service.SetSubtitleTrackAndPersistAsync(2, "en", indexOnly);
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            Require(map.TryGetValue(1, out var fallback) && fallback > 2 && mpv.GetPropertyDouble("sid") == fallback,
                "Absent embedded track did not execute its sidecar fallback.");
            var downloaded = session.SubtitleUrls.Single(t => t.Index == 7);
            await service.SetSubtitleTrackAndPersistAsync(8, downloaded.Language, downloaded);
            Require(map.TryGetValue(7, out var downloadedSid) && downloadedSid > fallback && mpv.GetPropertyDouble("sid") == downloadedSid,
                "Downloaded combined ordinal did not execute its sidecar independently of embedded source identity.");
            var japanese = session.SubtitleUrls.Single(t => t.Index == 4);
            await service.SetSubtitleTrackAndPersistAsync(5, "ja", japanese);
            Require(mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused, "Later native identity was shifted by sidecar/index-only inventory.");
            await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            Require(mpv.GetPropertyDouble("sid") <= 0 && mpv.IsPaused, "Explicit Off changed viewer pause intent.");
            // Remote-only rows remain in the menu, but a local cycle must not
            // fetch them (including a busy URL) or save series preferences.
            Call(service, "LoadSubtitles");
            subtitles.Fail = true;
            var cycleWrites = wire.PreferenceWrites;
            var cycleRequests = Volatile.Read(ref subtitles.Requests);
            foreach (var (index, sid) in new[] { (4, 2), (2, 1), (-1, 0) })
            {
                // Every cycle is a viewer gesture, even after an automatic pick
                // (including automatic Off); it must not inherit Auto provenance.
                manager.RecordLocalSubtitleSelection(service.ActiveSubtitleServerIndex < 0 ? null : service.ActiveSubtitleServerIndex,
                    isAutomatic: true);
                Call(service, "CycleSubtitleBackward");
                Require(!manager.IsSubtitleSelectionAutomatic && !manager.CaptureLocalSubtitleIntent().IsAutomatic,
                    "Local caption cycling retained the previous automatic selection's provenance.");
                Require(service.ActiveSubtitleServerIndex == index && manager.SubtitleSelectionForLoad == index &&
                    (sid == 0 ? mpv.GetPropertyDouble("sid") <= 0 : mpv.GetPropertyDouble("sid") == sid),
                    "Local caption cycling did not map server rows to their actual loaded native SIDs.");
            }
            japanese.Unplayable = true;
            try
            {
                Call(service, "CycleSubtitleBackward");
                Require(service.ActiveSubtitleServerIndex == 2 && mpv.GetPropertyDouble("sid") == 1,
                    "Local caption cycling selected an unavailable native row.");
                Call(service, "CycleSubtitleBackward");
                Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0,
                    "Local caption cycling did not include Off.");
                await VerifySubtitleCycleKeysAsync(assets, service, manager, mpv);
            }
            finally { japanese.Unplayable = false; }
            Require(wire.PreferenceWrites == cycleWrites && Volatile.Read(ref subtitles.Requests) == cycleRequests && mpv.IsPaused,
                "Local caption cycling downloaded a remote sidecar, saved a series preference or resumed playback.");
            Program.Log("PASS: subtitle identity uses actual loaded Matroska tracks across index-only gap and sidecar addition; unavailable forged service/native callbacks preserve selection/preferences; Off and genuine pause survive.");
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static async Task VerifySubtitleCycleKeysAsync(string assets, PlayerService service, PlaybackManager manager, MpvPlayer mpv)
    {
        await using var dispatcherShell = EpisodeQuickPlayNativeFixture.CreateOwnedDispatcherShell();
        Require(SiloPlayer.App.MainWindowInstance != null, "Real subtitle-key routing requires the owned synthetic shell dispatcher.");
        // Load the production OSC into the same libmpv instance that has both
        // native subtitle streams. Japanese is demuxed but server-unplayable.
        // Use real keypresses with default bindings enabled, not Lua callbacks.
        var script = Path.GetFullPath(Path.Combine(assets, "..", "..", "..", "libs", "mpv", "scripts", "silo-osc.lua")).Replace('\\', '/');
        var probe = Path.Combine(Program.ResultDirectory, "identity-cycle-keys.lua");
        await File.WriteAllTextAsync(probe, $$"""
            local mp = require 'mp'
            local register = mp.register_script_message
            mp.register_script_message = function(name, callback)
                if name == 'osc-set-active-subtitle' then
                    register(name, function(index, ...)
                        callback(index, ...)
                        mp.commandv('script-message', 'silo-identity-key-active', index)
                    end)
                else
                    register(name, callback)
                end
            end
            assert(loadfile([[{{script}}]]))()
            mp.register_script_message = register
            local readiness
            readiness = mp.add_periodic_timer(0.01, function()
                local forward, backward = false, false
                for _, binding in ipairs(mp.get_property_native('input-bindings') or {}) do
                    if (binding.priority or -1) >= 0 then
                        if binding.key == 'j' and binding.cmd:find('/silo-subtitle-cycle-forward', 1, true) then forward = true end
                        if binding.key == 'J' and binding.cmd:find('/silo-subtitle-cycle-backward', 1, true) then backward = true end
                    end
                end
                if forward and backward then
                    readiness:kill()
                    mp.commandv('script-message', 'silo-identity-keys-ready')
                end
            end)
            """);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gestures = 0;
        void Observe(string[] message)
        {
            if (message.FirstOrDefault() == "silo-identity-keys-ready") ready.TrySetResult();
            if (message.FirstOrDefault() == "silo-identity-key-active" && message.Length > 1) selected.TrySetResult(message[1]);
            if (message.FirstOrDefault() == "silo-subtitle-cycle")
            {
                Interlocked.Increment(ref gestures);
                Call(service, "OnScriptMessage", (object)message);
            }
        }
        mpv.ScriptMessageReceived += Observe;
        try
        {
            mpv.SetProperty("input-default-bindings", "yes");
            Call(mpv, "Command", (object)new[] { "load-script", probe });
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
            foreach (var (key, index, sid) in new[] { ("j", 2, 1), ("j", -1, 0), ("J", 2, 1), ("J", -1, 0) })
            {
                selected = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var before = Volatile.Read(ref gestures);
                Call(mpv, "Command", (object)new[] { "keypress", key });
                Require(await selected.Task.WaitAsync(TimeSpan.FromSeconds(4)) == index.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    Volatile.Read(ref gestures) == before + 1 && service.ActiveSubtitleServerIndex == index &&
                    manager.SubtitleSelectionForLoad == index && !manager.IsSubtitleSelectionAutomatic &&
                    ((SubtitleTrackInfo?)Get(service, "_executedSubtitleTrack"))?.Index == (index < 0 ? (int?)null : index) &&
                    (sid == 0 ? mpv.GetPropertyDouble("sid") <= 0 : mpv.GetPropertyDouble("sid") == sid),
                    $"Actual {key} key bypassed host subtitle cycling or selected the unavailable demuxed stream.");
            }
        }
        finally { mpv.ScriptMessageReceived -= Observe; }
    }

    private static async Task BurnedInTextSubtitlesAsync(string assets)
    {
        using var subtitles = new SubtitleServer(File.ReadAllBytes(Path.Combine(assets, "english.srt")));
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://subtitle-identity.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "subtitle-burned-text")), new SettingsApi(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [new() { FileId = 42 }] });
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); mpv.Pause(); mpv.SetSubtitleTrack(0);
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "movie");
        // Use the real loaded native inventory, but no native route on the
        // accepted HLS plan: only its burn-in grant can attest execution.
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("transcode", subtitles.Origin + "/master.m3u8"));
        var nativeIds = mpv.GetSubtitleTrackIds().ToArray();
        try
        {
            foreach (var codec in new[] { "srt", "ass" })
            foreach (var hasUrl in new[] { false, true })
            foreach (var hasIdentity in new[] { false, true })
            {
                var track = new SubtitleTrackInfo { Index = 8, TrackId = hasIdentity ? "burned-text" : null,
                    Source = "embedded", Codec = codec, Language = "en", Delivery = "native",
                    Url = hasUrl ? subtitles.Origin + "/captions.srt" : "" };
                var other = new SubtitleTrackInfo { Index = 9, TrackId = "other-text", Source = "embedded",
                    Codec = codec, Language = "ja", Delivery = "native" };
                var session = new PlaybackStartResponse { SessionId = "burned-text", ProtocolVersion = 3, MediaFileId = 42,
                    PlayMethod = "transcode", SubtitleMode = "burn_in", SelectedSubtitleTrackId = track.TrackId,
                    SelectedSubtitleTrackIndex = track.Index, SubtitleUrls = [track, other] };
                typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
                var writes = wire.PreferenceWrites;
                Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, false)! &&
                    service.ActiveSubtitleServerIndex == track.Index && ReferenceEquals(Get(service, "_executedSubtitleTrack"), track) &&
                    wire.PreferenceWrites == writes,
                    "Load-time selection refused the granted text/ASS burn-in or saved a preference.");
                Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, true)! &&
                    service.ActiveSubtitleServerIndex == track.Index && ReferenceEquals(Get(service, "_executedSubtitleTrack"), track) &&
                    manager.SubtitleSelectionForLoad == track.Index && wire.PreferenceWrites == writes + 1,
                    "Manual selection failed to execute/persist the granted text/ASS burn-in.");
                Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", other.Index, true)! &&
                    service.ActiveSubtitleServerIndex == track.Index && wire.PreferenceWrites == writes + 1,
                    "A different unroutable row borrowed the text/ASS burn-in grant.");
                Require(mpv.GetPropertyDouble("sid") <= 0 && mpv.GetSubtitleTrackIds().SequenceEqual(nativeIds) &&
                    mpv.IsPaused && wire.Replans == 0 && Volatile.Read(ref subtitles.Requests) == 0 &&
                    ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).Count == 0 &&
                    ((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0,
                    "The text/ASS burn-in grant caused a duplicate native/sidecar selection, download, replan or resume.");
            }
            Program.Log("PASS: text/ASS burn-in grants execute by ID or legacy index without native/sidecar routes or duplicate downloads, retaining pause and saving only explicit executed preferences.");
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static async Task VerifyPresentationAsync(StackPanel parent, PlayerService service,
        PlaybackStartResponse session, MpvPlayer mpv, Wire wire, string scenario)
    {
        var unavailable = session.SubtitleUrls.Single(track => track.Index == 3);
        unavailable.Url = "";
        unavailable.Label = "Fixture unavailable without URL";
        session.SubtitleUrls.Single(track => track.Index == 2).Label = "Fixture native available";
        session.SubtitleUrls.Add(new() { Index = 9, Source = "embedded", Codec = "webvtt", Language = "en",
            Label = "Fixture locally unavailable", StreamIndex = 99, ContainerTrackId = "99" });
        var beforeSid = mpv.GetPropertyDouble("sid");
        var beforeIndex = service.ActiveSubtitleServerIndex;
        if (scenario == "presentation-flyout")
        {
            var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previousServices = (IServiceProvider)servicesField.GetValue(null)!;
            PlayerOverlay? overlay = null;
            var mounted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnMounted(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) => mounted.TrySetResult();
            try
            {
                servicesField.SetValue(null, new PresentationServices(previousServices, service));
                overlay = new PlayerOverlay(); overlay.Loaded += OnMounted;
                parent.Children.Add(overlay);
                await mounted.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Call(overlay, "PopulateSubtitleFlyout");
                var list = (StackPanel)overlay.FindName("SubtitleListPanel");
                Button? Row(string label) => list.Children.OfType<Button>().SingleOrDefault(button =>
                    button.Content is Grid grid && grid.Children.OfType<StackPanel>().Any(descriptor =>
                        descriptor.Children.OfType<TextBlock>().Any(text => text.Text.Contains(label, StringComparison.Ordinal))));
                var row = Row(unavailable.Label);
                Require(row != null && !row.IsEnabled && AutomationProperties.GetHelpText(row) == "Subtitle unavailable",
                    "The actual native subtitle flyout hides the URL-less unavailable inventory row.");
                var local = Row("Fixture locally unavailable");
                Require(local != null && !local.IsEnabled && AutomationProperties.GetHelpText(local) == "Subtitle unavailable",
                    "The actual native subtitle flyout enables a locally route-less inventory row.");
                Require(Row("Fixture native available") is { IsEnabled: true } && list.Children.OfType<Button>().First().IsEnabled,
                    "The actual native subtitle flyout disabled its Off and real playable track controls.");
                Program.Log("PASS: actual mounted subtitle flyout retains URL-less/unroutable rows disabled and real playable/Off controls enabled.");
            }
            finally
            {
                if (overlay != null) { overlay.Loaded -= OnMounted; parent.Children.Remove(overlay); }
                servicesField.SetValue(null, previousServices);
            }
        }
        else
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inventory = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(string[] message)
            {
                if (message.FirstOrDefault() == "silo-presentation-ready") ready.TrySetResult();
                if (message.FirstOrDefault() == "silo-presentation-inventory" && message.Length > 1) inventory.TrySetResult(message[1]);
            }
            mpv.ScriptMessageReceived += Observe;
            try
            {
                var probe = Path.Combine(Program.ResultDirectory, "subtitle-presentation.lua");
                await File.WriteAllTextAsync(probe, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-set-subtitles', function(payload)
                        mp.commandv('script-message', 'silo-presentation-inventory', payload)
                    end)
                    mp.commandv('script-message', 'silo-presentation-ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", probe });
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Call(service, "SendSubtitleListToOsc");
                using var document = JsonDocument.Parse(await inventory.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                var tracks = document.RootElement.EnumerateArray().ToArray();
                Require(tracks.Single(track => track.GetProperty("index").GetInt32() == 9).GetProperty("unplayable").GetBoolean(),
                    "The actual OSC payload enables a locally route-less subtitle row.");
                Require(tracks.Single(track => track.GetProperty("index").GetInt32() == 3).GetProperty("unplayable").GetBoolean() &&
                    !tracks.Single(track => track.GetProperty("index").GetInt32() == 2).GetProperty("unplayable").GetBoolean(),
                    "OSC route availability lost the explicit unavailable or real native playable control.");
                var castTracks = service.CaptureSiloCastState()["subtitleTracks"]!.AsArray();
                Require(!castTracks.Any(track => track!["trackId"]!.GetValue<int>() == 10) &&
                    castTracks.Any(track => track!["trackId"]!.GetValue<int>() == 3),
                    "SiloCast availability diverged from the actual OSC selection routes.");
                Program.Log("PASS: actual OSC payload disables route-less rows and agrees with SiloCast while keeping real native tracks playable.");
            }
            finally { mpv.ScriptMessageReceived -= Observe; }
        }
        Require(mpv.GetPropertyDouble("sid") == beforeSid && service.ActiveSubtitleServerIndex == beforeIndex &&
            mpv.IsPaused && wire.PreferenceWrites == 0 && wire.Replans == 0,
            "Subtitle presentation changed the applied selection, pause, preference or transport.");
    }

    private sealed class PresentationServices(IServiceProvider fallback, PlayerService service, ToastService? toast = null) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(PlayerService) ? service :
            type == typeof(ToastService) && toast != null ? toast : fallback.GetService(type);
    }

    private static async Task VerifyAiPresentationAsync(StackPanel parent, PlayerService service,
        PlaybackStartResponse session, MpvPlayer mpv, Wire wire, string scenario)
    {
        var track = session.SubtitleUrls.Single(row => row.Index == 2);
        var callback = scenario.StartsWith("presentation-ai-post", StringComparison.Ordinal) ||
            scenario.StartsWith("presentation-ai-job", StringComparison.Ordinal) || scenario.StartsWith("presentation-ai-refresh", StringComparison.Ordinal) ||
            scenario.StartsWith("presentation-ai-prefetch", StringComparison.Ordinal) || scenario.StartsWith("presentation-ai-cues", StringComparison.Ordinal);
        if (!callback) track.Url = "";
        track.Label = "Native text source";
        session.SubtitleUrls = [track];
        wire.AiTranslateEnabled = true;
        var beforeSid = mpv.GetPropertyDouble("sid");
        var beforeIndex = service.ActiveSubtitleServerIndex;
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = (IServiceProvider)servicesField.GetValue(null)!;
        PlayerOverlay? overlay = null;
        Task? showing = null;
        try
        {
            servicesField.SetValue(null, new PresentationServices(previousServices, service));
            overlay = new PlayerOverlay();
            var mounted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            overlay.Loaded += (_, _) => mounted.TrySetResult();
            parent.Children.Add(overlay);
            await mounted.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await (Task)Call(overlay, "RefreshSubtitleAiCapabilityAsync")!;
            if (callback)
            {
                await VerifyAiCallbackAsync(parent, overlay, service, session, mpv, wire, scenario);
                return;
            }
            if (scenario == "presentation-ai-menu")
            {
                Call(overlay, "PopulateSubtitleFlyout");
                var list = (StackPanel)overlay.FindName("SubtitleListPanel");
                Require((bool)Call(overlay, "CanShowSubtitleAi")! && list.Children.OfType<Button>().Any(button =>
                    button.Content is StackPanel label && label.Children.OfType<TextBlock>().Any(text => text.Text == "Translate with AI…")),
                    "Actual native subtitle menu hides AI translation for the only native text source without a delivery URL.");
                track.Codec = "dvdsub";
                Require(!(bool)Call(overlay, "CanShowSubtitleAi")!, "Bitmap-only inventory incorrectly enabled subtitle translation.");
                track.Codec = "subrip";
                wire.AiTranslateEnabled = false;
                await (Task)Call(overlay, "RefreshSubtitleAiCapabilityAsync")!;
                Require(!(bool)Call(overlay, "CanShowSubtitleAi")!, "Disabled server translation incorrectly enabled its native action.");
                Program.Log("PASS: actual native AI menu includes URL-less native text and excludes bitmap-only/disabled translation without changing playback.");
            }
            else
            {
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (scenario is "presentation-ai-profile" or "presentation-ai-access")
                    wire.BeforeAiStatus = () => { started.TrySetResult(); return release.Task; };
                showing = overlay.ShowSubtitleAiDialogAsync(parent.XamlRoot);
                if (wire.BeforeAiStatus != null)
                {
                    try
                    {
                        await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        if (scenario == "presentation-ai-profile") service.ActiveAuthority.ApiClient.SetProfile("replacement");
                        else Require(service.ActiveAuthority.ApiClient.TryInvalidateAccessContext(service.ActiveAuthority.ApiClient.CaptureContext(), out _),
                            "AI access-only control did not invalidate its cache.");
                    }
                    finally { release.TrySetResult(); wire.BeforeAiStatus = null; }
                }
                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < deadline && !showing.IsCompleted && !AiDialogs(parent.XamlRoot).Any(dialog => dialog.IsLoaded)) await Task.Delay(20);
                if (scenario == "presentation-ai-profile")
                {
                    Require(showing.IsCompleted && !AiDialogs(parent.XamlRoot).Any(), "A retired profile's held AI status opened a foreign native dialog.");
                    await showing; showing = null;
                    Require(mpv.GetPropertyDouble("sid") == beforeSid && service.ActiveSubtitleServerIndex == beforeIndex && mpv.IsPaused &&
                        wire.PreferenceWrites == 0 && wire.Replans == 0 && wire.AiStarts == 0, "Retired AI status changed playback or started work.");
                    Program.Log("PASS: held native AI status cannot open a dialog after its profile retires.");
                    return;
                }
                var active = AiDialogs(parent.XamlRoot).SingleOrDefault(dialog => dialog.IsLoaded);
                Require(active is SubtitleAiDialog, "Actual native AI entry route shows No compatible source instead of the URL-less native text translation dialog.");
                var sources = (ComboBox)active!.FindName("SourceComboBox");
                Require(sources.Items.Count == 1 && sources.SelectedItem is ComboBoxItem selected && ReferenceEquals(selected.Tag, track) &&
                    ((SubtitleTrackInfo)selected.Tag).Index == 2 && ((Button)active.FindName("SubmitButton")).IsEnabled && active.XamlRoot == parent.XamlRoot,
                    "Native AI dialog lost the exact URL-less text source/index or its current mounted owner.");
                active.Hide(); await showing.WaitAsync(TimeSpan.FromSeconds(4)); showing = null;
                Program.Log("PASS: actual native AI dialog uses the complete inventory's URL-less text row/index on its mounted owner without starting a job.");
            }
            Require(mpv.GetPropertyDouble("sid") == beforeSid && service.ActiveSubtitleServerIndex == beforeIndex && mpv.IsPaused &&
                wire.PreferenceWrites == 0 && wire.Replans == 0 && wire.AiStarts == 0,
                "AI availability/dialog changed playback, pause, preferences or started an unsolicited AI job.");
        }
        finally
        {
            foreach (var dialog in AiDialogs(parent.XamlRoot)) dialog.Hide();
            if (showing != null) await showing.WaitAsync(TimeSpan.FromSeconds(4));
            if (overlay != null) { overlay.Deactivate(); parent.Children.Remove(overlay); }
            servicesField.SetValue(null, previousServices);
        }
    }

    private static IEnumerable<ContentDialog> AiDialogs(Microsoft.UI.Xaml.XamlRoot root)
    {
        var found = new HashSet<ContentDialog>();
        foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            foreach (var element in new[] { popup.Child }.Concat(AiDescendants(popup.Child))) if (element is ContentDialog dialog) found.Add(dialog);
            for (Microsoft.UI.Xaml.DependencyObject? ancestor = popup.Child; ancestor != null; ancestor = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(ancestor))
                if (ancestor is ContentDialog dialog) found.Add(dialog);
        }
        return found;
    }

    private static async Task VerifyAiCallbackAsync(StackPanel parent, PlayerOverlay overlay, PlayerService service,
        PlaybackStartResponse session, MpvPlayer mpv, Wire wire, string scenario)
    {
        var current = scenario.EndsWith("-current", StringComparison.Ordinal) || scenario.EndsWith("-access", StringComparison.Ordinal);
        var access = scenario.EndsWith("-access", StringComparison.Ordinal);
        var authority = service.ActiveAuthority;
        void Change()
        {
            if (access) Require(authority.ApiClient.TryInvalidateAccessContext(authority.ApiClient.CaptureContext(), out _), "AI callback access control did not invalidate.");
            else if (!current) authority.ApiClient.SetProfile("replacement");
        }
        var beforeSid = mpv.GetPropertyDouble("sid");
        var beforeInventory = session.SubtitleUrls;
        if (scenario.StartsWith("presentation-ai-cues", StringComparison.Ordinal))
        {
            var path = Path.Combine(Program.ResultDirectory, "owned-live-cues.vtt");
            const string empty = "WEBVTT\n\n";
            await File.WriteAllTextAsync(path, empty);
            Set(service, "_liveSubtitleJobId", 77L); Set(service, "_liveSubtitleFileId", 42); Set(service, "_liveSubtitlePath", path);
            var owner = typeof(PlayerService).GetMethod("CaptureSubtitleOperationOwner", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(service, [session]);
            typeof(PlayerService).GetField("_liveSubtitleOperationOwner", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(service, owner);
            try
            {
                Change();
                using var payload = JsonDocument.Parse("""{"job_id":77,"cues":[{"start":0,"end":5,"text":"owned cue"}]}""");
                Call(service, "ApplySubtitleTranslationCues", payload.RootElement);
                Require(current ? (await File.ReadAllTextAsync(path)).Contains("owned cue", StringComparison.Ordinal) && mpv.GetPropertyDouble("sid") > 0
                    : !File.Exists(path) && (long)Get(service, "_liveSubtitleJobId")! == 0 &&
                        Get(service, "_liveSubtitleOperationOwner") == null && Get(service, "_liveSubtitleResumeCts") == null &&
                        mpv.GetPropertyDouble("sid") == beforeSid && (int)Get(service, "_liveSubtitleSid")! == 0,
                    "Retired live AI cues changed native subtitles/owned bytes, or access-only invalidation lost current cues.");
                Require(mpv.IsPaused && wire.PreferenceWrites == 0, "Live AI cue ownership changed genuine pause or a saved preference.");
            }
            finally
            {
                lock (Get(service, "_liveSubtitleLock")!) Call(service, "ClearLiveSubtitleTranslationLocked", false);
            }
            Program.Log($"PASS: {scenario} gates actual native live-cue writes and SID attachment using only an isolated fixture file.");
            return;
        }
        if (scenario.StartsWith("presentation-ai-post", StringComparison.Ordinal))
        {
            var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previous = (IServiceProvider)field.GetValue(null)!;
            var toast = new ToastService(); var container = new ToastContainer();
            parent.Children.Add(container); toast.Register(container, parent.DispatcherQueue);
            field.SetValue(null, new PresentationServices(previous, service, toast));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.BeforeAiStart = () => { entered.TrySetResult(); return release.Task; };
            Task? showing = null;
            try
            {
                showing = overlay.ShowSubtitleAiDialogAsync(parent.XamlRoot);
                var until = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < until && !AiDialogs(parent.XamlRoot).OfType<SubtitleAiDialog>().Any(dialog => dialog.IsLoaded)) await Task.Delay(20);
                var dialog = AiDialogs(parent.XamlRoot).OfType<SubtitleAiDialog>().Single(dialog => dialog.IsLoaded);
                var target = (ComboBox)dialog.FindName("TargetLanguageComboBox");
                target.SelectedItem = target.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "fr");
                var submit = (Button)dialog.FindName("SubmitButton");
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(submit)
                    .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Change(); release.TrySetResult();
                await showing.WaitAsync(TimeSpan.FromSeconds(4)); showing = null;
                var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                parent.DispatcherQueue.TryEnqueue(() => drained.TrySetResult()); await drained.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var stack = (StackPanel)container.FindName("ToastStack");
                Require(stack.Children.Count == (current ? 1 : 0) && wire.AiStarts == 1 && wire.AiJobReads == 0 &&
                    wire.AiStartRequest?.GetProperty("source_index").GetInt32() == 2 &&
                    wire.AiStartRequest?.GetProperty("media_file_id").GetString() == "42" &&
                    (long)Get(service, "_liveSubtitleJobId")! == 0,
                    "Held AI POST published a retired owner's toast/live job/monitor, or lost a current/access-only job.");
            }
            finally
            {
                release.TrySetResult(); wire.BeforeAiStart = null;
                foreach (var dialog in AiDialogs(parent.XamlRoot)) dialog.Hide();
                if (showing != null) await showing.WaitAsync(TimeSpan.FromSeconds(4));
                toast.Unregister(); parent.Children.Remove(container); field.SetValue(null, previous);
            }
        }
        else
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refresh = scenario.StartsWith("presentation-ai-refresh", StringComparison.Ordinal);
            var prefetch = scenario.StartsWith("presentation-ai-prefetch", StringComparison.Ordinal);
            var assets = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity"));
            using var sidecar = prefetch ? new SubtitleServer(File.ReadAllBytes(Path.Combine(assets, "english.srt"))) : null;
            if (prefetch)
            {
                authority.ApiClient.SetBaseUrl(sidecar!.Origin); wire.InventoryHost = new Uri(sidecar.Origin).Host;
                sidecar.BeforeSubtitleResponse = () => { entered.TrySetResult(); return release.Task; };
                wire.AiResultSubtitleId = 91;
            }
            else if (refresh) wire.BeforeAiInventory = () => { entered.TrySetResult(); return release.Task; };
            else wire.BeforeAiJob = () => { entered.TrySetResult(); return release.Task; };
            wire.DownloadedInventory = new() { Subtitles = [new() { Id = 91, Language = "fr", Format = "srt" }] };
            var monitor = typeof(PlayerOverlay).GetMethod("MonitorSubtitleAiJobAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var capturedOwner = typeof(PlayerService).GetMethod("CaptureSubtitleOperationOwner", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(service, [session]);
            var pending = (Task)monitor.Invoke(overlay, monitor.GetParameters().Length == 3
                ? [authority.PlaybackApi, 77L, 42] : [authority.PlaybackApi, 77L, 42, capturedOwner])!;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(4)); Change(); release.TrySetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(4));
                var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                parent.DispatcherQueue.TryEnqueue(() => drained.TrySetResult()); await drained.Task.WaitAsync(TimeSpan.FromSeconds(4));
                // The queued refresh starts its real wire request before returning.
                // When that response was held, await its separately captured task.
                if (prefetch)
                {
                    var until = DateTime.UtcNow.AddSeconds(4);
                    while (DateTime.UtcNow < until && Get(service, "_pendingSubtitlePreferenceOwner") != null) await Task.Delay(20);
                    Require(Get(service, "_pendingSubtitlePreferenceOwner") == null, "Owned AI sidecar selection did not release its admission.");
                    var cache = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
                    if (!current)
                        Require(cache.Count == 0 && mpv.GetPropertyDouble("sid") == beforeSid && service.ActiveSubtitleServerIndex == -1 && wire.PreferenceWrites == 0,
                            "Retired AI sidecar prefetch cached/attached foreign bytes or retained its unexecuted highlight/preference.");
                    else Require(cache.ContainsKey(3) && mpv.GetPropertyDouble("sid") > 0 && service.ActiveSubtitleServerIndex == 3 && wire.PreferenceWrites == 1,
                        "Access-only invalidation lost the current AI sidecar execution or its once-only preference.");
                }
                else if (refresh)
                {
                    var until = DateTime.UtcNow.AddSeconds(4);
                    while (DateTime.UtcNow < until && ReferenceEquals(beforeInventory, session.SubtitleUrls) && current) await Task.Delay(20);
                    await Task.Delay(100);
                    Require(ReferenceEquals(beforeInventory, session.SubtitleUrls) == !current,
                        "Held AI inventory response published under a retired owner, or lost current/access-only refresh.");
                }
                else Require(wire.AiInventoryReads == (current ? 1 : 0), "Held AI job response queued an inventory read for a retired owner, or lost a current/access-only refresh.");
            }
            finally { release.TrySetResult(); wire.BeforeAiJob = null; wire.BeforeAiInventory = null; }
        }
        var executedCurrentSidecar = scenario == "presentation-ai-prefetch-access";
        Require((executedCurrentSidecar || mpv.GetPropertyDouble("sid") == beforeSid) && mpv.IsPaused &&
            wire.PreferenceWrites == (executedCurrentSidecar ? 1 : 0) && wire.Replans == 0,
            "AI callback control changed the applied subtitle, genuine pause or saved preference.");
        Program.Log($"PASS: {scenario} preserves exact AI callback ownership across real held wire/dispatch boundaries.");
    }
    private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> AiDescendants(Microsoft.UI.Xaml.DependencyObject root)
    {
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var nested in AiDescendants(child)) yield return nested;
        }
    }

    private static async Task LegacyPrePlayAsync(string assets, bool retiredOnly = false)
    {
        foreach (var change in retiredOnly ? new[] { "retired-initializer" } : new[] { "current", "access", "profile", "inventory", "manual", "manual-aba", "session", "play", "mixed", "missing", "external", "unmatched" })
        {
            using var retiredInitializer = new CancellationTokenSource();
            using var currentInitializer = new CancellationTokenSource();
            using var wire = new Wire();
            using var http = new HttpClient(wire);
            var api = new SiloApiClient(http); api.SetBaseUrl("https://subtitle-identity.invalid"); api.SetProfile("fixture");
            using var auth = new AuthService(api, new AuthApi(api));
            using var service = new PlayerService(new(api), new(api), auth, api,
                new SettingsService(Path.Combine(Program.ResultDirectory, "legacy-preplay-" + change)), new SettingsApi(api));
            using var manager = new PlaybackManager(new(api), new(api), auth, api);
            var session = new PlaybackStartResponse { SessionId = "legacy", ProtocolVersion = 2, MediaFileId = 42, PlayMethod = "direct" };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.UseWatchDetail(new() { ContentId = "movie", SeriesId = "series", Versions = [new() { FileId = 42 }] });
            using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
            Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
            typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
            typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "movie");
            Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
            Call(service, "WireMpvEvents");
            try
            {
                var version = new FileVersion { FileId = 42, SubtitleTracks =
                    [new() { Language = "eng", Codec = "subrip" }, new() { Language = "jpn", Codec = "subrip" }] };
                if (change == "mixed") version.SubtitleTracks =
                    [new() { Language = "deu", Codec = "subrip", External = true }, new() { Language = "eng", Codec = "subrip" }, new() { Language = "jpn", Codec = "subrip" }];
                if (change == "missing") version.SubtitleTracks.RemoveAt(1);
                if (change == "external") version.SubtitleTracks[1].External = true;
                if (change == "unmatched") version.SubtitleTracks[1].Language = "fra";
                Call(service, "ResolveInitialSubtitleSelection", new WatchDetailResponse(), version, session, null, 1, null);
                Call(service, "ApplyPendingSubtitleSelection");
                Require(mpv.GetPropertyDouble("sid") <= 0, "Legacy pre-play choice selected an outgoing SID before FILE_LOADED.");
                if (change == "retired-initializer")
                {
                    Set(service, "_loadedMediaInitGeneration", 2L);
                    Set(service, "_loadedMediaInitCts", currentInitializer);
                    var choice = Get(service, "_legacySubtitleChoice");
                    Require(choice != null, "Retired initializer control did not establish a newer legacy choice.");
                    var helper = typeof(PlayerService).GetMethod("TryApplyLegacyInitialSubtitleChoice", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    helper.Invoke(service, helper.GetParameters().Length == 0 ? [] : [1L, retiredInitializer]);
                    Require(ReferenceEquals(choice, Get(service, "_legacySubtitleChoice")) && mpv.GetPropertyDouble("sid") <= 0,
                        "A retired initializer consumed the newer load's legacy choice before taking its state gate.");
                }
                switch (change)
                {
                    case "access": Require(api.TryInvalidateAccessContext(api.CaptureContext(), out _), "Access-only control did not invalidate its cache."); break;
                    case "profile": api.SetProfile("replacement"); break;
                    case "inventory": session.SubtitleUrls.Add(new() { Index = 1, TrackId = "identified", Source = "embedded", ContainerTrackId = "99" }); break;
                    case "manual": await service.SetSubtitleTrackAndPersistAsync(0, null, null); break;
                    case "manual-aba": await service.SetSubtitleTrackAndPersistAsync(0, null, null); await service.SetSubtitleTrackAndPersistAsync(0, null, null); break;
                    case "session": typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, new PlaybackStartResponse { ProtocolVersion = 2, MediaFileId = 42 }); break;
                    case "play": Set(service, "_playRequestGeneration", (long)Get(service, "_playRequestGeneration")! + 1); break;
                }
                var writes = wire.PreferenceWrites;
                var generation = (long)Get(service, "_loadedMediaInitGeneration")!;
                Set(service, "_restorePausedAfterLoad", true);
                typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, true);
                mpv.SetProperty("sid", "no");
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Loaded() => loaded.TrySetResult();
                mpv.FileLoaded += Loaded;
                try
                {
                    mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
                    await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var deadline = DateTime.UtcNow.AddSeconds(4);
                    while (DateTime.UtcNow < deadline && ((long)Get(service, "_loadedMediaInitGeneration")! <= generation ||
                        service.IsLoading || Get(service, "_loadedMediaInitCts") != null)) await Task.Delay(20);
                    Require((long)Get(service, "_loadedMediaInitGeneration")! > generation && !service.IsLoading && Get(service, "_loadedMediaInitCts") == null,
                        "Actual legacy FILE_LOADED initialization did not finish within its bound.");
                    var expected = change == "mixed" ? mpv.GetEmbeddedSubtitleTracks()[0].Id :
                        change is "current" or "access" or "retired-initializer" ? mpv.GetEmbeddedSubtitleTracks()[1].Id : 0;
                    if (expected > 0)
                        Require(service.ActiveSubtitleServerIndex == 1 && manager.SubtitleSelectionForLoad == 1 &&
                            manager.CaptureLocalSubtitleIntent().HasSelection && !manager.IsSubtitleSelectionAutomatic &&
                            ReferenceEquals(Get(service, "_executedSubtitleTrack"), session.SubtitleUrls.Single()) &&
                            (int?)Get(service, "_executedSubtitleFileId") == 42,
                            "Executed legacy metadata selection was not recorded for presentation and subsequent loads.");
                    Require((int)Math.Round(mpv.GetPropertyDouble("sid")) == expected && mpv.IsPaused && wire.PreferenceWrites == writes && wire.Replans == 0,
                        $"Legacy empty-inventory {change} FILE_LOADED selected SID {mpv.GetPropertyDouble("sid")}, expected {expected}, or changed pause/preferences/transport.");
                    if (expected > 0)
                    {
                        await service.SetSubtitleTrackAndPersistAsync(0, null, null);
                        Require(manager.SubtitleSelectionForLoad == -1 && manager.CaptureLocalSubtitleIntent().Track == null &&
                            service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 && mpv.IsPaused,
                            "A newer explicit Off did not replace the recorded legacy choice for subsequent loads.");
                    }
                    Program.Log($"PASS: legacy empty-inventory {change} actual FILE_LOADED SID={expected}, initial pause/preferences retained, newer Off supersedes its intent.");
                }
                finally { mpv.FileLoaded -= Loaded; }
            }
            finally
            {
                Call(service, "StopPlaybackStallWatchdog");
                Call(service, "StopSubtitleSync");
                await service.CloseAsync();
                Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            }
        }
    }

    private static async Task AiSameFileReplanAsync(string assets, string? retirement = null)
    {
        foreach (var afterBuffer in retirement == null ? new[] { false, true } : new[] { false })
        {
            using var subtitles = new SubtitleServer(File.ReadAllBytes(Path.Combine(assets, "english.srt")))
                { MediaBytes = File.ReadAllBytes(Path.Combine(assets, "tracks.mkv")) };
            using var wire = new Wire { InitialPlaybackUrl = subtitles.Origin + "/media.mkv",
                InventoryHost = new Uri(subtitles.Origin).Host,
                DownloadedInventory = new() { Subtitles = [new() { Id = 91, Language = "fr", Format = "srt" }] } };
            using var http = new HttpClient(wire);
            var api = new SiloApiClient(http); api.SetBaseUrl(subtitles.Origin); api.SetProfile("fixture");
            using var auth = new AuthService(api, new AuthApi(api));
            using var manager = new PlaybackManager(new(api), new(api), auth, api);
            using var service = new PlayerService(new(api), new(api), auth, api,
                new SettingsService(Path.Combine(Program.ResultDirectory, "ai-replan-" + afterBuffer + retirement)), new SettingsApi(api));
            var version = new FileVersion { FileId = 42, Duration = 60, Container = "mkv", AudioTracks = [new() { Language = "eng", Default = true }] };
            manager.UseWatchDetail(new() { ContentId = "movie", Versions = [version] });
            var session = await manager.StartSessionAsync(42);
            using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
            var initialLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void InitiallyLoaded() => initialLoad.TrySetResult();
            mpv.FileLoaded += InitiallyLoaded; mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
            await initialLoad.Task.WaitAsync(TimeSpan.FromSeconds(5)); mpv.FileLoaded -= InitiallyLoaded;
            Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
            typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "movie");
            typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
            typeof(PlayerService).GetProperty("Versions")!.SetValue(service, new List<FileVersion> { version });
            Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bufferingSeen = false;
            void Observe(string[] message)
            {
                if (message.FirstOrDefault() == "silo-ai-replan-ready") ready.TrySetResult();
                if (message.FirstOrDefault() == "silo-ai-replan-buffer" && message.Length > 1)
                {
                    if (message[1] != "false") bufferingSeen = true;
                    else if (bufferingSeen) cleared.TrySetResult();
                }
                if (message.FirstOrDefault() == "silo-ai-replan-active" && message.ElementAtOrDefault(1) == "10") active.TrySetResult();
            }
            mpv.ScriptMessageReceived += Observe;
            try
            {
                var probe = Path.Combine(Program.ResultDirectory, "ai-replan-" + afterBuffer + retirement + ".lua");
                await File.WriteAllTextAsync(probe, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-set-translation-buffering', function(value)
                        mp.commandv('script-message', 'silo-ai-replan-buffer', value)
                    end)
                    mp.register_script_message('osc-set-active-subtitle', function(index)
                        mp.commandv('script-message', 'silo-ai-replan-active', index)
                    end)
                    mp.commandv('script-message', 'silo-ai-replan-ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", probe });
                var readyDeadline = DateTime.UtcNow.AddSeconds(4);
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                mpv.Play();
                while (mpv.IsPaused && DateTime.UtcNow < readyDeadline) await Task.Delay(10);
                Require(!mpv.IsPaused, "AI replan setup did not observe actual playing state within its readiness budget.");
                var owner = Call(service, "CaptureSubtitleOperationOwner", session)!;
                // Establish Begin's downstream buffer state in this fixture's
                // owned directory, rather than writing the installed app's
                // LocalApplicationData/live-subtitles directory.
                var isolatedLivePath = Path.Combine(Program.ResultDirectory, "ai-replan-owned-" + afterBuffer + retirement + ".vtt");
                await File.WriteAllTextAsync(isolatedLivePath, "WEBVTT\n\n");
                Set(service, "_liveSubtitlePath", isolatedLivePath);
                Set(service, "_liveSubtitleJobId", 77L); Set(service, "_liveSubtitleFileId", 42);
                Set(service, "_liveSubtitleOperationOwner", owner);
                Set(service, "_liveSubtitleFileLoadGeneration", Get(service, "_fileLoadGeneration"));
                Set(service, "_preLiveSubtitleSid", Math.Max(0, (int)Math.Round(mpv.GetPropertyDouble("sid"))));
                Set(service, "_resumeAfterLiveSubtitleBuffer", true);
                Set(service, "_liveSubtitleResumeCts", new CancellationTokenSource());
                mpv.Pause();
                Call(service, "SendTranslationBufferingState", true, "French AI");
                Call(service, "PrepareLiveSubtitleTranslationForOwner", 77L, 42, "fr", "French AI", owner);
                while (!mpv.IsPaused && DateTime.UtcNow < readyDeadline) await Task.Delay(10);
                Require(mpv.IsPaused && (bool)Get(service, "_resumeAfterLiveSubtitleBuffer")!, "AI replan fixture did not establish its temporary buffering pause.");
                var bufferToken = ((CancellationTokenSource)Get(service, "_liveSubtitleResumeCts")!).Token;
                var livePath = (string)Get(service, "_liveSubtitlePath")!;
                using var cues = JsonDocument.Parse("""{"job_id":77,"cues":[{"start":0,"end":5,"text":"AI replan cue"}]}""");
                if (afterBuffer) Call(service, "ApplySubtitleTranslationCues", cues.RootElement);
                var wasPaused = (bool)Call(service, "CaptureUserPausedState")!;
                Require(!wasPaused, "An AI buffering pause was captured as viewer pause intent.");
                var replacement = await manager.ReplanQualityAsync("720p", 0);
                Require(!ReferenceEquals(session, replacement), "Quality replan did not replace its response object.");
                if (retirement != null)
                {
                    // Model an accepted plan changing the effective file, not
                    // merely replacing the response object for the same file.
                    replacement.MediaFileId = 43;
                    version.FileId = 43;
                }
                if (retirement == "timeout")
                {
                    mpv.SetSubtitleTrack(1);
                    // Invoke the exact timer callback without a 30-second sleep.
                    Call(service, "ResumeAfterLiveSubtitleBuffer", 77L);
                    await cleared.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(bufferToken.IsCancellationRequested && Get(service, "_liveSubtitleResumeCts") == null &&
                        !(bool)Get(service, "_resumeAfterLiveSubtitleBuffer")! && (long)Get(service, "_liveSubtitleJobId")! == 0 &&
                        Get(service, "_liveSubtitleOperationOwner") == null && !File.Exists(livePath) &&
                        mpv.IsPaused && mpv.GetPropertyDouble("sid") == 1 && (bool)Call(service, "CaptureUserPausedState")!,
                        "Retired AI timeout left buffering/timer state or changed the replacement file's pause/SID.");
                    Program.Log("PASS: AI timeout after MediaFileId changes clears the overlay, cancels the resume CTS and retires pause intent without touching replacement playback.");
                    continue;
                }
                Call(service, "WireMpvEvents");
                var initializationGeneration = (long)Get(service, "_loadedMediaInitGeneration")!;
                var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Reloaded() => reloaded.TrySetResult();
                service.ContentLoaded += Reloaded;
                try
                {
                    var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", replacement, version,
                        subtitles.Origin + "/media.mkv", 0d, CancellationToken.None)!;
                    await prepare;
                    var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
                    Call(service, "ApplyPreparedTransport", prepared); Call(service, "BeginMpvLoad", prepared, wasPaused);
                    if (retirement != null)
                        Require(bufferToken.IsCancellationRequested && Get(service, "_liveSubtitleResumeCts") == null &&
                            !(bool)Get(service, "_resumeAfterLiveSubtitleBuffer")! && (long)Get(service, "_liveSubtitleJobId")! == 0 &&
                            Get(service, "_liveSubtitleOperationOwner") == null && !File.Exists(livePath),
                            "Accepting a different media file did not immediately retire AI buffering and its resume CTS.");
                    await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (((long)Get(service, "_loadedMediaInitGeneration")! <= initializationGeneration ||
                        Get(service, "_loadedMediaInitCts") != null) && DateTime.UtcNow < deadline) await Task.Delay(20);
                    Require((long)Get(service, "_loadedMediaInitGeneration")! > initializationGeneration &&
                        Get(service, "_loadedMediaInitCts") == null, "AI replacement initialization did not finish.");
                }
                finally { service.ContentLoaded -= Reloaded; }
                if (retirement != null)
                {
                    var pausedControlDeadline = DateTime.UtcNow.AddSeconds(4);
                    await cleared.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(!mpv.IsPaused, "AI retirement carried its temporary pause into replacement playback.");
                    service.SetPaused(true);
                    mpv.SetSubtitleTrack(1);
                    var cachedPauseBeforeNativeRead = mpv.IsPaused;
                    var capturePauseBeforeNativeRead = Call(service, "CaptureUserPausedState");
                    Program.Log($"Retired AI BEFORE late callbacks: initialCachedPause={cachedPauseBeforeNativeRead}, initialCapturePause={capturePauseBeforeNativeRead}, nativePause={mpv.GetPropertyFlag("pause")}, cachedPause={mpv.IsPaused}, SID={mpv.GetPropertyDouble("sid")}, prefs={wire.PreferenceWrites}, job={Get(service, "_liveSubtitleJobId")}, CTS={Get(service, "_liveSubtitleResumeCts") != null}, capturePause={Call(service, "CaptureUserPausedState")}.");
                    while (!mpv.IsPaused && DateTime.UtcNow < pausedControlDeadline) await Task.Delay(10);
                    Require(mpv.IsPaused && mpv.GetPropertyFlag("pause") && mpv.GetPropertyDouble("sid") == 1,
                        "Retired AI control did not establish its replacement viewer pause/SID within its original readiness budget.");
                    Call(service, "ApplySubtitleTranslationCues", cues.RootElement);
                    Call(service, "ResumeAfterLiveSubtitleBuffer", 77L);
                    Program.Log($"Retired AI AFTER late callbacks: nativePause={mpv.GetPropertyFlag("pause")}, cachedPause={mpv.IsPaused}, SID={mpv.GetPropertyDouble("sid")}, prefs={wire.PreferenceWrites}, job={Get(service, "_liveSubtitleJobId")}, CTS={Get(service, "_liveSubtitleResumeCts") != null}, capturePause={Call(service, "CaptureUserPausedState")}.");
                    Require(mpv.IsPaused && mpv.GetPropertyDouble("sid") == 1 && wire.PreferenceWrites == 0 &&
                        (bool)Call(service, "CaptureUserPausedState")! && Get(service, "_liveSubtitleResumeCts") == null,
                        "Late retired AI callbacks changed the replacement file's SID, pause or preference.");
                    Program.Log("PASS: accepted MediaFileId change clears AI buffering and its resume CTS before load; late callbacks preserve replacement SID and user pause.");
                    continue;
                }
                Call(service, "ApplySubtitleTranslationCues", cues.RootElement);
                await cleared.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(!mpv.IsPaused && Get(service, "_liveSubtitleResumeCts") == null,
                    "Same-file replan stranded the AI buffer pause or timer.");
                await (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, (long?)91, owner)!;
                await active.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var selected = manager.CurrentSession!.SubtitleUrls.Single(track => track.Id == 91);
                var sids = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(service.ActiveSubtitleServerIndex == selected.Index && ReferenceEquals(Get(service, "_executedSubtitleTrack"), selected) &&
                    sids.TryGetValue(selected.Index, out var sid) && mpv.GetPropertyDouble("sid") == sid &&
                    wire.PreferenceWrites == 1 && !mpv.IsPaused,
                    "AI completion after a same-file replan lost its subtitle, OSC selection, preference or play intent.");
                Program.Log($"PASS: AI quality replan afterBuffer={afterBuffer} retains file ownership, clears buffering, and executes/publishes the completed subtitle.");
                await ConsecutiveAiJobsAsync(service, manager, mpv, wire, selected);
            }
            finally
            {
                mpv.ScriptMessageReceived -= Observe;
                Call(service, "ClearLiveSubtitleTranslation", false); Call(service, "StopSubtitleSync");
                Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
                Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
                Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            }
        }
    }

    private static async Task ConsecutiveAiJobsAsync(PlayerService service, PlaybackManager manager, MpvPlayer mpv,
        Wire wire, SubtitleTrackInfo completed)
    {
        Require((long)Get(service, "_liveSubtitleJobId")! == 0 && (int)Get(service, "_liveSubtitleFileId")! == 0 &&
            (long)Get(service, "_liveSubtitleFileLoadGeneration")! == 0 && (int)Get(service, "_preLiveSubtitleSid")! == 0 &&
            Get(service, "_liveSubtitleOperationOwner") == null && Get(service, "_liveSubtitleResumeCts") == null &&
            Get(service, "_liveSubtitlePath") == null && !(bool)Get(service, "_resumeAfterLiveSubtitleBuffer")!,
            "Committing the final AI subtitle did not retire its live job and restore/buffering state.");
        var native = new SubtitleTrackInfo { Index = manager.CurrentSession!.SubtitleUrls.Max(track => track.Index) + 1,
            Source = "embedded", Language = "en", Codec = "subrip", ContainerTrackId = "2", StreamIndex = 1 };
        manager.CurrentSession.SubtitleUrls.Add(native);
        var jobId = 78L;
        foreach (var scenario in new[] { "before-cues", "live", "start-native", "viewer-native", "viewer-off", "viewer-native-more-cues", "viewer-off-more-cues" })
        {
            var initial = scenario == "start-native" ? native : completed;
            await service.SetSubtitleTrackAndPersistAsync(initial.Index + 1, initial.Language, initial);
            var expectedSid = mpv.GetPropertyDouble("sid");
            var expectedTrack = (SubtitleTrackInfo?)initial;
            var owner = Call(service, "CaptureSubtitleOperationOwner", manager.CurrentSession)!;
            Call(service, "ClearLiveSubtitleTranslation", true);
            var ownedPath = Path.Combine(Program.ResultDirectory, $"consecutive-ai-{jobId}.vtt");
            await File.WriteAllTextAsync(ownedPath, "WEBVTT\n\n");
            Set(service, "_liveSubtitlePath", ownedPath);
            Set(service, "_liveSubtitleJobId", jobId); Set(service, "_liveSubtitleFileId", 42);
            Set(service, "_liveSubtitleOperationOwner", owner);
            Set(service, "_liveSubtitleFileLoadGeneration", Get(service, "_fileLoadGeneration"));
            Set(service, "_preLiveSubtitleSid", (int)Math.Round(expectedSid));
            Set(service, "_resumeAfterLiveSubtitleBuffer", !mpv.IsPaused);
            Set(service, "_liveSubtitleResumeCts", new CancellationTokenSource());
            mpv.Pause();
            Call(service, "SendTranslationBufferingState", true, "Second French AI");
            Call(service, "PrepareLiveSubtitleTranslationForOwner", jobId, 42, "fr", "Second French AI", owner);
            var pausedDeadline = DateTime.UtcNow.AddSeconds(4);
            while (!mpv.IsPaused && DateTime.UtcNow < pausedDeadline) await Task.Delay(10);
            Require(mpv.IsPaused, "Consecutive AI setup did not observe its temporary native buffer pause.");
            Require(mpv.GetPropertyDouble("sid") == expectedSid && service.ActiveSubtitleServerIndex == initial.Index &&
                ReferenceEquals(Get(service, "_executedSubtitleTrack"), initial),
                $"Starting a second AI job ({scenario}) replaced the current subtitle with a stale pre-translation SID.");
            var livePath = (string)Get(service, "_liveSubtitlePath")!;
            using var cues = JsonDocument.Parse(JsonSerializer.Serialize(new { job_id = jobId,
                cues = new[] { new { start = 0, end = 5, text = "Second AI job cue" } } }));
            if (scenario != "before-cues")
            {
                Call(service, "ApplySubtitleTranslationCues", cues.RootElement);
                Require((int)Get(service, "_liveSubtitleSid")! > 0 &&
                    mpv.GetPropertyDouble("sid") == (int)Get(service, "_liveSubtitleSid")!,
                    "The second AI job did not attach its actual live track.");
            }
            if (scenario.StartsWith("viewer-", StringComparison.Ordinal))
            {
                expectedTrack = scenario.StartsWith("viewer-off", StringComparison.Ordinal) ? null : native;
                await service.SetSubtitleTrackAndPersistAsync(expectedTrack == null ? 0 : expectedTrack.Index + 1,
                    expectedTrack?.Language, expectedTrack);
                expectedSid = mpv.GetPropertyDouble("sid");
                if (scenario.EndsWith("-more-cues", StringComparison.Ordinal))
                {
                    var writesBeforeCue = wire.PreferenceWrites;
                    Call(service, "ApplySubtitleTranslationCues", cues.RootElement);
                    Require(mpv.GetPropertyDouble("sid") == expectedSid && service.ActiveSubtitleServerIndex == (expectedTrack?.Index ?? -1) &&
                        ReferenceEquals(Get(service, "_executedSubtitleTrack"), expectedTrack) && wire.PreferenceWrites == writesBeforeCue,
                        "A late live cue replaced the newer viewer choice before its job was retired.");
                }
            }
            var intent = manager.CaptureLocalSubtitleIntent();
            var writes = wire.PreferenceWrites;
            if (jobId % 2 == 0) service.FailLiveSubtitleTranslation(jobId, "Synthetic failure");
            else
            {
                using var failed = JsonDocument.Parse(JsonSerializer.Serialize(new { job_id = jobId }));
                Call(service, "ApplySubtitleTranslationFailed", failed.RootElement);
            }
            var resumedDeadline = DateTime.UtcNow.AddSeconds(4);
            while (mpv.IsPaused && DateTime.UtcNow < resumedDeadline) await Task.Delay(10);
            Require(mpv.GetPropertyDouble("sid") == expectedSid && service.ActiveSubtitleServerIndex == (expectedTrack?.Index ?? -1) &&
                ReferenceEquals(Get(service, "_executedSubtitleTrack"), expectedTrack) && manager.CaptureLocalSubtitleIntent() == intent &&
                manager.SubtitleSelectionForLoad == (expectedTrack?.Index ?? -1) && wire.PreferenceWrites == writes && !mpv.IsPaused &&
                (long)Get(service, "_liveSubtitleJobId")! == 0 && Get(service, "_liveSubtitleResumeCts") == null && !File.Exists(livePath),
                $"A failed second AI job ({scenario}) lost the executed SID, viewer choice, intent, preference or play state.");
            jobId++;
        }
        Program.Log("PASS: consecutive same-load AI jobs retain the completed/current subtitle on start and restore the latest executed native/sidecar/Off choice on failure, before cues and after later live cues.");
    }

    private static async Task FreshPrePlayWithoutRouteAsync(string origin)
    {
        using var wire = new Wire { InitialPlaybackUrl = origin + "/media.mkv" };
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://subtitle-identity.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "subtitle-identity-fresh-preplay")), new SettingsApi(api));
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv);
        Call(service, "WireMpvEvents");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notice = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(string[] message)
        {
            if (message.FirstOrDefault() == "silo-initial-refusal-ready") ready.TrySetResult();
            if (message.FirstOrDefault() == "silo-initial-refusal-notice" && message.Length > 1) notice.TrySetResult(message[1]);
        }
        mpv.ScriptMessageReceived += Observe;
        try
        {
            var probe = Path.Combine(Program.ResultDirectory, "identity-initial-refusal.lua");
            await File.WriteAllTextAsync(probe, """
                local mp = require 'mp'
                mp.register_script_message('osc-show-notice', function(payload)
                    mp.commandv('script-message', 'silo-initial-refusal-notice', payload)
                end)
                mp.commandv('script-message', 'silo-initial-refusal-ready')
                """);
            Call(mpv, "Command", (object)new[] { "load-script", probe });
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var watch = new WatchDetailResponse
            {
                ContentId = "fresh-preplay", Type = "movie", PreparedFileId = 42,
                Versions = [new() { FileId = 42, Duration = 60, Container = "mkv",
                    AudioTracks = [new() { Language = "eng", Default = true }],
                    SubtitleTracks = [new() { Index = 77, Language = "eng", Codec = "webvtt" }] }],
            };
            wire.InitialWatchDetail = watch;
            await service.PlayAsync("fresh-preplay", fromStart: true, fileId: 42,
                subtitleSelection: 0, prefetchedWatchDetail: watch);
            using var payload = JsonDocument.Parse(await notice.Task.WaitAsync(TimeSpan.FromSeconds(8)));
            Require(payload.RootElement.GetProperty("message").GetString()!.Contains("another subtitle", StringComparison.OrdinalIgnoreCase),
                "A fresh unroutable pre-play choice did not show actionable feedback.");
            for (var attempt = 0; attempt < 200 && (service.IsLoading || Get(service, "_loadedMediaInitCts") != null); attempt++)
                await Task.Delay(20);
            var manager = (PlaybackManager)Get(service, "_playbackManager")!;
            Require(manager != null && !service.IsLoading && Get(service, "_loadedMediaInitCts") == null &&
                service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                !manager.CaptureLocalSubtitleIntent().HasSelection && manager.IsSubtitleSelectionAutomatic &&
                manager.SubtitleSelectionForLoad == -1 && Get(service, "_pendingExplicitInitialSubtitleOwner") == null &&
                Get(service, "_pendingSubtitlePreferenceOwner") == null && Get(service, "_pendingSubtitlePreferenceIndex") == null &&
                wire.PreferenceWrites == 0 && wire.Replans == 0,
                "A fresh unroutable pre-play choice retained unexecuted intent, changed the selection or saved a preference.");
            await manager.ReplanQualityAsync("720p", 0);
            Require(wire.ReplanRequests.Count == 1 &&
                !wire.ReplanRequests[0].GetProperty("selected_tracks").TryGetProperty("subtitle", out _),
                "A quality replan resent the refused pre-play subtitle.");
            Program.Log("PASS: fresh PlayAsync refuses an index-only WebVTT pre-play pick with actionable feedback, restores automatic intent and does not resend it on a quality replan.");
        }
        finally
        {
            mpv.ScriptMessageReceived -= Observe;
            await service.CloseAsync();
            Set(service, "_mpv", null);
        }
    }

    private static async Task RefreshedDownloadedSubtitlesAsync(string assets, bool storedIdentity = true, bool trackIdentity = true)
    {
        using var subtitles = new SubtitleServer(File.ReadAllBytes(Path.Combine(assets, "japanese.srt")));
        using var wire = new Wire { InventoryHost = new Uri(subtitles.Origin).Host };
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl(subtitles.Origin); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "subtitle-refresh-identity")), new SettingsApi(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); mpv.Pause();
        var original = new SubtitleTrackInfo { Index = 1, Id = storedIdentity ? 100 : null,
            TrackId = trackIdentity ? "download:100" : null, SyncKey = storedIdentity ? "stored-100" : null,
            Source = "downloaded", Codec = "srt", Language = "en", Url = subtitles.Origin + "/english.srt" };
        var session = new PlaybackStartResponse { SessionId = "refresh", ProtocolVersion = 3, MediaFileId = 42,
            SelectedSubtitleTrackId = original.TrackId, SelectedSubtitleTrackIndex = original.Index,
            SubtitleMode = "sidecar", SubtitleUrls = [new() { Index = 0, Source = "embedded" }, original] };
        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
        manager.UseWatchDetail(new() { ContentId = "movie", SeriesId = "series", Versions = [new() { FileId = 42 }] });
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "movie");
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
        try
        {
            var englishPath = Path.Combine(Program.ResultDirectory, "refresh-english.srt");
            File.Copy(Path.Combine(assets, "english.srt"), englishPath, overwrite: true);
            Require(mpv.AddSubtitle(englishPath, "Original", "en", select: true), "Could not load the original download.");
            var originalSid = (int)Math.Round(mpv.GetPropertyDouble("sid"));
            var sids = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
            sids[1] = originalSid; files[1] = englishPath;
            ((List<string>)Get(service, "_subtitleSyncFiles")!).Add(englishPath);
            Set(service, "_activeSubtitleServerIndex", 1);
            Call(service, "CommitExecutedSubtitleSelection", original);
            Set(service, "_pendingInitialServerSubtitleIndex", 1);
            Set(service, "_pendingInitialSubtitleFileId", 42);
            manager.RecordLocalSubtitleSelection(1);
            wire.DownloadedInventory = new() { Subtitles =
                [new() { Id = 200, Language = "ja", Format = "srt" }, new() { Id = 100, Language = "en", Format = "srt" }] };

            if (!storedIdentity)
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var notices = 0;
                var substitutedHighlights = 0;
                var retainedHighlights = 0;
                void Observe(string[] message)
                {
                    if (message.FirstOrDefault() == "silo-refresh-active" && message.Length > 1)
                    {
                        if (message[1] == original.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            Interlocked.Increment(ref retainedHighlights);
                        else Interlocked.Increment(ref substitutedHighlights);
                    }
                    if (message.FirstOrDefault() == "silo-refresh-ready") ready.TrySetResult();
                    if (message.FirstOrDefault() == "silo-refresh-notice") Interlocked.Increment(ref notices);
                    if (message.FirstOrDefault() == "silo-refresh-drained") drained.TrySetResult();
                }
                mpv.ScriptMessageReceived += Observe;
                try
                {
                    var probe = Path.Combine(Program.ResultDirectory, "refresh-unbound.lua");
                    await File.WriteAllTextAsync(probe, """
                        local mp = require 'mp'
                        mp.register_script_message('osc-show-notice', function()
                            mp.commandv('script-message', 'silo-refresh-notice')
                        end)
                        mp.register_script_message('osc-set-active-subtitle', function(index)
                            mp.commandv('script-message', 'silo-refresh-active', index)
                        end)
                        mp.register_script_message('silo-refresh-drain', function()
                            mp.commandv('script-message', 'silo-refresh-drained')
                        end)
                        mp.commandv('script-message', 'silo-refresh-ready')
                        """);
                    Call(mpv, "Command", (object)new[] { "load-script", probe });
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    var intent = manager.CaptureLocalSubtitleIntent();
                    var writes = wire.PreferenceWrites;
                    foreach (var refresh in new[] { 1, 2 })
                    {
                        await service.RefreshSubtitlesAfterAiAsync(42);
                        Require(mpv.GetPropertyDouble("sid") == originalSid &&
                            ReferenceEquals(Get(service, "_executedSubtitleTrack"), original) &&
                            session.SubtitleUrls.Contains(original) && original.Index == 3 && service.ActiveSubtitleServerIndex == 3 &&
                            sids[3] == originalSid && files[3] == englishPath && File.Exists(englishPath) &&
                            !sids.ContainsKey(1) && !files.ContainsKey(1) && !sids.ContainsKey(2) && !files.ContainsKey(2) &&
                            (int?)Get(service, "_pendingInitialServerSubtitleIndex") == 3 && session.SelectedSubtitleTrackIndex == 3 &&
                            manager.SubtitleSelectionForLoad == 3 && !manager.IsRetainedSubtitleUnavailable &&
                            session.SubtitleUrls.Select(track => track.Index).Distinct().Count() == session.SubtitleUrls.Count &&
                            manager.CaptureLocalSubtitleIntent() == intent && wire.PreferenceWrites == writes && mpv.IsPaused,
                            $"Refresh {refresh} lost an identity-unknown download's row/active state or lent its index/cache to another row.");
                    }
                    Call(mpv, "Command", (object)new[] { "script-message", "silo-refresh-drain" });
                    await drained.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(Volatile.Read(ref notices) == 0 && Volatile.Read(ref substitutedHighlights) == 0 &&
                        Volatile.Read(ref retainedHighlights) == 2,
                        "An identity-unknown download lost its OSC highlight, was falsely reported as removed or highlighted a different refreshed row.");
                    var explicitPick = session.SubtitleUrls.Single(track => track.Id == 200);
                    await service.SetSubtitleTrackAndPersistAsync(explicitPick.Index + 1, explicitPick.Language, explicitPick);
                    Require(service.ActiveSubtitleServerIndex == explicitPick.Index && sids[explicitPick.Index] != originalSid &&
                        mpv.GetPropertyDouble("sid") == sids[explicitPick.Index] && wire.PreferenceWrites == writes + 1,
                        "An explicit pick after an unbound refresh reused the old SID or failed to execute its own subtitle.");
                }
                finally { mpv.ScriptMessageReceived -= Observe; }
                Program.Log($"PASS: a plan download without stored keys (TrackId={trackIdentity}) keeps its exact row, active highlight, pending/granted intent and SID/cache across refreshes without substitution or preference writes.");
                return;
            }

            await service.RefreshSubtitlesAfterAiAsync(42);
            var retained = session.SubtitleUrls.Single(track => track.Id == 100);
            Require(retained.Index == 2 && service.ActiveSubtitleServerIndex == 2 &&
                ReferenceEquals(Get(service, "_executedSubtitleTrack"), retained) &&
                (int?)Get(service, "_pendingInitialServerSubtitleIndex") == 2 && session.SelectedSubtitleTrackIndex == 2 &&
                manager.SubtitleSelectionForLoad == 2 && mpv.GetPropertyDouble("sid") == originalSid,
                "Inventory refresh failed to rebind active, executed, granted or pending subtitle identity.");
            Require(!sids.ContainsKey(1) && !files.ContainsKey(1) && sids[2] == originalSid && files[2] == englishPath,
                "A new downloaded row inherited the previous row's SID or prefetched bytes.");
            var added = session.SubtitleUrls.Single(track => track.Id == 200);
            await service.SetSubtitleTrackAndPersistAsync(added.Index + 1, added.Language, added);
            var addedSid = sids[added.Index];
            Require(addedSid != originalSid && mpv.GetPropertyDouble("sid") == addedSid &&
                File.ReadAllText(files[added.Index]) == File.ReadAllText(Path.Combine(assets, "japanese.srt")),
                "Selecting a reused index did not load that row's own sidecar bytes.");

            wire.DownloadedInventory.Subtitles[0] = new() { Id = 300, Language = "ja", Format = "srt" };
            await service.RefreshSubtitlesAfterAiAsync(42, preferredSubtitleId: 300);
            Require(sids[1] != addedSid && mpv.GetPropertyDouble("sid") == sids[1] &&
                ((SubtitleTrackInfo?)Get(service, "_executedSubtitleTrack"))?.Id == 300,
                "Translation-completed selection reused a removed row's cached SID.");

            wire.DownloadedInventory.Subtitles.RemoveAt(0);
            await service.RefreshSubtitlesAfterAiAsync(42);
            Require(service.ActiveSubtitleServerIndex == -1 && Get(service, "_executedSubtitleTrack") == null &&
                mpv.GetPropertyDouble("sid") <= 0 && sids[1] == originalSid && files[1] == englishPath && !sids.ContainsKey(2),
                "Removal reused an active index or failed to move the retained download's caches.");
            Require(mpv.IsPaused, "Subtitle refresh changed viewer pause intent.");
            Program.Log("PASS: downloaded subtitle refresh rebinds active/pending identity and SID/prefetch caches across insertion, preferred replacement and removal.");
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static async Task ExpectUnavailableAsync(Func<Task> action)
    {
        try { await action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("SiloCast reported success for an unavailable subtitle.");
    }

    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class SubtitleServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Origin { get; }
        public volatile bool Fail;
        public byte[]? MediaBytes;
        public int Requests;
        public Func<Task>? BeforeSubtitleResponse;
        public Func<Task>? BeforeMediaResponse;
        public SubtitleServer(byte[] bytes)
        {
            var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reserve.Start();
            var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext request;
                    try { request = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                    Interlocked.Increment(ref Requests);
                    if (request.Request.Url?.AbsolutePath == "/media.mkv" && MediaBytes is { } media)
                    {
                        if (BeforeMediaResponse != null) await BeforeMediaResponse();
                        var start = long.Parse((request.Request.Headers["Range"] ?? "bytes=0-").Split('=')[1].Split('-')[0]);
                        if (start >= media.Length) { request.Response.StatusCode = 416; request.Response.Close(); continue; }
                        request.Response.StatusCode = 206;
                        request.Response.Headers["Content-Range"] = $"bytes {start}-{media.Length - 1}/{media.Length}";
                        request.Response.ContentLength64 = media.Length - start;
                        await request.Response.OutputStream.WriteAsync(media.AsMemory((int)start));
                        request.Response.Close();
                        continue;
                    }
                    if (Fail)
                    {
                        request.Response.StatusCode = 503;
                        request.Response.Close();
                        continue;
                    }
                    if (BeforeSubtitleResponse is {} beforeSubtitle) await beforeSubtitle();
                    request.Response.ContentType = "application/x-subrip"; request.Response.ContentLength64 = bytes.Length;
                    await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                }
            });
        }
        public void Dispose() => _listener.Close();
    }
    private sealed class Wire : HttpMessageHandler
    {
        public int PreferenceWrites, Replans;
        public bool AiTranslateEnabled;
        public int AiStarts;
        public Func<Task>? BeforeAiStatus;
        public Func<Task>? BeforeAiStart, BeforeAiJob, BeforeAiInventory;
        public int? ReplanFileId;
        public int AiJobReads, AiInventoryReads;
        public long? AiResultSubtitleId;
        public JsonElement? AiStartRequest;
        public string? InitialPlaybackUrl;
        public string? InventoryHost;
        public SubtitleListResponse? DownloadedInventory;
        public WatchDetailResponse? InitialWatchDetail;
        public List<JsonElement> ReplanRequests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "subtitle-identity.invalid" && request.RequestUri.Host != InventoryHost)
                throw new InvalidOperationException("Unexpected fixture networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/subtitles/ai/status")
            {
                if (BeforeAiStatus is {} before) await before();
                return Json(JsonSerializer.Serialize(new { enabled = AiTranslateEnabled, transcribe_enabled = false }));
            }
            if (path == "/api/v2/subtitles/ai/translate")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                AiStartRequest = body.RootElement.Clone();
                AiStarts++; if (BeforeAiStart is {} before) await before();
                return Json("""{"job":{"id":77,"status":"running"}}""");
            }
            if (path == "/api/v2/subtitles/ai/jobs/77")
            {
                AiJobReads++; if (BeforeAiJob is {} before) await before();
                return Json(JsonSerializer.Serialize(new { job = new { id = 77, status = "completed", result_subtitle_id = AiResultSubtitleId } }));
            }
            if (path == "/api/v2/subtitles/42")
            {
                AiInventoryReads++; if (BeforeAiInventory is {} before) await before();
            }
            if (path == "/api/v2/subtitles/42" && DownloadedInventory != null)
                return Json(JsonSerializer.Serialize(DownloadedInventory,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            if (path.Contains("-prefs/")) PreferenceWrites++;
            if (path.EndsWith("/replan"))
            {
                Replans++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                ReplanRequests.Add(body.RootElement.Clone());
            }
            if (InitialPlaybackUrl != null)
            {
                if (path.StartsWith("/api/v2/watch/", StringComparison.Ordinal))
                    return Json(JsonSerializer.Serialize(InitialWatchDetail,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
                if (path.EndsWith("/capabilities"))
                    return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
                if (path.EndsWith("/start") || path.EndsWith("/replan"))
                    return Json(JsonSerializer.Serialize(new
                    {
                        protocol_version = 3, outcome = "playable", session_id = "fresh-preplay",
                        playback_plan = new
                        {
                            plan_id = $"initial-{Replans}", plan_attempt_key = "key", session_id = "fresh-preplay",
                            effective_media_file_id = path.EndsWith("/replan") ? ReplanFileId ?? 42 : 42, delivery = "original_http", active_quality = "original",
                            stream = new { url = InitialPlaybackUrl }, timeline = new { can_seek_anywhere = true },
                            source = new { duration_seconds = 60 },
                            selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 } },
                            subtitle = new { mode = "off", inventory = new[]
                            {
                                new { track_id = "index-only", combined_index = 9, stream_index = 77,
                                    language = "eng", codec = "webvtt", source = "embedded", native_selectable = false },
                            } },
                        },
                    }));
            }
            return Json("{}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
