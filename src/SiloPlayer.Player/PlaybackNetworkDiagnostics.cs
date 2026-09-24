using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Net;

namespace SiloPlayer.Player;

/// <summary>Observe .NET's real sockets without replacing its connection logic.</summary>
internal sealed class PlaybackNetworkDiagnostics : EventListener
{
    private sealed record Context(PlaybackDiagnostics Recorder, long Request, int Pool);
    private static readonly AsyncLocal<Context?> Current = new();
    private sealed record Connection(WeakReference<PlaybackDiagnostics> Recorder, string? Peer);
    private static readonly ConcurrentDictionary<long, Connection> Connections = new();
    private static readonly PlaybackNetworkDiagnostics Instance = new();
    public static IDisposable Request(PlaybackDiagnostics recorder, long request, int pool)
    {
        GC.KeepAlive(Instance);
        var previous = Current.Value;
        Current.Value = new Context(recorder, request, pool);
        return new Scope(previous);
    }
    private sealed class Scope(Context? previous) : IDisposable { public void Dispose() => Current.Value = previous; }
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name is "System.Net.Http" or "System.Net.Sockets" or "System.Net.Security")
            EnableEvents(source, EventLevel.Informational, EventKeywords.All);
    }
    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        try
        {
            if (data.EventName is not ("ConnectionEstablished" or "ConnectionClosed" or "RequestHeadersStart" or
                "RequestLeftQueue" or "ConnectStart" or "ConnectStop" or "ConnectFailed" or
                "HandshakeStart" or "HandshakeStop" or "HandshakeFailed")) return;
            object? Value(string name)
            {
                var index = data.PayloadNames?.IndexOf(name) ?? -1;
                return index < 0 ? null : data.Payload?[index];
            }
            var context = Current.Value;
            var connection = Value("connectionId") is long id ? id : (long?)null;
            var recorder = context?.Recorder;
            Connection? known = null;
            if (connection.HasValue) Connections.TryGetValue(connection.Value, out known);
            if (recorder == null) known?.Recorder.TryGetTarget(out recorder);
            var peer = IPAddress.TryParse(Value("remoteAddress")?.ToString(), out var address) ? address.ToString() : known?.Peer;
            if (connection.HasValue && data.EventName == "ConnectionClosed") Connections.TryRemove(connection.Value, out _);
            if (recorder == null) return;
            if (connection.HasValue)
            {
                if (data.EventName == "ConnectionClosed") Connections.TryRemove(connection.Value, out _);
                else if (known != null || Connections.Count < 256)
                    Connections[connection.Value] = new(new(recorder), peer);
            }
            recorder.Record("network", new { name = data.EventName, request = context?.Request, pool = context?.Pool,
                connection, peer, major = Value("versionMajor"), minor = Value("versionMinor"),
                queuedMs = Value("timeOnQueueMilliseconds") });
        }
        catch { /* Diagnostics must never affect HTTP transport. */ }
    }
}
