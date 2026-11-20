using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One shared counts read, 10-second cache, 60-second polling only while the registered Admin sidebar listens.</summary>
public sealed class AdminRequestCountStore : IAdminNavBadgeSource, IDisposable
{
    private readonly AdminRequestsApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly object _gate = new();
    private Action? _changed;
    private CancellationTokenSource? _poll;
    private Task<JsonObject>? _read;
    private JsonObject? _counts;
    private long _epoch;
    private int _badge;
    private DateTimeOffset _readAt;
    private bool _disposed;
    public AdminRequestCountStore(AdminRequestsApi api, AdminMonitorAuthority authority) { _api = api; _authority = authority; authority.AvailabilityChanged += Retire; }
    public AdminRoute Route => AdminRoute.RequestQueue;
    public AdminNavBadge? Current
    {
        get
        {
            lock (_gate)
            {
                var count = BadgeLocked();
                return count == 0 ? null : new(AdminNavBadgeKind.Count, count,
                    AdminRequestsText.T("web.admin.requests.queue.view.needsApproval") + ": " + AdminText.FormatNumber(count));
            }
        }
    }
    public event Action? Changed
    {
        add { lock (_gate) { _changed += value; if (!_disposed && _poll == null) { _poll = new(); _ = PollAsync(_poll.Token); } } }
        remove { lock (_gate) { _changed -= value; if (_changed == null) { _poll?.Cancel(); _poll?.Dispose(); _poll = null; _counts = null; _badge = 0; } } }
    }
    public async Task<JsonObject> GetAsync(MonitorAuthorityLease caller, bool force = false)
    {
        caller.ThrowIfRetired(); Task<JsonObject> read;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!force && _counts != null && _epoch == caller.Epoch && DateTimeOffset.UtcNow - _readAt < TimeSpan.FromSeconds(10)) return (JsonObject)_counts.DeepClone();
            read = _read ??= ReadAsync(caller.Epoch);
        }
        try { var result = await read.WaitAsync(caller.Token).ConfigureAwait(false); caller.ThrowIfRetired(); return (JsonObject)result.DeepClone(); }
        catch { lock (_gate) { if (_epoch == caller.Epoch) _counts = null; } Raise(); throw; }
        finally { lock (_gate) if (read.IsCompleted && ReferenceEquals(_read, read)) _read = null; }
    }
    private async Task<JsonObject> ReadAsync(long epoch)
    {
        using var lease = _authority.Capture();
        if (lease.Epoch != epoch) throw new MonitorAuthorityRetiredException();
        var result = await _api.GetCountsAsync(lease).ConfigureAwait(false);
        lease.ThrowIfRetired();
        lock (_gate) { if (_disposed || _authority.Epoch != epoch) throw new MonitorAuthorityRetiredException(); _counts = result; _epoch = epoch; _readAt = DateTimeOffset.UtcNow; }
        Raise(); return result;
    }
    private async Task PollAsync(CancellationToken ct)
    {
        await Task.Yield();
        while (!ct.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(60);
            try { if (_authority.IsAvailable) { using var lease = _authority.Capture(ct); await GetAsync(lease).ConfigureAwait(false); } }
            // 404 or 501 means this server has no Requests admin API, so it is asked far less often.
            catch (ApiException error) when (error.StatusCode is 404 or 501) { wait = TimeSpan.FromMinutes(10); }
            catch (Exception) { if (ct.IsCancellationRequested) break; }
            try { await Task.Delay(wait, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
    private void Retire() { lock (_gate) { _counts = null; _read = null; _epoch = 0; } Raise(); }
    private int BadgeLocked() => !_authority.IsAvailable || _authority.Epoch != _epoch || _counts == null ? 0 : AdminRequestJson.Number(_counts, "needs_approval");
    /// <summary>Listeners rebuild the sidebar, so they hear only a badge that differs from the last one announced.</summary>
    private void Raise() { Action? handler; lock (_gate) { var badge = BadgeLocked(); if (badge == _badge) return; _badge = badge; handler = _changed; } handler?.Invoke(); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _poll?.Cancel(); _poll?.Dispose(); _poll = null; _changed = null; _counts = null; } _authority.AvailabilityChanged -= Retire; }
}
