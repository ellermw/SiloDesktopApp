using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.SiloCast;
using SiloPlayer.Services;
using SiloPlayer.Services.SiloCast;

[CollectionDefinition("Receiver", DisableParallelization = true)]
public sealed class ReceiverCollection;

[Collection("Receiver")]
public sealed class ReceiverTests
{
    [Fact]
    public async Task ReplacementHandoffWaitsWhenTwoCanceledRequestsAreStillCleaningUp()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        var first = f.EnqueueApproval("profile-A", blocked: true);
        first.HoldCleanup = true;
        var second = f.EnqueueApproval("profile-B", blocked: true);
        second.HoldCleanup = true;
        var third = f.EnqueueApproval("profile-C");
        try
        {
            await phone.SendAsync(Offer("request-A", "profile-A"));
            await first.PollEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await phone.SendAsync(Offer("request-B", "profile-B"));
            await first.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await second.PollEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await phone.SendAsync(Offer("request-C", "profile-C"));
            await second.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await phone.BarrierAsync(); // Approval cleanup must leave the pong/read path live.
            await Task.Delay(100);
            Assert.False(third.PollEntered.Task.IsCompleted);
        }
        finally
        {
            first.CleanupPermit.TrySetResult();
            second.CleanupPermit.TrySetResult();
        }
        var ready = await phone.WaitAsync(m => Type(m) == "handoff_ready");
        Assert.Equal("request-C", ready["handoffReady"]!["requestId"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConcurrentControllerCloseWaitsForItsPendingConnectionCleanup()
    {
        await using var f = await ReceiverFixture.StartAsync();
        await f.ConnectAsync();
        var controller = ReceiverFixture.Field(f.Receiver, "_controller")!;
        var connection = (SiloCastConnection)controller.GetType().GetProperty("Connection")!.GetValue(controller)!;
        var writeGate = (SemaphoreSlim)ReceiverFixture.Field(connection, "_writeGate")!;
        var close = typeof(SiloCastReceiverService).GetMethod("CloseControllerAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await writeGate.WaitAsync();
        Task? first = null;
        Task? second = null;
        try
        {
            first = (Task)close.Invoke(f.Receiver, [controller, true])!;
            second = (Task)close.Invoke(f.Receiver, [controller, false])!;
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted); // Candidate cleanup must not dispose lifetime before the first closer drains.
        }
        finally
        {
            writeGate.Release();
            if (first != null) await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (second != null) await second.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SupersedingHandoffCannotReauthorizeAlreadyQueuedLaunch()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAsync(phone, "request-A", "profile-A");
        var operations = (SemaphoreSlim)ReceiverFixture.Field(f.Receiver, "_operations")!;
        await operations.WaitAsync();
        try
        {
            await phone.SendAsync(Launch("movie-A"));
            await phone.BarrierAsync(); // Read loop has accepted launch A, but cannot execute it.
            await f.PrepareAsync(phone, "request-B", "profile-B");
        }
        finally { operations.Release(); }
        await phone.WaitAsync(m => Type(m) == "error");
        Assert.Empty(f.Player.Launches);
        await phone.SendAsync(Launch("movie-B"));
        await ReceiverFixture.EventuallyAsync(() => f.Player.Launches.Count == 1);
        Assert.Equal(("movie-B", "profile-B"), Assert.Single(f.Player.Launches));
    }

    [Fact]
    public async Task OldCancelCannotCancelNewPendingHandoff()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        var approval = f.EnqueueApproval("profile-B", blocked: true);
        await phone.SendAsync(Offer("request-B", "profile-B"));
        await approval.PollEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await phone.SendAsync(SiloCastProtocol.CreateMessage("handoff_cancel", "handoffCancel", new JsonObject
            { ["requestId"] = "request-A", ["reason"] = "canceled" }));
        await phone.BarrierAsync();
        Assert.False(approval.PollToken.IsCancellationRequested);
        approval.Permit.TrySetResult();
        var ready = await phone.WaitAsync(m => Type(m) == "handoff_ready");
        Assert.Equal("request-B", ready["handoffReady"]!["requestId"]!.GetValue<string>());
    }

    [Fact]
    public async Task MatchingCancelCancelsPendingApprovalAndCannotLaunch()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        var approval = f.EnqueueApproval("profile-A", blocked: true);
        await phone.SendAsync(Offer("request-A", "profile-A"));
        await approval.PollEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await phone.SendAsync(SiloCastProtocol.CreateMessage("handoff_cancel", "handoffCancel", new JsonObject
            { ["requestId"] = "request-A", ["reason"] = "canceled" }));
        await phone.BarrierAsync();
        Assert.True(approval.PollToken.IsCancellationRequested);
        await phone.SendAsync(Launch("movie-A"));
        await phone.WaitAsync(m => Type(m) == "error");
        Assert.Empty(f.Player.Launches);
    }

    [Fact]
    public async Task CrossServerPhoneLosesControlWhenLocalAuthorityIsRestored()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        f.Player.Close();
        f.Player.PlayLocal("local-movie");
        await phone.SendAsync(SiloCastProtocol.CreateMessage("control", "control", new JsonObject { ["name"] = "pause" }));
        await phone.WaitAsync(m => Type(m) == "error");
        Assert.Empty(f.Player.Controls);
        Assert.Equal("local-profile", f.Player.ActiveAuthority.ApiClient.ProfileId);
        Assert.Equal("local-movie", f.Player.ContentId);
    }

