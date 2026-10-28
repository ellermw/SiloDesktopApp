namespace SiloPlayer.Core.Services;

/// <summary>Decides when route navigation needs to rewrite native typography.</summary>
public static class AccessibilityTypographyPolicy
{
    /// <summary>
    /// Default text needs no per-element rewrite unless an earlier non-default
    /// preference left locally scaled values on retained (cached) pages.
    /// </summary>
    public static bool RequiresTreeWalk(string? textScale, string? textWeight, bool previouslyModified) =>
        previouslyModified || textScale is "large" or "x-large" || textWeight == "strong";
}
