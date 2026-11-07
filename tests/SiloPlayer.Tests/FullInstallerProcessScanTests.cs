using System.Diagnostics;
using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class FullInstallerProcessScanTests
{
    [Theory]
    [InlineData("null", true, "clear")]
    [InlineData("null", false, "unverifiable")]
    [InlineData("blank", false, "unverifiable")]
    [InlineData("read-error", true, "clear")]
    [InlineData("read-error", false, "unverifiable")]
    [InlineData("exit-error", false, "unverifiable")]
    [InlineData("other", false, "clear")]
    [InlineData("installed", false, "open")]
    public async Task ActualExitScanClassifiesUnavailableProcessImagesWithoutLosingItsSafetyGate(
        string image, bool exited, string expected)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "silo-exit-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var helper = Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Updates", "UpdateOnExit.ps1");
        var script = Path.Combine(sandbox, "scan.ps1");
        await File.WriteAllTextAsync(script, $$$"""
            $ErrorActionPreference='Stop';Set-StrictMode -Version 2
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{{Quote(helper)}}},[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'Helper parse failed.'}
            $enumeration="[Diagnostics.Process]::GetProcessesByName('SiloPlayer')"
            $scan=$ast.Find({param($n) $n -is [Management.Automation.Language.ForEachStatementAst] -and $n.Extent.Text.Contains($enumeration)},$true)
            if(!$scan){throw 'Actual exit scan was not found.'}
            # Replace only the input enumeration. Execute the actual policy,
            # including all catches/finally, with no real process or setup action.
            $body=$scan.Extent.Text
            if($body.IndexOf($enumeration) -ne $body.LastIndexOf($enumeration)){throw 'Ambiguous scan boundary.'}
            $body=$body.Replace($enumeration,'$fixtureProcesses')
            $appDirectory='C:\Synthetic Silo';$app=$appDirectory+'\versions\1.2.517.2\SiloPlayer.exe'
            $paths=@{Versioned=$true};$failureState='failed';$script:disposed=0
            $image={{{Quote(image)}}}
            $didExit={{{(exited ? "$true" : "$false")}}}
            $file=if($image -eq 'null' -or $image -eq 'exit-error'){$null}elseif($image -eq 'blank'){''}elseif($image -eq 'installed'){$app}else{'C:\Other Synthetic Silo\SiloPlayer.exe'}
            $process=[pscustomobject]@{MainModule=[pscustomobject]@{FileName=$file};HasExited=$didExit}
            if($image -eq 'read-error'){$process|Add-Member -Force -MemberType ScriptProperty -Name MainModule -Value {throw 'Synthetic module unavailable.'}}
            if($image -eq 'exit-error'){$process|Add-Member -Force -MemberType ScriptProperty -Name HasExited -Value {throw 'Synthetic exit state unavailable.'}}
            $process|Add-Member -MemberType ScriptMethod -Name Dispose -Value {[void]($script:disposed++)}
            $fixtureProcesses=@($process);$state='clear';$message=''
            try{. ([scriptblock]::Create($body))}catch{$state=$failureState;$message=$_.Exception.Message}
            @{state=$state;message=$message;disposed=$script:disposed}|ConvertTo-Json -Compress
            """);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var output = await stdout;
            Assert.True(process.ExitCode == 0, output + await stderr);
            using var json = JsonDocument.Parse(output);
            var result = json.RootElement;
            Assert.Equal(expected == "clear" ? "clear" : "deferred", result.GetProperty("state").GetString());
            Assert.Equal(1, result.GetProperty("disposed").GetInt32());
            Assert.Equal(expected switch
            {
                "unverifiable" => "Another Silo process cannot be checked. Update postponed.",
                "open" => "Another Silo instance is open. Update postponed.",
                _ => ""
            }, result.GetProperty("message").GetString());
        }
        finally { Directory.Delete(sandbox, true); }
    }
}
