using System.Text.RegularExpressions;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// The shared destructive (red) roles against the official WebUI (web/src/app.css at 02a9d1d3:
/// --destructive-base #ef4444 line 670, --destructive-foreground #ffffff line 671, --destructive-fill
/// #dc2626 line 672, --destructive-label line 595, high contrast line 844). The one official theme is
/// midnight-cinema; the only server appearance override is the accent, never a red.
/// </summary>
public sealed class DestructiveThemeTests
{
    private const string Fill = "#DC2626";
    private const string White = "#FFFFFF";

    // Surfaces of midnight-cinema the red sits on (app.css 636-660 and the ThemeService table).
    public static TheoryData<string, string> Surfaces => new()
    {
        { "page", "#141417" }, { "card", "#1C1C20" }, { "popover", "#18181C" },
        { "raised", "#28282E" }, { "surface-hover", "#232328" }, { "sidebar", "#0F0F12" },
    };

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([TestRepository.Root, .. parts])).ReplaceLineEndings("\n");

    [Fact]
    public void Defaults_are_the_official_midnight_cinema_tokens()
    {
        var tokens = DestructiveTheme.Resolve(null, null, null, highContrast: false);
        Assert.Equal("#EF4444", tokens.Text);
        Assert.Equal("#DC2626", tokens.Fill);
        Assert.Equal("#FFFFFF", tokens.Foreground);
        // --destructive-label: color-mix(in srgb, white 25%, #ef4444).
        Assert.Equal("#F37373", tokens.Label);
    }

    [Fact]
    public void The_built_in_theme_declares_the_official_tokens()
    {
        var service = Read("src", "SiloPlayer", "Services", "ThemeService.cs");
        string Value(string property)
        {
            var match = Regex.Match(service, $@"\b{property} = ""(?<hex>#[0-9A-Fa-f]{{6}})"",");
            Assert.True(match.Success, property);
            return match.Groups["hex"].Value.ToUpperInvariant();
        }
        Assert.Equal("#EF4444", Value("Destructive"));
        Assert.Equal("#DC2626", Value("DestructiveFill"));
        Assert.Equal("#FFFFFF", Value("DestructiveForeground"));
        Assert.Equal(1, Regex.Matches(service, @"\[""[a-z-]+""\] = new ThemeColors").Count); // midnight-cinema is the only theme
        Assert.Equal(DestructiveTheme.Resolve(null, null, null, false),
            DestructiveTheme.Resolve(Value("Destructive"), Value("DestructiveFill"), Value("DestructiveForeground"), false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    public void A_theme_that_omits_a_token_inherits_the_default(string? omitted)
    {
        var tokens = DestructiveTheme.Resolve(omitted, omitted, omitted, highContrast: false);
        Assert.Equal(DestructiveTheme.Resolve(null, null, null, false), tokens);
    }

    [Fact]
    public void A_theme_supplies_each_red_token_and_nothing_else_can()
    {
        // The theme's own tokens are used as written (normalized to upper case).
        var themed = DestructiveTheme.Resolve("#f87171", "#b91c1c", "#fafafa", false);
        Assert.Equal("#F87171", themed.Text);
        Assert.Equal("#B91C1C", themed.Fill);
        Assert.Equal("#FAFAFA", themed.Foreground);
        Assert.Equal("#FA9595", themed.Label); // 25% white over #F87171

        // The server sends only branding accent keys, so Resolve has no override input at all: no
        // destructive fill/foreground pair can arrive from outside the theme without a contrast check.
        var parameters = typeof(DestructiveTheme).GetMethod(nameof(DestructiveTheme.Resolve))!.GetParameters();
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType.IsGenericType
            && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

        // ThemeService passes the theme's tokens only and the server override map has no red key.
        var service = Read("src", "SiloPlayer", "Services", "ThemeService.cs");
        Assert.Contains("DestructiveTheme.Resolve(colors.Destructive, colors.DestructiveFill, colors.DestructiveForeground, highContrast);", service);
        var map = service[service.IndexOf("OverrideResourceMap = new", StringComparison.Ordinal)..];
        map = map[..map.IndexOf("};", StringComparison.Ordinal)];
        Assert.DoesNotContain("destructive", map, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Error", map);
    }

    [Fact]
    public void High_contrast_lightens_the_text_and_label_and_leaves_the_fill()
    {
        var tokens = DestructiveTheme.Resolve(null, null, null, highContrast: true);
        // --destructive: color-mix(in srgb, white 65%, #ef4444); --destructive-label: var(--destructive).
        Assert.Equal("#F9BEBE", tokens.Text);
        Assert.Equal(tokens.Text, tokens.Label);
        Assert.Equal("#DC2626", tokens.Fill);
        Assert.Equal("#FFFFFF", tokens.Foreground);

        var themed = DestructiveTheme.Resolve("#B91C1C", null, null, true);
        Assert.Equal(DestructiveTheme.Mix(White, 0.65, "#B91C1C"), themed.Text);
    }

    [Fact]
    public void Mix_and_Over_are_srgb_blends()
    {
        Assert.Equal("#F37373", DestructiveTheme.Mix(White, 0.25, "#EF4444"));
        Assert.Equal("#808080", DestructiveTheme.Mix(White, 0.5, "#000000"));
        Assert.Equal("#C82425", DestructiveTheme.Over(Fill, 0.9, "#141417"));
        Assert.Equal("#B42223", DestructiveTheme.Over(Fill, 0.8, "#141417"));
        Assert.Equal(Fill, DestructiveTheme.Over(Fill, 1, "#141417"));
        Assert.Equal("#EE4444", DestructiveTheme.Normalize("#e44"));
        Assert.Null(DestructiveTheme.Normalize("e44"));
    }

    [Fact]
    public void White_on_the_fill_is_the_official_4_83_and_white_on_the_text_red_is_not_enough()
    {
        Assert.Equal(4.83, DestructiveTheme.Contrast(White, Fill), 2);       // app.css:669 "4.83:1"
        Assert.Equal(3.76, DestructiveTheme.Contrast(White, "#EF4444"), 2);  // app.css:666 "only 3.76:1"
        Assert.True(DestructiveTheme.Contrast(White, Fill) >= 4.5);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Hover_and_pressed_only_darken_the_fill_so_white_contrast_only_rises(string name, string surface)
    {
        var rest = DestructiveTheme.Contrast(White, Fill);
        var hover = DestructiveTheme.Contrast(White, DestructiveTheme.Over(Fill, DestructiveTheme.HoverOpacity, surface));
        var pressed = DestructiveTheme.Contrast(White, DestructiveTheme.Over(Fill, DestructiveTheme.PressedOpacity, surface));
        Assert.True(hover > rest, $"{name}: hover {hover:0.00} must exceed rest {rest:0.00}");
        Assert.True(pressed > hover, $"{name}: pressed {pressed:0.00} must exceed hover {hover:0.00}");
        Assert.True(hover >= 4.5 && pressed >= 4.5, $"{name}: {hover:0.00}/{pressed:0.00}");
        // The old runtime fill (#EF4444 at 90%) and the XAML default (#EF6B73 at 90%) never reached 4.5:1.
        Assert.True(DestructiveTheme.Contrast(White, DestructiveTheme.Over("#EF4444", 0.9, surface)) < 4.5, name);
        Assert.True(DestructiveTheme.Contrast(White, DestructiveTheme.Over("#EF6B73", 0.9, surface)) < 4.5, name);
    }

    [Theory]
    [InlineData("page", "#141417", 4.89)]          // app.css:664 "4.89:1 on --background"
    [InlineData("card", "#1C1C20", 4.51)]          // app.css:664 "4.51:1 on --card"
    [InlineData("popover", "#18181C", 4.70)]
    [InlineData("raised", "#28282E", 3.89)]        // below 4.5:1: official value kept
    [InlineData("surface-hover", "#232328", 4.16)] // below 4.5:1: official value kept
    [InlineData("sidebar", "#0F0F12", 5.09)]
    public void The_text_red_on_each_surface_is_the_official_value(string name, string surface, double expected)
    {
        Assert.Equal(expected, DestructiveTheme.Contrast("#EF4444", surface), 2);
        Assert.True(name.Length > 0);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void The_label_holds_4_5_on_its_own_tint_where_the_plain_red_does_not(string name, string surface)
    {
        var tint = DestructiveTheme.Over("#EF4444", DestructiveTheme.TintOpacity, surface);
        var label = DestructiveTheme.Resolve(null, null, null, false).Label;
        Assert.True(DestructiveTheme.Contrast(label, tint) >= 4.5, $"{name}: label {DestructiveTheme.Contrast(label, tint):0.00}");
        Assert.True(DestructiveTheme.Contrast(label, tint) > DestructiveTheme.Contrast("#EF4444", tint), name);
    }

    [Fact]
    public void High_contrast_red_text_keeps_4_5_on_the_lightened_surfaces_it_sits_on()
    {
        var text = DestructiveTheme.Resolve(null, null, null, true).Text;
        // app.css:830-846 lightens surfaces toward the boost; red is held to 4.5:1 on the lightest, --surface-raised (4.77:1).
        var raised = DestructiveTheme.Mix(White, 0.20, "#28282E");
        Assert.Equal(4.78, DestructiveTheme.Contrast(text, raised), 2);
        foreach (var (boost, surface) in new[] { (0.0, "#141417"), (0.10, "#1C1C20"), (0.16, "#232328"), (0.20, "#28282E") })
            Assert.True(DestructiveTheme.Contrast(text, boost == 0 ? surface : DestructiveTheme.Mix(White, boost, surface)) >= 4.5, surface);
    }

    [Fact]
    public void The_button_style_and_template_use_the_fill_and_hover_and_pressed_only_darken()
    {
        var theme = Read("src", "SiloPlayer", "Themes", "DarkTheme.xaml");
        var style = theme[theme.IndexOf("x:Key=\"DestructiveButtonStyle\"", StringComparison.Ordinal)..];
        style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];
        Assert.Contains("<Setter Property=\"Background\" Value=\"{StaticResource DestructiveFillBrush}\" />", style);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{StaticResource DestructiveForegroundBrush}\" />", style);
        Assert.DoesNotContain("ErrorBrush", style);
        Assert.Contains("<Setter Property=\"UseSystemFocusVisuals\" Value=\"True\" />", style);

        var template = theme[theme.IndexOf("x:Key=\"WebUiDestructiveButtonTemplate\"", StringComparison.Ordinal)..];
        template = template[..template.IndexOf("</ControlTemplate>", StringComparison.Ordinal)];
        Assert.DoesNotContain("ErrorBrush", template);
        Assert.Matches(new Regex(@"x:Name=""PointerOver""><VisualState.Setters>\s*<Setter Target=""WebUiRoot.Background"" Value=""\{StaticResource WebUiDestructiveHoverBrush\}"""), template);
        Assert.Matches(new Regex(@"x:Name=""Pressed""><VisualState.Setters>\s*<Setter Target=""WebUiRoot.Background"" Value=""\{StaticResource WebUiDestructivePressedBrush\}"""), template);
        Assert.Contains("x:Name=\"Disabled\"><VisualState.Setters><Setter Target=\"WebUiRoot.Opacity\" Value=\"0.5\" />", template);

        Assert.Contains($"<SolidColorBrush x:Key=\"WebUiDestructiveHoverBrush\" Color=\"{{StaticResource DestructiveFillColor}}\" Opacity=\"{DestructiveTheme.HoverOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" />", theme);
        Assert.Contains($"<SolidColorBrush x:Key=\"WebUiDestructivePressedBrush\" Color=\"{{StaticResource DestructiveFillColor}}\" Opacity=\"{DestructiveTheme.PressedOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" />", theme);
        Assert.Contains("<SolidColorBrush x:Key=\"ErrorTintBrush\" Color=\"{StaticResource ErrorColor}\" Opacity=\"0.1\" />", theme);

        // Design-time defaults equal the official tokens, so a page is right before ThemeService runs too.
        Assert.Contains("<Color x:Key=\"ErrorColor\">#EF4444</Color>", theme);
        Assert.Contains("<Color x:Key=\"DestructiveFillColor\">#DC2626</Color>", theme);
        Assert.Contains("<Color x:Key=\"DestructiveForegroundColor\">#FFFFFF</Color>", theme);
        Assert.Contains($"<Color x:Key=\"ErrorLabelColor\">{DestructiveTheme.Resolve(null, null, null, false).Label}</Color>", theme);
    }

    [Fact]
    public void ThemeService_writes_every_red_role_in_place_after_the_contrast_adjustments()
    {
        var service = Read("src", "SiloPlayer", "Services", "ThemeService.cs");
        var apply = Body(service, "public void ApplyTheme(");
        var contrast = apply.IndexOf("if (highContrast)", StringComparison.Ordinal);
        var destructive = apply.IndexOf("ApplyDestructive(res, colors, highContrast);", StringComparison.Ordinal);
        var derived = apply.IndexOf("RefreshDerivedControlResources(res);", StringComparison.Ordinal);
        var system = apply.IndexOf("ApplySystemContrast(res);", StringComparison.Ordinal);
        Assert.True(contrast >= 0 && contrast < destructive && destructive < derived && derived < system, "order: high contrast, red roles, derived controls, system colors");

        var roles = Body(service, "private void ApplyDestructive(");
        foreach (var key in new[] { "ErrorBrush", "ErrorColor", "ErrorTintBrush", "DestructiveFillBrush", "DestructiveFillColor", "DestructiveForegroundBrush", "DestructiveForegroundColor", "ErrorLabelBrush", "ErrorLabelColor" })
            Assert.Contains($"\"{key}\"", roles);
        Assert.Contains("DestructiveTheme.Resolve(", roles);
        Assert.DoesNotContain("new SolidColorBrush", roles); // in place, never replaced

        var derivedBody = Body(service, "private static void RefreshDerivedControlResources(");
        Assert.Contains("CopyBrushColor(resources, \"DestructiveFillBrush\", \"WebUiDestructiveHoverBrush\", \"WebUiDestructivePressedBrush\");", derivedBody);
        Assert.DoesNotContain("\"ErrorBrush\"", derivedBody);

    }

    [Fact]
    public void A_windows_contrast_theme_repaints_every_red_role_with_a_system_color()
    {
        // Official app.css (forced-colors: active) lets the system replace author colors, so no red survives:
        // text and labels read as WindowText, the tint disappears into Window, the solid fill is Highlight
        // under HighlightText, like the accent.
        var service = Read("src", "SiloPlayer", "Services", "ThemeService.cs");
        string List(string name) => Regex.Match(service, @"private static readonly string\[\] " + name + @"\s*=\s*\[(?<l>[^\]]*)\]").Groups["l"].Value;
        Assert.Matches(@"""Error""", List("SystemWindowTextTokens"));
        Assert.Matches(@"""ErrorLabel""", List("SystemWindowTextTokens"));
        Assert.Matches(@"""ErrorTint""", List("SystemWindowTokens"));
        Assert.Matches(@"""DestructiveFill""", List("SystemHighlightTokens"));
        Assert.Matches(@"""DestructiveForeground""", List("SystemHighlightTextTokens"));

        // The palette is applied by one public method; production calls it with the real system colors and
        // no field exists only for tests. The hover and pressed fills are copied from the fill afterwards.
        var apply = Body(service, "public void ApplySystemPalette(");
        Assert.Contains("RefreshDerivedControlResources(res);", apply);
        Assert.True(apply.IndexOf("SystemHighlightTokens", StringComparison.Ordinal) < apply.IndexOf("RefreshDerivedControlResources(res);", StringComparison.Ordinal));
        Assert.Contains("ApplySystemPalette(res, palette);", Body(service, "private void ApplySystemContrast("));
        Assert.DoesNotContain("_simulatedSystemPalette", service);
        Assert.DoesNotContain("_simulatedSystemPalette", Read("tests", "SiloPlayer.NativeRegressionTests", "SharedDestructiveThemeNativeFixture.cs"));
        // Every key the palette writes is one ApplyTheme writes (ApplyDestructive for the reds), so leaving the theme restores it.
        var roles = Body(service, "private void ApplyDestructive(");
        foreach (var key in new[] { "Error", "ErrorLabel", "ErrorTint", "DestructiveFill", "DestructiveForeground" })
            Assert.Contains($"\"{key}Brush\"", roles);
    }

    public static TheoryData<string, string, string, string, string> ContrastPalettes => new()
    {
        // Window, WindowText, Highlight, HighlightText: Windows "Night sky"/black style and "Desert"/white style themes.
        { "dark", "#000000", "#FFFFFF", "#1AEBFF", "#000000" },
        { "light", "#FFFFFF", "#000000", "#37006E", "#FFFFFF" },
    };

    [Theory]
    [MemberData(nameof(ContrastPalettes))]
    public void In_a_contrast_theme_every_red_pairing_is_the_system_pairing_at_4_5_or_more(string name, string window, string windowText, string highlight, string highlightText)
    {
        // Text and label are WindowText on Window; the tint is Window over Window (no tint).
        Assert.True(DestructiveTheme.Contrast(windowText, window) >= 4.5, name);
        Assert.Equal(window, DestructiveTheme.Over(window, DestructiveTheme.TintOpacity, window));
        Assert.True(DestructiveTheme.Contrast(windowText, DestructiveTheme.Over(window, DestructiveTheme.TintOpacity, window)) >= 4.5, name);
        // The fill is Highlight under HighlightText, and the hover and pressed fills (the fill at 90% and 80%
        // over the window behind) keep 4.5:1 for the label.
        Assert.True(DestructiveTheme.Contrast(highlightText, highlight) >= 4.5, name);
        foreach (var opacity in new[] { DestructiveTheme.HoverOpacity, DestructiveTheme.PressedOpacity })
            Assert.True(DestructiveTheme.Contrast(highlightText, DestructiveTheme.Over(highlight, opacity, window)) >= 4.5,
                $"{name}: {opacity} {DestructiveTheme.Contrast(highlightText, DestructiveTheme.Over(highlight, opacity, window)):0.00}");
        // The fixed reds the system replaces would not have: the text red on a white contrast window is 3.76:1.
        Assert.True(name != "light" || DestructiveTheme.Contrast("#EF4444", window) < 4.5);
    }

    [Fact]
    public void The_remaining_literal_reds_use_the_shared_roles()
    {
        string Src(params string[] parts) => Read(["src", "SiloPlayer", .. parts]);
        // The error toast uses the label red (5.24:1 on the raised surface; ErrorBrush is 3.89:1 there) as the live brush.
        var toast = Src("Controls", "ToastContainer.xaml.cs");
        Assert.DoesNotContain("0xEF, 0x6B, 0x73", toast);
        Assert.Contains("ToastKind.Error => (\"\\uEA39\", (Brush)Application.Current.Resources[\"ErrorLabelBrush\"])", toast);
        Assert.Equal(5.24, DestructiveTheme.Contrast(DestructiveTheme.Resolve(null, null, null, false).Label, "#28282E"), 2);
        Assert.Equal(3.89, DestructiveTheme.Contrast(DestructiveTheme.DefaultText, "#28282E"), 2);
        // The Downloads failed badge: label on the tint, both live brushes (was the plain red on a 15% copy, 4.21/3.85/3.55).
        var downloads = Src("Views", "DownloadsPage.xaml.cs");
        Assert.Contains("\"failed\" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[\"ErrorLabelBrush\"]", downloads);
        Assert.Contains("Application.Current.Resources[\"ErrorTintBrush\"]", downloads);
        Assert.DoesNotContain("\"failed\" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[\"ErrorBrush\"]", downloads);
        // The rest of the literal reds.
        Assert.DoesNotContain("IndianRed", Src("Controls", "EpisodeCardCaption.cs"));
        Assert.Contains("Foreground = (Brush)Application.Current.Resources[\"ErrorBrush\"]", Src("Controls", "EpisodeCardCaption.cs"));
        Assert.Contains("Background=\"{StaticResource ErrorTintBrush}\"", Src("Controls", "SubtitleSearchDialog.xaml"));
        Assert.DoesNotContain("#1AEF4444", Src("Controls", "SubtitleSearchDialog.xaml"));
        // Its text is the label red on that tint, so it follows high contrast and a Windows contrast theme.
        Assert.DoesNotContain("#FFFCA5A5", Src("Controls", "SubtitleSearchDialog.xaml"));
        Assert.Contains("Foreground=\"{StaticResource ErrorLabelBrush}\"", Src("Controls", "SubtitleSearchDialog.xaml"));
        Assert.DoesNotContain("IndianRed", Src("Views", "Admin", "AdminRequestsPage.cs"));
        Assert.Contains("Application.Current.Resources[\"ErrorBrush\"]", Src("Views", "Admin", "AdminRequestsPage.cs"));
        var overlay = Src("Controls", "AdminAppearanceOverlayEditor.Images.cs");
        Assert.DoesNotContain("0xEF, 0x44, 0x44", overlay);
        Assert.Contains("_libraryError.Foreground = (Brush)Application.Current.Resources[\"ErrorBrush\"];", overlay);
        var prepared = Src("Views", "Admin", "AdminPreparedFilesPage.cs");
        Assert.DoesNotContain("Tint(\"ErrorBrush\"", prepared);
        Assert.Contains("\"danger\" => (Brush(\"ErrorLabelBrush\"), Brush(\"ErrorTintBrush\"))", prepared);
        // The profile editor validation text uses ErrorBrush, which a contrast theme now repaints (it left SystemFillColorCriticalBrush, which also followed the system).
        Assert.Contains("_validationText.Foreground = Brush(\"ErrorBrush\", \"SecondaryTextBrush\");", Src("Views", "Dialogs", "ProfileEditorDialog.cs"));
        var settings = Src("Views", "SettingsPage.xaml.cs");
        Assert.DoesNotContain("\"#F87171\", \"#3A1313\"", settings);
        Assert.Contains("(string?)null, (string?)null", settings);
        Assert.Contains("Application.Current.Resources[\"ErrorLabelBrush\"]", settings);
    }

    [Fact]
    public void Every_xaml_reference_to_a_red_key_resolves_and_is_defined_before_it_is_used()
    {
        var theme = Read("src", "SiloPlayer", "Themes", "DarkTheme.xaml");
        var keys = new[] { "ErrorBrush", "ErrorColor", "DestructiveFillBrush", "DestructiveFillColor", "DestructiveForegroundBrush", "DestructiveForegroundColor", "ErrorLabelBrush", "ErrorLabelColor",
            "ErrorTintBrush", "WebUiDestructiveHoverBrush", "WebUiDestructivePressedBrush" };
        foreach (var key in keys)
        {
            var definition = theme.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
            Assert.True(definition >= 0, key + " is defined in the shared theme");
            var first = theme.IndexOf($"{{StaticResource {key}}}", StringComparison.Ordinal);
            Assert.True(first < 0 || definition < first, key + " is defined before the theme uses it (a StaticResource cannot look ahead)");
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(TestRepository.Root, "src"), "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match use in Regex.Matches(text, @"\{StaticResource (?<key>(?:Error|Destructive|WebUiDestructive)\w*)\}"))
                Assert.True(theme.Contains($"x:Key=\"{use.Groups["key"].Value}\"", StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} references {use.Groups["key"].Value}, which the shared theme does not define");
        }
    }

    [Fact]
    public void The_shared_theme_no_longer_carries_the_rose_red()
    {
        Assert.DoesNotContain("EF6B73", Read("src", "SiloPlayer", "Themes", "DarkTheme.xaml"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EF6B73", Read("src", "SiloPlayer", "Services", "ThemeService.cs"), StringComparison.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views"), "*.xaml", SearchOption.AllDirectories))
            Assert.DoesNotContain("#2DEF6B73", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
    }

    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidOperationException("Unbalanced braces after " + signature);
    }
}
