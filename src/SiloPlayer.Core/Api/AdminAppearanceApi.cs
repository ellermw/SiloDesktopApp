using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed class AdminAppearanceBranding
{
    public bool StorageAvailable { get; set; }
    public string? WordmarkUrl { get; set; }
    public string? MarkUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? LoginBgUrl { get; set; }
    public string? ServerName { get; set; }
    public override string ToString() => nameof(AdminAppearanceBranding);

    public string? AssetUrl(string kind) => kind switch
    {
        "wordmark" => WordmarkUrl,
        "mark" => MarkUrl,
        "favicon" => FaviconUrl,
        "login_bg" => LoginBgUrl,
        _ => null,
    };
}

public sealed class AdminAppearanceAsset
{
    public string Kind { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Url { get; set; } = "";
    public override string ToString() => nameof(AdminAppearanceAsset);
}

public sealed class AdminAppearanceOutcomeUnknownException() : Exception("The image change outcome is unknown. Reload and review before trying again.");

public sealed partial class AdminAppearanceApi(SiloApiClient client)
{
    public Task<AdminAppearanceBranding> GetBrandingAsync(MonitorAuthorityLease lease, CancellationToken cancellation = default)
        => client.AdminLibrarySendAsync<AdminAppearanceBranding>(lease, HttpMethod.Get, "/api/v2/theme/branding", ct: cancellation);

    public Task<OverlayConfigResponse> GetOverlayConfigAsync(MonitorAuthorityLease lease, CancellationToken cancellation = default)
        => client.AdminLibrarySendAsync<OverlayConfigResponse>(lease, HttpMethod.Get, "/api/v2/settings/overlay-config", ct: cancellation);

    public async Task<AdminAppearanceAsset> UploadAssetAsync(MonitorAuthorityLease lease, string kind, byte[] bytes, string mediaType,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!AdminAppearanceAssetPolicy.IsAllowed(kind, mediaType, bytes.LongLength))
            throw new ArgumentException("The selected image type or size is not supported.", nameof(bytes));
        lease.ThrowIfRetired();
        try
        {
            var asset = await client.AdminAppearanceUploadAsync(lease, kind, bytes, mediaType, cancellation).ConfigureAwait(false);
            if (asset.Kind != kind || asset.Ref.Length is 0 or > 512 ||
                asset.Url != "/api/v2/branding/assets/" + kind + "?v=" + Uri.EscapeDataString(asset.Ref).Replace("%20", "+", StringComparison.Ordinal))
                throw new InvalidDataException("The image assignment was not acknowledged.");
            return asset;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw SafeFailure(error); }
        catch { throw new AdminAppearanceOutcomeUnknownException(); }
    }

    public async Task DeleteAssetAsync(MonitorAuthorityLease lease, string kind, CancellationToken cancellation = default)
    {
        if (kind is not ("wordmark" or "mark" or "favicon" or "login_bg"))
            throw new ArgumentException("Unknown branding image slot.", nameof(kind));
        lease.ThrowIfRetired();
        try
        {
            await client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Delete, "/api/v2/admin/branding/assets/" + kind,
                ct: cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw SafeFailure(error); }
        catch { throw new AdminAppearanceOutcomeUnknownException(); }
    }

    private static ApiException SafeFailure(ApiException error) => new(AdminServerSettingsApi.SafeCode(error.ErrorCode), "The server refused the image change.", error.StatusCode);
}