    [Fact]
    public async Task QueuedControlCannotActOnReplacementLocalPlayback()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        var operations = (SemaphoreSlim)ReceiverFixture.Field(f.Receiver, "_operations")!;
        await operations.WaitAsync();
        try
        {
            await phone.SendAsync(SiloCastProtocol.CreateMessage("control", "control", new JsonObject { ["name"] = "pause" }));
            await phone.BarrierAsync();
            f.Player.Close();
            f.Player.PlayLocal("replacement-local");
        }
        finally { operations.Release(); }
        await phone.WaitAsync(m => Type(m) == "error");
        Assert.Empty(f.Player.Controls);
        Assert.Equal("replacement-local", f.Player.ContentId);
    }

    [Fact]
    public async Task CancelingReplacementHandoffLeavesExistingPlaybackAndAuthorityIntact()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        var authority = f.Player.ActiveAuthority;
        var approval = f.EnqueueApproval("replacement-profile", blocked: true);
        await phone.SendAsync(Offer("replacement-request", "replacement-profile"));
        await approval.PollEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await phone.SendAsync(SiloCastProtocol.CreateMessage("handoff_cancel", "handoffCancel", new JsonObject
            { ["requestId"] = "replacement-request", ["reason"] = "canceled" }));
        await phone.BarrierAsync();
        Assert.True(approval.PollToken.IsCancellationRequested);
        Assert.Same(authority, f.Player.ActiveAuthority);
        Assert.True(authority.Auth.IsLoggedIn);
        Assert.Equal("remote-movie", f.Player.ContentId);
        Assert.Equal(0, f.Player.CloseCalls);
    }

    [Fact]
    public async Task SameControllerAndProfileReuseApprovedAuthorityWithoutAnotherHttpHandoff()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        var originalAuth = f.Player.ActiveAuthority.Auth;
        // No queued HTTP approval: attempting another real handoff fails the test fixture.
        await phone.SendAsync(Offer("reuse-request", "remote-profile"));
        var ready = await phone.WaitAsync(m => Type(m) == "handoff_ready");
        Assert.True(ready["handoffReady"]!["reused"]!.GetValue<bool>());
        Assert.Equal("reuse-request", ready["handoffReady"]!["requestId"]!.GetValue<string>());
        await phone.SendAsync(Launch("second-remote-movie"));
        await ReceiverFixture.EventuallyAsync(() => f.Player.Launches.Count == 2);
        Assert.Same(originalAuth, f.Player.ActiveAuthority.Auth);
        Assert.True(originalAuth.IsLoggedIn);
        Assert.Equal("second-remote-movie", f.Player.ContentId);
    }

    [Fact]
    public async Task DroppedTcpConnectionPreservesAlreadyAuthorizedPlayback()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        var authority = f.Player.ActiveAuthority;
        await phone.DisposeAsync();
        await ReceiverFixture.EventuallyAsync(() => ReceiverFixture.Field(f.Receiver, "_controller") == null);
        Assert.Equal(0, f.Player.CloseCalls);
        Assert.Same(authority, f.Player.ActiveAuthority);
        Assert.True(authority.Auth.IsLoggedIn);
        Assert.Equal("remote-movie", f.Player.ContentId);
    }

    [Fact]
    public async Task WireCloseDisconnectsControlWhileAuthorizedPlaybackContinues()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        var authority = f.Player.ActiveAuthority;
        await phone.SendAsync(SiloCastProtocol.CreateMessage("close"));
        await ReceiverFixture.EventuallyAsync(() => ReceiverFixture.Field(f.Receiver, "_controller") == null);
        Assert.Equal(0, f.Player.CloseCalls);
        Assert.Same(authority, f.Player.ActiveAuthority);
        Assert.Equal("remote-movie", f.Player.ContentId);
        Assert.True(authority.Auth.IsLoggedIn);
    }

    [Fact]
    public async Task EffectiveDeploymentIdentityAppearsInTxtAndReplacementHello()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        await ReceiverFixture.EventuallyAsync(() => f.Advertisement.Publications.Any(t => t["server"] == RemoteId));
        var txt = f.Advertisement.Publications.Last(t => t["server"] == RemoteId);
        Assert.Equal("remote-deployment", txt["serverIdentity"]);
        var replacement = await f.ConnectAsync();
        Assert.Equal(RemoteId, replacement.Hello["hello"]!["serverId"]!.GetValue<string>());
        Assert.Equal("remote-deployment", replacement.Hello["hello"]!["serverIdentity"]!.GetValue<string>());
    }

    [Fact]
    public async Task StoppingRemotePlaybackPublishesIdleAndRestoresLocalServerIdentity()
    {
        await using var f = await ReceiverFixture.StartAsync();
        var phone = await f.ConnectAsync();
        await f.PrepareAndLaunchAsync(phone);
        await ReceiverFixture.EventuallyAsync(() => f.Advertisement.Publications.Last()["playing"] == "1");
        f.Player.Close();
        await ReceiverFixture.EventuallyAsync(() => f.Advertisement.Publications.Last()["playing"] == "0");
        var txt = f.Advertisement.Publications.Last();
        Assert.Equal(LocalId, txt["server"]);
        Assert.Equal("local-deployment", txt["serverIdentity"]);
    }

    [Fact]
    public async Task LocalPlayerStateChangesUpdatePlayingDiscoveryFlag()
    {
        await using var f = await ReceiverFixture.StartAsync();
        Assert.Equal("0", f.Advertisement.Publications.Last()["playing"]);
        f.Player.PlayLocal("local-movie");
        await ReceiverFixture.EventuallyAsync(() => f.Advertisement.Publications.Last()["playing"] == "1");
        f.Player.Close();
        await ReceiverFixture.EventuallyAsync(() => f.Advertisement.Publications.Last()["playing"] == "0");
    }

    internal const string RemoteUrl = "https://remote.test";
    internal const string LocalUrl = "https://local.test";
    internal static string RemoteId => SiloCastProtocol.ServerId(RemoteUrl);
    internal static string LocalId => SiloCastProtocol.ServerId(LocalUrl);
    internal static string? Type(JsonObject m) => m["type"]?.GetValue<string>();
    internal static JsonObject Offer(string request, string profile) => SiloCastProtocol.CreateMessage("handoff_offer", "handoffOffer", new JsonObject
    {
        ["requestId"] = request, ["profileId"] = profile, ["profileName"] = profile,
        ["serverURL"] = RemoteUrl, ["serverId"] = RemoteId, ["serverIdentity"] = "remote-deployment", ["serverName"] = "Remote"
    });
    internal static JsonObject Launch(string content) => SiloCastProtocol.CreateMessage("launch", "launch", new JsonObject
    {
        ["serverId"] = RemoteId, ["playback"] = new JsonObject { ["contentId"] = content, ["startFromBeginning"] = false }
    });
}

