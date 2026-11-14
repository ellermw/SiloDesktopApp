using System.Net.WebSockets;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public interface ILogsSocket : IDisposable
{
    Task ConnectAsync(Uri address, IReadOnlyList<string> protocols, CancellationToken ct);
    Task<string?> ReceiveAsync(CancellationToken ct);
    void Abort();
}

public sealed class LogsStreamClient(AdminMonitorApi api, Func<ILogsSocket>? socketFactory = null, TimeProvider? timeProvider = null)
{
    public const string Protocol = "silo.admin-logs.v2";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task RunAsync(MonitorAuthorityLease lease, string stream, LogsQuery query, Action<LogsSocketFrame> receive, CancellationToken ct = default)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        var failures = 0;
        while (true)
        {
            lease.ThrowIfRetired();
            stopping.Token.ThrowIfCancellationRequested();
            var snapshotReceived = false;
            try
            {
                var expired = await RunOnceAsync(lease, stream, query, frame =>
                {
                    if (frame.Type == "snapshot") { snapshotReceived = true; failures = 0; }
                    receive(frame);
                }, stopping.Token).ConfigureAwait(false);
                if (expired) { failures = 0; continue; }
                if (snapshotReceived) failures = 0;
                else if (++failures >= 3) throw new InvalidDataException("The log stream repeatedly closed before its snapshot.");
            }
            catch (Exception error) when (!stopping.IsCancellationRequested && lease.IsCurrent &&
                (error is WebSocketException or HttpRequestException or OperationCanceledException || error is ApiException { StatusCode: >= 500 or 429 }))
            {
                if (++failures >= 3) throw;
            }
            // Clean closes renew too, but never allow a server to create a tight ticket loop.
            await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << failures)), _time, stopping.Token).ConfigureAwait(false);
        }
    }

    private async Task<bool> RunOnceAsync(MonitorAuthorityLease lease, string stream, LogsQuery query, Action<LogsSocketFrame> receive, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        var ticket = await api.LogsCreateSocketTicketAsync(lease, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        using var socket = socketFactory?.Invoke() ?? new LogsClientWebSocket();
        using var expiration = new CancellationTokenSource(TimeSpan.FromSeconds(ticket.MaxConnectionSeconds), _time);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct, expiration.Token);
        using var abort = lifetime.Token.Register(socket.Abort);
        var origin = new Uri(lease.Context.BaseUrl);
        if (origin.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(origin.UserInfo)) throw new InvalidDataException("Invalid log stream origin.");
        var address = new UriBuilder(origin) { Scheme = origin.Scheme == "https" ? "wss" : "ws", Path = "/api/v2/admin/logs/ws", Query = "stream=" + stream + "&" + query.ToQuery(stream).TrimStart('?'), Fragment = "" }.Uri;
        try
        {
            await socket.ConnectAsync(address, [Protocol, "silo.ticket." + ticket.Ticket], lifetime.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            while (true)
            {
                var text = await socket.ReceiveAsync(lifetime.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
                if (text == null) return expiration.IsCancellationRequested;
                var frame = LogsSocketFrame.Parse(text, stream);
                if (frame == null) continue;
                lease.ThrowIfRetired();
                receive(frame);
            }
        }
        catch (Exception error) when (expiration.IsCancellationRequested && !ct.IsCancellationRequested && lease.IsCurrent && (error is OperationCanceledException or WebSocketException))
        {
            // The ticket's bounded connection lifetime is a routine renewal, not a disconnect.
            return true;
        }
        finally { lease.ThrowIfRetired(); }
    }

    private sealed class LogsClientWebSocket : ILogsSocket
    {
        private readonly ClientWebSocket _socket = new();
        public async Task ConnectAsync(Uri address, IReadOnlyList<string> protocols, CancellationToken ct)
        {
            foreach (var protocol in protocols) _socket.Options.AddSubProtocol(protocol);
            await _socket.ConnectAsync(address, ct).ConfigureAwait(false);
            if (_socket.SubProtocol != Protocol) throw new InvalidDataException("The log server selected an unexpected protocol.");
        }
        public async Task<string?> ReceiveAsync(CancellationToken ct)
        {
            using var message = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Unexpected log stream frame.");
                if (message.Length + result.Count > 2 * 1024 * 1024) throw new InvalidDataException("Log frame exceeds its size limit.");
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
        public void Abort() => _socket.Abort();
        public void Dispose() => _socket.Dispose();
    }
}
