namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The fixed colours the Libraries page takes from the official WebUI (Silo-Server e130f262d <c>web/src/app.css</c>), with the contrast
/// arithmetic a test needs to check them numerically. It holds plain RGB values only (no UI types and no theme lookups), so nothing here can
/// throw while a static initializer runs, and a unit test can compute the ratios.
/// </summary>
public static class AdminLibrariesPalette
{
    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    }

    /// <summary><c>--destructive-fill: #dc2626</c>: the solid fill under a destructive button's or badge's label (a fixed value in <c>:root</c>; no theme overrides it).</summary>
    public static readonly Rgb DestructiveFill = new(0xDC, 0x26, 0x26);

    /// <summary><c>--destructive-foreground: #ffffff</c>.</summary>
    public static readonly Rgb DestructiveForeground = new(0xFF, 0xFF, 0xFF);

    /// <summary><c>hover:bg-destructive-fill/90</c> (<c>button.tsx</c>, <c>badge.tsx</c>): the fill at 90% opacity on hover.</summary>
    public const double DestructiveHoverOpacity = 0.9;

    /// <summary><c>hover:bg-destructive/10</c> on the stop-task button (<c>LibraryRowParts.tsx</c>).</summary>
    public const double StopHoverOpacity = 0.1;

    /// <summary>The raised panel surface (<c>#28282e</c>, <c>app.css</c>), the lightest backdrop the page puts a destructive button on.</summary>
    public static readonly Rgb RaisedSurface = new(0x28, 0x28, 0x2E);

    /// <summary>The alpha byte of an opacity, rounded the way a browser rounds a colour channel.</summary>
    public static byte AlphaByte(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero);

    /// <summary>The colour a layer of <paramref name="top"/> at <paramref name="opacity"/> shows over <paramref name="backdrop"/>.</summary>
    public static Rgb Over(Rgb top, double opacity, Rgb backdrop)
    {
        static byte Mix(byte a, byte b, double alpha) => (byte)Math.Round(a * alpha + b * (1 - alpha), MidpointRounding.AwayFromZero);
        return new(Mix(top.R, backdrop.R, opacity), Mix(top.G, backdrop.G, opacity), Mix(top.B, backdrop.B, opacity));
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    public static double Luminance(Rgb color)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG 2.x contrast ratio between two colours (1 to 21).</summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
