using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class SubtitleIdentityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, bool unresolvedOnly = false, bool regionalOnly = false)
    {
        var assets = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity"));
        using var subtitles = new SubtitleServer(File.ReadAllBytes(Path.Combine(assets, "english.srt")));
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
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE") == "legacy-preplay")
            {
                session.SubtitleUrls = [];
                var watch = new WatchDetailResponse();
                var version = new FileVersion { FileId = 42 };
                Call(service, "ResolveInitialSubtitleSelection", watch, version, session, null, 1, null);
                Call(service, "ApplyPendingSubtitleSelection");
                var owner = new CancellationTokenSource();
                Set(service, "_loadedMediaInitCts", owner);
                await (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
                Require(mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused && wire.PreferenceWrites == 0,
                    "Legacy explicit pre-play ordinal was discarded when both response and watch metadata inventories were absent.");
                Program.Log("PASS: legacy pre-play ordinal executes only against the loaded file, preserving viewer pause and avoiding invented server preference indices.");
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
                    Require(castTracks.All(t => t!["trackId"]!.GetValue<int>() != 4), "SiloCast advertised an explicitly unplayable subtitle.");
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
            }
            finally { japanese.Unplayable = false; }
            Require(wire.PreferenceWrites == cycleWrites && Volatile.Read(ref subtitles.Requests) == cycleRequests && mpv.IsPaused,
                "Local caption cycling downloaded a remote sidecar, saved a series preference or resumed playback.");
            Program.Log("PASS: subtitle identity uses actual loaded Matroska tracks across index-only gap and sidecar addition; unavailable forged service/native callbacks preserve selection/preferences; Off and genuine pause survive.");
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
        public int Requests;
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
                    if (Fail)
                    {
                        request.Response.StatusCode = 503;
                        request.Response.Close();
                        continue;
                    }
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "subtitle-identity.invalid") throw new InvalidOperationException("Unexpected fixture networking.");
            if (request.RequestUri.AbsolutePath.Contains("-prefs/")) PreferenceWrites++;
            if (request.RequestUri.AbsolutePath.EndsWith("/replan")) Replans++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
