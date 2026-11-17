using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin.Users;

public sealed class AdminUsersWorkspace : IDisposable
{
    private static long _nextGeneration;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly SemaphoreSlim _foreground = new(1, 1);
    private CancellationTokenSource? _location;
    private long _generation;
    private bool _disposed;
    private bool _needsReview;
    private bool _outcomeUnknown;
    private readonly object _gate = new();
    private int _active;
    public event Action? Retired;
    public bool NeedsReview { get { lock (_gate) return _needsReview; } }
    public bool OutcomeUnknown { get { lock (_gate) return _outcomeUnknown; } }
    public long Generation { get { lock (_gate) return _generation; } }
    public CancellationToken Token { get { lock (_gate) return _disposed ? new CancellationToken(true) : _location?.Token ?? new CancellationToken(true); } }
    public void AcceptReviewedReadback(long generation)
    {
        lock (_gate)
        {
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            if (_outcomeUnknown) throw new AdminUsersReviewRequiredException();
            _needsReview = false;
        }
    }
    public bool IsCurrent(long generation)
    {
        lock (_gate) return !_disposed && generation == _generation && _location is { IsCancellationRequested: false } && _lifetime.Owner is { IsCurrent: true };
    }

    public AdminUsersWorkspace(AdminMonitorAuthority authority)
    {
        _lifetime = new(authority);
        _lifetime.Retired += AuthorityRetired;
    }
    public long Activate()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _location?.Cancel();
            _location?.Dispose(); _location = null;
            _generation = Interlocked.Increment(ref _nextGeneration);
            _lifetime.Resume();
            var owner = _lifetime.Open();
            _location = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            _needsReview = false;
            _outcomeUnknown = false;
            return _generation;
        }
    }
    public async Task<T> ReadAsync<T>(long generation, Func<MonitorAuthorityLease, CancellationToken, Task<T>> read)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            token = _location!.Token; _active++;
        }
        var entered = false;
        try
        {
            await _foreground.WaitAsync(token).ConfigureAwait(false); entered = true;
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            using var action = _lifetime.CaptureAction();
            var value = await read(action, token).ConfigureAwait(false);
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            token.ThrowIfCancellationRequested();
            return value;
        }
        finally
        {
            if (entered) _foreground.Release();
            lock (_gate) { _active--; if (_disposed && _active == 0) _foreground.Dispose(); }
        }
    }
    public async Task<AdminUsersDocument> WriteAsync(long generation, AdminUsersApi api, AdminUserReview review, AdminUsersWrite write)
    {
        if (NeedsReview) throw new AdminUsersReviewRequiredException();
        var attempted = false;
        AdminUsersDocument? result = null;
        try
        {
            return await ReadAsync(generation, async (lease, token) =>
            {
                if (NeedsReview) throw new AdminUsersReviewRequiredException();
                result = await api.WriteAsync(lease, review, write, token, () => attempted = true).ConfigureAwait(false);
                return result;
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (result != null) await result.DiscardViewAsAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (IsCurrent(generation))
                {
                    var failure = AdminFailures.Classify(error);
                    if (attempted && error is not AdminUsersReviewRequiredException && (failure is AdminFailureKind.Transport or AdminFailureKind.Server or
                        AdminFailureKind.Unknown or AdminFailureKind.InvalidResponse or AdminFailureKind.Cancelled)) _outcomeUnknown = true;
                    if (error is AdminUsersReviewRequiredException || _outcomeUnknown ||
                        attempted && (failure is AdminFailureKind.PreconditionFailed or AdminFailureKind.PreconditionRequired)) _needsReview = true;
                }
            }
            throw;
        }
    }
    private void AuthorityRetired()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _location?.Cancel();
            _generation = Interlocked.Increment(ref _nextGeneration);
            _needsReview = true;
        }
        Retired?.Invoke();
    }
    public void Suspend()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _location?.Cancel();
            _generation = Interlocked.Increment(ref _nextGeneration);
            _lifetime.Retire();
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Suspend(); _disposed = true;
            _lifetime.Retired -= AuthorityRetired; _lifetime.Dispose();
            _location?.Dispose();
            if (_active == 0) _foreground.Dispose();
        }
    }
}
