using System.Globalization;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Per-stream bitrate caps (<c>max_remote_stream_bitrate_kbps</c>, <c>max_local_stream_bitrate_kbps</c>) in whole kbps with 0 meaning
/// unlimited, entered in Mbps. Official 217ff25 <c>web/src/lib/streamBitrateLimit.ts</c> and <c>StreamBitrateLimitInput</c>.
/// </summary>
public static partial class AdminStreamBitrate
{
    /// <summary>Preset caps offered ahead of a custom value, highest first.</summary>
    public static readonly IReadOnlyList<int> PresetsKbps = [40000, 20000, 10000, 8000, 5000, 3000, 2000, 1000];
    /// <summary>Caps below this force very low-quality transcodes, so the form warns.</summary>
    public const int LowKbps = 1000;
    public const string Unlimited = "0";
    public const string Custom = "custom";

    public static bool IsPreset(int kbps) => PresetsKbps.Contains(kbps);
    public static bool IsLow(int kbps) => kbps > 0 && kbps < LowKbps;
    /// <summary>The choice a stored cap selects: Unlimited, a preset, or Custom. Null while nothing is stored.</summary>
    public static string? Choice(int? kbps) => kbps is not { } value ? null : value == 0 || IsPreset(value) ? value.ToString(CultureInfo.InvariantCulture) : Custom;
    /// <summary>kbps as an exact Mbps number string: 8000 → "8", 1500 → "1.5", 250 → "0.25".</summary>
    public static string MbpsText(int kbps) => (kbps / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\s*(\d+([.,]\d{0,3})?|[.,]\d{1,3})\s*$")]
    private static partial Regex MbpsPattern();

    /// <summary>
    /// Parses a typed custom cap in Mbps into kbps. Null unless the text is a positive number the API can store exactly (1 kbps
    /// resolution): nothing is rounded silently, and 0 is refused because it means unlimited, which has its own choice.
    /// </summary>
    public static int? ParseMbps(string? text)
    {
        if (text == null || !MbpsPattern().IsMatch(text)) return null;
        if (!decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mbps)) return null;
        var kbps = decimal.Round(mbps * 1000m, MidpointRounding.AwayFromZero);
        return kbps > 0 && kbps <= int.MaxValue ? (int)kbps : null;
    }

    public static string Format(int kbps) => kbps == 0
        ? AdminUserResourceText.T("web.player.bitrateLimit.unlimited")
        : AdminUserResourceText.T("web.player.bitrateLimit.value", ("mbps", MbpsText(kbps)));
}
