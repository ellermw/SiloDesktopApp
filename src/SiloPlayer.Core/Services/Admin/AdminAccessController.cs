using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One page lifetime, one in-flight operation, no queued writes or automatic replay.</summary>
public abstract class AdminAccessController : IDisposable
{
    protected readonly object Gate = new();
    private readonly AdminAuthorityLifetime _lifetime;
    private bool _busy, _disposed;
    public event Action? Changed;
    public MonitorAuthorityLease? Owner => _lifetime.Owner;
    public bool Busy { get { lock (Gate) return _busy; } }
    public string? Message { get; protected set; }
    public bool ReviewRequired { get; protected set; }
    public bool Current => Owner is { IsCurrent: true };
    protected AdminAccessController(AdminMonitorAuthority authority) { _lifetime = new(authority); _lifetime.Retired += Clear; }
    protected void Notify() => Changed?.Invoke();
    protected void Publish(MonitorAuthorityLease owner, Action action)
    {
        lock (Gate) { if (_disposed || !ReferenceEquals(Owner, owner) || !owner.IsCurrent) return; action(); }
        Notify();
    }
    protected async Task OpenPageAsync(Func<MonitorAuthorityLease, Task> load)
    {
        if (Busy || _disposed) return;
        _lifetime.Retire(); Clear(); _lifetime.Resume();
        try { _lifetime.Open(); } catch (OperationCanceledException) { Notify(); return; }
        await RunAsync(load, reconcile: true).ConfigureAwait(false);
    }
    protected async Task RunAsync(Func<MonitorAuthorityLease, Task> work, bool write = false, bool reconcile = false)
    {
        MonitorAuthorityLease owner;
        lock (Gate)
        {
            if (_disposed || _busy || Owner is not { IsCurrent: true } current || write && ReviewRequired) return;
            owner = current; _busy = true; Message = null;
        }
        Notify();
        try
        {
            owner.ThrowIfRetired(); await work(owner).ConfigureAwait(false); owner.ThrowIfRetired();
            if (reconcile) Publish(owner, () => ReviewRequired = false);
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error)
        {
            Publish(owner, () =>
            {
                Message = error switch
                {
                    AdminAccessException safe => safe.Message,
                    AdminSettingsConflictException => "Settings changed on the server. Reload and review your changes before saving again.",
                    AdminSettingsOutcomeUnknownException => "The save outcome is unknown. Reload and review before saving again. This request was not resent.",
                    ApiException api => new AdminAccessException(api.ProblemType ?? api.ErrorCode, api.StatusCode).Message,
                    ArgumentException => "Review the entered values before continuing.",
                    _ => "The request could not be completed. Reload and review before continuing.",
                };
                // A rejected local value never reached the server, so it leaves no unknown state to review.
                if (write && error is not ArgumentException) ReviewRequired = true;
            });
        }
        finally { Publish(owner, () => _busy = false); }
    }
    protected abstract void Reset();
    private void Clear() { lock (Gate) { Reset(); _busy = false; Message = null; ReviewRequired = false; } Notify(); }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose() { if (_disposed) return; Suspend(); _disposed = true; _lifetime.Dispose(); Changed = null; }
}
