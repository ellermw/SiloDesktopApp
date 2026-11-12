using System.Text.RegularExpressions;

namespace SiloPlayer.Tests;

public sealed class NativeApiSourceGuardTests
{
    [Fact]
    public void ProductionSourceKeepsRetiredApiPathsInsideExplicitCompatibilityUtilities()
    {
        // These utilities recognize old saved server suffixes and delivery
        // URLs. They never authorize new requests to a retired API route.
        var compatibilityFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SiloPlayer.Core/Services/PlaybackDeliveryUrl.cs",
            "SiloPlayer.Core/Services/ServerUrlIdentity.cs",
            "SiloPlayer.Core/Services/ServerWebUiUri.cs",
        };
        var codeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".xaml", ".lua", ".js", ".mjs", ".html",
        };
        var generatedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "vendor", "node_modules",
        };
        var sourceRoot = Path.Combine(TestRepository.Root, "src");
        var retiredPath = new Regex(@"/api/v1\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            if (!codeExtensions.Contains(Path.GetExtension(file))
                || relative.Split('/').Any(generatedDirectories.Contains)
                || compatibilityFiles.Contains(relative))
                continue;
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (retiredPath.IsMatch(line)) violations.Add($"{relative}:{lineNumber}");
            }
        }
        Assert.True(violations.Count == 0,
            "Retired native API references outside compatibility utilities: " + string.Join(", ", violations));
    }
}
