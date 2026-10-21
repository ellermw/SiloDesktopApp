using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class Oct20WatchPartyStartupTests
{
    [Fact]
    public void RecoveryBeforePlayableMediaUsesDueRoomPositionAndDropsItAcrossReconnect()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("play", 40), 1000);
        Assert.Null(f.Sync.StartupPosition(999));
        f.Sync.Tick(3000);
        Assert.Equal(42, f.Sync.StartupPosition(3000));
        f.Sync.QueueCommand(f.Command("pause", 43, "pause"), 3500);
        Assert.Equal(43, f.Sync.StartupPosition(5000));
        f.Sync.SetConnected(false, 5000);
        Assert.Null(f.Sync.StartupPosition(5000));
        f.Sync.SetConnected(true, 6000);
        Assert.Null(f.Sync.StartupPosition(6000));
    }

    [Fact]
    public void PlayWaitsForPlayableMediaThenStartsAtProjectedPositionEvenOnForwardOnlyTransport()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("play", 0), 1000);
        f.Sync.Tick(1000); f.Sync.Tick(3000);
        Assert.True(f.Player.Paused);
        Assert.Empty(f.Player.Seeks);
        Assert.DoesNotContain(f.Messages, message => message.Type is "ready" or "state_report");
        f.Player.Loaded = true; f.Player.Busy = false;
        f.Sync.Tick(3500);
        Assert.Equal(2.5, Assert.Single(f.Player.Seeks));
        Assert.True(Assert.Single(f.Player.PausedWhenSeeked));
        Assert.False(f.Player.Paused);
        Assert.DoesNotContain(f.Messages, message => message.Type == "transport_request");
    }

    [Fact]
    public void PauseSupersedesPlayWhileMediaLoadsAndDoesNotProjectTime()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("play", 20), 0);
        f.Sync.Tick(1000);
        f.Sync.QueueCommand(f.Command("pause", 21, "pause"), 1000);
        f.Player.Loaded = true; f.Player.Busy = false;
        f.Sync.Tick(3500);
        Assert.Equal(21, Assert.Single(f.Player.Seeks));
        Assert.True(f.Player.Paused);
    }

    [Fact]
    public void HeldPlayAfterAppliedPauseStillProjectsCurrentTransportStartup()
    {
        var f = new Fixture();
        f.Player.Loaded = true; f.Player.Busy = false;
        f.Sync.QueueCommand(f.Command("pause", 0, "barrier"), 0);
        f.Sync.Tick(0);
        f.Player.Busy = true;
        f.Sync.QueueCommand(f.Command("play", 0), 1000);
        f.Sync.Tick(1000);
        f.Player.Busy = false;
        f.Sync.Tick(3500);
        Assert.Equal(2.5, Assert.Single(f.Player.Seeks));
        Assert.True(Assert.Single(f.Player.PausedWhenSeeked));
        Assert.False(f.Player.Paused);
    }

    [Fact]
    public void OrdinaryPlayableForwardOnlyResumeRetainsGentleSmallGapBehavior()
    {
        var f = new Fixture();
        f.Player.Loaded = true; f.Player.Busy = false;
        f.Sync.QueueCommand(f.Command("pause", 0, "pause"), 0); f.Sync.Tick(0);
        f.Sync.QueueCommand(f.Command("play", 2.5), 1000); f.Sync.Tick(1000);
        Assert.Empty(f.Player.Seeks);
        Assert.False(f.Player.Paused);
    }

    [Fact]
    public void ReconnectDropsQueuedPlayAndWaitsForFreshRoomCommand()
    {
        var f = new Fixture();
        f.Sync.QueueCommand(f.Command("play", 20), 0);
        f.Sync.SetConnected(false, 500); f.Sync.SetConnected(true, 1000);
        f.Player.Loaded = true; f.Player.Busy = false;
        f.Sync.Tick(3500);
        Assert.True(f.Player.Paused);
        Assert.Empty(f.Player.Seeks);
        f.Sync.QueueCommand(f.Command("play", 24, "fresh"), 4000);
        f.Sync.Tick(4500);
        Assert.Equal(24.5, Assert.Single(f.Player.Seeks));
        Assert.False(f.Player.Paused);
    }

    private sealed class Fixture
    {
        public readonly StartupPlayer Player = new();
        public readonly List<WatchPartyMessage> Messages = [];
        public WatchPartySyncController Sync { get; }
        public Fixture()
        {
            Sync = new(Player, Messages.Add);
            Sync.SetSnapshot(new() { RoomId = "r", Phase = "playing", PlaybackState = "playing", SelectionRevision = 1 });
            Sync.SetSession("session"); Sync.SetConnected(true, 0);
        }
        public WatchTogetherTransportCommand Command(string action, double position, string id = "play") => new()
        {
            CommandId = id, Action = action, SessionId = "session", SelectionRevision = 1,
            PositionSeconds = position, PlaybackState = action == "play" ? "playing" : "paused",
        };
    }

    private sealed class StartupPlayer : IWatchPartyPlayback
    {
        public double Position { get; set; }
        public double Duration => 1000;
        public bool Paused { get; set; } = true;
        public bool Buffering => false;
        public bool Busy { get; set; } = true;
        public bool Loaded { get; set; }
        public double Rate { get; set; } = 1;
        public string Quality => "auto";
        public readonly List<double> Seeks = [];
        public readonly List<bool> PausedWhenSeeked = [];
        public bool CanSeekLocally(double target) => false;
        public bool IsTargetBuffered(double target) => false;
        public void Seek(double target) { Seeks.Add(target); PausedWhenSeeked.Add(Paused); }
        public void SetPaused(bool paused) => Paused = paused;
    }
}
