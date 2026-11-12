using System.Globalization;

namespace SiloPlayer.Core.Services;

/// <summary>accentColor.ts at Server 0b579aa6: text-readable lamp and its label on #101013.</summary>
public static class PlayerHudAccent
{
    public static string OnDark(string rgb)
    {
        if (!int.TryParse(rgb.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) value = 0xE8B86E;
        for (var step = 0; step <= 20; step++)
        {
            int Mix(int shift) { var channel = (value >> shift) & 255; return (int)Math.Floor(channel + (255 - channel) * step / 20d + .5); }
            var candidate = (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
            if (Contrast(candidate, 0x101013) >= 4.5 && Contrast(candidate, LabelValue(candidate)) >= 4.5) return candidate.ToString("X6", CultureInfo.InvariantCulture);
        }
        return "FFFFFF";
    }

    public static string LabelOn(string rgb) => LabelValue(int.Parse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString("X6", CultureInfo.InvariantCulture);
    private static int LabelValue(int rgb) => Contrast(rgb, 0xFFFFFF) > Contrast(rgb, 0x141417) ? 0xFFFFFF : 0x141417;
    private static double Contrast(int a, int b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
    private static double Luminance(int rgb)
    {
        double Channel(int shift) { var c = ((rgb >> shift) & 255) / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Channel(16) + .7152 * Channel(8) + .0722 * Channel(0);
    }
}
