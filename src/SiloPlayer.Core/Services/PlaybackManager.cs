using SiloPlayer.Core.Api;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public class PlaybackManager : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly SiloApiClient _apiClient;
    private readonly AudioPassthroughCapabilities? _audioPassthrough;
    private Timer? _progressTimer;
    private CancellationTokenSource? _progressStopCts;
    private string? _sessionId;
    private double _lastReportedPosition;
    private bool _isPaused;
    private readonly SemaphoreSlim _progressGuard = new(1, 1);
    private int _consecutiveProgressFailures;

    /// <summary>
    /// Fires when progress reporting has failed 3 consecutive times (network
    /// stall, server-side session reaping, etc.). The server reaps sessions
    /// after ~45s of no progress — once that happens the stream URL 404s and
    /// mpv hangs with an audio buffer loop. Subscribers should surface an
    /// error to the user and tear down playback cleanly.
    /// </summary>
    public event Action<string>? ProgressReportingFailed;

    public PlaybackManager(PlaybackApi playbackApi, CatalogApi catalogApi, AuthService authService, SiloApiClient apiClient, AudioPassthroughCapabilities? audioPassthrough = null)
    {
        _playbackApi = playbackApi;
        _catalogApi = catalogApi;
        _authService = authService;
        _apiClient = apiClient;
        _audioPassthrough = audioPassthrough;
    }

    public string? SessionId => _sessionId;
    public WatchDetailResponse? WatchDetail { get; private set; }
    public PlaybackStartResponse? CurrentSession { get; private set; }
    public string? StreamUrl { get; private set; }

    public async Task<WatchDetailResponse> GetWatchDetailAsync(string contentId, CancellationToken ct = default)
    {
        WatchDetail = await _playbackApi.GetWatchDetailAsync(contentId, ct);
        return WatchDetail;
    }

    public FileVersion? SelectBestVersion(
        List<FileVersion> versions,
        string? qualityPreference = null,
        WatchUserData? userData = null)
    {
        // Delegate to the shared ranker (see VersionRanking.SelectDefaultVersion).
        // Respects last-watched file, quality-preference cap, and best-audio-codec.
        return VersionRanking.SelectDefaultVersion(versions, userData, qualityPreference);
    }

    public FileVersion? SelectBestVariantVersion(
        List<FileVersion> versions,
        List<PlaybackVariant>? playbackVariants,
        string? qualityPreference = null,
        WatchUserData? userData = null,
        string? preferredEditionKey = null)
    {
        return VersionRanking.SelectDefaultPlaybackVariantVersion(
            versions,
            playbackVariants,
            userData,
            qualityPreference,
            preferredEditionKey);
    }

    public async Task<PlaybackStartResponse> StartSessionAsync(
        int fileId,
        double startPosition = 0,
        bool forceStartPosition = false,
        int? audioTrackIndex = null,
        bool forceDirectAudioSelection = false,
        bool disableProgressPersistence = false,
        CancellationToken ct = default)
    {
        // Declare full codec capabilities so the server chooses direct play for HEVC/HDR/lossless
        // audio content. This is the whole point of the native mpv player — without these caps
        // the server falls back to forcing H.264 transcoding for HEVC content.
        var request = new PlaybackStartRequest
        {
            FileId = fileId,
            ProfileId = _authService.SelectedProfileId ?? "",
            // Native mpv can select any embedded audio track without changing
            // the media bytes. The current server only honors
            // PreserveDirectAudioSelection for an explicit direct request.
            PlayMethod = forceDirectAudioSelection && audioTrackIndex.HasValue ? "direct" : null,
            // Send explicit 0 when forceStartPosition is true (play from start).
            // null means "let server restore saved progress".
            StartPosition = forceStartPosition ? startPosition : (startPosition > 0 ? startPosition : null),
            AudioTrackIndex = audioTrackIndex,
            PreserveDirectAudioSelection = true,
            DisableProgressPersistence = disableProgressPersistence,
        };

        // Keep the request synchronized with the exact libmpv/FFmpeg binary
        // shipped in this build. Advertising less forces needless transcodes;
        // advertising more can make the server choose an unplayable stream.
        MpvNativePlaybackCapabilities.ApplyTo(request, _audioPassthrough);

        LogToStateTrace($"StartSession: fileId={fileId}, pos={startPosition}, force={forceStartPosition}, codecs_video=[{string.Join(",", request.CodecsVideo)}], codecs_audio=[{string.Join(",", request.CodecsAudio)}], containers=[{string.Join(",", request.Containers)}], max_res={request.MaxResolution}, hdr={request.Hdr}");

        var response = await _playbackApi.StartPlaybackAsync(request, ct);
        _sessionId = response.SessionId;
        CurrentSession = response;
        _lastReportedPosition = Math.Max(0, response.Position);
        _isPaused = response.IsPaused;

        LogToStateTrace($"StartSession response: play_method={response.PlayMethod}, session={response.SessionId}, position={response.Position:F1}");

        var baseUrl = _apiClient.BaseUrl;
        var streamPath = response.StreamUrl;

        // The API returns paths like "/stream/{session}" -- prefix with /api/v1 if not already there
        if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
            streamPath = "/api/v1" + streamPath;
        var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
        if (response.PlayMethod == "remux" &&
            response.Position > 0 &&
            !PlaybackTransportPlanner.IsHlsStreamUrl(response.StreamUrl))
            url += (url.Contains('?') ? "&" : "?") + "seek=" +
                response.Position.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        StreamUrl = url;
        StartProgressReporting();
        return response;
    }

    /// <summary>
    /// Starts a replacement session before retiring the active one. This mirrors
    /// the current WebUI handoff: the old stream remains usable until the server
    /// has accepted the new session, so version and quality switches do not sit
    /// behind synchronous history/scrobble cleanup on DELETE.
    /// </summary>
    public async Task<PlaybackStartResponse> StartReplacementSessionAsync(
        int fileId,
        double startPosition,
        bool forceStartPosition = true,
        int? audioTrackIndex = null,
        bool forceDirectAudioSelection = false,
        double? previousFinalPosition = null,
        CancellationToken ct = default)
    {
        var previousSessionId = _sessionId;
        var response = await StartSessionAsync(
            fileId,
            startPosition,
            forceStartPosition,
            audioTrackIndex,
            forceDirectAudioSelection,
            ct: ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(previousSessionId) &&
            !string.Equals(previousSessionId, response.SessionId, StringComparison.Ordinal))
        {
            _ = RetireSupersededSessionAsync(
                previousSessionId,
                Math.Max(0, previousFinalPosition ?? startPosition));
        }

        return response;
    }

    private async Task RetireSupersededSessionAsync(string sessionId, double finalPosition)
    {
        if (finalPosition > 0)
        {
            try
            {
                using var progressCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _playbackApi.ReportProgressAsync(
                    sessionId,
                    finalPosition,
                    false,
                    progressCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine(
                    "progress_error.txt",
                    $"Superseded session final progress failed: {ex.Message}");
            }
        }

        try
        {
            // Server stop finalization includes database history, session sync,
            // and provider scrobbling. It can legitimately outlive the visual
            // handoff, so keep it off the playback-start critical path.
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _playbackApi.StopPlaybackAsync(sessionId, stopCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine(
                "progress_error.txt",
                $"Superseded playback session cleanup failed: {ex.Message}");
        }
    }

    public void UpdatePosition(double position, bool isPaused)
    {
        _lastReportedPosition = position;
        _isPaused = isPaused;
    }

    public void ApplyAudioChange(ChangeAudioResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (CurrentSession == null) return;

        CurrentSession.AudioTrackIndex = response.AudioTrackIndex;
        if (!string.IsNullOrWhiteSpace(response.PlayMethod))
            CurrentSession.PlayMethod = response.PlayMethod;
        if (!string.IsNullOrWhiteSpace(response.StreamUrl))
        {
            CurrentSession.StreamUrl = response.StreamUrl;
            var streamPath = response.StreamUrl;
            if (!streamPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !streamPath.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
                streamPath = "/api/v1" + (streamPath.StartsWith('/') ? "" : "/") + streamPath;
            StreamUrl = streamPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? streamPath
                : $"{_apiClient.BaseUrl}{streamPath}";
        }
        if (response.PlaybackInfo != null)
            CurrentSession.PlaybackInfo = response.PlaybackInfo;
    }

    public async Task ReportProgressNowAsync(
        double position,
        bool isPaused,
        CancellationToken ct = default)
    {
        UpdatePosition(position, isPaused);
        await _progressGuard.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessionId = _sessionId;
            if (sessionId == null)
                return;
            await _playbackApi.ReportProgressAsync(
                sessionId,
                _lastReportedPosition,
                _isPaused,
                ct).ConfigureAwait(false);
            _consecutiveProgressFailures = 0;
        }
        finally
        {
            _progressGuard.Release();
        }
    }

    public async Task StopSessionAsync(
        double? finalPosition = null,
        bool? isPaused = null,
        CancellationToken ct = default)
    {
        StopProgressReporting();
        var sessionId = _sessionId;
        if (sessionId == null)
            return;

        var position = Math.Max(0, finalPosition ?? _lastReportedPosition);
        var paused = isPaused ?? _isPaused;
        var guardEntered = false;
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        operationCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await _progressGuard.WaitAsync(operationCts.Token).ConfigureAwait(false);
            guardEntered = true;

            // A newer session may have replaced the captured one while an
            // older progress request was draining. Never tear that session down.
            if (!string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
                return;

            if (position > 0)
            {
                try
                {
                    using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(operationCts.Token);
                    progressCts.CancelAfter(TimeSpan.FromSeconds(3));
                    await _playbackApi.ReportProgressAsync(
                        sessionId,
                        position,
                        paused,
                        progressCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    LocalLog.AppendLine("progress_error.txt", "Final progress report timed out before session stop.");
                }
                catch (Exception ex)
                {
                    LocalLog.AppendLine("progress_error.txt", $"Final progress report failed before session stop: {ex.Message}");
                }
            }

            try
            {
                using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(operationCts.Token);
                stopCts.CancelAfter(TimeSpan.FromSeconds(3));
                await _playbackApi.StopPlaybackAsync(sessionId, stopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LocalLog.AppendLine("progress_error.txt", "Playback stop timed out.");
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine("progress_error.txt", $"Playback stop failed: {ex.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            LocalLog.AppendLine(
                "progress_error.txt",
                "Timed out waiting for the active progress request during playback stop.");
        }
        finally
        {
            if (guardEntered)
                _progressGuard.Release();

            if (string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                _sessionId = null;
                CurrentSession = null;
                StreamUrl = null;
            }
        }

        // Sync progress across devices after stopping. This is best-effort and
        // deliberately detached so a slow server never blocks player teardown.
        _ = SyncProgressAsync();
    }

    /// <summary>
    /// Triggers a cross-device progress sync with the server.
    /// Called after stopping playback to ensure other devices see updated state.
    /// </summary>
    public async Task SyncProgressAsync()
    {
        try
        {
            await _catalogApi.SyncProgressAsync(new { });
        }
        catch
        {
            // Sync failure is non-fatal -- progress was already saved by the stop call
        }
    }

    public List<(SubtitleTrackInfo Track, string FullUrl)> GetSubtitleUrls()
    {
        if (CurrentSession == null) return [];
        var baseUrl = _apiClient.BaseUrl;
        var token = _apiClient.AccessToken;
        return CurrentSession.SubtitleUrls.Select(s =>
        {
            var subPath = s.Url;
            if (!subPath.StartsWith("http") && !subPath.StartsWith("/api/v1"))
                subPath = "/api/v1" + subPath;
            var url = subPath.StartsWith("http") ? subPath : $"{baseUrl}{subPath}";
            url = UrlHelper.AppendToken(url, token);
            return (s, url);
        }).ToList();
    }

    private void StartProgressReporting()
    {
        StopProgressReporting();
        _progressStopCts = new CancellationTokenSource();
        var stopToken = _progressStopCts.Token;
        _consecutiveProgressFailures = 0;
        _progressTimer = new Timer(async _ =>
        {
            var sessionId = _sessionId; // Capture to avoid race with StopSessionAsync
            if (sessionId == null) return;
            if (stopToken.IsCancellationRequested) return;

            if (!_progressGuard.Wait(0)) return; // skip if previous report still in-flight
            try
            {
                // Per-call 10s timeout. Without this the default HttpClient
                // timeout is 100s — long enough for the server to reap the
                // session (45s) and the stream to start 404'ing while we're
                // still hung on one progress POST. Subsequent Wait(0) checks
                // would then silently skip, so we'd never notice.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stopToken);
                await _playbackApi.ReportProgressAsync(sessionId, _lastReportedPosition, _isPaused, linkedCts.Token);
                if (!stopToken.IsCancellationRequested)
                    _consecutiveProgressFailures = 0;
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                // Playback is stopping; the in-flight keepalive was intentionally canceled.
            }
            catch (Exception ex)
            {
                _consecutiveProgressFailures++;

                // Log progress failures so we can diagnose session reaping.
                LocalLog.AppendLine(
                    "progress_error.txt",
                    $"session={_sessionId} pos={_lastReportedPosition:F1} paused={_isPaused} consec={_consecutiveProgressFailures} err={ex.Message}");

                if (_consecutiveProgressFailures >= 3)
                {
                    var msg = ex is OperationCanceledException
                        ? "Playback session timed out — the server stopped responding."
                        : $"Playback session lost contact with the server: {ex.Message}";
                    // Stop the timer first so we don't keep firing after the
                    // subscriber tears the session down.
                    StopProgressReporting();
                    try { ProgressReportingFailed?.Invoke(msg); } catch { }
                }
            }
            finally
            {
                _progressGuard.Release();
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7));
    }

    private void StopProgressReporting()
    {
        var stopCts = Interlocked.Exchange(ref _progressStopCts, null);
        try { stopCts?.Cancel(); } catch (ObjectDisposedException) { }
        stopCts?.Dispose();

        Interlocked.Exchange(ref _progressTimer, null)?.Dispose();
    }

    private static void LogToStateTrace(string msg)
    {
        LocalLog.AppendLine("state_trace.txt", $"PlaybackManager: {msg}");
    }

    public void Dispose()
    {
        StopProgressReporting();
        var sessionId = Interlocked.Exchange(ref _sessionId, null);
        CurrentSession = null;
        StreamUrl = null;
        if (sessionId != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _playbackApi.StopPlaybackAsync(sessionId, cts.Token).ConfigureAwait(false);
                }
                catch { }
            });
        }
    }
}
