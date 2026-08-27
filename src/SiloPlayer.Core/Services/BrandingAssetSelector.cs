using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public readonly record struct BrandingAssetChoice(string? WordmarkUrl, string? MarkUrl);

/// <summary>Selects server branding assets for the active theme appearance.</summary>
public static class BrandingAssetSelector
{
    public static BrandingAssetChoice Select(
        ServerBrandingResponse branding,
        bool isLightAppearance)
    {
        ArgumentNullException.ThrowIfNull(branding);
        return new BrandingAssetChoice(
            isLightAppearance
                ? branding.WordmarkLightUrl ?? branding.WordmarkUrl
                : branding.WordmarkUrl,
            isLightAppearance
                ? branding.MarkLightUrl ?? branding.MarkUrl
                : branding.MarkUrl);
    }
}
