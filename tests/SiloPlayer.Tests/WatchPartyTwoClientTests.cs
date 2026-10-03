using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Two production controllers against a deliberately small authoritative relay.
/// This tests cross-client message ordering, not the real server's implementation.</summary>
public sealed class WatchPartyTwoClientTests
{
    [Fact]
    public void HostAndGuestSeekTogetherGuestCannotSeekAndCommandsNeverEcho()
    {
        var relay = new Relay();
        relay.Start(25);
        relay.Pump(0);
        Assert.True(relay.Host.Player.Paused);
        Assert.True(relay.Guest.Player.Paused);
        relay.Pump(500);
        Assert.False(relay.Host.Player.Paused);
        Assert.False(relay.Guest.Player.Paused);
        Assert.Equal(25, relay.Host.Player.Position);
        Assert.Equal(25, relay.Guest.Player.Position);
        Assert.Equal(0, relay.TransportRequests);

        Assert.False(relay.Guest.Sync.RequestTransport("seek", 300, false));
        relay.Pump(750);
        Assert.Equal(0, relay.TransportRequests);
        Assert.Equal(25, relay.Guest.Player.Position);
        Assert.True(relay.Host.Sync.RequestTransport("seek", 300, false));
        relay.Pump(1000);
        relay.Pump(1500);
        Assert.Equal(1, relay.TransportRequests);
        Assert.Equal(300, relay.Host.Player.Position);
        Assert.Equal(300, relay.Guest.Player.Position);
        Assert.False(relay.Host.Player.Paused);
        Assert.False(relay.Guest.Player.Paused);

        Assert.True(relay.Guest.Sync.RequestTransport("pause", 300, true));
        relay.Pump(1750);
        Assert.True(relay.Host.Player.Paused);
        Assert.True(relay.Guest.Player.Paused);
        Assert.Equal(2, relay.TransportRequests); // applied commands did not request transport again
    }

    [Fact]
    public void SlowGuestHoldsTargetBarrierThenSustainedStallPausesAndRecoversBothClients()
    {
        var relay = new Relay();
        relay.Guest.Player.LandSeeks = false;
        relay.Start(100);
        relay.Pump(0);
        relay.Pump(500);
        Assert.True(relay.Host.Player.Paused);
        Assert.True(relay.Guest.Player.Paused);
        Assert.Equal(new[] { "host" }, relay.ReadyMembers.Order().ToArray());
        relay.Guest.Player.Position = 99.6;
        relay.Pump(1000);
        Assert.False(relay.Host.Player.Paused);
        Assert.False(relay.Guest.Player.Paused);

        relay.Guest.Player.Buffering = true;
        relay.Pump(1250);
        relay.Pump(3000); // transient: less than two seconds
        Assert.False(relay.Host.Player.Paused);
        Assert.Equal(0, relay.BufferingReports);
        relay.Pump(3250);
        Assert.True(relay.Host.Player.Paused);
        Assert.True(relay.Guest.Player.Paused);
        Assert.Equal(1, relay.BufferingReports);
        relay.Pump(3500);
        relay.Pump(3750);
        Assert.Equal(1, relay.BufferingReports);
        relay.Guest.Player.Buffering = false;
        relay.Pump(4000);
        Assert.False(relay.Host.Player.Paused);
        Assert.False(relay.Guest.Player.Paused);
        Assert.Equal(0, relay.TransportRequests);
    }

    [Fact]
    public void SocketRecoveryReattachesSameGuestSessionAndRestoresRoomAuthority()
    {
        var relay = new Relay();
        relay.Start(40);
        relay.Pump(0);
        relay.Pump(500);
        relay.Guest.Sync.SetConnected(false, 750);
        Assert.False(relay.Guest.Sync.RequestTransport("pause", 40, true));
        relay.Pump(1000);
        Assert.False(relay.Host.Player.Paused);
        relay.Guest.Sync.SetConnected(true, 1250);
        relay.Pump(1250);
        Assert.Equal(2, relay.Attachments.Count(x => x == "guest-session"));
        Assert.True(relay.Guest.Sync.RequestTransport("pause", 40, true));
        relay.Pump(1500);
        Assert.True(relay.Host.Player.Paused);
        Assert.True(relay.Guest.Player.Paused);
        Assert.Equal(1, relay.TransportRequests);
    }

    private sealed class Relay
    {
        private readonly Queue<(Client Client, WatchPartyMessage Message)> _outbound = [];
        private long _now;
        private int _command;
        private string? _barrierCommand;
        private string _state = "playing";
        private double _anchor;
        private bool _recovering;
        public Client Host { get; }
        public Client Guest { get; }
        public List<string> Attachments { get; } = [];
        public HashSet<string> ReadyMembers { get; } = [];
        public int TransportRequests { get; private set; }
        public int BufferingReports { get; private set; }

