using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Services;

public interface IAppUpdateInstaller
{
    bool IsSupported { get; }
    bool CanScheduleFullInstaller => IsSupported;
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

/// <summary>Package is set when the download is ready; otherwise Error explains a failure, and neither means the update needs another path.</summary>
public sealed record WindowedUpdateDownload(PreparedUpdatePackage? Package, string? Error)
{
    /// <summary>Records the user's Later for the next start; false when it could not be saved, so the window must not promise it.</summary>
    public Func<bool>? Later { get; init; }
}

/// <summary>Staged: installed alongside and waiting for Silo to close. Deferred: downloaded, and the user chose Later.</summary>
public enum WindowedUpdateOutcome { NotStarted, Staged, Deferred }

/// <summary>Opens the update window first; it shows the client's download, then installs the result.</summary>
public interface IWindowedAppUpdateInstaller
{
    /// <param name="confirmRestart">After the download the window asks Restart now or Later before administrator approval.</param>
    Task<WindowedUpdateOutcome> PrepareWithDownloadAsync(AppRelease release, string packageDirectory, bool restart, bool confirmRestart, long ownerWindow,
        Func<Action<IncrementalUpdateProgress>, CancellationToken, Task<WindowedUpdateDownload>> download, CancellationToken ct);
}
