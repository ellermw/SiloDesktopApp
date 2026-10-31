using SiloPlayer.Core.Models.Updates;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace SiloPlayer.Core.Services.Updates;
public static class UpdatePackBuilder
{
    public static Task<UpdateManifest> BuildAsync(string clientDirectory, string? bootstrapDirectory, string outputDirectory,
        Version version, CancellationToken ct = default) => BuildCoreAsync(clientDirectory, bootstrapDirectory, outputDirectory, version, null, ct);

    public static Task<UpdateManifest> BuildAsync(string clientDirectory, string? bootstrapDirectory, string outputDirectory,
        ReleaseVersion release, CancellationToken ct = default) => BuildCoreAsync(clientDirectory, bootstrapDirectory, outputDirectory, release.GetWindowsVersion(), release.Text, ct);

    private static async Task<UpdateManifest> BuildCoreAsync(string clientDirectory, string? bootstrapDirectory, string outputDirectory,
        Version version, string? releaseVersion, CancellationToken ct)
    {
        var output = Path.GetFullPath(outputDirectory);
        UpdateManifestValidation.EnsureOrdinaryPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Pack output must be a fresh directory.");
        var sources = Enumerate(clientDirectory, "client").Concat(bootstrapDirectory is null ? [] : Enumerate(bootstrapDirectory, "bootstrap"))
            .OrderBy(item => item.Area, StringComparer.Ordinal).ThenBy(item => item.Relative, StringComparer.Ordinal).ToArray();
        foreach (var root in new[] { clientDirectory, bootstrapDirectory }.OfType<string>())
            if (output.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pack output cannot be inside a payload tree.");
        if (sources.Length > 4096) throw new InvalidDataException("Too many payload files.");
        Directory.CreateDirectory(output);
        var packPath = Path.Combine(output, UpdateManifestValidation.PackName);
        var scratch = Path.Combine(output, "object.tmp");
        var entries = new List<UpdateFileEntry>();
        var objects = new Dictionary<string, UpdateFileEntry>(StringComparer.Ordinal);
        long rawTotal = 0;
        await using (var pack = new FileStream(packPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                UpdateManifestValidation.EnsureOrdinaryPath(source.Full);
                await using var input = new FileStream(source.Full, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (input.Length > UpdateManifestValidation.MaximumPackBytes || (rawTotal += input.Length) > UpdateManifestValidation.MaximumRawBytes)
                    throw new InvalidDataException("Payload exceeds update limits.");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false)); input.Position = 0;
                var identity = hash + ":" + input.Length;
                if (objects.TryGetValue(identity, out var existing))
                { entries.Add(existing with { Path = source.Relative, Area = source.Area }); continue; }
                await using (var compressed = new FileStream(scratch, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                await using (var brotli = new BrotliStream(compressed, CompressionLevel.SmallestSize, leaveOpen: false))
                    await input.CopyToAsync(brotli, ct).ConfigureAwait(false);
                await using (var compressed = new FileStream(scratch, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
                {
                    if (compressed.Length > UpdateManifestValidation.MaximumPackBytes - pack.Position) throw new InvalidDataException("Pack exceeds the supported limit.");
                    var packedHash = Convert.ToHexString(await SHA256.HashDataAsync(compressed, ct).ConfigureAwait(false)); compressed.Position = 0;
                    var entry = new UpdateFileEntry(source.Relative, input.Length, hash, pack.Position, compressed.Length, packedHash, source.Area);
                    await compressed.CopyToAsync(pack, ct).ConfigureAwait(false);
                    objects.Add(identity, entry); entries.Add(entry);
                }
                File.Delete(scratch);
            }
        }
        await using var completed = File.OpenRead(packPath);
        var manifest = new UpdateManifest(1, GitHubReleaseReader.Normalize(version).ToString(4), completed.Length,
            Convert.ToHexString(await SHA256.HashDataAsync(completed, ct).ConfigureAwait(false)), entries) { ReleaseVersion = releaseVersion };
        UpdateManifestValidation.Validate(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        if (bytes.Length > UpdateManifestValidation.MaximumManifestBytes) throw new InvalidDataException("Generated manifest is too large.");
        await File.WriteAllBytesAsync(Path.Combine(output, UpdateManifestValidation.ManifestName), bytes, ct).ConfigureAwait(false);
        return manifest;
    }

    private static IEnumerable<(string Full, string Relative, string Area)> Enumerate(string root, string area)
    {
        root = Path.GetFullPath(root); UpdateManifestValidation.EnsureOrdinaryPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Payload directory is missing.");
        var pending = new Stack<string>(); pending.Push(root); var count = 0;
        while (pending.TryPop(out var directory))
        {
            UpdateManifestValidation.EnsureOrdinaryPath(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 8192) throw new InvalidDataException("Too many payload entries.");
                UpdateManifestValidation.EnsureOrdinaryPath(entry);
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                UpdateManifestValidation.ValidatePath(relative, area);
                if (Directory.Exists(entry)) pending.Push(entry);
                else yield return (entry, relative, area);
            }
        }
    }
}
