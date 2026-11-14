using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The single owner of Admin Monitor authority. Every Monitor read, socket,
/// timer and action runs under a <see cref="MonitorAuthorityLease"/> captured
/// here and checks it before sending, after every awaited response and again
/// inside dispatcher or file publication.
/// <para>
/// Acting admin is re-evaluated live through
/// <see cref="AuthorizationPolicy.IsActingAdmin(AuthService)"/> (role exactly
/// admin with the primary profile, or no selected profile; a selected profile
/// that has not resolved yet fails closed). A cached role alone never grants it.
/// </para>
/// <para>
/// All leases retire together (their epoch ends and their token cancels) when
/// the account, role, acting-admin state, selected profile, profile proof, server
/// or API request context changes, including the event channel's
/// <c>access_changed</c>/4001 and transport catch-up, which reach this owner
/// through <see cref="SiloApiClient.ContextInvalidated"/>. Losing and regaining
/// admin on the same account still retires, so work from before the loss can
/// never revive. An access-token refresh changes none of these and is benign.
/// </para>
/// <para>
/// <see cref="AvailabilityChanged"/> is raised (on the raising thread) after
/// every retirement and every availability change; handlers marshal to their
/// dispatcher, clear retired data and recapture if <see cref="IsAvailable"/>.
/// </para>
/// </summary>
public sealed class AdminMonitorAuthority : IDisposable
{
    private readonly SiloApiClient _client;
    private readonly AuthService _auth;
    private readonly object _gate = new();
    private readonly HashSet<MonitorAuthorityLease> _leases = [];
    private long _epoch = 1;
    private Fingerprint? _fingerprint;
    private bool _disposed;

    public AdminMonitorAuthority(SiloApiClient client, AuthService auth)
    {
        _client = client;
        _auth = auth;
        _fingerprint = ReadFingerprint();
        _auth.UserChanged += Reevaluate;
        _auth.ProfileChanged += Reevaluate;
        _auth.LoggedOut += Reevaluate;
        _client.ContextInvalidated += Reevaluate;
    }

    /// <summary>The current epoch; it only grows.</summary>
    public long Epoch { get { lock (_gate) return _epoch; } }

    /// <summary>Whether the signed-in principal acts as admin right now.</summary>
    public bool IsAvailable
    {
        get
        {
            if (_disposed) return false;
            return ReadFingerprint() is { } current && current.ActingAdmin;
        }
    }

    /// <summary>Raised after leases retire or availability changes. May run on any thread.</summary>
    public event Action? AvailabilityChanged;

