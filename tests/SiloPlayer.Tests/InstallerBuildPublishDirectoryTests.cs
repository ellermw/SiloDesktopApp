using System.Diagnostics;
using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class InstallerBuildPublishDirectoryTests
{
    [Fact]
    public async Task PublishCleanupDeletesOnlyDedicatedOutputFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-publish-guard-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, "src"));
        File.WriteAllText(Path.Combine(repository, "src", "kept.cs"), "kept");
        Directory.CreateDirectory(Path.Combine(root, "foreign"));
        File.WriteAllText(Path.Combine(root, "foreign", "kept.txt"), "kept");
        Directory.CreateDirectory(Path.Combine(root, "previous"));
        File.WriteAllText(Path.Combine(root, "previous", "SiloPlayer.exe"), "");
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        var cases = new (string Path, bool Allowed)[]
        {
            (Path.GetPathRoot(root)!, false),
            (root, false),
            (repository, false),
            (repository + Path.DirectorySeparatorChar, false),
            (Path.Combine(repository, "src"), false),
            (Path.Combine(root, "foreign"), false),
            (Path.Combine(root, "previous"), true),
            (Path.Combine(root, "empty"), true),
            (Path.Combine(root, "new"), true),
            (Path.Combine(repository, "installer", "publish"), true),
        };
        var script = Path.Combine(root, "probe.ps1");
        var calls = string.Join("\n", cases.Select(c =>
            $"try {{ $null = Clear-DedicatedPublishDirectory -Path {Quote(c.Path)} -RepositoryRoot {Quote(repository)}; $results += $true }} catch {{ $results += $false }}"));
        File.WriteAllText(script, $$"""
            $ErrorActionPreference='Stop'
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(FindBuildScript())}},[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'build.ps1 has parsing errors'}
            $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Clear-DedicatedPublishDirectory'},$true)
            . ([scriptblock]::Create($function.Extent.Text))
            $results=@()
            {{calls}}
            ConvertTo-Json -Compress -InputObject $results
            """);
        try
        {
            var start = new ProcessStartInfo(PowerShellPath())
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(process.ExitCode == 0, await output + await error);
            var results = JsonSerializer.Deserialize<bool[]>(await output)!;
            Assert.Equal(cases.Length, results.Length);
            for (var i = 0; i < cases.Length; i++) Assert.True(cases[i].Allowed == results[i], cases[i].Path);
            Assert.True(File.Exists(Path.Combine(repository, "src", "kept.cs")));
            Assert.True(File.Exists(Path.Combine(root, "foreign", "kept.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, "previous")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string PowerShellPath() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
        : "pwsh";

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static string FindBuildScript()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "installer", "build.ps1"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "installer", "build.ps1");
    }
}
