using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class WatchPartySyncTests
{
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
        f.Sync.QueueCommand(f.Command("play", 10), 0);
        f.Sync.Tick(0);
        Assert.Empty(f.Player.Seeks);
        Assert.InRange(f.Player.Rate, 1.01, 1.25);
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
            new() { CommandId = id, SessionId = "s1", SelectionRevision = 1, Action = action, PositionSeconds = position };
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
