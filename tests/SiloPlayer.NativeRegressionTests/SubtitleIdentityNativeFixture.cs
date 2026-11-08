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
    private static async Task RunAdditionalIdentityCasesAsync(StackPanel parent)
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
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE") == "ambiguous-rebind")
            {
                foreach (var stableId in new[] { false, true })
                {
                    var requested = new SubtitleTrackInfo { Index = 1, TrackId = stableId ? "identity-88" : null,
                        Id = stableId ? null : 88, Source = "downloaded", Codec = "srt", Language = "spa" };
                    var available = new SubtitleTrackInfo { Index = 7, TrackId = requested.TrackId, Id = requested.Id,
                        Source = requested.Source, Codec = requested.Codec, Language = requested.Language };
                    var unavailableDuplicate = new SubtitleTrackInfo { Index = 8, TrackId = requested.TrackId, Id = requested.Id,
                        Source = requested.Source, Codec = requested.Codec, Language = requested.Language, Unplayable = true };
                    session.SubtitleUrls = [available];
                    Require(ReferenceEquals(Call(service, "ResolveCurrentSubtitleChoice", requested, 42), available),
                        "A unique available strong identity did not rebind to its current row.");
                    session.SubtitleUrls = [unavailableDuplicate];
                    Require(Call(service, "ResolveCurrentSubtitleChoice", requested, 42) == null,
                        "An unavailable strong identity remained executable.");
                    session.SubtitleUrls = [available, unavailableDuplicate];
                    Require(Call(service, "ResolveCurrentSubtitleChoice", requested, 42) == null,
                        "An unavailable duplicate made a strong subtitle identity falsely unique during actual service rebinding.");
                }
                Program.Log("PASS: actual service rebinding requires unique available TrackId/stored ID across the full inventory, including unavailable attestations.");
                return;
            }
            var refreshCase = Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE");
            if (refreshCase is "refresh-reindex" or "refresh-held" or "refresh-removed")
            {
                await CheckDownloadedRefreshAsync(refreshCase, service, manager, session, mpv, api, wire, subtitles);
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE") == "legacy-preplay")
            {
                session.SubtitleUrls = [];
                var watch = new WatchDetailResponse();
                var version = new FileVersion { FileId = 42 };
                foreach (var protocol in new[] { 2, 3 })
                {
                    session.ProtocolVersion = protocol;
                    Call(service, "ResolveInitialSubtitleSelection", watch, version, session, null, 1, null);
                    if (protocol == 3)
                        Require(api.TryInvalidateAccessContext(api.CaptureContext(), out _),
                            "Legacy fixture did not advance its read cache epoch without changing account/profile identity.");
                    await Initialize();
                    Require(mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused && wire.PreferenceWrites == 0,
                        "Legacy explicit pre-play ordinal was discarded when both response and watch metadata inventories were absent.");
                }
                Call(service, "ResolveInitialSubtitleSelection", watch, version, session, null, 99, null);
                await Initialize();
                Require(mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0 && mpv.IsPaused,
                    "Legacy missing ordinal selected another track or persisted an invented server index.");
                Call(service, "ResolveInitialSubtitleSelection", watch, version, session, null, 1, null);
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, false)!;
                await Initialize();
                Require(mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0,
                    "Legacy pre-play fallback overwrote the later explicit Off choice.");
                Call(service, "ResolveInitialSubtitleSelection", watch, version, session, null, 1, null);
                api.SetProfile("legacy-retired");
                await Initialize();
                Require(mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0 && mpv.IsPaused,
                    "Retired profile executed its earlier legacy subtitle ordinal.");
                Program.Log("PASS: legacy pre-play ordinal executes only against the loaded file, preserving viewer pause and avoiding invented server preference indices.");
                return;

                async Task Initialize()
                {
                    Call(service, "ApplyPendingSubtitleSelection");
                    var owner = new CancellationTokenSource();
                    Set(service, "_loadedMediaInitCts", owner);
                    await (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
                }
            }
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static async Task CheckDownloadedRefreshAsync(string scenario, PlayerService service, PlaybackManager manager,
        PlaybackStartResponse session, MpvPlayer mpv, SiloApiClient api, Wire wire, SubtitleServer subtitles)
    {
        api.SetBaseUrl(subtitles.Origin);
        wire.InventoryHost = new Uri(subtitles.Origin).Host;
        var embedded = session.SubtitleUrls.Single(track => track.Index == 2);
        embedded.Index = 0; embedded.Url = "";
        var retained = new SubtitleTrackInfo { Index = 1, Id = 88, SyncKey = "stored-88", TrackId = "stored-track-88",
            MediaFileId = 42, Source = "downloaded", Language = "spa", Codec = "srt", Url = subtitles.Origin + "/retained.srt" };
        var other = new SubtitleTrackInfo { Index = 2, Id = 73, SyncKey = "stored-73", TrackId = "stored-track-73",
            MediaFileId = 42, Source = "downloaded", Language = "eng", Codec = "srt", Url = subtitles.Origin + "/other.srt" };
        session.SubtitleUrls = [embedded, retained, other];
        Call(service, "SendSubtitleListToOsc");
        Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!,
            "Stored refresh baseline did not execute the actual retained subtitle.");
        var priorSid = mpv.GetPropertyDouble("sid");
        var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
        var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
        Require(priorSid > 0 && map[1] == priorSid && files.TryGetValue(1, out _),
            "Stored refresh baseline did not acquire a real native SID and owned downloaded file.");
        var priorPath = files[1];
        var writes = wire.PreferenceWrites;
        var generation = Get(service, "_subtitleSidecarGeneration");
        wire.StoredSubtitleResponse = scenario == "refresh-removed"
            ? """{"subtitles":[{"id":73,"media_file_id":42,"language":"eng","format":"srt"}]}"""
            : """{"subtitles":[{"id":73,"media_file_id":42,"language":"eng","format":"srt"},{"id":88,"media_file_id":42,"language":"spa","format":"srt"}]}""";
        if (scenario == "refresh-held")
        {
            Call(service, "LoadSubtitles");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            subtitles.BeforeSubtitleResponse = () => { entered.TrySetResult(); return release.Task; };
            var pending = (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                await service.RefreshSubtitlesAfterAiAsync(42).WaitAsync(TimeSpan.FromSeconds(4));
                Require(wire.PreferenceWrites == writes, "Inventory refresh saved a still-unread manual subtitle.");
                release.TrySetResult();
                Require(await pending.WaitAsync(TimeSpan.FromSeconds(5)) && service.ActiveSubtitleServerIndex == 2 &&
                    session.SubtitleUrls.Single(track => track.Index == 2).Id == 88 && manager.SubtitleSelectionForLoad == 2 &&
                    mpv.GetPropertyDouble("sid") > 0 && map.TryGetValue(2, out var sid) && mpv.GetPropertyDouble("sid") == sid &&
                    wire.StoredPreferenceIndices.ToArray().SequenceEqual(new[] { 1, 2 }) && mpv.IsPaused,
                    "A retained manual download read failed to follow its stable ID through inventory reindexing or saved the outgoing index.");
            }
            finally
            {
                release.TrySetResult(); subtitles.BeforeSubtitleResponse = null;
                try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
        }
        else
        {
            await service.RefreshSubtitlesAfterAiAsync(42).WaitAsync(TimeSpan.FromSeconds(4));
            Require(wire.PreferenceWrites == writes && Equals(Get(service, "_subtitleSidecarGeneration"), generation) && mpv.IsPaused,
                "Metadata refresh saved a preference, pretended to load a new file or changed genuine pause.");
            if (scenario == "refresh-removed")
            {
                Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                    Get(service, "_executedSubtitleTrack") == null && !map.Values.Contains((int)priorSid) && !files.Values.Contains(priorPath) && !File.Exists(priorPath) &&
                    manager.CaptureLocalSubtitleIntent().Track?.Id == 88 && !manager.CaptureLocalSubtitleIntent().IsAutomatic &&
                    Get(service, "_pendingSubtitlePreferenceOwner") == null,
                    "Removed stored subtitle retained an executable SID/cache/highlight, invented Off preference or lost the viewer's retained identity.");
                Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 2, true)! && wire.PreferenceWrites == writes,
                    "A stale removed download index remained selectable or persisted.");
            }
            else
            {
                Require(service.ActiveSubtitleServerIndex == 2 && ReferenceEquals(session.SubtitleUrls.Single(track => track.Index == 2), retained) &&
                    ReferenceEquals(Get(service, "_executedSubtitleTrack"), retained) && manager.SubtitleSelectionForLoad == 2 &&
                    map.TryGetValue(2, out var sid) && sid == priorSid && !map.ContainsKey(1) &&
                    files.TryGetValue(2, out var path) && path == priorPath && !files.ContainsKey(1) && mpv.GetPropertyDouble("sid") == priorSid,
                    "Stored subtitle refresh rebound the current checkmark/native SID or cached bytes by old ordinal instead of retained ID.");
                Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)! &&
                    service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") > 0 && mpv.GetPropertyDouble("sid") != priorSid &&
                    wire.StoredPreferenceIndices.ToArray().SequenceEqual(new[] { 1, 1 }) && map[2] == priorSid && File.Exists(priorPath),
                    "Selecting the newly inserted row reused the retained row's old-index SID/bytes or saved the wrong index.");
            }
        }
        Program.Log("PASS: actual downloaded subtitle " + scenario + " preserves stable identity, owned cache/SID, manual execution and exact preference authority.");
    }

    private static async Task RunCloseoutAsync(StackPanel parent)
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
            if (closeout is "cast-off" or "cast-routes" or "reload-identity" or "async-add" or "async-lifecycle" or "async-service" or "async-core-probe" or "retired-read-cache" or "mime-busy" or "mime-ready" or "refresh-raw-labels" or "async-prompt-cancel" or "async-late-cleanup" or "fallback-deadline" or "cast-poll")
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
                                    subtitles.Requests == beforeReads + 2 && subtitles.NativeRequests == beforeNativeReads &&
                                    ((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).TryGetValue(track.Index, out var path) &&
                                    File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(assets, "english.srt"))),
                                    "Supported async managed URL fallback did not retain its bytes and execute/persist its actual SID under current service ownership, or issued a remote native read.");
                            else
                                Require(!cached && mpv.GetPropertyDouble("sid") <= 0 && service.ActiveSubtitleServerIndex == -1 &&
                                    wire.PreferenceWrites == beforeFallbackWrites && mpv.IsPaused &&
                                    subtitles.Requests == beforeReads + 1 && subtitles.NativeRequests == beforeNativeReads,
                                    "Retired profile/ABA URL fallback enabled captions, cached its SID or persisted a stale choice.");
                            Program.Log($"PASS: actual service unknown-format managed fallback {retirement} bytes/ownership/pause/preference.");
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
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static async Task VerifyLegacyPublisherGateAsync(PlayerService service, PlaybackStartResponse session, FileVersion version)
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
                    version, session, null, (int?)0, null);
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
            version, session, null, (int?)1, null);
        Program.Log("PASS: actual legacy choice publisher waits for the shared consumption gate before changing pending state.");
    }

    private static bool HasPendingPreference(PlayerService service)
    {
        var owner = Get(service, "_subtitleSelectionOperation");
        return owner != null && (bool)owner.GetType().GetProperty("Accepted")!.GetValue(owner)! &&
            (int)owner.GetType().GetField("PreferenceClaimed")!.GetValue(owner)! == 0;
    }

    internal static async Task RunAsync(StackPanel parent, bool unresolvedOnly = false, bool regionalOnly = false)
    {
        var presentation = Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE");
        if (presentation is "ambiguous-rebind" or "refresh-reindex" or "refresh-held" or "refresh-removed" or "legacy-preplay")
        {
            await RunAdditionalIdentityCasesAsync(parent);
            return;
        }
        if (presentation is "cast-off" or "cast-routes" or "reload-identity" or "async-add" or "async-lifecycle" or "async-service" or "async-core-probe" or "retired-read-cache" or "mime-busy" or "mime-ready" or "refresh-raw-labels" or "async-prompt-cancel" or "async-late-cleanup" or "fallback-deadline" or "cast-poll")
        {
            await RunCloseoutAsync(parent);
            return;
        }
        var aiCallback = presentation is "presentation-ai-post" or "presentation-ai-post-current" or "presentation-ai-post-access" or
            "presentation-ai-job" or "presentation-ai-job-current" or "presentation-ai-job-access" or "presentation-ai-refresh" or "presentation-ai-refresh-access" or
            "presentation-ai-prefetch" or "presentation-ai-prefetch-access" or "presentation-ai-cues" or "presentation-ai-cues-access";
        var presentationOnly = aiCallback || presentation is "presentation-flyout" or "presentation-osc" or "presentation-ai-menu" or "presentation-ai-dialog" or "presentation-ai-profile" or "presentation-ai-access";
        var assets = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity"));
        if (presentation == "review-consecutive-ai")
        {
            await AiSameFileReplanAsync(assets);
            return;
        }
        if (presentation == "review-existing-ai-retirement")
        {
            await AiSameFileReplanAsync(assets, retirement: "load");
            return;
        }
        if (presentation is "review-ai-load" or "review-ai-timeout-load" or "review-ai-aba" or "review-ai-same-file" or "review-owned-foreign-grant" or "review-cue-owned-cleanup" or "review-cue-positive" or "review-ai-newer-native-cue" or "review-ai-newer-off-cue" or "review-ai-newer-off-completion" or "review-ai-resume-manual")
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
                    manager.RecordLocalSubtitleSelection(null, isAutomatic: true);
                    automaticIntent = manager.CaptureLocalSubtitleIntent();
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, true);
                    await service.SetSubtitleTrackAndPersistAsync(busy.Index + 1, busy.Language, busy);
                    typeof(PlayerService).GetProperty("IsLoading")!.SetValue(service, false);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", busy.Index, false)!;
                    Require(manager.CaptureLocalSubtitleIntent() == automaticIntent && manager.IsSubtitleSelectionAutomatic &&
                        service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                        !HasPendingPreference(service) && wire.PreferenceWrites == preferenceWrites,
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

                var missingNative = session.SubtitleUrls.Single(track => track.Index == 1);
                var savedUrl = missingNative.Url;
                try
                {
                    missingNative.Url = "";
                    inventory = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Call(service, "SendSubtitleListToOsc");
                    using var unavailablePayload = JsonDocument.Parse(await inventory.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                    Require(unavailablePayload.RootElement.EnumerateArray().Single(t => t.GetProperty("index").GetInt32() == 1).GetProperty("unplayable").GetBoolean() &&
                        !unavailablePayload.RootElement.EnumerateArray().Single(t => t.GetProperty("index").GetInt32() == 2).GetProperty("unplayable").GetBoolean(),
                        "Actual OSC payload enabled an unrouteable native row or disabled its verified native peer.");
                    missingNative.Url = savedUrl;
                    inventory = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Call(service, "SendSubtitleListToOsc");
                    using var restoredPayload = JsonDocument.Parse(await inventory.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                    Require(!restoredPayload.RootElement.EnumerateArray().Single(t => t.GetProperty("index").GetInt32() == 1).GetProperty("unplayable").GetBoolean(),
                        "Actual OSC republish retained a disabled row after its sidecar route returned.");
                    Program.Log("PASS: actual OSC payload disables the full unrouteable row, retains a verified native peer and reenables a restored sidecar route.");
                }
                finally { missingNative.Url = savedUrl; }

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
            var reads = Volatile.Read(ref subtitles.Requests);
            await service.SetSubtitleTrackAndPersistAsync(3, english.Language, english);
            Require(mpv.GetPropertyDouble("sid") == 1, "Interleaved index-only inventory selected the wrong native SID instead of source container track 2.");
            Require(Volatile.Read(ref subtitles.Requests) == reads, "Native English selection unexpectedly read a remote sidecar.");
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
            session.SubtitleUrls.Remove(containerOnly);
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
            // An already explicit choice remains explicit as well. This is a
            // local provenance check, without a new API preference write.
            manager.RecordLocalSubtitleSelection(null, isAutomatic: false);
            Call(service, "CycleSubtitleBackward");
            Require(!manager.IsSubtitleSelectionAutomatic && !manager.CaptureLocalSubtitleIntent().IsAutomatic &&
                service.ActiveSubtitleServerIndex == 4 && manager.SubtitleSelectionForLoad == 4 && mpv.GetPropertyDouble("sid") == 2,
                "Local caption cycling lost explicit provenance or the actual Japanese native track.");
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
            subtitles.Fail = false;
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
                if (change == "current" && Environment.GetEnvironmentVariable("SILO_NATIVE_LEGACY_BOUNDARY") != "initializer")
                    await VerifyLegacyPublisherGateAsync(service, session, version);
                Call(service, "ApplyPendingSubtitleSelection");
                Require(mpv.GetPropertyDouble("sid") <= 0, "Legacy pre-play choice selected an outgoing SID before FILE_LOADED.");
                if (change == "retired-initializer")
                {
                    Set(service, "_loadedMediaInitGeneration", 2L);
                    Set(service, "_loadedMediaInitCts", currentInitializer);
                    var choice = Get(service, "_legacySubtitleChoice");
                    Require(choice != null, "Retired initializer control did not establish a newer legacy choice.");
                    var helper = typeof(PlayerService).GetMethod("TryApplyLegacyInitialSubtitleChoice", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    foreach (var (staleGeneration, staleOwner) in new[]
                        { (1L, retiredInitializer), (2L, retiredInitializer), (1L, currentInitializer) })
                    {
                        helper.Invoke(service, helper.GetParameters().Length == 0 ? [] : [staleGeneration, staleOwner]);
                        Require(ReferenceEquals(choice, Get(service, "_legacySubtitleChoice")) &&
                            Get(service, "_pendingSubtitleSelection") is int retained && retained == 1 &&
                            mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0,
                            "A retired initializer consumed the newer load's legacy choice before taking its state gate.");
                    }
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
            var pausedBeforeManual = mpv.IsPaused;
            var pauseIntentBeforeManual = Call(service, "CaptureUserPausedState");
            var resumeIntentBeforeManual = Get(service, "_resumeAfterLiveSubtitleBuffer");
            var bufferBeforeManual = Get(service, "_liveSubtitleResumeCts") != null;
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
            Program.Log($"Consecutive {scenario}: beforeManual cachedPause={pausedBeforeManual}, capturePause={pauseIntentBeforeManual}, buffer={bufferBeforeManual}, resume={resumeIntentBeforeManual}; expectedSID={expectedSid}, actualSID={mpv.GetPropertyDouble("sid")}, expectedIndex={expectedTrack?.Index ?? -1}, active={service.ActiveSubtitleServerIndex}, executed={ReferenceEquals(Get(service, "_executedSubtitleTrack"), expectedTrack)}, sameIntent={manager.CaptureLocalSubtitleIntent() == intent}, desired={manager.SubtitleSelectionForLoad}, prefs={wire.PreferenceWrites}/{writes}, nativePause={mpv.GetPropertyFlag("pause")}, cachedPause={mpv.IsPaused}, job={Get(service, "_liveSubtitleJobId")}, buffer={Get(service, "_liveSubtitleResumeCts") != null}, pathExists={File.Exists(livePath)}.");
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
        public byte[]? MediaBytes;
        public int Requests, NativeRequests, BusyResponses;
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
                    var count = Interlocked.Increment(ref Requests);
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
                    var native = request.Request.UserAgent?.Contains("mpv", StringComparison.OrdinalIgnoreCase) == true ||
                        request.Request.UserAgent?.Contains("Lavf", StringComparison.OrdinalIgnoreCase) == true;
                    if (native) Interlocked.Increment(ref NativeRequests);
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
                    if (BeforeSubtitleResponse is {} beforeSubtitle) await beforeSubtitle();
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
        public bool AiTranslateEnabled;
        public int AiStarts;
        public Func<Task>? BeforeAiStatus;
        public Func<Task>? BeforeAiStart, BeforeAiJob, BeforeAiInventory;
        public int? ReplanFileId;
        public int AiJobReads, AiInventoryReads;
        public long? AiResultSubtitleId;
        public JsonElement? AiStartRequest;
        public string? StoredSubtitleResponse;
        public readonly System.Collections.Concurrent.ConcurrentQueue<int> StoredPreferenceIndices = new();
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
            if (path == "/api/v2/subtitles/42" && StoredSubtitleResponse != null)
                return Json(StoredSubtitleResponse);
            if (path == "/api/v2/subtitles/42" && SubtitleList is { } list)
                return Json(list);
            if (path == "/api/v2/subtitles/42" && DownloadedInventory != null)
                return Json(JsonSerializer.Serialize(DownloadedInventory,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            if (path.Contains("-prefs/"))
            {
                PreferenceWrites++;
                if (request.Content != null)
                {
                    using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                    LastSubtitleMode = body.RootElement.GetProperty("subtitle_mode").GetString();
                    if (body.RootElement.TryGetProperty("track_signature", out var signature) && signature.ValueKind == JsonValueKind.Object)
                        SavedLabels.Enqueue(signature.GetProperty("label").GetString());
                    if ((StoredSubtitleResponse != null || InventoryHost != null) &&
                        body.RootElement.TryGetProperty("subtitle_track_index", out var index) &&
                        index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var storedIndex))
                        StoredPreferenceIndices.Enqueue(storedIndex);
                }
            }
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
