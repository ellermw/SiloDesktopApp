using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Core.Services;

public class PlaybackManager : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;
    private Timer? _progressTimer;
    private string? _sessionId;
    private double _lastReportedPosition;
    private bool _isPaused;

    public PlaybackManager(PlaybackApi playbackApi, AuthService authService, ContinuumApiClient apiClient)
    {
        _playbackApi = playbackApi;
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

    public FileVersion? SelectBestVersion(List<FileVersion> versions, string? qualityPreference = null)
    {
        if (versions.Count == 0) return null;
        var sorted = versions
            .OrderByDescending(v => ResolutionRank(v.Resolution))
            .ThenByDescending(v => v.Hdr)
            .ThenByDescending(v => v.Bitrate)
            .ToList();

        if (qualityPreference != null && qualityPreference != "auto")
        {
            var match = sorted.FirstOrDefault(v =>
                v.Resolution.Equals(qualityPreference, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return sorted.First();
    }

    public async Task<PlaybackStartResponse> StartSessionAsync(int fileId, double startPosition = 0, CancellationToken ct = default)
    {
        var request = new PlaybackStartRequest
        {
            FileId = fileId,
            ProfileId = _authService.SelectedProfileId ?? "",
            StartPosition = startPosition > 0 ? startPosition : null,
        };

        var response = await _playbackApi.StartPlaybackAsync(request, ct);
        _sessionId = response.SessionId;
        CurrentSession = response;

        var baseUrl = _apiClient.BaseUrl;
        var streamPath = response.StreamUrl;
        var token = _apiClient.AccessToken;

        // The API returns paths like "/stream/{session}" -- prefix with /api/v1 if not already there
        if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
            streamPath = "/api/v1" + streamPath;
        var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
        if (token != null)
            url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
        if (response.PlayMethod == "remux" && response.Position > 0)
            url += $"&seek={response.Position:F3}";

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
            if (token != null)
                url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
            return (s, url);
        }).ToList();
    }

    private void StartProgressReporting()
    {
        StopProgressReporting();
        _progressTimer = new Timer(async _ =>
        {
            if (_sessionId == null) return;
            try { await _playbackApi.ReportProgressAsync(_sessionId, _lastReportedPosition, _isPaused); }
            catch { }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7));
    }

    private void StopProgressReporting()
    {
        _progressTimer?.Dispose();
        _progressTimer = null;
    }

    private static int ResolutionRank(string res) => res?.ToLower() switch
    {
        "2160p" or "4k" => 4, "1440p" => 3, "1080p" => 2, "720p" => 1, "480p" => 0, _ => -1
    };

    public void Dispose()
    {
        StopProgressReporting();
        _ = StopSessionAsync();
    }
}