        public Relay()
        {
            Host = new("host", true, Enqueue);
            Guest = new("guest", false, Enqueue);
            Snapshots();
            foreach (var client in Clients)
            {
                client.Sync.SetSession(client.Name + "-session");
                client.Sync.SetConnected(true, 0);
            }
        }
        private IEnumerable<Client> Clients => new[] { Host, Guest };
        private void Enqueue(Client client, WatchPartyMessage message) => _outbound.Enqueue((client, message));
        public void Start(double target)
        {
            _anchor = target;
            _state = "waiting";
            ReadyMembers.Clear();
            Snapshots();
            _barrierCommand = Broadcast("seek", target);
        }
        private string Broadcast(string action, double target)
        {
            var id = "relay-" + ++_command;
            foreach (var client in Clients) Send(client, action, target, id);
            return id;
        }
        private void Send(Client client, string action, double target, string id) => client.Sync.QueueCommand(new()
        {
            CommandId = id, SessionId = client.Name + "-session", SelectionRevision = 1,
            Action = action, PositionSeconds = target, PlaybackState = _state,
        }, _now);
        private void Snapshots()
        {
            foreach (var client in Clients) client.Sync.SetSnapshot(new()
            {
                RoomId = "room", Phase = "playing", PlaybackState = _state, SelectionRevision = 1,
                SelectedContentId = "movie", SelfCanManageRoom = client.Host, SelfCanControlTransport = true,
                Members = Clients.Select(member => new WatchTogetherRoomMember
                {
                    UserId = member.Host ? 1 : 2, ProfileId = member.Name, DisplayName = member.Name,
                    IsSelf = ReferenceEquals(client, member), IsReady = ReadyMembers.Contains(member.Name),
                    IsBuffering = _recovering && !member.Host,
                }).ToList(),
            });
        }
        public void Pump(long now)
        {
            _now = now;
            for (var iteration = 0; iteration < 20; iteration++)
            {
                foreach (var client in Clients) client.Sync.Tick(now);
                if (_outbound.Count == 0) return;
                while (_outbound.TryDequeue(out var envelope))
                {
                    var (client, message) = envelope;
                    switch (message.Type)
                    {
                        case "attach_session":
                            Attachments.Add(message.SessionId!);
                            if (_barrierCommand == null && _command > 0)
                                Send(client, _state == "paused" ? "pause" : "play", _anchor, "reattach-" + ++_command);
                            break;
                        case "transport_request":
                            TransportRequests++;
                            if (message.Action == "seek")
                            {
                                Assert.True(client.Host); // relay independently checks host-only seek
                                Start(message.PositionSeconds);
                            }
                            else
                            {
                                _state = message.Action == "pause" ? "paused" : "playing";
                                Snapshots();
                                Broadcast(message.Action!, message.PositionSeconds);
                            }
                            break;
                        case "buffering":
                            BufferingReports++;
                            _recovering = true;
                            _state = "waiting";
                            ReadyMembers.Clear();
                            Snapshots();
                            _barrierCommand = Broadcast("pause", _anchor);
                            break;
                        case "ready":
                            if (message.CommandId != _barrierCommand) break;
                            ReadyMembers.Add(client.Name);
                            if (ReadyMembers.Count == 2)
                            {
                                _barrierCommand = null;
                                _recovering = false;
                                _state = "playing";
                                Snapshots();
                                Broadcast("play", _anchor);
                            }
                            break;
                    }
                }
            }
            Assert.Fail("Controller/relay failed to quiesce: possible command echo loop.");
        }
    }

    private sealed class Client
    {
        public string Name { get; }
        public bool Host { get; }
        public FixturePlayer Player { get; } = new();
        public WatchPartySyncController Sync { get; }
        public Client(string name, bool host, Action<Client, WatchPartyMessage> send)
        {
            Name = name; Host = host;
            Sync = new(Player, message => send(this, message));
        }
    }
    private sealed class FixturePlayer : IWatchPartyPlayback
    {
        public double Position { get; set; }
        public double Duration => 1000;
        public bool Paused { get; private set; } = true;
        public bool Buffering { get; set; }
        public bool Busy => false;
        public bool Loaded => true;
        public double Rate { get; set; } = 1;
        public string Quality => "original";
        public bool LandSeeks { get; set; } = true;
        public bool CanSeekLocally(double target) => true;
        public bool IsTargetBuffered(double target) => true;
        public void Seek(double target) { if (LandSeeks) Position = target; }
        public void SetPaused(bool paused) => Paused = paused;
    }
}
