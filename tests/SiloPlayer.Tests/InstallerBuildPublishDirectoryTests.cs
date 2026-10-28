using System.Diagnostics;
using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class InstallerBuildPublishDirectoryTests
{
    [Fact]
    public async Task PublishAliasesProtectedPathsAndConfigurationTraversalAreRefused()
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-publish-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "real"));
        var script = Path.Combine(root, "probe.ps1");
        File.WriteAllText(script, $$"""
            $ErrorActionPreference='Stop'
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(FindBuildScript())}},[ref]$tokens,[ref]$errors)
            foreach($name in @('Assert-NoLinkedPublishPath','Get-DedicatedPublishDirectory')) {
                $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                . ([scriptblock]::Create($function.Extent.Text))
            }
            $root={{Quote(root)}}
            New-Item -ItemType Junction -Path "$root\alias" -Value "$root\real"|Out-Null
            $results=@()
            foreach($path in @("$root\alias","$root\alias\new",(Join-Path $env:ProgramFiles 'SiloNeverCreatedOutput'))) {
                try {$null=Get-DedicatedPublishDirectory -Path $path -RepositoryRoot "$root\repo";$results+=$false} catch {$results+=$true}
            }
            $binder=Join-Path $root 'bind.ps1'
            [IO.File]::WriteAllText($binder,$ast.ParamBlock.Extent.Text+"`n'accepted'")
            try {$null=& $binder -Configuration '..\..';$results+=$false} catch {$results+=$true}
            $results+=((& $binder -Configuration Release) -eq 'accepted')
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
            Assert.All(JsonSerializer.Deserialize<bool[]>(await output)!, Assert.True);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "real")));
        }
        finally
        {
            var link = Path.Combine(root, "alias");
            if (Directory.Exists(link)) Directory.Delete(link); // Remove only the synthetic junction, without recursion.
            Directory.Delete(root, true);
        }
    }

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
        File.WriteAllText(Path.Combine(root, "previous", "preserved-evidence.txt"), "kept");
        Directory.CreateDirectory(Path.Combine(root, "fake-marker", "SiloPlayer.exe"));
        File.WriteAllText(Path.Combine(root, "fake-marker", "kept.txt"), "kept");
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        var cases = new (string Path, bool Allowed)[]
        {
            (Path.GetPathRoot(root)!, false),
            (root, false),
            (repository, false),
            (repository + Path.DirectorySeparatorChar, false),
            (Path.Combine(repository, "src"), false),
            (Path.Combine(root, "foreign"), false),
            (Path.Combine(root, "previous"), false),
            (Path.Combine(root, "fake-marker"), false),
            (Path.Combine(root, "empty"), true),
            (Path.Combine(root, "new"), true),
            (Path.Combine(repository, "installer", "publish"), true),
        };
        var script = Path.Combine(root, "probe.ps1");
        var calls = string.Join("\n", cases.Select(c =>
            $"try {{ $null = Get-DedicatedPublishDirectory -Path {Quote(c.Path)} -RepositoryRoot {Quote(repository)}; $results += $true }} catch {{ $results += $false }}"));
        File.WriteAllText(script, $$"""
            $ErrorActionPreference='Stop'
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(FindBuildScript())}},[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'build.ps1 has parsing errors'}
            foreach($name in @('Assert-NoLinkedPublishPath','Get-DedicatedPublishDirectory')) {
                $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                if($function){. ([scriptblock]::Create($function.Extent.Text))}
            }
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
            Assert.True(File.Exists(Path.Combine(root, "previous", "preserved-evidence.txt")));
            Assert.True(File.Exists(Path.Combine(root, "fake-marker", "kept.txt")));
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
