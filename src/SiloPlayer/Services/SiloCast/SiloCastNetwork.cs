using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SiloPlayer.Services.SiloCast;

/// <summary>Fail-closed selection of active, physical, Private Ethernet/Wi-Fi interfaces.</summary>
public static class SiloCastNetwork
{
    public static IReadOnlyList<IPAddress> GetEligibleAddresses() =>
        GetEligibleInterfaces().SelectMany(n => n.Addresses).Distinct().ToArray();

    internal static IReadOnlyList<SiloCastNetworkInterface> GetEligibleInterfaces()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var privateAdapters = GetPrivateAdapterIds();
        var eligible = new List<SiloCastNetworkInterface>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!Guid.TryParse(adapter.Id, out var id) || !privateAdapters.Contains(id) ||
                adapter.OperationalStatus != OperationalStatus.Up || !adapter.SupportsMulticast) continue;
            try
            {
                var properties = adapter.GetIPProperties();
                var index = properties.GetIPv4Properties()?.Index ?? properties.GetIPv6Properties()?.Index ?? 0;
                if (index == 0) continue;
                var row = new InterfaceRow { InterfaceIndex = (uint)index };
                if (GetIfEntry2(ref row) != 0 || !IsEligible(row.Type, row.TunnelType, row.Flags, true, row.OperStatus == 1)) continue;
                var addresses = properties.UnicastAddresses
                    .Where(a => a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred &&
                        a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
                        !IPAddress.IsLoopback(a.Address) && !a.Address.Equals(IPAddress.Any) && !a.Address.Equals(IPAddress.IPv6Any))
                    .Select(a => a.Address).Distinct().ToArray();
                if (addresses.Length > 0) eligible.Add(new((uint)index, addresses));
            }
            catch (NetworkInformationException) { /* Adapter removed during enumeration. */ }
        }
        return eligible;
    }

    internal static bool IsEligible(uint type, uint tunnel, byte flags, bool isPrivate, bool up) =>
        isPrivate && up && type is 6 or 71 && tunnel == 0 && (flags & 1) != 0 && (flags & (2 | 8 | 16 | 32 | 128)) == 0;

    private static HashSet<Guid> GetPrivateAdapterIds()
    {
        var ids = new HashSet<Guid>();
        INetworkListManager? manager = null;
        IEnumNetworkConnections? connections = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), true)!;
            manager = (INetworkListManager)Activator.CreateInstance(type)!;
            connections = manager.GetNetworkConnections();
            var item = new INetworkConnection[1];
            while (connections.Next(1, item, out uint fetched) == 0 && fetched == 1)
            {
                INetwork? network = null;
                try
                {
                    var connection = item[0];
                    network = connection.GetNetwork();
                    // NLM_NETWORK_CATEGORY_PRIVATE=1. Public and domain-authenticated are excluded.
                    if (connection.IsConnected && network.GetCategory() == 1) ids.Add(connection.GetAdapterId());
                }
                finally { Release(network); Release(item[0]); item[0] = null!; }
            }
        }
        catch (COMException) { /* Unknown network trust stays ineligible. */ }
        finally { Release(connections); Release(manager); }
        return ids;
    }

    private static void Release(object? value)
    { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    // Full MIB_IF_ROW2 layout from netioapi.h; fixed-width fields preserve alignment on x86/x64.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceRow
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte Flags;
        public uint OperStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed, ReceiveLinkSpeed;
        public ulong InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos;
        public ulong InUcastOctets, InMulticastOctets, InBroadcastOctets;
        public ulong OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors;
        public ulong OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
    }
    [DllImport("iphlpapi.dll", ExactSpelling = true)] private static extern uint GetIfEntry2(ref InterfaceRow row);

    // These are dual COM interfaces: declaration order follows the SDK vtable, including
    // unused members before GetAdapterId/GetCategory. No type library or COM package is needed.
    [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        [return: MarshalAs(UnmanagedType.Interface)] object GetNetworks(uint flags);
        INetwork GetNetwork(Guid id);
        IEnumNetworkConnections GetNetworkConnections();
        INetworkConnection GetNetworkConnection(Guid id);
        bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        uint GetConnectivity();
    }
    [ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworkConnections
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetNewEnum();
        [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] INetworkConnection[] connections, out uint fetched);
        void Skip(uint count);
        void Reset();
        IEnumNetworkConnections Clone();
    }
    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnection
    {
        INetwork GetNetwork();
        bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        uint GetConnectivity();
        Guid GetConnectionId();
        Guid GetAdapterId();
        uint GetDomainType();
    }
    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        void SetName([MarshalAs(UnmanagedType.BStr)] string name);
        [return: MarshalAs(UnmanagedType.BStr)] string GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
        Guid GetNetworkId();
        uint GetDomainType();
        IEnumNetworkConnections GetNetworkConnections();
        void GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);
        bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        uint GetConnectivity();
        uint GetCategory();
        void SetCategory(uint category);
    }
}

internal sealed record SiloCastNetworkInterface(uint Index, IReadOnlyList<IPAddress> Addresses);
