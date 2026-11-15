namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Implemented by an admin page with staged edits. The shell asks before any
/// in-admin navigation, Back, search pick or app departure; MainWindow uses
/// <see cref="RequiresLeaveConfirmation"/> to intercept Back to App and window
/// close. A retired owner's draft must never block leaving. Logs › Settings
/// keeps its own existing guard.
/// </summary>
public interface IAdminNavigationGuard
{
    /// <summary>True only while a current owner holds unsaved edits.</summary>
    bool RequiresLeaveConfirmation { get; }

    /// <summary>Confirms discarding unsaved edits; false keeps the page and its draft.</summary>
    Task<bool> CanLeaveAsync();
}

/// <summary>
/// Implemented by an admin page that owns reads, timers, sockets, dialogs or
/// drafts. The shell calls it when the whole Admin area departs, because an
/// outer Frame departure neither navigates nor unloads the nested Frame.
/// Retire everything and clear private data; never resume from it.
/// </summary>
public interface IAdminPageSuspension
{
    void SuspendAdminPage();
}

/// <summary>
/// The unsaved-changes decision every staged admin page shares (WebUI
/// <c>UnsavedChangesGuard</c>; native Logs › Settings behaviour): nothing to
/// confirm without a current owner and a dirty draft; a confirmation already
/// in progress (or a dialog collision, reported as null) keeps the draft;
/// Discard runs only for the owner that asked; an owner that retired while
/// asking releases navigation without accepting its draft.
/// </summary>
public sealed class AdminLeaveGuard(Func<MonitorAuthorityLease?> owner, Func<bool> isDirty, Action discard)
{
    private bool _confirming;

    public bool IsConfirming => _confirming;

    public bool RequiresConfirmation => owner() is { IsCurrent: true } && isDirty();

    /// <param name="confirmDiscard">
    /// Shows the discard confirmation under the given owner: true discards,
    /// false cancels, null means it could not be shown (another dialog is open).
    /// </param>
    public async Task<bool> CanLeaveAsync(Func<MonitorAuthorityLease, Task<bool?>> confirmDiscard)
    {
        var current = owner();
        if (current is not { IsCurrent: true } || !isDirty()) return true;
        if (_confirming) return false;
        _confirming = true;
        try
        {
            bool? answer;
            try { answer = await confirmDiscard(current); }
            catch (OperationCanceledException) { return !current.IsCurrent || !ReferenceEquals(current, owner()); }
            if (!current.IsCurrent || !ReferenceEquals(current, owner())) return true;
            if (answer != true) return false;
            discard();
            return true;
        }
        finally { _confirming = false; }
    }
}

/// <summary>
/// One admin page's authority lifetime under the single shared
/// <see cref="AdminMonitorAuthority"/>: an owner lease for the page's reads,
/// per-action leases bound to that owner, and retirement on any authority
/// change. It is not another authority source; it holds no credentials and is
/// never persisted. (Same contract as the Monitor Logs lifetime.)
/// </summary>
public sealed class AdminAuthorityLifetime : IDisposable
{
    private readonly AdminMonitorAuthority _authority;
    private readonly object _gate = new();
    private MonitorAuthorityLease? _owner;
    private bool _disposed;
    private bool _suspended;

    public AdminAuthorityLifetime(AdminMonitorAuthority authority)
    {
        _authority = authority;
        authority.AvailabilityChanged += OnAuthorityChanged;
    }

    /// <summary>Raised only for authority changes while active. Marshal, clear retired data, then reopen if available. Explicit departure never raises this event.</summary>
    public event Action? Retired;

    public MonitorAuthorityLease? Owner { get { lock (_gate) return _owner; } }

    /// <summary>Captures a new owner, retiring the previous one. Throws when not acting as admin.</summary>
    public MonitorAuthorityLease Open()
    {
        var captured = _authority.Capture();
        lock (_gate)
        {
            if (_disposed) { captured.Dispose(); throw new ObjectDisposedException(nameof(AdminAuthorityLifetime)); }
            if (_suspended) { captured.Dispose(); throw new MonitorAuthorityRetiredException(); }
            _owner?.Dispose();
            captured.ThrowIfRetired();
            return _owner = captured;
        }
    }

    /// <summary>A lease for one action (confirmation, write, file delivery) that dies with the current owner.</summary>
    public MonitorAuthorityLease CaptureAction()
    {
        MonitorAuthorityLease owner;
        lock (_gate) owner = _owner ?? throw new MonitorAuthorityRetiredException();
        owner.ThrowIfRetired();
        var action = _authority.Capture(owner.Token);
        lock (_gate)
        {
            if (!_disposed && ReferenceEquals(owner, _owner) && owner.IsCurrent && action.IsCurrent && action.Epoch == owner.Epoch) return action;
            action.Dispose();
            throw new MonitorAuthorityRetiredException();
        }
    }

    /// <summary>Whether <paramref name="lease"/> belongs to the still-current owner's authority.</summary>
    public bool IsCurrent(MonitorAuthorityLease? lease)
    {
        lock (_gate)
            return !_disposed && lease is { IsCurrent: true } && _owner is { IsCurrent: true } owner && owner.Epoch == lease.Epoch &&
                   string.Equals(owner.AccountId, lease.AccountId, StringComparison.Ordinal);
    }

    /// <summary>Explicit page activation after suspension. Call only when the page becomes visible again, then Open.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _suspended = false;
        }
    }

    private void OnAuthorityChanged()
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            if (_disposed || _suspended) return;
            owner = _owner; _owner = null;
        }
        owner?.Dispose();
        Retired?.Invoke();
    }

    /// <summary>Suspends the page without requesting a restart. Resume is required before another Open.</summary>
    public void Retire()
    {
        MonitorAuthorityLease? owner;
        lock (_gate) { _suspended = true; owner = _owner; _owner = null; }
        owner?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _authority.AvailabilityChanged -= OnAuthorityChanged;
        Retire();
        Retired = null;
    }
}
