using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin.Users;

public sealed record AdminUsersReconciliation(AdminUsersOperation Operation, string? AccountId, string? Path, bool Collection, bool CanReadBack);

// A replaced page must not forget a lost receipt or race a receipt that is still arriving.
internal sealed class AdminUsersReceiptReviews
{
    private readonly Dictionary<(string Account, AdminUsersOperation Family), AdminUsersReconciliation> _pending = [];
    internal readonly SemaphoreSlim Writer = new(1, 1);

    private static AdminUsersOperation Family(AdminUsersOperation operation) =>
        operation == AdminUsersOperation.ResetEmail ? AdminUsersOperation.ResetLink : operation;
    internal static bool Tracks(AdminUsersOperation operation) =>
        operation is AdminUsersOperation.ViewAs or AdminUsersOperation.ResetLink or AdminUsersOperation.ResetEmail;
    internal bool Any { get { lock (_pending) return _pending.Count != 0; } }
    internal AdminUsersReconciliation? Find(string account, AdminUsersOperation operation)
    {
        lock (_pending) return _pending.GetValueOrDefault((account, Family(operation)));
    }
    internal void Record(string account, AdminUsersOperation operation)
    {
        lock (_pending) _pending[(account, Family(operation))] = new(operation, account, null, false, false);
    }
    internal void Accept(AdminUsersReconciliation pending)
    {
        if (pending.AccountId is not { } account) return;
        var key = (account, Family(pending.Operation));
        lock (_pending)
            if (ReferenceEquals(_pending.GetValueOrDefault(key), pending)) _pending.Remove(key);
    }
}

public sealed class AdminUsersWorkspace : IDisposable
{
    private static long _nextGeneration;
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly SemaphoreSlim _foreground = new(1, 1);
    private TaskCompletionSource? _readsDrained;
    private int _readers;
    private CancellationTokenSource? _location;
    private long _generation;
    private bool _disposed;
    private bool _needsReview;
    private bool _outcomeUnknown;
    private long? _epoch;
    private AdminUsersReconciliation? _reconciliation;
    private readonly object _gate = new();
    private int _active;
    public event Action? Retired;
    public bool NeedsReview { get { lock (_gate) return _needsReview || CurrentReceiptReviews?.Any == true; } }
    public bool OutcomeUnknown { get { lock (_gate) return _outcomeUnknown || CurrentReceiptReviews?.Any == true; } }
    public bool NeedsReloadReview { get { lock (_gate) return _needsReview; } }
    public AdminUsersReconciliation? Reconciliation { get { lock (_gate) return _reconciliation; } }
    private AdminUsersReceiptReviews? CurrentReceiptReviews => _epoch is long epoch ? _authority.UsersReceiptReviews(epoch) : null;
    public AdminUsersReconciliation? ReceiptReview(string? account, AdminUsersOperation operation)
    {
        lock (_gate) return account == null || !AdminUsersReceiptReviews.Tracks(operation) ? null : CurrentReceiptReviews?.Find(account, operation);
    }
    public bool NeedsReviewFor(string? account, AdminUsersOperation operation)
    {
        lock (_gate) return _needsReview || ReceiptReview(account, operation) != null;
    }
    public bool OutcomeUnknownFor(string? account, AdminUsersOperation operation)
    {
        lock (_gate) return _outcomeUnknown || ReceiptReview(account, operation) != null;
    }
    public long Generation { get { lock (_gate) return _generation; } }
    public long? EpochOf(long generation)
    {
        lock (_gate) return IsCurrent(generation) ? _lifetime.Owner?.Epoch : null;
    }
    public CancellationToken Token { get { lock (_gate) return _disposed ? new CancellationToken(true) : _location?.Token ?? new CancellationToken(true); } }
    public void AcceptReviewedReadback(long generation)
    {
        lock (_gate)
        {
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            if (_outcomeUnknown || _reconciliation == null && CurrentReceiptReviews?.Any == true)
                throw new AdminUsersReviewRequiredException();
            ClearReviewRequirement();
        }
    }
    public void AcceptReloadReadback(long generation, string? accountId, string? path, bool complete = true)
    {
        lock (_gate)
        {
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            if (_reconciliation is { CanReadBack: true } pending && pending.AccountId == accountId && pending.Path == path &&
                (!pending.Collection || complete)) ClearReviewRequirement();
        }
    }
    public void AcceptVerifiedOutcome(long generation, AdminUsersReconciliation pending)
    {
        lock (_gate)
        {
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            if (pending.Collection && ReferenceEquals(_reconciliation, pending)) ClearReviewRequirement();
            else if (!pending.CanReadBack) CurrentReceiptReviews?.Accept(pending);
        }
    }
    private void ClearReviewRequirement()
    {
        _needsReview = _outcomeUnknown = false;
        _reconciliation = null;
    }
    public bool IsCurrent(long generation)
    {
        lock (_gate) return !_disposed && generation == _generation && _location is { IsCancellationRequested: false } && _lifetime.Owner is { IsCurrent: true };
    }