internal sealed class ReceiverFixture : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SiloCastReceiverTests", Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<Approval> _approvals = [];
    private readonly List<PhonePeer> _phones = [];
    private readonly HttpClient _localHttp;
    private readonly AuthService _localAuth;
    public SiloCastReceiverService Receiver { get; }
    public PlayerService Player { get; }
    public SiloCastAdvertisement Advertisement => (SiloCastAdvertisement)Field(Receiver, "_advertisement")!;
    private ReceiverFixture()
    {
        _localHttp = new HttpClient(new HandoffServer(null, "local-deployment"));
        var api = new SiloApiClient(_localHttp);
        api.SetBaseUrl(ReceiverTests.LocalUrl);
        _localAuth = new AuthService(api, new AuthApi(api));
        _localAuth.SetTokens("synthetic-local-access", "synthetic-local-refresh", 86400);
        _localAuth.SetCurrentUser(new UserInfo { Id = "local-user", Username = "Local" });
        _localAuth.SelectProfile("local-profile", "synthetic-local-proof");
        Player = new PlayerService(new PlaybackAuthorityScope(api, _localAuth));
        var settings = new SettingsService(_directory);
        settings.Load().SiloCastEnabled = true;
        settings.Load().DeviceId = "test-pc";
        var handoff = new SiloCastHandoff(() => new HandoffServer(_approvals.TryDequeue(out var approval)
            ? approval : throw new InvalidOperationException("Unexpected handoff."), "remote-deployment"));
        Receiver = new SiloCastReceiverService(settings, _localAuth, api, Player, handoff);
    }
    public static async Task<ReceiverFixture> StartAsync()
    {
        var fixture = new ReceiverFixture();
        fixture.Receiver.Initialize(new Microsoft.UI.Dispatching.DispatcherQueue());
        try { await EventuallyAsync(() => fixture.Advertisement.Port != 0); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }
    public async Task<PhonePeer> ConnectAsync()
    {
        var peer = await PhonePeer.ConnectAsync(Advertisement.Port);
        _phones.Add(peer);
        await EventuallyAsync(() => Field(Receiver, "_controller") != null);
        return peer;
    }
    public Approval EnqueueApproval(string profile, bool blocked = false)
    {
        var approval = new Approval(profile);
        if (!blocked) approval.Permit.TrySetResult();
        _approvals.Enqueue(approval);
        return approval;
    }
    public async Task PrepareAsync(PhonePeer peer, string request, string profile)
    {
        EnqueueApproval(profile);
        await peer.SendAsync(ReceiverTests.Offer(request, profile));
        var ready = await peer.WaitAsync(m => ReceiverTests.Type(m) == "handoff_ready" && m["handoffReady"]?["requestId"]?.GetValue<string>() == request);
        Assert.Equal(profile, ready["handoffReady"]!["profileId"]!.GetValue<string>());
    }
    public async Task PrepareAndLaunchAsync(PhonePeer peer)
    {
        await PrepareAsync(peer, "request-remote", "remote-profile");
        await peer.SendAsync(ReceiverTests.Launch("remote-movie"));
        await EventuallyAsync(() => Player.Launches.Count == 1);
    }
    public static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value);
    public static async Task EventuallyAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, deadline.Token);
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var phone in _phones) await phone.DisposeAsync();
        await Receiver.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        _localAuth.Dispose();
        _localHttp.Dispose();
        var directory = Path.GetFullPath(_directory);
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SiloCastReceiverTests")) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup escaped its temporary directory.");
        Directory.Delete(directory, recursive: true);
    }
}

