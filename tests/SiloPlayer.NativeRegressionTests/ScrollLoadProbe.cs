using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// What this host process costs while a scroll segment runs, beyond the UI thread's frame
/// interval: CPU per thread (the composition render thread is not the UI thread) and this
/// process's own GPU engine utilization per adapter (Windows "GPU Engine" counters, read for
/// this process id only). Adapter names come from DXGI so a LUID can be told apart.
/// </summary>
internal sealed class ScrollLoadProbe : IDisposable
{
    private readonly int _pid = Environment.ProcessId;
    private readonly uint _uiThread = GetCurrentThreadId();
    private Dictionary<int, TimeSpan> _cpu = new();
    private long _started;
    private IntPtr _query, _counter;
    private static readonly Dictionary<string, string> Adapters = ReadAdapters();

    public ScrollLoadProbe()
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) == 0)
                PdhAddEnglishCounterW(_query, $@"\GPU Engine(pid_{_pid}_*)\Utilization Percentage", IntPtr.Zero, out _counter);
        }
        catch (Exception) { _query = IntPtr.Zero; }
    }

    public static string AdapterSummary => string.Join("; ", Adapters.Select(pair => $"{pair.Value} [{pair.Key}]"));

    public void Begin()
    {
        _cpu = ThreadTimes();
        _started = Stopwatch.GetTimestamp();
        if (_query != IntPtr.Zero) PdhCollectQueryData(_query);
    }

    public string End()
    {
        var wall = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        var now = ThreadTimes();
        var deltas = now.Select(pair => (Id: pair.Key, Ms: (pair.Value - _cpu.GetValueOrDefault(pair.Key)).TotalMilliseconds))
            .Where(entry => entry.Ms > 0).OrderByDescending(entry => entry.Ms).ToList();
        var ui = deltas.FirstOrDefault(entry => entry.Id == _uiThread).Ms;
        var busiest = deltas.Where(entry => entry.Id != _uiThread).Take(3).Select(entry => $"{entry.Ms * 100 / wall:0}%");
        var text = new StringBuilder($"cpu process={deltas.Sum(entry => entry.Ms) * 100 / wall:0}% ui={ui * 100 / wall:0}% busiestOther=[{string.Join(",", busiest)}]");
        if (_query != IntPtr.Zero && _counter != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            var engines = ReadEngines();
            var byAdapter = engines.GroupBy(engine => (Luid: Luid(engine.Name), Type: EngineType(engine.Name)))
                .Select(group => (group.Key.Luid, group.Key.Type, Value: group.Max(engine => engine.Value)))
                .Where(entry => entry.Value >= 0.5).OrderByDescending(entry => entry.Value)
                .Select(entry => $"{Adapters.GetValueOrDefault(entry.Luid, entry.Luid)} {entry.Type}={entry.Value:0}%");
            text.Append(" gpu=[").Append(string.Join(", ", byAdapter)).Append(']');
        }
        return text.ToString();
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    private static Dictionary<int, TimeSpan> ThreadTimes()
    {
        var result = new Dictionary<int, TimeSpan>();
        using var process = Process.GetCurrentProcess();
        foreach (ProcessThread thread in process.Threads)
        {
            try { result[thread.Id] = thread.TotalProcessorTime; }
            catch (Exception) { }
            finally { thread.Dispose(); }
        }
        return result;
    }

    private List<(string Name, double Value)> ReadEngines()
    {
        var engines = new List<(string, double)>();
        uint size = 0;
        const uint PdhFmtDouble = 0x00000200, PdhFmtNoCap100 = 0x00008000, PdhMoreData = 0x800007D2;
        var status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out var count, IntPtr.Zero);
        if (status != unchecked((int)PdhMoreData) || size == 0) return engines;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out count, buffer) != 0) return engines;
            for (var i = 0; i < count; i++)
            {
                // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then { CStatus, padding, double }.
                var item = buffer + i * 24;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + 16));
                engines.Add((name, value));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return engines;
    }

    // pid_1234_luid_0x00000000_0x0000D1A3_phys_0_eng_0_engtype_3D
    private static string Luid(string instance)
    {
        var at = instance.IndexOf("luid_", StringComparison.Ordinal);
        if (at < 0) return "?";
        var parts = instance[(at + 5)..].Split('_');
        return parts.Length >= 2 ? $"{parts[0]}_{parts[1]}".ToLowerInvariant() : "?";
    }

    private static string EngineType(string instance)
    {
        var at = instance.IndexOf("engtype_", StringComparison.Ordinal);
        return at < 0 ? "?" : instance[(at + 8)..];
    }

    private static Dictionary<string, string> ReadAdapters()
    {
        var adapters = new Dictionary<string, string>();
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out var factory) != 0) return adapters;
            try
            {
                for (uint index = 0; ; index++)
                {
                    // IDXGIFactory1::EnumAdapters1 is vtable slot 12.
                    var enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdapters1>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
                    if (enumAdapters(factory, index, out var adapter) != 0) break;
                    try
                    {
                        // IDXGIAdapter1::GetDesc1 is vtable slot 10.
                        var getDesc = Marshal.GetDelegateForFunctionPointer<GetDesc1>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                        var desc = Marshal.AllocHGlobal(512);
                        try
                        {
                            if (getDesc(adapter, desc) != 0) continue;
                            var description = Marshal.PtrToStringUni(desc, 128).TrimEnd('\0');
                            // DXGI_ADAPTER_DESC1: Description[128] (256 bytes), 4 UINTs, 3 SIZE_Ts, LUID { Low, High }.
                            var luidOffset = 256 + 16 + 3 * IntPtr.Size;
                            var low = (uint)Marshal.ReadInt32(desc, luidOffset);
                            var high = (uint)Marshal.ReadInt32(desc, luidOffset + 4);
                            adapters[$"0x{high:x8}_0x{low:x8}"] = $"adapter{index}:{description}";
                        }
                        finally { Marshal.FreeHGlobal(desc); }
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            finally { Marshal.Release(factory); }
        }
        catch (Exception) { }
        return adapters;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1(IntPtr self, IntPtr desc);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
}
