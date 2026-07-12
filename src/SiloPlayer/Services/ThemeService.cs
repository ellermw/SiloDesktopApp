using Microsoft.UI.Xaml.Media;
using Windows.UI;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Services;

/// <summary>Metadata for a single theme used in the settings picker.</summary>
public record ThemeInfo(
    string Id,
    string Label,
    string Description,
    string PreviewAccent,
    string PreviewBackground,
    bool IsCurated,
    string FontFamily);

public class ThemeColors
{
    // ===== Existing properties =====
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

    // ===== New semantic color tokens =====
    public string Popover { get; set; } = "";
    public string PopoverForeground { get; set; } = "";
    public string CardForeground { get; set; } = "";
    public string AccentForeground { get; set; } = "";
    public string DestructiveForeground { get; set; } = "";
    public string Muted { get; set; } = "";
    public string Ring { get; set; } = "";
    public string Chart1 { get; set; } = "";
    public string Chart2 { get; set; } = "";
    public string Chart3 { get; set; } = "";
    public string Chart4 { get; set; } = "";
    public string Chart5 { get; set; } = "";
    public string SidebarForeground { get; set; } = "";
    public string SidebarPrimary { get; set; } = "";
    public string SidebarPrimaryForeground { get; set; } = "";
    public string SidebarAccent { get; set; } = "";
    public string SidebarAccentForeground { get; set; } = "";
    public string SidebarBorder { get; set; } = "";
    public string SidebarRing { get; set; } = "";

    // ===== Font properties =====
    public string FontFamily { get; set; } = "Outfit";
    public string DisplayFontFamily { get; set; } = "Outfit";
}

public class ThemeService
{
    private readonly SettingsService _settingsService;
    private readonly SettingsApi _settingsApi;
    private string? _previewBaseTheme;

