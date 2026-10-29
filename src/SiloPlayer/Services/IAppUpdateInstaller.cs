using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Services;

public interface IAppUpdateInstaller
{
    bool IsSupported { get; }
    Task<bool> ScheduleAsync(AppRelease release, string installerPath, bool restart, CancellationToken ct);
    void ConfirmNormalClose();
}

public interface IStagedAppUpdateInstaller
{
    Task<bool> PrepareAsync(AppRelease release, PreparedUpdatePackage package, bool restart, long ownerWindow, CancellationToken ct);
    bool HasPreparedHandoff { get; }
    void ArmNormalClose();
    void CancelPreparedHandoff();
    DateTimeOffset? AuthorityCooldownUntil=>null;
}
