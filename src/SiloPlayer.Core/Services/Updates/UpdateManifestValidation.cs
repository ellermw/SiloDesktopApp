using SiloPlayer.Core.Models.Updates;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Updates;

public static class UpdateManifestValidation
{
    public const string ManifestName = "SiloUpdate-Windows-x64.manifest.json";
    public const string PackName = "SiloUpdate-Windows-x64.pack";
    public const int MaximumManifestBytes = 2 * 1024 * 1024;
    public const long MaximumPackBytes = 512L * 1024 * 1024;
    public const long MaximumRawBytes = 2L * 1024 * 1024 * 1024;
    public static void Validate(UpdateManifest manifest)
    {
        if (manifest is null) throw new InvalidDataException("Missing update manifest.");
        if (manifest.Schema != 1 || manifest.BootstrapProtocol != 1 || !manifest.BackwardCompatibleData)
            throw new InvalidDataException("Unsupported update schema, bootstrap protocol or data migration.");
        if (!Version.TryParse(manifest.Version, out var version) || version.Build < 0 || version.Revision < 0 || version.ToString(4) != manifest.Version ||
            manifest.Version.Length > 64 || !Regex.IsMatch(manifest.Version, @"^\d+\.\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The manifest version must have four normalized components.");
        if (manifest.PackSize is <= 0 or > MaximumPackBytes || !IsHash(manifest.PackSha256) ||
            manifest.Files is null || manifest.Files.Count is < 2 or > 4096)
            throw new InvalidDataException("Invalid update manifest bounds.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long raw = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || file.Area is not ("client" or "bootstrap")) throw new InvalidDataException("Invalid update area.");
            ValidatePath(file.Path, file.Area);
            if (!paths.Add(file.Area + "/" + file.Path)) throw new InvalidDataException("Duplicate update path.");
            if (file.Size is < 0 or > MaximumPackBytes || file.PackedSize is <= 0 or > MaximumPackBytes ||
                file.Offset < 0 || file.Offset > manifest.PackSize - file.PackedSize || !IsHash(file.Sha256) || !IsHash(file.PackedSha256))
                throw new InvalidDataException("Invalid update object bounds or hash.");
            raw += file.Size;
            if (raw > MaximumRawBytes) throw new InvalidDataException("The update expands beyond the supported limit.");
        }
        foreach (var file in manifest.Files)
        {
            // A file may not also be an ancestor directory, even with different casing.
            for (var slash = file.Path.IndexOf('/'); slash >= 0; slash = file.Path.IndexOf('/', slash + 1))
                if (paths.Contains(file.Area + "/" + file.Path[..slash])) throw new InvalidDataException("Update file/directory collision.");
        }
        var objects = manifest.Files.OrderBy(file => file.Offset).ThenBy(file => file.PackedSize).ToArray();
        for (var i = 1; i < objects.Length; i++)
        {
            var previous = objects[i - 1]; var current = objects[i];
            if (current.Offset < previous.Offset + previous.PackedSize &&
                (current.Offset != previous.Offset || current.PackedSize != previous.PackedSize || current.Size != previous.Size ||
                 !current.PackedSha256.Equals(previous.PackedSha256, StringComparison.OrdinalIgnoreCase) ||
                 !current.Sha256.Equals(previous.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Update objects overlap.");
        }
        if (!paths.Contains("client/SiloPlayer.exe") || !paths.Contains("client/SiloPlayer.dll"))
            throw new InvalidDataException("The client entry points are missing.");
    }

    public static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);

    public static void ValidatePath(string path, string area)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
            path.Any(character => character < 32 || character == 127 || "<>\"|?*".Contains(character)))
            throw new InvalidDataException("Unsafe update path.");
        var parts = path.Split('/');
        if (parts.Any(part => part.Equals(".silo-owned.json", StringComparison.OrdinalIgnoreCase) || part.Equals(".silo-update", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Update payload cannot replace generated ownership metadata.");
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0].TrimEnd(' ');
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                Regex.IsMatch(stem, @"^(?:CON|CONIN\$|CONOUT\$|CLOCK\$|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Unsafe Windows update path.");
        }
        if (area == "bootstrap" && (parts[0].StartsWith('.') || new[] { "versions", "transactions", "state.json", "installation.json",
            "silo-installation.json", "silo-update-state.json" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Bootstrap files cannot replace installation control data.");
    }

    public static string ResolvePath(string root, string area, string path)
    {
        if (area is not ("client" or "bootstrap")) throw new InvalidDataException("Invalid update area.");
        ValidatePath(path, area);
        var basePath = Path.GetFullPath(Path.Combine(root, area));
        var result = Path.GetFullPath(Path.Combine(basePath, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe update destination.");
        EnsureOrdinaryPath(result);
        return result;
    }

    public static void EnsureOrdinaryPath(string path) => AppUpdateDownloader.EnsureOrdinaryPath(path);

    public static async Task<UpdateManifest> ReadAsync(string path, CancellationToken ct = default)
    {
        EnsureOrdinaryPath(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var bytes = await ReadBoundedAsync(file, MaximumManifestBytes, ct).ConfigureAwait(false);
        return Parse(bytes);
    }

    internal static UpdateManifest Parse(byte[] bytes)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = false, MaxDepth = 16 })
                ?? throw new InvalidDataException("Missing update manifest.");
            Validate(manifest); return manifest;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException)
        { throw new InvalidDataException("Invalid update manifest.", ex); }
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536]; int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximum) throw new InvalidDataException("Update content exceeds its limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    internal static async Task<bool> VerifyFileAsync(string path, long size, string hash, CancellationToken ct)
    {
        EnsureOrdinaryPath(path);
        if (!File.Exists(path)) return false;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return input.Length == size && Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}
