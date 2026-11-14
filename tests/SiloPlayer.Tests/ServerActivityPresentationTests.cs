using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Behaviour oracle: upstream web/src/components/ServerActivity.tsx and ServerActivity.test.tsx.</summary>
public sealed class ServerActivityPresentationTests
{
    private static ServerActivitySnapshot Snapshot(
        IReadOnlyList<AdminActivitySession>? sessions = null,
        IReadOnlyList<AdminIndicatorTask>? tasks = null,
        IReadOnlyList<AdminIndicatorScanRun>? scans = null,
        bool scansLoaded = true,
        AdminIndicatorPreparationList? preparations = null,
        AdminRealtimeConnectionState connection = AdminRealtimeConnectionState.Live) =>
        ServerActivitySnapshot.Empty with
        {
            IsActingAdmin = true,
            Sessions = sessions ?? [],
            Tasks = tasks ?? [],
            Scans = scans ?? [],
            ScansLoaded = scansLoaded,
            Preparations = preparations,
            Connection = connection,
            LibraryNames = new Dictionary<string, string> { ["1"] = "Movies" },
        };

    private static AdminIndicatorScanRun Scan(int i, string status = "running") => new($"scan-{i}", "1", "library", null, status, null);

    [Fact]
    public void IdleShowsEmptyStateAndIdleName()
    {
        var view = ServerActivityPresentation.Build(Snapshot());
        Assert.Equal(0, view.TotalActive);
        Assert.True(view.ShowEmptyState);
        Assert.Equal("Server activity", view.AccessibleName);
        Assert.Null(view.ConnectionText);
    }

    [Fact]
    public void EmptyStateWaitsForTheScanSnapshot()
    {
        Assert.False(ServerActivityPresentation.Build(Snapshot(scansLoaded: false)).ShowEmptyState);
    }

    [Fact]
    public void TotalCountsSessionsRunningTasksActiveScansAndActivePreparations()
    {
        var sessions = new[]
        {
            AdminActivityPresentationTests.Session(x => { x.SessionId = "a"; x.PlayMethod = "direct"; x.VideoDecision = "direct"; x.AudioDecision = "direct"; }),
            AdminActivityPresentationTests.Session(x => x.SessionId = "b"),
            AdminActivityPresentationTests.Session(x => { x.SessionId = "c"; x.PlayMethod = "remux"; x.VideoDecision = "copy"; x.AudioDecision = "copy"; }),
        };
        var tasks = new[]
        {
            new AdminIndicatorTask { Key = "hidden", Name = "Hidden worker", State = "running", Progress = 0 },
            new AdminIndicatorTask { Key = "cancelling", Name = "Cancelling", State = "cancelling" },
            new AdminIndicatorTask { Key = "idle", Name = "Idle", State = "idle" },
        };
        var scans = new[] { Scan(1), Scan(2, "accepted"), Scan(3, "completed") };
        var preparations = new AdminIndicatorPreparationList
        {
            Counts = new() { Running = 1, Queued = 2, Retrying = 1, Paused = 3, FailedRecent = 4 },
        };
        var view = ServerActivityPresentation.Build(Snapshot(sessions, tasks, scans, preparations: preparations));

        Assert.Equal(3 + 1 + 2 + 4, view.TotalActive);
        Assert.Equal("10", view.BadgeText);
        Assert.Equal("Server activity: 10 active", view.AccessibleName);
        Assert.Equal(["1 Direct Play", "1 Remux", "1 Transcode"], view.Streams.Select(s => s.Text));
        var task = Assert.Single(view.Tasks);
        Assert.Null(task.Percent); // indeterminate: spinner + Running, never a false 0%
        Assert.Equal(2, view.ScanCount);
        Assert.Equal("Scanning…", view.Scans[0].StatusText);
        Assert.Equal("Queued", view.Scans[1].StatusText);
        Assert.Equal("Full library", view.Scans[0].DetailText);
        Assert.Equal("Movies", view.Scans[0].LibraryName);
    }

    [Fact]
    public void BadgeCapsAt99AndScanSnapshotLimitIsALowerBound()
    {
        var many = Enumerable.Range(0, 120).Select(i => Scan(i)).ToArray();
        var view = ServerActivityPresentation.Build(Snapshot(scans: many));
        Assert.Equal("99+", view.BadgeText);
        Assert.False(view.CountIsLowerBound);
        Assert.Equal(25, view.Scans.Count);
        Assert.Equal("95 more scans queued", view.MoreScansText);

        var limit = Enumerable.Range(0, 500).Select(i => Scan(i)).ToArray();
        var bounded = ServerActivityPresentation.Build(Snapshot(scans: limit));
        Assert.True(bounded.CountIsLowerBound);
        Assert.Equal("99+", bounded.BadgeText);
        Assert.Equal("Server activity: at least 500 active", bounded.AccessibleName);
        Assert.Equal("At least 475 more scans queued", bounded.MoreScansText);

        Assert.Equal("7+", ServerActivityPresentation.FormatBadgeCount(7, lowerBound: true));
        Assert.Equal("99", ServerActivityPresentation.FormatBadgeCount(99));
    }

