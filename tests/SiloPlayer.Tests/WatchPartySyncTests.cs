using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class WatchPartySyncTests
{
    [Theory]
    [InlineData(.39, 1)]
    [InlineData(.45, 1.02)]
    [InlineData(.625, 1.025)]
    [InlineData(1, 1.04)]
    [InlineData(5, 1.04)]
    [InlineData(-.625, .975)]
    [InlineData(-5, .96)]
    public void PlayingCommandUsesGentleQuantizedRateEvenWhenBuffered(double drift, double rate)
    {
        var f = new Fixture();
        f.Player.Paused = false;
        f.Player.Position = 100 - drift;
        f.Sync.QueueCommand(f.Command("play", 100), 0);
        f.Sync.Tick(0);
        Assert.Empty(f.Player.Seeks);
        Assert.Equal(rate, f.Player.Rate, 6);
    }

    [Fact]
    public void LatePlayingCommandProjectsFromScheduledTimeRatherThanArrival()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("play", 100), 1000);
        f.Sync.Tick(3500);
        Assert.Equal(102.5, Assert.Single(f.Player.Seeks));
        f.Player.Position = 102.5;
        f.Sync.Tick(5000);
        Assert.Contains(f.Messages, m => m.Type == "state_report" && m.CommandId == "c1");
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("seek")]
    public void LatePausedCommandDoesNotAdvanceAndKeepsPause(string action)
    {
        var f = new Fixture();
        var command = f.Command(action, 100);
        command.PlaybackState = "paused";
        f.Sync.QueueCommand(command, 0);
        f.Sync.Tick(5000);
        Assert.Equal(100, Assert.Single(f.Player.Seeks));
        Assert.True(f.Player.Paused);
    }

    [Fact]
    public void ContinuousNudgeHasHysteresisAndNeverSeeksEvenBeyondFiveSeconds()
    {
        var f = new Fixture();
        f.Player.Paused = false;
        f.Player.Position = 100;
        f.Sync.QueueCommand(f.Command("play", 100), 0);
        f.Sync.Tick(0);
        f.Player.Position = 100.5; // room at101: start a2% nudge
        f.Sync.Tick(1000);
        Assert.Equal(1.02, f.Player.Rate);
        f.Player.Position = 101.75; // room at102: keep nudging inside start band
        f.Sync.Tick(2000);
        Assert.Equal(1.01, f.Player.Rate);
        f.Player.Position = 102.95; // room at103: settled
        f.Sync.Tick(3000);
        Assert.Equal(1, f.Player.Rate);
        f.Player.Position = 103.7; // room at104: do not restart below0.4
        f.Sync.Tick(4000);
        Assert.Equal(1, f.Player.Rate);
        f.Sync.Tick(40_000); // no20s timeout, and no periodic reanchor
        Assert.Equal(1.04, f.Player.Rate);
        Assert.Empty(f.Player.Seeks);
    }

    [Fact]
    public void PendingSeekAdoptsNearbyCommandButDifferentBurstIntentSurvives()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("seek", 100), 0);
        f.Sync.Tick(0);
        f.Player.Busy = true;
        f.Sync.QueueCommand(f.Command("seek", 100.5, "same-target"), 100);
        f.Sync.Tick(100);
        f.Player.Busy = false;
        f.Sync.Tick(200);
        Assert.Single(f.Player.Seeks);
        f.Sync.QueueCommand(f.Command("seek", 200, "new-target"), 300);
        f.Sync.Tick(300);
        Assert.Equal(new[] { 100d, 200d }, f.Player.Seeks);
        f.Player.Position = 200;
        f.Sync.Tick(1800);
        Assert.Contains(f.Messages, m => m.Type == "state_report" && m.CommandId == "new-target");
    }

    [Fact]
    public void ReplacementSessionClearsReferenceAndReportedCommand()
    {
        var f = new Fixture();
        f.Player.Paused = false;
        f.Player.Position = 99;
        f.Sync.QueueCommand(f.Command("play", 100), 0);
        f.Sync.Tick(0);
        f.Sync.SetSession("replacement");
        f.Sync.Tick(5000);
        Assert.Equal(1, f.Player.Rate);
        Assert.DoesNotContain(f.Messages, m => m.Type == "state_report" && m.CommandId == "c1");
        Assert.Empty(f.Player.Seeks);
    }

    [Fact]
    public void PlayAdoptingUnfinishedSeekDoesNotReportAppliedIdBeforeLanding()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("seek", 100), 0);
        f.Sync.Tick(0);
        f.Player.Position = 97.5;
        f.Sync.QueueCommand(f.Command("play", 100, "adopted"), 100);
        f.Sync.Tick(100);
        f.Sync.Tick(1600);
        Assert.Contains(f.Messages, m => m.Type == "state_report" && m.CommandId == null);
        f.Player.Position = 100;
        f.Sync.Tick(3100);
        Assert.Contains(f.Messages, m => m.Type == "state_report" && m.CommandId == "adopted");
    }

    [Fact]
    public void RelativeSeekBurstBuildsOnNewestRequestInsteadOfNativePosition()
    {
        var f = new Fixture(host: true, controls: true);
        f.Player.Position = 100;
        Assert.True(f.Sync.RequestTransport("seek_relative", 10, true));
        Assert.True(f.Sync.RequestTransport("seek_relative", 10, true));
        Assert.Equal(new[] { 110d, 120d }, f.Messages.Where(m => m.Type == "transport_request").Select(m => m.PositionSeconds));
        // Earlier reply must not release newest intent before it executes.
        f.Sync.QueueCommand(f.Command("seek", 110, "earlier"), 0);
        f.Sync.Tick(0);
        Assert.True(f.Sync.RequestTransport("seek_relative", 10, true));
        Assert.Equal(130, f.Messages.Last(m => m.Type == "transport_request").PositionSeconds);
        f.Player.Position = 110; // the earlier command finally landed; newest request was unanswered
        f.Sync.Tick(5001);
        Assert.True(f.Sync.RequestTransport("seek_relative", 10, true));
        Assert.Equal(120, f.Messages.Last(m => m.Type == "transport_request").PositionSeconds);
    }

    [Fact]
    public void RelativePressAfterMatchingCommandUsesItsNativeSeekStillInFlight()
    {
        var f = new Fixture(host: true);
        f.Player.Position = 100;
        f.Sync.RequestTransport("seek_relative", 10, true);
        f.Sync.QueueCommand(f.Command("seek", 110), 0);
        f.Sync.Tick(0); // native seek was issued but fake media has not landed
        f.Sync.RequestTransport("seek_relative", 10, true);
        Assert.Equal(new[] { 110d, 120d }, f.Messages.Where(m => m.Type == "transport_request").Select(m => m.PositionSeconds));
        Assert.Equal(100, f.Player.Position);
    }

    [Fact]
    public void ClockSampleAfterQueueRebasesExecutionAndAppliedReference()
    {
        var f = new Fixture();
        var clock = new WatchPartyRoomClock();
        f.Player.Paused = false;
        f.Player.Position = 100;
        var command = f.Command("play", 100);
        command.ExecuteAt = DateTimeOffset.UnixEpoch.AddSeconds(2).ToString("O");
        f.Sync.QueueCommand(command, clock, DateTimeOffset.UnixEpoch, 0);
        f.Sync.Tick(0);
        clock.RecordSample(0, 2010, 2010, 20); // server2s ahead: command is due now
        f.Sync.Tick(0);
        f.Player.Position = 101;
        f.Sync.Tick(1000);
        Assert.Equal(1, f.Player.Rate);
        clock.RecordSample(0, 3010, 3010, 20); // clock step: reference now one second later
        f.Sync.Tick(1500);
        Assert.Equal(1.04, f.Player.Rate);
    }

    [Fact]
    public void LocalWallClockStepAndNewPongCancelOutForAppliedRoomPosition()
    {
        var f = new Fixture();
        var clock = new WatchPartyRoomClock();
        clock.RecordSample(0, 2010, 2010, 20);
        var command = f.Command("play", 100);
        command.ExecuteAt = DateTimeOffset.UnixEpoch.AddSeconds(2).ToString("O");
        f.Player.Paused = false; f.Player.Position = 100;
        f.Sync.QueueCommand(command, clock, DateTimeOffset.UnixEpoch, 0);
        f.Sync.Tick(0, DateTimeOffset.UnixEpoch);
        // One real second later the local wall clock jumps forward five seconds.
        clock.RecordSample(6000, 3010, 3010, 6020);
        f.Player.Position = 101;
        f.Sync.Tick(1000, DateTimeOffset.UnixEpoch.AddSeconds(6));
        Assert.Equal(1, f.Player.Rate);
        Assert.Empty(f.Player.Seeks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public void MissingExecutionTimestampFallsBackOnceAndStillAdvances(string? timestamp)
    {
        var f = new Fixture();
        var command = f.Command("play", 100); command.ExecuteAt = timestamp;
        f.Player.Paused = false; f.Player.Position = 100;
        f.Sync.QueueCommand(command, new WatchPartyRoomClock(), DateTimeOffset.UnixEpoch, 0);
        f.Sync.Tick(0, DateTimeOffset.UnixEpoch);
        f.Sync.Tick(1000, DateTimeOffset.UnixEpoch.AddSeconds(1));
        Assert.Equal(1.04, f.Player.Rate);
    }

    [Fact]
    public void SeekLandingInsideReadinessToleranceReleasesPendingReportAndNudge()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("seek", 100), 0);
        f.Sync.Tick(0);
        f.Player.Position = 100.7;
        f.Sync.Tick(500);
        f.Player.Paused = false;
        f.Sync.QueueCommand(f.Command("play", 101.3, "playing"), 500);
        f.Sync.Tick(500);
        Assert.Equal(1.025, f.Player.Rate);
        f.Sync.Tick(2000);
        Assert.Contains(f.Messages, m => m.Type == "state_report" && m.CommandId == "playing");
    }

    [Theory]
    [InlineData("session")]
    [InlineData("selection")]
    [InlineData("connection")]
    public void HeldSeekBelongsToCurrentSessionSelectionAndConnection(string replacement)
    {
        var f = new Fixture(host: true);
        f.Sync.RequestTransport("seek", 100, true);
        Assert.Equal(100, f.Sync.HeldSeekPosition(0));
        if (replacement == "session") f.Sync.SetSession("replacement");
        else if (replacement == "connection") f.Sync.SetConnected(false, 0);
        else f.Sync.SetSnapshot(new() { RoomId = "r", Phase = "playing", SelectionRevision = 2 });
        Assert.Null(f.Sync.HeldSeekPosition(0));
    }

    [Fact]
    public void StaleUnlandedSeekReleasesCorrectionBlockWithoutClaimingItsReportId()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("seek", 100), 0);
        f.Sync.Tick(0);
        f.Sync.Tick(30_000);
        Assert.DoesNotContain(f.Messages, m => m.Type == "state_report" && m.CommandId == "c1");
        f.Sync.QueueCommand(f.Command("play", 0, "surviving"), 30_001);
        f.Sync.Tick(30_001);
        f.Sync.Tick(32_000);
        Assert.Contains(f.Messages, m => m.Type == "state_report" && m.CommandId == "surviving");
    }

    [Theory]
    [InlineData("waiting")]
    [InlineData("paused")]
    public void PlayFrameCannotResumePausedRoomIntent(string state)
    {
        var f = new Fixture();
        var command = f.Command("play", 0);
        command.PlaybackState = state;
        f.Sync.QueueCommand(command, 0);
        f.Sync.Tick(5000);
        Assert.True(f.Player.Paused);
        Assert.Empty(f.Player.Seeks);
    }

    [Theory]
    [InlineData(false, false, "pause", false)]
    [InlineData(false, true, "pause", true)]
    [InlineData(false, true, "seek", false)]
    [InlineData(true, true, "seek", true)]
    public void IntentsRespectHostAndGuestPermissions(bool host, bool controls, string action, bool allowed)
    {
        var f = new Fixture(host, controls);
        f.Sync.RequestTransport(action, 42, true);
        Assert.Equal(allowed ? 1 : 0, f.Messages.Count(x => x.Type == "transport_request"));
        Assert.Empty(f.Player.Seeks);
        Assert.True(f.Player.Paused);
        f.Sync.SetConnected(false, 0);
        f.Sync.RequestTransport(action, 42, true);
        Assert.Equal(allowed ? 1 : 0, f.Messages.Count(x => x.Type == "transport_request"));
    }

    [Fact]
    public void ReconnectAttachesSameSessionAgain()
    {
        var f = new Fixture();
        Assert.Single(f.Messages.Where(x => x.Type == "attach_session"));
        f.Sync.SetConnected(false, 0);
        f.Sync.SetConnected(true, 100);
        Assert.Equal(2, f.Messages.Count(x => x.Type == "attach_session" && x.SessionId == "s1"));
        f.Sync.SetConnected(true, 200);
        Assert.Equal(2, f.Messages.Count(x => x.Type == "attach_session"));
    }

    [Fact]
    public void CommandsAreScheduledDeduplicatedAndNeverEchoed()
    {
        var f = new Fixture();
        var cmd = f.Command("play", 10);
        f.Sync.QueueCommand(cmd, 1000);
        f.Sync.Tick(999);
        Assert.True(f.Player.Paused);
        f.Sync.Tick(1000);
        Assert.False(f.Player.Paused);
        f.Sync.QueueCommand(cmd, 1001);
        f.Sync.Tick(1001);
        Assert.Single(f.Player.Seeks);
        Assert.DoesNotContain(f.Messages, x => x.Type == "transport_request");
    }

    [Fact]
    public void InitialLobbyDoesNotCloseButPlayingToLobbyClosesMatchingSession()
    {
        var f = new Fixture();
        var closed = new List<string>();
        f.Sync.ClosePlaybackRequested += closed.Add;
        f.Sync.SetSnapshot(new() { RoomId = "r", Phase = "lobby" });
        Assert.Equal(new[] { "s1" }, closed);
        f.Sync.SetSnapshot(new() { RoomId = "r", Phase = "lobby" });
        Assert.Single(closed);
        var initial = new WatchPartySyncController(f.Player, _ => { });
        initial.ClosePlaybackRequested += _ => Assert.Fail("Initial lobby must not stop playback");
        initial.SetSnapshot(new() { RoomId = "r", Phase = "lobby" });
    }

    [Fact]
    public void ReadinessRequiresAppliedCommandAndTargetNotJustDuration()
    {
        var f = new Fixture();
        f.Room.PlaybackState = "waiting";
        f.Sync.SetSnapshot(f.Room);
        f.Sync.Tick(0);
        Assert.DoesNotContain(f.Messages, x => x.Type == "ready");
        f.Sync.QueueCommand(f.Command("seek", 100), 250);
        f.Sync.Tick(250);
        f.Player.Position = 90;
        f.Sync.Tick(500);
        Assert.DoesNotContain(f.Messages, x => x.Type == "ready");
        f.Player.Position = 99.5;
        f.Sync.Tick(750);
        Assert.Contains(f.Messages, x => x.Type == "ready" && x.CommandId == "c1");
    }

    [Fact]
    public void BriefStallStaysLocalAndSustainedStallReportsOnceAfterGrace()
    {
        var f = new Fixture();
        f.Player.Buffering = true;
        f.Sync.Tick(0);
        f.Sync.Tick(1999);
        Assert.DoesNotContain(f.Messages, x => x.Type == "buffering");
        f.Player.Buffering = false;
        f.Sync.Tick(2000);
        f.Player.Buffering = true;
        f.Sync.Tick(3000);
        f.Sync.Tick(5000);
        f.Sync.Tick(5500);
        Assert.Single(f.Messages.Where(x => x.Type == "buffering"));
    }

    [Fact]
    public void SmallRemuxDriftConvergesWithoutReloadAndRestoresUserRate()
    {
        var f = new Fixture();
        f.Player.LocallySeekable = false;
        f.Player.Rate = 1.5;
        f.Player.Position = 9;
        f.Player.Paused = false;
        f.Sync.QueueCommand(f.Command("play", 10), 0);
        f.Sync.Tick(0);
        Assert.Empty(f.Player.Seeks);
        Assert.Equal(1.04, f.Player.Rate);
        f.Player.Position = 11;
        f.Sync.Tick(1000);
        Assert.Equal(1.5, f.Player.Rate);
    }

    [Fact]
    public void CorrectionsAreBoundedButExplicitRoomSeekIsNeverDropped()
    {
        var f = new Fixture();
        f.Player.LocallySeekable = false;
        f.Sync.QueueCommand(f.Command("play", 100), 0);
        f.Sync.Tick(0);
        f.Sync.QueueCommand(f.Command("play", 105, "c2"), 1000);
        f.Sync.Tick(1000);
        Assert.Single(f.Player.Seeks);
        f.Sync.QueueCommand(f.Command("seek", 500, "c3"), 2000);
        f.Sync.Tick(2000);
        Assert.Equal(new[] { 100d, 500d }, f.Player.Seeks);
    }

    [Fact]
    public void ReconnectNoticeHasGraceAndRepeatedStallsOfferQualityOnlyOnce()
    {
        var f = new Fixture();
        var notices = new List<string>();
        var offers = 0;
        f.Sync.Notice += notices.Add;
        f.Sync.LowerQualityRequested += () => offers++;
        f.Sync.SetConnected(false, 0);
        f.Sync.Tick(1999);
        Assert.Empty(notices);
        f.Sync.Tick(2000);
        Assert.Single(notices);
        f.Sync.SetConnected(true, 2100);
        for (var i = 0; i < 3; i++)
        {
            f.Player.Buffering = true;
            f.Sync.Tick(3000 + i * 5000);
            f.Sync.Tick(5000 + i * 5000);
            f.Player.Buffering = false;
            f.Sync.Tick(5500 + i * 5000);
        }
        Assert.Equal(1, offers);
    }

    [Fact]
    public void RemuxPrerollUsesOneExactCachedSeekAndRetainsPauseAndRate()
    {
        var f = new Fixture();
        f.Room.PlaybackState = "waiting";
        f.Player.Rate = 1.5;
        f.Player.LocallySeekable = false;
        var seek = f.Command("seek", 100);
        seek.PlaybackState = "waiting";
        f.Sync.QueueCommand(seek, 0);
        f.Sync.Tick(0);
        f.Player.Position = 94; // rebuilt remux starts on the preceding keyframe
        f.Sync.Tick(500);
        Assert.Single(f.Player.Seeks);
        f.Player.LocallySeekable = true; // mpv now exposes target in its cache
        f.Sync.Tick(750);
        f.Sync.Tick(1000);
        Assert.Equal(new[] { 100d, 100d }, f.Player.Seeks);
        Assert.DoesNotContain(f.Messages, m => m.Type == "ready");
        f.Player.Position = 100;
        f.Sync.Tick(1250);
        Assert.Contains(f.Messages, m => m.Type == "ready" && m.CommandId == "c1");
        Assert.True(f.Player.Paused);
        Assert.Equal(1.5, f.Player.Rate);
    }

    [Fact]
    public void StaleSessionRevisionAndSupersededScheduledCommandsAreIgnored()
    {
        var f = new Fixture();
        var wrongSession = f.Command("play", 10);
        wrongSession.SessionId = "retired";
        f.Sync.QueueCommand(wrongSession, 0);
        var wrongRevision = f.Command("play", 20, "old-revision");
        wrongRevision.SelectionRevision = 0;
        f.Sync.QueueCommand(wrongRevision, 0);
        f.Sync.Tick(0);
        Assert.Empty(f.Player.Seeks);
        f.Sync.QueueCommand(f.Command("play", 10, "early"), 1000);
        f.Sync.QueueCommand(f.Command("pause", 20, "late"), 500);
        f.Sync.Tick(500);
        f.Sync.Tick(1000);
        Assert.Equal(new[] { 20d }, f.Player.Seeks);
        Assert.True(f.Player.Paused);
    }

    [Fact]
    public void CorrectionReloadHasStaleTimeoutAndMeasuredLead()
    {
        var f = new Fixture();
        f.Player.LocallySeekable = false;
        f.Sync.QueueCommand(f.Command("play", 100), 0);
        f.Sync.Tick(0);
        f.Sync.QueueCommand(f.Command("play", 105, "retry"), 29_999);
        f.Sync.Tick(29_999);
        Assert.Single(f.Player.Seeks);
        f.Sync.QueueCommand(f.Command("play", 130, "stale"), 30_000);
        f.Sync.Tick(30_000);
        Assert.Equal(2, f.Player.Seeks.Count);
        f.Player.Position = 130;
        f.Sync.Tick(32_000); // second reload landed after two seconds
        f.Sync.QueueCommand(f.Command("play", 152, "budget"), 40_000);
        f.Sync.Tick(40_000);
        Assert.Equal(2, f.Player.Seeks.Count);
        f.Sync.QueueCommand(f.Command("play", 155, "lead"), 52_000);
        f.Sync.Tick(52_000);
        Assert.Equal(157, f.Player.Seeks.Last());
    }

    [Fact]
    public void RateCorrectionPreservesAnExplicitViewerRateChange()
    {
        var f = new Fixture();
        f.Player.Position = 9;
        f.Player.LocallySeekable = false;
        f.Sync.QueueCommand(f.Command("play", 10), 0);
        f.Sync.Tick(0);
        f.Player.Rate = 1.8;
        f.Sync.SetSession(null);
        Assert.Equal(1.8, f.Player.Rate);
    }

    [Fact]
    public void IgnoredSlowGuestExplicitlyAcknowledgesRecoveryAfterTargetLands()
    {
        var f = new Fixture();
        f.Room.SelfIgnoreWait = true;
        f.Room.Members.Add(new() { IsSelf = true, IsBuffering = true });
        f.Sync.QueueCommand(f.Command("seek", 100), 0);
        f.Sync.Tick(0);
        f.Sync.Tick(1000);
        Assert.DoesNotContain(f.Messages, m => m.Type is "state_report" or "ready");
        f.Player.Position = 100;
        f.Sync.Tick(1500);
        Assert.Contains(f.Messages, m => m.Type == "ready" && m.CommandId == "c1");
    }

    private sealed class Fixture
    {
        public FakePlayer Player { get; } = new();
        public List<WatchPartyMessage> Messages { get; } = [];
        public WatchTogetherRoomSnapshot Room { get; }
        public WatchPartySyncController Sync { get; }
        public Fixture(bool host = false, bool controls = false)
        {
            Room = new() { RoomId = "r", Phase = "playing", PlaybackState = "playing", SelectionRevision = 1,
                SelfCanManageRoom = host, SelfCanControlTransport = controls, SelectedContentId = "item" };
            Sync = new(Player, Messages.Add);
            Sync.SetSnapshot(Room);
            Sync.SetSession("s1");
            Sync.SetConnected(true, 0);
        }
        public WatchTogetherTransportCommand Command(string action, double position, string id = "c1") =>
            new() { CommandId = id, SessionId = "s1", SelectionRevision = 1, Action = action, PositionSeconds = position,
                PlaybackState = action == "play" ? "playing" : "paused" };
    }

    private sealed class FakePlayer : IWatchPartyPlayback
    {
        public double Position { get; set; }
        public double Duration => 1000;
        public bool Paused { get; set; } = true;
        public bool Buffering { get; set; }
        public bool Busy { get; set; }
        public bool Loaded => true;
        public bool LocallySeekable { get; set; } = true;
        public double Rate { get; set; } = 1;
        public string Quality => "original";
        public List<double> Seeks { get; } = [];
        public bool CanSeekLocally(double target) => LocallySeekable;
        public bool IsTargetBuffered(double target) => LocallySeekable;
        public void Seek(double target) => Seeks.Add(target);
        public void SetPaused(bool paused) => Paused = paused;
    }
}
