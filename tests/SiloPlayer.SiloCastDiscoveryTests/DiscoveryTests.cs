using System.Net;
using System.Runtime.InteropServices;
using SiloPlayer.Services.SiloCast;

public class DiscoveryTests
{
    private static readonly IReadOnlyDictionary<string,string> Txt = new Dictionary<string,string> { ["v"]="2", ["name"]="Living Room", ["id"]="stable-device", ["server"]="c2VydmVy", ["serverName"]="Silo", ["playing"]="0" };
    private static IReadOnlyList<SiloCastNetworkInterface> Interfaces() => [new(7, [IPAddress.Parse("192.168.1.42")])];

    [Fact]
    public async Task RegistersMulticastOnSelectedInterfaceAndWithdrawsBeforeFreeingOriginalInstance()
    {
        var native = new FakeDns();
        await using var advertiser = new SiloCastAdvertisement(native, Interfaces);
        await advertiser.StartAsync(18443, Txt, default);
        Assert.Equal(7u, native.Requests[0].InterfaceIndex);
        Assert.False(native.Requests[0].UnicastEnabled);
        Assert.EndsWith("._silocast._tcp.local", native.ServiceName);
        Assert.Equal((ushort)18443, native.Port);
        Assert.Equal("stable-device", native.Txt!["id"]);
        Assert.Equal(1, native.LiveInstances);
        await advertiser.StopAsync();
        Assert.Equal(1, native.Deregistrations);
        Assert.Equal(0, native.LiveInstances);
    }

    [Fact]
    public async Task CancellationRetainsNativeRequestUntilLateCallbackAndWithdrawsLateSuccess()
    {
        var native = new FakeDns { DelayRegister = true };
        await using var advertiser = new SiloCastAdvertisement(native, Interfaces);
        using var cancellation = new CancellationTokenSource();
        var start = advertiser.StartAsync(18443, Txt, cancellation.Token);
        await native.Registered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(1, native.Cancellations);
        Assert.Equal(1, native.LiveInstances);
        GC.Collect(); GC.WaitForPendingFinalizers();
        native.CompleteRegister(0);
        await native.Withdrawn.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await advertiser.StopAsync();
        Assert.Equal(0, native.LiveInstances);
    }

