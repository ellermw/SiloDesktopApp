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
    private string? _sessionId;
    private double _lastReportedPosition;
    private bool _isPaused;
    private readonly SemaphoreSlim _progressGuard = new(1, 1);

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
        var token = _apiClient.AccessToken;

        // The API returns paths like "/stream/{session}" -- prefix with /api/v1 if not already there
        if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
            streamPath = "/api/v1" + streamPath;
        var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
        url = UrlHelper.AppendToken(url, token);
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

    public async Task StopSessionAsync()
    {
        StopProgressReporting();
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
        _progressTimer = new Timer(async _ =>
        {
            var sessionId = _sessionId; // Capture to avoid race with StopSessionAsync
            if (sessionId == null) return;
            if (!_progressGuard.Wait(0)) return; // skip if previous report still in-flight
            try
            {
                await _playbackApi.ReportProgressAsync(sessionId, _lastReportedPosition, _isPaused);
            }
            catch (Exception ex)
            {
                // Log progress failures so we can diagnose session reaping
                try
                {
                    var logPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ContinuumPlayer", "progress_error.txt");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                    System.IO.File.AppendAllText(logPath,
                        $"{DateTime.Now} | session={_sessionId} pos={_lastReportedPosition:F1} paused={_isPaused} err={ex.Message}\n");
                }
                catch { }
            }
            finally
            {
                _progressGuard.Release();
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7));
    }

    private void StopProgressReporting()
    {
        _progressTimer?.Dispose();
        _progressTimer = null;
    }

    private static void LogToStateTrace(string msg)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "state_trace.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] PlaybackManager: {msg}\n");
        }
        catch { }
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
        _progressGuard.Dispose();
    }
}
