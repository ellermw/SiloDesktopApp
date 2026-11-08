using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class FullInstallerInvocationTests
{
    [Theory]
    [InlineData("versioned", "independent release authority")]
    [InlineData("versioned-no-root", "independent release authority")]
    [InlineData("forced-legacy", "active version-local client")]
    [InlineData("forced-root", "not the registered machine installation")]
    [InlineData("legacy", "independent release authority")]
    [InlineData("hkcu-only", "registered machine installation")]
    [InlineData("hkcu-only-legacy", "registered machine installation")]
    [InlineData("unprotected", "protected machine installation")]
    [InlineData("unprotected-legacy", "protected machine installation")]
    public async Task DirectHelperInvocationCannotUseCallerHashOrLegacyArgumentsToAuthorizeSetup(string scenario, string expectedError)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "silo-full-invocation-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(sandbox, "installation"); var cache = Path.Combine(sandbox, "cache");
        var client = Path.Combine(install, "versions", "1.2.517.2", "SiloPlayer.exe");
        const string numeric = "1.2.517.12";
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.Combine(cache, numeric));
        var versioned = scenario is not ("legacy" or "hkcu-only-legacy" or "unprotected-legacy");
        if (versioned)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(client)!);
            await File.WriteAllTextAsync(client, "Synthetic client; never executed");
            Directory.CreateDirectory(Path.Combine(install, ".silo-update"));
            await File.WriteAllTextAsync(Path.Combine(install, ".silo-update", "state.json"), """{"ActiveVersion":"1.2.517.2"}""");
        }
        var bytes = "Caller-selected package; never executable"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(cache, numeric, "SiloInstaller-Windows-x64.exe"), bytes);
        var metadata = UpdateChannelTests.Release("1.2.518-beta.12");
        // Match the caller's size but not its digest: only independent authority
        // may authorize the package, even though the caller's own hash is correct.
        metadata["assets"]![0]!["size"] = bytes.Length;
        metadata["assets"]![0]!["digest"] = "sha256:" + new string('a', 64);
        var source = scenario == "forced-legacy" || !versioned ? Path.Combine(install, "SiloPlayer.exe") : client;
        var requestedRoot = scenario == "forced-root" ? Path.GetDirectoryName(client)! : install;
        var rootArgument = scenario is "forced-legacy" or "versioned-no-root" ? "" : " -InstallRoot " + Quote(requestedRoot);
        var registration = scenario.StartsWith("hkcu-only", StringComparison.Ordinal) ? "''" : Quote(install);
        var permissions = scenario.StartsWith("unprotected", StringComparison.Ordinal) ? "throw 'A protected machine installation is required.'" : "";
        var copy = Path.Combine(sandbox, "helper.ps1");
        try
        {
            var output = await RunAsync(sandbox, $$"""
                $ErrorActionPreference='Stop'
                $tokens=$null;$errors=$null
                $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(Helper)}},[ref]$tokens,[ref]$errors)
                if($errors.Count){throw 'Helper has parsing errors'}
                $text=[IO.File]::ReadAllText({{Quote(Helper)}})
                # Replace only machine/transport boundaries in this isolated copy.
                # Run the actual helper entry point, including mode selection,
                # package locking/hash checks and the pre-elevation authority gate.
                $mocks=@{
                    'Get-UpdateRegisteredRoot'={{Quote("function Get-UpdateRegisteredRoot { return " + registration + " }")}}
                    'Assert-UpdateProtectedInstallation'={{Quote("function Assert-UpdateProtectedInstallation([string]$Installation) { " + permissions + " }")}}
                    'Get-UpdateReleaseMetadata'={{Quote("function Get-UpdateReleaseMetadata([string]$Tag) { return (" + Quote(metadata.ToJsonString()) + " | ConvertFrom-Json) }")}}
                    'New-UpdateSetupStartInfo'="function New-UpdateSetupStartInfo { throw 'Unexpected elevation attempt' }"
                }
                foreach($name in $mocks.Keys){
                    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                    if(!$function){throw ('Missing helper boundary '+$name)}
                    $text=$text.Replace($function.Extent.Text,$mocks[$name])
                }
                $cacheExpression="Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SiloPlayer\updates'"
                if(!$text.Contains($cacheExpression)){throw 'Missing cache boundary'}
                $text=$text.Replace($cacheExpression,{{Quote(Quote(cache))}})
                [IO.File]::WriteAllText({{Quote(copy)}},$text)
                Add-Type -TypeDefinition '[assembly: System.Reflection.AssemblyTitle("Silo Desktop Player launcher; FullUpdateGate=1")] public static class SyntheticLauncher { public static void Main() {} }' -OutputAssembly {{Quote(Path.Combine(install, "SiloPlayer.exe"))}} -OutputType ConsoleApplication
                & {{Quote(copy)}} -Version '{{numeric}}' -Sha256 '{{Convert.ToHexString(SHA256.HashData(bytes))}}' -ExpectedSize {{bytes.Length}} -AppPath {{Quote(source)}}{{rootArgument}} -ReleaseTag 'v1.2.518-beta.12' -InstallerUrl 'https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.518-beta.12/SiloInstaller-Windows-x64.exe'
                exit $LASTEXITCODE
                """, 1);
            using var result = JsonDocument.Parse(output);
            Assert.Equal("failed", result.RootElement.GetProperty("state").GetString());
            Assert.Contains(expectedError, result.RootElement.GetProperty("message").GetString()!);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(cache, numeric, "SiloInstaller-Windows-x64.exe")));
        }
        finally { Directory.Delete(sandbox, true); }
    }

    [Fact]
    public async Task HelperAclAndPeChecksRejectWritableInstallationsAndDisagreeingIdentities()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "silo-full-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            var output = await RunAsync(sandbox, $$"""
                $ErrorActionPreference='Stop';Set-StrictMode -Version 2
                $tokens=$null;$errors=$null
                $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(Helper)}},[ref]$tokens,[ref]$errors)
                if($errors.Count){throw 'Helper has parsing errors'}
                foreach($name in @('Test-UpdatePrivilegedAcl','Confirm-UpdateInstallerIdentity')){
                    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                    if(!$function){throw ('Missing helper boundary '+$name)}
                    . ([scriptblock]::Create($function.Extent.Text))
                }
                $acl=[Security.AccessControl.DirectorySecurity]::new()
                $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;BU)')
                if(!(Test-UpdatePrivilegedAcl $acl)){throw 'Protected ACL rejected'}
                $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FW;;;BU)')
                if(Test-UpdatePrivilegedAcl $acl){throw 'Writable ACL accepted'}
                if(!(Test-UpdatePrivilegedAcl $acl $true)){throw 'Ancestor create-only rights rejected'}
                $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;SD;;;BU)')
                if(Test-UpdatePrivilegedAcl $acl $true){throw 'Replaceable ancestor accepted'}
                $acl.SetSecurityDescriptorSddlForm('O:BUG:BAD:(A;;FA;;;SY)(A;;FA;;;BA)')
                if(Test-UpdatePrivilegedAcl $acl){throw 'Unprivileged owner accepted'}
                $rejected=0
                foreach($release in @(
                    @('v1.2.518','1.2.518.0','1.2.518'),
                    @('v1.2.518.0','1.2.518.0','1.2.518.0'),
                    @('v1.2.518-beta.12','1.2.517.12','1.2.518-beta.12'))){
                    $v=[version]$release[1]
                    $good=@{FileMajorPart=$v.Major;FileMinorPart=$v.Minor;FileBuildPart=$v.Build;FilePrivatePart=$v.Revision;
                        ProductMajorPart=$v.Major;ProductMinorPart=$v.Minor;ProductBuildPart=$v.Build;ProductPrivatePart=$v.Revision;ProductVersion=$release[2]}
                    Confirm-UpdateInstallerIdentity $release[0] $release[1] ([pscustomobject]$good)
                    # Inno Setup pads version text with spaces to its fixed resource width (50 for ProductVersion).
                    $padded=$good.Clone();$padded.ProductVersion=$release[2].PadRight(50)
                    Confirm-UpdateInstallerIdentity $release[0] $release[1] ([pscustomobject]$padded)
                    foreach($fault in @('FileBuildPart','ProductPrivatePart','ProductVersion','PaddedProductVersion','ProductVersionSuffix','ProductVersionTab')){
                        $bad=$good.Clone()
                        switch($fault){
                            'ProductVersion'{$bad.ProductVersion='1.2.999'}
                            'PaddedProductVersion'{$bad.ProductVersion='1.2.999'.PadRight(50)}
                            'ProductVersionSuffix'{$bad.ProductVersion=$release[2]+' x'}
                            'ProductVersionTab'{$bad.ProductVersion=$release[2]+"`t"}
                            default{$bad[$fault]++}
                        }
                        $failed=$false
                        try{Confirm-UpdateInstallerIdentity $release[0] $release[1] ([pscustomobject]$bad)}catch{$failed=$true}
                        if(!$failed){throw ('Disagreeing PE identity accepted: '+$fault)}
                        $rejected++
                    }
                }
                @{rejected=$rejected}|ConvertTo-Json -Compress
                """, 0);
            using var result = JsonDocument.Parse(output);
            Assert.Equal(18, result.RootElement.GetProperty("rejected").GetInt32());
        }
        finally { Directory.Delete(sandbox, true); }
    }

    [Fact]
    public async Task BuildRepairsPaddedProductVersionSoShippedClientsAcceptTheInstaller()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "silo-version-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var setup = Path.Combine(sandbox, "setup.exe");
        try
        {
            var output = await RunAsync(sandbox, $$"""
                $ErrorActionPreference='Stop'
                # Inno pads ProductVersion with spaces to its fixed resource width.
                $padded='1.2.811-beta.1'.PadRight(50)
                Add-Type -TypeDefinition ('[assembly: System.Reflection.AssemblyFileVersion("1.2.810.1")] [assembly: System.Reflection.AssemblyInformationalVersion("'+$padded+'")] public static class SyntheticSetup { public static void Main() {} }') -OutputAssembly {{Quote(setup)}} -OutputType ConsoleApplication
                $before=[Diagnostics.FileVersionInfo]::GetVersionInfo({{Quote(setup)}})
                if($before.ProductVersion -cne $padded){throw ('Synthetic padding missing: ['+$before.ProductVersion+']')}
                $size=(Get-Item -LiteralPath {{Quote(setup)}}).Length
                & {{Quote(Repair)}} -Path {{Quote(setup)}} -ProductVersion '1.2.811-beta.1'
                & {{Quote(Repair)}} -Path {{Quote(setup)}} -ProductVersion '1.2.811-beta.1'
                $after=[Diagnostics.FileVersionInfo]::GetVersionInfo({{Quote(setup)}})
                $wrong=$false;try{& {{Quote(Repair)}} -Path {{Quote(setup)}} -ProductVersion '1.2.812-beta.1'}catch{$wrong=$true}
                # Clients up to 1.2.709 compare the text exactly (no trimming).
                @{product=$after.ProductVersion;legacyAccepts=($after.ProductVersion -ceq '1.2.811-beta.1');
                  sameSize=((Get-Item -LiteralPath {{Quote(setup)}}).Length -eq $size);wrongRejected=$wrong;file=$after.FileVersion}|ConvertTo-Json -Compress
                """, 0);
            using var result = JsonDocument.Parse(output);
            Assert.Equal("1.2.811-beta.1", result.RootElement.GetProperty("product").GetString());
            Assert.True(result.RootElement.GetProperty("legacyAccepts").GetBoolean());
            Assert.True(result.RootElement.GetProperty("sameSize").GetBoolean());
            Assert.True(result.RootElement.GetProperty("wrongRejected").GetBoolean());
            Assert.Equal("1.2.810.1", result.RootElement.GetProperty("file").GetString());
        }
        finally { Directory.Delete(sandbox, true); }
    }

    private static string Repair => Path.Combine(TestRepository.Root, "installer", "Repair-InstallerVersionText.ps1");
    private static string Helper => Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Updates", "UpdateOnExit.ps1");
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static async Task<string> RunAsync(string sandbox, string text, int expectedExitCode)
    {
        var script = Path.Combine(sandbox, "invocation.ps1");
        await File.WriteAllTextAsync(script, text);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var result = await output;
        Assert.True(process.ExitCode == expectedExitCode, result + await error);
        return result;
    }
}
