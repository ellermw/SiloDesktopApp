using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SiloPlayer.Core.Services.SiloCast;

namespace SiloPlayer.Tests;

public class SiloCastConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedTlsConnectionsExchangeFramesWithFreshPsk(bool tls13)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
            var accepted = await listener.AcceptTcpClientAsync(deadline.Token);
            var accepting = SiloCastConnection.AcceptAsync(accepted, deadline.Token);
            var peer = new TlsClientProtocol(client.GetStream());
            var connecting = Task.Run(() => peer.Connect(new TestPskClient(tls13)), deadline.Token);
            await using var connection = await accepting;
            // Receiver hello must be emitted by the owner immediately after AcceptAsync:
            // Apple's TLS 1.3 client can wait for this application record to finish connecting.
            await connection.SendAsync(SiloCastProtocol.CreateMessage("hello", "hello", new JsonObject { ["role"] = "tv", ["supportedVersions"] = new JsonArray(2) }), deadline.Token);
            await connecting.WaitAsync(deadline.Token);
            var readHello = Task.Run(() => SiloCastFrameCodec.ReadAsync(peer.Stream, deadline.Token), deadline.Token);
            Assert.Equal("tv", (await readHello.WaitAsync(deadline.Token))!["hello"]!["role"]!.GetValue<string>());
            await Task.Run(() => SiloCastFrameCodec.WriteAsync(peer.Stream, SiloCastProtocol.CreateMessage("pong"), deadline.Token), deadline.Token);
            Assert.Equal("pong", (await connection.ReadAsync(deadline.Token))!["type"]!.GetValue<string>());
            var pending = connection.ReadAsync(deadline.Token);
            await connection.DisposeAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task HandshakeCancellationClosesAcceptedSocket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = await listener.AcceptTcpClientAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SiloCastConnection.AcceptAsync(accepted, deadline.Token));
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task SilentClientCannotHoldHandshakePastTenSecondDeadline()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = await listener.AcceptTcpClientAsync();
        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SiloCastConnection.AcceptAsync(accepted, default).WaitAsync(TimeSpan.FromSeconds(12)));
        Assert.InRange(elapsed.Elapsed.TotalSeconds, 9, 12);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task UnreadPeerCannotAccumulateOutgoingFramesOrPreventDisposal()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient { ReceiveBufferSize = 1024 };
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
        var accepted = await listener.AcceptTcpClientAsync(deadline.Token);
        var accepting = SiloCastConnection.AcceptAsync(accepted, deadline.Token);
        var peer = new TlsClientProtocol(client.GetStream());
        var connecting = Task.Run(() => peer.Connect(new TestPskClient(false)), deadline.Token);
        await using var connection = await accepting;
        await connection.SendAsync(SiloCastProtocol.CreateMessage("ping"), deadline.Token);
        await connecting.WaitAsync(deadline.Token);
        // The peer now stops consuming records; socket buffers eventually fill.
        var message = new JsonObject { ["data"] = new string('x', 1048576 - 11) };
        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            for (var i = 0; i < 32; i++)
                await connection.SendAsync(message, deadline.Token);
        });
        Assert.InRange(elapsed.Elapsed.TotalSeconds, 4, 8);
        await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.SendAsync(SiloCastProtocol.CreateMessage("ping"), default));
    }

    private sealed class TestPskClient : PskTlsClient
    {
        private readonly bool _tls13;
        private readonly BcTlsCrypto _crypto;

        public TestPskClient(bool tls13) : this(tls13, new BcTlsCrypto(new SecureRandom())) { }
        private TestPskClient(bool tls13, BcTlsCrypto crypto) : base(crypto, Encoding.UTF8.GetBytes("silo-cast"), Encoding.UTF8.GetBytes("silo-cast-v1"))
        {
            _tls13 = tls13;
            _crypto = crypto;
        }
        protected override ProtocolVersion[] GetSupportedVersions() => [_tls13 ? ProtocolVersion.TLSv13 : ProtocolVersion.TLSv12];
        protected override int[] GetSupportedCipherSuites() => [_tls13 ? CipherSuite.TLS_AES_128_GCM_SHA256 : CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256];
        public override IList<TlsPskExternal> GetExternalPsks() => [new BasicTlsPskExternal(Encoding.UTF8.GetBytes("silo-cast"), _crypto.CreateSecret(Encoding.UTF8.GetBytes("silo-cast-v1")), PrfAlgorithm.tls13_hkdf_sha256)];
    }
}
