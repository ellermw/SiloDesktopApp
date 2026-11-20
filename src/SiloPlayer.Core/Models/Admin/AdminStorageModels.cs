namespace SiloPlayer.Core.Models.Admin;

/// <summary>
/// <c>artwork_storage</c> from <c>GET /api/v2/admin/server/status</c>. When <see cref="StatusKnown"/> is false the lock
/// state could not be read, and <see cref="Locked"/> = false must never be treated as permission to move storage.
/// </summary>
public sealed record AdminArtworkStorageStatus(bool StatusKnown, bool Locked, bool PrivateLocked, string Backend);

/// <summary><c>GET /api/v2/admin/storage-transitions/capabilities</c>; only the server-wide state is used by the page.</summary>
public sealed record AdminStorageTransitionCapabilities(string State, bool? Allowed)
{
    /// <summary>Official <c>state === "available" &amp;&amp; allowed !== false</c>.</summary>
    public bool ManagedTransitionsAvailable => State == "available" && Allowed != false;
}

/// <summary>
/// <c>GET /api/v2/admin/storage-transitions/source-health</c>. Every text is the server's safe summary
/// (no storage errors or locations) and is still bounded on receipt.
/// </summary>
public sealed record AdminStorageSourceHealth(
    string CurrentBackend, bool Reachable, bool PublicConfigured, bool PublicReachable,
    bool PrivateConfigured, bool PrivateReachable, bool ReachabilityProbed, string? Message,
    bool RecoveryPending, string? RecoveryState, string? RecoveryError, int RecoveryProgressPercent)
{
    public override string ToString() => nameof(AdminStorageSourceHealth);
}

/// <summary>
/// The latest <c>storage_transition</c> admin job, mapped like the official <c>adminJobFromV2</c>:
/// <c>succeeded</c> → completed, <c>canceled</c> → cancelled, <c>canceling</c> → running. Only the safe structured
/// <c>storage_transition_result</c> is kept; free-form failure text is never retained for this kind.
/// </summary>
public sealed record AdminStorageTransitionJob(
    string Id, string Status, string? Phase, long? VerifiedObjects, string? FailureCategory, bool ManualRestartRequired)
{
    public bool IsActive => Status is "queued" or "running";
    public bool IsFailedOrCancelled => Status is "failed" or "cancelled";
}
