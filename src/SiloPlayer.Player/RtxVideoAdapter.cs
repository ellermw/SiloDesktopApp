using System.Runtime.InteropServices;

namespace SiloPlayer.Player;

/// <summary>DXGI inventory includes the discrete GPU on hybrid laptops even
/// when it has no monitor attached. Inventory is a candidate check, not proof
/// that the NVIDIA driver has enabled its video enhancement.</summary>
public static class RtxVideoAdapter
{
    private static readonly Lazy<IReadOnlyList<VideoAdapterInfo>> Inventory = new(FindAdapters);
    public static IReadOnlyList<VideoAdapterInfo> Adapters => Inventory.Value;
    public static string? Name => VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Nvidia, Adapters).Adapter?.Name;

    private static IReadOnlyList<VideoAdapterInfo> FindAdapters()
    {
        var results = new List<VideoAdapterInfo>();
        if (!OperatingSystem.IsWindows()) return results;
        IntPtr factory = IntPtr.Zero;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out factory) < 0) return results;
            var enumerate = Method<EnumAdaptersDelegate>(factory, 12);
            for (uint index = 0; index < 32; index++)
            {
                if (enumerate(factory, index, out var adapter) < 0) break;
                try
                {
                    if (Method<GetDescDelegate>(adapter, 10)(adapter, out var desc) >= 0)
                        results.Add(new(desc.Description, desc.VendorId, (desc.Flags & 2) != 0));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch (Exception) { /* Optional enhancement must never prevent playback. */ }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
        return results;
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersDelegate(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescDelegate(IntPtr adapter, out AdapterDescription description);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSystemId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }
}
