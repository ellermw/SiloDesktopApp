using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Services;

public sealed record OverlayStyle
{
    [JsonPropertyName("size")] public double? Size { get; init; }
    [JsonPropertyName("shape")] public string? Shape { get; init; }
    [JsonPropertyName("background")] public string? Background { get; init; }
    [JsonPropertyName("textColor")] public string? TextColor { get; init; }
    [JsonPropertyName("borderColor")] public string? BorderColor { get; init; }
    [JsonPropertyName("borderWidth")] public double? BorderWidth { get; init; }
    [JsonPropertyName("fontWeight")] public string? FontWeight { get; init; }
    [JsonPropertyName("uppercase")] public bool? Uppercase { get; init; }
    [JsonPropertyName("shadow")] public bool? Shadow { get; init; }

    public static OverlayStyle? Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        string? Text(string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        double? Number(string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
        bool? Flag(string key) => element.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        return new() { Size = Number("size"), Shape = Text("shape"), Background = Text("background"), TextColor = Text("textColor"), BorderColor = Text("borderColor"), BorderWidth = Number("borderWidth"), FontWeight = Text("fontWeight"), Uppercase = Flag("uppercase"), Shadow = Flag("shadow") };
    }

    public void WriteTo(JsonObject root)
    {
        var original = root["style"] as JsonObject;
        if (original is not null && Parse(JsonSerializer.SerializeToElement(original)) == this) return;
        var style = original ?? new JsonObject();
        var previous = original is null ? null : JsonSerializer.SerializeToNode(Parse(JsonSerializer.SerializeToElement(original)))?.AsObject();
        var serialized = JsonSerializer.SerializeToNode(this)!.AsObject();
        foreach (var pair in serialized)
        {
            if (previous is not null && JsonNode.DeepEquals(previous[pair.Key], pair.Value)) continue;
            if (pair.Value is null) style.Remove(pair.Key);
            else style[pair.Key] = pair.Value.DeepClone();
        }
        root["style"] = style;
    }
}

public readonly record struct OverlayColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public string Hex => $"#{Red:x2}{Green:x2}{Blue:x2}{Alpha:x2}";
    public static OverlayColor? Parse(string? hex)
    {
        if (hex is not { Length: 7 or 9 } || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit)) return null;
        byte Part(int start) => byte.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new(Part(1), Part(3), Part(5), hex.Length == 9 ? Part(7) : (byte)255);
    }
    public double Luminance
    {
        get
        {
            static double Linear(byte part) { var channel = part / 255d; return channel <= .03928 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); }
            return .2126 * Linear(Red) + .7152 * Linear(Green) + .0722 * Linear(Blue);
        }
    }
    public OverlayColor ReadableText => Luminance > .4 ? new(0, 0, 0) : new(255, 255, 255);
    public double Contrast(OverlayColor other) => (Math.Max(Luminance, other.Luminance) + .05) / (Math.Min(Luminance, other.Luminance) + .05);
    public OverlayColor Mix(OverlayColor basis, double weight)
    {
        var alpha = Alpha * weight + basis.Alpha * (1 - weight);
        if (alpha == 0) return default;
        var firstAlpha = Alpha;
        byte Channel(byte first, byte second) => (byte)Math.Round((first * firstAlpha * weight + second * basis.Alpha * (1 - weight)) / alpha);
        return new(Channel(Red, basis.Red), Channel(Green, basis.Green), Channel(Blue, basis.Blue), (byte)Math.Round(alpha));
    }
}

public sealed record OverlayAppearance
{
    public double FontSize { get; init; } = 10;
    public double PaddingInline { get; init; } = 8;
    public double PaddingBlock { get; init; } = 2;
    public double IconSize { get; init; } = 11;
    public double IconGap { get; init; } = 4;
    public double StackGap { get; init; } = 4;
    public double Radius { get; init; } = 9999;
    public double BorderWidth { get; init; } = 1;
    public double BorderLeftWidth { get; init; }
    public int FontWeight { get; init; } = 600;
    public bool Uppercase { get; init; } = true;
    public bool PreferIcon { get; init; }
    public bool SourceChips { get; init; }
    public bool Band { get; init; }
    public bool BoxShadow { get; init; }
    public bool TextShadow { get; init; }
    public OverlayColor Background { get; init; } = new(0, 0, 0, 153);
    public OverlayColor Foreground { get; init; } = new(255, 255, 255);
    public OverlayColor Border { get; init; } = new(255, 255, 255, 38);

