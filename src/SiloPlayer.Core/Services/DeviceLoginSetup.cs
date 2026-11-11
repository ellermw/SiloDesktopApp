using System.Security.Cryptography;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Owns one phone sign-in setup. Its secret stays in memory and is shared by all
/// requests/retries in this setup; replacing or finishing the setup disposes it.
/// </summary>
public sealed class DeviceLoginSetup(AuthApi api, string server, Func<bool>? isCurrent = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
    private Task<bool>? _negotiation;
    private bool _disposed;

    public async Task<DeviceLoginStartResponse> StartAsync(string deviceName, string devicePlatform, CancellationToken ct = default)
    {
        Task<bool> negotiation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            negotiation = _negotiation ??= NegotiateAsync(ct);
        }
        var bound = await negotiation;
        Task<DeviceLoginStartResponse> started;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false) throw new OperationCanceledException("Device sign-in setup changed.", ct);
            started = api.DeviceStartAsync(server, deviceName, devicePlatform, bound ? Encode(SHA256.HashData(_secret)) : null, ct);
        }
        return await started;
    }

    public async Task<DeviceLoginPollResponse> PollAsync(string deviceCode, CancellationToken ct = default)
    {
        Task<bool> negotiation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            negotiation = _negotiation ?? throw new InvalidOperationException("Start device sign-in before polling.");
        }
        var bound = await negotiation;
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

    private async Task<bool> NegotiateAsync(CancellationToken ct)
    {
        bool bound;
        try { bound = (await api.GetDeviceLoginCapabilityAsync(server, ct)).SessionBinding; }
        // A missing capability route predates binding. Other failures are not an
        // unsupported-capability answer and must not silently start unbound.
        catch (ApiException ex) when (ex.StatusCode is 404 or 405) { bound = false; }
        if (bound && (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Phone sign-in session binding requires HTTPS.");
        return bound;
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
