namespace SiloPlayer.Core.Services;

/// <summary>
/// Canonical quality-tier policy shared by initial HLS startup and OSC quality
/// switches. The tier order and bitrates mirror the current Silo WebUI
/// useTranscodeQuality hook.
/// </summary>
public static class TranscodeQualityPolicy
{
    public sealed record Tier(string Id, string Resolution, int BitrateKbps, int Height);

    public static IReadOnlyList<Tier> Tiers { get; } =
    [
        new("1080p-high", "1080p", 10_000, 1080),
        new("1080p", "1080p", 6_000, 1080),
        new("720p-high", "720p", 4_000, 720),
        new("720p", "720p", 2_000, 720),
        new("480p", "480p", 1_500, 480),
        new("420p", "420p", 720, 420),
    ];

    public static Tier? Find(string? tierId)
        => Tiers.FirstOrDefault(tier =>
            string.Equals(tier.Id, tierId, StringComparison.OrdinalIgnoreCase));

    public static Tier? Find(string? resolution, int bitrateKbps)
        => Tiers.FirstOrDefault(tier =>
            string.Equals(tier.Resolution, resolution, StringComparison.OrdinalIgnoreCase) &&
            tier.BitrateKbps == bitrateKbps);

    /// <summary>
    /// Matches the WebUI auto-start policy for a base session that requires
    /// video transcoding. Explicit Original stays source-resolution; a valid
    /// explicit resolution selects the first (highest bitrate) matching tier;
    /// Auto selects the first tier strictly below the source resolution.
    /// </summary>
    public static Tier? ResolveInitialVideoTier(string? sourceResolution, string? qualityPreference)
    {
        var sourceHeight = ResolutionHeight(sourceResolution);
        IEnumerable<Tier> visibleTiers = sourceHeight > 0
            ? Tiers.Where(tier => tier.Height < sourceHeight)
            : [];

        if (!string.IsNullOrWhiteSpace(qualityPreference) &&
            !qualityPreference.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var preferredResolution = PreferenceResolution(qualityPreference);
            return string.IsNullOrEmpty(preferredResolution)
                ? null
                : visibleTiers.FirstOrDefault(tier =>
                    tier.Resolution.Equals(preferredResolution, StringComparison.OrdinalIgnoreCase));
        }

        return visibleTiers.FirstOrDefault();
    }

    public static int ResolutionHeight(string? resolution)
        => (resolution ?? "").Trim().ToLowerInvariant() switch
        {
            "4k" or "uhd" or "2160p" => 2160,
            "1440p" => 1440,
            "1080p" => 1080,
            "720p" => 720,
            "480p" => 480,
            "420p" => 420,
            "360p" => 360,
            _ => 0,
        };

    public static string? PreferenceResolution(string? preference)
        => (preference ?? "").Trim().ToLowerInvariant() switch
        {
            "4k" or "uhd" or "2160p" => "2160p",
            "1080p" => "1080p",
            "720p" => "720p",
            "480p" => "480p",
            "420p" => "420p",
            _ => null,
        };

    /// <summary>
    /// Applies the profile's preferred-quality ceiling without ever
    /// advertising a capability above the actual native player limit.
    /// </summary>
    public static string ResolveMaximumResolution(
        string capabilityResolution,
        string? qualityPreference)
    {
        var preferredResolution = PreferenceResolution(qualityPreference);
        var capabilityHeight = ResolutionHeight(capabilityResolution);
        var preferredHeight = ResolutionHeight(preferredResolution);
        return preferredHeight > 0 &&
               (capabilityHeight == 0 || preferredHeight < capabilityHeight)
            ? preferredResolution!
            : capabilityResolution;
    }
}
