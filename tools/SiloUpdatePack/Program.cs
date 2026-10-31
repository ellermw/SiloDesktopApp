using SiloPlayer.Core.Services.Updates;
using SiloPlayer.Core.Models.Updates;

try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 == args.Length || args[i] is not ("--client" or "--bootstrap" or "--output" or "--version" or "--release-version") || !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Expected --client PATH [--bootstrap PATH] --output FRESH_DIR --version M.m.P.R [--release-version M.m.P[-beta.N]].");
    }
    if (!options.TryGetValue("--client", out var client) || !options.TryGetValue("--output", out var output) ||
        !options.TryGetValue("--version", out var value) || !Version.TryParse(value, out var version) || version.Revision < 0 || version.ToString(4) != value)
        throw new ArgumentException("Expected --client PATH [--bootstrap PATH] --output FRESH_DIR --version 1.2.N.0.");
    var semantic = options.TryGetValue("--release-version", out var text) ? ReleaseVersion.Parse(text) : null;
    if (semantic is not null && semantic.GetWindowsVersion() != version) throw new InvalidDataException("Semantic and numeric package versions disagree.");
    var manifest = semantic is null
        ? await UpdatePackBuilder.BuildAsync(client, options.GetValueOrDefault("--bootstrap"), output, version)
        : await UpdatePackBuilder.BuildAsync(client, options.GetValueOrDefault("--bootstrap"), output, semantic);
    Console.WriteLine($"Created {manifest.Version}: {manifest.Files.Count} files, {manifest.PackSize} packed bytes, SHA256 {manifest.PackSha256}.");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message); return 1;
}
