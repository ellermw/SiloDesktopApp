using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Services;

public class ThemeColors
{
    public string Background { get; set; } = "";
    public string Foreground { get; set; } = "";
    public string Card { get; set; } = "";
    public string Primary { get; set; } = "";
    public string PrimaryForeground { get; set; } = "";
    public string Secondary { get; set; } = "";
    public string SecondaryForeground { get; set; } = "";
    public string MutedForeground { get; set; } = "";
    public string Border { get; set; } = "";
    public string Input { get; set; } = "";
    public string Sidebar { get; set; } = "";
    public string Surface { get; set; } = "";
    public string SurfaceHover { get; set; } = "";
    public string SurfaceRaised { get; set; } = "";
    public string Destructive { get; set; } = "";
    public string Accent { get; set; } = "";
}

public class ThemeService
{
    private readonly SettingsService _settingsService;

    private static readonly Dictionary<string, ThemeColors> Themes = new()
    {
        ["catppuccin"] = new ThemeColors
        {
            Background = "#1E1E2E",
            Foreground = "#CDD6F4",
            Card = "#313244",
            Primary = "#CBA6F7",
            PrimaryForeground = "#1E1E2E",
            Secondary = "#45475A",
            SecondaryForeground = "#CDD6F4",
            MutedForeground = "#A6ADC8",
            Border = "#45475A",
            Input = "#45475A",
            Sidebar = "#181825",
            Surface = "#313244",
            SurfaceHover = "#45475A",
            SurfaceRaised = "#3B3B50",
            Destructive = "#F38BA8",
            Accent = "#45475A",
        },
        ["gruvbox"] = new ThemeColors
        {
            Background = "#282828",
            Foreground = "#EBDBB2",
            Card = "#3C3836",
            Primary = "#FABD2F",
            PrimaryForeground = "#282828",
            Secondary = "#3C3836",
            SecondaryForeground = "#EBDBB2",
            MutedForeground = "#A89984",
            Border = "#504945",
            Input = "#3C3836",
            Sidebar = "#1D2021",
            Surface = "#3C3836",
            SurfaceHover = "#504945",
            SurfaceRaised = "#5A524C",
            Destructive = "#FB4934",
            Accent = "#504945",
        },
        ["void-space"] = new ThemeColors
        {
            Background = "#0D1117",
            Foreground = "#C9D1D9",
            Card = "#161B22",
            Primary = "#58A6FF",
            PrimaryForeground = "#0D1117",
            Secondary = "#21262D",
            SecondaryForeground = "#C9D1D9",
            MutedForeground = "#8B949E",
            Border = "#30363D",
            Input = "#21262D",
            Sidebar = "#010409",
            Surface = "#161B22",
            SurfaceHover = "#21262D",
            SurfaceRaised = "#272D36",
            Destructive = "#F85149",
            Accent = "#21262D",
        },
        ["charcoal-studio"] = new ThemeColors
        {
            Background = "#1C1C1E",
            Foreground = "#F2F2F7",
            Card = "#2C2C2E",
            Primary = "#0A84FF",
            PrimaryForeground = "#FFFFFF",
            Secondary = "#38383A",
            SecondaryForeground = "#F2F2F7",
            MutedForeground = "#98989D",
            Border = "#38383A",
            Input = "#2C2C2E",
            Sidebar = "#141414",
            Surface = "#2C2C2E",
            SurfaceHover = "#38383A",
            SurfaceRaised = "#424244",
            Destructive = "#FF375F",
            Accent = "#38383A",
        },
        ["graphite-pro"] = new ThemeColors
        {
            Background = "#18181B",
            Foreground = "#FAFAFA",
            Card = "#27272A",
            Primary = "#A855F7",
            PrimaryForeground = "#18181B",
            Secondary = "#3F3F46",
            SecondaryForeground = "#FAFAFA",
            MutedForeground = "#A1A1AA",
            Border = "#3F3F46",
            Input = "#27272A",
            Sidebar = "#111113",
            Surface = "#27272A",
            SurfaceHover = "#3F3F46",
            SurfaceRaised = "#48484E",
            Destructive = "#EF4444",
            Accent = "#3F3F46",
        },
        ["obsidian-depth"] = new ThemeColors
        {
            Background = "#0F0F0F",
            Foreground = "#F5F5F5",
            Card = "#1A1A1A",
            Primary = "#00D4AA",
            PrimaryForeground = "#0F0F0F",
            Secondary = "#262626",
            SecondaryForeground = "#F5F5F5",
            MutedForeground = "#8A8A8A",
            Border = "#2A2A2A",
            Input = "#1A1A1A",
            Sidebar = "#0A0A0A",
            Surface = "#1A1A1A",
            SurfaceHover = "#262626",
            SurfaceRaised = "#303030",
            Destructive = "#FF6B6B",
            Accent = "#262626",
        },
        ["midnight-cinema"] = new ThemeColors
        {
            Background = "#141417",
            Foreground = "#E8E8EC",
            Card = "#1C1C20",
            Primary = "#E8E8EC",
            PrimaryForeground = "#141417",
            Secondary = "#232328",
            SecondaryForeground = "#D0D0D6",
            MutedForeground = "#6E6E78",
            Border = "#28282E",
            Input = "#1C1C20",
            Sidebar = "#0F0F12",
            Surface = "#1C1C20",
            SurfaceHover = "#232328",
            SurfaceRaised = "#28282E",
            Destructive = "#EF4444",
            Accent = "#232328",
        },
        ["cinema-light"] = new ThemeColors
        {
            Background = "#F4F4F6",
            Foreground = "#1A1A1E",
            Card = "#FFFFFF",
            Primary = "#1A1A1E",
            PrimaryForeground = "#F4F4F6",
            Secondary = "#E8E8EC",
            SecondaryForeground = "#3A3A42",
            MutedForeground = "#78787F",
            Border = "#D8D8DE",
            Input = "#E8E8EC",
            Sidebar = "#EAEAEE",
            Surface = "#EBEBEF",
            SurfaceHover = "#E0E0E6",
            SurfaceRaised = "#FFFFFF",
            Destructive = "#DC2626",
            Accent = "#E8E8EC",
        },
        ["cobalt-studio"] = new ThemeColors
        {
            Background = "#101722",
            Foreground = "#F4F8FF",
            Card = "#151E2B",
            Primary = "#78AEFC",
            PrimaryForeground = "#0F1722",
            Secondary = "#1B2634",
            SecondaryForeground = "#D7E2F1",
            MutedForeground = "#90A0B5",
            Border = "#28384D",
            Input = "#182231",
            Sidebar = "#0C131D",
            Surface = "#151E2B",
            SurfaceHover = "#1D2A3B",
            SurfaceRaised = "#223245",
            Destructive = "#EF6B73",
            Accent = "#203043",
        },
        ["oxblood-noir"] = new ThemeColors
        {
            Background = "#171113",
            Foreground = "#F8F2F3",
            Card = "#20181B",
            Primary = "#D16A78",
            PrimaryForeground = "#180F12",
            Secondary = "#281D21",
            SecondaryForeground = "#DECFD2",
            MutedForeground = "#A28E95",
            Border = "#3B2830",
            Input = "#251A1F",
            Sidebar = "#120D0F",
            Surface = "#20181B",
            SurfaceHover = "#2A2025",
            SurfaceRaised = "#34262C",
            Destructive = "#F08080",
            Accent = "#322228",
        },
        ["ember-slate"] = new ThemeColors
        {
            Background = "#151213",
            Foreground = "#F7F3F2",
            Card = "#1D191B",
            Primary = "#F07B62",
            PrimaryForeground = "#1A1110",
            Secondary = "#262123",
            SecondaryForeground = "#DDD4D1",
            MutedForeground = "#A19692",
            Border = "#392F33",
            Input = "#231D20",
            Sidebar = "#100D0E",
            Surface = "#1D191B",
            SurfaceHover = "#272124",
            SurfaceRaised = "#31282C",
            Destructive = "#FF8B7D",
            Accent = "#30282B",
        },
        ["evergreen-studio"] = new ThemeColors
        {
            Background = "#101715",
            Foreground = "#F2F8F5",
            Card = "#16201D",
            Primary = "#5BC39D",
            PrimaryForeground = "#0D1513",
            Secondary = "#1B2824",
            SecondaryForeground = "#D1E0D9",
            MutedForeground = "#91A39C",
            Border = "#284038",
            Input = "#182420",
            Sidebar = "#0C1210",
            Surface = "#16201D",
            SurfaceHover = "#1E2B27",
            SurfaceRaised = "#263732",
            Destructive = "#F07A7A",
            Accent = "#20322D",
        },
        ["verdant-ink"] = new ThemeColors
        {
            Background = "#0D1513",
            Foreground = "#F3F9F7",
            Card = "#121D1A",
            Primary = "#86D4B6",
            PrimaryForeground = "#0D1513",
            Secondary = "#172521",
            SecondaryForeground = "#D5E5DF",
            MutedForeground = "#96A9A1",
            Border = "#264139",
            Input = "#16231F",
            Sidebar = "#09110F",
            Surface = "#121D1A",
            SurfaceHover = "#192823",
            SurfaceRaised = "#22352F",
            Destructive = "#F18989",
            Accent = "#1D312B",
        },
    };

