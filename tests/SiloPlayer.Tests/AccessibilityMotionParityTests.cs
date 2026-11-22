using System.Globalization;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

/// <summary>
/// Official Silo Server #4294 (pin button names), #4311 (reduced motion), #4308 (field
/// edge contrast and system contrast) and #4297 (Windows contrast themes).
/// </summary>
public sealed class AccessibilityMotionParityTests
{
    private static AppLocale Locale(string language) => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));

    // ── #4294: the section pin is named by its localized action and described by the row ──

    [Fact]
    public void Section_pin_uses_the_upstream_keys_and_the_rows_shown_title()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml.cs").ReplaceLineEndings("\n");

        Assert.DoesNotContain("\"Pin to sidebar\"", source);
        Assert.DoesNotContain("\"Unpin from sidebar\"", source);

        var label = MethodBody(source, "private void UpdatePinLabel(");
        Assert.Contains("\"web.home.sectionRow.unpin\"", label);
        Assert.Contains("\"web.home.sectionRow.pin\"", label);
        Assert.Contains("AppStrings.Get(", label);
        Assert.Contains("AutomationProperties.SetName(PinSectionBtn, label)", label);
        Assert.Contains("ToolTipService.SetToolTip(PinSectionBtn, label)", label);
        // The row context is the displayed (translated) heading, not the stored pin label.
        Assert.Contains("AutomationProperties.SetHelpText(PinSectionBtn, SectionTitle.Text)", label);

        // Pin-state changes and title or language refreshes both rename the button.
        Assert.Contains("UpdatePinLabel();", MethodBody(source, "private void UpdatePinVisual("));
        Assert.Contains("UpdatePinLabel();", MethodBody(source, "private void UpdateTitle("));
    }

    [Fact]
    public void Section_pin_labels_are_translated_in_every_shipped_language()
    {
        var english = Locale("en");
        Assert.Equal("Pin to sidebar", english.Get("web.home.sectionRow.pin"));
        Assert.Equal("Unpin from sidebar", english.Get("web.home.sectionRow.unpin"));

        foreach (var language in MessageCatalog.ShippedLanguages.Select(l => l.Tag).Where(tag => tag != "en"))
        {
            var locale = Locale(language);
            foreach (var key in new[] { "web.home.sectionRow.pin", "web.home.sectionRow.unpin" })
            {
                var value = locale.Get(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language} {key}");
                Assert.NotEqual(english.Get(key), value);
            }
        }
    }

    // ── #4311: scripted scrolling honours Windows "Animation effects" ──

    [Fact]
    public void System_motion_is_cached_and_follows_the_windows_setting()
    {
        var helper = ReadRepoFile("src", "SiloPlayer", "Helpers", "SystemMotion.cs").ReplaceLineEndings("\n");

        Assert.Contains("private static volatile bool s_animationsEnabled", helper);
        Assert.Contains("AnimationsEnabledChanged +=", helper);
        Assert.Contains("OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)", helper);
        Assert.Contains("internal static bool AnimationsEnabled => s_followsChanges ? s_animationsEnabled : ReadAnimationsEnabled();", helper);
        // One UISettings for the process, not one per query.
        Assert.Single(Regex.Matches(helper, @"new Windows\.UI\.ViewManagement\.UISettings\(\)"));

        // The artwork reveal shares the cached value instead of querying WinRT itself.
        var reveal = ReadRepoFile("src", "SiloPlayer", "Helpers", "ArtworkReveal.cs");
        Assert.Contains("internal static bool AnimationsEnabled => SystemMotion.AnimationsEnabled;", reveal);
        Assert.DoesNotContain("new Windows.UI.ViewManagement.UISettings()", reveal);
    }

    [Theory]
    [InlineData("Controls", "SectionRow.xaml.cs", 1)]
    [InlineData("Controls", "CarouselRail.cs", 3)]
    [InlineData("Views", "ItemDetailPage.xaml.cs", 3)]
    [InlineData("Views", "CalendarPage.xaml.cs", 1)]
    [InlineData("Views", "RequestsPage.xaml.cs", 2)]
    public void Every_scripted_scroll_in_ported_views_respects_the_animation_preference(string folder, string file, int honoured)
    {
        var source = ReadRepoFile("src", "SiloPlayer", folder, file).ReplaceLineEndings("\n");
        var calls = ChangeViewCalls(source);
        Assert.NotEmpty(calls);

        // The three-argument overload always animates; every call states its motion.
        Assert.All(calls, args => Assert.True(args.Count == 4, "ChangeView(" + string.Join(",", args) + ")"));
        var motionAware = calls.Count(args =>
            args[3].Contains("!SystemMotion.AnimationsEnabled", StringComparison.Ordinal)
            || args[3].Trim() == "ReduceMotion");
        Assert.Equal(honoured, motionAware);
        // Anything else is an explicit instant jump, never a literal "animate".
        Assert.All(calls.Where(args => !args[3].Contains("SystemMotion", StringComparison.Ordinal) && args[3].Trim() != "ReduceMotion"),
            args => Assert.EndsWith("true", args[3].Trim()));
    }

    [Fact]
    public void Section_row_navigation_jumps_when_animation_effects_are_off()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml.cs").ReplaceLineEndings("\n");
        Assert.Contains(
            "CardsScrollViewer.ChangeView(target, null, null, disableAnimation: !SystemMotion.AnimationsEnabled);",
            MethodBody(source, "private void NavigateTo("));

        var rail = ReadRepoFile("src", "SiloPlayer", "Controls", "CarouselRail.cs");
        Assert.Contains("private static bool ReduceMotion => !SiloPlayer.Helpers.SystemMotion.AnimationsEnabled;", rail);
        Assert.Contains("scroll.ChangeView(0, null, null, ReduceMotion)", rail);
        Assert.Contains("scroll.ChangeView(scroll.ScrollableWidth, null, null, ReduceMotion)", rail);
        // Pointer dragging already tracks the finger without animation.
        Assert.Contains("scroll.ScrollableWidth), null, null, true)", rail);
    }

    // ── #4308: form fields get a 3:1 edge ──

    [Fact]
    public void Form_fields_and_the_unchecked_switch_use_the_input_border_token()
    {
        var theme = ReadRepoFile("src", "SiloPlayer", "Themes", "DarkTheme.xaml");
        Assert.Contains("<Color x:Key=\"InputBorderColor\">#6A6A74</Color>", theme);
        Assert.Contains("<SolidColorBrush x:Key=\"InputBorderBrush\" Color=\"{StaticResource InputBorderColor}\" />", theme);
        foreach (var key in new[] { "TextControlBorderBrush", "ComboBoxBorderBrush", "ToggleSwitchFillOff", "ToggleSwitchFillOffPointerOver" })
            Assert.Contains($"<SolidColorBrush x:Key=\"{key}\" Color=\"{{StaticResource InputBorderColor}}\" />", theme);
        foreach (var style in new[] { "DarkTextBoxStyle", "DarkPasswordBoxStyle" })
        {
            var start = theme.IndexOf($"x:Key=\"{style}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, style);
            var body = theme[start..theme.IndexOf("</Style>", start, StringComparison.Ordinal)];
            Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"{StaticResource InputBorderBrush}\" />", body);
        }

        var service = ReadRepoFile("src", "SiloPlayer", "Services", "ThemeService.cs").ReplaceLineEndings("\n");
        Assert.Contains("InputBorder = \"#6A6A74\",", service);
        Assert.Contains("UpdateBrush(res, \"InputBorderBrush\", colors.InputBorder);", service);
        Assert.Contains("UpdateColor(res, \"InputBorderColor\", colors.InputBorder);", service);
        // In-app high contrast: color-mix(#FFF 16%, #6A6A74).
        Assert.Contains("UpdateBrush(res, \"InputBorderBrush\", \"#82828A\");", service);
        Assert.Contains("UpdateColor(res, \"InputBorderColor\", \"#82828A\");", service);

        var derived = MethodBody(service, "private static void RefreshDerivedControlResources(");
        Assert.Matches(new Regex("CopyBrushColor\\(resources, \"InputBorderBrush\",\\s*\"TextControlBorderBrush\", \"ComboBoxBorderBrush\",\\s*\"ToggleSwitchFillOff\", \"ToggleSwitchFillOffPointerOver\"\\);"), derived);
        // Dividers, buttons and popups keep the hairline border.
        var borderCopy = Regex.Match(derived, "CopyBrushColor\\(resources, \"BorderBrush\",[^;]*;").Value;
        Assert.Contains("\"ButtonBorderBrush\"", borderCopy);
        Assert.DoesNotContain("\"TextControlBorderBrush\"", borderCopy);
        Assert.DoesNotContain("\"ComboBoxBorderBrush\"", borderCopy);
        Assert.DoesNotContain("ToggleSwitchFillOff", derived.Replace(Regex.Match(derived, "CopyBrushColor\\(resources, \"InputBorderBrush\",[^;]*;").Value, ""));
    }

    [Theory]
    [InlineData("Background")] // page
    [InlineData("Card")]
    [InlineData("Popover")]
    [InlineData("Input")] // the field's own fill
    public void Input_border_holds_three_to_one_against_each_surface(string surface)
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "ThemeService.cs");
        var edge = ThemeHex(service, "InputBorder");
        var behind = ThemeHex(service, surface);
        Assert.True(Contrast(edge, behind) >= 3, $"{edge} on {behind}: {Contrast(edge, behind):0.00}");
        Assert.True(Contrast("#82828A", behind) >= 3, $"high contrast #82828A on {behind}");
        // The old hairline edge did not.
        Assert.True(Contrast(ThemeHex(service, "Border"), behind) < 3);
    }

    // ── #4297/#4308: Windows contrast themes ──

    [Fact]
    public void Windows_contrast_themes_repaint_tokens_and_control_state_with_system_colors()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "ThemeService.cs").ReplaceLineEndings("\n");
        var applyTheme = MethodBody(service, "public void ApplyTheme(");
        Assert.True(
            applyTheme.IndexOf("ApplySystemContrast(res);", StringComparison.Ordinal)
                > applyTheme.IndexOf("RefreshDerivedControlResources(res);", StringComparison.Ordinal),
            "The system palette must apply after the app and in-app high-contrast palette.");

        Assert.Contains("new Windows.UI.ViewManagement.AccessibilitySettings()", service);
        Assert.Contains("_systemColors.ColorValuesChanged +=", service);
        Assert.Contains("dispatcher.TryEnqueue(", service);
        Assert.Contains("if (_systemContrastApplied || IsSystemHighContrast) ApplyTheme(CurrentTheme);", service);

        // The real system colors are read by ApplySystemContrast and applied by ApplySystemPalette (the one
        // method the native host also calls with a dark and a light palette).
        var read = MethodBody(service, "private void ApplySystemContrast(");
        foreach (var role in new[] { "Window", "WindowText", "Highlight", "HighlightText", "GrayText" })
            Assert.Contains($"UIElementColor(Windows.UI.ViewManagement.UIElementType.{role})", read);
        Assert.Contains("ApplySystemPalette(res, palette);", read);
        var apply = MethodBody(service, "public void ApplySystemPalette(");
        Assert.Contains("SetBrushColor(res, \"NavigationViewItemBackgroundSelected\", highlight);", apply);
        Assert.Contains("SetBrushColor(res, \"NavigationViewItemForegroundSelected\", highlightText);", apply);
        Assert.Contains("SetBrushColor(res, \"ComboBoxItemBackgroundSelected\", highlight);", apply);
        Assert.Contains("SetBrushColor(res, \"ToggleSwitchFillOff\", window);", apply);
        Assert.True(apply.IndexOf("RefreshDerivedControlResources(res);", StringComparison.Ordinal)
            < apply.IndexOf("NavigationViewItemBackgroundSelected", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_system_contrast_token_exists_and_is_restored_by_the_app_theme()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "ThemeService.cs").ReplaceLineEndings("\n");
        var theme = ReadRepoFile("src", "SiloPlayer", "Themes", "DarkTheme.xaml");
        // The red roles are written back by ApplyDestructive, which ApplyTheme runs before the system palette.
        var applyTheme = MethodBody(service, "public void ApplyTheme(") + MethodBody(service, "private void ApplyDestructive(");

        var tokens = Regex.Matches(service, @"private static readonly string\[\] System\w+Tokens\s*=\s*\[(?<list>[^\]]*)\]")
            .SelectMany(m => Regex.Matches(m.Groups["list"].Value, "\"(?<t>\\w+)\"").Select(t => t.Groups["t"].Value))
            .ToArray();
        Assert.Equal(35, tokens.Length); // 30 palette tokens plus the five red roles (Error, ErrorLabel, ErrorTint, DestructiveFill, DestructiveForeground)
        Assert.Contains("InputBorder", tokens);

        foreach (var token in tokens)
        {
            Assert.Contains($"x:Key=\"{token}Brush\"", theme);
            // ErrorTint is a brush only (the error color at 10% opacity); every other token has a color too.
            if (token == "ErrorTint") { Assert.DoesNotContain("x:Key=\"ErrorTintColor\"", theme); }
            else Assert.Contains($"x:Key=\"{token}Color\"", theme);
            // Leaving the contrast theme reruns ApplyTheme, which must write each back.
            Assert.Matches(new Regex($"UpdateBrush\\(res, \"{token}Brush\", "), applyTheme);
            if (token != "ErrorTint") Assert.Matches(new Regex($"UpdateColor\\(res, \"{token}Color\", "), applyTheme);
        }
    }

    [Fact]
    public void Accessibility_service_does_not_overwrite_the_theme_after_applying_it()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Services", "AccessibilityService.cs").ReplaceLineEndings("\n");
        var apply = MethodBody(source, "public void Apply(");
        Assert.Contains("themeService.ApplyTheme(themeService.CurrentTheme);", apply);
        Assert.DoesNotContain("SetBrush(", source);
        Assert.DoesNotContain("#8091A7", source);
    }

    /// <summary>The top-level arguments of every <c>.ChangeView(...)</c> call in the source.</summary>
    private static List<List<string>> ChangeViewCalls(string source)
    {
        var calls = new List<List<string>>();
        for (var at = source.IndexOf(".ChangeView(", StringComparison.Ordinal); at >= 0;
             at = source.IndexOf(".ChangeView(", at + 1, StringComparison.Ordinal))
        {
            var args = new List<string>();
            var depth = 0;
            var start = at + ".ChangeView(".Length;
            for (var i = start; i < source.Length; i++)
            {
                var c = source[i];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    if (depth-- == 0) { args.Add(source[start..i]); break; }
                }
                else if (c == ',' && depth == 0) { args.Add(source[start..i]); start = i + 1; }
            }
            calls.Add(args);
        }
        return calls;
    }

    private static string ThemeHex(string service, string property)
    {
        var match = Regex.Match(service, $@"\b{property} = ""(?<hex>#[0-9A-Fa-f]{{6}})"",");
        Assert.True(match.Success, property);
        return match.Groups["hex"].Value;
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        static double Channel(string hex, int offset)
        {
            var c = int.Parse(hex.AsSpan(1 + offset, 2), NumberStyles.HexNumber) / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(hex, 0) + 0.7152 * Channel(hex, 2) + 0.0722 * Channel(hex, 4);
    }

    private static string MethodBody(string source, string signature)
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

    private static string ReadRepoFile(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = FindRepositoryRoot();
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