    public static OverlayAppearance Resolve(string preset, OverlayStyle? style, string? accentHex)
    {
        var accent = OverlayColor.Parse(accentHex);
        var look = preset switch
        {
            "minimal" => new OverlayAppearance { FontSize = 9, PaddingInline = 4, PaddingBlock = 0, Radius = 8, BorderWidth = 0, IconSize = 10, StackGap = 2, Background = default, Foreground = accent ?? new(255, 255, 255, 217), TextShadow = true },
            "vibrant" => new OverlayAppearance { Radius = 10, BorderWidth = 0, IconSize = 12, FontWeight = 700, PreferIcon = true, BoxShadow = true, Background = accent ?? new(220, 220, 220, 242), Foreground = accent is null ? new(0, 0, 0) : new(255, 255, 255) },
            "pill" => new OverlayAppearance { PaddingInline = 10, PaddingBlock = 4, IconSize = 12, PreferIcon = true, Background = accent?.Mix(new(20, 20, 30, 179), .2) ?? new(20, 20, 30, 179) },
            "square" => new OverlayAppearance { FontSize = 9, PaddingInline = 6, Radius = 8, BorderWidth = 0, BorderLeftWidth = accent is null ? 0 : 2, FontWeight = 700, IconSize = 10, StackGap = 2, Background = new(0, 0, 0, 204), Foreground = accent ?? new(255, 255, 255), Border = accent ?? default },
            "plate" => new OverlayAppearance { FontSize = 12, PaddingInline = 7, PaddingBlock = 4, Radius = 6, IconSize = 13, IconGap = 5, FontWeight = 800, SourceChips = true, PreferIcon = true, BoxShadow = true, Background = new(0, 0, 0, 184), Border = accent is { } color ? color with { Alpha = (byte)Math.Round(color.Alpha * .45) } : new(255, 255, 255, 26) },
            "band" => new OverlayAppearance { FontSize = 12.5, PaddingInline = 0, PaddingBlock = 1, Radius = 0, BorderWidth = 0, IconSize = 13, IconGap = 3, StackGap = 7, FontWeight = 800, Band = true, Background = default, Foreground = accent ?? new(255, 255, 255), TextShadow = true },
            _ => new OverlayAppearance { Background = accent?.Mix(new(0, 0, 0, 153), .28) ?? new(0, 0, 0, 153) },
        };
        if (style is null) return look;
        var size = style.Size is >= .75 and <= 2 ? style.Size.Value : 1;
        var fill = OverlayColor.Parse(style.Background);
        var text = style.TextColor is { Length: 7 } ? OverlayColor.Parse(style.TextColor) : null;
        var border = OverlayColor.Parse(style.BorderColor);
        var solid = fill is { Alpha: >= 128 };
        var textStrategy = preset is "minimal" or "band";
        var borderStrategy = preset is "square" or "plate";
        var accentText = accent is { } tint && textStrategy && (!solid || tint.Contrast(fill!.Value) >= 3);
        var accentFill = accent is not null && (!textStrategy && !borderStrategy || textStrategy && !accentText);
        var width = style.BorderWidth is >= 0 and <= 3 ? style.BorderWidth : null;
        look = look with
        {
            FontSize = look.FontSize * size, PaddingInline = look.PaddingInline * size, PaddingBlock = look.PaddingBlock * size,
            IconSize = look.IconSize * size, IconGap = look.IconGap * size, StackGap = look.StackGap * size,
            Radius = style.Shape switch { "pill" => 9999, "rounded" => 6, "square" => 2, _ => look.Radius },
            BorderWidth = width ?? look.BorderWidth,
            FontWeight = style.FontWeight switch { "regular" => 400, "semibold" => 600, "extrabold" => 800, _ => look.FontWeight },
            Uppercase = style.Uppercase ?? look.Uppercase,
            BoxShadow = style.Shadow ?? look.BoxShadow,
            TextShadow = !solid && look.TextShadow,
            Background = fill is { } background ? accentFill ? accent!.Value.Mix(background, .3) : background : look.Background,
            Foreground = !accentText ? text ?? (solid ? fill!.Value.ReadableText : look.Foreground) : look.Foreground,
            Border = border is { } borderColor && !(accent is not null && borderStrategy) ? borderColor : look.Border,
        };
        if (fill is { Alpha: > 0 } || width > 0)
            look = look with { PaddingInline = Math.Max(look.PaddingInline, look.FontSize * .6), PaddingBlock = Math.Max(look.PaddingBlock, look.FontSize * .2) };
        return look;
    }
}

