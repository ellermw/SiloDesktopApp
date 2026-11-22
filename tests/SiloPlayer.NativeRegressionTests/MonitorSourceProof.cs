using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;

internal static class MonitorSourceProof
{
    /// <summary>
    /// Proves every compiled document of the product and host assemblies is the checked-out file with the checksum in its PDB. The root is
    /// <c>SILO_MONITOR_SOURCE_ROOT</c>, else the acceptance script's <c>SILO_ACCEPTANCE_SOURCE_ROOT</c> (always set by it, so a default
    /// run verifies). With neither set it logs a SKIP line instead of passing silently.
    /// </summary>
    internal static void Verify()
    {
        var root = Environment.GetEnvironmentVariable("SILO_MONITOR_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(root)) root = Environment.GetEnvironmentVariable("SILO_ACCEPTANCE_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Program.Log("SKIP: exact-source portable PDB proof not run (neither SILO_MONITOR_SOURCE_ROOT nor SILO_ACCEPTANCE_SOURCE_ROOT is set)");
            return;
        }
        root = Path.GetFullPath(root);
        var rows = new List<object>();
        // The WinUI project compiles Core/Player sources into SiloPlayer.dll.
        var assemblies = new List<(string Name, string Directory)> { ("SiloPlayer", Program.AppDirectory), ("SiloPlayer.NativeRegressionTests", AppContext.BaseDirectory) };
        if (Environment.GetEnvironmentVariable("SILO_MONITOR_UNIT_DIRECTORY") is { Length: > 0 } units)
            foreach (var name in new[] { "SiloPlayer.Tests", "SiloPlayer.Core", "SiloPlayer.Player" }) assemblies.Add((name, units));
        foreach (var (name, directory) in assemblies)
        {
            var pdb = Path.Combine(directory, name + ".pdb");
            using var stream = File.OpenRead(pdb);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            var documents = new List<object>();
            foreach (var handle in reader.Documents)
            {
                var doc = reader.GetDocument(handle);
                var path = reader.GetString(doc.Name);
                if (!File.Exists(path))
                {
                    // Roslyn generator documents have checksums in the PDB but
                    // are not emitted as loose files. Do not exempt handwritten input.
                    if (path.StartsWith(Path.Combine(root, ".codex-tmp") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                        && path.EndsWith(".g.cs", StringComparison.Ordinal))
                    {
                        documents.Add(new { path, checksum = Convert.ToHexString(reader.GetBlobBytes(doc.Hash)), generated = true });
                        continue;
                    }
                    throw new InvalidOperationException("PDB source missing: " + path);
                }
                if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFullPath(path).StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("PDB source from another checkout: " + path);
                var expected = reader.GetBlobBytes(doc.Hash);
                var bytes = File.ReadAllBytes(path);
                var algorithm = reader.GetGuid(doc.HashAlgorithm);
                var actual = algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16") ? SHA256.HashData(bytes)
                    : algorithm == new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460") ? SHA1.HashData(bytes)
                    : throw new InvalidOperationException("Unsupported PDB checksum");
                if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("PDB source checksum mismatch: " + path);
                documents.Add(new { path, checksum = Convert.ToHexString(actual) });
            }
            rows.Add(new { assembly = name, assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name + ".dll")))), pdbSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pdb))), documents });
            Program.Log($"PASS: exact-source portable PDB {name}: {documents.Count} documents");
        }
        File.WriteAllText(Path.Combine(Program.ResultDirectory, "source-pdb-proof.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
}