    /// <summary>
    /// Captures a lease for the current authority. Fails closed with
    /// <see cref="MonitorAuthorityRetiredException"/> when not acting as admin.
    /// The lease's token cancels on retirement, on dispose, or with <paramref name="ct"/>.
    /// </summary>
    public MonitorAuthorityLease Capture(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Observe any change whose event has not been delivered yet first, so a
        // capture can never pair a new principal with the previous epoch.
        Reevaluate();
        MonitorAuthorityLease lease;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var current = ReadFingerprint();
            if (current is not { ActingAdmin: true } || current != _fingerprint)
                throw new MonitorAuthorityRetiredException();
            lease = new MonitorAuthorityLease(this, current.Context, current.AccountId, _epoch, ct);
            _leases.Add(lease);
        }
        return lease;
    }

    /// <summary>Live check: same epoch, not disposed, same API context and account, still acting admin.</summary>
    public bool IsCurrent(MonitorAuthorityLease? lease)
    {
        if (lease == null || !ReferenceEquals(lease.Owner, this) || lease.IsDisposed) return false;
        lock (_gate)
        {
            if (_disposed || lease.Epoch != _epoch) return false;
        }
        var current = ReadFingerprint();
        lock (_gate)
        {
            return !_disposed && !lease.IsDisposed && lease.Epoch == _epoch &&
                   current == _fingerprint && current is { ActingAdmin: true } &&
                   current.Context == lease.Context &&
                   string.Equals(current.AccountId, lease.AccountId, StringComparison.Ordinal);
        }
    }

    /// <summary>Retires every outstanding lease. Use for explicit sign-out style resets.</summary>
    public void Retire()
    {
        List<MonitorAuthorityLease> retired;
        lock (_gate)
        {
            if (_disposed) return;
            _epoch++;
            _fingerprint = ReadFingerprint();
            retired = [.. _leases];
            _leases.Clear();
        }
        foreach (var lease in retired) lease.Cancel();
        RaiseAvailabilityChanged();
    }

    private void Reevaluate()
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            changed = ReadFingerprint() != _fingerprint;
        }
        if (changed) Retire();
    }

    internal void Release(MonitorAuthorityLease lease)
    {
        lock (_gate) _leases.Remove(lease);
    }

    private void RaiseAvailabilityChanged()
    {
        foreach (var handler in AvailabilityChanged?.GetInvocationList() ?? [])
        {
            try { ((Action)handler)(); }
            catch (Exception ex) { LocalLog.AppendLine("admin_monitor.txt", $"availability handler failed: {ex.GetType().Name}"); }
        }
    }

    private Fingerprint? ReadFingerprint()
    {
        var user = _auth.CurrentUser;
        if (user == null) return null;
        var context = _client.CaptureContext();
        if (string.IsNullOrWhiteSpace(context.BaseUrl)) return null;
        var profile = _auth.SelectedProfile;
        return new Fingerprint(
            context,
            user.Id,
            user.Role ?? "",
            _auth.SelectedProfileId,
            profile?.Id,
            profile?.IsPrimary == true,
            AuthorizationPolicy.IsActingAdmin(_auth));
    }

    public void Dispose()
    {
        List<MonitorAuthorityLease> retired;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _epoch++;
            retired = [.. _leases];
            _leases.Clear();
        }
        _auth.UserChanged -= Reevaluate;
        _auth.ProfileChanged -= Reevaluate;
        _auth.LoggedOut -= Reevaluate;
        _client.ContextInvalidated -= Reevaluate;
        foreach (var lease in retired) lease.Cancel();
        RaiseAvailabilityChanged();
        AvailabilityChanged = null;
    }

    private sealed record Fingerprint(
        ApiRequestContext Context,
        string AccountId,
        string Role,
        string? SelectedProfileId,
        string? ResolvedProfileId,
        bool PrimaryProfile,
        bool ActingAdmin);
}

/// <summary>
/// One captured Admin Monitor authority: API context, account and epoch, with a
/// token that cancels when it retires. Never serialized or persisted; it holds
/// no credentials. Dispose it when the owning page, timer or request ends.
/// </summary>
public sealed class MonitorAuthorityLease : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private int _disposed;

    internal MonitorAuthorityLease(AdminMonitorAuthority owner, ApiRequestContext context, string accountId, long epoch,
        CancellationToken linked)
    {
        Owner = owner;
        Context = context;
        AccountId = accountId;
        Epoch = epoch;
        _cts = linked.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(linked) : new CancellationTokenSource();
        Token = _cts.Token;
    }

    internal AdminMonitorAuthority Owner { get; }
    public ApiRequestContext Context { get; }
    public string AccountId { get; }
    public long Epoch { get; }
    /// <summary>Cancels on retirement, on dispose, or with the token passed to capture.</summary>
    public CancellationToken Token { get; }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public bool IsCurrent => !Token.IsCancellationRequested && Owner.IsCurrent(this);

    /// <summary>Throws <see cref="MonitorAuthorityRetiredException"/> unless this lease is still current.</summary>
    public void ThrowIfRetired()
    {
        if (!IsCurrent) throw new MonitorAuthorityRetiredException(Token);
    }

    internal void Cancel()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Owner.Release(this);
        Cancel();
        _cts.Dispose();
    }

    public override string ToString() => nameof(MonitorAuthorityLease);
}

/// <summary>The Monitor authority a read or action was started under has retired (or never existed).</summary>
public sealed class MonitorAuthorityRetiredException : OperationCanceledException
{
    public MonitorAuthorityRetiredException() : base("The admin session changed. Reload and try again.") { }
    public MonitorAuthorityRetiredException(CancellationToken token) : base("The admin session changed. Reload and try again.", token) { }
}
