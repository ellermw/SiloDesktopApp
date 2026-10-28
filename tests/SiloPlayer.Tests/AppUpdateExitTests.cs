using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class AppUpdateExitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitHelperJournalReplacesStatusInWindowsPowerShell(bool scheduledJournalExists)
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-update-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var journal = Path.Combine(root, "last-result.json");
        if (scheduledJournalExists) await File.WriteAllTextAsync(journal, """{"state":"scheduled"}""");
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = Path.Combine(root, "journal-check.ps1");
        await File.WriteAllTextAsync(script, $$"""
            $ErrorActionPreference='Stop'
            $root={{Quote(root)}}; $Version='1.2.337.0'; $VerifyOnly=$false
            $tokens=$null; $errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(FindScript())}},[ref]$tokens,[ref]$errors)
            foreach($name in @('Assert-OrdinaryPath','Write-Result')) {
                $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                if(-not $function){throw 'Missing journal boundary'}
                . ([scriptblock]::Create($function.Extent.Text))
            }
            Write-Result 'waiting' 'Waiting for the synthetic parent.'
            Write-Result 'installing' 'Synthetic status only; setup is never launched.'
            Write-Result 'installed' 'Synthetic journal completion.'
            """);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(process.ExitCode == 0, await output + await error);
            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(journal));
            Assert.Equal("installed", final.RootElement.GetProperty("state").GetString());
            var states = File.ReadLines(Path.Combine(root, "install.log")).Select(line =>
            { using var json = JsonDocument.Parse(line); return json.RootElement.GetProperty("state").GetString(); }).ToArray();
            Assert.Equal(new[] { "waiting", "installing", "installed" }, states);
            Assert.Empty(Directory.EnumerateFiles(root, "result-*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InstallGateIsPerInstallAndStableAcrossCase()
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-gate-review");
        var name = AppUpdateInstallGate.NameFor(Path.Combine(root, "SiloPlayer.exe"));
        Assert.StartsWith(@"Global\SiloDesktopUpdate-", name);
        Assert.Equal(name, AppUpdateInstallGate.NameFor(Path.Combine(root.ToUpperInvariant(), "SiloPlayer.exe")));
        Assert.NotEqual(name, AppUpdateInstallGate.NameFor(Path.Combine(root, "other", "SiloPlayer.exe")));
    }

    [Fact]
    public async Task StartupDetectsAnInstallGateWithoutTerminatingItsOwner()
    {
        var exe = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SiloPlayer.exe");
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEvent(false);
        var owner = Task.Run(() =>
        {
            using var mutex = new Mutex(true, AppUpdateInstallGate.NameFor(exe));
            acquired.SetResult(); release.WaitOne(); mutex.ReleaseMutex();
        });
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try { Assert.True(AppUpdateInstallGate.InstallationInProgress(exe)); Assert.False(owner.IsCompleted); }
        finally { release.Set(); await owner; }
        Assert.False(AppUpdateInstallGate.InstallationInProgress(exe));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ExitHelperVerificationAcceptsOnlyTheExpectedPackageWithoutLaunchingAnything(bool corrupt, bool validateGate)
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-helper-review-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "1.2.335.0"); Directory.CreateDirectory(folder);
        var bytes = "Synthetic package; never executable"u8.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var path = Path.Combine(folder, "SiloInstaller-Windows-x64.exe");
        await File.WriteAllBytesAsync(path, corrupt ? new byte[bytes.Length] : bytes);
        var script = FindScript();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-Version", "1.2.335.0", "-Sha256", digest, "-ExpectedSize", bytes.Length.ToString(),
            "-AppPath", Path.Combine(root, "app", "SiloPlayer.exe"), "-VerifyOnly", "-ValidationRoot", root }) start.ArgumentList.Add(arg);
        if (validateGate) start.ArgumentList.Add("-ValidateExitGate");
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var text = await output; var stderr = await errors;
            Assert.True(process.ExitCode == (corrupt ? 1 : 0), text + stderr);
            using var result = JsonDocument.Parse(text.Trim());
            Assert.Equal(corrupt ? "failed" : validateGate ? "verified-gate" : "verified", result.RootElement.GetProperty("state").GetString());
            if (!corrupt) Assert.Equal(AppUpdateInstallGate.NameFor(Path.Combine(root, "app", "SiloPlayer.exe")), result.RootElement.GetProperty("gate").GetString());
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "SiloPlayer", "Updates", "UpdateOnExit.ps1");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Update helper source not found.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongRunningSetupRetainsItsGateUntilTheProcessActuallyExits(bool journalFails)
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-long-setup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var app = Path.Combine(root, "SiloPlayer.exe"); var marker = Path.Combine(root, "still-running");
        var script = Path.Combine(root, "gate-check.ps1");
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        await File.WriteAllTextAsync(script, $$"""
            $ErrorActionPreference='Stop'
            $tokens=$null; $errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(FindScript())}},[ref]$tokens,[ref]$errors)
            $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Wait-UpdateSetup'},$true)
            if(-not $function){throw 'Missing setup wait boundary'}
            . ([scriptblock]::Create($function.Extent.Text))
            $gate=[Threading.Mutex]::new($true,{{Quote(AppUpdateInstallGate.NameFor(app))}})
            try {
                $setup=Start-Process (Join-Path $PSHOME 'powershell.exe') -WindowStyle Hidden -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 2') -PassThru
                try {
                    $long=$false; $warningFailed=$false
                    try {$long=Wait-UpdateSetup -Process $setup -WarningAfterMilliseconds 10 -OnLongRunning {
                        [IO.File]::WriteAllText({{Quote(marker)}},'waiting')
                        if({{(journalFails ? "$true" : "$false")}}){throw 'Synthetic journal failure'}
                    } } catch {if(-not {{(journalFails ? "$true" : "$false")}}){throw};$warningFailed=$true}
                    if((-not $long -and -not $warningFailed) -or -not $setup.HasExited){throw 'Setup wait ended early'}
                }finally{$setup.Dispose()}
            }finally{$gate.ReleaseMutex();$gate.Dispose()}
            """);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!File.Exists(marker) && !process.HasExited && DateTime.UtcNow < until) await Task.Delay(20);
            Assert.True(File.Exists(marker), process.HasExited ? await process.StandardError.ReadToEndAsync() : "Setup timeout marker missing");
            Assert.True(AppUpdateInstallGate.InstallationInProgress(app));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            Assert.False(AppUpdateInstallGate.InstallationInProgress(app));
        }
        finally { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Directory.Delete(root, true); }
    }
}
