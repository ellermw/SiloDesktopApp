using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public static partial class SideBySideInstallation
{
    // Protocol 1 keeps its managed launcher/runtime immutable. An interrupted multi-file
    // bootstrap replacement could prevent the recovery code itself from starting.
    // A changed bootstrap needs the ordinary full installer migration instead.
    private static void VerifyBootstrapCompatible(string root, UpdateManifest manifest)
    {
        foreach (var entry in manifest.Files.Where(f => f.Area == "bootstrap"))
        {
            var path = RelativeFile(root, entry.Path);
            using var parents = LockDirectories(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) throw new InvalidDataException("The launcher bootstrap requires a full installer migration.");
            using var file = OpenFile(path, FileAccess.Read, FileShare.Read);
            if (!Matches(file, entry.Size, entry.Sha256))
                throw new InvalidDataException("The launcher bootstrap requires a full installer migration.");
        }
    }
}
