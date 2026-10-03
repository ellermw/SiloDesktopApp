using Microsoft.UI.Xaml.Media;
using Windows.UI;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Services;

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
    private readonly SharedAppearanceState _sharedAppearance;


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
        ["midnight-cinema"] = new ThemeColors
        {
            Background = "#141417",
            Foreground = "#E8E8EC",
            Card = "#1C1C20",
            Primary = "#E8E8EC",
            PrimaryForeground = "#141417",
            Secondary = "#232328",
            SecondaryForeground = "#D0D0D6",
            // Current WebUI midnight-cinema token. The old #6E6E78 value made
            // admin subtitles, navigation, table metadata, and button text
            // visibly dimmer than the live UI.
            MutedForeground = "#9696A0",
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
    };

    public ThemeService(SettingsService settingsService, SettingsApi settingsApi)
    {
        _settingsService = settingsService;
        _sharedAppearance = new SharedAppearanceState(settingsApi);
        _sharedAppearance.Changed += () => ApplyTheme(CurrentTheme);
    }

    public string CurrentTheme => "midnight-cinema";
    public static bool IsLightAppearance(string themeId) => false;
    public void ApplySavedTheme() => ResetSharedAppearance();
    public void ResetSharedAppearance() => _sharedAppearance.Reset();
    public Task RefreshSharedAppearanceIfStaleAsync(CancellationToken cancellationToken = default)
        => _sharedAppearance.RefreshIfStaleAsync(cancellationToken);
    public Task SyncFromServerAsync(CancellationToken cancellationToken = default)
        => _sharedAppearance.RefreshAsync(cancellationToken);
    public void ApplyTheme(string themeName)
    {
        var colors = Themes[CurrentTheme];

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
        UpdateColor(res, "AmbientColor", colors.Foreground);
        UpdateBrush(res, "AmbientBrush", colors.Foreground);
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
        UpdateFontFamily(res, "ContentControlThemeFontFamily", colors.FontFamily);
        UpdateFontFamily(res, "ThemeDisplayFontFamily", colors.DisplayFontFamily);

        ApplyOverrideResources(_sharedAppearance.Colors);
        Helpers.PageBackdrop.RefreshAll();
        // Readability wins over server decoration, including refresh/reset.
        UpdateBrush(res, "TertiaryTextBrush", "#6E7681");
        UpdateColor(res, "TertiaryTextColor", "#6E7681");
        if (_settingsService.Load().UiHighContrast)
        {
            UpdateBrush(res, "PrimaryTextBrush", "#FFFFFF");
            UpdateBrush(res, "SecondaryTextBrush", "#D1D5DB");
            UpdateBrush(res, "TertiaryTextBrush", "#B6C0CE");
            UpdateBrush(res, "BorderBrush", "#8091A7");
            UpdateColor(res, "PrimaryTextColor", "#FFFFFF");
            UpdateColor(res, "SecondaryTextColor", "#D1D5DB");
            UpdateColor(res, "TertiaryTextColor", "#B6C0CE");
            UpdateColor(res, "BorderColor", "#8091A7");
        }
        RefreshDerivedControlResources(res);
    }

    private void ApplyOverrideResources(IReadOnlyDictionary<string, string> overrides)
    {
        var res = Application.Current.Resources;
        foreach (var (token, value) in overrides)
        {
            if (!OverrideResourceMap.TryGetValue(token, out var resources)) continue;
            UpdateBrush(res, resources.Brush, value);
            UpdateColor(res, resources.Color, value);
        }
        if (overrides.TryGetValue("primary", out var primary)) UpdateBrush(res, "BadgeTextBrush", primary);
    }
    private static readonly Dictionary<string, (string Brush, string Color)> OverrideResourceMap = new(StringComparer.Ordinal)
    {
        ["background"] = ("AppBackgroundBrush", "AppBackgroundColor"),
        ["ambient"] = ("AmbientBrush", "AmbientColor"),
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
        ["accent"] = ("AccentBackgroundBrush", "AccentBackgroundColor"),
        ["accent-foreground"] = ("ThemeAccentForegroundBrush", "ThemeAccentForegroundColor"),
        ["card-foreground"] = ("CardForegroundBrush", "CardForegroundColor"),
        ["chart-1"] = ("Chart1Brush", "Chart1Color"),
        ["chart-2"] = ("Chart2Brush", "Chart2Color"),
        ["chart-3"] = ("Chart3Brush", "Chart3Color"),
        ["chart-4"] = ("Chart4Brush", "Chart4Color"),
        ["chart-5"] = ("Chart5Brush", "Chart5Color"),
        ["destructive-foreground"] = ("DestructiveForegroundBrush", "DestructiveForegroundColor"),
        ["muted"] = ("MutedBrush", "MutedColor"),
        ["popover"] = ("PopoverBrush", "PopoverColor"),
        ["popover-foreground"] = ("PopoverForegroundBrush", "PopoverForegroundColor"),
        ["ring"] = ("RingBrush", "RingColor"),
        ["sidebar-accent-foreground"] = ("SidebarAccentForegroundBrush", "SidebarAccentForegroundColor"),
        ["sidebar-foreground"] = ("SidebarForegroundBrush", "SidebarForegroundColor"),
        ["sidebar-primary"] = ("SidebarPrimaryBrush", "SidebarPrimaryColor"),
        ["sidebar-primary-foreground"] = ("SidebarPrimaryForegroundBrush", "SidebarPrimaryForegroundColor"),
        ["sidebar-ring"] = ("SidebarRingBrush", "SidebarRingColor"),
        ["surface-raised"] = ("SurfaceRaisedBrush", "SurfaceRaisedColor"),
    };

    private static void UpdateBrush(ResourceDictionary res, string key, string hex)
    {
        if (TryFindBrush(res, key, out var brush))
        {
            brush!.Color = ColorFromHex(hex);
        }
    }

    private static void RefreshDerivedControlResources(ResourceDictionary resources)
    {
        CopyBrushColor(resources, "SurfaceBrush",
            "ButtonBackground", "ButtonBackgroundDisabled");
        CopyBrushColor(resources, "SurfaceHoverBrush",
            "ButtonBackgroundPointerOver", "TextControlBackgroundPointerOver",
            "ComboBoxBackgroundPointerOver", "MenuFlyoutItemBackgroundPointerOver");
        CopyBrushColor(resources, "SurfaceRaisedBrush",
            "ButtonBackgroundPressed", "ComboBoxBackgroundPressed",
            "MenuFlyoutItemBackgroundPressed", "SliderTrackFill");
        CopyBrushColor(resources, "InputBrush",
            "TextControlBackground", "TextControlBackgroundFocused",
            "ComboBoxBackground", "ComboBoxBackgroundFocused");
        CopyBrushColor(resources, "PrimaryTextBrush",
            "ButtonForeground", "ButtonForegroundPointerOver",
            "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
            "ComboBoxForeground", "ComboBoxItemForeground", "ComboBoxItemForegroundSelected",
            "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver",
            "NavigationViewItemForeground", "NavigationViewItemForegroundPointerOver",
            "ToggleSwitchKnobFillOff", "ToggleSwitchKnobFillOffPointerOver");
        CopyBrushColor(resources, "SecondaryTextBrush",
            "ButtonForegroundPressed");
        CopyBrushColor(resources, "TertiaryTextBrush",
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TextControlPlaceholderForegroundFocused");
        CopyBrushColor(resources, "BorderBrush",
            "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "TextControlBorderBrush", "ComboBoxBorderBrush", "ComboBoxDropDownBorderBrush",
            "ContentDialogBorderBrush", "MenuFlyoutPresenterBorderBrush",
            "ToggleSwitchStrokeOff", "ToggleSwitchStrokeOffPointerOver");
        CopyBrushColor(resources, "AccentBrush",
            "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
            "ComboBoxBorderBrushPointerOver",
            "SystemControlHighlightAccentBrush", "NavigationViewItemForegroundSelected",
            "HyperlinkButtonForeground", "HyperlinkButtonForegroundPointerOver", "HyperlinkButtonForegroundPressed",
            "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
            "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver",
            "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
            "SliderThumbBorderBrush", "SliderThumbBorderBrushPointerOver", "SliderThumbBorderBrushPressed");
        CopyBrushColor(resources, "RingBrush", "FocusVisualPrimaryBrush");
        CopyBrushColor(resources, "AccentForegroundBrush", "ToggleSwitchKnobFillOn");
        CopyBrushColor(resources, "AccentBrush", "WebUiPrimaryHoverBrush");
        CopyBrushColor(resources, "SecondaryBackgroundBrush", "WebUiSecondaryHoverBrush");
        CopyBrushColor(resources, "ErrorBrush", "WebUiDestructiveHoverBrush");
        CopyBrushColor(resources, "AccentBackgroundBrush",
            "ComboBoxItemBackgroundSelected", "SystemControlHighlightListAccentLowBrush",
            "SystemControlHighlightListAccentMediumBrush", "NavigationViewItemBackgroundSelected");
        CopyBrushColor(resources, "PopoverBrush", "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground");
        CopyBrushColor(resources, "AppBackgroundBrush", "NavigationViewContentBackground", "ContentDialogBackground",
            "SliderOuterThumbBackground", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed");
        CopyBrushColor(resources, "SurfaceHoverBrush", "NavigationViewItemBackgroundPointerOver");
        CopyBrushColor(resources, "BorderBrush", "ToggleSwitchFillOff", "ToggleSwitchFillOffPointerOver");
    }

    private static void CopyBrushColor(ResourceDictionary resources, string sourceKey, params string[] targetKeys)
    {
        if (!TryFindBrush(resources, sourceKey, out var source) || source is null)
            return;

        foreach (var targetKey in targetKeys)
        {
            if (TryFindBrush(resources, targetKey, out var target) && target is not null)
                target.Color = source.Color;
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
