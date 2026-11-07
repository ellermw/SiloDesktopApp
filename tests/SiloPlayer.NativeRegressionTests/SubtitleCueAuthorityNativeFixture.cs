using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class SubtitleCueAuthorityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_IDENTITY_CASE") ?? "";
        var modes = new[] { "integrated", "worker", "redirect", "declared", "stream", "worker-redirect", "worker-redirect-declared", "worker-redirect-stream", "switch-during-reload", "failed-reload-reselect" };
        if (mode.Length > 0 && !modes.Contains(mode)) throw new InvalidOperationException("Unknown cue authority scope.");
        foreach (var scenario in mode.Length == 0 ? modes : [mode]) await CheckAsync(scenario);
    }

    private static async Task CheckAsync(string scenario)
    {
        var assets = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity"));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(assets, "english.srt"));
        var reselect = scenario is "switch-during-reload" or "failed-reload-reselect";
        if (reselect) bytes = System.Text.Encoding.UTF8.GetBytes("1\n00:00:02,000 --> 00:00:04,000\nCorrected timing\n\n");
        var declared = scenario.EndsWith("declared", StringComparison.Ordinal);
        var streamed = scenario.EndsWith("stream", StringComparison.Ordinal);
        var workerRedirect = scenario.StartsWith("worker-redirect", StringComparison.Ordinal);
        using var integrated = new CueServer(bytes) { Mode = declared ? "declared" : streamed ? "stream" : "normal" };
        using var worker = new CueServer(bytes);
        if (scenario == "redirect") { integrated.Mode = "redirect"; integrated.Redirect = worker.Origin + "/captions.srt"; worker.Mode = "normal"; }
        if (workerRedirect) { worker.Mode = "redirect"; worker.Redirect = integrated.Origin + "/captions.srt"; }
        using var http = new HttpClient(new Wire());
        var api = new SiloApiClient(http); api.SetBaseUrl(integrated.Origin);
        // Generated proofs belong only to this isolated fixture; only header
        // presence is observed or recorded, never their random values.
        api.BeginAuthenticationSession(Guid.NewGuid().ToString("N"));
        api.SetProfile("synthetic-cue", Guid.NewGuid().ToString("N"));
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "cue-" + scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var key = "external-" + new string('a', 64);
        var track = new SubtitleTrackInfo { Index = 0, Source = "external", Codec = "srt", Language = "en", SyncKey = key,
            Url = scenario == "worker" || workerRedirect ? worker.Origin + "/captions.srt" : integrated.Origin + "/api/v2/stream/fixture/subtitles/0" };
        var session = new PlaybackStartResponse { SessionId = "cue-session", ProtocolVersion = 3, MediaFileId = 42, PlayMethod = "direct", SubtitleUrls = [track] };
        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
        Set(manager, "_sessionId", session.SessionId);
        manager.UseWatchDetail(new() { ContentId = "movie", SeriesId = "series", Versions = [new() { FileId = 42 }] });
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "movie");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPaused(bool value) { if (value) paused.TrySetResult(); }
        mpv.PauseChanged += OnPaused;
        try { mpv.Pause(); if (!mpv.IsPaused) await paused.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
        finally { mpv.PauseChanged -= OnPaused; }
        Require(mpv.AddSubtitle(Path.Combine(assets, "english.srt"), "Previous cue", "en", select: true), "Cue fixture could not establish its applied subtitle.");
        var previousSid = (int)Math.Round(mpv.GetPropertyDouble("sid"));
        ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!)[0] = previousSid;
        Set(service, "_activeSubtitleServerIndex", 0);
        using var lifetime = new CancellationTokenSource();
        Set(service, "_subtitleSyncCts", lifetime);
        Set(service, "_subtitleSyncOwner", Get(service, "_activeAuthority"));
        Set(service, "_subtitleSyncAuthority", api.CaptureContext());
        Set(service, "_subtitleSyncSession", session.SessionId);
        Set(service, "_subtitleSyncFile", 42);
        var entryType = typeof(PlayerService).GetNestedType("SyncEntry", BindingFlags.NonPublic)!;
        var entry = Activator.CreateInstance(entryType, nonPublic: true)!;
        entryType.GetField("State")!.SetValue(entry, new SubtitleSyncState { Key = key, MediaFileId = 42, Source = "external",
            Format = "srt", Language = "en", Timing = new() { OffsetMs = 75 } });
        var entries = Get(service, "_subtitleSyncEntries")!;
        entries.GetType().GetMethod("Add")!.Invoke(entries, [key, entry]);
        Task? reload = null;
        try
        {
            if (reselect)
            {
                await CheckStaleReselectionAsync(service, manager, mpv, integrated, track, entry, lifetime.Token,
                    assets, bytes, previousSid, scenario == "failed-reload-reselect");
                return;
            }
            reload = (Task)Call(service, "ReloadCorrectedSubtitleAsync", entry, lifetime.Token, false)!;
            if (declared || streamed)
            {
                await integrated.GateReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(integrated.AttemptedBytes >= (declared ? 1 : SubtitleSidecarFile.MaxBytes + 1), "The body-limit probe did not reach its structural HTTP boundary.");
                try { await reload.WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (TimeoutException ex) { throw new InvalidOperationException("Corrected cue read waited for an oversized held body instead of enforcing its declared/stream limit.", ex); }
            }
            else await reload.WaitAsync(TimeSpan.FromSeconds(5));
            var succeeds = scenario is "integrated" or "worker" or "worker-redirect";
            var mappedSid = ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!)[0];
            var error = entryType.GetField("Error")!.GetValue(entry) as string;
            Program.Log($"Cue boundary {scenario}: integratedRequests={integrated.Headers.Count}, workerRequests={worker.Headers.Count}, workerProofPresent={worker.Headers.Any(h => h.Proof)}, errorPresent={error != null}.");
            if (scenario == "redirect") Require(worker.Headers.IsEmpty,
                "A corrected integrated cue redirect forwarded a request or profile proof to another origin.");
            Require(mpv.IsPaused && service.ActiveSubtitleServerIndex == 0, "Cue reload changed playback pause or its selected server index.");
            if (succeeds)
                Require(mappedSid > 0 && mappedSid != previousSid && mpv.GetPropertyDouble("sid") == mappedSid && error == null,
                    "An authorized cue reload did not apply its new native SID.");
            else
                Require(mappedSid == previousSid && mpv.GetPropertyDouble("sid") == previousSid && error != null &&
                    ((List<string>)Get(service, "_subtitleSyncFiles")!).Count == 0,
                    "A refused redirect/oversized cue replaced the applied subtitle or left an attached file.");
            if (scenario == "worker" || workerRedirect) Require(worker.Headers.TryPeek(out var workerHeaders) && !workerHeaders.Authorization && !workerHeaders.Profile && !workerHeaders.Proof,
                "A corrected worker cue request received account/profile credentials.");
            if (workerRedirect) Require(worker.Headers.Count == 1 && integrated.Headers.Count == 1 &&
                integrated.Headers.All(headers => !headers.Authorization && !headers.Profile && !headers.Proof),
                "A worker redirect failed to reach its target or introduced account/profile credentials at the next origin.");
            if (scenario == "integrated") Require(integrated.Headers.TryPeek(out var integratedHeaders) && integratedHeaders.Authorization && integratedHeaders.Profile && integratedHeaders.Proof,
                "An integrated cue request lost its captured account/profile authority.");
            Program.Log($"PASS: actual corrected cue {scenario} authority/body boundary retains native selection and pause.");
        }
        finally
        {
            lifetime.Cancel(); integrated.Release.TrySetResult(); worker.Release.TrySetResult();
            if (reload != null) try { await reload.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
            Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckStaleReselectionAsync(PlayerService service, PlaybackManager manager, MpvPlayer mpv,
        CueServer server, SubtitleTrackInfo track, object entry, CancellationToken ct, string assets, byte[] corrected, int previousSid, bool fail)
    {
        var sids = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
        var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
        var other = new SubtitleTrackInfo { Index = 1, Source = "external", Codec = "srt", Language = "ja",
            Url = server.Origin + "/other.srt" };
        manager.CurrentSession!.SubtitleUrls.Add(other);
        Require(mpv.AddSubtitle(Path.Combine(assets, "japanese.srt"), "Other cue", "ja"), "Could not cache the other track.");
        var otherSid = mpv.GetSubtitleTrackIds(externalOnly: true).Max();
        sids[other.Index] = otherSid;
        mpv.SetSubtitleTrack(previousSid);
        // These are the timing-change path's invalidation and corrected read.
        // The old active SID must remain visible, but no longer be reusable.
        Call(service, "InvalidateSubtitleTimingCache", track.SyncKey);
        Require(mpv.GetPropertyDouble("sid") == previousSid, "Timing invalidation removed the applied captions before replacement.");
        server.Mode = fail ? "reject" : "held";
        var reload = (Task)Call(service, "ReloadCorrectedSubtitleAsync", entry, ct, false)!;
        try
        {
            if (fail)
            {
                await reload.WaitAsync(TimeSpan.FromSeconds(5));
                Require(entry.GetType().GetField("Error")!.GetValue(entry) != null && mpv.GetPropertyDouble("sid") == previousSid,
                    "A failed corrected read did not retain the applied captions and expose its error.");
            }
            else
            {
                await server.GateReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!reload.IsCompleted, "The corrected read was not held before the newer choice.");
            }
            Require((bool)Call(service, "SelectSubtitleSidecar", other)!, "Could not switch to the cached other track.");
            Set(service, "_activeSubtitleServerIndex", other.Index);
            server.Release.TrySetResult();
            await reload.WaitAsync(TimeSpan.FromSeconds(5));
            Require(mpv.GetPropertyDouble("sid") == otherSid && !files.ContainsKey(track.Index),
                "An old corrected read replaced the newer choice or registered discarded bytes.");
            // A direct cached selection must refuse the stale SID too, even
            // before the asynchronous selection preflight downloads new cues.
            Require(!(bool)Call(service, "SelectSubtitleSidecar", track)! && mpv.GetPropertyDouble("sid") == otherSid,
                "Selecting the old track silently reused its stale-timing SID.");
            server.Mode = "normal";
            var reads = server.Headers.Count;
            await ((Task)Call(service, "PrefetchSubtitleSidecarAsync", track)!).WaitAsync(TimeSpan.FromSeconds(5));
            Require(server.Headers.Count == reads + 1 && files.TryGetValue(track.Index, out var path) &&
                File.ReadAllBytes(path).SequenceEqual(corrected) && mpv.GetPropertyDouble("sid") == otherSid,
                "Reselecting a timing-invalidated track did not fetch fresh corrected cues before switching.");
            Require((bool)Call(service, "SelectSubtitleSidecar", track)!, "Fresh corrected cues could not be selected.");
            Set(service, "_activeSubtitleServerIndex", track.Index);
            var newSid = sids[track.Index];
            Require(newSid != previousSid && mpv.GetPropertyDouble("sid") == newSid && mpv.IsPaused &&
                !mpv.GetSubtitleTrackIds(externalOnly: true).Contains(previousSid),
                "Fresh selection reused the stale SID, retained it in mpv, or changed pause.");
            await ((Task)Call(service, "PrefetchSubtitleSidecarAsync", track)!).WaitAsync(TimeSpan.FromSeconds(5));
            Require((bool)Call(service, "SelectSubtitleSidecar", track)! && sids[track.Index] == newSid && server.Headers.Count == reads + 1,
                "The successful replacement remained stale instead of being reusable.");
            Program.Log($"PASS: {(fail ? "failed" : "switched-away")} corrected read retains applied captions and reselects only fresh cues.");
        }
        finally
        {
            server.Release.TrySetResult();
            await reload.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
    private sealed record HeaderPresence(bool Authorization, bool Profile, bool Proof);
    private sealed class CueServer : IDisposable
    {
        private readonly HttpListener listener = new();
        public string Origin { get; }
        public string Mode = "normal";
        public string? Redirect;
        public long AttemptedBytes;
        public ConcurrentQueue<HeaderPresence> Headers { get; } = new();
        public TaskCompletionSource GateReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CueServer(byte[] subtitle)
        {
            var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reserve.Start();
            var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; listener.Prefixes.Add(Origin + "/"); listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); } catch (Exception) when (!listener.IsListening) { break; }
                    Headers.Enqueue(new(context.Request.Headers["Authorization"] != null, context.Request.Headers["X-Profile-Id"] != null, context.Request.Headers["X-Profile-Token"] != null));
                    try
                    {
                        var response = context.Response;
                        if (Mode == "held") { GateReached.TrySetResult(); await Release.Task; }
                        if (Mode == "reject") { response.StatusCode = 503; response.Close(); continue; }
                        if (Mode == "redirect") { response.StatusCode = 302; response.RedirectLocation = Redirect; response.Close(); continue; }
                        response.ContentType = "application/x-subrip";
                        if (Mode == "declared")
                        {
                            response.ContentLength64 = SubtitleSidecarFile.MaxBytes + 1;
                            Interlocked.Increment(ref AttemptedBytes);
                            await response.OutputStream.WriteAsync(subtitle.AsMemory(0, 1));
                            await response.OutputStream.FlushAsync(); GateReached.TrySetResult();
                            await Release.Task;
                        }
                        else if (Mode == "stream")
                        {
                            response.SendChunked = true;
                            var body = new byte[SubtitleSidecarFile.MaxBytes + 1];
                            subtitle.CopyTo(body, 0); Array.Fill(body, (byte)'\n', subtitle.Length, body.Length - subtitle.Length);
                            for (var offset = 0; offset < body.Length; offset += 65536)
                            {
                                var count = Math.Min(65536, body.Length - offset); Interlocked.Add(ref AttemptedBytes, count);
                                await response.OutputStream.WriteAsync(body.AsMemory(offset, count));
                            }
                            await response.OutputStream.FlushAsync(); GateReached.TrySetResult();
                            await Release.Task;
                        }
                        else { response.ContentLength64 = subtitle.Length; await response.OutputStream.WriteAsync(subtitle); }
                        response.Close();
                    }
                    catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
                    { GateReached.TrySetResult(); }
                }
            });
        }
        public void Dispose() { Release.TrySetResult(); listener.Close(); }
    }
}
