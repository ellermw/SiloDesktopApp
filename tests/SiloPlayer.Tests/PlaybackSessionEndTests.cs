using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Official silo-server #5180 (paused_timeout_v1) and #5225: a session the server ended answers 410, and the player
/// leaves instead of retrying or recovering into a fresh attempt.
/// </summary>
public sealed class PlaybackSessionEndTests
{
    private const string PausedTimeout = "playback_session_paused_timeout";
    private const string Ended = "playback_session_ended";

    [Fact]
    public async Task PausedTimeoutOnPeriodicProgress_StopsReportingAtOnceWithoutCountingAFailure()
    {
        var handler = new SessionEndHandler();
        handler.ProgressAnswer = (_, _) => Problem(HttpStatusCode.Gone, PausedTimeout);
        using var manager = Manager(handler);
        var failures = 0;
        manager.ProgressReportingFailed += _ => Interlocked.Increment(ref failures);
        await manager.StartSessionAsync(123, forceStartPosition: true);

        await handler.FirstProgress.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var timer = typeof(PlaybackManager).GetField("_progressTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (timer.GetValue(manager) != null && DateTime.UtcNow < deadline) await Task.Delay(25);

        // The ended session's reporting stops on the first 410; it is never treated as a lost connection.
        Assert.Null(timer.GetValue(manager));
        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.Equal(0, (int)typeof(PlaybackManager).GetField("_consecutiveProgressFailures", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!);
        Assert.NotNull(typeof(PlaybackManager).GetEvent("SessionEnded"));
    }

    // ---- The rules ----

    [Theory]
    [InlineData(410, PausedTimeout, null, "PausedTimeout")]
    [InlineData(410, Ended, null, "Ended")]
    [InlineData(410, "unknown", PausedTimeout, "PausedTimeout")]
    [InlineData(410, "unknown", Ended, "Ended")]
    [InlineData(410, "gone", null, null)]
    [InlineData(404, "not_found", null, null)]
    [InlineData(404, PausedTimeout, null, null)]
    [InlineData(409, Ended, null, null)]
    [InlineData(503, "dependency_unavailable", null, null)]
    public void OnlyA410NamingAnEndedSessionEndsIt(int status, string code, string? problemType, string? expected)
    {
        var error = new ApiException(code, "Synthetic refusal.", status) { ProblemType = problemType };
        Assert.Equal(expected, PlaybackSessionEndRules.Classify(error)?.ToString());
    }

    [Fact]
    public void ANetworkFailureOrTimeoutIsNotAnEnd()
    {
        Assert.Null(PlaybackSessionEndRules.Classify(new HttpRequestException("Synthetic network failure.")));
        Assert.Null(PlaybackSessionEndRules.Classify(new TaskCanceledException()));
        Assert.Null(PlaybackSessionEndRules.Classify(null));
    }

    [Fact]
    public void OnlyThePausedTimeoutsTicketAnswerMakesThePlayerLeave()
    {
        Assert.True(PlaybackSessionEndRules.TicketRefusalEndsPlayback(new ApiException(PausedTimeout, "x", 410)));
        // Every stopped attempt answers playback_session_ended to a mint, an expiry included: it only stops minting.
        Assert.False(PlaybackSessionEndRules.TicketRefusalEndsPlayback(new ApiException(Ended, "x", 410)));
        Assert.True(PlaybackControlRules.TicketRefusalEndsControl(410));
        Assert.False(PlaybackSessionEndRules.TicketRefusalEndsPlayback(new ApiException(PausedTimeout, "x", 404)));
        Assert.False(PlaybackSessionEndRules.TicketRefusalEndsPlayback(new HttpRequestException("Synthetic network failure.")));
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void OnlyAVideoWatchedHereAdvertisesTheFeature(bool audiobook, bool castReceiver, bool hasVideo, bool expected) =>
        Assert.Equal(expected, PlaybackSessionEndRules.AdvertisesPausedTimeout(audiobook, castReceiver, hasVideo));

    [Fact]
    public void EachEndKeepsItsOwnNotice()
    {
        Assert.Equal("Playback stopped because it was paused for too long.", PlaybackSessionEndRules.Notice(PlaybackSessionEndReason.PausedTimeout));
        Assert.Equal("Playback ended", PlaybackSessionEndRules.Notice(PlaybackSessionEndReason.Ended));
    }

    // ---- Advertisement ----

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task OnlyAStartWhosePlayerLeavesOnItAdvertisesPausedTimeoutAndEveryReplanRestatesIt(bool leaves, bool serverAdvertises, bool expected)
    {
        var handler = new SessionEndHandler();
        if (!serverAdvertises) handler.ServerFeatures = ["fixed_media_file_v1"];
        using var manager = Manager(handler);
        manager.LeavesOnPausedTimeout = leaves;
        await manager.StartSessionAsync(123, forceStartPosition: true);
        await manager.ReplanSeekAsync(30);

        Assert.Equal(expected, Assert.Single(handler.StartBodies).Contains("\"paused_timeout_v1\"", StringComparison.Ordinal));
        Assert.Equal(expected, Assert.Single(handler.ReplanBodies).Contains("\"paused_timeout_v1\"", StringComparison.Ordinal));
    }

    // ---- Progress, pause flush, replan, ticket mint and recovery ----

    [Theory]
    [InlineData(PausedTimeout, PlaybackSessionEndReason.PausedTimeout, "Playback stopped because it was paused for too long.")]
    [InlineData(Ended, PlaybackSessionEndReason.Ended, "Playback ended")] // Generic 410 without an admin marker.
    public async Task APeriodicReportThatLearnsTheEndRaisesItOnceAndNeverRecovers(string code, PlaybackSessionEndReason expected, string expectedNotice)
    {
        var handler = new SessionEndHandler { ProgressAnswer = (_, _) => Problem(HttpStatusCode.Gone, code) };
        using var manager = Manager(handler);
        var ends = new ConcurrentQueue<(string, PlaybackSessionEndReason)>();
        var failures = 0;
        manager.SessionEnded += (session, reason) => ends.Enqueue((session, reason));
        manager.ProgressReportingFailed += _ => Interlocked.Increment(ref failures);
        await manager.StartSessionAsync(123, forceStartPosition: true);

        await WaitUntil(() => !ends.IsEmpty);
        var end = Assert.Single(ends);
        Assert.Equal(("session-1", expected), end);
        Assert.Equal(expectedNotice, PlaybackSessionEndRules.Notice(end.Item2));
        Assert.True(manager.CurrentSessionEnded);
        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.Null(ProgressTimer(manager));
    }

    [Fact]
    public async Task APauseFlushThatLearnsThePausedTimeoutEndsTheSessionAndStopsNothingElse()
    {
        var handler = new SessionEndHandler { ProgressAnswer = (_, _) => Problem(HttpStatusCode.Gone, PausedTimeout) };
        using var manager = Manager(handler);
        var ends = new ConcurrentQueue<(string, PlaybackSessionEndReason)>();
        manager.SessionEnded += (session, reason) => ends.Enqueue((session, reason));
        await manager.StartSessionAsync(123, forceStartPosition: true);

        var error = await Assert.ThrowsAsync<PlaybackSessionEndedException>(() => manager.ReportProgressNowAsync(42, isPaused: true));
        Assert.Equal(("session-1", PlaybackSessionEndReason.PausedTimeout), (error.SessionId, error.Reason));
        // A second report for the same session answers the same way, without a second notice or a re-armed retry.
        await Assert.ThrowsAsync<PlaybackSessionEndedException>(() => manager.ReportProgressNowAsync(42, isPaused: true));
        Assert.Equal(("session-1", PlaybackSessionEndReason.PausedTimeout), Assert.Single(ends));
        Assert.Equal(0, (int)Field(manager, "_immediateProgressPending")!);
        Assert.Null(ProgressTimer(manager));

        // Leaving keeps the server's saved resume position: no final progress or stop goes to the ended session.
        var posts = handler.ProgressSessions.Count;
        await manager.StopSessionAsync(42, isPaused: true);
        Assert.Equal(posts, handler.ProgressSessions.Count);
        Assert.Empty(handler.Deletes);
        Assert.Null(manager.SessionId);
    }

    [Fact]
    public async Task AReplanThatLearnsThePausedTimeoutEndsTheSessionAndNeverStartsAFreshAttempt()
    {
        var handler = new SessionEndHandler { ReplanAnswer = _ => Problem(HttpStatusCode.Gone, PausedTimeout) };
        using var manager = Manager(handler);
        var ends = new ConcurrentQueue<(string, PlaybackSessionEndReason)>();
        manager.SessionEnded += (session, reason) => ends.Enqueue((session, reason));
        await manager.StartSessionAsync(123, forceStartPosition: true);

        var error = await Assert.ThrowsAsync<PlaybackSessionEndedException>(() => manager.ReplanSeekAsync(30));
        Assert.Equal(PlaybackSessionEndReason.PausedTimeout, error.Reason);
        // A media failure on this HLS plan recovers through a replan, which reaches the same answer (protocol v3 §6.3).
        await Assert.ThrowsAsync<PlaybackSessionEndedException>(() => manager.RecoverPlaybackFailureAsync(30, "playback_interrupted", "playback-error"));
        Assert.Equal(("session-1", PlaybackSessionEndReason.PausedTimeout), Assert.Single(ends));
        Assert.Single(handler.StartBodies);
        Assert.True(manager.CurrentSessionEnded);
    }

    [Theory]
    [InlineData(PausedTimeout, true)]
    [InlineData(Ended, false)]
    public async Task AControlTicketMintEndsPlaybackOnlyForThePausedTimeout(string code, bool leaves)
    {
        var handler = new SessionEndHandler { TicketAnswer = _ => Problem(HttpStatusCode.Gone, code) };
        var (manager, playbackApi) = ManagerWithApi(handler);
        using (manager)
        {
            var ends = new ConcurrentQueue<(string, PlaybackSessionEndReason)>();
            manager.SessionEnded += (session, reason) => ends.Enqueue((session, reason));
            await manager.StartSessionAsync(123, forceStartPosition: true);

            var refusal = await Assert.ThrowsAsync<ApiException>(() => playbackApi.CreateControlTicketAsync("session-1"));
            Assert.True(PlaybackControlRules.TicketRefusalEndsControl(refusal.StatusCode));
            Assert.Equal(leaves, PlaybackSessionEndRules.TicketRefusalEndsPlayback(refusal));
            // The player hands a paused-timeout mint to the manager, which ends the session as a progress 410 would.
            if (PlaybackSessionEndRules.TicketRefusalEndsPlayback(refusal)) manager.ReportServerEnd("session-1", PlaybackSessionEndReason.PausedTimeout);
            Assert.Equal(leaves, manager.CurrentSessionEnded);
            Assert.Equal(leaves ? 1 : 0, ends.Count);
            Assert.Equal(leaves, ProgressTimer(manager) == null);
        }
    }

    [Fact]
    public async Task ARecoveryThatWouldReopenWithoutAskingChecksTheSessionFirst()
    {
        var handler = new SessionEndHandler();
        using var manager = Manager(handler);
        var ends = new ConcurrentQueue<(string, PlaybackSessionEndReason)>();
        manager.SessionEnded += (session, reason) => ends.Enqueue((session, reason));
        await manager.StartSessionAsync(123, forceStartPosition: true);

        // A restarted server's 404 and an unreachable server both let recovery go on.
        handler.ProgressAnswer = (_, _) => Problem(HttpStatusCode.NotFound, "not_found");
        await manager.ConfirmSessionAsync();
        handler.ProgressAnswer = (_, _) => null;
        await manager.ConfirmSessionAsync();
        Assert.Empty(ends);

        handler.ProgressAnswer = (_, _) => Problem(HttpStatusCode.Gone, PausedTimeout);
        var error = await Assert.ThrowsAsync<PlaybackSessionEndedException>(() => manager.ConfirmSessionAsync());
        Assert.Equal(PlaybackSessionEndReason.PausedTimeout, error.Reason);
        Assert.Equal(("session-1", PlaybackSessionEndReason.PausedTimeout), Assert.Single(ends));
    }

    [Fact]
    public async Task ANetworkFailureKeepsTheNormalRetry()
    {
        var handler = new SessionEndHandler { ProgressAnswer = (_, _) => null };
        using var manager = Manager(handler);
        var ends = 0;
        manager.SessionEnded += (_, _) => Interlocked.Increment(ref ends);
        await manager.StartSessionAsync(123, forceStartPosition: true);

        var error = await Record.ExceptionAsync(() => manager.ReportProgressNowAsync(42, isPaused: true));
        Assert.NotNull(error);
        Assert.IsNotType<PlaybackSessionEndedException>(error);
        Assert.Equal(1, (int)Field(manager, "_immediateProgressPending")!);

        // The periodic report counts the failure toward the three that start recovery, and keeps reporting.
        await WaitUntil(() => (int)Field(manager, "_consecutiveProgressFailures")! >= 1);
        Assert.NotNull(ProgressTimer(manager));
        Assert.False(manager.CurrentSessionEnded);
        Assert.Equal(0, Volatile.Read(ref ends));
    }

    [Fact]
    public async Task ALateAnswerForAReplacedSessionNeverEndsTheReplacement()
    {
        var handler = new SessionEndHandler
        {
            ProgressAnswer = (session, _) => session == "session-1" ? Problem(HttpStatusCode.Gone, PausedTimeout) : Json("{\"outcome\":\"applied\"}"),
        };
        using var manager = Manager(handler);
        var ends = 0;
        manager.SessionEnded += (_, _) => Interlocked.Increment(ref ends);
        await manager.StartSessionAsync(123, forceStartPosition: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.HoldSession = "session-1";
        handler.HoldProgress = release;

        var flush = manager.ReportProgressNowAsync(10, isPaused: true);
        await handler.ProgressHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var replacement = await manager.StartReplacementSessionAsync(123, 10);
        Assert.Equal("session-2", replacement.SessionId);
        release.TrySetResult();

        // The replaced session's own request still learns its end, but the new session plays on.
        await Assert.ThrowsAsync<PlaybackSessionEndedException>(() => flush);
        Assert.Equal("session-2", manager.SessionId);
        Assert.False(manager.CurrentSessionEnded);
        Assert.NotNull(ProgressTimer(manager));
        Assert.Equal(0, Volatile.Read(ref ends));
    }

    // ---- The player's wiring (WinUI, read from source) ----

    [Fact]
    public void ThePlayerAdvertisesOnlyForVideoWatchedHereAndLeavesFromEveryPath()
    {
        var player = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        Assert.Contains("_playbackManager.LeavesOnPausedTimeout = PlaybackSessionEndRules.AdvertisesPausedTimeout(IsAudiobook, HasRemoteAuthority,", player);
        // Every manager the player retires drops the end handler with the keepalive handler.
        Assert.Equal(CountOf(player, "ProgressReportingFailed -= OnProgressReportingFailed;"), CountOf(player, "SessionEnded -= OnPlaybackSessionEnded;"));
        Assert.Contains("_playbackManager.SessionEnded += OnPlaybackSessionEnded;", player);
        Assert.Contains("socket.SessionEnded += OnControlSessionEnded;", player);
        Assert.Contains("_webSocket.SessionEnded -= OnControlSessionEnded;", player);
        // The leave closes from any state; recovery checks the session before it reopens or restarts.
        var leave = player[player.IndexOf("private void LeaveServerEndedSession", StringComparison.Ordinal)..];
        leave = leave[..leave.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Assert.Contains("!string.Equals(manager.SessionId, sessionId, StringComparison.Ordinal)", leave);
        Assert.Contains("PlaybackSessionEndRules.Notice(reason)", leave);
        Assert.Contains("_ = CloseAsync();", leave);
        Assert.DoesNotContain("PlayerState.Minimized", leave);
        Assert.Contains("await manager.ConfirmSessionAsync(ct).ConfigureAwait(false);", player);
        var socket = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlaybackWebSocket.cs"));
        Assert.Contains("PlaybackSessionEndRules.TicketRefusalEndsPlayback(ex)", socket);
    }

    // ---- Fixture ----

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static object? Field(PlaybackManager manager, string name) =>
        typeof(PlaybackManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager);

    private static object? ProgressTimer(PlaybackManager manager) => Field(manager, "_progressTimer");

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not reached in time.");
            await Task.Delay(25);
        }
    }

    private static PlaybackManager Manager(SessionEndHandler handler) => ManagerWithApi(handler).Manager;

    private static (PlaybackManager Manager, PlaybackApi Api) ManagerWithApi(SessionEndHandler handler)
    {
        var api = new SiloApiClient(new HttpClient(handler));
        api.SetBaseUrl("https://example.test");
        var playbackApi = new PlaybackApi(api);
        return (new PlaybackManager(playbackApi, new CatalogApi(api), new AuthService(api, new AuthApi(api)), api), playbackApi);
    }

    internal static HttpResponseMessage Problem(HttpStatusCode status, string type) => new(status)
    {
        Content = new StringContent(
            "{\"type\":\"https://siloserver.org/docs/api/v2/problems/" + type + "\",\"title\":\"Playback session ended\",\"status\":" +
            (int)status + ",\"detail\":\"The playback session has ended.\",\"instance\":\"urn:silo:request:1\"}",
            System.Text.Encoding.UTF8, "application/problem+json"),
    };

    internal static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    internal sealed class SessionEndHandler : HttpMessageHandler
    {
        private int _starts;
        public string[] ServerFeatures { get; set; } = ["fixed_media_file_v1", "paused_timeout_v1"];
        public ConcurrentQueue<string> StartBodies { get; } = new();
        public ConcurrentQueue<string> ReplanBodies { get; } = new();
        public ConcurrentQueue<string> ProgressSessions { get; } = new();
        public ConcurrentQueue<string> Deletes { get; } = new();
        public TaskCompletionSource FirstProgress { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>The answer for a progress post: (session, call number) to a response, or null to throw a network failure.</summary>
        public Func<string, int, HttpResponseMessage?> ProgressAnswer { get; set; } = (_, _) => Json("{\"outcome\":\"applied\"}");
        public Func<string, HttpResponseMessage>? ReplanAnswer { get; set; }
        public Func<string, HttpResponseMessage>? TicketAnswer { get; set; }
        /// <summary>Held before a progress answer is returned, so a test can replace the session meanwhile.</summary>
        public TaskCompletionSource? HoldProgress { get; set; }
        public string? HoldSession { get; set; }
        public TaskCompletionSource ProgressHeld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _progressCalls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v2/playback/capabilities")
                return Json("{\"allowed\":true,\"state\":\"available\",\"installation_id\":\"11111111-1111-4111-8111-111111111111\",\"protocol_versions\":[3],\"features\":[" +
                    string.Join(",", ServerFeatures.Select(feature => "\"" + feature + "\"")) + "]}");
            if (request.Method == HttpMethod.Post && path == "/api/v2/playback/start")
            {
                StartBodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json(Decision("session-" + Interlocked.Increment(ref _starts)));
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/progress", StringComparison.Ordinal))
            {
                var session = path.Split('/')[4];
                ProgressSessions.Enqueue(session);
                FirstProgress.TrySetResult();
                var call = Interlocked.Increment(ref _progressCalls);
                if (HoldProgress is { } hold && (HoldSession == null || HoldSession == session)) { ProgressHeld.TrySetResult(); await hold.Task.WaitAsync(cancellationToken); }
                return ProgressAnswer(session, call) ?? throw new HttpRequestException("Synthetic network failure.");
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/replan", StringComparison.Ordinal))
            {
                ReplanBodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
                var session = path.Split('/')[4];
                return ReplanAnswer?.Invoke(session) ?? Json(Decision(session));
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/control/ws-ticket", StringComparison.Ordinal))
                return TicketAnswer?.Invoke(path.Split('/')[5]) ?? Json("{\"protocol\":\"silo.playback-control.v2\",\"ticket\":\"fixture\"}");
            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v2/playback/session-", StringComparison.Ordinal))
            {
                Deletes.Enqueue(path.Split('/')[4]);
                return Json("{\"outcome\":\"stopped\"}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string Decision(string sessionId) => $$"""
            {
              "protocol_version": 3,
              "server_features": ["playback_plan_v3", "plan_source_duration_v1"],
              "outcome": "playable",
              "session_id": "{{sessionId}}",
              "playback_plan": {
                "protocol_version": 3,
                "plan_id": "plan:{{sessionId}}",
                "plan_attempt_key": "v3:{{sessionId}}",
                "session_id": "{{sessionId}}",
                "delivery": "server_transcode_hls",
                "stream": { "url": "/stream/{{sessionId}}/master.m3u8", "protocol": "hls", "headers": {} },
                "timeline": { "source_start_seconds": 0, "stream_origin_seconds": 0, "player_start_seconds": 0, "timeline_offset_seconds": 0, "can_seek_anywhere": true },
                "selected_tracks": { "audio": { "id": "file:123:audio:0", "index": 0 } },
                "effective_recipe": { "video_codec": "h264", "audio_codec": "aac" },
                "subtitle": { "mode": "off", "inventory": [] },
                "available_qualities": [{ "label": "original", "preserves_source": true }],
                "degradation_warnings": [],
                "requested_media_file_id": 123,
                "effective_media_file_id": 123,
                "source": { "media_file_id": 123, "duration_seconds": 3600 }
              }
            }
            """;
    }
}
