using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

internal static class SiloCastScopeChecks
{
    public static async Task RunAsync(string testDirectory)
    {
        using var localHandler = new ScopeHandler("local");
        using var remoteHandler = new ScopeHandler("remote");
        using var localHttp = new HttpClient(localHandler);
        using var remoteHttp = new HttpClient(remoteHandler);
        var credentials = new MemoryCredentialStore();
        var localApi = new SiloApiClient(localHttp);
        localApi.SetBaseUrl("https://local.fixture.invalid");
        localApi.SetDeviceMetadata("fixture-pc", "Fixture PC", "windows");
        using var localAuth = new AuthService(localApi, new AuthApi(localApi), credentials);
        localAuth.SetTokens("local-access", "local-refresh", 86400);
        localAuth.SelectProfile("local-profile", "local-proof");
        var savedCredentials = credentials.Snapshot();
        var localPlayback = new PlaybackApi(localApi);
        using var service = new PlayerService(localPlayback, new CatalogApi(localApi), localAuth, localApi,
            new SettingsService(Path.Combine(testDirectory, "silocast-fixture-settings")), new SettingsApi(localApi));
        var localAuthority = service.ActiveAuthority;

        var remoteApi = new SiloApiClient(remoteHttp);
        remoteApi.SetBaseUrl("https://remote.fixture.invalid");
        remoteApi.SetDeviceMetadata("fixture-pc", "Fixture PC", "windows");
        using var remoteAuth = new AuthService(remoteApi, new AuthApi(remoteApi), credentialStore: null);
        remoteAuth.SetTokens("remote-access", "remote-refresh", 3600);
        remoteAuth.SelectProfile("remote-profile", "remote-proof");
        var remoteAuthority = new PlaybackAuthorityScope(remoteApi, remoteAuth);
        SetField(service, "_activeAuthority", remoteAuthority);

        var browsing = await service.GetOrFetchWatchDetailAsync("same-movie");
        Require(browsing.Title == "local watch", "Browsing used the remote identity.");
        var playbackCore = typeof(PlayerService).GetMethod("GetOrFetchWatchDetailCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Published PlayerService lacks the playback watch boundary.");
        var remoteWatch = await (Task<WatchDetailResponse>)playbackCore.Invoke(service,
            ["same-movie", true, CancellationToken.None, null, null, null])!;
        Require(remoteWatch.Title == "remote watch", "Playback consumed a browsing prefetch or used local credentials.");
        service.PrefetchWatchDetail("hover-movie");
        var hover = await service.GetOrFetchWatchDetailAsync("hover-movie");
        Require(hover.Title == "local watch", "Public hover prefetch used remote authority.");
        Require(localHandler.Requests.Count(r => r.Path == "/api/v2/watch/hover-movie") == 1,
            "Hover and browsing failed to coalesce under local authority.");
        Require(!remoteHandler.Requests.Any(r => r.Path == "/api/v2/watch/hover-movie"),
            "Hover sent a remote watch request.");
        using (var sameServerHandler = new ScopeHandler("same-server remote"))
        using (var sameServerHttp = new HttpClient(sameServerHandler))
        {
            var sameServerApi = new SiloApiClient(sameServerHttp);
            sameServerApi.SetBaseUrl("https://local.fixture.invalid");
            using var sameServerAuth = new AuthService(sameServerApi, new AuthApi(sameServerApi), credentialStore: null);
            sameServerAuth.SetTokens("same-server-remote-access", "same-server-remote-refresh", 3600);
            sameServerAuth.SelectProfile("local-profile", "same-server-remote-proof");
            Require(sameServerApi.CaptureContext() == localApi.CaptureContext(),
                "Same-server/profile collision fixture does not have equal authentication/request epochs.");
            var sameServerAuthority = new PlaybackAuthorityScope(sameServerApi, sameServerAuth);
            SetField(service, "_activeAuthority", sameServerAuthority);
            try
            {
                var sameServerWatch = await (Task<WatchDetailResponse>)playbackCore.Invoke(service,
                    ["hover-movie", true, CancellationToken.None, null, null, null])!;
                Require(sameServerWatch.Title == "same-server remote watch" && sameServerHandler.Requests.Count == 1 &&
                        sameServerHandler.Requests.Single().Authorization == "Bearer same-server-remote-access",
                    "Equal URL/profile/epochs allowed a remote identity to consume a local browsing prefetch.");
            }
            finally { SetField(service, "_activeAuthority", remoteAuthority); }
        }
        Console.WriteLine("PASS published PlayerService SiloCast: equal URL/profile/authentication/request epochs still isolate local and remote prefetches.");
        Require(localHandler.Requests.Count(r => r.Path == "/api/v2/watch/same-movie") == 1 &&
                remoteHandler.Requests.Count(r => r.Path == "/api/v2/watch/same-movie") == 1,
            "Local and remote cache entries shared data or issued duplicate requests.");
        var libraryWatch = await service.GetOrFetchWatchDetailAsync("library-movie", libraryId: 7, fileId: 42);
        var otherLibraryWatch = await service.GetOrFetchWatchDetailAsync("library-movie", libraryId: 9, fileId: 43);
        var cachedLibraryWatch = await service.GetOrFetchWatchDetailAsync("library-movie", libraryId: 7, fileId: 42);
        Require(libraryWatch.PreparedLibraryId == 7 && libraryWatch.PreparedFileId == 42 &&
                otherLibraryWatch.PreparedLibraryId == 9 && otherLibraryWatch.PreparedFileId == 43 && ReferenceEquals(libraryWatch, cachedLibraryWatch),
            "Browsing lost library/file preparation or shared cache entries across different routes.");
        Require(localHandler.Requests.Where(r => r.Path == "/api/v2/watch/library-movie").Select(r => r.Query)
            .SequenceEqual(new[] { "?library_id=7&file_id=42", "?library_id=9&file_id=43" }),
            "Library/file watch routes were dropped or fetched more than once.");
        var remoteLibraryWatch = await (Task<WatchDetailResponse>)playbackCore.Invoke(service,
            ["library-movie", true, CancellationToken.None, 7, 42, null])!;
        Require(remoteLibraryWatch.Title == "remote watch" && remoteLibraryWatch.PreparedLibraryId == 7 && remoteLibraryWatch.PreparedFileId == 42,
            "Remote playback lost the current library/file overload.");

        localHandler.BlockedWatchContentId = "stale-local";
        var staleWatch = service.GetOrFetchWatchDetailAsync("stale-local");
        await localHandler.WatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        localApi.SetProfile("local-profile", "changed-local-proof");
        localHandler.ReleaseWatch.TrySetResult();
        try { await staleWatch; throw new InvalidOperationException("Browsing returned watch detail prepared under a stale API context."); }
        catch (OperationCanceledException) { }
        localHandler.BlockedWatchContentId = null;
        localApi.SetProfile("local-profile", "local-proof");
        await service.GetOrFetchWatchDetailAsync("stale-local");
        Require(localHandler.Requests.Count(r => r.Path == "/api/v2/watch/stale-local") == 2,
            "A stale context populated the next browsing generation's cache.");
        AssertLocalIdentity();
        Console.WriteLine("PASS published PlayerService SiloCast: browsing/watch hover retain local identity; playback watch and its cache use remote identity.");
        Console.WriteLine("PASS published PlayerService SiloCast: library/file routes and cache keys survive handoff; stale captured browsing contexts cancel and refetch.");

        // Register the session through the actual API v2 path; only native window
        // setup and the manager's already-started session state are fixture-installed.
        await remoteAuthority.PlaybackApi.StartPlaybackV3Async(new PlaybackStartRequestV3
        {
            ProtocolVersion = 3, FileId = 42, ProfileId = "remote-profile",
            PlaybackAttemptId = "fixture-attempt", QualityPreference = "original",
        });
        var session = new PlaybackStartResponse
        {
            ProtocolVersion = 3, SessionId = "remote-session", MediaFileId = 42,
            AudioTrackIndex = 1, DurationSeconds = 1200,
            SubtitleUrls =
            [
                new() { Index = 4, Label = "English", Language = "en", Codec = "srt" },
                new() { Index = 8, Label = "French", Language = "fr", Codec = "vtt" },
            ],
            AvailableQualities = [new() { Label = "original", PreservesSource = true }, new() { Label = "1080p-high" }],
        };
        var manager = new PlaybackManager(remoteAuthority.PlaybackApi, remoteAuthority.CatalogApi, remoteAuth, remoteApi);
        SetField(manager, "_sessionId", "remote-session");
        SetProperty(manager, "CurrentSession", session);
        manager.UseWatchDetail(remoteWatch);
        SetField(service, "_playbackManager", manager);
        SetProperty(service, "ContentId", "same-movie");
        SetProperty(service, "Title", "Remote movie");
        SetProperty(service, "Position", 321d);
        SetProperty(service, "Duration", 1200d);
        SetProperty(service, "State", PlayerState.Expanded);
        SetProperty(service, "IsPaused", false);
        SetProperty(service, "Versions", new List<FileVersion>
        {
            new() { FileId = 42, Hdr = true, AudioTracks =
                [new() { Title = "English", Language = "en" }, new() { Title = "French", Language = "fr" }] },
        });
        SetField(service, "_activeSubtitleServerIndex", 8);
        var state = service.CaptureSiloCastState();
        Require(state["audioTracks"]!.AsArray().Select(t => t!["trackId"]!.GetValue<int>()).SequenceEqual(new[] { 1, 2 }),
            "Advertised audio IDs do not map to the server inventory.");
        Require(state["subtitleTracks"]!.AsArray().Select(t => t!["trackId"]!.GetValue<int>()).SequenceEqual(new[] { 5, 9 }),
            "Advertised subtitles lost sparse server indexes.");
        Require(state["selectedAudioTrackId"]!.GetValue<int>() == 2 && state["selectedSubtitleTrackId"]!.GetValue<int>() == 9,
            "State selected IDs differ from advertised choices.");
        Require(state["qualityOptions"]!.AsArray().Select(q => q!["id"]!.GetValue<string>()).SequenceEqual(new[] { "auto", "1080p-high" }),
            "Advertised quality choices differ from the captured session.");
        Require(state["activeQualityId"]!.GetValue<string>() == "auto", "Cast state must report selected quality intent.");
        Require(state["supportsHDRToggle"]!.GetValue<bool>() == false && state["hdrEnabled"]!.GetValue<bool>(),
            "HDR state or required v2 compatibility field changed.");
        Require(state["currentTime"]!.GetValue<double>() == 321 && state["sessionId"]!.GetValue<string>() == "remote-session",
            "State changed the captured session timeline.");
        SetField(service, "_activeSubtitleServerIndex", -1);
        Require(service.CaptureSiloCastState()["selectedSubtitleTrackId"] == null, "Explicit subtitle-off did not produce a null selected ID.");
        SetField(service, "_activeSubtitleServerIndex", 8);
        Console.WriteLine("PASS published PlayerService SiloCast: advertised audio/subtitle IDs preserve server choices, sparse subtitles, explicit off, quality, HDR and timeline.");

        var captureSnapshot = typeof(PlayerService).GetMethod("CapturePlaybackUiSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var restoreSnapshot = typeof(PlayerService).GetMethod("RestorePlaybackUiSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var snapshot = captureSnapshot.Invoke(service, null)!;
        SetField(service, "_activeAuthority", localAuthority);
        SetProperty(service, "State", PlayerState.Fullscreen);
        restoreSnapshot.Invoke(service, [snapshot, false]);
        Require(service.State == PlayerState.Fullscreen, "A retired subtitle dialog restored playback UI across an authority switch.");
        SetField(service, "_activeAuthority", remoteAuthority);
        using (var replacement = new PlaybackManager(remoteAuthority.PlaybackApi, remoteAuthority.CatalogApi, remoteAuth, remoteApi))
        {
            SetField(service, "_playbackManager", replacement);
            restoreSnapshot.Invoke(service, [snapshot, false]);
            Require(service.State == PlayerState.Fullscreen, "A retired subtitle dialog restored playback UI onto a later session of the same owner.");
            SetField(service, "_playbackManager", manager);
        }
        SetProperty(service, "State", PlayerState.Expanded);
        Console.WriteLine("PASS published PlayerService SiloCast: subtitle dialog snapshots cannot restore another owner or session.");

        // A sentinel native object is sufficient: these stale controls must
        // return before any native call after the blocked drain is released.
        var mpvType = typeof(PlayerService).GetField("_mpv", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType;
        var nativeSentinel = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(mpvType);
        var transportGate = (SemaphoreSlim)GetField(service, "_transportRestartGate")!;
        foreach (var audio in new[] { true, false })
        {
            using var replacement = new PlaybackManager(localAuthority.PlaybackApi, localAuthority.CatalogApi, localAuth, localApi);
            await transportGate.WaitAsync();
            SetField(service, "_mpv", nativeSentinel);
            Task control;
            try
            {
                control = audio ? service.SwitchAudioTrackAsync(1) : service.SelectQualityAsync("1080p-high");
                Require(!control.IsCompleted, "Control touched native playback before draining captured transport work.");
                SetField(service, "_activeAuthority", localAuthority);
                SetField(service, "_playbackManager", replacement);
            }
            finally { transportGate.Release(); }
            try
            {
                await control.WaitAsync(TimeSpan.FromSeconds(2));
                Require(!service.IsLoading && ReferenceEquals(GetField(service, "_playbackManager"), replacement),
                    "A queued remote audio/quality control changed the replacement local session.");
                Require(!localHandler.Requests.Any(r => r.Path.StartsWith("/api/v2/playback/", StringComparison.Ordinal)),
                    "A queued remote control sent a playback request using local authority.");
            }
            finally
            {
                SetField(service, "_mpv", null);
                SetField(service, "_activeAuthority", remoteAuthority);
                SetField(service, "_playbackManager", manager);
            }
        }
        Console.WriteLine("PASS published PlayerService SiloCast: blocked audio/quality controls retain their captured owner and cannot alter a replacement local session.");

        var ended = new TaskCompletionSource<PlaybackAuthorityScope>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RemoteAuthorityEnded += authority =>
        {
            Require(remoteHandler.DeleteCompleted, "Remote authority ended before the captured DELETE completed.");
            Require(ReferenceEquals(service.ActiveAuthority, localAuthority), "End notification did not restore local authority first.");
            Require(ReferenceEquals(authority, remoteAuthority), "End notification reported the wrong owner.");
            AssertLocalIdentity();
            ended.TrySetResult(authority);
        };
        remoteHandler.BlockTeardown = true;
        var authorityGate = (SemaphoreSlim)GetField(service, "_authoritySwitchGate")!;
        var ownerLifetime = (CancellationTokenSource)GetField(service, "_authorityLifetimeCts")!;
        await authorityGate.WaitAsync();
        var queuedContinuation = service.PlayAsync("retired-remote", authority: remoteAuthority, cancellationToken: ownerLifetime.Token);
        var close = service.CloseAsync();
        try { await queuedContinuation.WaitAsync(TimeSpan.FromSeconds(2)); throw new InvalidOperationException("Close left the outgoing owner's queued continuation alive."); }
        catch (OperationCanceledException) { }
        finally { authorityGate.Release(); }
        Require(!remoteHandler.Requests.Any(r => r.Path == "/api/v2/watch/retired-remote"),
            "A queued continuation started playback after its owner was retired.");
        await remoteHandler.ProgressEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(!close.IsCompleted && !ended.Task.IsCompleted && service.HasRemoteAuthority,
            "Close released authority before final progress completed.");
        var concurrentClose = service.CloseAsync();
        Require(ReferenceEquals(close, concurrentClose), "Concurrent CloseAsync callers did not share the captured teardown completion.");
        Require(!concurrentClose.IsCompleted, "Concurrent CloseAsync returned before final progress/DELETE finished.");
        Require(ReferenceEquals(service.ActiveAuthority, remoteAuthority), "Close changed authority while final progress was in flight.");
        remoteHandler.ReleaseProgress.TrySetResult();
        await remoteHandler.DeleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(!close.IsCompleted && !ended.Task.IsCompleted && service.HasRemoteAuthority,
            "Close released authority before DELETE completed.");
        Require(!concurrentClose.IsCompleted, "Concurrent CloseAsync returned while DELETE was in flight.");
        AssertLocalIdentity();
        remoteHandler.ReleaseDelete.TrySetResult();
        await close.WaitAsync(TimeSpan.FromSeconds(2));
        await concurrentClose.WaitAsync(TimeSpan.FromSeconds(2));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(!service.HasRemoteAuthority && service.State == PlayerState.Idle, "Close failed to restore idle/local playback.");
        var idleState = service.CaptureSiloCastState();
        Require(idleState["subtitleTracks"]!.AsArray().Count == 0 && idleState["selectedSubtitleTrackId"] == null,
            "Idle state retained a selected subtitle from retired remote playback.");
        Require(service.PendingProgressSave.IsCompletedSuccessfully, "Close left captured progress/stop work undrained.");
        var teardown = remoteHandler.Requests.Where(r => r.Path.StartsWith("/api/v2/playback/remote-session", StringComparison.Ordinal)).ToArray();
        Require(teardown.Length == 2 && teardown[0].Method == "POST" && teardown[1].Method == "DELETE", "Captured session teardown order changed.");
        Require(teardown.All(r => r.Authorization == "Bearer remote-access" && r.ProfileId == "remote-profile" && r.ProfileToken == "remote-proof"),
            "Progress/stop were reauthorized under local credentials.");
        Require(teardown.All(r => r.Body?["position"]?.GetValue<double>() == 321 && r.Body?["is_paused"]?.GetValue<bool>() == true),
            "Final progress and stop lost the captured media position/pause sample.");
        Require(!localHandler.Requests.Any(r => r.Path.StartsWith("/api/v2/playback/", StringComparison.Ordinal)), "Remote teardown touched a local session.");
        var browsingAfterClose = await service.GetOrFetchWatchDetailAsync("after-close");
        Require(browsingAfterClose.Title == "local watch", "Browsing did not resume local authority after remote close.");
        AssertLocalIdentity();
        Console.WriteLine("PASS published PlayerService SiloCast: CloseAsync drains captured remote progress and DELETE before local restoration/end notification; saved login/profile remain intact.");
        Console.WriteLine("PASS published PlayerService SiloCast: CloseAsync cancels the outgoing owner's queued continuation before native playback startup.");

        void AssertLocalIdentity()
        {
            Require(localApi.AccessToken == "local-access" && localAuth.RefreshToken == "local-refresh" &&
                localAuth.SelectedProfileId == "local-profile" && localApi.ProfileToken == "local-proof",
                "Remote playback mutated the desktop's in-memory login/profile.");
            Require(credentials.Snapshot().SequenceEqual(savedCredentials), "Remote playback changed the desktop's saved credentials.");
            Require(localHandler.Requests.All(r => r.Authorization == "Bearer local-access" &&
                r.ProfileId == "local-profile" && r.ProfileToken == "local-proof"), "Browsing request lost desktop authorization.");
        }
    }

    private static void SetField(object target, string name, object? value)
        => (target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Fixture field missing: " + name)).SetValue(target, value);
    private static object? GetField(object target, string name)
        => (target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Fixture field missing: " + name)).GetValue(target);
    private static void SetProperty(object target, string name, object? value)
        => (target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Fixture property missing: " + name)).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class MemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = [];
        public void SaveCredential(string serverUrl, string key, string value) => _values[serverUrl + "|" + key] = value;
        public string? LoadCredential(string serverUrl, string key) => _values.GetValueOrDefault(serverUrl + "|" + key);
        public void DeleteCredential(string serverUrl, string key) => _values.Remove(serverUrl + "|" + key);
        public void DeleteAllForServer(string serverUrl)
        {
            foreach (var key in _values.Keys.Where(k => k.StartsWith(serverUrl + "|", StringComparison.Ordinal)).ToArray()) _values.Remove(key);
        }
        public KeyValuePair<string, string>[] Snapshot() => _values.OrderBy(k => k.Key, StringComparer.Ordinal).ToArray();
    }

    private sealed record RequestRecord(string Method, string Path, string Query, string? Authorization, string? ProfileId, string? ProfileToken, JsonObject? Body);
    private sealed class ScopeHandler(string scope) : HttpMessageHandler
    {
        public ConcurrentQueue<RequestRecord> Requests { get; } = new();
        public bool BlockTeardown { get; set; }
        public bool DeleteCompleted { get; private set; }
        public string? BlockedWatchContentId { get; set; }
        public TaskCompletionSource WatchEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProgressEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseProgress { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
            Requests.Enqueue(new(request.Method.Method, path, request.RequestUri.Query, request.Headers.Authorization?.ToString(), Header("X-Profile-Id"), Header("X-Profile-Token"), body));
            if (path.StartsWith("/api/v2/watch/", StringComparison.Ordinal))
            {
                if (path.Split('/').Last() == BlockedWatchContentId)
                {
                    WatchEntered.TrySetResult();
                    await ReleaseWatch.Task.WaitAsync(ct);
                }
                return Json(new JsonObject { ["content_id"] = path.Split('/').Last(), ["title"] = scope + " watch", ["type"] = "movie", ["versions"] = new JsonArray() }.ToJsonString());
            }
            if (path == "/api/v2/playback/capabilities")
                return Json("""{"state":"available","allowed":true,"protocol_versions":[3],"installation_id":"fixture-installation","features":["fixed_media_file_v1"]}""");
            if (path == "/api/v2/playback/start")
                return Json("""{"outcome":"playable","protocol_version":3,"session_id":"remote-session"}""");
            if (path == "/api/v2/playback/remote-session/progress")
            {
                ProgressEntered.TrySetResult();
                if (BlockTeardown) await ReleaseProgress.Task.WaitAsync(ct);
                return Json("""{"outcome":"applied","accepted":{"sequence":1,"position":321,"is_paused":true}}""");
            }
            if (path == "/api/v2/playback/remote-session" && request.Method == HttpMethod.Delete)
            {
                DeleteEntered.TrySetResult();
                if (BlockTeardown) await ReleaseDelete.Task.WaitAsync(ct);
                DeleteCompleted = true;
                return Json("""{"outcome":"stopped","accepted":{"sequence":2,"position":321,"is_paused":true}}""");
            }
            throw new InvalidOperationException("Unexpected fixture request: " + request.Method + " " + path);
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
