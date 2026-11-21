using System.Globalization;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The official WebUI's destructive (red) tokens (<c>web/src/app.css</c>, server 02a9d1d3).
/// No single red serves both jobs on the dark surfaces: <c>--destructive</c> is red text, icons and
/// borders; <c>--destructive-fill</c> is the solid fill under <c>--destructive-foreground</c>;
/// <c>--destructive-label</c> is red text on its own 10% tint. High contrast lightens
/// <c>--destructive</c> toward <c>--contrast-boost</c> and leaves the fill alone.
/// </summary>
public static class DestructiveTheme
{
    /// <summary><c>--destructive-base</c> of midnight-cinema, the default for any theme that omits it.</summary>
    public const string DefaultText = "#EF4444";
    /// <summary><c>--destructive-fill</c> of midnight-cinema.</summary>
    public const string DefaultFill = "#DC2626";
    /// <summary><c>--destructive-foreground</c> of midnight-cinema.</summary>
    public const string DefaultForeground = "#FFFFFF";
    /// <summary><c>--contrast-boost</c>: white on a dark page.</summary>
    public const string ContrastBoost = "#FFFFFF";

    /// <summary><c>hover:bg-destructive-fill/90</c>: the fill over the surface behind the button.</summary>
    public const double HoverOpacity = 0.9;
    /// <summary>Pressed has no official state; the native affordance is the fill a step darker than hover.</summary>
    public const double PressedOpacity = 0.8;
    /// <summary><c>bg-destructive/10</c>: the tint under a label or an error panel.</summary>
    public const double TintOpacity = 0.1;

    private const double HighContrastTextMix = 0.65; // --destructive in html[data-high-contrast]
    private const double LabelMix = 0.25;            // --destructive-label outside high contrast

    /// <summary>The resolved role colors, as <c>#RRGGBB</c>.</summary>
    /// <param name="Text">Error text, icons and borders (<c>--destructive</c>).</param>
    /// <param name="Fill">Solid fill of a destructive button or badge (<c>--destructive-fill</c>).</param>
    /// <param name="Foreground">Text on the fill (<c>--destructive-foreground</c>).</param>
    /// <param name="Label">Red label on its own tint (<c>--destructive-label</c>).</param>
    public readonly record struct Tokens(string Text, string Fill, string Foreground, string Label);

    /// <summary>
    /// Resolves the roles for a theme. <paramref name="text"/> is the theme's <c>--destructive-base</c>.
    /// A token the theme does not define (null, empty or not a hex color) inherits the default.
    /// The server never supplies a red (it sends only the branding accent keys), so there is no override
    /// input: a theme's own tokens are the only source, and no fill/foreground pair can arrive unchecked.
    /// </summary>
    public static Tokens Resolve(string? text, string? fill, string? foreground, bool highContrast,
        string contrastBoost = ContrastBoost)
    {
        var baseText = Normalize(text) ?? DefaultText;
        var boost = Normalize(contrastBoost) ?? ContrastBoost;
        var resolvedText = highContrast ? Mix(boost, HighContrastTextMix, baseText) : baseText;
        var label = highContrast ? resolvedText : Mix(boost, LabelMix, baseText);
        return new Tokens(resolvedText,
            Normalize(fill) ?? DefaultFill,
            Normalize(foreground) ?? DefaultForeground,
            label);
    }

    /// <summary>The fill as drawn at <paramref name="opacity"/> over <paramref name="surface"/>.</summary>
    public static string Over(string color, double opacity, string surface)
    {
        var top = Parse(color); var behind = Parse(surface);
        return Format(Blend(top, opacity, behind));
    }

    /// <summary>CSS <c>color-mix(in srgb, a p%, b)</c>.</summary>
    public static string Mix(string a, double share, string b)
    {
        var first = Parse(a); var second = Parse(b);
        return Format(Blend(first, share, second));
    }

    /// <summary>WCAG 2.x contrast ratio between two opaque colors.</summary>
    public static double Contrast(string first, string second)
    {
        var a = Luminance(Parse(first)); var b = Luminance(Parse(second));
        var (light, dark) = a >= b ? (a, b) : (b, a);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>Upper-case <c>#RRGGBB</c> for <c>#RGB</c> or <c>#RRGGBB</c>; null for anything else.</summary>
    public static string? Normalize(string? value)
    {
        var hex = value?.Trim();
        if (hex is null || hex.Length == 0 || hex[0] != '#') return null;
        hex = hex[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit)) return null;
        return "#" + hex.ToUpperInvariant();
    }

    private static (int R, int G, int B) Parse(string value)
    {
        var hex = Normalize(value) ?? throw new FormatException($"Not a #RGB or #RRGGBB color: {value}");
        return (int.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static (int R, int G, int B) Blend((int R, int G, int B) top, double share, (int R, int G, int B) behind)
    {
        static int Channel(int a, double share, int b) => (int)Math.Round(a * share + b * (1 - share), MidpointRounding.AwayFromZero);
        return (Channel(top.R, share, behind.R), Channel(top.G, share, behind.G), Channel(top.B, share, behind.B));
    }

    private static string Format((int R, int G, int B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Luminance((int R, int G, int B) c)
    {
        static double Channel(int value)
        {
            var s = value / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
