using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class SideBySideInstallationTests : IDisposable
{
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "silo-side-by-side-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(sandbox, "installation");

    public SideBySideInstallationTests() => Directory.CreateDirectory(Root);

    [Fact]
    public void MutexIdentityMatchesLegacyCanonicalRoot()
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(Root).ToUpperInvariant())))[..16];
        Assert.Equal(@"Global\SiloDesktopUpdate-" + hash, SideBySideInstallation.GetMutexName(Root));
    }

    [Fact]
    public void CancelledStagedVersionCanBeRetriedWithoutDeletingForeignFiles()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.Rollback(Root, staged.TransactionId);
        SideBySideInstallation.CleanupOwnedVersions(Root, _ => false);
        Assert.False(Directory.Exists(staged.VersionDirectory));
        Assert.NotNull(SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default));
    }

    [Fact]
    public void PendingBootReadinessWritesOnlyPerUserReceiptAndRequiresExactLiveProcess()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2", realClient: true), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        var protectedState = File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json"));
        var receiptDirectory = Path.Combine(sandbox, "user-receipts");
        using var process = StartClient(staged.VersionDirectory);
        try
        {
            SideBySideInstallation.RecordBoot(Root, process, receiptDirectory);
            Assert.False(SideBySideInstallation.IsBootReady(Root, receiptDirectory));
            SideBySideInstallation.RecordReady(Root, process, receiptDirectory);
            Assert.True(SideBySideInstallation.IsBootReady(Root, receiptDirectory));
            Assert.Equal(protectedState, File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json")));
            Assert.Equal("1.0.0.2", SideBySideInstallation.ReadState(Root)!.PendingVersion);
            var receipt = SideBySideInstallation.ReadBootReceipt(Root, receiptDirectory)!;
            File.WriteAllBytes(Path.Combine(receiptDirectory, "boot.json"), JsonSerializer.SerializeToUtf8Bytes(receipt with { ProcessStartUtcTicks = receipt.ProcessStartUtcTicks - 1 }));
            Assert.False(SideBySideInstallation.IsBootReady(Root, receiptDirectory));
        }
        finally { if (!process.HasExited) process.Kill(true); process.WaitForExit(); }
    }

    [Fact]
    public void UnreadyExitedProcessFallsBackButReadyNormalExitKeepsSelectedVersion()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2", realClient: true), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        var receipts = Path.Combine(sandbox, "user-receipts");
        using (var process = StartClient(staged.VersionDirectory))
        {
            SideBySideInstallation.RecordBoot(Root, process, receipts);
            Assert.Equal(Path.Combine(staged.VersionDirectory, "SiloPlayer.exe"), SideBySideInstallation.ResolveLaunchExecutable(Root, receipts));
            process.Kill(true); process.WaitForExit();
        }
        Assert.Equal(Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.exe"), SideBySideInstallation.ResolveLaunchExecutable(Root, receipts));
        using (var process = StartClient(staged.VersionDirectory))
        {
            SideBySideInstallation.RecordBoot(Root, process, receipts);
            SideBySideInstallation.RecordReady(Root, process, receipts);
            process.Kill(true); process.WaitForExit();
        }
        Assert.Equal(Path.Combine(staged.VersionDirectory, "SiloPlayer.exe"), SideBySideInstallation.ResolveLaunchExecutable(Root, receipts));
    }

    private static Process StartClient(string directory) => Process.Start(new ProcessStartInfo(Path.Combine(directory, "SiloPlayer.exe"))
    { ArgumentList = { "-n", "30", "127.0.0.1" }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;

    [Fact]
    public void FullInstallerInitializationCanSelectNewVerifiedLayoutAndUninstallPreservesUnknownFiles()
    {
        Initialize();
        var package = Package("1.0.0.2");
        var target = Path.Combine(Root, "versions", "1.0.0.2");
        Directory.CreateDirectory(target);
        foreach (var file in package.Manifest.Files)
            File.Copy(Path.Combine(package.Directory, "client", file.Path), Path.Combine(target, file.Path));
        SideBySideInstallation.InitializeFromInstaller(Root, package.Manifest);
        Assert.Equal("1.0.0.2", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.PreviousVersion);
        var foreign = Path.Combine(target, "foreign.txt");
        File.WriteAllText(foreign, "preserve");
        SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root, _ => false);
        Assert.Equal("preserve", File.ReadAllText(foreign));
        Assert.False(File.Exists(Path.Combine(target, "SiloPlayer.dll")));
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", "1.0.0.1")));
    }

    private (string Launcher, string Target) CreateLauncherFixture(bool initialize = true)
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "launcher-fixture"), "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(Root, Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "launcher-fixture"), path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination);
        }
        var launcher = Path.Combine(Root, "SiloPlayer.exe");
        File.Move(Path.Combine(Root, "SiloPlayer.Launcher.exe"), launcher);
        var target = Path.Combine(Root, "versions", "1.0.0.1"); Directory.CreateDirectory(target);
        var entries = new List<UpdateFileEntry>(); long offset = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "client-fixture")))
        {
            var name = Path.GetFileName(path);
            if (name == "SiloPlayer.LauncherFixture.exe") name = "SiloPlayer.exe";
            var bytes = File.ReadAllBytes(path); File.WriteAllBytes(Path.Combine(target, name), bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            entries.Add(new(name, bytes.LongLength, hash, offset++, 1, hash));
        }
        // The production entry point requirement remains SiloPlayer.dll. The fixture's
        // apphost uses its distinct assembly name and this dummy is inventory-only.
        File.WriteAllText(Path.Combine(target, "SiloPlayer.dll"), "fixture inventory");
        var dummy = File.ReadAllBytes(Path.Combine(target, "SiloPlayer.dll"));
        entries.Add(new("SiloPlayer.dll", dummy.Length, Convert.ToHexString(SHA256.HashData(dummy)), offset++, 1, new string('A', 64)));
        var manifest = new UpdateManifest(1, "1.0.0.1", offset, new string('A', 64), entries);
        Directory.CreateDirectory(Path.Combine(Root, ".silo-update"));
        File.WriteAllBytes(Path.Combine(Root, ".silo-update", "initial.manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
        if (initialize)
        {
            RunLauncher(launcher, ["--silo-initialize"]);
            Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        }
        return (launcher, target);
    }

    [Fact]
    public void FirstFullMigrationCanInitializeWhileExactLegacyRootGateIsHeld()
    {
        var (launcher, _) = CreateLauncherFixture(initialize: false);
        using var legacyGate = new Mutex(false, SideBySideInstallation.GetMutexName(Root));
        Assert.True(legacyGate.WaitOne(0));
        try
        {
            RunLauncher(launcher, ["--silo-initialize"]);
            Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        }
        finally { legacyGate.ReleaseMutex(); }
    }

    [Fact]
    public void FirstMigrationIsSerializedByItsSeparateInitializationGate()
    {
        var (launcher, _) = CreateLauncherFixture(initialize: false);
        using var held = new Mutex(false, @"Global\SiloDesktopInitialize-" + SideBySideInstallation.RootHash(Root)[..16]);
        Assert.True(held.WaitOne(0));
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(launcher) { UseShellExecute = false, ArgumentList = { "--silo-initialize" } })!;
            Assert.False(process.WaitForExit(250));
            Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "state.json")));
        }
        finally { held.ReleaseMutex(); }
        using (process!) { Assert.True(process!.WaitForExit(15000)); Assert.Equal(0, process.ExitCode); }
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
    }

    [Theory]
    [InlineData("--silo-initialize")]
    [InlineData("--silo-uninstall-owned")]
    public void ExistingInstallationInternalMutationStillRespectsHeldRootGate(string operation)
    {
        var (launcher, _) = CreateLauncherFixture();
        var state = File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json"));
        using var held = new Mutex(false, SideBySideInstallation.GetMutexName(Root)); Assert.True(held.WaitOne(0));
        try
        {
            var start = new ProcessStartInfo(launcher) { UseShellExecute = false, ArgumentList = { operation } };
            using var process = Process.Start(start)!; Assert.True(process.WaitForExit(15000)); Assert.Equal(2, process.ExitCode);
            Assert.Equal(state, File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json")));
        }
        finally { held.ReleaseMutex(); }
    }

    [Fact]
    public void OrdinaryLaunchCannotDispatchClientWhileRootGateIsHeld()
    {
        var (launcher, _) = CreateLauncherFixture();
        var output = Path.Combine(sandbox, "should-not-launch.json");
        using var held = new Mutex(false, SideBySideInstallation.GetMutexName(Root)); Assert.True(held.WaitOne(0));
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(launcher) { UseShellExecute = false, ArgumentList = { "silo://synthetic-gate-check" } };
            start.Environment["SILO_LAUNCHER_FIXTURE_OUTPUT"] = output;
            process = Process.Start(start)!;
            Assert.True(SpinWait.SpinUntil(() => { process.Refresh(); return process.HasExited || process.MainWindowHandle != IntPtr.Zero; }, 10000));
            if (!process.HasExited) PostMessage(process.MainWindowHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);
            Assert.True(process.WaitForExit(10000)); Assert.Equal(1, process.ExitCode);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (process is not null) { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } process.Dispose(); }
            held.ReleaseMutex();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);

    [Fact]
    public void OwnedUninstallRemovesValidatedCooldownAndPreservesForeignMetadata()
    {
        Initialize();
        UpdateAuthorityCooldown.Record(Root, TimeSpan.FromMinutes(10));
        var metadata = Path.Combine(Root, ".silo-update");
        File.WriteAllText(Path.Combine(metadata, "foreign.txt"), "preserve metadata addition");
        SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root, _ => false);
        Assert.False(File.Exists(Path.Combine(metadata, "github-cooldown.json")));
        Assert.Equal("preserve metadata addition", File.ReadAllText(Path.Combine(metadata, "foreign.txt")));
    }

    [Theory]
    [InlineData("foreign content")]
    [InlineData("{\"Schema\":2}")]
    public void OwnedUninstallPreservesUnrecognizedCooldownFile(string content)
    {
        Initialize();
        var path = Path.Combine(Root, ".silo-update", "github-cooldown.json"); File.WriteAllText(path, content);
        SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root, _ => false);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void ActualLauncherInitializesDispatchesArgumentsAndUninstallsOnlyOwnedFiles()
    {
        var (launcher, target) = CreateLauncherFixture();
        var output = Path.Combine(sandbox, "arguments.json");
        string[] expected = ["silo://auth/callback?code=synthetic&state=fixture", "space argument", "quote\"argument", "trailing\\"];
        var start = new ProcessStartInfo(launcher) { UseShellExecute = false };
        start.Environment["SILO_LAUNCHER_FIXTURE_OUTPUT"] = output;
        foreach (var argument in expected) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!) { Assert.True(process.WaitForExit(15000)); Assert.Equal(0, process.ExitCode); }
        Assert.True(SpinWait.SpinUntil(() => File.Exists(output), 15000));
        Assert.Equal(expected, JsonSerializer.Deserialize<string[]>(File.ReadAllText(output)));
        var foreign = Path.Combine(target, "foreign.txt"); File.WriteAllText(foreign, "keep");
        RunLauncher(launcher, ["--silo-uninstall-owned"]);
        Assert.Equal("keep", File.ReadAllText(foreign));
        Assert.False(File.Exists(Path.Combine(target, "SiloPlayer.exe")));
    }

    [Fact]
    public void ActualLauncherForwardsReadinessHandshakeAndRecoversImmediateExit()
    {
        var (launcher, target) = CreateLauncherFixture();
        var initial = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllBytes(Path.Combine(Root, ".silo-update", "initial.manifest.json")))!;
        var directory = Path.Combine(sandbox, "prepared"); Directory.CreateDirectory(Path.Combine(directory, "client"));
        foreach (var file in initial.Files) File.Copy(Path.Combine(target, file.Path), Path.Combine(directory, "client", file.Path));
        var manifest = initial with { Version = "1.0.0.2" };
        var path = Path.Combine(directory, "manifest.json"); File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(manifest));
        var staged = SideBySideInstallation.Stage(Root, new(directory, path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), manifest), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        var output = Path.Combine(sandbox, "handshake.json");
        var receiptDirectory = SideBySideInstallation.GetBootReceiptDirectory(Root);
        try
        {
            string[] expected = ["--silo-update-ready", "SiloUpdate-" + Guid.NewGuid().ToString("N")];
            var start = new ProcessStartInfo(launcher) { UseShellExecute = false };
            start.Environment["SILO_LAUNCHER_FIXTURE_OUTPUT"] = output;
            foreach (var argument in expected) start.ArgumentList.Add(argument);
            using (var process = Process.Start(start)!) { Assert.True(process.WaitForExit(15000)); Assert.Equal(0, process.ExitCode); }
            Assert.True(SpinWait.SpinUntil(() => File.Exists(output), 15000));
            Assert.Equal(expected, JsonSerializer.Deserialize<string[]>(File.ReadAllText(output)));
            // Fixture exits immediately without a readiness callback. The launcher may
            // observe its live PID or only the prestart attempt; either must recover.
            Assert.True(SpinWait.SpinUntil(() => SideBySideInstallation.ResolveLaunchExecutable(Root) == Path.Combine(target, "SiloPlayer.exe"), 15000));
            Assert.Equal("1.0.0.2", SideBySideInstallation.ReadState(Root)!.PendingVersion);
        }
        finally
        {
            var receipt = Path.Combine(receiptDirectory, "boot.json"); if (File.Exists(receipt)) File.Delete(receipt);
            if (Directory.Exists(receiptDirectory) && !Directory.EnumerateFileSystemEntries(receiptDirectory).Any()) Directory.Delete(receiptDirectory);
        }
    }

    [Fact]
    public void InterruptedStagedTransactionRecoversForSameVersionRetry()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.RecoverInterruptedTransaction(Root);
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
        Assert.NotNull(SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default));
    }

    [Fact]
    public void CleanupPreservesForeignEmptyDirectoriesAndModifiedOwnedFiles()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.Rollback(Root, staged.TransactionId);
        var foreignDirectory = Path.Combine(staged.VersionDirectory, "foreign-empty"); Directory.CreateDirectory(foreignDirectory);
        var modified = Path.Combine(staged.VersionDirectory, "SiloPlayer.dll"); File.WriteAllText(modified, "modified preserve");
        SideBySideInstallation.CleanupOwnedVersions(Root, _ => false);
        Assert.True(Directory.Exists(foreignDirectory));
        Assert.Equal("modified preserve", File.ReadAllText(modified));
    }

    [Fact]
    public void FailedLaunchFallsBackWithoutRewritingProtectedState()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        var receipts = Path.Combine(sandbox, "receipt");
        SideBySideInstallation.RecordFailedLaunch(Root, Path.Combine(staged.VersionDirectory, "SiloPlayer.exe"), receipts);
        Assert.Equal(Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.exe"), SideBySideInstallation.ResolveLaunchExecutable(Root, receipts));
        Assert.Equal("1.0.0.2", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.False(SideBySideInstallation.IsBootReady(Root, receipts));
    }

    [Fact]
    public void AbandonedPrestartObservationFallsBackAndNeverClaimsReadiness()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        var receipts = Path.Combine(sandbox, "receipt");
        SideBySideInstallation.BeginLaunch(Root, Path.Combine(staged.VersionDirectory, "SiloPlayer.exe"), receipts);
        var receipt = SideBySideInstallation.ReadBootReceipt(Root, receipts)!;
        Assert.True(receipt.Starting);
        Assert.False(SideBySideInstallation.IsBootReady(Root, receipts));
        // The test process is deliberately not the registered root launcher, so its
        // observation cannot keep an abandoned startup looking alive.
        Assert.Equal(Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.exe"), SideBySideInstallation.ResolveLaunchExecutable(Root, receipts));
    }

    [Fact]
    public void HardLinkedCacheSourceIsRejectedWithoutChangingActiveVersion()
    {
        Initialize();
        var package = Package("1.0.0.2");
        var source = Path.Combine(package.Directory, "client", "SiloPlayer.dll");
        var outside = Path.Combine(sandbox, "linked.dll"); File.Move(source, outside);
        Assert.True(CreateHardLink(source, outside, IntPtr.Zero));
        Assert.Throws<InvalidDataException>(() => SideBySideInstallation.Stage(Root, package, _ => { }, default));
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        SideBySideInstallation.RecoverInterruptedTransaction(Root);
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
    }

    [Fact]
    public void InterruptedCopyKeepsActiveAndAllowsFreshRetry()
    {
        Initialize();
        Assert.Throws<IOException>(() => SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => throw new IOException("Synthetic full-disk/copy failure"), default));
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        SideBySideInstallation.RecoverInterruptedTransaction(Root);
        Assert.NotNull(SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default));
    }

    [Fact]
    public void CleanupDoesNotDeleteUninventoriedEmptyVersionDirectory()
    {
        Initialize();
        var unknown = Path.Combine(Root, "versions", "9.0.0.9"); Directory.CreateDirectory(unknown);
        SideBySideInstallation.CleanupOwnedVersions(Root, _ => false);
        Assert.True(Directory.Exists(unknown));
    }

    [Fact]
    public void CorruptActiveFallbackIsRejectedBeforeTransactionStarts()
    {
        Initialize();
        File.WriteAllText(Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.dll"), "corrupt active");
        Assert.Throws<InvalidDataException>(() => SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default));
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnobservedPendingMissingOrCorruptPayloadFallsBackWithoutProtectedWrites(bool corrupt)
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        var protectedPath = Path.Combine(Root, ".silo-update", "state.json");
        var original = File.ReadAllBytes(protectedPath);
        var file = Path.Combine(staged.VersionDirectory, "SiloPlayer.dll");
        if (corrupt) File.WriteAllText(file, "corrupt pending"); else File.Delete(file);
        Assert.Equal(Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.exe"), SideBySideInstallation.ResolveLaunchExecutable(Root, Path.Combine(sandbox, "receipt")));
        Assert.Equal(original, File.ReadAllBytes(protectedPath));
    }

    [Fact]
    public void ConfirmedActiveCorruptionStillFailsInsteadOfSilentlyDowngrading()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.Activate(staged); SideBySideInstallation.Confirm(Root, staged.TransactionId);
        File.Delete(Path.Combine(staged.VersionDirectory, "SiloPlayer.dll"));
        Assert.ThrowsAny<IOException>(() => SideBySideInstallation.ResolveLaunchExecutable(Root, Path.Combine(sandbox, "receipt")));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr security);

    private static void RunLauncher(string launcher, string[] arguments)
    {
        var start = new ProcessStartInfo(launcher) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(15000)); Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void StageKeepsConfirmedVersionAndLiveSyntheticClientUntilActivation()
    {
        Initialize();
        using var client = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
            Assert.False(client.HasExited);
            Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
            SideBySideInstallation.Activate(staged);
            Assert.Equal("1.0.0.2", SideBySideInstallation.ReadState(Root)!.PendingVersion);
            SideBySideInstallation.Confirm(Root, staged.TransactionId);
            Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.PreviousVersion);
        }
        finally { if (!client.HasExited) client.Kill(true); client.WaitForExit(); }
    }

    [Fact]
    public void TamperedCacheCannotCreateReadyVersion()
    {
        Initialize();
        var package = Package("1.0.0.2");
        File.WriteAllText(Path.Combine(package.Directory, "client", "SiloPlayer.dll"), "tampered");
        Assert.ThrowsAny<Exception>(() => SideBySideInstallation.Stage(Root, package, _ => { }, default));
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", "1.0.0.2")));
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
    }

    [Fact]
    public void CancellationAndStaleTransactionPreserveWorkingVersion()
    {
        Initialize();
        Assert.Throws<OperationCanceledException>(() => SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, new CancellationToken(true)));
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        Assert.Throws<InvalidOperationException>(() => SideBySideInstallation.Stage(Root, Package("1.0.0.3"), _ => { }, default));
        Assert.Throws<InvalidOperationException>(() => SideBySideInstallation.Confirm(Root, Guid.NewGuid().ToString("N")));
        SideBySideInstallation.Rollback(Root, staged.TransactionId);
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
    }

    [Fact]
    public void ChangedBootstrapIsRejectedBeforeCreatingTransaction()
    {
        Initialize();
        File.WriteAllText(Path.Combine(Root, "first.dll"), "old first");
        File.WriteAllText(Path.Combine(Root, "second.dll"), "old second");
        Assert.ThrowsAny<InvalidDataException>(() => SideBySideInstallation.Stage(Root, Package("1.0.0.2", true), _ => { }, default));
        Assert.Equal("old first", File.ReadAllText(Path.Combine(Root, "first.dll")));
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
    }

    [Fact]
    public void UnchangedBootstrapRemainsImmutableAndCleanupPreservesForeignFile()
    {
        Initialize();
        File.WriteAllText(Path.Combine(Root, "first.dll"), "new first");
        File.WriteAllText(Path.Combine(Root, "second.dll"), "new second");
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2", true), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        Assert.Equal("new first", File.ReadAllText(Path.Combine(Root, "first.dll")));
        SideBySideInstallation.Rollback(Root, staged.TransactionId);
        Assert.Equal("new first", File.ReadAllText(Path.Combine(Root, "first.dll")));
        var foreign = Path.Combine(staged.VersionDirectory, "foreign-not-owned.txt");
        File.WriteAllText(foreign, "preserve");
        SideBySideInstallation.CleanupOwnedVersions(Root, _ => false);
        Assert.Equal("preserve", File.ReadAllText(foreign));
        Assert.False(File.Exists(Path.Combine(staged.VersionDirectory, "SiloPlayer.dll")));
    }

    [Fact]
    public void LinkedSourceIsRejected()
    {
        Initialize();
        var package = Package("1.0.0.2");
        var file = Path.Combine(package.Directory, "client", "SiloPlayer.dll");
        var outside = Path.Combine(sandbox, "outside.dll");
        File.Move(file, outside);
        try { File.CreateSymbolicLink(file, outside); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }
        Assert.ThrowsAny<Exception>(() => SideBySideInstallation.Stage(Root, package, _ => { }, default));
    }

    private void Initialize()
    {
        var package = Package("1.0.0.1");
        var target = Path.Combine(Root, "versions", "1.0.0.1");
        Directory.CreateDirectory(target);
        foreach (var file in package.Manifest.Files.Where(f => f.Area == "client"))
            File.Copy(Path.Combine(package.Directory, "client", file.Path), Path.Combine(target, file.Path));
        SideBySideInstallation.Initialize(Root, package.Manifest);
    }

    private PreparedUpdatePackage Package(string version, bool bootstrap = false, bool realClient = false)
    {
        var directory = Path.Combine(sandbox, Guid.NewGuid().ToString("N"));
        var entries = new List<UpdateFileEntry>();
        long offset = 0;
        void Add(string area, string path, string value)
        {
            Directory.CreateDirectory(Path.Combine(directory, area));
            var bytes = realClient && area == "client" && path == "SiloPlayer.exe"
                ? File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe"))
                : System.Text.Encoding.UTF8.GetBytes(value);
            File.WriteAllBytes(Path.Combine(directory, area, path), bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            entries.Add(new(path, bytes.Length, hash, offset++, 1, hash, area));
        }
        Add("client", "SiloPlayer.exe", "synthetic exe " + version);
        Add("client", "SiloPlayer.dll", "synthetic dll " + version);
        if (bootstrap) { Add("bootstrap", "first.dll", "new first"); Add("bootstrap", "second.dll", "new second"); }
        var manifest = new UpdateManifest(1, version, offset, new string('A', 64), entries);
        var manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
        return new(directory, manifestPath, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))), manifest);
    }

    public void Dispose() { try { Directory.Delete(sandbox, true); } catch { } }
}
