using System.Diagnostics;
using System.Runtime.InteropServices;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = SideBySideInstallation.CanonicalRoot(AppContext.BaseDirectory);
            if (args.Length == 1 && args[0] == "--silo-initialize") return InitializeInstalledLayout(root);
            using var mutex = new Mutex(false, SideBySideInstallation.GetMutexName(root));
            var owns = false;
            string executable;
            try
            {
                try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
                if (!owns) throw new InvalidOperationException("Silo is applying an update. Please try again shortly.");
                if (args.Length == 1 && args[0] == "--silo-uninstall-owned")
                {
                    SideBySideInstallation.RemoveOwnedVersionsForUninstall(root, IsVersionInUse);
                    return 0;
                }
                // Internal installer verbs are never forwarded to the client as an activation.
                var readinessHandshake = args.Length == 2 && args[0] == "--silo-update-ready" &&
                    System.Text.RegularExpressions.Regex.IsMatch(args[1], "^SiloUpdate-[a-f0-9]{32}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                if (!readinessHandshake && args.Any(a => a.StartsWith("--silo-", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Invalid Silo launcher operation.");
                executable = SideBySideInstallation.ResolveLaunchExecutable(root);
            }
            finally { if (owns) mutex.ReleaseMutex(); }

            // Release before starting the client: its startup gate checks the same mutex.
            // Version directories are immutable, so an active-pointer switch cannot change
            // the selected executable or any of the resources it will load.
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            try
            {
                SideBySideInstallation.BeginLaunch(root, executable);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Silo could not be started.");
                var state = SideBySideInstallation.ReadState(root);
                if (state?.PendingVersion is not null &&
                    Path.GetDirectoryName(executable)!.Equals(Path.Combine(root, "versions", state.PendingVersion), StringComparison.OrdinalIgnoreCase))
                {
                    try { SideBySideInstallation.RecordBoot(root, process); }
                    catch (InvalidOperationException)
                    {
                        if (process.HasExited) SideBySideInstallation.RecordFailedLaunch(root, executable);
                    }
                }
            }
            catch { SideBySideInstallation.RecordFailedLaunch(root, executable); throw; }
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Any(a => a is "--silo-initialize" or "--silo-uninstall-owned")) return 2;
            var message = ex is InvalidOperationException ? ex.Message : "Silo could not verify its installed files. Use the full installer to repair this installation.";
            MessageBox(IntPtr.Zero, message, "Silo Desktop Player", 0x10);
            return 1;
        }
    }

    private static int InitializeInstalledLayout(string root)
    {
        // The legacy updater retains the normal root gate while running Inno.
        // First migration has no side-by-side state/worker yet, so serialize
        // initializers separately and allow only that first atomic publication.
        // Existing-state repair remains excluded by the normal mutation gate.
        using var initialization = new Mutex(false, @"Global\SiloDesktopInitialize-" + SideBySideInstallation.RootHash(root)[..16]);
        var initializationOwned = false;
        try
        {
            try { initializationOwned = initialization.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { initializationOwned = true; }
            if (!initializationOwned) throw new InvalidOperationException("Another Silo installer is initializing this folder.");
            var firstMigration = !File.Exists(Path.Combine(root, ".silo-update", "state.json")) &&
                !File.Exists(Path.Combine(root, ".silo-update", "transaction.json"));
            if (firstMigration)
            {
                InitializeContents(root);
                return 0;
            }
            using var mutation = new Mutex(false, SideBySideInstallation.GetMutexName(root));
            var mutationOwned = false;
            try
            {
                try { mutationOwned = mutation.WaitOne(0); } catch (AbandonedMutexException) { mutationOwned = true; }
                if (!mutationOwned)
                    throw new InvalidOperationException("Silo is applying an update. Please try again shortly.");
                InitializeContents(root);
                return 0;
            }
            finally { if (mutationOwned) mutation.ReleaseMutex(); }
        }
        finally { if (initializationOwned) initialization.ReleaseMutex(); }
    }

    private static void InitializeContents(string root)
    {
        var manifest = UpdateManifestValidation.ReadAsync(Path.Combine(root, ".silo-update", "initial.manifest.json")).GetAwaiter().GetResult();
        SideBySideInstallation.InitializeFromInstaller(root, manifest);
    }

    private static bool IsVersionInUse(string directory)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        foreach (var name in new[] { "SiloPlayer", "SiloPlayer.UpdateHost" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && Path.GetFullPath(process.MainModule?.FileName ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { return true; } // Uncertain identity must defer deletion.
            }
        }
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
