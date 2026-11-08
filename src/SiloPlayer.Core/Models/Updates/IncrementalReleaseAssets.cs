namespace SiloPlayer.Core.Models.Updates;

public sealed record IncrementalReleaseAssets(Uri ManifestUrl, string ManifestSha256, long ManifestSize,
    Uri PackUrl, string PackSha256, long PackSize);

public sealed record UpdateFileEntry(string Path, long Size, string Sha256, long Offset, long PackedSize,
    string PackedSha256, string Area = "client");

public sealed record UpdateManifest(int Schema, string Version, long PackSize, string PackSha256,
    IReadOnlyList<UpdateFileEntry> Files, int BootstrapProtocol = 1, bool BackwardCompatibleData = true)
{
    public string? ReleaseVersion { get; init; }
}

public sealed record PreparedUpdatePackage(string Directory, string ManifestPath, string ManifestSha256, UpdateManifest Manifest);

// CompletedFiles/TotalFiles count every file in the release inventory. The Download*
// and CurrentFile* values describe only what is actually fetched over the network:
// changed objects (or one complete package when ranges are unavailable) and the
// object being fetched now.
public sealed record IncrementalUpdateProgress(string Stage, long CompletedBytes, long TotalBytes, int CompletedFiles, int TotalFiles,
    int DownloadedFiles = 0, int DownloadFiles = 0, long CurrentFileBytes = 0, long CurrentFileTotal = 0);