    /// <summary>Display names for the theme picker.</summary>
    private static readonly Dictionary<string, string> ThemeDisplayNames = new()
    {
        ["catppuccin"] = "Catppuccin",
        ["gruvbox"] = "Gruvbox",
        ["void-space"] = "Void Space",
        ["charcoal-studio"] = "Charcoal Studio",
        ["graphite-pro"] = "Graphite Pro",
        ["obsidian-depth"] = "Obsidian Depth",
        ["midnight-cinema"] = "Midnight Cinema",
        ["cinema-light"] = "Cinema Light",
        ["cobalt-studio"] = "Cobalt Studio",
        ["oxblood-noir"] = "Oxblood Noir",
        ["ember-slate"] = "Ember Slate",
        ["evergreen-studio"] = "Evergreen Studio",
        ["verdant-ink"] = "Verdant Ink",
    };

    public ThemeService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<string> AvailableThemeIds => Themes.Keys.ToList();

    public string CurrentTheme { get; private set; } = "cobalt-studio";

    public static string GetDisplayName(string themeId)
        => ThemeDisplayNames.TryGetValue(themeId, out var name) ? name : themeId;

    /// <summary>
    /// Apply the saved theme from local settings on startup (before network).
    /// </summary>
    public void ApplySavedTheme()
    {
        var settings = _settingsService.Load();
        var saved = settings.LastTheme;
        if (!string.IsNullOrEmpty(saved) && Themes.ContainsKey(saved))
        {
            ApplyTheme(saved);
        }
    }

