using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Services;

public interface IAppUpdateInstaller
{
    bool IsSupported { get; }
    Task<bool> ScheduleAsync(AppRelease release, string installerPath, bool restart, CancellationToken ct);
    void ConfirmNormalClose();
}
