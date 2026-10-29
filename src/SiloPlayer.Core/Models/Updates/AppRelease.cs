namespace SiloPlayer.Core.Models.Updates;

public enum AppUpdateMode { NotifyOnly, Automatic }

public sealed record AppRelease(Version Version, string Tag, string Notes, DateTimeOffset PublishedAt,
    Uri InstallerUrl, string Sha256, long Size)
{
    public IncrementalReleaseAssets? Incremental { get; init; }
}

public sealed record AppUpdateState(AppRelease? Release = null, bool IsChecking = false,
    bool IsDownloading = false, long DownloadedBytes = 0, string? ReadyInstaller = null, string? Error = null)
{
    public PreparedUpdatePackage? PreparedPackage { get; init; }
    public IncrementalUpdateProgress? PreparationProgress { get; init; }
}
