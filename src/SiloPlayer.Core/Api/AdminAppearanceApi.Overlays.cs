using System.Text;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminAppearanceApi
{
    public const int MaxOverlayUploadBytes = 4 * 1024 * 1024;
    public const int MaxOverlayImageNameLength = 64;

    public async Task<OverlayLibraryImage> UploadOverlayImageAsync(MonitorAuthorityLease lease, byte[] bytes,
        string mediaType, string name, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (bytes.Length is 0 or > MaxOverlayUploadBytes || mediaType is not ("image/png" or "image/jpeg" or "image/webp") ||
            trimmed.Length == 0 || trimmed.EnumerateRunes().Count() > MaxOverlayImageNameLength)
            throw new ArgumentException("The selected badge image type, size or name is not supported.");
        lease.ThrowIfRetired();
        cancellation.ThrowIfCancellationRequested();
        try
        {
            var image = await client.AdminAppearanceUploadOverlayAsync(lease, bytes, mediaType, trimmed, cancellation).ConfigureAwait(false);
            lease.ThrowIfRetired();
            return image;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw SafeFailure(error); }
        catch { throw new AdminAppearanceOutcomeUnknownException(); }
    }

    public async Task DeleteOverlayImageAsync(MonitorAuthorityLease lease, string reference, CancellationToken cancellation = default)
    {
        if (!SiloApiClient.IsOverlayImageRef(reference)) throw new ArgumentException("Invalid badge image reference.", nameof(reference));
        lease.ThrowIfRetired();
        cancellation.ThrowIfCancellationRequested();
        try
        {
            await client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Delete, "/api/v2/admin/overlay-images/" + reference,
                ct: cancellation).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw SafeFailure(error); }
        catch { throw new AdminAppearanceOutcomeUnknownException(); }
    }
}
