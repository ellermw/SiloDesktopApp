using System.Net;
using System.Security.Cryptography;

namespace SiloPlayer.Core.Api;

public sealed class OverlayLibraryImage
{
    public string Ref { get; set; } = "";
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}

public partial class SiloApiClient
{
    public const int MaxOverlayImageBytes = 1024 * 1024;

    public static bool IsOverlayImageRef(string? reference) => reference is { Length: 69 }
        && reference.EndsWith(".webp", StringComparison.Ordinal)
        && reference.Take(64).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public async Task<byte[]?> GetOverlayImageAsync(string reference, ApiRequestContext context, CancellationToken ct = default)
    {
        if (!IsOverlayImageRef(reference)) throw new ArgumentException("Invalid overlay image reference.", nameof(reference));
        ApiIdentityContext owner;
        HttpRequestMessage bound;
        // Starting the read takes current request authority. Reconnect catch-up can retire
        // that authority mid-read without changing whose image this is, so the finished
        // read answers to the account/profile captured together with the request.
        lock (_authStateGate)
        {
            if (!IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.", ct);
            owner = CaptureIdentityContext();
            bound = CreateContextBoundRequest(context, HttpMethod.Get, "/api/v2/overlay-images/" + reference, null);
        }
        using var request = bound;
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Overlay image unavailable.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > MaxOverlayImageBytes) throw new InvalidDataException("Overlay image exceeds the byte limit.");
        if (response.Content.Headers.ContentType?.MediaType is not "image/webp") throw new InvalidDataException("Unsupported overlay image content.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(block, ct).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaxOverlayImageBytes) throw new InvalidDataException("Overlay image exceeds the byte limit.");
            buffer.Write(block, 0, count);
        }
        if (!IsCurrentIdentityContext(owner)) throw new OperationCanceledException("The selected profile changed.", ct);
        var bytes = buffer.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(reference[..64], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Overlay image content does not match its reference.");
        return bytes;
    }
}

public partial class SettingsApi
{
    public Task<byte[]?> GetOverlayImageAsync(string reference, ApiRequestContext context, CancellationToken ct = default)
        => client.GetOverlayImageAsync(reference, context, ct);

    /// <summary>Account/profile that owns overlay images and edits; unlike a request context it survives reconnect catch-up.</summary>
    public ApiIdentityContext CaptureOverlayImageOwner() => client.CaptureIdentityContext();
    public bool IsCurrentOverlayImageOwner(ApiIdentityContext owner) => client.IsCurrentIdentityContext(owner);
}

