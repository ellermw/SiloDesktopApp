using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Messaging;

namespace SiloPlayer.Services;

/// <summary>Owns captured realtime invalidations until bounded UI dispatch.</summary>
public sealed class RealtimeMediaStateRefresh : IDisposable
{
    private readonly Func<ApiRequestContext> _captureContext;
    private readonly Func<ApiRequestContext, bool> _isCurrentContext;
    private readonly Action<Action> _enqueue;
    private readonly Action<MediaStateInvalidated> _publish;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly HashSet<string> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _series = new(StringComparer.Ordinal);
    private readonly HashSet<string> _viewerStateItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _playbackStateItems = new(StringComparer.Ordinal);
    private ApiRequestContext? _context;
    private DateTimeOffset _deadline;
    private ITimer? _timer;
    private long _epoch;
    private long _schedule;
    private bool _catchUp;
    private bool _disposed;

    public RealtimeMediaStateRefresh(SiloApiClient client, Action<Action> enqueue,
        IMessenger? messenger = null, TimeProvider? timeProvider = null)
        : this(client.CaptureContext, client.IsCurrentContext, enqueue,
            message => (messenger ?? WeakReferenceMessenger.Default).Send(message), timeProvider) { }

    // A mounted detail uses the same bounded batching for local progress, but
    // delivers only to itself: the original local message already invalidated caches.
    internal RealtimeMediaStateRefresh(CatalogApi catalog, Action<Action> enqueue,
        Action<MediaStateInvalidated> publish, TimeProvider? timeProvider = null)
        : this(catalog.CaptureContext, catalog.IsCurrentContext, enqueue, publish, timeProvider) { }

    private RealtimeMediaStateRefresh(Func<ApiRequestContext> captureContext,
        Func<ApiRequestContext, bool> isCurrentContext, Action<Action> enqueue,
        Action<MediaStateInvalidated> publish, TimeProvider? timeProvider)
    {
        _captureContext = captureContext; _isCurrentContext = isCurrentContext;
        _enqueue = enqueue; _publish = publish;
        _time = timeProvider ?? TimeProvider.System;
    }

    internal void Invalidate(MediaStateInvalidated message) =>
        Queue(message.Context, message.ContentIds.ToArray(), message.SeriesIds.ToArray(), message.CatchUp,
            message.ViewerStateContentIds?.ToArray(), message.PlaybackStateContentIds?.ToArray());

    public void ReceiveRealtime(string channel, string eventName, JsonElement data)
    {
        // Capture before any dispatcher work. The payload never outlives its JSON owner.
        var context = _captureContext();
        if (channel != "user_state" || eventName != "user_state.changed" || data.ValueKind != JsonValueKind.Object
            || Text(data, "change") is not ("watched" or "progress" or "favorite" or "watchlist" or "rating" or "not_interested")
            || Text(data, "content_id") is not { Length: > 0 } id
            || string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(context.ProfileId)
            || (data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
                && profile.GetString() != context.ProfileId)) return;
        var series = Text(data, "series_id");
        var viewerState = Text(data, "change") is "favorite" or "watchlist" or "rating" or "not_interested";
        Queue(context, [id], string.IsNullOrWhiteSpace(series) ? [] : [series], false, viewerState ? [id] : []);
    }

    public void CatchUp(ApiRequestContext context) => Queue(context, [], [], true);

    private static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private void Queue(ApiRequestContext context, string[] items, string[] series, bool catchUp,
        string[]? viewerStateItems = null, string[]? playbackStateItems = null)
    {
        if (string.IsNullOrEmpty(context.ProfileId)) return;
        playbackStateItems ??= items.Except(viewerStateItems ?? [], StringComparer.Ordinal).ToArray();
        long epoch;
        lock (_gate)
        {
            if (_disposed || !_isCurrentContext(context)) return;
            epoch = _epoch;
            // Retire in-flight /watch and detail reads on the receive thread,
            // before playback can accept them while the UI dispatcher is busy.
            _publish(new MediaStateInvalidated(context, items.ToHashSet(StringComparer.Ordinal),
                series.ToHashSet(StringComparer.Ordinal), catchUp, RefreshDetails: false,
                ViewerStateContentIds: viewerStateItems?.ToHashSet(StringComparer.Ordinal),
                PlaybackStateContentIds: playbackStateItems.ToHashSet(StringComparer.Ordinal)));
        }
        _enqueue(() =>
        {
            lock (_gate)
            {
                if (_disposed || epoch != _epoch || !_isCurrentContext(context)) return;
                if (_context != context)
                {
                    ClearPending();
                    _context = context;
                }
                if (_items.Count == 0 && _series.Count == 0 && !_catchUp)
                    _deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(1);
                _items.UnionWith(items); _series.UnionWith(series); _catchUp |= catchUp;
                if (viewerStateItems != null) _viewerStateItems.UnionWith(viewerStateItems);
                _playbackStateItems.UnionWith(playbackStateItems);
                var wait = _deadline - _time.GetUtcNow();
                var schedule = ++_schedule;
                if (wait <= TimeSpan.Zero)
                {
                    // A receipt already ahead of the due timer in the UI queue
                    // must not keep canceling that flush under continuous input.
                    Flush(epoch, schedule);
                    return;
                }
                if (wait > TimeSpan.FromMilliseconds(200)) wait = TimeSpan.FromMilliseconds(200);
                _timer?.Dispose();
                _timer = _time.CreateTimer(_ => _enqueue(() => Flush(epoch, schedule)), null, wait, Timeout.InfiniteTimeSpan);
            }
        });
    }

    private void Flush(long epoch, long schedule)
    {
        lock (_gate)
        {
            if (_disposed || epoch != _epoch || schedule != _schedule || _context is not { } context) return;
            var message = new MediaStateInvalidated(context, _items.ToHashSet(StringComparer.Ordinal),
                _series.ToHashSet(StringComparer.Ordinal), _catchUp,
                ViewerStateContentIds: _viewerStateItems.ToHashSet(StringComparer.Ordinal),
                PlaybackStateContentIds: _playbackStateItems.ToHashSet(StringComparer.Ordinal));
            ClearPending();
            if (_isCurrentContext(context)) _publish(message);
        }
    }

    /// <summary>Retires all queued callbacks when the shell subscription leaves its profile.</summary>
    public void Reset() { lock (_gate) { ++_epoch; ClearPending(); } }

    private void ClearPending()
    {
        _timer?.Dispose(); _timer = null; ++_schedule;
        _context = null; _items.Clear(); _series.Clear(); _catchUp = false;
        _viewerStateItems.Clear(); _playbackStateItems.Clear();
    }

    public void Dispose() { lock (_gate) { _disposed = true; ++_epoch; ClearPending(); } }
}
