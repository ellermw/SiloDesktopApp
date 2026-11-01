using System.Diagnostics;
using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class FullInstallerAuthorityTests
{
    [Theory]
    [InlineData("1.2.518", "1.2.518.0")]
    [InlineData("1.2.518-beta.12", "1.2.517.12")]
    [InlineData("1.2.518.0", "1.2.518.0")]
    public async Task HelperRequiresIndependentTagSizeDigestAndUrlAgreement(string release, string numeric)
    {
        var root = Path.Combine(Path.GetTempPath(), "silo-full-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var metadata = UpdateChannelTests.Release(release);
        var asset = metadata["assets"]![0]!;
        var helper = Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Updates", "UpdateOnExit.ps1");
        var script = Path.Combine(root, "authority.ps1");
        await File.WriteAllTextAsync(script, $$"""
            $ErrorActionPreference='Stop';Set-StrictMode -Version 2
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile({{Quote(helper)}},[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'Helper has parsing errors'}
            $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Confirm-UpdateReleaseAuthority'},$true)
            if(!$function){throw 'Missing independent full installer authority boundary'}
            . ([scriptblock]::Create($function.Extent.Text))
            $json={{Quote(metadata.ToJsonString())}}
            $tag={{Quote("v" + release)}};$numeric={{Quote(numeric)}}
            $digest={{Quote(asset["digest"]!.GetValue<string>()[7..])}}
            $size={{asset["size"]!.GetValue<int>()}};$url={{Quote(asset["browser_download_url"]!.GetValue<string>())}}
            $script:reads=0;$script:unavailable=$false
            # Only replace transport. Exercise the actual helper's validation with
            # independent synthetic metadata, without network, setup or elevation.
            function Get-UpdateReleaseMetadata([string]$Tag){
                $script:reads++
                if($script:unavailable){throw 'Synthetic authority unavailable'}
                return $script:metadata
            }
            $script:metadata=$json|ConvertFrom-Json
            Confirm-UpdateReleaseAuthority $tag $numeric $digest $size $url
            if($script:reads -ne 1){throw 'Full installer authority was not independently fetched'}
            $faults=@('tag','draft','prerelease','missing-flag','date','size','digest','url','duplicate','pending','missing','unavailable')
            $rejected=0
            foreach($fault in $faults){
                $script:metadata=$json|ConvertFrom-Json;$script:unavailable=$false
                switch($fault){
                    'tag' {$script:metadata.tag_name='v1.2.999'}
                    'draft' {$script:metadata.draft=$true}
                    'prerelease' {$script:metadata.prerelease=!$script:metadata.prerelease}
                    'missing-flag' {$script:metadata.PSObject.Properties.Remove('prerelease')}
                    'date' {$script:metadata.published_at='not a date'}
                    'size' {$script:metadata.assets[0].size=$size+1}
                    'digest' {$script:metadata.assets[0].digest='sha256:'+('b'*64)}
                    'url' {$script:metadata.assets[0].browser_download_url='https://evil.invalid/setup.exe'}
                    'duplicate' {$script:metadata.assets=@($script:metadata.assets[0],$script:metadata.assets[0])}
                    'pending' {$script:metadata.assets[0].state='starter'}
                    'missing' {$script:metadata.assets=@()}
                    'unavailable' {$script:unavailable=$true}
                }
                $failed=$false
                try{Confirm-UpdateReleaseAuthority $tag $numeric $digest $size $url}catch{$failed=$true}
                if(!$failed){throw ('Accepted manipulated full installer authority: '+$fault)}
                $rejected++
            }
            $script:unavailable=$false;$script:metadata=$json|ConvertFrom-Json
            foreach($arguments in @(
                @($tag,'1.2.999.0',$digest,$size,$url),
                @($tag.Substring(1),$numeric,$digest,$size,$url),
                @('v1.2.518-beta.0',$numeric,$digest,$size,$url),
                @('v1.2.518-beta.65535',$numeric,$digest,$size,$url),
                @($tag,$numeric,('b'*64),$size,$url),
                @($tag,$numeric,$digest,($size+1),$url),
                @($tag,$numeric,$digest,$size,'https://evil.invalid/setup.exe'))){
                $failed=$false
                try{Confirm-UpdateReleaseAuthority @arguments}catch{$failed=$true}
                if(!$failed){throw 'Accepted manipulated client handoff'}
                $rejected++
            }
            @{rejected=$rejected;reads=$script:reads}|ConvertTo-Json -Compress
            """);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var text = await output;
            Assert.True(process.ExitCode == 0, text + await error);
            using var result = JsonDocument.Parse(text);
            Assert.Equal(19, result.RootElement.GetProperty("rejected").GetInt32());
            Assert.True(result.RootElement.GetProperty("reads").GetInt32() >= 13);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RealVersionedHandoffsRequirePinnedBoundedAuthorityBeforeSetupCanStart()
    {
        var helper = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Updates", "UpdateOnExit.ps1"));
        var validation = helper.IndexOf("if(-not $VerifyOnly){Confirm-UpdateReleaseAuthority $ReleaseTag $Version $Sha256 $ExpectedSize $InstallerUrl}", StringComparison.Ordinal);
        Assert.True(validation > helper.IndexOf("if ($paths.Versioned)", StringComparison.Ordinal));
        Assert.True(validation < helper.IndexOf("$setup=[Diagnostics.Process]::Start($start)", StringComparison.Ordinal));
        Assert.Contains("https://api.github.com/repos/ellermw/SiloDesktopApp/releases/tags/", helper);
        var tls = helper.IndexOf("[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12", StringComparison.Ordinal);
        Assert.True(tls > helper.IndexOf("function Get-UpdateReleaseMetadata", StringComparison.Ordinal));
        Assert.True(tls < helper.IndexOf("$request=[Net.WebRequest]::CreateHttp($uri)", StringComparison.Ordinal));
        Assert.Contains("$request.AllowAutoRedirect=$false;$request.UseDefaultCredentials=$false;$request.Credentials=$null", helper);
        Assert.Contains("$request.Timeout=12000;$request.ReadWriteTimeout=12000", helper);
        Assert.Contains("$bytes.Length+$count -gt 524288", helper);
        var client = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "WindowsAppUpdateInstaller.cs"));
        Assert.Contains("\"-ReleaseTag\", release.Tag, \"-InstallerUrl\", release.InstallerUrl.AbsoluteUri", client);
    }
}