    /// <summary>Maps font family name to the ms-appx font URI for WinUI 3.</summary>
    private static readonly Dictionary<string, string> FontUris = new()
    {
        ["Outfit"] = "ms-appx:///Assets/Fonts/Outfit-VariableFont_wght.ttf#Outfit",
        ["Manrope"] = "ms-appx:///Assets/Fonts/Manrope-VariableFont_wght.ttf#Manrope",
        ["Sora"] = "ms-appx:///Assets/Fonts/Sora-VariableFont_wght.ttf#Sora",
        ["Urbanist"] = "ms-appx:///Assets/Fonts/Urbanist-VariableFont_wght.ttf#Urbanist",
    };

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
            // New tokens
            Popover = "#1E1E2E",
            PopoverForeground = "#CDD6F4",
            CardForeground = "#CDD6F4",
            AccentForeground = "#CDD6F4",
            DestructiveForeground = "#1E1E2E",
            Muted = "#313244",
            Ring = "#CBA6F7",
            Chart1 = "#CBA6F7",
            Chart2 = "#89B4FA",
            Chart3 = "#A6E3A1",
            Chart4 = "#FAB387",
            Chart5 = "#F5C2E7",
            SidebarForeground = "#CDD6F4",
            SidebarPrimary = "#CBA6F7",
            SidebarPrimaryForeground = "#1E1E2E",
            SidebarAccent = "#45475A",
            SidebarAccentForeground = "#CDD6F4",
            SidebarBorder = "#45475A",
            SidebarRing = "#CBA6F7",
            // Fonts
            FontFamily = "Outfit",
            DisplayFontFamily = "Outfit",
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
            // New tokens
            Popover = "#282828",
            PopoverForeground = "#EBDBB2",
            CardForeground = "#EBDBB2",
            AccentForeground = "#EBDBB2",
            DestructiveForeground = "#EBDBB2",
            Muted = "#3C3836",
            Ring = "#FABD2F",
            Chart1 = "#FABD2F",
            Chart2 = "#B8BB26",
            Chart3 = "#83A598",
            Chart4 = "#FE8019",
            Chart5 = "#D3869B",
            SidebarForeground = "#EBDBB2",
            SidebarPrimary = "#FABD2F",
            SidebarPrimaryForeground = "#282828",
            SidebarAccent = "#504945",
            SidebarAccentForeground = "#EBDBB2",
            SidebarBorder = "#504945",
            SidebarRing = "#FABD2F",
            // Fonts
            FontFamily = "Manrope",
            DisplayFontFamily = "Manrope",
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
            // New tokens
            Popover = "#0D1117",
            PopoverForeground = "#C9D1D9",
            CardForeground = "#C9D1D9",
            AccentForeground = "#C9D1D9",
            DestructiveForeground = "#0D1117",
            Muted = "#161B22",
            Ring = "#58A6FF",
            Chart1 = "#58A6FF",
            Chart2 = "#79C0FF",
            Chart3 = "#56D364",
            Chart4 = "#F78166",
            Chart5 = "#D2A8FF",
            SidebarForeground = "#C9D1D9",
            SidebarPrimary = "#58A6FF",
            SidebarPrimaryForeground = "#0D1117",
            SidebarAccent = "#21262D",
            SidebarAccentForeground = "#C9D1D9",
            SidebarBorder = "#30363D",
            SidebarRing = "#58A6FF",
            // Fonts
            FontFamily = "Manrope",
            DisplayFontFamily = "Manrope",
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
            // New tokens
            Popover = "#1C1C1E",
            PopoverForeground = "#F2F2F7",
            CardForeground = "#F2F2F7",
            AccentForeground = "#F2F2F7",
            DestructiveForeground = "#FFFFFF",
            Muted = "#2C2C2E",
            Ring = "#0A84FF",
            Chart1 = "#0A84FF",
            Chart2 = "#5E5CE6",
            Chart3 = "#30D158",
            Chart4 = "#FF9F0A",
            Chart5 = "#BF5AF2",
            SidebarForeground = "#F2F2F7",
            SidebarPrimary = "#0A84FF",
            SidebarPrimaryForeground = "#FFFFFF",
            SidebarAccent = "#38383A",
            SidebarAccentForeground = "#F2F2F7",
            SidebarBorder = "#38383A",
            SidebarRing = "#0A84FF",
            // Fonts
            FontFamily = "Outfit",
            DisplayFontFamily = "Outfit",
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
            // New tokens
            Popover = "#18181B",
            PopoverForeground = "#FAFAFA",
            CardForeground = "#FAFAFA",
            AccentForeground = "#FAFAFA",
            DestructiveForeground = "#FAFAFA",
            Muted = "#27272A",
            Ring = "#A855F7",
            Chart1 = "#A855F7",
            Chart2 = "#EC4899",
            Chart3 = "#14B8A6",
            Chart4 = "#F97316",
            Chart5 = "#06B6D4",
            SidebarForeground = "#FAFAFA",
            SidebarPrimary = "#A855F7",
            SidebarPrimaryForeground = "#18181B",
            SidebarAccent = "#3F3F46",
            SidebarAccentForeground = "#FAFAFA",
            SidebarBorder = "#3F3F46",
            SidebarRing = "#A855F7",
            // Fonts
            FontFamily = "Sora",
            DisplayFontFamily = "Sora",
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
            // New tokens
            Popover = "#0F0F0F",
            PopoverForeground = "#F5F5F5",
            CardForeground = "#F5F5F5",
            AccentForeground = "#F5F5F5",
            DestructiveForeground = "#0F0F0F",
            Muted = "#1A1A1A",
            Ring = "#00D4AA",
            Chart1 = "#00D4AA",
            Chart2 = "#00A3CC",
            Chart3 = "#4ADE80",
            Chart4 = "#FB923C",
            Chart5 = "#FF6B9D",
            SidebarForeground = "#F5F5F5",
            SidebarPrimary = "#00D4AA",
            SidebarPrimaryForeground = "#0F0F0F",
            SidebarAccent = "#262626",
            SidebarAccentForeground = "#F5F5F5",
            SidebarBorder = "#2A2A2A",
            SidebarRing = "#00D4AA",
            // Fonts
            FontFamily = "Urbanist",
            DisplayFontFamily = "Urbanist",
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
            // New tokens
            Popover = "#18181C",
            PopoverForeground = "#E8E8EC",
            CardForeground = "#E8E8EC",
            AccentForeground = "#E8E8EC",
            DestructiveForeground = "#FFFFFF",
            Muted = "#1C1C20",
            Ring = "#E8E8EC",
            Chart1 = "#8B9CF7",
            Chart2 = "#7EC8E3",
            Chart3 = "#81C995",
            Chart4 = "#E8A87C",
            Chart5 = "#C78DBD",
            SidebarForeground = "#E8E8EC",
            SidebarPrimary = "#E8E8EC",
            SidebarPrimaryForeground = "#0F0F12",
            SidebarAccent = "#1C1C20",
            SidebarAccentForeground = "#E8E8EC",
            SidebarBorder = "#232328",
            SidebarRing = "#E8E8EC",
            // Fonts
            FontFamily = "Outfit",
            DisplayFontFamily = "Outfit",
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
            // New tokens
            Popover = "#FFFFFF",
            PopoverForeground = "#1A1A1E",
            CardForeground = "#1A1A1E",
            AccentForeground = "#1A1A1E",
            DestructiveForeground = "#FFFFFF",
            Muted = "#EBEBEF",
            Ring = "#1A1A1E",
            Chart1 = "#6366F1",
            Chart2 = "#0EA5E9",
            Chart3 = "#22C55E",
            Chart4 = "#F59E0B",
            Chart5 = "#A855F7",
            SidebarForeground = "#1A1A1E",
            SidebarPrimary = "#1A1A1E",
            SidebarPrimaryForeground = "#F4F4F6",
            SidebarAccent = "#DFDFE5",
            SidebarAccentForeground = "#1A1A1E",
            SidebarBorder = "#D0D0D8",
            SidebarRing = "#1A1A1E",
            // Fonts
            FontFamily = "Outfit",
            DisplayFontFamily = "Outfit",
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
            // New tokens
            Popover = "#121A25",
            PopoverForeground = "#F4F8FF",
            CardForeground = "#F4F8FF",
            AccentForeground = "#F4F8FF",
            DestructiveForeground = "#FFFFFF",
            Muted = "#151E2B",
            Ring = "#78AEFC",
            Chart1 = "#78AEFC",
            Chart2 = "#87D2FF",
            Chart3 = "#6EC7BA",
            Chart4 = "#B9C7FF",
            Chart5 = "#F18D8D",
            SidebarForeground = "#F4F8FF",
            SidebarPrimary = "#78AEFC",
            SidebarPrimaryForeground = "#0F1722",
            SidebarAccent = "#172231",
            SidebarAccentForeground = "#F4F8FF",
            SidebarBorder = "#1F2A39",
            SidebarRing = "#78AEFC",
            // Fonts
            FontFamily = "Outfit",
            DisplayFontFamily = "Outfit",
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
            // New tokens
            Popover = "#1B1417",
            PopoverForeground = "#F8F2F3",
            CardForeground = "#F8F2F3",
            AccentForeground = "#F8F2F3",
            DestructiveForeground = "#FFFFFF",
            Muted = "#20181B",
            Ring = "#D16A78",
            Chart1 = "#D16A78",
            Chart2 = "#F08E7A",
            Chart3 = "#C8A0B8",
            Chart4 = "#8AA0C6",
            Chart5 = "#E1B86E",
            SidebarForeground = "#F8F2F3",
            SidebarPrimary = "#D16A78",
            SidebarPrimaryForeground = "#180F12",
            SidebarAccent = "#21161A",
            SidebarAccentForeground = "#F8F2F3",
            SidebarBorder = "#2C1D23",
            SidebarRing = "#D16A78",
            // Fonts
            FontFamily = "Outfit",
            DisplayFontFamily = "Outfit",
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
            // New tokens
            Popover = "#181516",
            PopoverForeground = "#F7F3F2",
            CardForeground = "#F7F3F2",
            AccentForeground = "#F7F3F2",
            DestructiveForeground = "#FFFFFF",
            Muted = "#1D191B",
            Ring = "#F07B62",
            Chart1 = "#F07B62",
            Chart2 = "#F0A162",
            Chart3 = "#D4B078",
            Chart4 = "#8DB6C9",
            Chart5 = "#D497AC",
            SidebarForeground = "#F7F3F2",
            SidebarPrimary = "#F07B62",
            SidebarPrimaryForeground = "#1A1110",
            SidebarAccent = "#201A1C",
            SidebarAccentForeground = "#F7F3F2",
            SidebarBorder = "#2B2326",
            SidebarRing = "#F07B62",
            // Fonts
            FontFamily = "Urbanist",
            DisplayFontFamily = "Urbanist",
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
            // New tokens
            Popover = "#131B19",
            PopoverForeground = "#F2F8F5",
            CardForeground = "#F2F8F5",
            AccentForeground = "#F2F8F5",
            DestructiveForeground = "#FFFFFF",
            Muted = "#16201D",
            Ring = "#5BC39D",
            Chart1 = "#5BC39D",
            Chart2 = "#7DD8BD",
            Chart3 = "#7EB7A4",
            Chart4 = "#88A9CF",
            Chart5 = "#E0B66B",
            SidebarForeground = "#F2F8F5",
            SidebarPrimary = "#5BC39D",
            SidebarPrimaryForeground = "#0D1513",
            SidebarAccent = "#16221E",
            SidebarAccentForeground = "#F2F8F5",
            SidebarBorder = "#1D2F2A",
            SidebarRing = "#5BC39D",
            // Fonts
            FontFamily = "Outfit",
            DisplayFontFamily = "Outfit",
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
            // New tokens
            Popover = "#101916",
            PopoverForeground = "#F3F9F7",
            CardForeground = "#F3F9F7",
            AccentForeground = "#F3F9F7",
            DestructiveForeground = "#FFFFFF",
            Muted = "#121D1A",
            Ring = "#86D4B6",
            Chart1 = "#86D4B6",
            Chart2 = "#6FC7C4",
            Chart3 = "#9FDC8D",
            Chart4 = "#8EB0E8",
            Chart5 = "#F0B36B",
            SidebarForeground = "#F3F9F7",
            SidebarPrimary = "#86D4B6",
            SidebarPrimaryForeground = "#0D1513",
            SidebarAccent = "#14211D",
            SidebarAccentForeground = "#F3F9F7",
            SidebarBorder = "#1C302A",
            SidebarRing = "#86D4B6",
            // Fonts
            FontFamily = "Urbanist",
            DisplayFontFamily = "Urbanist",
        },
    };

    /// <summary>Metadata for each theme used in the picker UI.</summary>
    private static readonly Dictionary<string, ThemeInfo> ThemeInfos = new()
    {
        ["midnight-cinema"] = new("midnight-cinema", "Cinema Dark", "Monochromatic cinema -- content is the color", "#E8E8EC", "#141417", true, "Outfit"),
        ["cinema-light"] = new("cinema-light", "Cinema Light", "Light monochromatic cinema -- content is the color", "#1A1A1E", "#F4F4F6", true, "Outfit"),
        ["cobalt-studio"] = new("cobalt-studio", "Cobalt", "Cool blue graphite with crisp contrast", "#78AEFC", "#101722", true, "Outfit"),
        ["oxblood-noir"] = new("oxblood-noir", "Oxblood", "Deep red-black with restrained luxury warmth", "#D16A78", "#171113", true, "Outfit"),
        ["evergreen-studio"] = new("evergreen-studio", "Evergreen", "Refined evergreen accents on dense graphite", "#5BC39D", "#101715", true, "Outfit"),
        ["ember-slate"] = new("ember-slate", "Ember", "Smoked charcoal with ember-red accents", "#F07B62", "#151213", false, "Urbanist"),
        ["verdant-ink"] = new("verdant-ink", "Verdant Ink", "Cool green-black with softer luminous contrast", "#86D4B6", "#0D1513", false, "Urbanist"),
        ["catppuccin"] = new("catppuccin", "Catppuccin", "Pastel purple on warm dark blue", "#CBA6F7", "#1E1E2E", false, "Outfit"),
        ["gruvbox"] = new("gruvbox", "Gruvbox", "Warm retro with golden accent", "#FABD2F", "#282828", false, "Manrope"),
        ["void-space"] = new("void-space", "Void Space", "Cool blue on deep space black", "#58A6FF", "#0D1117", false, "Manrope"),
        ["charcoal-studio"] = new("charcoal-studio", "Charcoal", "Apple-inspired blue on dark gray", "#0A84FF", "#1C1C1E", false, "Outfit"),
        ["graphite-pro"] = new("graphite-pro", "Graphite", "Vibrant purple on zinc", "#A855F7", "#18181B", false, "Sora"),
        ["obsidian-depth"] = new("obsidian-depth", "Obsidian", "Cyan accent on true dark", "#00D4AA", "#0F0F0F", false, "Urbanist"),
    };

    public ThemeService(SettingsService settingsService, SettingsApi settingsApi)
    {
        _settingsService = settingsService;
        _settingsApi = settingsApi;
    }

    public IReadOnlyList<string> AvailableThemeIds => Themes.Keys.ToList();

    public string CurrentTheme { get; private set; } = "midnight-cinema";

    // Single source of truth for theme labels: ThemeInfos.Label. WebUI uses
    // Cinema Dark / Cinema Light / Cobalt / Oxblood / Ember / etc. — not the
    // "Midnight Cinema / Cobalt Studio / Ember Slate" labels from the old dict.
    public static string GetDisplayName(string themeId)
        => ThemeInfos.TryGetValue(themeId, out var info) ? info.Label : themeId;

    /// <summary>Returns theme metadata for the picker UI. Curated themes are returned first.</summary>
    public static IReadOnlyList<ThemeInfo> GetAllThemeInfos()
    {
        var curated = ThemeInfos.Values.Where(t => t.IsCurated).ToList();
        var others = ThemeInfos.Values.Where(t => !t.IsCurated).ToList();
        curated.AddRange(others);
        return curated;
    }

    /// <summary>Returns theme metadata for a single theme.</summary>
    public static ThemeInfo? GetThemeInfo(string themeId)
        => ThemeInfos.TryGetValue(themeId, out var info) ? info : null;

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

        // ===== Existing brush/color updates =====

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
        UpdateBrush(res, "SidebarAccentBrush", colors.SidebarAccent);
        UpdateBrush(res, "SidebarBorderBrush", colors.SidebarBorder);

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

        // ===== New semantic token colors and brushes =====

        // Popover
        UpdateColor(res, "PopoverColor", colors.Popover);
        UpdateColor(res, "PopoverForegroundColor", colors.PopoverForeground);
        UpdateBrush(res, "PopoverBrush", colors.Popover);
        UpdateBrush(res, "PopoverForegroundBrush", colors.PopoverForeground);

        // Card foreground
        UpdateColor(res, "CardForegroundColor", colors.CardForeground);
        UpdateBrush(res, "CardForegroundBrush", colors.CardForeground);

        // Accent foreground (theme-level, not primary button)
        UpdateColor(res, "ThemeAccentForegroundColor", colors.AccentForeground);
        UpdateBrush(res, "ThemeAccentForegroundBrush", colors.AccentForeground);

        // Destructive foreground
        UpdateColor(res, "DestructiveForegroundColor", colors.DestructiveForeground);
        UpdateBrush(res, "DestructiveForegroundBrush", colors.DestructiveForeground);

        // Muted
        UpdateColor(res, "MutedColor", colors.Muted);
        UpdateBrush(res, "MutedBrush", colors.Muted);

        // Ring
        UpdateColor(res, "RingColor", colors.Ring);
        UpdateBrush(res, "RingBrush", colors.Ring);

        // Chart colors
        UpdateColor(res, "Chart1Color", colors.Chart1);
        UpdateColor(res, "Chart2Color", colors.Chart2);
        UpdateColor(res, "Chart3Color", colors.Chart3);
        UpdateColor(res, "Chart4Color", colors.Chart4);
        UpdateColor(res, "Chart5Color", colors.Chart5);
        UpdateBrush(res, "Chart1Brush", colors.Chart1);
        UpdateBrush(res, "Chart2Brush", colors.Chart2);
        UpdateBrush(res, "Chart3Brush", colors.Chart3);
        UpdateBrush(res, "Chart4Brush", colors.Chart4);
        UpdateBrush(res, "Chart5Brush", colors.Chart5);

        // Sidebar tokens (new dedicated sidebar colors)
        UpdateColor(res, "SidebarForegroundColor", colors.SidebarForeground);
        UpdateColor(res, "SidebarPrimaryColor", colors.SidebarPrimary);
        UpdateColor(res, "SidebarPrimaryForegroundColor", colors.SidebarPrimaryForeground);
        UpdateColor(res, "SidebarAccentColor", colors.SidebarAccent);
        UpdateColor(res, "SidebarAccentForegroundColor", colors.SidebarAccentForeground);
        UpdateColor(res, "SidebarBorderColor", colors.SidebarBorder);
        UpdateColor(res, "SidebarRingColor", colors.SidebarRing);
        UpdateBrush(res, "SidebarForegroundBrush", colors.SidebarForeground);
        UpdateBrush(res, "SidebarPrimaryBrush", colors.SidebarPrimary);
        UpdateBrush(res, "SidebarPrimaryForegroundBrush", colors.SidebarPrimaryForeground);
        // SidebarAccentBrush and SidebarBorderBrush already updated in existing section above
        UpdateBrush(res, "SidebarAccentForegroundBrush", colors.SidebarAccentForeground);
        UpdateBrush(res, "SidebarRingBrush", colors.SidebarRing);

        // ===== Font switching =====
        UpdateFontFamily(res, "ThemeFontFamily", colors.FontFamily);
        UpdateFontFamily(res, "ThemeDisplayFontFamily", colors.DisplayFontFamily);

        ApplyOverrideResources(_settingsService.Load().ThemeOverrides);
    }

    public IReadOnlyDictionary<string, string> GetThemeOverrides()
        => new Dictionary<string, string>(_settingsService.Load().ThemeOverrides, StringComparer.Ordinal);

    public void SetThemeOverride(string token, string value)
    {
        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(value)) settings.ThemeOverrides.Remove(token);
        else settings.ThemeOverrides[token] = value.Trim();
        _settingsService.Save(settings);
        ApplyTheme(CurrentTheme);
        _ = PersistThemeOverridesAsync(settings.ThemeOverrides);
    }

    public void ImportThemeOverrides(Dictionary<string, string> overrides)
    {
        var settings = _settingsService.Load();
        settings.ThemeOverrides = overrides
            .Where(pair => IsSupportedOverride(pair.Key, pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _settingsService.Save(settings);
        ApplyTheme(CurrentTheme);
        _ = PersistThemeOverridesAsync(settings.ThemeOverrides);
    }

    public void ResetThemeOverrides()
    {
        var settings = _settingsService.Load();
        settings.ThemeOverrides.Clear();
        _settingsService.Save(settings);
        ApplyTheme(CurrentTheme);
        _ = PersistThemeOverridesAsync(settings.ThemeOverrides);
    }

    public void SetThemeOverridesFromServer(Dictionary<string, string> overrides)
    {
        var settings = _settingsService.Load();
        settings.ThemeOverrides = overrides
            .Where(pair => IsSupportedOverride(pair.Key, pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _settingsService.Save(settings);
        ApplyTheme(CurrentTheme);
    }

    private async Task PersistThemeOverridesAsync(Dictionary<string, string> overrides)
    {
        try
        {
            await _settingsApi.PutSettingAsync("ui_custom_theme_vars",
                System.Text.Json.JsonSerializer.Serialize(overrides));
        }
        catch { /* local copy remains available and a later edit retries */ }
    }

    private void ApplyOverrideResources(IReadOnlyDictionary<string, string> overrides)
    {
        var res = Application.Current.Resources;
        foreach (var (token, value) in overrides)
        {
            if (!IsSupportedOverride(token, value)) continue;
            if (token == "font-body")
            {
                UpdateFontFamily(res, "ThemeFontFamily", ParseFontName(value));
                continue;
            }

            if (!OverrideResourceMap.TryGetValue(token, out var resources)) continue;
            UpdateBrush(res, resources.Brush, value);
            UpdateColor(res, resources.Color, value);
        }
    }

    private static bool IsSupportedOverride(string token, string value)
        => token == "font-body"
            ? FontUris.ContainsKey(ParseFontName(value))
            : OverrideResourceMap.ContainsKey(token) && IsHexColor(value);

    private static bool IsHexColor(string value)
        => value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private static string ParseFontName(string value)
        => value.Split(',')[0].Trim().Trim('"', '\'');

    private static readonly Dictionary<string, (string Brush, string Color)> OverrideResourceMap = new(StringComparer.Ordinal)
    {
        ["background"] = ("AppBackgroundBrush", "AppBackgroundColor"),
        ["foreground"] = ("PrimaryTextBrush", "PrimaryTextColor"),
        ["card"] = ("CardBackgroundBrush", "CardBackgroundColor"),
        ["surface"] = ("SurfaceBrush", "SurfaceColor"),
        ["surface-hover"] = ("SurfaceHoverBrush", "SurfaceHoverColor"),
        ["primary"] = ("AccentBrush", "AccentColor"),
        ["primary-foreground"] = ("AccentForegroundBrush", "AccentForegroundColor"),
        ["secondary"] = ("SecondaryBackgroundBrush", "SecondaryBackgroundColor"),
        ["secondary-foreground"] = ("SecondaryForegroundBrush", "SecondaryForegroundColor"),
        ["muted-foreground"] = ("SecondaryTextBrush", "SecondaryTextColor"),
        ["border"] = ("BorderBrush", "BorderColor"),
        ["input"] = ("InputBrush", "InputColor"),
        ["sidebar"] = ("SidebarBackgroundBrush", "SidebarBackgroundColor"),
        ["sidebar-accent"] = ("SidebarAccentBrush", "SidebarAccentColor"),
        ["sidebar-border"] = ("SidebarBorderBrush", "SidebarBorderColor"),
        ["destructive"] = ("ErrorBrush", "ErrorColor"),
    };

    public void PreviewTheme(string themeName)
    {
        if (_previewBaseTheme == null) _previewBaseTheme = CurrentTheme;
        ApplyTheme(themeName);
    }

    public void CancelThemePreview()
    {
        if (_previewBaseTheme == null) return;
        var original = _previewBaseTheme;
        _previewBaseTheme = null;
        ApplyTheme(original);
    }

    public void CommitThemePreview(string themeName)
    {
        _previewBaseTheme = null;
        ApplyTheme(themeName);
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

    private static void UpdateFontFamily(ResourceDictionary res, string key, string fontName)
    {
        var uri = FontUris.TryGetValue(fontName, out var u) ? u : fontName;
        var fontFamily = new FontFamily(uri);

        if (res.ContainsKey(key))
        {
            res[key] = fontFamily;
            return;
        }
        foreach (var merged in res.MergedDictionaries)
        {
            if (merged.ContainsKey(key))
            {
                merged[key] = fontFamily;
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
