using System.Diagnostics;

/// <summary>
/// What a full collection has to walk, counted out of process: SILO_SCROLL_CENSUS names a census tool
/// (ClrMD on a PssCaptureSnapshot copy of this process) that lists GC handles by kind and by the type of the
/// object each holds, and managed objects by type. A native object wrapper (RCW) of a XAML object is a
/// projection object (Microsoft.UI.Xaml.*); a managed object XAML holds (delegate, managed control) has a
/// reference-counted handle; each reference the XAML reference-tracker walk finds becomes a dependent handle.
/// </summary>
internal static class ScrollHeapCensus
{
    private static readonly string? Tool = Environment.GetEnvironmentVariable("SILO_SCROLL_CENSUS") is { Length: > 0 } path && File.Exists(path) ? path : null;

    internal static void Take(string when, string? rootsOf = null)
    {
        if (Tool == null) return;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var output = Path.GetFullPath(Path.Combine(Program.ResultDirectory, "scroll-heap-census.txt"));
        using var census = Process.Start(new ProcessStartInfo(Tool)
        {
            ArgumentList = { Environment.ProcessId.ToString(), when, output, rootsOf ?? "" },
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        })!;
        var line = census.StandardOutput.ReadLine();
        if (!census.WaitForExit(180_000)) { census.Kill(); line = "timed out"; }
        Program.Log($"INFO: scroll heap census {when}: {line?.TrimStart('=', ' ')}");
    }
}
