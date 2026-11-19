using System.Globalization;

namespace SiloPlayer.Core.Services.Admin.Users;

public static class AdminUserAvatars
{
    public static IReadOnlyList<string> Styles { get; } = Array.AsReadOnly(new[] { "identicon", "initials", "bottts-neutral", "fun-emoji", "pixel-art-neutral" });
    private static readonly string[] Adjectives = ["cosmic", "jelly", "starlight", "mango", "bubble", "neon", "marble", "comet", "snappy", "pepper", "glimmer", "ripple", "pocket", "candy", "ember", "mochi", "sprout", "twinkle", "whiz", "plasma", "mint", "velvet", "crunchy", "sunbeam", "toffee", "splash", "boomer", "lunar", "fizzy", "biscuit", "rocket", "cobalt"];
    private static readonly string[] Nouns = ["otter", "rocket", "fox", "cookie", "gecko", "bunny", "penguin", "puffin", "tiger", "panda", "saturn", "wizard", "skater", "nebula", "robot", "pirate", "meteor", "sprite", "pebble", "nova", "dragon", "donut", "parrot", "panther", "goblin", "muffin", "laser", "pickle", "falcon", "sunset", "toaster", "bandit"];
    public static IReadOnlyList<(string Reference, string Label)> Batch(string style, int batch = 0)
    {
        if (!Styles.Contains(style)) style = Styles[0];
        batch = Math.Max(0, batch) % 32;
        return Enumerable.Range(0, 18).Select(index =>
        {
            var seed = Adjectives[(batch * 7 + index * 3 + style.Length) % Adjectives.Length] + "-" + Nouns[(batch * 11 + index * 5 + style.Length * 2) % Nouns.Length];
            return ("preset:dicebear:" + style + ":" + seed, CultureInfo.InvariantCulture.TextInfo.ToTitleCase(seed.Replace('-', ' ')));
        }).ToArray();
    }
    public static string Style(string? reference)
    {
        var parts = (reference ?? "").Replace("preset:", "", StringComparison.Ordinal).Split(':');
        return parts.Length == 3 && parts[0] == "dicebear" && Styles.Contains(parts[1]) ? parts[1] : Styles[0];
    }
    public static void ValidateEditorPin(string pin)
    {
        if (pin.Length != 4 || pin.Any(character => character is < '0' or > '9')) throw new ArgumentException("A profile PIN must contain exactly four digits.");
    }
}