    [Fact]
    public async Task NewGenerationWaitsForCanceledNativeRegistrationToWithdraw()
    {
        var native = new FakeDns { DelayRegister = true };
        await using var advertiser = new SiloCastAdvertisement(native, Interfaces);
        using var cancellation = new CancellationTokenSource();
        var oldStart = advertiser.StartAsync(18443, Txt, cancellation.Token);
        await native.Registered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldStart);
        var newStart = advertiser.StartAsync(18444, Txt, default);
        try
        {
            await Task.Delay(100);
            Assert.Single(native.Requests);
        }
        finally
        {
            native.CompleteRegisterAt(0,0);
            for (int i=0; native.Requests.Count<2 && i<100; i++) await Task.Delay(10);
            native.CompleteRegisterAt(1,0);
            await newStart;
        }
    }

    [Fact]
    public async Task FailedRegistrationFreesRequestAndDoesNotDeregister()
    {
        var native = new FakeDns { RegisterStatus = 5 };
        await using var advertiser = new SiloCastAdvertisement(native, Interfaces);
        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => advertiser.StartAsync(18443, Txt, default));
        Assert.Equal(0, native.LiveInstances);
        Assert.Equal(0, native.Deregistrations);
    }

    [Fact]
    public async Task AsyncCallbackFailureFreesBothCallbackCopyAndOriginalInstance()
    {
        var native = new FakeDns { DelayRegister = true };
        await using var advertiser = new SiloCastAdvertisement(native, Interfaces);
        var start = advertiser.StartAsync(18443, Txt, default);
        await native.Registered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        native.CompleteRegister(5);
        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => start);
        Assert.Equal(0, native.LiveInstances);
        Assert.Equal(0, native.Deregistrations);
    }

    [Fact]
    public async Task RestartWithdrawsOldInterfaceBeforeAdvertisingNewNetwork()
    {
        var native = new FakeDns();
        uint index = 7;
        await using var advertiser = new SiloCastAdvertisement(native, () => [new(index, [IPAddress.Parse("192.168.1.42")])]);
        await advertiser.StartAsync(18443, Txt, default);
        index = 12;
        await advertiser.StartAsync(18443, Txt, default);
        Assert.Equal(1, native.Deregistrations);
        Assert.Equal(12u, native.Requests[^1].InterfaceIndex);
        Assert.Equal(1, native.LiveInstances);
    }

    [Theory]
    [InlineData(6u, 0u, (byte)1, true, true, true)]
    [InlineData(71u, 0u, (byte)1, true, true, true)]
    [InlineData(6u, 0u, (byte)0, true, true, false)]
    [InlineData(6u, 0u, (byte)1, false, true, false)]
    [InlineData(6u, 0u, (byte)1, true, false, false)]
    [InlineData(131u, 2u, (byte)1, true, true, false)]
    [InlineData(6u, 0u, (byte)3, true, true, false)]
    public void EligibleNetworkRequiresPrivateActivePhysicalEthernetOrWifi(uint type,uint tunnel,byte flags,bool isPrivate,bool up,bool expected) =>
        Assert.Equal(expected, SiloCastNetwork.IsEligible(type,tunnel,flags,isPrivate,up));

    [Fact]
    public async Task NoEligibleNetworkReportsActionableFailure()
    {
        await using var advertiser = new SiloCastAdvertisement(new FakeDns(), () => []);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => advertiser.StartAsync(18443, Txt, default));
        Assert.Contains("Private", error.Message);
    }

    [Fact]
    public void NativeNetworkEnumerationReturnsOnlyPhysicalLocalAddresses()
    {
        var networks = SiloCastNetwork.GetEligibleInterfaces();
        Assert.All(networks, network => Assert.All(network.Addresses, address => Assert.False(IPAddress.IsLoopback(address))));
        var selectedAdapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => networks.Any(network => network.Index == (uint)(adapter.GetIPProperties().GetIPv4Properties()?.Index ?? adapter.GetIPProperties().GetIPv6Properties()?.Index ?? 0)));
        Assert.All(selectedAdapters, adapter => Assert.True(adapter.SupportsMulticast));
        if (uint.TryParse(Environment.GetEnvironmentVariable("SILOCAST_NATIVE_INTERFACE_INDEX"),out uint expectedIndex))
            Assert.Contains(networks, network => network.Index == expectedIndex);
        if (uint.TryParse(Environment.GetEnvironmentVariable("SILOCAST_NATIVE_EXCLUDED_INTERFACE_INDEX"),out uint excludedIndex))
            Assert.DoesNotContain(networks, network => network.Index == excludedIndex);
    }
    [Fact]
    public async Task NativeRegistrationAndWithdrawalSmoke()
    {
        if (Environment.GetEnvironmentVariable("SILOCAST_NATIVE_SMOKE") != "1") return;
        await using var advertiser = new SiloCastAdvertisement();
        await advertiser.StartAsync(18443, Txt, default);
        await advertiser.StopAsync();
    }

    private sealed class FakeDns : ISiloCastDnsApi
    {
        public int LiveInstances, Deregistrations, Cancellations;
        public bool DelayRegister;
        public uint RegisterStatus;
        public string? ServiceName;
        public ushort Port;
        public IReadOnlyDictionary<string,string>? Txt;
        public List<SiloCastDnsRequest> Requests { get; } = [];
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Withdrawn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private SiloCastDnsRequest _pending;
        public IntPtr Construct(string service,string host,IPAddress? ipv4,IPAddress? ipv6,ushort port,IReadOnlyDictionary<string,string> txt)
        { ServiceName=service; Port=port; Txt=txt; Interlocked.Increment(ref LiveInstances); return Marshal.AllocHGlobal(16); }
        public uint Register(IntPtr request,IntPtr cancel)
        {
            _pending=Marshal.PtrToStructure<SiloCastDnsRequest>(request); Requests.Add(_pending); Registered.TrySetResult();
            if(RegisterStatus != 0) return RegisterStatus;
            if(!DelayRegister) CompleteRegister(0);
            return 9506;
        }
        public void CompleteRegister(uint status) => Complete(_pending,status);
        public void CompleteRegisterAt(int index,uint status) => Complete(Requests[index],status);
        private void Complete(SiloCastDnsRequest request,uint status)
        {
            Interlocked.Increment(ref LiveInstances);
            var result=Marshal.AllocHGlobal(16);
            Marshal.GetDelegateForFunctionPointer<SiloCastDnsComplete>(request.Callback)(status,request.Context,result);
        }
        public uint Deregister(IntPtr request)
        { Interlocked.Increment(ref Deregistrations); Complete(Marshal.PtrToStructure<SiloCastDnsRequest>(request),0); Withdrawn.TrySetResult(); return 9506; }
        public uint Cancel(IntPtr cancel) { Interlocked.Increment(ref Cancellations); return 0; }
        public void Free(IntPtr instance) { Marshal.FreeHGlobal(instance); Interlocked.Decrement(ref LiveInstances); }
    }
}