    [Fact]
    public void DeterminateTaskProgressNeverRoundsUpToFinished()
    {
        Assert.Equal("40%", ServerActivityPresentation.FormatTaskProgress(40));
        Assert.Equal("41.5%", ServerActivityPresentation.FormatTaskProgress(41.5));
        Assert.Equal("99.9%", ServerActivityPresentation.FormatTaskProgress(99.97));
        Assert.Equal("100%", ServerActivityPresentation.FormatTaskProgress(140));
    }

    [Fact]
    public void ScanProgressAndPaths()
    {
        var scan = new AdminIndicatorScanRun("s", "9", "subtree", "/media/tv", "running", new AdminIndicatorScanResult(200, 50, null));
        var view = ServerActivityPresentation.Build(Snapshot(scans: [scan]));
        Assert.Equal("Library #9", view.Scans[0].LibraryName);
        Assert.Equal("Subtree · /media/tv", view.Scans[0].DetailText);
        Assert.Equal("Processing files · 50 / 200 (25%)", view.Scans[0].ProgressText);
        Assert.Equal("phase", ServerActivityPresentation.FormatScanProgress(scan with { Result = new(null, null, "phase") }));
    }

    [Fact]
    public void PreparationsFoldExtraRunningRowsIntoTheWaitingLine()
    {
        AdminIndicatorPreparation Running(int i) => new()
        {
            Id = $"r{i}", State = "running", Format = "transcode", MediaTitle = $"Movie {i}", MediaType = "movie", MediaFileId = $"{i}",
            Output = new() { VideoCodec = "h264", Resolution = "1080p" },
            Progress = new() { EncodedSeconds = 30, DurationSeconds = 120, Speed = 1 },
            Worker = new() { Kind = "node", Name = "gpu-1" },
        };
        var list = new AdminIndicatorPreparationList
        {
            Counts = new() { Running = 5, Queued = 2, Retrying = 1, Paused = 1, FailedRecent = 1 },
            Items = Enumerable.Range(0, 5).Select(Running).ToList(),
        };
        var prep = ServerActivityPresentation.Build(Snapshot(preparations: list)).Preparations!;
        Assert.Equal(8, prep.ActiveCount);
        Assert.Equal(3, prep.Running.Count);
        Assert.Equal("25%", prep.Running[0].PercentText);
        Assert.Equal("Transcode 1080p H.264 · gpu-1 · about 2 min left", prep.Running[0].DetailText);
        Assert.Equal("2 more running · 2 queued · 1 waiting to retry", prep.WaitingText);
        Assert.Equal("1 paused", prep.PausedText);
        Assert.Equal("1 failed in the last 24 hours", prep.FailedText);
        Assert.Null(prep.EmptyText);
    }

    [Fact]
    public void RecentFailuresStayVisibleWhileIdleAndUnavailableQueueHidesTheSection()
    {
        var failedOnly = new AdminIndicatorPreparationList { Counts = new() { FailedRecent = 2 } };
        var view = ServerActivityPresentation.Build(Snapshot(preparations: failedOnly));
        Assert.Equal(0, view.TotalActive);
        Assert.False(view.ShowEmptyState);
        Assert.Equal("2 failed in the last 24 hours", view.Preparations!.FailedText);

        var empty = ServerActivityPresentation.Build(Snapshot(preparations: new AdminIndicatorPreparationList()));
        Assert.Equal("No downloads being prepared", empty.Preparations!.EmptyText);
        Assert.Null(ServerActivityPresentation.Build(Snapshot(preparations: null)).Preparations);
    }

    [Fact]
    public void EpisodePreparationsUseCompactTitlesAndIndeterminateLabels()
    {
        var p = new AdminIndicatorPreparation { Id = "e", State = "running", Format = "remux", SeriesName = "Severance", SeasonNumber = 2, EpisodeNumber = 1, MediaFileId = "5" };
        Assert.Equal("Severance S02E01", ServerActivityPresentation.PreparationCompactTitle(p));
        var view = ServerActivityPresentation.Build(Snapshot(preparations: new() { Counts = new() { Running = 1 }, Items = [p] }));
        Assert.Null(view.Preparations!.Running[0].Percent);
        Assert.Equal("Remuxing", view.Preparations.Running[0].RunningLabel);
        Assert.Equal("about 2 h left", ServerActivityPresentation.FormatRemaining(7200));
        Assert.Equal("about 1 h 5 min left", ServerActivityPresentation.FormatRemaining(3900));
        Assert.Equal("under 1 min left", ServerActivityPresentation.FormatRemaining(30));
    }

    [Fact]
    public void ConnectionHeaderReportsConnectingAndDisconnected()
    {
        Assert.Equal("Connecting…", ServerActivityPresentation.Build(Snapshot(connection: AdminRealtimeConnectionState.Connecting)).ConnectionText);
        Assert.Equal("Disconnected", ServerActivityPresentation.Build(Snapshot(connection: AdminRealtimeConnectionState.Disconnected)).ConnectionText);
    }
}
