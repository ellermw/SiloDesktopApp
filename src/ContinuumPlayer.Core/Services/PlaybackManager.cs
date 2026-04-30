using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Helpers;
using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Core.Services;

public class PlaybackManager : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;
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

    public PlaybackManager(PlaybackApi playbackApi, CatalogApi catalogApi, AuthService authService, ContinuumApiClient apiClient)
    {
        _playbackApi = playbackApi;
        _catalogApi = catalogApi;
        _authService = authService;
        _apiClient = apiClient;
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

    public async Task<PlaybackStartResponse> StartSessionAsync(int fileId, double startPosition = 0, bool forceStartPosition = false, int? audioTrackIndex = null, CancellationToken ct = default)
    {
        // Declare full codec capabilities so the server chooses direct play for HEVC/HDR/lossless
        // audio content. This is the whole point of the native mpv player — without these caps
        // the server falls back to forcing H.264 transcoding for HEVC content.
        // (Per CLAUDE.md lines 223-230: containers=[mp4,mkv], 4K HDR, all audio codecs.)
        var request = new PlaybackStartRequest
        {
            FileId = fileId,
            ProfileId = _authService.SelectedProfileId ?? "",
            // Send explicit 0 when forceStartPosition is true (play from start).
            // null means "let server restore saved progress".
            StartPosition = forceStartPosition ? startPosition : (startPosition > 0 ? startPosition : null),
            AudioTrackIndex = audioTrackIndex,
            CodecsVideo = ["h264", "hevc", "av1", "vp9"],
            CodecsAudio = ["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"],
            Containers = ["mp4", "mkv"],
            MaxResolution = "2160p",
            Hdr = true,
        };

        LogToStateTrace($"StartSession: fileId={fileId}, pos={startPosition}, force={forceStartPosition}, codecs_video=[{string.Join(",", request.CodecsVideo)}], codecs_audio=[{string.Join(",", request.CodecsAudio)}], containers=[{string.Join(",", request.Containers)}], max_res={request.MaxResolution}, hdr={request.Hdr}");

        var response = await _playbackApi.StartPlaybackAsync(request, ct);
        _sessionId = response.SessionId;
        CurrentSession = response;

        LogToStateTrace($"StartSession response: play_method={response.PlayMethod}, session={response.SessionId}, position={response.Position:F1}");

        var baseUrl = _apiClient.BaseUrl;
        var streamPath = response.StreamUrl;

        // The API returns paths like "/stream/{session}" -- prefix with /api/v1 if not already there
        if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
            streamPath = "/api/v1" + streamPath;
        var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
        if (response.PlayMethod == "remux" && response.Position > 0)
            url += (url.Contains('?') ? "&" : "?") + $"seek={response.Position:F3}";

        StreamUrl = url;
        StartProgressReporting();
        return response;
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
            CurrentSession.StreamUrl = response.StreamUrl;
        if (response.PlaybackInfo != null)
            CurrentSession.PlaybackInfo = response.PlaybackInfo;
    }

    public async Task StopSessionAsync()
    {
        await StopProgressReportingAsync();
        if (_sessionId != null)
        {
            try { await _playbackApi.StopPlaybackAsync(_sessionId); }
            catch { }
            _sessionId = null;
            CurrentSession = null;
            StreamUrl = null;

            // Sync progress across devices after stopping
            _ = SyncProgressAsync();
        }
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
        _progressStopCts?.Cancel();
        _progressTimer?.Dispose();
        _progressTimer = null;
    }

    private async Task StopProgressReportingAsync()
    {
        StopProgressReporting();

        try
        {
            using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _progressGuard.WaitAsync(drainCts.Token).ConfigureAwait(false);
            _progressGuard.Release();
        }
        catch (OperationCanceledException)
        {
            LocalLog.AppendLine("progress_error.txt", "Timed out waiting for in-flight progress report to stop.");
        }

        _progressStopCts?.Dispose();
        _progressStopCts = null;
    }

    private static void LogToStateTrace(string msg)
    {
        LocalLog.AppendLine("state_trace.txt", $"PlaybackManager: {msg}");
    }

    public void Dispose()
    {
        StopProgressReporting();
        if (_sessionId != null)
        {
            _ = Task.Run(async () =>
            {
                try { await _playbackApi.StopPlaybackAsync(_sessionId); }
                catch { }
            });
        }
    }
}
