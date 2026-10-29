using SiloPlayer.Core.Models.Updates;
using System.Buffers;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace SiloPlayer.Core.Services.Updates;

public sealed class IncrementalUpdateClient(HttpClient http, string cacheDirectory, string installedDirectory, Action<string>? log = null)
{
    public async Task<PreparedUpdatePackage> PrepareAsync(AppRelease release, Action<IncrementalUpdateProgress> progress, CancellationToken ct)
    {
        var bytes = await IncrementalUpdateTransport.FetchManifestAsync(http, release, ct).ConfigureAwait(false);
        var manifest = UpdateManifestValidation.Parse(bytes);
        var identity = release.Incremental!.ManifestSha256.ToLowerInvariant();
        var folder = Path.Combine(Path.GetFullPath(cacheDirectory), "incremental-" + identity);
        UpdateManifestValidation.EnsureOrdinaryPath(folder);
        Directory.CreateDirectory(folder);
        // Cache identity is in the directory name, so partial objects cannot be adopted across manifests.
        var prepared = Path.Combine(folder, "prepared"); var objects = Path.Combine(folder, "objects");
        foreach (var directory in new[] { prepared, objects })
        { UpdateManifestValidation.EnsureOrdinaryPath(directory); Directory.CreateDirectory(directory); }
        var manifestPath = Path.Combine(prepared, UpdateManifestValidation.ManifestName);
        await WriteAtomicAsync(manifestPath, bytes, ct).ConfigureAwait(false);
        var fullPack = Path.Combine(folder, UpdateManifestValidation.PackName);
        var fullVerified = await UpdateManifestValidation.VerifyFileAsync(fullPack, manifest.PackSize, manifest.PackSha256, ct).ConfigureAwait(false);
        long transferred = 0; long networkBytes = 0; var completed = 0;
        var reusable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiredObjects = new HashSet<(long Offset, long PackedSize, string PackedSha256)>();
        foreach (var file in manifest.Files)
        {
            var destination = UpdateManifestValidation.ResolvePath(prepared, file.Area, file.Path);
            if (await UpdateManifestValidation.VerifyFileAsync(destination, file.Size, file.Sha256, ct).ConfigureAwait(false)) continue;
            var local = LocalPath(file);
            if (local is not null && await UpdateManifestValidation.VerifyFileAsync(local, file.Size, file.Sha256, ct).ConfigureAwait(false))
                reusable.Add(file.Area + "/" + file.Path);
            else if (!fullVerified && !await UpdateManifestValidation.VerifyFileAsync(Path.Combine(objects, file.PackedSha256.ToLowerInvariant() + ".object"), file.PackedSize, file.PackedSha256, ct).ConfigureAwait(false))
                requiredObjects.Add((file.Offset, file.PackedSize, file.PackedSha256));
        }
        var total = requiredObjects.Sum(f => f.PackedSize);
        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var destination = UpdateManifestValidation.ResolvePath(prepared, file.Area, file.Path);
            if (!await UpdateManifestValidation.VerifyFileAsync(destination, file.Size, file.Sha256, ct).ConfigureAwait(false))
            {
                var local = LocalPath(file);
                var copied = local is not null && reusable.Contains(file.Area + "/" + file.Path) && await CopyVerifiedAsync(local, destination, file, ct).ConfigureAwait(false);
                if (!copied)
                {
                    var compressed = Path.Combine(objects, file.PackedSha256.ToLowerInvariant() + ".object");
                    if (!await UpdateManifestValidation.VerifyFileAsync(compressed, file.PackedSize, file.PackedSha256, ct).ConfigureAwait(false))
                    {
                        UpdateManifestValidation.EnsureOrdinaryPath(compressed);
                        if (File.Exists(compressed)) File.Delete(compressed);
                        if (!fullVerified)
                        {
                            var fetched = await FetchObjectAsync(release, file, compressed, transferred, total,
                                completed, manifest.Files.Count, progress, ct).ConfigureAwait(false);
                            networkBytes += fetched.NetworkBytes;
                            if (fetched.Ranged) transferred += file.PackedSize;
                            else
                            {
                                total = transferred + manifest.PackSize;
                                progress(new("Downloading complete package", transferred, total, completed, manifest.Files.Count));
                                await FetchFullAsync(release, fullPack, p => progress(new("Downloading complete package", transferred + p, total, completed, manifest.Files.Count)), ct).ConfigureAwait(false);
                                networkBytes += manifest.PackSize;
                                transferred = total;
                                fullVerified = true;
                            }
                        }
                        if (fullVerified) await ExtractObjectAsync(fullPack, compressed, file, ct).ConfigureAwait(false);
                    }
                    await Task.Run(() => Decode(compressed, destination, file, ct), ct).ConfigureAwait(false);
                }
            }
            completed++;
            progress(new("Preparing files", transferred, total, completed, manifest.Files.Count));
        }
        var package = new PreparedUpdatePackage(prepared, manifestPath, identity, manifest);
        progress(new("Verifying", transferred, total, completed, manifest.Files.Count));
        if (!await VerifyPreparedAsync(package, ct).ConfigureAwait(false)) throw new InvalidDataException("The prepared update could not be verified.");
        log?.Invoke($"incremental_verified version={manifest.Version} downloaded_bytes={networkBytes} files={completed}");
        return package;
    }

    public async Task<bool> VerifyPreparedAsync(PreparedUpdatePackage package, CancellationToken ct)
    {
        try
        {
            UpdateManifestValidation.Validate(package.Manifest);
            if (!UpdateManifestValidation.IsHash(package.ManifestSha256)) return false;
            UpdateManifestValidation.EnsureOrdinaryPath(package.Directory);
            UpdateManifestValidation.EnsureOrdinaryPath(package.ManifestPath);
            await using var manifestFile = new FileStream(package.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            var bytes = await UpdateManifestValidation.ReadBoundedAsync(manifestFile, UpdateManifestValidation.MaximumManifestBytes, ct).ConfigureAwait(false);
            if (bytes.Length > UpdateManifestValidation.MaximumManifestBytes || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(package.ManifestSha256, StringComparison.OrdinalIgnoreCase)) return false;
            var parsed = UpdateManifestValidation.Parse(bytes);
            if (!JsonSerializer.SerializeToUtf8Bytes(parsed).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(package.Manifest))) return false;
            foreach (var file in parsed.Files)
                if (!await UpdateManifestValidation.VerifyFileAsync(UpdateManifestValidation.ResolvePath(package.Directory, file.Area, file.Path), file.Size, file.Sha256, ct).ConfigureAwait(false)) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { return false; }
    }

    private string? LocalPath(UpdateFileEntry entry)
    {
        var client = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installedDirectory));
        var root = client;
        if (entry.Area == "bootstrap")
        {
            var parent = Path.GetDirectoryName(client);
            // Never infer a registered bootstrap root from an arbitrary legacy client directory.
            if (parent is null || !Path.GetFileName(parent).Equals("versions", StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(Path.GetFileName(client), out var version) || version.ToString(4) != Path.GetFileName(client)) return null;
            root = Path.GetDirectoryName(parent)!;
        }
        var result = Path.GetFullPath(Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
        UpdateManifestValidation.EnsureOrdinaryPath(result);
        return result;
    }

    private static async Task<bool> CopyVerifiedAsync(string source, string destination, UpdateFileEntry entry, CancellationToken ct)
    {
        UpdateManifestValidation.EnsureOrdinaryPath(source);
        if (!File.Exists(source)) return false;
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != entry.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        input.Position = 0;
        var temporary = NewTemporary(destination);
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            if (!await UpdateManifestValidation.VerifyFileAsync(temporary, entry.Size, entry.Sha256, ct).ConfigureAwait(false)) throw new InvalidDataException("Local update copy changed during verification.");
            File.Move(temporary, destination, true); return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<(bool Ranged, long NetworkBytes)> FetchObjectAsync(AppRelease release, UpdateFileEntry entry, string destination,
        long transferred, long total, int completed, int count, Action<IncrementalUpdateProgress> progress, CancellationToken ct)
    {
        var partial = destination + ".part"; UpdateManifestValidation.EnsureOrdinaryPath(partial);
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset > entry.PackedSize) { File.Delete(partial); offset = 0; }
        if (offset < entry.PackedSize)
        {
            using var response = await IncrementalUpdateTransport.OpenAsync(http, release.Incremental!.PackUrl,
                entry.Offset + offset, entry.Offset + entry.PackedSize - 1, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) return (false, 0);
            if (response.StatusCode != HttpStatusCode.PartialContent) throw new HttpRequestException("Update range download failed.", null, response.StatusCode);
            var range = response.Content.Headers.ContentRange;
            if (range is null || range.Unit != "bytes" || range.From != entry.Offset + offset || range.To != entry.Offset + entry.PackedSize - 1 || range.Length != release.Incremental.PackSize)
                throw new InvalidDataException("The update range does not match the requested object.");
            IncrementalUpdateTransport.ValidateBody(response, entry.PackedSize - offset);
            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            await CopyBoundedAsync(body, output, entry.PackedSize - offset,
                p => progress(new("Downloading changed files", transferred + offset + p, total, completed, count)), ct).ConfigureAwait(false);
        }
        if (!await UpdateManifestValidation.VerifyFileAsync(partial, entry.PackedSize, entry.PackedSha256, ct).ConfigureAwait(false))
        { File.Delete(partial); throw new InvalidDataException("Update object checksum mismatch."); }
        File.Move(partial, destination, true); return (true, entry.PackedSize - offset);
    }

    private async Task FetchFullAsync(AppRelease release, string path, Action<long> progress, CancellationToken ct)
    {
        using var response = await IncrementalUpdateTransport.OpenAsync(http, release.Incremental!.PackUrl, null, null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Full update download failed.", null, response.StatusCode);
        IncrementalUpdateTransport.ValidateBody(response, release.Incremental.PackSize);
        var temporary = NewTemporary(path);
        try
        {
            await using (var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                await CopyBoundedAsync(body, output, release.Incremental.PackSize, progress, ct).ConfigureAwait(false);
            if (!await UpdateManifestValidation.VerifyFileAsync(temporary, release.Incremental.PackSize, release.Incremental.PackSha256, ct).ConfigureAwait(false)) throw new InvalidDataException("Complete update checksum mismatch.");
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task ExtractObjectAsync(string pack, string destination, UpdateFileEntry entry, CancellationToken ct)
    {
        UpdateManifestValidation.EnsureOrdinaryPath(pack);
        await using var input = new FileStream(pack, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        input.Position = entry.Offset; var temporary = NewTemporary(destination);
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536]; long remaining = entry.PackedSize;
                while (remaining > 0)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct).ConfigureAwait(false);
                    if (read == 0) throw new InvalidDataException("Truncated update pack.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false); remaining -= read;
                }
            }
            if (!await UpdateManifestValidation.VerifyFileAsync(temporary, entry.PackedSize, entry.PackedSha256, ct).ConfigureAwait(false)) throw new InvalidDataException("Pack object checksum mismatch.");
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long length, Action<long> progress, CancellationToken ct)
    {
        var buffer = new byte[65536]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (read > length - total) throw new InvalidDataException("Update content exceeds its advertised length.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false); total += read; progress(total);
        }
        if (total != length) throw new InvalidDataException("Update content was truncated.");
    }

    private static void Decode(string compressed, string destination, UpdateFileEntry entry, CancellationToken ct)
    {
        UpdateManifestValidation.EnsureOrdinaryPath(compressed);
        using var input = new FileStream(compressed, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != entry.PackedSize || !Convert.ToHexString(SHA256.HashData(input)).Equals(entry.PackedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Cached object checksum mismatch.");
        input.Position = 0; var temporary = NewTemporary(destination);
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var decoder = new BrotliDecoder();
                try
                {
                    var encoded = new byte[65536]; var decoded = new byte[65536];
                    var available = 0; var position = 0; long writtenTotal = 0; long consumedTotal = 0;
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (position == available) { available = input.Read(encoded); position = 0; }
                        var status = decoder.Decompress(encoded.AsSpan(position, available - position), decoded,
                            out var consumed, out var written);
                        position += consumed; consumedTotal += consumed; writtenTotal += written;
                        if (writtenTotal > entry.Size) throw new InvalidDataException("Update object expands beyond its declared length.");
                        output.Write(decoded, 0, written);
                        if (status == OperationStatus.Done)
                        {
                            if (writtenTotal != entry.Size || consumedTotal != entry.PackedSize) throw new InvalidDataException("Update object has trailing data or an incorrect decoded length.");
                            break;
                        }
                        if (status == OperationStatus.InvalidData || status == OperationStatus.NeedMoreData && available == 0 || consumed == 0 && written == 0 && status != OperationStatus.NeedMoreData)
                            throw new InvalidDataException("Invalid compressed update object.");
                        if (status == OperationStatus.NeedMoreData && position != available) throw new InvalidDataException("Invalid compressed update boundary.");
                    }
                }
                finally { decoder.Dispose(); }
            }
            using (var decoded = File.OpenRead(temporary))
                if (!Convert.ToHexString(SHA256.HashData(decoded)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Decoded update checksum mismatch.");
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string NewTemporary(string destination)
    {
        UpdateManifestValidation.EnsureOrdinaryPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        UpdateManifestValidation.EnsureOrdinaryPath(destination);
        return destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var temporary = NewTemporary(path);
        try { await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
