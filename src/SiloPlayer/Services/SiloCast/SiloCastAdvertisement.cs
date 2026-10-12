using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace SiloPlayer.Services.SiloCast;

/// <summary>Per-interface Windows mDNS publication. Restart to update TXT or reconcile networks.</summary>
public sealed class SiloCastAdvertisement : IAsyncDisposable
{
    private readonly ISiloCastDnsApi _native;
    private readonly Func<IReadOnlyList<SiloCastNetworkInterface>> _interfaces;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _state = new();
    private readonly List<Registration> _registrations = [];
    private CancellationTokenSource? _pendingStart;
    private bool _disposed;
    private Task _drainingCleanup = Task.CompletedTask;

    public SiloCastAdvertisement() : this(new SiloCastDnsApi(), SiloCastNetwork.GetEligibleInterfaces) { }
    internal SiloCastAdvertisement(ISiloCastDnsApi native, Func<IReadOnlyList<SiloCastNetworkInterface>> interfaces)
    { _native = native; _interfaces = interfaces; }

    public Task StartAsync(int port, IReadOnlyDictionary<string, string> txt, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentNullException.ThrowIfNull(txt);
        var properties = new Dictionary<string, string>(txt, StringComparer.Ordinal);
        foreach (var pair in properties)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Any(c => c is < (char)0x20 or > (char)0x7e or '=') ||
                pair.Value is null || pair.Value.Contains('\0') || Encoding.UTF8.GetByteCount(pair.Key + "=" + pair.Value) > 255)
                throw new ArgumentException("SiloCast TXT properties must be valid DNS-SD entries of at most 255 UTF-8 bytes.", nameof(txt));
        }
        CancellationTokenSource start;
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pendingStart?.Cancel();
            start = CancellationTokenSource.CreateLinkedTokenSource(ct);
            start.CancelAfter(TimeSpan.FromSeconds(15));
            _pendingStart = start;
        }
        // NLM enumeration and DNS construction must never execute on the WinUI dispatcher.
        return Task.Run(() => StartCoreAsync((ushort)port, properties, start));
    }

    private async Task StartCoreAsync(ushort port, IReadOnlyDictionary<string, string> txt, CancellationTokenSource start)
    {
        bool entered = false;
        try
        {
            await _gate.WaitAsync(start.Token).ConfigureAwait(false);
            entered = true;
            await WithdrawAsync().ConfigureAwait(false);
            await _drainingCleanup.WaitAsync(start.Token).ConfigureAwait(false);
            start.Token.ThrowIfCancellationRequested();
            var interfaces = _interfaces();
            if (interfaces.Count == 0)
                throw new InvalidOperationException("SiloCast needs an active physical Wi-Fi or Ethernet connection marked Private in Windows network settings.");
            var machine = Environment.MachineName;
            // A DNS label has a 63-octet limit. Windows machine names are normally limited to 15.
            var label = new string(machine.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(63).ToArray());
            if (label.Length == 0) label = "Silo-Windows";
            foreach (var network in interfaces)
            {
                start.Token.ThrowIfCancellationRequested();
                var registration = new Registration(_native, network, label, port, txt);
                _registrations.Add(registration);
                await registration.RegisterAsync(start.Token).ConfigureAwait(false);
            }
            start.Token.ThrowIfCancellationRequested();
        }
        catch
        {
            if (entered) await WithdrawAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (entered) _gate.Release();
            lock (_state)
            {
                if (ReferenceEquals(_pendingStart, start)) _pendingStart = null;
                start.Dispose();
            }
        }
    }

    public Task StopAsync()
    {
        lock (_state) _pendingStart?.Cancel();
        return Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await WithdrawAsync().ConfigureAwait(false); }
            finally { _gate.Release(); }
        });
    }

    private async Task WithdrawAsync()
    {
        var registrations = _registrations.ToArray();
        _registrations.Clear();
        var cleanup = Task.WhenAll(registrations.Select(r => r.WithdrawAsync()).Prepend(_drainingCleanup));
        _drainingCleanup = cleanup;
        // A DNS service that fails to deliver its cancellation callback cannot block app shutdown.
        // The rooted registration/cleanup task retains all native memory until that callback arrives.
        try
        {
            await cleanup.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            _drainingCleanup = Task.CompletedTask;
        }
        catch (TimeoutException) { _ = ObserveCleanupAsync(cleanup); }
    }

    private static async Task ObserveCleanupAsync(Task cleanup)
    {
        try { await cleanup.ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SiloCast DNS withdrawal: {ex.GetType().Name}"); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_state)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingStart?.Cancel();
        }
        await StopAsync().ConfigureAwait(false);
        // Keep the semaphore alive: already-queued StopAsync callers can still be draining.
    }

    private sealed class Registration
    {
        private static readonly SiloCastDnsComplete CompletionCallback = Complete;
        private readonly ISiloCastDnsApi _native;
        private readonly object _cancelGate = new();
        private readonly IntPtr _instance;
        private readonly IntPtr _request;
        private readonly IntPtr _cancel;
        private GCHandle _root;
        private TaskCompletionSource<uint> _completion = NewCompletion();
        private CancellationTokenRegistration _cancellation;
        private Task? _withdrawal;
        private Task _cancelTask = Task.CompletedTask;
        private bool _started;

        internal Registration(ISiloCastDnsApi native, SiloCastNetworkInterface network, string name, ushort port, IReadOnlyDictionary<string,string> txt)
        {
            _native = native;
            _instance = native.Construct(name + "._silocast._tcp.local", name + ".local",
                network.Addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork),
                network.Addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6), port, txt);
            if (_instance == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not construct the SiloCast service.");
            try
            {
                _root = GCHandle.Alloc(this);
                _cancel = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(_cancel, IntPtr.Zero);
                _request = Marshal.AllocHGlobal(Marshal.SizeOf<SiloCastDnsRequest>());
                Marshal.StructureToPtr(new SiloCastDnsRequest
                {
                    Version = 1, InterfaceIndex = network.Index, Instance = _instance,
                    Callback = Marshal.GetFunctionPointerForDelegate(CompletionCallback), Context = GCHandle.ToIntPtr(_root),
                    UnicastEnabled = false
                }, _request, false);
            }
            catch
            {
                if (_request != IntPtr.Zero) Marshal.FreeHGlobal(_request);
                if (_cancel != IntPtr.Zero) Marshal.FreeHGlobal(_cancel);
                if (_root.IsAllocated) _root.Free();
                native.Free(_instance);
                throw;
            }
        }

        internal async Task RegisterAsync(CancellationToken token)
        {
            _started = true;
            uint result;
            try { result = _native.Register(_request, _cancel); }
            catch { _completion.TrySetResult(1); throw; }
            if (result != 9506) _completion.TrySetResult(result);
            _cancellation = token.Register(Cancel);
            uint status = await _completion.Task.WaitAsync(token).ConfigureAwait(false);
            if (status != 0) throw new Win32Exception((int)status, "Windows could not advertise SiloCast on the local network.");
        }

        private void Cancel()
        {
            lock (_cancelGate)
            {
                _cancelTask = Task.Run(() =>
                {
                    lock (_cancelGate)
                    {
                        if (!_completion.Task.IsCompleted) _native.Cancel(_cancel);
                    }
                });
            }
        }

        internal Task WithdrawAsync() => _withdrawal ??= WithdrawCoreAsync();

        private async Task WithdrawCoreAsync()
        {
            if (_started)
            {
                // Dispose waits for an in-flight cancellation call before we eventually free pCancel.
                _cancellation.Dispose();
                await _cancelTask.ConfigureAwait(false);
                uint registered = await _completion.Task.ConfigureAwait(false);
                if (registered == 0)
                {
                    _completion = NewCompletion();
                    uint result;
                    try { result = _native.Deregister(_request); }
                    catch { _completion.TrySetResult(1); throw; }
                    if (result != 9506) _completion.TrySetResult(result);
                    uint status = await _completion.Task.ConfigureAwait(false);
                    if (status != 0)
                    {
                        // Do not free a request still owned by an advertisement. Windows removes it
                        // at process exit; retaining the root is safer than a native use-after-free.
                        throw new Win32Exception((int)status, "Windows could not withdraw the SiloCast advertisement.");
                    }
                }
            }
            lock (_cancelGate)
            {
                _native.Free(_instance);
                Marshal.FreeHGlobal(_request);
                Marshal.FreeHGlobal(_cancel);
                _root.Free();
            }
        }

        private static TaskCompletionSource<uint> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static void Complete(uint status, IntPtr context, IntPtr instance)
        {
            // Never let a managed exception escape across the native callback boundary.
            Registration? registration = null;
            try
            {
                registration = (Registration?)GCHandle.FromIntPtr(context).Target;
                if (registration is null) return;
                if (instance != IntPtr.Zero && instance != registration._instance) registration._native.Free(instance);
                registration._completion.TrySetResult(status);
            }
            catch (Exception) { registration?._completion.TrySetResult(1); }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SiloCastDnsRequest
{
    public uint Version;
    public uint InterfaceIndex;
    public IntPtr Instance;
    public IntPtr Callback;
    public IntPtr Context;
    public IntPtr Credentials;
    [MarshalAs(UnmanagedType.Bool)] public bool UnicastEnabled;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void SiloCastDnsComplete(uint status, IntPtr context, IntPtr instance);

internal interface ISiloCastDnsApi
{
    IntPtr Construct(string service, string host, IPAddress? ipv4, IPAddress? ipv6, ushort port, IReadOnlyDictionary<string,string> txt);
    uint Register(IntPtr request, IntPtr cancel);
    uint Deregister(IntPtr request);
    uint Cancel(IntPtr cancel);
    void Free(IntPtr instance);
}

internal sealed class SiloCastDnsApi : ISiloCastDnsApi
{
    public IntPtr Construct(string service, string host, IPAddress? ipv4, IPAddress? ipv6, ushort port, IReadOnlyDictionary<string,string> txt)
    {
        var allocated = new List<IntPtr>();
        IntPtr Bytes(byte[] bytes)
        {
            var pointer = Marshal.AllocHGlobal(bytes.Length); allocated.Add(pointer); Marshal.Copy(bytes, 0, pointer, bytes.Length); return pointer;
        }
        IntPtr Strings(IEnumerable<string> strings)
        {
            var values = strings.Select(s => { var p = Marshal.StringToHGlobalUni(s); allocated.Add(p); return p; }).ToArray();
            if (values.Length == 0) return IntPtr.Zero;
            var pointer = Marshal.AllocHGlobal(values.Length * IntPtr.Size); allocated.Add(pointer); Marshal.Copy(values, 0, pointer, values.Length); return pointer;
        }
        try
        {
            return DnsServiceConstructInstance(service, host,
                ipv4 is null ? IntPtr.Zero : Bytes(ipv4.GetAddressBytes()),
                ipv6 is null ? IntPtr.Zero : Bytes(ipv6.GetAddressBytes()), port, 0, 0, (uint)txt.Count,
                Strings(txt.Keys), Strings(txt.Values));
        }
        finally { foreach (var pointer in allocated) Marshal.FreeHGlobal(pointer); }
    }
    public uint Register(IntPtr request, IntPtr cancel) => DnsServiceRegister(request, cancel);
    public uint Deregister(IntPtr request) => DnsServiceDeRegister(request, IntPtr.Zero);
    public uint Cancel(IntPtr cancel) => DnsServiceRegisterCancel(cancel);
    public void Free(IntPtr instance) => DnsServiceFreeInstance(instance);

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr DnsServiceConstructInstance(string service, string host, IntPtr ip4, IntPtr ip6, ushort port, ushort priority, ushort weight, uint count, IntPtr keys, IntPtr values);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern uint DnsServiceRegisterCancel(IntPtr cancel);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern void DnsServiceFreeInstance(IntPtr instance);
}
