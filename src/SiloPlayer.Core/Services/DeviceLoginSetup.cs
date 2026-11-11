using System.Security.Cryptography;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

/// <summary>How the server answered the binding negotiation of one phone sign-in setup.</summary>
public enum DeviceLoginBinding
{
    /// <summary>The server advertised binding; the start and every poll carry the setup secret.</summary>
    Bound,
    /// <summary>The server answered that it does not bind setups.</summary>
    Declined,
    /// <summary>The answer does not mention binding, or the server has no capability route.</summary>
    Unsupported,
}

/// <summary>
/// Owns one phone sign-in setup. Its secret stays in memory and is shared by all
/// requests/retries in this setup; replacing or finishing the setup disposes it.
/// </summary>
public sealed class DeviceLoginSetup(AuthApi api, string server, Func<bool>? isCurrent = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
    private Task<DeviceLoginBinding>? _negotiation;
    private bool _disposed;

    /// <summary>
    /// The negotiated answer, or null until a start has negotiated one. Anything but
    /// <see cref="DeviceLoginBinding.Bound"/> runs unbound; the caller must tell the
    /// user so instead of letting the missing setup protection pass silently.
    /// </summary>
    public DeviceLoginBinding? Binding => _negotiation is { IsCompletedSuccessfully: true } negotiated ? negotiated.Result : null;

    public async Task<DeviceLoginStartResponse> StartAsync(string deviceName, string devicePlatform, CancellationToken ct = default)
    {
        Task<DeviceLoginBinding> negotiation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            negotiation = _negotiation ??= NegotiateAsync(ct);
        }
        var bound = await negotiation == DeviceLoginBinding.Bound;
        Task<DeviceLoginStartResponse> started;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false) throw new OperationCanceledException("Device sign-in setup changed.", ct);
            started = api.DeviceStartAsync(server, deviceName, devicePlatform, bound ? Encode(SHA256.HashData(_secret)) : null, ct);
        }
        try { return await started; }
        // A server that advertised binding yet refuses the bound start as invalid
        // does not accept this setup. Report that distinctly; never retry unbound.
        catch (ApiException ex) when (bound && ex.StatusCode is 400 or 422)
        { throw new ApiException("device_binding_rejected", "The server did not accept this device sign-in setup.", ex.StatusCode, retryAfterSeconds: ex.RetryAfterSeconds); }
    }

    public async Task<DeviceLoginPollResponse> PollAsync(string deviceCode, CancellationToken ct = default)
    {
        Task<DeviceLoginBinding> negotiation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            negotiation = _negotiation ?? throw new InvalidOperationException("Start device sign-in before polling.");
        }
        var bound = await negotiation == DeviceLoginBinding.Bound;
        Task<DeviceLoginPollResponse> polled;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false) throw new OperationCanceledException("Device sign-in setup changed.", ct);
            polled = api.DevicePollAsync(server, deviceCode, bound ? Encode(_secret) : null, ct);
        }
        return await polled;
    }

    private async Task<DeviceLoginBinding> NegotiateAsync(CancellationToken ct)
    {
        DeviceLoginBinding binding;
        try
        {
            // An answer without the flag is not an explicit refusal. Both run
            // unbound, but they stay distinguishable and are reported to the caller.
            binding = (await api.GetDeviceLoginCapabilityAsync(server, ct)).SessionBinding switch
            { true => DeviceLoginBinding.Bound, false => DeviceLoginBinding.Declined, _ => DeviceLoginBinding.Unsupported };
        }
        // A missing capability route predates binding. Other failures are not an
        // unsupported-capability answer and must not silently start unbound.
        catch (ApiException ex) when (ex.StatusCode is 404 or 405) { binding = DeviceLoginBinding.Unsupported; }
        // Preserve phone sign-in on configured HTTP servers too: binding adds
        // setup ownership, while transport security remains the server's policy.
        return binding;
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CryptographicOperations.ZeroMemory(_secret);
        }
    }
}