internal sealed class Approval(string profile)
{
    public string Profile { get; } = profile;
    public TaskCompletionSource Permit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource PollEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken PollToken { get; set; }
    public bool HoldCleanup { get; set; }
    public TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CleanupPermit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class HandoffServer(Approval? approval, string deployment) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        JsonObject document;
        var status = HttpStatusCode.OK;
        switch (request.RequestUri!.AbsolutePath)
        {
            case "/api/v2/system/identity": document = new() { ["server_id"] = deployment }; break;
            case "/api/v2/auth/device/capability": document = new()
                { ["state"] = "available", ["allowed"] = true, ["remote_playback_handoff"] = true, ["protocol_versions"] = new JsonArray(2), ["cancel"] = true }; break;
            case "/api/v2/auth/device/start":
                status = HttpStatusCode.Created;
                document = new() { ["device_code"] = "synthetic-device", ["user_code"] = "1111-2222", ["match_code"] = "test phrase",
                    ["expires_at"] = now.AddMinutes(15).ToString("O"), ["expires_in"] = 900, ["interval"] = 1, ["temporary"] = true, ["client_purpose"] = "remote_playback" }; break;
            case "/api/v2/auth/device/poll":
                approval!.PollToken = ct;
                approval.PollEntered.TrySetResult();
                await approval.Permit.Task.WaitAsync(ct);
                document = new() { ["status"] = "approved", ["temporary"] = true, ["profile_id"] = approval.Profile,
                    ["profile_token"] = "synthetic-proof", ["session_expires_at"] = now.AddHours(2).ToString("O"),
                    ["tokens"] = new JsonObject { ["access_token"] = "synthetic-remote-access", ["refresh_token"] = "synthetic-remote-refresh", ["expires_in"] = 3600,
                        ["user"] = new JsonObject { ["id"] = "remote-user", ["username"] = "Remote", ["role"] = "user" } } }; break;
            case "/api/v2/auth/device/cancel":
                if (approval is { HoldCleanup: true })
                {
                    approval.CleanupEntered.TrySetResult();
                    await approval.CleanupPermit.Task.WaitAsync(ct);
                }
                document = new(); break;
            default: throw new InvalidOperationException("Unexpected test HTTP endpoint " + request.RequestUri.AbsolutePath);
        }
        return new HttpResponseMessage(status) { Content = new StringContent(document.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}

internal sealed class PhonePeer : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private readonly Channel<JsonObject> _received = Channel.CreateUnbounded<JsonObject>();
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly SemaphoreSlim _engine = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TlsClientProtocol _tls = new();
    private readonly byte[] _incoming = new byte[65536];
    private readonly byte[] _outgoing = new byte[65536];
    private Stream _application = null!;
    private Task _reader = Task.CompletedTask;
    private int _disposed;
    public JsonObject Hello { get; private set; } = null!;
    public static async Task<PhonePeer> ConnectAsync(int port)
    {
        var peer = new PhonePeer();
        await peer._tcp.ConnectAsync(IPAddress.Loopback, port);
        using var handshakeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        peer._tls.Connect(new PhonePsk());
        await peer.DrainAsync(handshakeDeadline.Token);
        while (!peer._tls.IsConnected)
        {
            var count = await peer._tcp.GetStream().ReadAsync(peer._incoming, handshakeDeadline.Token);
            if (count == 0) throw new EndOfStreamException("Receiver closed TLS handshake.");
            peer._tls.OfferInput(peer._incoming, 0, count);
            await peer.DrainAsync(handshakeDeadline.Token);
        }
        peer._application = new PeerApplicationStream(peer);
        peer._reader = Task.Run(peer.ReadLoopAsync);
        peer.Hello = await peer.WaitAsync(m => ReceiverTests.Type(m) == "hello");
        await peer.SendAsync(SiloCastProtocol.CreateMessage("hello", "hello", new JsonObject
        {
            ["role"] = "phone", ["deviceId"] = "phone-device", ["deviceName"] = "Test phone",
            ["serverId"] = ReceiverTests.RemoteId, ["serverIdentity"] = "remote-deployment", ["supportedVersions"] = new JsonArray(2)
        }));
        await peer.BarrierAsync();
        return peer;
    }
    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await SiloCastFrameCodec.ReadAsync(_application, _lifetime.Token);
                if (message == null) break;
                if (ReceiverTests.Type(message) == "ping") await SendAsync(SiloCastProtocol.CreateMessage("pong"));
                else await _received.Writer.WriteAsync(message);
            }
        }
        catch (Exception ex) { _received.Writer.TryComplete(ex); }
        finally { _received.Writer.TryComplete(); }
    }
    public async Task SendAsync(JsonObject message)
    {
        await _write.WaitAsync();
        try { await SiloCastFrameCodec.WriteAsync(_application, message, _lifetime.Token); }
        finally { _write.Release(); }
    }
    public async Task<JsonObject> WaitAsync(Func<JsonObject, bool> matches)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var message in _received.Reader.ReadAllAsync(timeout.Token)) if (matches(message)) return message;
        throw new EndOfStreamException("Phone connection closed before expected response.");
    }
    public async Task BarrierAsync()
    {
        await SendAsync(SiloCastProtocol.CreateMessage("ping"));
        await WaitAsync(m => ReceiverTests.Type(m) == "pong");
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _tcp.Dispose();
        try { await _reader.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        _lifetime.Dispose();
    }
    private async ValueTask<int> ReadApplicationAsync(Memory<byte> buffer, CancellationToken ct)
    {
        while (true)
        {
            await _engine.WaitAsync(ct);
            try
            {
                var count = Math.Min(buffer.Length, _tls.GetAvailableInputBytes());
                if (count > 0) return _tls.ReadApplicationData(buffer.Span[..count]);
                if (_tls.IsClosed) return 0;
            }
            finally { _engine.Release(); }
            var received = await _tcp.GetStream().ReadAsync(_incoming, ct);
            if (received == 0) return 0;
            await _engine.WaitAsync(ct);
            try { _tls.OfferInput(_incoming, 0, received); await DrainAsync(ct); }
            finally { _engine.Release(); }
        }
    }
    private async ValueTask WriteApplicationAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        await _engine.WaitAsync(ct);
        try { _tls.WriteApplicationData(buffer.Span); await DrainAsync(ct); }
        finally { _engine.Release(); }
    }
    private async Task DrainAsync(CancellationToken ct)
    {
        while (_tls.GetAvailableOutputBytes() > 0)
        {
            var count = _tls.ReadOutput(_outgoing, 0, _outgoing.Length);
            await _tcp.GetStream().WriteAsync(_outgoing.AsMemory(0, count), ct);
        }
    }
    private sealed class PeerApplicationStream(PhonePeer peer) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => peer.ReadApplicationAsync(buffer, ct);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => peer.WriteApplicationAsync(buffer, ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class PhonePsk() : PskTlsClient(new BcTlsCrypto(new SecureRandom()), Encoding.UTF8.GetBytes("silo-cast"), Encoding.UTF8.GetBytes("silo-cast-v1"))
    {
        protected override ProtocolVersion[] GetSupportedVersions() => [ProtocolVersion.TLSv12];
        protected override int[] GetSupportedCipherSuites() => [CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256];
    }
}