    public void ApplyTheme(string themeName)
    {
        if (!Themes.TryGetValue(themeName, out var colors)) return;
        CurrentTheme = themeName;

        // Persist locally for next startup
        var settings = _settingsService.Load();
        settings.LastTheme = themeName;
        _settingsService.Save(settings);

        var res = Application.Current.Resources;

        // Backgrounds
        UpdateBrush(res, "AppBackgroundBrush", colors.Background);
        UpdateBrush(res, "SidebarBackgroundBrush", colors.Sidebar);
        UpdateBrush(res, "CardBackgroundBrush", colors.Card);
        UpdateBrush(res, "SurfaceBrush", colors.Surface);
        UpdateBrush(res, "SurfaceHoverBrush", colors.SurfaceHover);
        UpdateBrush(res, "SurfaceRaisedBrush", colors.SurfaceRaised);
        UpdateBrush(res, "BorderBrush", colors.Border);
        UpdateBrush(res, "InputBrush", colors.Input);
        UpdateBrush(res, "SecondaryBackgroundBrush", colors.Secondary);
        UpdateBrush(res, "AccentBackgroundBrush", colors.Accent);
        UpdateBrush(res, "SidebarAccentBrush", colors.Accent);
        UpdateBrush(res, "SidebarBorderBrush", colors.Border);

        // Text
        UpdateBrush(res, "PrimaryTextBrush", colors.Foreground);
        UpdateBrush(res, "SecondaryTextBrush", colors.MutedForeground);
        UpdateBrush(res, "SecondaryForegroundBrush", colors.SecondaryForeground);

        // Accent
        UpdateBrush(res, "AccentBrush", colors.Primary);
        UpdateBrush(res, "AccentForegroundBrush", colors.PrimaryForeground);

        // Status
        UpdateBrush(res, "ErrorBrush", colors.Destructive);

        // Badge text follows primary accent
        UpdateBrush(res, "BadgeTextBrush", colors.Primary);

        // Also update the raw Color resources so any new elements pick up the right values
        UpdateColor(res, "AppBackgroundColor", colors.Background);
        UpdateColor(res, "SidebarBackgroundColor", colors.Sidebar);
        UpdateColor(res, "CardBackgroundColor", colors.Card);
        UpdateColor(res, "SurfaceColor", colors.Surface);
        UpdateColor(res, "SurfaceHoverColor", colors.SurfaceHover);
        UpdateColor(res, "SurfaceRaisedColor", colors.SurfaceRaised);
        UpdateColor(res, "BorderColor", colors.Border);
        UpdateColor(res, "InputColor", colors.Input);
        UpdateColor(res, "SecondaryBackgroundColor", colors.Secondary);
        UpdateColor(res, "AccentBackgroundColor", colors.Accent);
        UpdateColor(res, "PrimaryTextColor", colors.Foreground);
        UpdateColor(res, "SecondaryTextColor", colors.MutedForeground);
        UpdateColor(res, "SecondaryForegroundColor", colors.SecondaryForeground);
        UpdateColor(res, "AccentColor", colors.Primary);
        UpdateColor(res, "AccentForegroundColor", colors.PrimaryForeground);
        UpdateColor(res, "ErrorColor", colors.Destructive);
    }

