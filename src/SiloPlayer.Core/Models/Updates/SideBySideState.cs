namespace SiloPlayer.Core.Models.Updates;

public sealed record SideBySideState(string ActiveVersion, string? PreviousVersion, string? PendingVersion, string? TransactionId);

public sealed record StagedUpdate(string InstallationRoot, string TransactionId, string Version,
    string VersionDirectory, string BootstrapDirectory, UpdateManifest Manifest);

/// <summary>A per-user observation, never privileged authority for modifying an installation.</summary>
public sealed record SideBySideBootReceipt(string RootHash, string TransactionId, string Version,
    int ProcessId, long ProcessStartUtcTicks, string Executable, bool Ready, bool Failed = false)
{
    public bool Starting { get; init; }
    public int ObserverProcessId { get; init; }
    public long ObserverStartUtcTicks { get; init; }
}
