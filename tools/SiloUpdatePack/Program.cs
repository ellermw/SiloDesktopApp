using SiloPlayer.Core.Services.Updates;

try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 == args.Length || args[i] is not ("--client" or "--bootstrap" or "--output" or "--version") || !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Expected --client PATH [--bootstrap PATH] --output FRESH_DIR --version 1.2.N.0.");
    }
    if (!options.TryGetValue("--client", out var client) || !options.TryGetValue("--output", out var output) ||
        !options.TryGetValue("--version", out var value) || !Version.TryParse(value, out var version) || version.Revision < 0 || version.ToString(4) != value)
        throw new ArgumentException("Expected --client PATH [--bootstrap PATH] --output FRESH_DIR --version 1.2.N.0.");
    var manifest = await UpdatePackBuilder.BuildAsync(client, options.GetValueOrDefault("--bootstrap"), output, version);
    Console.WriteLine($"Created {manifest.Version}: {manifest.Files.Count} files, {manifest.PackSize} packed bytes, SHA256 {manifest.PackSha256}.");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message); return 1;
}