    private static void UpdateBrush(ResourceDictionary res, string key, string hex)
    {
        if (TryFindBrush(res, key, out var brush))
        {
            brush!.Color = ColorFromHex(hex);
        }
    }

    private static bool TryFindBrush(ResourceDictionary res, string key, out SolidColorBrush? brush)
    {
        if (res.TryGetValue(key, out var existing) && existing is SolidColorBrush scb)
        {
            brush = scb;
            return true;
        }

        foreach (var merged in res.MergedDictionaries)
        {
            if (TryFindBrush(merged, key, out brush))
                return true;
        }

        brush = null;
        return false;
    }

    private static void UpdateColor(ResourceDictionary res, string key, string hex)
    {
        var color = ColorFromHex(hex);
        if (res.ContainsKey(key))
        {
            res[key] = color;
            return;
        }
        foreach (var merged in res.MergedDictionaries)
        {
            if (merged.ContainsKey(key))
            {
                merged[key] = color;
                return;
            }
        }
    }

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 0xFF;
        byte r, g, b;

        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex[..2], 16);
            r = Convert.ToByte(hex[2..4], 16);
            g = Convert.ToByte(hex[4..6], 16);
            b = Convert.ToByte(hex[6..8], 16);
        }
        else if (hex.Length == 6)
        {
            r = Convert.ToByte(hex[..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
        }
        else
        {
            r = g = b = 0;
        }

        return Color.FromArgb(a, r, g, b);
    }
}
