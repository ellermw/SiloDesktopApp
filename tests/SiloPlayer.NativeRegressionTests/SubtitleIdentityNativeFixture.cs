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
                        """{"name":"select_subtitle_track"}""",
                        """{"name":"select_subtitle_track","trackId":null}""",
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
                    await service.SetSubtitleTrackAndPersistAsync(0, null, null);
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
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static async Task ExpectUnavailableAsync(Func<Task> action)
    {
        try { await action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("SiloCast reported success for an unavailable subtitle.");
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
