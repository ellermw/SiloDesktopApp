using System.Diagnostics;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdateClientIdentityTests
{
    [Theory]
    [InlineData("1.2.518-beta.12", "1.2.517.12", "1.2.517.12", "1.2.518-beta.12+source", true)]
    [InlineData("1.2.518", "1.2.518.0", "1.2.518.0", "1.2.518", true)]
    [InlineData("1.2.518", "1.2.518.0", "1.2.518.0", "1.2.518.0", true)]
    [InlineData("1.2.518-beta.12", "1.2.517.12", "1.2.517.12", "1.2.518-beta.2", false)]
    [InlineData("1.2.518-beta.12", "1.2.517.12", "1.2.518.0", "1.2.518-beta.12", false)]
    [InlineData("1.2.518-beta.12", "1.2.518.0", "1.2.517.12", "1.2.518-beta.12", false)]
    [InlineData("1.2.518-beta.12", "1.2.518.0", "1.2.518.0", "1.2.518", false)]
    public async Task HashVerifiedClientWithMismatchedPeStampsCannotPassTheConfirmationGate(
        string target, string assemblyVersion, string fileVersion, string informational, bool valid)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "silo-client-identity-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(sandbox, "installation");
        var old = Path.Combine(root, "versions", "1.2.516.0");
        var client = Path.Combine(sandbox, "client");
        Directory.CreateDirectory(old); Directory.CreateDirectory(client);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(old, "SiloPlayer.exe"), "synthetic old client");
            await File.WriteAllTextAsync(Path.Combine(old, "SiloPlayer.dll"), "synthetic old assembly");
            var baseline = await UpdatePackBuilder.BuildAsync(old, null, Path.Combine(sandbox, "baseline"), new Version(1, 2, 516, 0));
            SideBySideInstallation.Initialize(root, baseline);
            await File.WriteAllTextAsync(Path.Combine(client, "SiloPlayer.exe"), "synthetic client; never executed");
            var script = Path.Combine(sandbox, "stamp.ps1");
            var outputAssembly = Path.Combine(client, "SiloPlayer.dll").Replace("'", "''");
            await File.WriteAllTextAsync(script, $$"""
                $ErrorActionPreference='Stop'
                Add-Type -TypeDefinition '[assembly: System.Reflection.AssemblyVersion("{{assemblyVersion}}")]
                [assembly: System.Reflection.AssemblyFileVersion("{{fileVersion}}")]
                [assembly: System.Reflection.AssemblyInformationalVersion("{{informational}}")]
                public static class SyntheticClientIdentity {}' -OutputAssembly '{{outputAssembly}}' -OutputType Library
                """);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
            using (var process = Process.Start(start)!)
            {
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(process.ExitCode == 0, await output + await error);
            }
            var assets = Path.Combine(sandbox, "assets");
            var manifest = await UpdatePackBuilder.BuildAsync(client, null, assets, ReleaseVersion.Parse(target));
            var manifestPath = Path.Combine(assets, UpdateManifestValidation.ManifestName);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(manifestPath)));
            var package = new PreparedUpdatePackage(sandbox, manifestPath, hash, manifest);
            var staged = SideBySideInstallation.Stage(root, package, _ => { }, default);
            SideBySideInstallation.Activate(staged);
            // Hash verification succeeds even for disagreeing PE stamps.
            Assert.True(File.Exists(SideBySideInstallation.VerifyInstalledClientFile(root, manifest.Version, "SiloPlayer.dll")));
            if (valid)
            {
                Assert.Equal(target, SideBySideInstallation.VerifyInstalledClientIdentity(root, manifest.Version).Text);
                SideBySideInstallation.Confirm(root, staged.TransactionId);
                Assert.Null(SideBySideInstallation.ReadState(root)!.PendingVersion);
            }
            else
            {
                Assert.Throws<InvalidDataException>(() => SideBySideInstallation.VerifyInstalledClientIdentity(root, manifest.Version));
                var state = SideBySideInstallation.ReadState(root)!;
                Assert.Equal(manifest.Version, state.PendingVersion);
                Assert.Equal(staged.TransactionId, state.TransactionId);
                Assert.Equal("1.2.516.0", state.PreviousVersion);
                Assert.True(File.Exists(Path.Combine(old, "SiloPlayer.dll")));
            }
        }
        finally { Directory.Delete(sandbox, true); }
    }
}
