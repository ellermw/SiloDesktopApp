using System.Runtime.InteropServices;

/// <summary>
/// The process memory WinUI's own collection trigger reads: CCoreServices::TrackMemory compares
/// CWindowsServices::GetMemoryCount, which is GetProcessMemoryInfo's WorkingSetSize (microsoft-ui-xaml
/// release/1.8.12 PlatformServices.cpp; the shipped Microsoft.UI.Xaml.dll 3.1.8.2608 imports
/// K32GetProcessMemoryInfo). Read without allocating, so it can run on every frame.
/// </summary>
internal static class ScrollProcessMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Counters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out Counters counters, uint size);

    /// <summary>Working set and private bytes, in bytes.</summary>
    internal static (long WorkingSet, long Private) Read()
    {
        if (!GetProcessMemoryInfo(new IntPtr(-1), out var counters, (uint)Marshal.SizeOf<Counters>())) return (0, 0);
        return ((long)counters.WorkingSetSize, (long)counters.PrivateUsage);
    }

    /// <summary>Logs the process memory at a named point between measured segments.</summary>
    internal static void Mark(string when)
    {
        var (ws, priv) = Read();
        Program.Log($"INFO: scroll memory mark {when}: working set {ws / 1048576.0:0} MB, private {priv / 1048576.0:0} MB, managed heap {GC.GetTotalMemory(false) / 1048576.0:0} MB, gen2 collections {GC.CollectionCount(2)}, full blocking {GC.GetGCMemoryInfo(GCKind.FullBlocking).Index}, still-page checks {ScrollSmoothnessNativeFixture.FrameRecorder.StillChecks()}");
    }
}
