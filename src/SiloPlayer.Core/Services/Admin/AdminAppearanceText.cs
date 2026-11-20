using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

public static class AdminAppearanceText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.admin.common.brandingAssetField.previewAlt"] = "{label} preview",
        ["web.admin.common.brandingAssetField.remove"] = "Remove {label}",
        ["web.admin.common.brandingAssetField.replace"] = "Replace",
        ["web.admin.common.brandingAssetField.upload"] = "Upload",
        ["web.admin.common.brandingAssets.defaultCaption"] = "Default",
        ["web.admin.common.brandingAssets.faviconGuidance"] = "Square PNG, ICO, or SVG up to {size}. Stored exactly as uploaded — never resized or re-encoded.",
        ["web.admin.common.brandingAssets.loginBackgroundGuidance"] = "Wide photo, {px}px or wider. Stored as WebP capped at {px}px wide and shown cover-cropped, so keep the subject centered. PNG, JPEG, or WebP up to {size}.",
        ["web.admin.common.brandingAssets.markGuidance"] = "Square artwork, {px}×{px} or larger. Anything else is center-cropped to a square, then stored as WebP at exactly {px}×{px} — smaller art is upscaled. PNG, JPEG, or WebP up to {size}.",
        ["web.admin.common.brandingAssets.themeGradientCaption"] = "Theme gradient",
        ["web.admin.common.brandingAssets.wordmarkGuidance"] = "Wide artwork, {px}px or wider. Stored as WebP capped at {px}px wide; narrower images are not enlarged. PNG, JPEG, or WebP up to {size}.",
        ["web.admin.settings.appearance.badgeImages.group"] = "Badge images",
        ["web.admin.settings.appearance.colors.accent.custom"] = "Custom",
        ["web.admin.settings.appearance.colors.accent.customLabel"] = "Custom accent color",
        ["web.admin.settings.appearance.colors.accent.description"] = "Recolors buttons, focus outlines, and the sidebar.",
        ["web.admin.settings.appearance.colors.accent.label"] = "Accent color",
        ["web.admin.settings.appearance.colors.accent.themeDefault"] = "Theme default",
        ["web.admin.settings.appearance.colors.accent.useDefault"] = "Use theme default accent",
        ["web.admin.settings.appearance.colors.accent.usePreset"] = "Use accent {hex}",
        ["web.admin.settings.appearance.colors.group"] = "Colors",
        ["web.admin.settings.appearance.loading"] = "Loading...",
        ["web.admin.settings.appearance.logos.favicon.description"] = "Shown in the browser tab.",
        ["web.admin.settings.appearance.logos.favicon.label"] = "Favicon",
        ["web.admin.settings.appearance.logos.group"] = "Logos and icons",
        ["web.admin.settings.appearance.logos.loginBackground.description"] = "Shown on the login and signup pages.",
        ["web.admin.settings.appearance.logos.loginBackground.label"] = "Login background",
        ["web.admin.settings.appearance.logos.mark.description"] = "Shown in the collapsed sidebar and the installed app.",
        ["web.admin.settings.appearance.logos.mark.label"] = "Logo (icon)",
        ["web.admin.settings.appearance.logos.restartForUploads"] = "Restart the server to finish enabling image uploads.",
        ["web.admin.settings.appearance.logos.storageNeeded"] = "Image uploads need artwork storage (local disk or S3), set in Storage settings.",
        ["web.admin.settings.appearance.logos.wordmark.description"] = "Shown in the expanded sidebar.",
        ["web.admin.settings.appearance.logos.wordmark.label"] = "Logo (wordmark)",
        ["web.admin.settings.appearance.overlays.enabled.description"] = "The default for every profile. A profile that turns badges on or off for itself keeps its own choice.",
        ["web.admin.settings.appearance.overlays.enabled.label"] = "Show badges on poster art",
        ["web.admin.settings.appearance.overlays.group"] = "Card overlays",
        ["web.admin.settings.appearance.overlays.images"] = "{label} images",
        ["web.admin.settings.appearance.overlays.position"] = "{label} position",
        ["web.admin.settings.appearance.overlays.preview"] = "Preview",
        ["web.admin.settings.appearance.overlays.restoreDefaults"] = "Restore defaults",
        ["web.admin.settings.appearance.overlays.show"] = "Show {label}",
        ["web.admin.settings.appearance.overlays.style.description"] = "Default for people who have not chosen their own.",
        ["web.admin.settings.appearance.overlays.style.label"] = "Badge style",
        ["web.admin.settings.appearance.restoreDialog.confirm"] = "Restore",
        ["web.admin.settings.appearance.restoreDialog.description"] = "Replace the server-wide badge defaults with Silo's built-in configuration. The badge on/off switch is left alone, and nothing is written until you save.",
        ["web.admin.settings.appearance.restoreDialog.title"] = "Restore badge defaults",
        ["web.admin.settings.appearance.title"] = "Appearance",
        ["web.admin.settings.appearance.titlePages.group"] = "Title pages",
        ["web.admin.settings.appearance.titlePages.titleArt.description"] = "Default for people who have not chosen their own. Anyone can still change it for themselves.",
        ["settingsManifest.ui.title_art.label"] = "Show title art",
    };

    public static string T(string key, params (string Name, object? Value)[] values)
    {
        var arguments = values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
        return English.TryGetValue(key, out var pattern) ? AdminText.Format(pattern, arguments)
            : AppStrings.Get(key, arguments, fallback: AdminText.Pattern(key));
    }
}
