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
    public void BuiltRootApphostCarriesTheFullUpdateGateCapabilityResource()
    {
        var (launcher, _) = CreateLauncherFixture();
        Assert.Equal("Silo Desktop Player launcher; FullUpdateGate=1", FileVersionInfo.GetVersionInfo(launcher).FileDescription);
        Assert.True(UpdateInstallation.SupportsVersionedFullInstaller(Root));
    }

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
    // Until 2026-11-26 uninstall kept unknown files in a version folder; the owner decided that
    // uninstall removes everything, including what the app creates there at runtime.
    public void FullInstallerInitializationCanSelectNewVerifiedLayoutAndUninstallRemovesAppCreatedFiles()
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
        Directory.CreateDirectory(Path.Combine(target, "SiloPlayer.exe.WebView2", "EBWebView"));
        File.WriteAllText(Path.Combine(target, "SiloPlayer.exe.WebView2", "EBWebView", "Local State"), "created at runtime");
        File.WriteAllText(Path.Combine(target, "foreign.txt"), "unknown addition");
        var report = SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root);
        Assert.Empty(report.Retained);
        Assert.False(Directory.Exists(Path.Combine(Root, "versions")));
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update")));
    }

    [Fact]
    public void FullInstallerOverADeferredStagedUpdateOfTheSameVersionKeepsItsOwnPayload()
    {
        Initialize();
        var package = Package("1.0.0.2");
        // Later leaves the staged journal and the version's inventory in place across sessions.
        var staged = SideBySideInstallation.Stage(Root, package, _ => { }, default);
        // The full installer of that same release then writes identical bytes into the same directory.
        foreach (var file in package.Manifest.Files)
            File.Copy(Path.Combine(package.Directory, "client", file.Path), Path.Combine(staged.VersionDirectory, file.Path), overwrite: true);
        SideBySideInstallation.InitializeFromInstaller(Root, package.Manifest);
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal("1.0.0.2", state.ActiveVersion); Assert.Equal("1.0.0.1", state.PreviousVersion);
        Assert.Null(state.PendingVersion);
        Assert.Equal(Path.Combine(staged.VersionDirectory, "SiloPlayer.exe"), SideBySideInstallation.ResolveClientExecutable(Root));
    }

    [Fact]
    public void FullInstallerStillRemovesADeferredStagedUpdateOfAnotherVersion()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.3"), _ => { }, default);
        var package = Package("1.0.0.2");
        var target = Path.Combine(Root, "versions", "1.0.0.2");
        Directory.CreateDirectory(target);
        foreach (var file in package.Manifest.Files)
            File.Copy(Path.Combine(package.Directory, "client", file.Path), Path.Combine(target, file.Path));
        SideBySideInstallation.InitializeFromInstaller(Root, package.Manifest);
        Assert.Equal("1.0.0.2", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
        Assert.False(Directory.Exists(staged.VersionDirectory));
    }

    // The owner's case (2026-11-26): a full install of one version, then one incremental update
    // through the elevated host, then an ordinary uninstall. The folder must not survive it.
    [Fact]
    public void UninstallAfterAnIncrementalUpdateLeavesNoInstallFolder()
    {
        var logged = SimulateFullInstall("1.0.0.1");
        var staged = SideBySideInstallation.Stage(Root, HostAuthorityPackage(Package("1.0.0.2")), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        SideBySideInstallation.Confirm(Root, staged.TransactionId);
        SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root);
        SimulateInnoUninstall(logged);
        Assert.False(Directory.Exists(Root), "Left behind: " + Describe(Root));
    }

    // What an updater before 2026-11-26 left after every update, plus an interrupted record write.
    [Fact]
    public void UninstallAlsoRemovesWhatAnOlderUpdaterLeftBehind()
    {
        var logged = SimulateFullInstall("1.0.0.1");
        Directory.CreateDirectory(Path.Combine(Root, ".silo-update", "staging", Guid.NewGuid().ToString("N"), "bootstrap"));
        var authority = Path.Combine(Root, ".silo-update", "authority", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(authority); File.WriteAllText(Path.Combine(authority, new string('A', 64) + ".manifest.json"), "{}");
        File.WriteAllText(Path.Combine(Root, ".silo-update", "state.json." + Guid.NewGuid().ToString("N") + ".tmp"), "interrupted write");
        Assert.Empty(SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root).Retained);
        SimulateInnoUninstall(logged);
        Assert.False(Directory.Exists(Root), "Left behind: " + Describe(Root));
    }

    [Fact]
    public void UninstallWithALockedFileFinishesPromptlyAndRemovesEverythingElse()
    {
        Initialize();
        var locked = Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.dll");
        var clock = Stopwatch.StartNew();
        UninstallReport report;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            report = SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Uninstall waited for a locked file.");
        Assert.Contains(locked, report.Retained, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, report.QueuedForRestart);
        Assert.False(File.Exists(Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.exe")));
        Assert.False(File.Exists(Path.Combine(Root, "versions", "1.0.0.1", SideBySideInstallation.InventoryFileName)));
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update")));
    }

    [Fact]
    public void UninstallRemovesJunctionsAsLinksAndNeverDeletesThroughThem()
    {
        Initialize();
        var outside = Path.Combine(sandbox, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "nested"));
        File.WriteAllText(Path.Combine(outside, "nested", "keep.txt"), "outside the installation");
        CreateJunction(Path.Combine(Root, "versions", "1.0.0.1", "linked"), outside);
        Directory.CreateDirectory(Path.Combine(Root, ".silo-update", "staging"));
        CreateJunction(Path.Combine(Root, ".silo-update", "staging", Guid.NewGuid().ToString("N")), outside);
        Assert.Empty(SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root).Retained);
        Assert.Equal("outside the installation", File.ReadAllText(Path.Combine(outside, "nested", "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(Root, "versions")));
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update")));
    }

    [Fact]
    public void UninstallFromACustomInstallDirectoryRemovesOnlyThatInstallation()
    {
        var parent = Path.Combine(sandbox, "Apps folder");
        var custom = Path.Combine(parent, "Silo Player (custom)");
        var neighbour = Path.Combine(parent, "Other program");
        Directory.CreateDirectory(neighbour); File.WriteAllText(Path.Combine(neighbour, "keep.txt"), "another program");
        Initialize(custom);
        Assert.Empty(SideBySideInstallation.RemoveOwnedVersionsForUninstall(custom).Retained);
        Assert.Empty(Directory.EnumerateFileSystemEntries(custom));
        Assert.Equal("another program", File.ReadAllText(Path.Combine(neighbour, "keep.txt")));
    }

    [Fact]
    public void UninstallRefusesAFolderThatIsNotADedicatedSiloInstallation()
    {
        Initialize();
        Directory.CreateDirectory(Path.Combine(Root, ".git"));
        Assert.Throws<InvalidDataException>(() => SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root));
        Assert.True(File.Exists(Path.Combine(Root, "versions", "1.0.0.1", "SiloPlayer.dll")));
        // A folder with a versions folder but no Silo control records is not a Silo layout.
        var other = Path.Combine(sandbox, "not-silo");
        Directory.CreateDirectory(Path.Combine(other, "versions", "keep"));
        File.WriteAllText(Path.Combine(other, "versions", "keep", "data.txt"), "not Silo's");
        Assert.Throws<InvalidOperationException>(() => SideBySideInstallation.RemoveOwnedVersionsForUninstall(other));
        Assert.Equal("not Silo's", File.ReadAllText(Path.Combine(other, "versions", "keep", "data.txt")));
    }

    [Fact]
    public void ConfirmedUpdateLeavesNoStagingOrAuthorityAndKeepsRollbackToThePreviousVersion()
    {
        Initialize();
        var staged = SideBySideInstallation.Stage(Root, HostAuthorityPackage(Package("1.0.0.2")), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        SideBySideInstallation.Confirm(Root, staged.TransactionId);
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update", "staging")));
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update", "authority")));
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal(("1.0.0.2", "1.0.0.1"), (state.ActiveVersion, state.PreviousVersion));
        Assert.Equal(Path.Combine(Root, "versions", "1.0.0.2", "SiloPlayer.exe"), SideBySideInstallation.ResolveClientExecutable(Root));
        // The next update still falls back to, and rolls back to, the confirmed version.
        var next = SideBySideInstallation.Stage(Root, HostAuthorityPackage(Package("1.0.0.3")), _ => { }, default);
        SideBySideInstallation.Activate(next);
        var receipts = Path.Combine(sandbox, "receipt");
        SideBySideInstallation.RecordFailedLaunch(Root, Path.Combine(next.VersionDirectory, "SiloPlayer.exe"), receipts);
        Assert.Equal(Path.Combine(Root, "versions", "1.0.0.2", "SiloPlayer.exe"), SideBySideInstallation.ResolveLaunchExecutable(Root, receipts));
        SideBySideInstallation.Rollback(Root, next.TransactionId);
        Assert.Equal("1.0.0.2", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update", "staging", next.TransactionId)));
    }

    [Fact]
    public void LaterKeepsItsStagedUpdateWhileEachRunReplacesTheAuthorityCopy()
    {
        Initialize();
        var package = Package("1.0.0.2");
        var first = HostAuthorityPackage(package);
        var staged = SideBySideInstallation.Stage(Root, first, _ => { }, default);
        // Later: the journal and staged files stay. The next run fetches release authority again.
        var authority = SideBySideInstallation.PrepareAuthorityDirectory(Root);
        Assert.False(File.Exists(first.ManifestPath));
        Directory.CreateDirectory(authority);
        var path = Path.Combine(authority, Path.GetFileName(first.ManifestPath));
        File.Copy(package.ManifestPath, path);
        var resumed = SideBySideInstallation.TryResumeStaged(Root, package with { ManifestPath = path }, default);
        Assert.Equal(staged.TransactionId, resumed?.TransactionId);
        SideBySideInstallation.Activate(resumed!);
        // A pending transaction keeps its own staging through recovery.
        SideBySideInstallation.RecoverInterruptedTransaction(Root);
        Assert.True(Directory.Exists(resumed!.BootstrapDirectory));
        SideBySideInstallation.Confirm(Root, resumed.TransactionId);
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update", "staging")));
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update", "authority")));
    }

    [Fact]
    public void InterruptedUpdateStagingIsRemovedOnTheNextRun()
    {
        Initialize();
        Assert.Throws<IOException>(() => SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => throw new IOException("Synthetic power loss"), default));
        var staging = Path.Combine(Root, ".silo-update", "staging");
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(staging, "*", SearchOption.AllDirectories));
        // A confirmed transaction of an older updater also left its staging behind.
        Directory.CreateDirectory(Path.Combine(staging, Guid.NewGuid().ToString("N"), "bootstrap"));
        SideBySideInstallation.RecoverInterruptedTransaction(Root);
        Assert.False(Directory.Exists(staging));
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", "1.0.0.2")));
        Assert.NotNull(SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default));
    }

    [Fact]
    public void OlderFullInstallOverANewerVersionKeepsNoNewerFallbackAndCanUpdateBack()
    {
        Initialize();
        var newer = SideBySideInstallation.Stage(Root, Package("1.0.0.3"), _ => { }, default);
        SideBySideInstallation.Activate(newer); SideBySideInstallation.Confirm(Root, newer.TransactionId);
        // An older stable over the newer beta, with the full installer.
        CopyForFullInstaller(Package("1.0.0.2"), out var older);
        SideBySideInstallation.InitializeFromInstaller(Root, older.Manifest);
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal("1.0.0.2", state.ActiveVersion); Assert.Null(state.PreviousVersion);
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", "1.0.0.3")));
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", "1.0.0.1")));
        // The update back to the newer release is an ordinary update with the older one as fallback.
        var update = SideBySideInstallation.Stage(Root, Package("1.0.0.3"), _ => { }, default);
        SideBySideInstallation.Activate(update); SideBySideInstallation.Confirm(Root, update.TransactionId);
        state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal(("1.0.0.3", "1.0.0.2"), (state.ActiveVersion, state.PreviousVersion));
    }

    [Fact]
    public void FullInstallKeepsOnlyTheActiveAndPreviousVersions()
    {
        Initialize();
        var update = SideBySideInstallation.Stage(Root, Package("1.0.0.2"), _ => { }, default);
        SideBySideInstallation.Activate(update); SideBySideInstallation.Confirm(Root, update.TransactionId);
        CopyForFullInstaller(Package("1.0.0.3"), out var full);
        SideBySideInstallation.InitializeFromInstaller(Root, full.Manifest);
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal(("1.0.0.3", "1.0.0.2"), (state.ActiveVersion, state.PreviousVersion));
        Assert.True(Directory.Exists(Path.Combine(Root, "versions", "1.0.0.2")));
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", "1.0.0.1")));
    }

    private void CopyForFullInstaller(PreparedUpdatePackage package, out PreparedUpdatePackage copied)
    {
        var target = Path.Combine(Root, "versions", package.Manifest.Version);
        Directory.CreateDirectory(target);
        foreach (var file in package.Manifest.Files.Where(f => f.Area == "client"))
            File.Copy(Path.Combine(package.Directory, "client", file.Path), Path.Combine(target, file.Path));
        copied = package;
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, ArgumentList = { "/d", "/c", "mklink", "/J", link, target } })!;
        process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(15000)); Assert.Equal(0, process.ExitCode);
        Assert.True(new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    // Inno's [Files] step for the full installer: root bootstrap, the private version folder and the
    // initial manifest, then --silo-initialize. Returns what Inno logged: files, then created folders.
    private (List<string> Files, List<string> Directories) SimulateFullInstall(string version)
    {
        var directories = new List<string> { Root };
        var files = new List<string>();
        void Write(string relative, string content)
        {
            var path = Path.Combine(Root, relative);
            for (var parent = Path.GetDirectoryName(path)!; !directories.Contains(parent, StringComparer.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent)!)
                directories.Insert(1, parent);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content); files.Add(path);
        }
        Write("SiloPlayer.exe", "synthetic launcher");
        Write(Path.Combine("Assets", "app.ico"), "synthetic icon");
        var package = Package(version);
        foreach (var entry in package.Manifest.Files.Where(f => f.Area == "client"))
            Write(Path.Combine("versions", version, entry.Path), File.ReadAllText(Path.Combine(package.Directory, "client", entry.Path)));
        Write(Path.Combine(".silo-update", "initial.manifest.json"), File.ReadAllText(package.ManifestPath));
        Write("unins000.dat", "synthetic uninstall log"); Write("unins000.exe", "synthetic uninstaller");
        SideBySideInstallation.InitializeFromInstaller(Root, package.Manifest);
        return (files, directories.OrderBy(d => d.Length).ToList());
    }

    // Inno's uninstall: delete each logged file, then each folder it created if that folder is empty.
    private static void SimulateInnoUninstall((List<string> Files, List<string> Directories) logged)
    {
        foreach (var file in logged.Files) if (File.Exists(file)) File.Delete(file);
        foreach (var directory in Enumerable.Reverse(logged.Directories))
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    // UpdateWorker.FetchAuthoritativePackage keeps the release's manifest under .silo-update\authority.
    private PreparedUpdatePackage HostAuthorityPackage(PreparedUpdatePackage package)
    {
        var authority = Path.Combine(Root, ".silo-update", "authority", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(authority);
        var bytes = File.ReadAllBytes(package.ManifestPath);
        var path = Path.Combine(authority, Convert.ToHexString(SHA256.HashData(bytes)) + ".manifest.json");
        File.WriteAllBytes(path, bytes);
        return package with { ManifestPath = path };
    }

    private static string Describe(string directory) => !Directory.Exists(directory) ? "(nothing)" :
        string.Join(", ", Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(directory, p)));

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
    [InlineData("--silo-check-uninstall")]
    [InlineData("--silo-uninstall-owned")]
    public void ExistingInstallationMaintenanceRespectsHeldRootGate(string operation)
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryLaunchCannotDispatchClientWhileRootGateIsHeld(bool fullInstaller)
    {
        var (launcher, _) = CreateLauncherFixture();
        var output = Path.Combine(sandbox, "should-not-launch.json");
        var gateName = fullInstaller ? @"Global\SiloDesktopFullUpdate-" + SideBySideInstallation.RootHash(Root)[..16]
            : SideBySideInstallation.GetMutexName(Root);
        using var held = new Mutex(false, gateName); Assert.True(held.WaitOne(0));
        Process? process = null;
        try
        {
            // Full setup must still be able to run its post-copy verifier while
            // the helper excludes ordinary launches with the distinct gate.
            if (fullInstaller) RunLauncher(launcher, ["--silo-initialize"]);
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

    // Until 2026-11-26 uninstall kept unrecognized control files; uninstall now removes the whole
    // control folder (owner decision), whatever a cooldown or other record contains.
    [Theory]
    [InlineData(null)]
    [InlineData("foreign content")]
    [InlineData("{\"Schema\":2}")]
    public void UninstallRemovesTheWholeUpdaterControlFolder(string? cooldown)
    {
        Initialize();
        var metadata = Path.Combine(Root, ".silo-update");
        if (cooldown is null) UpdateAuthorityCooldown.Record(Root, TimeSpan.FromMinutes(10));
        else File.WriteAllText(Path.Combine(metadata, "github-cooldown.json"), cooldown);
        File.WriteAllText(Path.Combine(metadata, "foreign.txt"), "metadata addition");
        Assert.Empty(SideBySideInstallation.RemoveOwnedVersionsForUninstall(Root).Retained);
        Assert.False(Directory.Exists(metadata));
    }

    [Fact]
    public void UninstallPreflightRefusesALiveInstalledClientAndDoesNotRemoveFiles()
    {
        var (launcher, _) = CreateLauncherFixture();
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.2", realClient: true), _ => { }, default);
        var statePath = Path.Combine(Root, ".silo-update", "state.json");
        var state = File.ReadAllBytes(statePath);
        using (var client = StartClient(staged.VersionDirectory))
        {
            try
            {
                Assert.True(SpinWait.SpinUntil(() => SideBySideInstallation.IsVersionDirectoryInUse(staged.VersionDirectory), 5000));
                using var check = Process.Start(new ProcessStartInfo(launcher)
                { UseShellExecute = false, ArgumentList = { "--silo-check-uninstall" } })!;
                Assert.True(check.WaitForExit(15000));
                Assert.Equal(2, check.ExitCode);
                Assert.Equal(state, File.ReadAllBytes(statePath));
                Assert.True(File.Exists(Path.Combine(staged.VersionDirectory, "SiloPlayer.exe")));
            }
            finally { if (!client.HasExited) client.Kill(true); client.WaitForExit(); }
        }
        RunLauncher(launcher, ["--silo-check-uninstall"]);
        Assert.Equal(state, File.ReadAllBytes(statePath));
    }

    [Fact]
    public void UninstallPreflightReportsAnInternalErrorSeparatelyFromAnUpdateInProgress()
    {
        var (launcher, _) = CreateLauncherFixture();
        var statePath = Path.Combine(Root, ".silo-update", "state.json");
        var state = File.ReadAllBytes(statePath);
        // A different kernel object type under this fixture's mutex name makes opening the mutex fail,
        // rather than making WaitOne report that an update holds it.
        using var incompatibleGate = new EventWaitHandle(false, EventResetMode.ManualReset, SideBySideInstallation.GetMutexName(Root));
        using var check = Process.Start(new ProcessStartInfo(launcher)
        { UseShellExecute = false, ArgumentList = { "--silo-check-uninstall" } })!;
        Assert.True(check.WaitForExit(15000));
        Assert.Equal(3, check.ExitCode);
        Assert.Equal(state, File.ReadAllBytes(statePath));
    }

    [Fact]
    public void ActualLauncherInitializesDispatchesArgumentsAndUninstallRemovesTheVersionFolders()
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
        File.WriteAllText(Path.Combine(target, "foreign.txt"), "created next to the client");
        // A test layout is never the registered installation, so its run never touches profile data.
        RunLauncher(launcher, ["--silo-uninstall-owned"]);
        Assert.False(Directory.Exists(Path.Combine(Root, "versions")));
        Assert.False(Directory.Exists(Path.Combine(Root, ".silo-update")));
        // The root bootstrap stays for Inno, which removes its own logged files next.
        Assert.True(File.Exists(launcher));
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ChangedBootstrapIsRejectedBeforeCreatingTransaction(int bootstrapProtocol)
    {
        Initialize();
        File.WriteAllText(Path.Combine(Root, "first.dll"), "old first");
        File.WriteAllText(Path.Combine(Root, "second.dll"), "old second");
        var error = Assert.Throws<InvalidDataException>(() => SideBySideInstallation.Stage(Root,
            Package("1.0.0.2", true, bootstrapProtocol: bootstrapProtocol), _ => { }, default));
        Assert.Contains("full installer migration", error.Message);
        Assert.Equal("old first", File.ReadAllText(Path.Combine(Root, "first.dll")));
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.False(File.Exists(Path.Combine(Root, ".silo-update", "transaction.json")));
    }

    [Fact]
    public void FullInstallerCanMigrateBootstrapToProtocol2AndRetainProtocol1RecoveryInventory()
    {
        Initialize();
        var previous = SideBySideInstallation.ResolveClientExecutable(Root);
        var package = Package("1.0.0.2", true, bootstrapProtocol: 2);
        // Only the full installer may replace the root bootstrap and copy the
        // new version before initialization. Incremental Stage cannot do this.
        var target = Path.Combine(Root, "versions", package.Manifest.Version);
        Directory.CreateDirectory(target);
        foreach (var file in package.Manifest.Files)
            File.Copy(Path.Combine(package.Directory, file.Area, file.Path),
                Path.Combine(file.Area == "client" ? target : Root, file.Path));

        SideBySideInstallation.InitializeFromInstaller(Root, package.Manifest);

        Assert.Equal("new first", File.ReadAllText(Path.Combine(Root, "first.dll")));
        Assert.Equal(Path.Combine(target, "SiloPlayer.exe"), SideBySideInstallation.ResolveClientExecutable(Root));
        Assert.Equal("1.0.0.1", SideBySideInstallation.ReadState(Root)!.PreviousVersion);
        Assert.True(File.Exists(previous));
        var staged = SideBySideInstallation.Stage(Root, Package("1.0.0.3", true, bootstrapProtocol: 2), _ => { }, default);
        SideBySideInstallation.Activate(staged);
        SideBySideInstallation.Rollback(Root, staged.TransactionId);
        Assert.Equal(Path.Combine(target, "SiloPlayer.exe"), SideBySideInstallation.ResolveClientExecutable(Root));
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

    private void Initialize(string? root = null)
    {
        root ??= Root;
        var package = Package("1.0.0.1");
        var target = Path.Combine(root, "versions", "1.0.0.1");
        Directory.CreateDirectory(target);
        foreach (var file in package.Manifest.Files.Where(f => f.Area == "client"))
            File.Copy(Path.Combine(package.Directory, "client", file.Path), Path.Combine(target, file.Path));
        SideBySideInstallation.Initialize(root, package.Manifest);
    }

    private PreparedUpdatePackage Package(string version, bool bootstrap = false, bool realClient = false, int bootstrapProtocol = 1)
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
        var manifest = new UpdateManifest(1, version, offset, new string('A', 64), entries, BootstrapProtocol: bootstrapProtocol);
        var manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
        return new(directory, manifestPath, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))), manifest);
    }

    public void Dispose() { try { Directory.Delete(sandbox, true); } catch { } }
}
