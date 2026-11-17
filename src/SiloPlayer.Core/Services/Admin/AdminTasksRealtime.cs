using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminTaskNoticeKind { Started, CancelRequested, ScheduleSaved, Failed, OutcomeUnknown }

/// <summary>A toast-worthy command result for the owner that issued it. Holds no server text.</summary>
public sealed record AdminTaskNotice(AdminTaskNoticeKind Kind, string Message)
{
    public MonitorAuthorityLease? Owner { get; init; }
}

/// <summary>A <c>task.updated</c> frame reduced to what an invalidation needs. Null fields mean the frame did not carry a usable value.</summary>
public sealed record AdminTaskSignal(string? Key, string? State);

/// <summary>
/// The page's lease on the shared <c>tasks</c> realtime channel (official RealtimeEventsProvider
/// <c>tasks</c> branches). Frames are invalidation signals only: a <c>task.updated</c> frame omits
/// <c>started_at</c> and comes from any server, so callers refetch authoritative HTTP state. A snapshot
/// (subscription or reconnect) means events may have been missed. Disposal releases the lease and
/// stops every callback; a callback already running checks its owner before acting.
/// </summary>
public sealed class AdminTasksRealtime : IDisposable
{
    public const string Channel = "tasks";
    public const string UpdatedEvent = "task.updated";
    private readonly IAdminRealtimeChannel? _channel;
    private readonly IDisposable? _lease;
    private Action? _snapshot;
    private Action<AdminTaskSignal>? _updated;

    public AdminTasksRealtime(IAdminRealtimeChannel? channel, Action snapshot, Action<AdminTaskSignal> updated)
    {
        _channel = channel;
        _snapshot = snapshot;
        _updated = updated;
        if (channel == null) return;
        channel.SnapshotReceived += OnSnapshot;
        channel.EventReceived += OnEvent;
        _lease = channel.Subscribe(Channel);
    }

    private void OnSnapshot(string channel, JsonElement data)
    {
        if (channel == Channel) Volatile.Read(ref _snapshot)?.Invoke();
    }

    private void OnEvent(string channel, string name, JsonElement data)
    {
        if (channel != Channel || name != UpdatedEvent) return;
        Volatile.Read(ref _updated)?.Invoke(Parse(data));
    }

    /// <summary>Only the key and state are read; a malformed frame still invalidates, it just cannot be matched.</summary>
    public static AdminTaskSignal Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return new(null, null);
        string? key = data.TryGetProperty("key", out var keyElement) && keyElement.ValueKind == JsonValueKind.String && AdminTasksApi.IsTaskKey(keyElement.GetString())
            ? keyElement.GetString() : null;
        string? state = data.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String && stateElement.GetString() is { Length: > 0 and <= 64 } value
            ? value : null;
        return new(key, state);
    }

    public void Dispose()
    {
        Volatile.Write(ref _snapshot, null);
        Volatile.Write(ref _updated, null);
        if (_channel == null) return;
        _channel.SnapshotReceived -= OnSnapshot;
        _channel.EventReceived -= OnEvent;
        _lease?.Dispose();
    }
}

/// <summary>
/// One coalescing read: a foreground invalidation that arrives while the read runs queues exactly one
/// trailing read; a periodic tick never queues. Callers hold their own lock around every member.
/// </summary>
internal sealed class AdminTaskReadSlot
{
    public bool Running { get; private set; }
    private bool _pending;
    public bool Pending => _pending;

    /// <summary>True when the caller should start a read now. Inactive invalidations are retained without starting work.</summary>
    public bool Request(bool foreground, bool active = true)
    {
        if (active && !Running) { Running = true; _pending = false; return true; }
        if (foreground) _pending = true;
        return false;
    }

    /// <summary>Inactive owners retain a queued invalidation until reactivation instead of starting a trailing read.</summary>
    public bool Complete(bool active = true)
    {
        if (active && _pending) { _pending = false; return true; }
        Running = false;
        return false;
    }

    public void Reset() { Running = false; _pending = false; }
}
