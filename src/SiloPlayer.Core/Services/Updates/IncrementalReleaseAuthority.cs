using SiloPlayer.Core.Models.Updates;
using System.Security.Cryptography;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>Establishes privileged-worker authority from a fixed, independently fetched GitHub release.</summary>
public sealed class IncrementalReleaseAuthority(HttpClient http)
{
    public async Task<(AppRelease Release, UpdateManifest Manifest, string ManifestPath)> FetchAsync(
        string tag, string destinationDirectory, CancellationToken ct)
    {
        var release = await new GitHubReleaseReader(http).ReadTagAsync(tag, ct).ConfigureAwait(false);
        var bytes = await IncrementalUpdateTransport.FetchManifestAsync(http, release, ct).ConfigureAwait(false);
        var manifest = UpdateManifestValidation.Parse(bytes);
        var root = Path.GetFullPath(destinationDirectory);
        UpdateManifestValidation.EnsureOrdinaryPath(root);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Convert.ToHexString(SHA256.HashData(bytes)) + ".manifest.json");
        UpdateManifestValidation.EnsureOrdinaryPath(path);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            await output.WriteAsync(bytes, ct).ConfigureAwait(false);
        return (release, manifest, path);
    }
}
