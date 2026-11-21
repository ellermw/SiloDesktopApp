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
    public void A_server_theme_sets_what_it_defines_and_inherits_the_rest()
    {
        var both = DestructiveTheme.Resolve("#EF4444", "#DC2626", "#FFFFFF", false,
            new Dictionary<string, string> { ["destructive"] = "#f87171", ["destructive-fill"] = "#b91c1c" });
        Assert.Equal("#F87171", both.Text);
        Assert.Equal("#B91C1C", both.Fill);
        Assert.Equal("#FFFFFF", both.Foreground);
        Assert.Equal("#FA9595", both.Label); // 25% white over #F87171

        var fillOnly = DestructiveTheme.Resolve("#EF4444", "#DC2626", "#FFFFFF", false,
            new Dictionary<string, string> { ["destructive-fill"] = "#991B1B" });
        Assert.Equal("#EF4444", fillOnly.Text);
        Assert.Equal("#991B1B", fillOnly.Fill);
        Assert.Equal("#F37373", fillOnly.Label);

        var foreground = DestructiveTheme.Resolve("#EF4444", "#DC2626", "#FFFFFF", false,
            new Dictionary<string, string> { ["destructive-foreground"] = "#FAFAFA" });
        Assert.Equal("#FAFAFA", foreground.Foreground);

        // The branding accent is the only appearance the server sends today; it must not move a red.
        var accent = DestructiveTheme.Resolve("#EF4444", "#DC2626", "#FFFFFF", false,
            new Dictionary<string, string> { ["primary"] = "#4F46E5", ["ring"] = "#4F46E5", ["primary-foreground"] = "#FFFFFF" });
        Assert.Equal(DestructiveTheme.Resolve(null, null, null, false), accent);

        // A value that is not a hex color is ignored instead of blanking the role.
        var invalid = DestructiveTheme.Resolve("#EF4444", "#DC2626", "#FFFFFF", false,
            new Dictionary<string, string> { ["destructive"] = "var(--primary)", ["destructive-fill"] = "" });
        Assert.Equal(DestructiveTheme.Resolve(null, null, null, false), invalid);
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

        var server = DestructiveTheme.Resolve(null, null, null, true, new Dictionary<string, string> { ["destructive"] = "#B91C1C" });
        Assert.Equal(DestructiveTheme.Mix(White, 0.65, "#B91C1C"), server.Text);
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

        // A Windows contrast theme still paints no red role (system colors win on surfaces and text as before).
        var system2 = Body(service, "private void ApplySystemContrast(");
        Assert.DoesNotContain("Error", system2);
        Assert.DoesNotContain("Destructive", system2);
        foreach (Match list in Regex.Matches(service, @"private static readonly string\[\] System\w+Tokens\s*=\s*\[(?<l>[^\]]*)\]"))
            Assert.DoesNotMatch(@"Error|Destructive", list.Groups["l"].Value);
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
                Assert.True(keys.Contains(use.Groups["key"].Value) || use.Groups["key"].Value == "DestructiveButtonStyle" || use.Groups["key"].Value == "DestructiveButton",
                    $"{Path.GetFileName(file)} references {use.Groups["key"].Value}");
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
