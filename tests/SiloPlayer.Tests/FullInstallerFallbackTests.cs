using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class FullInstallerFallbackTests
{
    [Fact]
    public async Task FullMigrationBlocksClientStartupWithoutDeadlockingInstallerInitialization()
    {
        var exe = Path.Combine(Path.GetTempPath(), "silo-full-gate-" + Guid.NewGuid().ToString("N"), "SiloPlayer.exe");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = Task.Run(() =>
        {
            using var mutex = new Mutex(true, AppUpdateInstallGate.FullInstallerNameFor(exe));
            entered.TrySetResult();
            try { if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Synthetic gate was not released."); }
            finally { mutex.ReleaseMutex(); }
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.True(AppUpdateInstallGate.InstallationInProgress(exe));
            var canInitialize = await Task.Run(() =>
            {
                using var mutation = new Mutex(false, AppUpdateInstallGate.NameFor(exe));
                var held = mutation.WaitOne(0); if (held) mutation.ReleaseMutex(); return held;
            });
            Assert.True(canInitialize); // Inno's initializer uses the existing Protocol1 mutation gate.
        }
        finally { release.Set(); await owner; }
        Assert.False(AppUpdateInstallGate.InstallationInProgress(exe));
    }

    [Fact]
    public async Task HelperResolvesVersionedSourceToRegisteredRootNewClientAndStableRestartLauncher()
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-full-paths-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var helper = FindHelper(); var script = Path.Combine(root, "paths.ps1");
        await File.WriteAllTextAsync(script, $$"""
            $ErrorActionPreference='Stop';$tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(helper)}},[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'Helper has parsing errors'}
            foreach($name in @('Assert-OrdinaryPath','Get-UpdateInstallationPaths')){
                $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                if(!$function){throw ('Missing full migration boundary '+$name)}
                . ([scriptblock]::Create($function.Extent.Text))
            }
            $install='C:\Synthetic Silo'
            $valid=Get-UpdateInstallationPaths 'C:\Synthetic Silo\versions\1.2.517.2\SiloPlayer.exe' $install '1.2.517.12'
            $rejected=0
            foreach($case in @(
                @('C:\Other\versions\1.2.517.2\SiloPlayer.exe','1.2.517.12'),
                @('C:\Synthetic Silo\versions\1.2.517.2\..\SiloPlayer.exe','1.2.517.12'),
                @('C:\Synthetic Silo\versions\1.2.517.2\SiloPlayer.exe','1.2.517.1'),
                @('C:\Synthetic Silo\versions\1.2.517.2\SiloPlayer.exe','2.0.1.0'))){
                try{$null=Get-UpdateInstallationPaths $case[0] $install $case[1]}catch{$rejected++}
            }
            @{root=$valid.Root;target=$valid.Target;restart=$valid.Restart;versioned=$valid.Versioned;rejected=$rejected}|ConvertTo-Json -Compress
            """);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); var text = await output;
            Assert.True(process.ExitCode == 0, text + await error);
            using var result = JsonDocument.Parse(text); var data = result.RootElement;
            Assert.Equal(@"C:\Synthetic Silo", data.GetProperty("root").GetString());
            Assert.Equal(@"C:\Synthetic Silo\versions\1.2.517.12\SiloPlayer.exe", data.GetProperty("target").GetString());
            Assert.Equal(@"C:\Synthetic Silo\SiloPlayer.exe", data.GetProperty("restart").GetString());
            Assert.True(data.GetProperty("versioned").GetBoolean()); Assert.Equal(4, data.GetProperty("rejected").GetInt32());
        }
        finally { Directory.Delete(root, true); }
    }

    private static string FindHelper()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "SiloPlayer", "Updates", "UpdateOnExit.ps1"); if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Update helper source not found.");
    }

    [Theory]
    [InlineData("1.2.516.0", false)]
    [InlineData("1.2.517.0", true)]
    [InlineData("1.2.517.12", true)]
    [InlineData(null, false)]
    public async Task VersionedFullMigrationRequiresARootLauncherThatChecksTheFullUpdateGate(string? launcherVersion, bool supported)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "silo-full-launcher-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(sandbox, "installation"); var cache = Path.Combine(sandbox, "cache");
        var packageDirectory = Path.Combine(cache, "1.2.517.12"); Directory.CreateDirectory(packageDirectory); Directory.CreateDirectory(install);
        var package = Path.Combine(packageDirectory, "SiloInstaller-Windows-x64.exe");
        var bytes = "synthetic package; setup must never execute"u8.ToArray();
        await File.WriteAllBytesAsync(package, bytes);
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = Path.Combine(sandbox, "launcher-check.ps1");
        var stamp = launcherVersion is null ? "" : $"[assembly: System.Reflection.AssemblyFileVersion(\"{launcherVersion}\")]";
        await File.WriteAllTextAsync(script, $$"""
            $ErrorActionPreference='Stop'
            # Only the PE version is relevant to the conservative deferral policy.
            # This synthetic launcher and the dummy setup are never executed.
            Add-Type -TypeDefinition '{{stamp}} public static class SyntheticLauncher { public static void Main() {} }' -OutputAssembly {{Quote(Path.Combine(install, "SiloPlayer.exe"))}} -OutputType ConsoleApplication
            & {{Quote(FindHelper())}} -Version '1.2.517.12' -Sha256 '{{Convert.ToHexString(SHA256.HashData(bytes))}}' -ExpectedSize {{bytes.Length}} -AppPath {{Quote(Path.Combine(install, "versions", "1.2.517.2", "SiloPlayer.exe"))}} -InstallRoot {{Quote(install)}} -VerifyOnly -ValidateExitGate -ValidationRoot {{Quote(cache)}}
            exit $LASTEXITCODE
            """);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); var text = await output;
            Assert.True(process.ExitCode == (supported ? 0 : 1), text + await errors);
            using var json = JsonDocument.Parse(text); var result = json.RootElement;
            Assert.Equal(supported ? "verified-gate" : "deferred", result.GetProperty("state").GetString());
            if (!supported) Assert.Contains("run the full installer manually", result.GetProperty("message").GetString()!);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(package));
            Assert.False(Directory.Exists(Path.Combine(install, ".silo-update")));
            Assert.False(File.Exists(Path.Combine(cache, "last-result.json")));
        }
        finally { Directory.Delete(sandbox, true); }
    }

    [Fact]
    public async Task ActualHelperVerifiesAVersionedFallbackAndUsesTheDistinctRootGateWithoutSetup()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "silo-full-verify-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(sandbox, "installation"); var cache = Path.Combine(sandbox, "cache");
        var packageDirectory = Path.Combine(cache, "1.2.517.12"); Directory.CreateDirectory(packageDirectory); Directory.CreateDirectory(install);
        var bytes = "dummy non-executable package; VerifyOnly cannot execute setup"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(packageDirectory, "SiloInstaller-Windows-x64.exe"), bytes);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", FindHelper(),
            "-Version", "1.2.517.12", "-Sha256", Convert.ToHexString(SHA256.HashData(bytes)), "-ExpectedSize", bytes.Length.ToString(),
            "-AppPath", Path.Combine(install, "versions", "1.2.517.2", "SiloPlayer.exe"), "-InstallRoot", install,
            "-VerifyOnly", "-ValidationRoot", cache }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); var text = await output;
            Assert.True(process.ExitCode == 0, text + await errors);
            using var json = JsonDocument.Parse(text); Assert.Equal("verified", json.RootElement.GetProperty("state").GetString());
            Assert.Equal(AppUpdateInstallGate.FullInstallerNameFor(Path.Combine(install, "SiloPlayer.exe")), json.RootElement.GetProperty("gate").GetString());
            Assert.False(File.Exists(Path.Combine(cache, "last-result.json"))); Assert.True(File.Exists(Path.Combine(packageDirectory, "SiloInstaller-Windows-x64.exe")));
        }
        finally { Directory.Delete(sandbox, true); }
    }
}
