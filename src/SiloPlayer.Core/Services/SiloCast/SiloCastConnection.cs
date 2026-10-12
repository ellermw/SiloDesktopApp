using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace SiloPlayer.Core.Services.SiloCast;

public sealed class SiloCastConnection : IAsyncDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);
    private readonly TcpClient _client;
    private readonly NetworkStream _network;
    private readonly TlsServerProtocol _protocol = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _engineGate = new(1, 1);
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly byte[] _incoming = new byte[64 * 1024];
    private readonly byte[] _outgoing = new byte[64 * 1024];
    private readonly ApplicationStream _application;
    private int _disposed;

    private SiloCastConnection(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _network = client.GetStream();
        _application = new ApplicationStream(this);
    }

    public static async Task<SiloCastConnection> AcceptAsync(TcpClient client, CancellationToken ct)
    {
        var connection = new SiloCastConnection(client);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, connection._lifetime.Token);
        deadline.CancelAfter(HandshakeTimeout);
        try
        {
            connection._protocol.Accept(new CastPskServer(new BcTlsCrypto(new SecureRandom())));
            await connection.DrainOutputAsync(deadline.Token).ConfigureAwait(false);
            while (!connection._protocol.IsConnected)
            {
                var count = await connection._network.ReadAsync(connection._incoming, deadline.Token).ConfigureAwait(false);
                if (count == 0)
                    throw new EndOfStreamException("SiloCast TLS handshake ended before completion.");
                connection._protocol.OfferInput(connection._incoming, 0, count);
                await connection.DrainOutputAsync(deadline.Token).ConfigureAwait(false);
            }
            // The owner must promptly emit its receiver hello after this returns.
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<JsonObject?> ReadAsync(CancellationToken ct)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _readGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            return await SiloCastFrameCodec.ReadAsync(_application, cancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally { _readGate.Release(); }
    }

    public async Task SendAsync(JsonObject message, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        // Includes waiting for serialization: callers cannot wait indefinitely on an unread peer.
        deadline.CancelAfter(WriteTimeout);
        await _writeGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            await SiloCastFrameCodec.WriteAsync(_application, message, deadline.Token).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally { _writeGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        // Interrupt socket IO before acquiring the engine gate; an unread peer
        // may otherwise hold that gate forever while a TLS record is being written.
        _client.Dispose();
        _lifetime.Cancel();
        await _engineGate.WaitAsync().ConfigureAwait(false);
        try { _protocol.Close(); }
        catch (IOException) { }
        finally { _engineGate.Release(); }
    }

    private async ValueTask<int> ReadApplicationAsync(Memory<byte> buffer, CancellationToken ct)
    {
        while (true)
        {
            await _engineGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_protocol.GetAvailableInputBytes() > 0)
                {
                    var count = Math.Min(buffer.Length, _protocol.GetAvailableInputBytes());
                    return _protocol.ReadApplicationData(buffer.Span[..count]);
                }
                if (_protocol.IsClosed)
                    return 0;
            }
            finally { _engineGate.Release(); }

            var received = await _network.ReadAsync(_incoming, ct).ConfigureAwait(false);
            await _engineGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (received == 0)
                {
                    _protocol.CloseInput();
                    return 0;
                }
                _protocol.OfferInput(_incoming, 0, received);
                // Key updates and close alerts also generate output on a read.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
                deadline.CancelAfter(WriteTimeout);
                await DrainOutputAsync(deadline.Token).ConfigureAwait(false);
            }
            finally { _engineGate.Release(); }
        }
    }

    private async ValueTask WriteApplicationAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        await _engineGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (!buffer.IsEmpty)
            {
                var length = Math.Min(buffer.Length, 16 * 1024);
                _protocol.WriteApplicationData(buffer.Span[..length]);
                await DrainOutputAsync(ct).ConfigureAwait(false);
                buffer = buffer[length..];
            }
        }
        finally { _engineGate.Release(); }
    }

    // Called only by the handshake or while holding the engine gate. At most
    // one TLS record is produced per write, so the BC output queue stays bounded.
    private async Task DrainOutputAsync(CancellationToken ct)
    {
        while (_protocol.GetAvailableOutputBytes() > 0)
        {
            var count = _protocol.ReadOutput(_outgoing, 0, _outgoing.Length);
            await _network.WriteAsync(_outgoing.AsMemory(0, count), ct).ConfigureAwait(false);
        }
    }

    // Public protocol strings, not account credentials. BC destroys the key
    // arrays it receives: every TLS 1.2 and external TLS 1.3 PSK gets fresh bytes.
    private static byte[] Key() => Encoding.UTF8.GetBytes("silo-cast-v1");
    private static byte[] Identity() => Encoding.UTF8.GetBytes("silo-cast");

    private sealed class CastPskIdentityManager : TlsPskIdentityManager
    {
        public byte[]? GetHint() => null;
        public byte[]? GetPsk(byte[] identity) => identity.AsSpan().SequenceEqual(Identity()) ? Key() : null;
    }

    private sealed class CastPskServer(BcTlsCrypto crypto) : PskTlsServer(crypto, new CastPskIdentityManager())
    {
        protected override ProtocolVersion[] GetSupportedVersions() => [ProtocolVersion.TLSv13, ProtocolVersion.TLSv12];
        protected override int[] GetSupportedCipherSuites() => [CipherSuite.TLS_AES_128_GCM_SHA256, CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256];
        protected override bool AllowCertificateStatus() => false;
        public override TlsPskExternal? GetExternalPsk(IList<PskIdentity> identities) =>
            identities.Any(candidate => candidate.Identity.AsSpan().SequenceEqual(Identity()))
                ? new BasicTlsPskExternal(Identity(), crypto.CreateSecret(Key()), PrfAlgorithm.tls13_hkdf_sha256)
                : null;
    }

    private sealed class ApplicationStream(SiloCastConnection connection) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => connection.ReadApplicationAsync(buffer, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => connection.WriteApplicationAsync(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
