namespace SiloPlayer.Core.Services.Admin;

internal sealed class LogsAuthorityLifetime : IDisposable
{
    private readonly AdminMonitorAuthority _authority;
    private readonly object _gate = new();
    private MonitorAuthorityLease? _owner;
    private bool _disposed;
    public LogsAuthorityLifetime(AdminMonitorAuthority authority) { _authority = authority; authority.AvailabilityChanged += Retire; }
    public event Action? Retired;
    public MonitorAuthorityLease? Owner { get { lock (_gate) return _owner; } }
    public MonitorAuthorityLease Open()
    {
        var captured = _authority.Capture();
        lock (_gate)
        {
            if (_disposed) { captured.Dispose(); throw new ObjectDisposedException(nameof(LogsAuthorityLifetime)); }
            _owner?.Dispose();
            captured.ThrowIfRetired();
            return _owner = captured;
        }
    }
    public MonitorAuthorityLease CaptureAction()
    {
        MonitorAuthorityLease owner;
        lock (_gate) owner = _owner ?? throw new MonitorAuthorityRetiredException();
        owner.ThrowIfRetired();
        var action = _authority.Capture(owner.Token);
        lock (_gate)
        {
            if (!_disposed && ReferenceEquals(owner, _owner) && owner.IsCurrent && action.IsCurrent && action.Epoch == owner.Epoch && action.Context == owner.Context) return action;
            action.Dispose();
            throw new MonitorAuthorityRetiredException();
        }
    }
    public bool IsCurrent(MonitorAuthorityLease? lease)
    {
        lock (_gate) return !_disposed && lease is { IsCurrent: true } && _owner is { IsCurrent: true } owner && owner.Epoch == lease.Epoch && owner.Context == lease.Context;
    }
    private void Retire()
    {
        MonitorAuthorityLease? owner;
        lock (_gate) { owner = _owner; _owner = null; }
        owner?.Dispose();
        Retired?.Invoke();
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _authority.AvailabilityChanged -= Retire;
        Retire();
        Retired = null;
    }
}
