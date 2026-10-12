using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.SiloCast;

namespace SiloPlayer.Services.SiloCast;

/// <summary>App-lifetime receiver; controller connections never replace browsing credentials.</summary>
public sealed class SiloCastReceiverService(SettingsService settings, AuthService localAuth,
    SiloApiClient localApi, PlayerService player, SiloCastHandoff? handoff = null) : IAsyncDisposable
{
    private readonly SiloCastHandoff _handoff = handoff ?? new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly SemaphoreSlim _candidates = new(4, 4);
    private readonly SemaphoreSlim _handoffPreparations = new(2, 2);
    private readonly object _gate = new();
    private readonly List<TcpListener> _listeners = [];
    private readonly SiloCastAdvertisement _advertisement = new();
    private CancellationTokenSource? _listening;
    private SiloCastPlayerAdapter? _adapter;
    private Controller? _controller;
    private SiloCastIdentity? _identity;
    private PlaybackAuthorityScope? _remoteScope;
    private string _serverId = "";
    private string _serverName = "";
    private string? _serverIdentity;
    private int _port;
    private bool _initialized;
    private int _disposed;
    private bool _pendingFullscreen;
    private string _status = "Off";
    private long _playbackGeneration;
    private int _advertisementDirty;
    private readonly CancellationTokenSource _shutdown = new();
    public string Status { get { lock (_gate) return _status; } }
    public bool CanDisconnect { get { lock (_gate) return _controller != null || _remoteScope != null; } }
    public event Action? StatusChanged;

    private sealed class Controller(SiloCastConnection connection, CancellationTokenSource lifetime)
    {
        public SiloCastConnection Connection { get; } = connection;
        public CancellationTokenSource Lifetime { get; } = lifetime;
        public string DeviceId { get; set; } = "";
        public string Name { get; set; } = "Phone";
        public string ServerId { get; set; } = "";
        public string? ServerIdentity { get; set; }
        public bool CanControl { get; set; }
        public DateTimeOffset LastPong { get; set; } = DateTimeOffset.UtcNow;
        public CancellationTokenSource? HandoffCancellation { get; set; }
        public CancellationTokenSource? LaunchCancellation { get; set; }
        public SiloCastIdentity? PreparedIdentity { get; set; }
        public string? ReadyRequest { get; set; }
        public string? PendingRequest { get; set; }
        public long HandoffGeneration { get; set; }
        public object CloseGate { get; } = new();
        public TaskCompletionSource? CloseCompletion;
        public Channel<PendingAction> Actions { get; } = Channel.CreateBounded<PendingAction>(new BoundedChannelOptions(8)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }
    private sealed record PendingAction(JsonObject Message, long HandoffGeneration, string? ReadyRequest,
        SiloCastIdentity? PreparedIdentity, long PlaybackGeneration, PlaybackAuthorityScope Authority);

    public void Initialize(DispatcherQueue dispatcher)
    {
        if (_initialized) return;
        _initialized = true;
        _adapter = new(player, dispatcher);
        localAuth.UserChanged += OnAuthorityChanged;
        localAuth.LoggedOut += OnAuthorityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        player.ContentLoaded += OnContentLoaded;
        player.RemoteAuthorityEnded += OnRemoteAuthorityEnded;
        player.StateChanged += OnPlayerStateChanged;
        player.SessionStarted += OnSessionStarted;
        _ = RefreshSafelyAsync();
        _ = Task.Run(WatchAuthorityAsync);
    }

    private void SetStatus(string value)
    {
        lock (_gate) _status = value;
        try { StatusChanged?.Invoke(); } catch { /* UI observers cannot stop the receiver. */ }
    }

    private void OnAuthorityChanged() => _ = RefreshSafelyAsync();
    private void OnNetworkChanged(object? sender, EventArgs e) => _ = RefreshSafelyAsync();
    private string EffectiveServerId => _identity?.ServerId ?? _serverId;
    private string? EffectiveServerIdentity => _identity == null ? _serverIdentity : _identity.ServerIdentity;
    private void OnPlayerStateChanged(PlayerState state)
    {
        lock (_gate) { if (state == PlayerState.Idle) _playbackGeneration++; }
        QueueAdvertisementUpdate();
    }
    private void OnSessionStarted(string sessionId)
    {
        lock (_gate) { _playbackGeneration++; ReconcileController(); }
        QueueAdvertisementUpdate();
    }
    private void ReconcileController()
    {
        if (_controller != null) _controller.CanControl = SameServer(_controller, EffectiveServerId);
    }
    // State transitions are coalesced and DNS callbacks never execute on the UI thread.
    private void QueueAdvertisementUpdate()
    {
        if (_disposed != 0 || Interlocked.Exchange(ref _advertisementDirty, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, _shutdown.Token);
                await _lifecycle.WaitAsync(_shutdown.Token);
                try
                {
                    Interlocked.Exchange(ref _advertisementDirty, 0);
                    if (_disposed == 0 && _listening != null) await AdvertiseAsync(_listening.Token);
                }
                finally { _lifecycle.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LocalLog.AppendLine("silocast.txt", $"advertisement_update_failed | {ex.GetType().Name}"); }
            finally { Interlocked.Exchange(ref _advertisementDirty, 0); }
        });
    }

    public Task RefreshAsync() => Task.Run(RefreshCoreAsync);

    private async Task RefreshCoreAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed != 0) return;
            await StopListenerAsync();
            if (!settings.Load().SiloCastEnabled) { await EndRemotePlaybackAsync(); SetStatus("Off"); return; }
            if (!localAuth.IsLoggedIn || string.IsNullOrWhiteSpace(localApi.BaseUrl))
            { await EndRemotePlaybackAsync(); SetStatus("Sign in to enable SiloCast"); return; }
            var addresses = SiloCastNetwork.GetEligibleAddresses();
            if (addresses.Count == 0) { SetStatus("No private local network available"); return; }
            _serverId = SiloCastProtocol.ServerId(localApi.BaseUrl);
            _serverName = settings.Load().Servers.FirstOrDefault(s => ServerUrlIdentity.Normalize(s.Url) == localApi.BaseUrl)?.Name ?? "Silo";
            _serverIdentity = null;
            try
            {
                using var identityTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var identity = await localApi.GetUnauthenticatedAsync<JsonObject>("/api/v2/system/identity", identityTimeout.Token);
                _serverIdentity = identity["server_id"]?.GetValue<string>();
            }
            catch { /* URL identity supports older servers and transient discovery failure. */ }
            _listening = new CancellationTokenSource();
            foreach (var address in addresses)
            {
                var listener = new TcpListener(address, _listeners.Count == 0 ? 0 : _port);
                listener.Start(4);
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _listeners.Add(listener);
                _ = AcceptLoopAsync(listener, _listening.Token);
            }
            await AdvertiseAsync(_listening.Token);
            SetStatus("Ready — choose this PC from Silo on your phone");
        }
        catch
        {
            await StopListenerAsync();
            SetStatus("SiloCast unavailable — check your private network and firewall");
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    private async Task RefreshSafelyAsync()
    {
        try { await RefreshAsync(); }
        catch (Exception ex) { LocalLog.AppendLine("silocast.txt", $"receiver_start_failed | {ex.GetType().Name}"); }
    }

    private Task AdvertiseAsync(CancellationToken ct)
    {
        var config = settings.Load();
        Dictionary<string, string> txt;
        lock (_gate)
        {
            txt = new Dictionary<string, string>
            {
                ["v"] = "2", ["name"] = Environment.MachineName, ["id"] = config.DeviceId ?? "silo-windows",
                ["server"] = EffectiveServerId, ["serverName"] = _identity?.ServerName ?? _serverName,
                ["playing"] = player.State != PlayerState.Idle ? "1" : "0"
            };
            if (EffectiveServerIdentity is { } effectiveIdentity) txt["serverIdentity"] = effectiveIdentity;
        }
        return _advertisement.StartAsync(_port, txt, ct);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcp = await listener.AcceptTcpClientAsync(ct);
                if (!_candidates.Wait(0)) { tcp.Dispose(); continue; }
                _ = HandleCandidateAsync(tcp, ct);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { LocalLog.AppendLine("silocast.txt", $"listener_ended | {ex.GetType().Name}"); }
    }

    private async Task HandleCandidateAsync(TcpClient tcp, CancellationToken listenerToken)
    {
        Controller? candidate = null;
        CancellationTokenSource? lifetime = null;
        try
        {
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(listenerToken);
            var connection = await SiloCastConnection.AcceptAsync(tcp, lifetime.Token);
            candidate = new(connection, lifetime);
            JsonObject receiverHello;
            lock (_gate) receiverHello = new JsonObject
            {
                ["role"] = "tv", ["deviceName"] = Environment.MachineName,
                ["deviceId"] = settings.Load().DeviceId, ["serverId"] = EffectiveServerId,
                ["serverName"] = _identity?.ServerName ?? _serverName,
                ["serverIdentity"] = EffectiveServerIdentity, ["supportedVersions"] = new JsonArray(2)
            };
            await SendAsync(candidate, "hello", "hello", receiverHello);
            using var helloDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            helloDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            var first = await connection.ReadAsync(helloDeadline.Token);
            var hello = first?["hello"] as JsonObject;
            if (first?["type"]?.GetValue<string>() != "hello" || hello?["role"]?.GetValue<string>() != "phone" ||
                hello["supportedVersions"] is not JsonArray versions || !versions.Any(v => v?.GetValue<int>() == 2))
                throw new ArgumentException("Unsupported phone hello.");
            candidate.DeviceId = RequiredText(hello, "deviceId");
            candidate.Name = RequiredText(hello, "deviceName");
            candidate.ServerId = RequiredText(hello, "serverId");
            candidate.ServerIdentity = hello["serverIdentity"]?.GetValue<string>();
            candidate.CanControl = SameServer(candidate, EffectiveServerId);
            Controller? old;
            lock (_gate) { old = _controller; _controller = candidate; }
            if (old != null) await CloseControllerAsync(old, sendClose: true);
            SetStatus($"Connected to {candidate.Name}");
            var updates = UpdateLoopAsync(candidate);
            var actions = ActionLoopAsync(candidate);
            try
            {
                while (IsOwner(candidate))
                {
                    var message = await connection.ReadAsync(lifetime.Token);
                    if (message == null) break;
                    await HandleMessageAsync(candidate, message);
                }
            }
            finally
            {
                lifetime.Cancel();
                try { await updates; } catch { }
                try { await actions; } catch { }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { LocalLog.AppendLine("silocast.txt", $"connection_ended | {ex.GetType().Name}"); }
        catch (OperationCanceledException) { }
        finally
        {
            if (candidate != null)
            {
                lock (_gate) { if (ReferenceEquals(_controller, candidate)) _controller = null; }
                await CloseControllerAsync(candidate, sendClose: false);
                lock (_gate)
                {
                    if (_controller == null && _disposed == 0 && _listening != null)
                        SetStatus("Ready — phone disconnected");
                }
            }
            tcp.Dispose();
            lifetime?.Dispose();
            _candidates.Release();
        }
    }

    private bool SameServer(Controller c, string id) => c.ServerId == id ||
        (!string.IsNullOrWhiteSpace(EffectiveServerIdentity) && c.ServerIdentity == EffectiveServerIdentity);
    private bool IsOwner(Controller c) { lock (_gate) return ReferenceEquals(_controller, c) && !c.Lifetime.IsCancellationRequested; }
    private static string RequiredText(JsonObject value, string key)
    {
        var text = value[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048) throw new ArgumentException($"Invalid {key}.");
        return text;
    }
    private Task SendAsync(Controller c, string type, string? key = null, JsonObject? payload = null)
        => c.Connection.SendAsync(SiloCastProtocol.CreateMessage(type, key, payload), c.Lifetime.Token);

    private async Task HandleMessageAsync(Controller c, JsonObject message)
    {
        if (!IsOwner(c)) return;
        try
        {
            switch (message["type"]?.GetValue<string>())
            {
                case "pong": c.LastPong = DateTimeOffset.UtcNow; break;
                case "ping": await SendAsync(c, "pong"); break;
                case "handoff_offer":
                    var offer = message["handoffOffer"] as JsonObject ?? throw new ArgumentException("Missing handoff.");
                    var requestId = RequiredText(offer, "requestId");
                    CancellationTokenSource handoffOwner;
                    lock (_gate)
                    {
                        c.HandoffCancellation?.Cancel();
                        c.LaunchCancellation?.Cancel();
                        handoffOwner = c.HandoffCancellation = CancellationTokenSource.CreateLinkedTokenSource(c.Lifetime.Token);
                        c.HandoffGeneration++;
                        c.PendingRequest = requestId;
                        c.ReadyRequest = null;
                        if (!ReferenceEquals(c.PreparedIdentity, _identity)) c.PreparedIdentity?.Dispose();
                        c.PreparedIdentity = null;
                    }
                    _ = PrepareHandoffAsync(c, (JsonObject)offer.DeepClone(), handoffOwner);
                    break;
                case "handoff_cancel":
                    lock (_gate)
                    {
                        var canceledId = message["handoffCancel"]?["requestId"]?.GetValue<string>();
                        if (canceledId != null && canceledId == c.PendingRequest)
                        {
                            c.HandoffCancellation?.Cancel();
                            c.LaunchCancellation?.Cancel();
                            c.HandoffGeneration++;
                            c.PendingRequest = c.ReadyRequest = null;
                            if (!ReferenceEquals(c.PreparedIdentity, _identity)) c.PreparedIdentity?.Dispose();
                            c.PreparedIdentity = null;
                        }
                    }
                    break;
                case "launch":
                case "control":
                    lock (_gate)
                    {
                        if (!c.Actions.Writer.TryWrite(new PendingAction((JsonObject)message.DeepClone(),
                            c.HandoffGeneration, c.ReadyRequest, c.PreparedIdentity, _playbackGeneration, player.ActiveAuthority)))
                            throw new InvalidOperationException("Too many pending remote actions.");
                    }
                    break;
                case "close":
                    c.Lifetime.Cancel(); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (IsOwner(c)) await SendAsync(c, "error", "error", new JsonObject
                { ["code"] = "command_failed", ["message"] = "The requested action could not be completed." });
        }
    }

    // Read/pong handling must continue while server preparation or quality changes await I/O.
    private async Task ActionLoopAsync(Controller c)
    {
        try
        {
            await foreach (var action in c.Actions.Reader.ReadAllAsync(c.Lifetime.Token))
            {
                if (!IsOwner(c)) return;
                try
                {
                    var message = action.Message;
                    if (message["type"]?.GetValue<string>() == "launch")
                        await LaunchAsync(c, action);
                    else
                    {
                        await _operations.WaitAsync(c.Lifetime.Token);
                        try
                        {
                            if (!CanApplyControl(c, action)) throw new InvalidOperationException("Playback authority changed.");
                            await _adapter!.ControlAsync(message["control"] as JsonObject ?? throw new ArgumentException("Missing command."),
                                () => CanApplyControl(c, action), c.Lifetime.Token);
                        }
                        finally { _operations.Release(); }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LocalLog.AppendLine("silocast.txt", $"remote_action_failed | {ex.GetType().Name}");
                    if (IsOwner(c)) await SendAsync(c, "error", "error", new JsonObject
                        { ["code"] = "command_failed", ["message"] = "The requested action could not be completed." });
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { c.Lifetime.Cancel(); }
    }

    private bool CanApplyControl(Controller c, PendingAction action)
    {
        lock (_gate) return IsOwner(c) && c.CanControl && SameServer(c, EffectiveServerId) &&
            action.PlaybackGeneration == _playbackGeneration && ReferenceEquals(action.Authority, player.ActiveAuthority);
    }

    private async Task PrepareHandoffAsync(Controller c, JsonObject offer, CancellationTokenSource owner)
    {
        SiloCastIdentity? prepared = null;
        var entered = false;
        try
        {
            // Canceled approvals can still spend two seconds withdrawing their device code.
            // Include that cleanup in the bound; newer offers cancel any waiting predecessor.
            await _handoffPreparations.WaitAsync(owner.Token);
            entered = true;
            owner.Token.ThrowIfCancellationRequested();
            var active = _identity;
            if (active != null && active.Auth.IsLoggedIn && active.Auth.SelectedProfileId == active.ProfileId && active.ExpiresAt > DateTimeOffset.UtcNow &&
                active.ControllerDeviceId == c.DeviceId && active.ProfileId == offer["profileId"]?.GetValue<string>() &&
                active.ServerId == offer["serverId"]?.GetValue<string>() &&
                active.ServerId == SiloCastProtocol.ServerId(RequiredText(offer, "serverURL")))
            {
                lock (_gate)
                {
                    if (!IsOwner(c) || owner.IsCancellationRequested || !ReferenceEquals(c.HandoffCancellation, owner) ||
                        !ReferenceEquals(active, _identity) || !active.Auth.IsLoggedIn) return;
                    if (offer["serverIdentity"]?.GetValue<string>() is { Length: > 0 } deployment && active.ServerIdentity != deployment)
                        throw new InvalidOperationException("Deployment identity changed.");
                    if (!ReferenceEquals(c.PreparedIdentity, active) && !ReferenceEquals(c.PreparedIdentity, _identity)) c.PreparedIdentity?.Dispose();
                    c.PreparedIdentity = active;
                    c.ReadyRequest = RequiredText(offer, "requestId");
                }
                await SendReadyAsync(c, active, RequiredText(offer, "requestId"), reused: true);
                return;
            }
            prepared = await _handoff.PrepareAsync(offer, Environment.MachineName,
                settings.Load().DeviceId ?? "silo-windows", async challenge =>
                { if (IsOwner(c) && !owner.IsCancellationRequested && ReferenceEquals(c.HandoffCancellation, owner)) await SendAsync(c, "handoff_challenge", "handoffChallenge", challenge); }, owner.Token);
            var readyIdentity = prepared;
            lock (_gate)
            {
                if (!IsOwner(c) || owner.IsCancellationRequested || !ReferenceEquals(c.HandoffCancellation, owner)) return;
                if (!ReferenceEquals(c.PreparedIdentity, _identity)) c.PreparedIdentity?.Dispose();
                c.PreparedIdentity = prepared;
                c.PreparedIdentity.ControllerDeviceId = c.DeviceId;
                prepared = null;
                c.ReadyRequest = RequiredText(offer, "requestId");
            }
            await SendReadyAsync(c, readyIdentity, RequiredText(offer, "requestId"), reused: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsOwner(c) && ReferenceEquals(c.HandoffCancellation, owner))
                try { await SendAsync(c, "handoff_cancel", "handoffCancel", new JsonObject
                    { ["requestId"] = offer["requestId"]?.GetValue<string>(),
                      ["reason"] = ex is SiloCastHandoffException handoff ? handoff.Code : "handoff_failed",
                      ["message"] = "Profile handoff could not be completed. Check the server connection and try again." }); } catch { }
        }
        finally
        {
            prepared?.Dispose();
            if (entered) _handoffPreparations.Release();
            lock (_gate) { if (ReferenceEquals(c.HandoffCancellation, owner)) c.HandoffCancellation = null; }
            owner.Dispose();
        }
    }

    private Task SendReadyAsync(Controller c, SiloCastIdentity identity, string requestId, bool reused)
        => SendAsync(c, "handoff_ready", "handoffReady", new JsonObject
        {
            ["requestId"] = requestId, ["serverId"] = identity.ServerId, ["profileId"] = identity.ProfileId,
            ["sessionExpiresAt"] = identity.ExpiresAt.ToString("O"), ["reused"] = reused
        });

    private async Task LaunchAsync(Controller c, PendingAction action)
    {
        await _operations.WaitAsync(c.Lifetime.Token);
        try
        {
            var launch = action.Message["launch"] as JsonObject ?? throw new ArgumentException("Missing launch.");
            var playback = launch["playback"] as JsonObject ?? throw new ArgumentException("Missing playback.");
            SiloCastIdentity? previous;
            PlaybackAuthorityScope? previousScope;
            SiloCastIdentity identity;
            PlaybackAuthorityScope scope;
            using var launchCancellation = CancellationTokenSource.CreateLinkedTokenSource(c.Lifetime.Token);
            lock (_gate)
            {
                if (!IsOwner(c) || action.HandoffGeneration != c.HandoffGeneration ||
                    action.ReadyRequest == null || action.ReadyRequest != c.ReadyRequest ||
                    !ReferenceEquals(action.PreparedIdentity, c.PreparedIdentity) || c.PreparedIdentity == null ||
                    !c.PreparedIdentity.Auth.IsLoggedIn || c.PreparedIdentity.Auth.SelectedProfileId != c.PreparedIdentity.ProfileId ||
                    c.PreparedIdentity.ExpiresAt <= DateTimeOffset.UtcNow ||
                    launch["serverId"]?.GetValue<string>() != c.PreparedIdentity.ServerId)
                    throw new InvalidOperationException("A fresh profile handoff is required.");
                previous = _identity;
                previousScope = _remoteScope;
                identity = c.PreparedIdentity;
                c.LaunchCancellation = launchCancellation;
                scope = new PlaybackAuthorityScope(identity.ApiClient, identity.Auth,
                    identity.PlaybackApi, identity.CatalogApi, identity.SettingsApi);
                _identity = identity;
                _remoteScope = scope;
                _playbackGeneration++;
                c.PreparedIdentity = null;
                c.ReadyRequest = null;
                c.CanControl = true;
                _pendingFullscreen = true;
            }
            try
            {
                var launched = await _adapter!.LaunchAsync(playback, scope,
                    () => IsOwner(c) && action.HandoffGeneration == c.HandoffGeneration, launchCancellation.Token, c.Name, identity.ProfileName);
                if (!launched || launchCancellation.IsCancellationRequested || !IsOwner(c) || action.HandoffGeneration != c.HandoffGeneration)
                    throw new OperationCanceledException("Launch superseded.");
            }
            catch
            {
                await _adapter!.CloseAsync(scope);
                lock (_gate)
                {
                    if (ReferenceEquals(_identity, identity))
                    {
                        var previousStillPlaying = previousScope != null && ReferenceEquals(player.ActiveAuthority, previousScope);
                        _identity = previousStillPlaying ? previous : null;
                        _remoteScope = previousStillPlaying ? previousScope : null;
                        _pendingFullscreen = false;
                        if (!ReferenceEquals(_identity, identity)) identity.Dispose();
                        ReconcileController();
                    }
                }
                throw;
            }
            finally
            {
                lock (_gate) { if (ReferenceEquals(c.LaunchCancellation, launchCancellation)) c.LaunchCancellation = null; }
                if (!ReferenceEquals(previous, _identity)) previous?.Dispose();
                QueueAdvertisementUpdate();
            }
            SetStatus($"{c.Name} — {identity.ProfileName}");
        }
        finally { _operations.Release(); }
    }

    private async Task UpdateLoopAsync(Controller c)
    {
        var nextPing = DateTimeOffset.UtcNow;
        try
        {
            while (IsOwner(c))
            {
                if (DateTimeOffset.UtcNow - c.LastPong > TimeSpan.FromSeconds(12)) break;
                if (DateTimeOffset.UtcNow >= nextPing)
                { await SendAsync(c, "ping"); nextPing = DateTimeOffset.UtcNow.AddSeconds(3); }
                long generation;
                lock (_gate) generation = _playbackGeneration;
                if (c.CanControl && SameServer(c, EffectiveServerId))
                {
                    var state = await _adapter!.StateAsync(c.Lifetime.Token);
                    if (c.CanControl && SameServer(c, EffectiveServerId) && generation == _playbackGeneration)
                        await SendAsync(c, "state", "state", state);
                }
                await Task.Delay(500, c.Lifetime.Token);
            }
        }
        catch (Exception) { }
        finally { c.Lifetime.Cancel(); }
    }

    private void OnContentLoaded()
    {
        if (!_pendingFullscreen || _remoteScope == null) return;
        _pendingFullscreen = false;
        _ = _adapter!.InvokeAsync(() =>
        {
            if (player.HasRemoteAuthority) player.EnterFullscreen();
            return Task.FromResult(true);
        });
    }

    private void OnRemoteAuthorityEnded(PlaybackAuthorityScope scope)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_remoteScope, scope)) return;
            _remoteScope = null;
            var identity = _identity;
            _identity = null;
            _pendingFullscreen = false;
            _playbackGeneration++;
            ReconcileController();
            if (_controller != null && ReferenceEquals(_controller.PreparedIdentity, identity))
            {
                _controller.PreparedIdentity = null;
                _controller.ReadyRequest = null;
                _controller.HandoffGeneration++;
            }
            identity?.Dispose();
        }
        QueueAdvertisementUpdate();
        if (_disposed == 0)
            SetStatus(_controller is { } connected ? $"Connected to {connected.Name}" :
                _listening == null ? "Off" : "Ready — remote playback ended");
    }

    private async Task WatchAuthorityAsync()
    {
        var lastServer = localApi.BaseUrl;
        var networkCheck = DateTimeOffset.MinValue;
        string? lastNetwork = null;
        while (_disposed == 0)
        {
            try { await Task.Delay(1000, _shutdown.Token); } catch (OperationCanceledException) { break; }
            try
            {
                if (localApi.BaseUrl != lastServer)
                { lastServer = localApi.BaseUrl; await EndRemotePlaybackAsync(); await RefreshSafelyAsync(); }
                if (settings.Load().SiloCastEnabled && DateTimeOffset.UtcNow >= networkCheck)
                {
                    networkCheck = DateTimeOffset.UtcNow.AddSeconds(5);
                    var network = string.Join(",", SiloCastNetwork.GetEligibleAddresses().Select(a => a.ToString()).Order());
                    if (lastNetwork != null && network != lastNetwork) await RefreshSafelyAsync();
                    lastNetwork = network;
                }
                var identity = _identity;
                if (identity != null && (identity.ExpiresAt <= DateTimeOffset.UtcNow || !identity.Auth.IsLoggedIn ||
                    identity.Auth.SelectedProfileId != identity.ProfileId))
                { await EndRemotePlaybackAsync(); SetStatus("Remote session ended — reconnect from your phone"); }
            }
            catch (Exception ex) { LocalLog.AppendLine("silocast.txt", $"authority_check_failed | {ex.GetType().Name}"); }
        }
    }

    private async Task EndRemotePlaybackAsync()
    {
        await _operations.WaitAsync();
        try
        {
            var identity = _identity;
            var scope = _remoteScope;
            if (_adapter != null && scope != null) await _adapter.CloseAsync(scope);
            if (ReferenceEquals(_identity, identity))
            {
                lock (_gate)
                { _identity = null; _remoteScope = null; _pendingFullscreen = false; _playbackGeneration++; ReconcileController(); }
                identity?.Dispose();
            }
            QueueAdvertisementUpdate();
        }
        finally { _operations.Release(); }
    }

    public async Task DisconnectAsync()
    {
        Controller? c;
        lock (_gate) { c = _controller; _controller = null; }
        if (c != null) await CloseControllerAsync(c, true);
        await EndRemotePlaybackAsync();
        SetStatus(_listening == null ? "Off" : "Ready — phone disconnected");
    }

    private Task CloseControllerAsync(Controller c, bool sendClose)
    {
        lock (c.CloseGate)
        {
            if (c.CloseCompletion != null) return c.CloseCompletion.Task;
            var completion = c.CloseCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = CompleteControllerCloseAsync(c, sendClose, completion);
            return completion.Task;
        }
    }

    private async Task CompleteControllerCloseAsync(Controller c, bool sendClose, TaskCompletionSource completion)
    {
        try { await CloseControllerCoreAsync(c, sendClose); completion.TrySetResult(); }
        catch (Exception ex) { completion.TrySetException(ex); }
    }

    private async Task CloseControllerCoreAsync(Controller c, bool sendClose)
    {
        if (sendClose && !c.Lifetime.IsCancellationRequested)
            try { await SendAsync(c, "close"); } catch { }
        c.Lifetime.Cancel();
        lock (_gate)
        {
            c.HandoffCancellation?.Cancel();
            c.LaunchCancellation?.Cancel();
            c.HandoffGeneration++;
            if (!ReferenceEquals(c.PreparedIdentity, _identity)) c.PreparedIdentity?.Dispose();
            c.PreparedIdentity = null;
        }
        await c.Connection.DisposeAsync();
    }

    private async Task StopListenerAsync()
    {
        _listening?.Cancel();
        foreach (var listener in _listeners) listener.Stop();
        _listeners.Clear();
        Controller? c;
        lock (_gate) { c = _controller; _controller = null; }
        if (c != null) await CloseControllerAsync(c, true);
        await _advertisement.StopAsync();
        _listening?.Dispose();
        _listening = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        localAuth.UserChanged -= OnAuthorityChanged;
        localAuth.LoggedOut -= OnAuthorityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        player.ContentLoaded -= OnContentLoaded;
        player.RemoteAuthorityEnded -= OnRemoteAuthorityEnded;
        player.StateChanged -= OnPlayerStateChanged;
        player.SessionStarted -= OnSessionStarted;
        await _lifecycle.WaitAsync();
        try { await StopListenerAsync(); await EndRemotePlaybackAsync(); await _advertisement.DisposeAsync(); }
        finally { _lifecycle.Release(); }
    }

    public async Task ShutdownAsync()
    {
        try { await DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(12)); }
        catch (TimeoutException)
        {
            // Preserve final writes while the dispatcher is live, but never hang window closure.
            lock (_gate) { _identity?.Dispose(); _identity = null; _remoteScope = null; }
            LocalLog.AppendLine("silocast.txt", "shutdown_cleanup_timeout");
        }
    }
}
