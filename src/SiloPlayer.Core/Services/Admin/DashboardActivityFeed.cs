using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Live sessions and active scans as the dashboard reads them.</summary>
public sealed record DashboardActivityFeedSnapshot(
    bool SessionsLoaded,
    string? SessionsError,
    IReadOnlyList<DashboardSession> Sessions,
    bool ScansLoaded,
    IReadOnlyList<DashboardActiveScan> Scans);

/// <summary>
/// The shared activity data the dashboard consumes. The app's single
/// <see cref="ServerActivityStore"/> owns the admin channel lease, the strict
/// session drain and the 5s event coalescing; the dashboard never opens a
/// second subscription or a second sessions read.
/// </summary>
public interface IDashboardActivityFeed
{
    DashboardActivityFeedSnapshot Current { get; }
    /// <summary>Raised after the shared data changes, on any thread.</summary>
    event Action? Changed;
    /// <summary>Declares demand for the shared reads while the dashboard is mounted.</summary>
    IDisposable AcquireReads();
    Task RefreshSessionsAsync();
}

/// <summary>Adapter over the app-lifetime <see cref="ServerActivityStore"/>.</summary>
public sealed class ServerActivityDashboardFeed : IDashboardActivityFeed, IDisposable
{
    private readonly ServerActivityStore _store;
    private readonly object _gate = new();
    private ServerActivitySnapshot? _source;
    private DashboardActivityFeedSnapshot? _mapped;

    public ServerActivityDashboardFeed(ServerActivityStore store)
    {
        _store = store;
        _store.Changed += OnChanged;
    }

    public event Action? Changed;

    public DashboardActivityFeedSnapshot Current
    {
        get
        {
            var source = _store.Current;
            lock (_gate)
            {
                if (!ReferenceEquals(source, _source) || _mapped == null)
                {
                    _source = source;
                    _mapped = Map(source);
                }
                return _mapped;
            }
        }
    }

    public IDisposable AcquireReads() => _store.AcquireReads();
    public Task RefreshSessionsAsync() => _store.RefreshSessionsAsync();

    private void OnChanged(ServerActivitySnapshot snapshot) => Changed?.Invoke();

    public static DashboardActivityFeedSnapshot Map(ServerActivitySnapshot snapshot)
        => new(snapshot.SessionsLoaded, snapshot.SessionsError,
            snapshot.Sessions.Select(MapSession).ToArray(),
            snapshot.ScansLoaded,
            snapshot.Scans.Select(scan => new DashboardActiveScan(scan.Id, scan.LibraryId, scan.Mode, scan.Path, "", scan.Status, null,
                scan.Result?.TotalFiles, scan.Result?.FilesProcessed, scan.Result?.Message)).ToArray());

    public static DashboardSession MapSession(AdminActivitySession session) => new()
    {
        SessionId = session.SessionId,
        UserId = session.UserId,
        Username = session.Username,
        ProfileId = session.ProfileId,
        ProfileName = session.ProfileName,
        MediaFileId = session.MediaFileId,
        MediaTitle = session.MediaTitle,
        MediaType = session.MediaType,
        ContentId = session.ContentId,
        PosterUrl = session.PosterUrl,
        SeriesName = session.SeriesName,
        SeasonNumber = session.SeasonNumber,
        EpisodeNumber = session.EpisodeNumber,
        EpisodeName = session.EpisodeName,
        StartedAt = DateTimeOffset.TryParse(session.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started) ? started : null,
        IsPaused = session.IsPaused,
        HasPlaybackControl = session.HasPlaybackControl,
        PlayMethod = session.PlayMethod,
        EffectivePlayMethod = session.EffectivePlayMethod,
        VideoDecision = session.VideoDecision,
        AudioDecision = session.AudioDecision,
        TranscodeAudio = session.TranscodeAudio,
        IsJellyfinClient = session.IsJellyfinClient,
        ClientLabel = session.ClientLabel,
        ClientName = session.ClientName,
        ClientVersion = session.ClientVersion,
        ClientUserAgent = session.ClientUserAgent,
        StreamLocation = session.StreamLocation,
        RoutingNetworkProvider = session.RoutingNetworkProvider,
        RoutingWorkload = session.RoutingWorkload,
        RoutingExecution = session.RoutingExecution,
        RoutingExecutionNodeId = session.RoutingExecutionNodeId,
        RoutingExecutionNodeName = session.RoutingExecutionNodeName,
        RoutingEgress = session.RoutingEgress,
        RoutingEgressNodeId = session.RoutingEgressNodeId,
        RoutingEgressNodeName = session.RoutingEgressNodeName,
        ReportingNode = session.ReportingNode,
        NodeDisplayName = session.NodeDisplayName,
    };

    public void Dispose()
    {
        _store.Changed -= OnChanged;
        Changed = null;
    }
}