    public AdminUsersWorkspace(AdminMonitorAuthority authority)
    {
        _authority = authority;
        _lifetime = new(authority);
        _lifetime.Retired += AuthorityRetired;
    }
    public long Activate(bool preserveReview = false)
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
            if (!preserveReview || _epoch != owner.Epoch) ClearReviewRequirement();
            _epoch = owner.Epoch;
            return _generation;
        }
    }
    public Task<T> ReadAsync<T>(long generation, Func<MonitorAuthorityLease, CancellationToken, Task<T>> read) =>
        RunAsync(generation, read, exclusive: false);

    private async Task<T> RunAsync<T>(long generation, Func<MonitorAuthorityLease, CancellationToken, Task<T>> action, bool exclusive)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            token = _location!.Token; _active++;
        }
        var entered = false;
        var reading = false;
        try
        {
            await _foreground.WaitAsync(token).ConfigureAwait(false); entered = true;
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            if (exclusive)
            {
                // Keep admission closed while existing reads drain and throughout the reviewed write.
                Task? drain;
                lock (_gate) drain = _readsDrained?.Task;
                if (drain != null) await drain.WaitAsync(token).ConfigureAwait(false);
            }
            else
            {
                lock (_gate)
                {
                    if (_readers++ == 0) _readsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    reading = true;
                }
                _foreground.Release(); entered = false;
            }
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            using var lease = _lifetime.CaptureAction();
            var value = await action(lease, token).ConfigureAwait(false);
            if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
            token.ThrowIfCancellationRequested();
            return value;
        }
        finally
        {
            if (entered) _foreground.Release();
            lock (_gate)
            {
                if (reading && --_readers == 0)
                {
                    _readsDrained!.SetResult();
                    _readsDrained = null;
                }
                _active--;
                if (_disposed && _active == 0) _foreground.Dispose();
            }
        }
    }
    public async Task<AdminUsersDocument> WriteAsync(long generation, AdminUsersApi api, AdminUserReview review, AdminUsersWrite write)
    {
        var attempted = false;
        AdminUsersDocument? result = null;
        try
        {
            return await RunAsync(generation, async (lease, token) =>
            {
                var account = review.User.Text("id");
                var receipts = AdminUsersReceiptReviews.Tracks(write.Operation)
                    ? _authority.UsersReceiptReviews(lease.Epoch) ?? throw new MonitorAuthorityRetiredException() : null;
                if (receipts != null) await receipts.Writer.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!IsCurrent(generation)) throw new MonitorAuthorityRetiredException();
                    if (NeedsReloadReview || receipts?.Find(account, write.Operation) != null) throw new AdminUsersReviewRequiredException();
                    try
                    {
                        result = await api.WriteAsync(lease, review, write, token, () => attempted = true).ConfigureAwait(false);
                        return result;
                    }
                    catch (Exception error)
                    {
                        var failure = AdminFailures.Classify(error);
                        var unknown = attempted && error is not AdminUsersReviewRequiredException && (error is OperationCanceledException ||
                            failure is AdminFailureKind.Transport or AdminFailureKind.Server or AdminFailureKind.Unknown or AdminFailureKind.InvalidResponse or AdminFailureKind.Cancelled);
                        // Page cancellation can lose a sent receipt without retiring its authority epoch.
                        if (unknown && receipts != null) receipts.Record(account, write.Operation);
                        else lock (_gate)
                        {
                            if (IsCurrent(generation) && (error is AdminUsersReviewRequiredException || unknown ||
                                attempted && (failure is AdminFailureKind.PreconditionFailed or AdminFailureKind.PreconditionRequired)))
                            {
                                _outcomeUnknown = unknown;
                                _reconciliation = ReconciliationFor(account, write);
                                _needsReview = true;
                            }
                        }
                        throw;
                    }
                }
                finally { receipts?.Writer.Release(); }
            }, exclusive: true).ConfigureAwait(false);
        }
        catch
        {
            if (result != null) await result.DiscardViewAsAsync().ConfigureAwait(false);
            throw;
        }
    }
    private static AdminUsersReconciliation ReconciliationFor(string account, AdminUsersWrite write)
    {
        var root = AdminUsersApi.UserPath(account);
        var operation = write.Operation;
        return operation switch
        {
            AdminUsersOperation.CreateUser => new(operation, null, "/api/v2/admin/users", true, true),
            AdminUsersOperation.CreateInvitation or AdminUsersOperation.ResendInvitation or AdminUsersOperation.RevokeInvitation
                => new(operation, null, "/api/v2/admin/invitations", true, true),
            AdminUsersOperation.CreateCode or AdminUsersOperation.UpdateCode or AdminUsersOperation.TopUpCode or AdminUsersOperation.DeleteCode
                => new(operation, null, "/api/v2/admin/invite-codes", true, true),
            AdminUsersOperation.CreateProfile or AdminUsersOperation.UpdateProfile or AdminUsersOperation.DeleteProfile
                => new(operation, account, root + "/profiles", true, true),
            AdminUsersOperation.LinkIdentity or AdminUsersOperation.UnlinkIdentity => new(operation, account, root + "/identities", true, true),
            AdminUsersOperation.RevokeSession or AdminUsersOperation.RevokeSessions => new(operation, account, root + "/login-sessions", true, true),
            AdminUsersOperation.SetSetting or AdminUsersOperation.ResetSetting => new(operation, account, root + "/settings/values", true, true),
            AdminUsersOperation.SaveSections or AdminUsersOperation.ResetSections
                => new(operation, account, root + "/profiles/" + AdminUsersApi.Segment(write.ResourceId!) + "/sections?scope=home", true, true),
            AdminUsersOperation.Quota => new(operation, account, "/api/v2/admin/request-users/" + AdminUsersApi.Segment(account) + "/limit", false, true),
            _ => new(operation, account, null, false, true),
        };
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
