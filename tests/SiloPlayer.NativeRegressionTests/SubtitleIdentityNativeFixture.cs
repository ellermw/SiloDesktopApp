using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static partial class SubtitleIdentityNativeFixture
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
        CheckInitialPlayableSelection(service, manager);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); mpv.Pause();
        try
        {
            var demuxer = (string?)Call(mpv, "CurrentDemuxer");
            Program.Log($"Subtitle identity fixture current-demuxer={demuxer ?? "(unavailable)"}");
            Require(demuxer switch
            {
                "mkv" => mpv.HasContainerTrackIds() && !mpv.HasFfmpegTrackIndices(),
                "lavf" => mpv.HasFfmpegTrackIndices() && !mpv.HasContainerTrackIds(),
                _ => false
            }, "Matroska must attest only the active demuxer's identity namespace.");
            var embedded = mpv.GetEmbeddedSubtitleTracks();
            Require(embedded.Count == 2 && (demuxer == "mkv"
                ? embedded.Any(t => t.Id == 1 && t.ContainerTrackId == "2") && embedded.Any(t => t.Id == 2 && t.ContainerTrackId == "3")
                : embedded.Any(t => t.Id == 1 && t.StreamIndex == 1) && embedded.Any(t => t.Id == 2 && t.StreamIndex == 2)),
                "Loaded Matroska subtitle identities do not match the attested demuxer namespace.");
            var closeout = Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE");
            if (closeout is "cast-off" or "cast-routes" or "reload-identity" or "preplay-legacy" or "async-add" or "async-lifecycle" or "async-service" or "async-core-probe" or "retired-read-cache" or "mime-busy" or "mime-ready" or "refresh-raw-labels" or "async-prompt-cancel" or "async-late-cleanup" or "fallback-deadline" or "cast-poll")
            {
                if (closeout == "async-prompt-cancel") await CheckPromptCancellationAsync(mpv, subtitles);
                else if (closeout == "async-late-cleanup") await CheckLateCleanupAsync(mpv, subtitles, assets);
                else if (closeout == "fallback-deadline") await CheckFallbackDeadlineAsync(service, manager, mpv, subtitles, wire);
                else if (closeout == "cast-poll") await CheckCastPollingAsync(service, manager, mpv, subtitles, wire);
                else if (closeout == "refresh-raw-labels")
                {
                    var cases = new (string? Release, string? Provider, string? Title, string Expected)[]
                    {
                        ("Release A", "Provider A", "Display title", "Release A (Provider A)"),
                        ("Release B", "", null, "Release B ()"),
                        ("", "Provider C", "Display C", " (Provider C)"),
                        ("", "", "Display only", ""),
                        (null, null, null, ""),
                        (" ", "Provider D", null, "  (Provider D)"),
                    };
                    for (var offset = 0; offset < cases.Length; offset++)
                    {
                        var item = cases[offset]; var id = 70 + offset;
                        wire.SubtitleList = JsonSerializer.Serialize(new { subtitles = new[] { new { id, media_file_id = 42,
                            language = "en", format = "srt", release_name = item.Release, provider = item.Provider, title = item.Title } } });
                        await service.RefreshSubtitlesAfterAiAsync(42).WaitAsync(TimeSpan.FromSeconds(5));
                        var track = session.SubtitleUrls.Single(value => value.Id == id);
                        track.Url = subtitles.Origin + "/raw-label-" + id + ".srt";
                        await service.SetSubtitleTrackAndPersistAsync(track.Index + 1, track.Language, track).WaitAsync(TimeSpan.FromSeconds(5));
                        Require(service.ActiveSubtitleServerIndex == track.Index && mpv.GetPropertyDouble("sid") > 0 && mpv.IsPaused &&
                            wire.PreferenceWrites == offset + 1,
                            "The raw label fixture did not execute and save its current downloaded subtitle before checking its signature.");
                        Require(wire.SavedLabels.Last() == item.Expected && track.NativeTitle == item.Expected,
                            "A refreshed downloaded subtitle lost its exact server v3 raw label or persisted a display/language fallback.");
                    }
                    session.ProtocolVersion = 2;
                    wire.SubtitleList = """{"subtitles":[{"id":99,"media_file_id":42,"language":"en","format":"srt","title":"Legacy display"}]}""";
                    await service.RefreshSubtitlesAfterAiAsync(42).WaitAsync(TimeSpan.FromSeconds(5));
                    var legacy = session.SubtitleUrls.Single(value => value.Id == 99);
                    legacy.Url = subtitles.Origin + "/legacy-label.srt";
                    await service.SetSubtitleTrackAndPersistAsync(legacy.Index + 1, legacy.Language, legacy).WaitAsync(TimeSpan.FromSeconds(5));
                    Require(wire.SavedLabels.Last() == "Legacy display" && mpv.IsPaused,
                        "Raw v3 downloaded labels changed the existing legacy display-label preference contract.");
                    Program.Log("PASS: actual refresh/select/save preserves six raw v3 label shapes and a legacy display-label control.");

                    // Queue the actual refreshed Id-bearing row, then replace
                    // the grant with v3's SyncKey-only stored identity.
                    session.ProtocolVersion = 3;
                    wire.SubtitleList = """{"subtitles":[{"id":100,"media_file_id":42,"language":"en","format":"srt","title":"Queued download"}]}""";
                    var queuedWrites = wire.PreferenceWrites;
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, true);
                    await service.RefreshSubtitlesAfterAiAsync(42, 100).WaitAsync(TimeSpan.FromSeconds(5));
                    var refreshed = session.SubtitleUrls.Single(track => track.Id == 100);
                    Require(refreshed.TrackId == null && refreshed.SyncKey == "stored-100" &&
                        HasPendingPreference(service) && wire.PreferenceWrites == queuedWrites,
                        "A refreshed download did not retain its queued manual choice without an early preference write.");
                    var decision = new PlaybackDecisionResponseV3
                    {
                        Outcome = "playable",
                        PlaybackPlan = new()
                        {
                            Delivery = "original_http", EffectiveMediaFileId = 42,
                            Subtitle = new() { Inventory = [new() { CombinedIndex = 40, TrackId = "granted-download",
                                SyncKey = "stored-100", Source = "downloaded", Language = "en", Codec = "srt",
                                Url = subtitles.Origin + "/queued-download.srt" }] }
                        }
                    };
                    var adopt = typeof(PlaybackManager).GetMethod("AdoptProtocolV3Decision", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var granted = (PlaybackStartResponse)adopt.Invoke(null, [decision, "reload", "original", null])!;
                    var grantedTrack = granted.SubtitleUrls.Single();
                    Require(grantedTrack.Id == null && grantedTrack.Index != refreshed.Index,
                        "The queued download control did not cross the v3 Id/ordinal boundary.");
                    typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, granted);
                    var replacementLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void ReplacementLoaded() => replacementLoaded.TrySetResult();
                    mpv.FileLoaded += ReplacementLoaded;
                    try { mpv.LoadFile(Path.Combine(assets, "tracks.mkv")); await replacementLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                    finally { mpv.FileLoaded -= ReplacementLoaded; }
                    mpv.Pause();
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, false);
                    var beforeApplication = subtitles.Requests;
                    var initializer = new CancellationTokenSource();
                    Set(service, "_loadedMediaInitCts", initializer);
                    await ((Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), initializer)!).WaitAsync(TimeSpan.FromSeconds(5));
                    var appliedSid = (int)Math.Round(mpv.GetPropertyDouble("sid"));
                    Require(service.ActiveSubtitleServerIndex == grantedTrack.Index && appliedSid > 0 && mpv.IsPaused &&
                        mpv.GetSubtitleTrackIds(externalOnly: true).SequenceEqual(new[] { appliedSid }) &&
                        subtitles.Requests == beforeApplication + 1 && wire.PreferenceWrites == queuedWrites + 1,
                        "A refreshed download crossing a same-file v3 grant was refused, applied more than once, or lost its single preference save.");
                    await ((Task)Call(service, "CompleteExecutedSubtitleSelectionAsync", Get(service, "_subtitleSelectionOperation"), grantedTrack)!).WaitAsync(TimeSpan.FromSeconds(5));
                    Require(wire.PreferenceWrites == queuedWrites + 1 && subtitles.Requests == beforeApplication + 1 &&
                        mpv.GetPropertyDouble("sid") == appliedSid,
                        "A repeated completion duplicated the downloaded subtitle application or preference save.");
                    Program.Log("PASS: refreshed downloaded identity crosses a same-file v3 grant and executes/saves exactly once.");
                }
                else if (closeout is "mime-busy" or "mime-ready")
                {
                    subtitles.BusyResponses = closeout == "mime-busy" ? 2 : 0;
                    var track = new SubtitleTrackInfo { Index = 30, TrackId = closeout, Source = "external", Language = "en", Codec = null,
                        Url = subtitles.Origin + "/captions" };
                    session.SubtitleUrls.Add(track);
                    await service.SetSubtitleTrackAndPersistAsync(track.Index + 1, track.Language, track).WaitAsync(TimeSpan.FromSeconds(5));
                    Require(subtitles.Requests == subtitles.BusyResponses + 1 && subtitles.NativeRequests == 0 &&
                        service.ActiveSubtitleServerIndex == track.Index && mpv.GetPropertyDouble("sid") > 0 && mpv.IsPaused && wire.PreferenceWrites == 1,
                        "An extensionless missing-codec sidecar bypassed MIME/503 reads, failed its real native selection, or changed pause/preferences.");
                    Program.Log($"PASS: actual service {closeout} uses bounded MIME reads with requests={subtitles.Requests}, nativeRequests={subtitles.NativeRequests}.");
                }
                else if (closeout == "cast-off")
                {
                    foreach (var json in new[] { """{"name":"select_subtitle_track","trackId":null}""", """{"name":"select_subtitle_track"}""" })
                    {
                        var selected = session.SubtitleUrls.Single(t => t.Index == 2);
                        await service.SetSubtitleTrackAndPersistAsync(3, selected.Language, selected);
                        var beforeWrites = wire.PreferenceWrites;
                        await service.ApplySiloCastControlAsync(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject());
                        Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                            manager.SubtitleSelectionForLoad == -1 && wire.PreferenceWrites == beforeWrites + 1 && mpv.IsPaused,
                            "SiloCast null/missing Off failed to execute/persist exactly once while retaining genuine pause.");
                    }
                }
                else if (closeout == "cast-routes")
                {
                    var noRoute = new SubtitleTrackInfo { Index = 9, Source = "embedded", StreamIndex = 99, ContainerTrackId = "99", Language = "en", Codec = "srt" };
                    session.SubtitleUrls.Add(noRoute);
                    var advertised = service.CaptureSiloCastState()["subtitleTracks"]!.AsArray();
                    Require(advertised.All(t => t!["trackId"]!.GetValue<int>() != 10) &&
                        advertised.Any(t => t!["trackId"]!.GetValue<int>() == 3), "SiloCast advertised a route-less choice or hid a valid native choice.");
                    await ExpectUnavailableAsync(() => service.ApplySiloCastControlAsync(new() { ["name"] = "select_subtitle_track", ["trackId"] = 10 }));
                }
                else if (closeout == "reload-identity")
                {
                    var requested = new SubtitleTrackInfo { Index = 20, TrackId = "wanted", Source = "external", Language = "en" };
                    var reused = new SubtitleTrackInfo { Index = 20, TrackId = "other", Source = "external", Language = "ja" };
                    session.SubtitleUrls.Add(reused);
                    Require(Call(service, "ResolveReloadSubtitleChoice", requested, (int?)42) is null, "A removed stable identity rebound to another track at the same ordinal.");
                    var moved = new SubtitleTrackInfo { Index = 21, TrackId = "wanted", Source = "external", Language = "en" };
                    session.SubtitleUrls.Add(moved);
                    Require(ReferenceEquals(Call(service, "ResolveReloadSubtitleChoice", requested, (int?)42), moved), "A uniquely retained identity lost its reindexed route.");
                    var unavailableDuplicate = new SubtitleTrackInfo { Index = 22, TrackId = "wanted", Unplayable = true };
                    session.SubtitleUrls.Add(unavailableDuplicate);
                    Require(Call(service, "ResolveReloadSubtitleChoice", requested, (int?)42) is null, "An unavailable duplicate was discarded before strong identity uniqueness.");
                    session.SubtitleUrls.Remove(unavailableDuplicate);
                    var legacy = new SubtitleTrackInfo { Index = 20, Source = "external", Language = "ja" };
                    Require(ReferenceEquals(Call(service, "ResolveReloadSubtitleChoice", legacy, (int?)42), reused), "Same-file legacy ordinal compatibility was removed.");
                    var stored = new SubtitleTrackInfo { Id = 123, Index = 20, SyncKey = "stored-123", Source = "downloaded" };
                    var adopted = new SubtitleTrackInfo { Index = 23, SyncKey = "stored-123", Source = "downloaded" };
                    session.SubtitleUrls.Add(adopted);
                    Require(ReferenceEquals(Call(service, "ResolveReloadSubtitleChoice", stored, (int?)42), adopted),
                        "A v3 stored row without Id failed its unique same-file SyncKey match.");
                    adopted.Unplayable = true;
                    Require(Call(service, "ResolveReloadSubtitleChoice", stored, (int?)42) is null, "An unavailable stored identity was selected.");
                    adopted.Unplayable = false;
                    adopted.Id = 456;
                    Require(Call(service, "ResolveReloadSubtitleChoice", stored, (int?)42) is null, "A SyncKey match overrode a conflicting stored Id.");
                    adopted.Id = null;
                    var duplicate = new SubtitleTrackInfo { Index = 24, SyncKey = "stored-123", Unplayable = true };
                    session.SubtitleUrls.Add(duplicate);
                    Require(Call(service, "ResolveReloadSubtitleChoice", stored, (int?)42) is null,
                        "An unavailable duplicate was discarded to manufacture a unique stored identity.");
                    session.SubtitleUrls.Remove(duplicate);
                    session.SubtitleUrls.Remove(adopted);
                    Require(Call(service, "ResolveReloadSubtitleChoice", stored, (int?)42) is null,
                        "A missing stored identity borrowed the reused ordinal.");
                }
                else if (closeout == "preplay-legacy")
                {
                    using var initializer = new CancellationTokenSource();
                    using var retiredInitializer = new CancellationTokenSource();
                    var generation = (long)Get(service, "_loadedMediaInitGeneration")!;
                    Set(service, "_loadedMediaInitCts", initializer);
                    try
                    {
                        object? ApplyLegacy(long applyGeneration, CancellationTokenSource applyOwner)
                        {
                            var method = typeof(PlayerService).GetMethod("TryApplyLegacyInitialSubtitleChoice", BindingFlags.NonPublic | BindingFlags.Instance)!;
                            // The preserved pre-fix app has no private owner args;
                            // run the identical behavioral assertion against it.
                            return method.Invoke(service, method.GetParameters().Length == 0 ? [] : [applyGeneration, applyOwner]);
                        }
                        var legacySession = new PlaybackStartResponse { SessionId = "legacy", ProtocolVersion = 2, MediaFileId = 42, PlayMethod = "direct" };
                        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, legacySession);
                        Call(service, "ResolveInitialSubtitleSelection", new WatchDetailResponse { ContentId = "movie" }, new FileVersion { FileId = 42 }, legacySession, null, (int?)1, null);
                        Require(Get(service, "_pendingInitialServerSubtitleIndex") is null && Get(service, "_pendingSubtitleSelection") is int pending && pending == 1,
                            "Legacy pre-play fixture did not establish its explicit unresolved ordinal.");
                        var boundary = Environment.GetEnvironmentVariable("SILO_NATIVE_LEGACY_BOUNDARY") ?? "";
                        Require(boundary is "" or "publisher" or "initializer", "Unknown legacy boundary scope.");
                        if (boundary != "initializer") await VerifyLegacyPublisherGateAsync(service, legacySession);
                        Call(service, "ApplyPendingSubtitleSelection");
                        var pendingLegacy = Get(service, "_legacySubtitleChoice");
                        foreach (var stale in boundary == "publisher" ? Array.Empty<(long, CancellationTokenSource)>() :
                            new[] { (generation - 1, retiredInitializer), (generation, retiredInitializer), (generation - 1, initializer) })
                        {
                            ApplyLegacy(stale.Item1, stale.Item2);
                            Require(ReferenceEquals(pendingLegacy, Get(service, "_legacySubtitleChoice")) && Get(service, "_pendingSubtitleSelection") is int retained && retained == 1 &&
                                mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0,
                                "A retired loaded-media initializer consumed or applied the successor's legacy subtitle choice.");
                        }
                        // Pre-load holds Off; the real loaded-file executor resolves
                        // native IDs, without guessing that an ID equals ordinal+1.
                        ApplyLegacy(generation, initializer);
                        Require(mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused && wire.PreferenceWrites == 0,
                            "Explicit legacy pre-play ordinal was forced Off or changed pause/preferences.");
                        foreach (var retire in new[] { "profile", "inventory", "manual", "access" })
                        {
                            legacySession.SubtitleUrls.Clear();
                            Call(service, "ResolveInitialSubtitleSelection", new WatchDetailResponse { ContentId = "movie" }, new FileVersion { FileId = 42 }, legacySession, null, (int?)1, null);
                            mpv.SetSubtitleTrack(0);
                            if (retire == "profile") api.SetProfile("replacement");
                            else if (retire == "inventory") legacySession.SubtitleUrls.Add(new() { Index = 20, TrackId = "identified", Source = "embedded" });
                            else if (retire == "manual") await service.SetSubtitleTrackAndPersistAsync(0, null, null);
                            else { var captured = api.CaptureContext(); api.TryInvalidateAccessContext(captured, out _); }
                            var beforeApplyWrites = wire.PreferenceWrites;
                            ApplyLegacy(generation, initializer);
                            Require(mpv.GetPropertyDouble("sid") == (retire == "access" ? 2 : 0) && mpv.IsPaused && wire.PreferenceWrites == beforeApplyWrites,
                                "Legacy pre-play execution did not retain identity-only access compatibility or reject retired/populated/newer-manual ownership.");
                        }
                    }
                    finally { Set(service, "_loadedMediaInitCts", null); }
                }
                else if (closeout == "async-service")
                {
                    foreach (var retirement in new[] { "positive", "profile", "aba" })
                    {
                        api.SetProfile("fixture");
                        await service.SetSubtitleTrackAndPersistAsync(0, null, null);
                        var beforeFallbackWrites = wire.PreferenceWrites;
                        var beforeReads = subtitles.Requests; var beforeNativeReads = subtitles.NativeRequests;
                        var track = new SubtitleTrackInfo { Index = 30 + session.SubtitleUrls.Count, TrackId = retirement,
                            Source = "external", Codec = "unknown-probe", Language = "en", Url = subtitles.Origin + "/" + retirement + ".probe" };
                        session.SubtitleUrls.Add(track);
                        var held = subtitles.HoldNext();
                        var pending = service.SetSubtitleTrackAndPersistAsync(track.Index + 1, track.Language, track);
                        try
                        {
                            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                            if (retirement != "positive") api.SetProfile("retired");
                            if (retirement == "aba") api.SetProfile("fixture");
                            held.Release.TrySetResult();
                            await pending.WaitAsync(TimeSpan.FromSeconds(5));
                            var cached = ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).TryGetValue(track.Index, out var sid);
                            if (retirement == "positive")
                                Require(cached && sid > 2 && mpv.GetPropertyDouble("sid") == sid && service.ActiveSubtitleServerIndex == track.Index &&
                                    wire.PreferenceWrites == beforeFallbackWrites + 1 && mpv.IsPaused &&
                                    subtitles.Requests == beforeReads + 2 && subtitles.NativeRequests == beforeNativeReads + 1,
                                    "Supported asynchronous format fallback did not execute/persist its actual native SID under current service ownership.");
                            else
                                Require(!cached && mpv.GetPropertyDouble("sid") <= 0 && service.ActiveSubtitleServerIndex == -1 &&
                                    wire.PreferenceWrites == beforeFallbackWrites && mpv.IsPaused &&
                                    subtitles.Requests == beforeReads + 1 && subtitles.NativeRequests == beforeNativeReads,
                                    "Retired profile/ABA URL fallback enabled captions, cached its SID or persisted a stale choice.");
                            Program.Log($"PASS: actual service unknown-format async fallback {retirement} ownership/pause/preference.");
                        }
                        finally { held.Release.TrySetResult(); }
                    }
                }
                else if (closeout == "retired-read-cache")
                {
                    var prior = session.SubtitleUrls.Single(t => t.Index == 2);
                    await service.SetSubtitleTrackAndPersistAsync(3, prior.Language, prior);
                    var retainedWrites = wire.PreferenceWrites;
                    var original = new SubtitleTrackInfo { Index = 20, TrackId = "held-original", Source = "external", Codec = "srt", Language = "en", Url = subtitles.Origin + "/held-original.srt" };
                    session.SubtitleUrls.Add(original);
                    var held = subtitles.HoldNext();
                    var pending = service.SetSubtitleTrackAndPersistAsync(21, original.Language, original);
                    var replacement = new SubtitleTrackInfo { Index = 20, TrackId = "new-at-ordinal", Source = "external", Codec = "srt", Language = "ja", Url = subtitles.Origin + "/new-at-ordinal.srt" };
                    try
                    {
                        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        session.SubtitleUrls.Remove(original); session.SubtitleUrls.Add(replacement);
                        held.Release.TrySetResult();
                        await pending.WaitAsync(TimeSpan.FromSeconds(5));
                        Require(!((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).ContainsKey(20) &&
                            service.ActiveSubtitleServerIndex == 2 && mpv.GetPropertyDouble("sid") == 1 && wire.PreferenceWrites == retainedWrites,
                            "A retired inventory row left its bytes under another track's reused ordinal or changed the executed preference.");
                        var replacementReads = subtitles.Requests;
                        await service.SetSubtitleTrackAndPersistAsync(21, replacement.Language, replacement);
                        Require(subtitles.Requests == replacementReads + 1 && service.ActiveSubtitleServerIndex == 20 && wire.PreferenceWrites == retainedWrites + 1 && mpv.IsPaused,
                            "The new inventory choice reused old bytes or failed to execute/persist under its own read.");
                    }
                    finally { held.Release.TrySetResult(); }
                }
                else if (closeout == "async-core-probe")
                {
                    // The HTTP phase is structurally held. Query the actual
                    // native core from a dedicated owned thread, never the UI.
                    Require(mpv.GetPropertyDouble("duration") > 0, "Core probe requires loaded media before the held command.");
                    var held = subtitles.HoldNext();
                    var pending = mpv.AddSubtitleUnselectedAsync(subtitles.Origin + "/held-core.srt", "Core responsiveness probe", "en", CancellationToken.None);
                    Task<double>? query = null;
                    try
                    {
                        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        query = Task.Factory.StartNew(() =>
                        {
                            entered.TrySetResult();
                            return mpv.GetPropertyDouble("time-pos");
                        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        var responsive = await Task.WhenAny(query, Task.Delay(TimeSpan.FromSeconds(1))) == query;
                        Require(!pending.IsCompleted, "The core probe lost its held native HTTP boundary.");
                        Program.Log($"PROBE: actual mpv time-pos query completed while async sub-add HTTP was held: {responsive}.");
                    }
                    finally { held.Release.TrySetResult(); }
                    await pending.WaitAsync(TimeSpan.FromSeconds(5));
                    if (query != null) await query.WaitAsync(TimeSpan.FromSeconds(5));
                }
                else if (closeout == "async-lifecycle")
                {
                    var nativeErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
                    void ObserveNativeError(string message) => nativeErrors.Enqueue(message);
                    mpv.Error += ObserveNativeError;
                    try
                    {
                        mpv.SetSubtitleTrack(2);
                        var missing = Path.Combine(Program.ResultDirectory, "missing-native-subtitle.srt");
                        Require(!File.Exists(missing), "Native error diagnostic requires an absent isolated file.");
                        var rejected = await mpv.AddSubtitleUnselectedAsync(missing, "Missing owned fixture", null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
                        Require(rejected == 0 && nativeErrors.Any(message => message.StartsWith("mpv sub-add reply failed:", StringComparison.Ordinal)) &&
                            mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused,
                            "Failed async native command lost its safe diagnostic or changed the previous SID/pause.");
                        Program.Log("PASS: actual negative async native reply reports a safe error and retains applied SID/pause.");
                    }
                    finally { mpv.Error -= ObserveNativeError; }
                    foreach (var scenario in new[] { "newer-off", "newer-track", "cancel", "file", "dispose" })
                    {
                        mpv.SetSubtitleTrack(1);
                        var beforeIds = mpv.GetSubtitleTrackIds(externalOnly: true).ToHashSet();
                        var held = subtitles.HoldNext();
                        using var cancel = new CancellationTokenSource();
                        var pending = mpv.AddSubtitleUnselectedAsync(subtitles.Origin + "/" + scenario + ".srt", scenario, "en", cancel.Token);
                        try
                        {
                            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                            if (scenario == "newer-off") mpv.SetSubtitleTrack(0);
                            else if (scenario == "newer-track") mpv.SetSubtitleTrack(2);
                            else if (scenario == "cancel") cancel.Cancel();
                            else if (scenario == "file")
                            {
                                var replacement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                                void Loaded() => replacement.TrySetResult();
                                mpv.FileLoaded += Loaded;
                                try { mpv.LoadFile(Path.Combine(assets, "tracks.mkv")); await replacement.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                                finally { mpv.FileLoaded -= Loaded; }
                                mpv.Pause(); mpv.SetSubtitleTrack(2);
                            }
                            else { Set(service, "_mpv", null); mpv.Dispose(); }
                            held.Release.TrySetResult();
                            var result = 0;
                            try { result = await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
                            catch (OperationCanceledException) when (scenario == "cancel") { }
                            if (scenario == "dispose") Require(result == 0, "Disposed async native add retained a SID.");
                            else if (scenario == "file") Require(result == 0 && mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused,
                                "Retired native add changed the replacement file's SID/pause.");
                            else if (scenario == "cancel") Require(mpv.GetSubtitleTrackIds(externalOnly: true).ToHashSet().SetEquals(beforeIds) && mpv.GetPropertyDouble("sid") == 1 && mpv.IsPaused,
                                "Canceled native add retained an owned SID or changed the previous selection/pause.");
                            else Require(result > 0 && mpv.GetPropertyDouble("sid") == (scenario == "newer-off" ? 0 : 2) && mpv.IsPaused,
                                "Completed async native add replayed an older SID over a newer explicit choice.");
                            Program.Log($"PASS: actual held asynchronous native add {scenario} ownership.");
                        }
                        finally { held.Release.TrySetResult(); }
                    }
                }
                else
                {
                    mpv.SetSubtitleTrack(0);
                    var sid = await mpv.AddSubtitleUnselectedAsync(Path.Combine(assets, "english.srt"), "Async exact", "en", CancellationToken.None);
                    Require(sid > 0 && mpv.GetSubtitleTrackIds(externalOnly: true).Contains(sid) && mpv.GetPropertyDouble("sid") <= 0 && mpv.IsPaused,
                        "Async native add failed its supported flag or changed Off/pause.");
                }
                Program.Log($"PASS: closeout {closeout} actual native/service behavior.");
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
                    Require(wire.PreferenceWrites == preferenceWrites && HasPendingPreference(service),
                        "A choice made during loading persisted before native execution.");
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, false);
                    Call(service, "LoadSubtitles");
                    mpv.SetSubtitleTrack(0); // Replacement loads suppress unverified subtitles.
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 9, false)!;
                    Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid &&
                        wire.PreferenceWrites == preferenceWrites && manager.SubtitleSelectionForLoad == previousIndex &&
                        Get(service, "_pendingInitialServerSubtitleIndex") == null && !HasPendingPreference(service),
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
                        manager.SubtitleSelectionForLoad == -1 && wire.PreferenceWrites == preferenceWrites,
                        "A native-only choice remained highlighted after replacement with a no-route remux.");
                    previousTrack.Url = previousUrl;
                    Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
                    await service.SetSubtitleTrackAndPersistAsync(3, previousTrack.Language, previousTrack);

                    var busy = new SubtitleTrackInfo { Index = 10, Source = "embedded", Codec = "subrip", Language = "en",
                        StreamIndex = 99, ContainerTrackId = "99", Url = subtitles.Origin + "/busy.srt" };
                    session.SubtitleUrls.Add(busy);
                    subtitles.Fail = true;
                    preferenceWrites = wire.PreferenceWrites;
                    var cache = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", busy.Index, true)!;
                    Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid &&
                        wire.PreferenceWrites == preferenceWrites && !cache.ContainsKey(busy.Index) && manager.SubtitleSelectionForLoad == previousIndex,
                        "Failed sidecar add cached or persisted an unchanged native SID.");

                    var castTracks = service.CaptureSiloCastState()["subtitleTracks"]!.AsArray();
                    Require(castTracks.All(t => t!["trackId"]!.GetValue<int>() != 4), "SiloCast advertised an explicitly unplayable subtitle.");
                    foreach (var sid in new[] { 4, 10, 11 })
                        await ExpectUnavailableAsync(() => service.ApplySiloCastControlAsync(new() { ["name"] = "select_subtitle_track", ["trackId"] = sid }));
                    Require(service.ActiveSubtitleServerIndex == previousIndex && wire.PreferenceWrites == preferenceWrites,
                        "A refused SiloCast command changed the selected subtitle or preference.");
                    foreach (var malformed in new[]
                    {
                        """{"name":"select_subtitle_track","trackId":"3"}""",
                        """{"name":"select_subtitle_track","trackId":3.5}""",
                        """{"name":"select_subtitle_track","trackId":true}""",
                        """{"name":"select_subtitle_track","trackId":{}}""",
                        """{"name":"select_subtitle_track","trackId":[]}""",
                        """{"name":"select_subtitle_track","trackId":0}""",
                        """{"name":"select_subtitle_track","trackId":-1}""",
                        """{"name":"select_subtitle_track","trackId":2147483648}""",
                    })
                    {
                        await ExpectUnavailableAsync(() => service.ApplySiloCastControlAsync(
                            System.Text.Json.Nodes.JsonNode.Parse(malformed)!.AsObject()));
                        Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid &&
                            wire.PreferenceWrites == preferenceWrites && manager.SubtitleSelectionForLoad == previousIndex,
                            "A malformed SiloCast subtitle command disabled captions or changed their saved preference.");
                    }
                    foreach (var off in new[]
                    {
                        """{"name":"select_subtitle_track"}""",
                        """{"name":"select_subtitle_track","trackId":null}""",
                    })
                    {
                        await service.SetSubtitleTrackAndPersistAsync(3, previousTrack.Language, previousTrack);
                        Require(mpv.GetPropertyDouble("sid") == previousSid && service.ActiveSubtitleServerIndex == previousIndex,
                            "The SiloCast Off fixture did not establish enabled captions.");
                        preferenceWrites = wire.PreferenceWrites;
                        await service.ApplySiloCastControlAsync(System.Text.Json.Nodes.JsonNode.Parse(off)!.AsObject());
                        Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                            manager.SubtitleSelectionForLoad == -1 && service.CaptureSiloCastState()["selectedSubtitleTrackId"] == null &&
                            wire.PreferenceWrites == preferenceWrites + 1 && wire.LastSubtitleMode == "off" && mpv.IsPaused,
                            "SiloCast Off did not clear the native SID and persist mode off exactly once.");
                        await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, false)!;
                        Require(wire.PreferenceWrites == preferenceWrites + 1,
                            "Reinitializing SiloCast Off duplicated its preference write.");
                    }
                    preferenceWrites = wire.PreferenceWrites;
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", busy.Index, true)!;
                    Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                        wire.PreferenceWrites == preferenceWrites && !cache.ContainsKey(busy.Index),
                        "Failed sidecar add while Off guessed SID 1 or persisted the failed choice.");
                    Program.Log("PASS: no-route and failed-sidecar selections retain executed SID/intent/preferences, deferred choices persist only after execution, and SiloCast refuses unavailable choices.");
                }
                finally { mpv.ScriptMessageReceived -= Notice; }
                return;
            }
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inventory = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(string[] message)
            {
                if (message.FirstOrDefault() == "silo-identity-ready") ready.TrySetResult();
                if (message.FirstOrDefault() == "silo-identity-inventory" && message.Length > 1) inventory.TrySetResult(message[1]);
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
                    mp.commandv('script-message', 'silo-identity-ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", probe });
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Call(service, "SendSubtitleListToOsc");
                using var payload = JsonDocument.Parse(await inventory.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                Require(payload.RootElement.GetArrayLength() == 6 && payload.RootElement.EnumerateArray().Single(t => t.GetProperty("index").GetInt32() == 3).GetProperty("unplayable").GetBoolean(),
                    "Production OSC payload lost unavailable row or its combined ordinal.");
            }
            finally { mpv.ScriptMessageReceived -= Observe; }
            var english = session.SubtitleUrls.Single(t => t.Index == 2);
            var reads = Volatile.Read(ref subtitles.Requests);
            await service.SetSubtitleTrackAndPersistAsync(3, english.Language, english);
            Require(mpv.GetPropertyDouble("sid") == 1, "Interleaved index-only inventory selected the wrong native SID instead of source container track 2.");
            Require(Volatile.Read(ref subtitles.Requests) == reads, "Native English selection unexpectedly read a remote sidecar.");
            Require(mpv.IsPaused, "Native subtitle selection resumed viewer-paused playback.");
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
            var unlabelled = new SubtitleTrackInfo { Index = 9, Source = "downloaded", Codec = "srt", Label = "", Language = "", Url = subtitles.Origin + "/captions.srt" };
            session.SubtitleUrls.Add(unlabelled);
            await service.SetSubtitleTrackAndPersistAsync(10, unlabelled.Language, unlabelled);
            Require(map.TryGetValue(9, out var unlabelledSid) && unlabelledSid > downloadedSid && mpv.GetPropertyDouble("sid") == unlabelledSid &&
                service.ActiveSubtitleServerIndex == 9 && manager.SubtitleSelectionForLoad == 9,
                "A sidecar with both blank label and blank language was not selected and retained.");
            var japanese = session.SubtitleUrls.Single(t => t.Index == 4);
            reads = Volatile.Read(ref subtitles.Requests);
            await service.SetSubtitleTrackAndPersistAsync(5, "ja", japanese);
            Require(mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused, "Later native identity was shifted by sidecar/index-only inventory.");
            Require(Volatile.Read(ref subtitles.Requests) == reads, "Native Japanese selection unexpectedly read a remote sidecar.");
            var containerOnly = new SubtitleTrackInfo { Index = 8, TrackId = "sub:42:8", Source = "embedded", Codec = "subrip", Language = "en", ContainerTrackId = "2" };
            session.SubtitleUrls.Add(containerOnly);
            writes = wire.PreferenceWrites;
            await service.SetSubtitleTrackAndPersistAsync(9, containerOnly.Language, containerOnly);
            Require(mpv.GetPropertyDouble("sid") == 1 && service.ActiveSubtitleServerIndex == 8 &&
                manager.SubtitleSelectionForLoad == 8 && wire.PreferenceWrites == writes + 1 && mpv.IsPaused,
                "A container-only row without a sidecar failed to select its loaded container identity or unique metadata match.");
            await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            Require(mpv.GetPropertyDouble("sid") <= 0 && mpv.IsPaused, "Explicit Off changed viewer pause intent.");
            var untitled = Path.Combine(Program.ResultDirectory, "untitled-select.srt");
            var untitledAuto = Path.Combine(Program.ResultDirectory, "untitled-auto.srt");
            File.Copy(Path.Combine(assets, "english.srt"), untitled, overwrite: true);
            File.Copy(Path.Combine(assets, "english.srt"), untitledAuto, overwrite: true);
            var maximumSid = mpv.GetSubtitleTrackIds().Max();
            Require(mpv.AddSubtitle(untitled, "", "", select: true) && mpv.GetPropertyDouble("sid") > maximumSid,
                "sub-add with an empty title did not honor select:true.");
            var untitledSid = mpv.GetPropertyDouble("sid");
            Require(mpv.AddSubtitle(untitledAuto, null, "en", select: false) && mpv.GetPropertyDouble("sid") == untitledSid,
                "sub-add without a title did not honor auto or lost the language argument slot.");
            mpv.SetSubtitleTrack(0);
            var unselectedPath = Path.Combine(Program.ResultDirectory, "unselected-async.srt");
            File.Copy(Path.Combine(assets, "english.srt"), unselectedPath, overwrite: true);
            var unselectedSid = await mpv.AddSubtitleUnselectedAsync(unselectedPath, "Async unselected", "en", CancellationToken.None);
            Require(unselectedSid > 0 && mpv.GetSubtitleTrackIds(externalOnly: true).Contains(unselectedSid) && mpv.GetPropertyDouble("sid") <= 0,
                "An asynchronous unselected sub-add enabled captions while Off.");
            Program.Log("PASS: subtitle identity uses actual loaded Matroska tracks across index-only gap and sidecar addition; unavailable forged service/native callbacks preserve selection/preferences; Off and genuine pause survive.");
            await CheckAppliedSidecarReloadAsync(service, manager, mpv, subtitles, wire, assets);
            await CheckRetiredAddBeforeFileLoadAsync(mpv, subtitles, assets);
            await CheckCastSubtitleLaneAsync(service, manager, mpv, subtitles, wire, parent.DispatcherQueue);
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static void CheckInitialPlayableSelection(PlayerService service, PlaybackManager manager)
    {
        var previous = manager.CurrentSession;
        var version = new FileVersion { FileId = 42, SubtitleTracks = [
            new() { Language = "eng", Codec = "srt", External = true },
            new() { Language = "spa", Codec = "srt", External = true },
            new() { Language = "eng", Codec = "srt", External = true },
        ] };
        var session = new PlaybackStartResponse { ProtocolVersion = 3, MediaFileId = 42, SubtitleUrls = [
            new() { Index = 0, TrackId = "unavailable", Source = "external", Language = "eng", Codec = "srt", Unplayable = true },
            new() { Index = 1, TrackId = "spanish", Source = "external", Language = "spa", Codec = "srt" },
            new() { Index = 2, TrackId = "english", Source = "external", Language = "eng", Codec = "srt" },
        ] };
        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
        try
        {
            foreach (var source in new[] { "external", "downloaded" })
            {
                Call(service, "ResolveInitialSubtitleSelection", new WatchDetailResponse(), version, session, null, null,
                    new SubtitleTrackSignature { Source = source, Language = "eng", Codec = "srt" });
                Require(Get(service, "_pendingInitialServerSubtitleIndex") is 2 && manager.SubtitleSelectionForLoad == 2,
                    "Saved signature selected an unavailable duplicate instead of its playable match.");
            }
            foreach (var (ordinal, expected) in new[] { (0, 2), (1, 1), (2, 2) })
            {
                Call(service, "ResolveInitialSubtitleSelection", new WatchDetailResponse(), version, session, null, ordinal, null);
                Require(Get(service, "_pendingInitialServerSubtitleIndex") is int index && index == expected && manager.SubtitleSelectionForLoad == expected,
                    "Explicit pre-play selection chose an unavailable row or shifted an ordinal onto a different language.");
            }
            Program.Log("PASS: initial saved/explicit subtitle matching skips unavailable duplicates without shifting source ordinals.");
        }
        finally
        {
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, previous);
            Call(manager, "ResetLocalTrackIntent");
            Set(service, "_pendingInitialServerSubtitleIndex", null); Set(service, "_pendingSubtitleSelection", null);
        }
    }

    private static async Task ExpectUnavailableAsync(Func<Task> action)
    {
        try { await action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("SiloCast reported success for an unavailable subtitle.");
    }

    private static async Task VerifyLegacyPublisherGateAsync(PlayerService service, PlaybackStartResponse session)
    {
        var gate = Get(service, "_subtitleTrackStateLock")!;
        var before = Get(service, "_legacySubtitleChoice");
        Require(before != null, "Legacy publication probe requires a pending choice.");
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (gate)
            {
                held.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned legacy publication gate was not released.");
            }
        });
        Exception? failure = null;
        var publisherStarted = false;
        var publisher = new Thread(() =>
        {
            try
            {
                started.TrySetResult();
                Call(service, "ResolveInitialSubtitleSelection", new WatchDetailResponse { ContentId = "movie" },
                    new FileVersion { FileId = 42 }, session, null, (int?)0, null);
            }
            catch (Exception ex) { failure = ex; }
            finally { exited.TrySetResult(); }
        }) { IsBackground = true, Name = "Owned legacy subtitle publisher" };
        try
        {
            await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
            publisher.Start();
            publisherStarted = true;
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (!exited.Task.IsCompleted && (publisher.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Legacy publisher reached no structural wait/exit boundary.");
                await Task.Delay(1);
            }
            Require(!exited.Task.IsCompleted && ReferenceEquals(before, Get(service, "_legacySubtitleChoice")) &&
                Get(service, "_pendingSubtitleSelection") is int ordinal && ordinal == 1,
                "A legacy choice publisher mutated pending state outside the shared selection gate.");
        }
        finally
        {
            release.Set();
            await holder.WaitAsync(TimeSpan.FromSeconds(5));
            if (publisherStarted)
                await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        if (failure != null) throw new InvalidOperationException("Owned legacy publisher failed.", failure);
        Require(!ReferenceEquals(before, Get(service, "_legacySubtitleChoice")) && Get(service, "_pendingSubtitleSelection") is int next && next == 0,
            "The released legacy publisher did not publish its new choice.");
        Call(service, "ResolveInitialSubtitleSelection", new WatchDetailResponse { ContentId = "movie" },
            new FileVersion { FileId = 42 }, session, null, (int?)1, null);
        Program.Log("PASS: actual legacy choice publisher waits for the shared consumption gate before changing pending state.");
    }

    private static bool HasPendingPreference(PlayerService service)
    {
        var owner = Get(service, "_subtitleSelectionOperation");
        return owner != null && (bool)owner.GetType().GetProperty("Accepted")!.GetValue(owner)! &&
            (int)owner.GetType().GetField("PreferenceClaimed")!.GetValue(owner)! == 0;
    }

    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class SubtitleServer : IDisposable
    {
        public sealed class Gate
        {
            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private Gate? next;
        private Gate? nextNative;
        public Gate HoldNextNative()
        {
            var gate = new Gate();
            if (Interlocked.CompareExchange(ref nextNative, gate, null) is not null) throw new InvalidOperationException("A native subtitle response is already held.");
            return gate;
        }
        public Gate HoldNext()
        {
            var gate = new Gate();
            if (Interlocked.CompareExchange(ref next, gate, null) is not null) throw new InvalidOperationException("A subtitle response is already held.");
            return gate;
        }
        private readonly HttpListener _listener = new();
        public string Origin { get; }
        public volatile bool Fail;
        public int Requests, NativeRequests, BusyResponses;
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
                    var count = Interlocked.Increment(ref Requests);
                    var native = request.Request.UserAgent?.Contains("mpv", StringComparison.OrdinalIgnoreCase) == true ||
                        request.Request.UserAgent?.Contains("Lavf", StringComparison.OrdinalIgnoreCase) == true;
                    if (native)
                        Interlocked.Increment(ref NativeRequests);
                    var held = native ? Interlocked.Exchange(ref nextNative, null) : null;
                    held ??= Interlocked.Exchange(ref next, null);
                    if (held != null)
                    {
                        held.Started.TrySetResult();
                        await held.Release.Task;
                    }
                    if (Fail || count <= BusyResponses)
                    {
                        request.Response.StatusCode = 503;
                        request.Response.Headers["Retry-After"] = "0";
                        request.Response.Close();
                        continue;
                    }
                    try
                    {
                        request.Response.ContentType = request.Request.Url!.AbsolutePath.EndsWith(".probe", StringComparison.Ordinal)
                            ? "application/octet-stream" : "application/x-subrip";
                        request.Response.ContentLength64 = bytes.Length;
                        await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                    }
                    catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
                    {
                        // A canceled owned mpv request can close its socket
                        // before the held synthetic response is released.
                    }
                }
            });
        }
        public void Dispose() => _listener.Close();
    }
    private sealed class Wire : HttpMessageHandler
    {
        public int PreferenceWrites, Replans;
        public string? LastSubtitleMode;
        public string? SubtitleList;
        public System.Collections.Concurrent.ConcurrentQueue<string?> SavedLabels = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "subtitle-identity.invalid") throw new InvalidOperationException("Unexpected fixture networking.");
            if (request.RequestUri.AbsolutePath == "/api/v2/subtitles/42" && SubtitleList is { } list)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(list) };
            if (request.RequestUri.AbsolutePath.Contains("-prefs/"))
            {
                PreferenceWrites++;
                if (request.Content != null)
                {
                    using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                    LastSubtitleMode = body.RootElement.GetProperty("subtitle_mode").GetString();
                    if (body.RootElement.TryGetProperty("track_signature", out var signature) && signature.ValueKind == JsonValueKind.Object)
                        SavedLabels.Enqueue(signature.GetProperty("label").GetString());
                }
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/replan")) Replans++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
