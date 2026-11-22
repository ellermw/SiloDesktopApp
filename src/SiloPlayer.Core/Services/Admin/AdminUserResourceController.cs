using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>How one write ended. <see cref="NotStarted"/> means nothing was sent: the page was retired, a reload is needed, or another write is running.</summary>
public enum AdminWriteResult { NotStarted, Failed, Done }

/// <summary>One page's authority and operation lifetime. Reads supersede by channel; writes never queue or replay.</summary>
public sealed class AdminUserResourceController : IDisposable
{
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly Dictionary<string, MonitorAuthorityLease> _reads = new(StringComparer.Ordinal);
    private long _generation;
    private int _writing;
    private bool _disposed, _needsReview;
    private string? _error, _errorChannel;
    public AdminUserResourceController(AdminMonitorAuthority authority) { _lifetime = new(authority); _lifetime.Retired += OnRetired; }
    public MonitorAuthorityLease? Owner => _lifetime.Owner;
    public bool Busy => Volatile.Read(ref _writing) != 0;
    public bool NeedsReview { get { lock (_gate) return _needsReview; } }
    public string? Error { get { lock (_gate) return _error; } }
    public event Action? Changed;
    public event Action? Retired;
    public void Open()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _generation++; CancelReads(); _lifetime.Resume(); _lifetime.Open(); _needsReview = false; _error = _errorChannel = null;
        }
    }
    public bool IsCurrent(MonitorAuthorityLease? owner) { lock (_gate) return !_disposed && ReferenceEquals(Owner, owner) && owner is { IsCurrent: true }; }
    public async Task<T?> ReadAsync<T>(string channel, Func<MonitorAuthorityLease, Task<T>> read)
    {
        MonitorAuthorityLease? owner; MonitorAuthorityLease action; long generation;
        lock (_gate)
        {
            owner = Owner; generation = _generation;
            if (!IsCurrent(owner)) return default;
            if (_reads.Remove(channel, out var previous)) previous.Dispose();
            action = _lifetime.CaptureAction(); _reads[channel] = action;
        }
        bool Current() { lock (_gate) return IsCurrent(owner) && generation == _generation && _reads.GetValueOrDefault(channel) == action && action.IsCurrent; }
        try
        {
            var result = await read(action);
            if (!Current()) return default;
            var cleared = false;
            lock (_gate) { if (Current() && _errorChannel == channel) { _error = null; _errorChannel = null; cleared = true; } }
            if (cleared) Changed?.Invoke();
            return result;
        }
        catch (Exception error)
        {
            var notify = false;
            lock (_gate) { if (Current()) { _error = AdminUserResourceText.Failure(error); _errorChannel = channel; LastException = error; notify = true; } }
            if (notify) Changed?.Invoke();
            return default;
        }
        finally
        {
            lock (_gate) { if (_reads.GetValueOrDefault(channel) == action) _reads.Remove(channel); }
            action.Dispose();
        }
    }
    public async Task<bool> WriteAsync(Func<MonitorAuthorityLease, Task> write) => await TryWriteAsync(write) == AdminWriteResult.Done;
    /// <summary>The same write, saying whether it ever left: <see cref="AdminWriteResult.NotStarted"/> is the one answer that is certain to have changed nothing.</summary>
    public async Task<AdminWriteResult> TryWriteAsync(Func<MonitorAuthorityLease, Task> write)
    {
        MonitorAuthorityLease? owner; long generation;
        lock (_gate)
        {
            owner = Owner; generation = _generation;
            if (!IsCurrent(owner) || _needsReview || Interlocked.CompareExchange(ref _writing, 1, 0) != 0) return AdminWriteResult.NotStarted;
            _error = _errorChannel = null;
        }
        Changed?.Invoke();
        try
        {
            using var action = _lifetime.CaptureAction(); await write(action);
            lock (_gate) return IsCurrent(owner) && generation == _generation ? AdminWriteResult.Done : AdminWriteResult.Failed;
        }
        catch (Exception error)
        {
            lock (_gate)
                if (IsCurrent(owner) && generation == _generation)
                {
                    _needsReview = AdminUserResourceText.RequiresReload(error);
                    _error = AdminUserResourceText.Failure(error); _errorChannel = null;
                }
            return AdminWriteResult.Failed;
        }
        finally { Interlocked.Exchange(ref _writing, 0); Changed?.Invoke(); }
    }
    public void BeginReview()
    {
        if (Busy) throw new InvalidOperationException("Wait for the current action before reloading.");
        Open(); Changed?.Invoke();
    }
    /// <summary>The most recent exception behind the failure line, for diagnostics only; it is never displayed or logged with its text.</summary>
    public Exception? LastException { get; private set; }
    public void Report(Exception error) { lock (_gate) { _error = AdminUserResourceText.Failure(error); _errorChannel = null; LastException = error; } Changed?.Invoke(); }
    private void CancelReads() { foreach (var read in _reads.Values) read.Dispose(); _reads.Clear(); }
    private void OnRetired()
    {
        lock (_gate) { _generation++; CancelReads(); _error = _errorChannel = null; _needsReview = false; }
        Retired?.Invoke(); Changed?.Invoke();
    }
    public void Suspend() { lock (_gate) { _generation++; CancelReads(); _lifetime.Retire(); _error = _errorChannel = null; _needsReview = false; } }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        Suspend(); _lifetime.Retired -= OnRetired; _lifetime.Dispose(); Changed = null; Retired = null;
    }
}
