using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class WatchPartyRoomClockTests
{
    [Fact]
    public void FastestOfLastEightSamplesControlsRealCommandSchedule()
    {
        var clock = new WatchPartyRoomClock();
        clock.BeginConnection();
        Assert.Equal(2000, clock.PingDelayMilliseconds);
        clock.Reset();
        Assert.Equal(0, clock.OffsetMilliseconds);
        // First:80ms network RTT,20ms server work, true offset1000ms.
        Assert.True(clock.RecordSample(0, 1040, 1060, 100));
        // More delayed returns must not replace the low-RTT estimate.
        for (var i = 0; i < 7; i++) Assert.True(clock.RecordSample(1000, 2100, 2120, 1220));
        Assert.Equal(1000, clock.OffsetMilliseconds);
        Assert.Equal(15000, clock.PingDelayMilliseconds);
        var room = new SiloPlayer.Core.Models.Playback.WatchTogetherRoomSnapshot
        { RoomId = "r", Phase = "playing", PlaybackState = "playing", SelectionRevision = 1 };
        var player = new SchedulePlayer();
        var sync = new WatchPartySyncController(player, _ => { });
        sync.SetSnapshot(room); sync.SetSession("s"); sync.SetConnected(true, 0);
        sync.QueueCommand(new() { CommandId = "late", SelectionRevision = 1, SessionId = "s", Action = "play",
            PlaybackState = "playing", PositionSeconds = 100, ExecuteAt = DateTimeOffset.UnixEpoch.AddSeconds(10).ToString("O") },
            clock, DateTimeOffset.UnixEpoch.AddSeconds(12), 50_000);
        sync.Tick(50_000);
        Assert.Equal(103, player.Target); // execute_at was3s ago in server time
        // Expire the80ms sample. Newest among equal200ms RTTs wins.
        Assert.True(clock.RecordSample(2000, 3130, 3150, 2220));
        Assert.Equal(1030, clock.OffsetMilliseconds);
    }

    [Theory]
    [InlineData(1000, 2000, 2500, 1200, false)]
    [InlineData(1000, 2000, 2003, 1000, false)]
    [InlineData(0, 0, 0, 5001, false)]
    [InlineData(0, 0, 0, 5000, true)]
    [InlineData(1000, 2000, 2002, 1000, true)]
    [InlineData(double.NaN, 0, 0, 10, false)]
    public void InvalidPongsDoNotPolluteClock(double sent, double received, double replied, double now, bool accepted)
    {
        var clock = new WatchPartyRoomClock();
        Assert.Equal(accepted, clock.RecordSample(sent, received, replied, now));
        if (!accepted) Assert.Equal(0, clock.OffsetMilliseconds);
    }

    [Fact]
    public void ClockStepRestartsFastCadenceAndReconnectRetainsBestEstimate()
    {
        var clock = new WatchPartyRoomClock();
        for (var i = 0; i < 4; i++) clock.RecordSample(0, 1010, 1010, 20);
        Assert.Equal(15000, clock.PingDelayMilliseconds);
        clock.RecordSample(0, 1210, 1210, 20);
        Assert.Equal(1200, clock.OffsetMilliseconds);
        Assert.Equal(2000, clock.PingDelayMilliseconds);
        clock.BeginConnection();
        Assert.Equal(1200, clock.OffsetMilliseconds);
        Assert.Equal(2000, clock.PingDelayMilliseconds);
    }

    private sealed class SchedulePlayer : IWatchPartyPlayback
    {
        public double Target { get; private set; }
        public double Position => 0;
        public double Duration => 1000;
        public bool Paused => true;
        public bool Buffering => false;
        public bool Busy => false;
        public bool Loaded => true;
        public double Rate { get; set; } = 1;
        public string Quality => "original";
        public bool CanSeekLocally(double target) => true;
        public bool IsTargetBuffered(double target) => true;
        public void Seek(double target) => Target = target;
        public void SetPaused(bool paused) { }
    }
}
