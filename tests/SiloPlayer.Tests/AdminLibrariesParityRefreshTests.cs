using System.Text.RegularExpressions;
using SiloPlayer.Core.Services.Admin;
using Xunit;

namespace SiloPlayer.Tests;

/// <summary>
/// Guards for the Libraries visual refresh against official Silo Server e130f262d (the WebUI theme radius, the preference-aware date
/// formatter, the fixed destructive fill added in #4700). The destructive colours are computed numerically from the palette the page uses;
/// the radii and dates are checked against named components in the page sources. Rendered evidence (resolved hover colours, panel sizes,
/// icon positions) lives in the <c>admin-libraries</c> native fixture.
/// </summary>
public sealed class AdminLibrariesParityRefreshTests
{
    private static readonly Regex LiteralRadius = new(@"CornerRadius\s*=\s*new\((6|8|10|12|16|20|24)\)", RegexOptions.Compiled);
    private static readonly Regex NamedRadius = new(@"\b(Radius(?:Sm|Md|Lg|Xl|2Xl))\b", RegexOptions.Compiled);

    private static string ViewsDirectory() => Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Views", "Admin");

    private static string[] PageSources() => Directory.EnumerateFiles(ViewsDirectory(), "*.cs")
        .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("AdminLibrariesPage", StringComparison.Ordinal) || name is "AdminLibrariesUi.cs" or "AdminLibraryEditorView.cs"))
        .ToArray();

    // ── Radii ────────────────────────────────────────────────────────────

    /// <summary>The page's named radii as pixels, read from the constants it declares (theme <c>--radius</c> .75rem: rounded-sm/md/lg/xl/2xl = 8/10/12/16/20).</summary>
    private static Dictionary<string, double> RadiusConstants()
    {
        var ui = File.ReadAllText(Path.Combine(ViewsDirectory(), "AdminLibrariesUi.cs"));
        var declaration = Regex.Match(ui, @"internal const double (?<list>RadiusSm\s*=[^;]+);");
        Assert.True(declaration.Success, "the radius constants are declared in AdminLibrariesUi");
        return Regex.Matches(declaration.Groups["list"].Value, @"(Radius\w+)\s*=\s*(\d+)")
            .ToDictionary(match => match.Groups[1].Value, match => double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The radius in pixels the first named radius on <paramref name="line"/> stands for.</summary>
    private static double RadiusOnLine(string line, IReadOnlyDictionary<string, double> constants)
    {
        var match = NamedRadius.Match(line);
        Assert.True(match.Success, "a named radius is used on: " + line.Trim());
        return constants[match.Groups[1].Value];
    }

    /// <summary>One named component of the page: where its radius is set (the single source line holding <c>anchor</c>) and the official pixel radius.</summary>
    private sealed record Component(string Name, string File, string Anchor, double Expected);

    private static readonly Component[] Components =
    [
        new("badge (rounded-md)", "AdminLibrariesUi.cs", "Child = child, CornerRadius = new(RadiusMd), Padding = new(8, 2, 8, 2)", 10),
        new("table panel (rounded-2xl)", "AdminLibrariesPage.List.cs", "return Surface(scroller,", 20),
        new("card panel (rounded-2xl)", "AdminLibrariesPage.List.cs", "var card = Surface(stack,", 20),
        new("diagnostics section panel (rounded-2xl)", "AdminLibrariesPage.Diagnostics.cs", "Section.CornerRadius =", 20),
        new("editor dialog (rounded-lg)", "AdminLibraryEditorView.cs", "private readonly Border _panel = new() {", 12),
        new("editor dialog restored from phone (rounded-lg)", "AdminLibraryEditorView.cs", "_panel.CornerRadius = new(RadiusLg); _panel.BorderThickness = new(1);", 12),
        new("scan queue popover (popover.tsx rounded-xl)", "AdminLibrariesPage.ScanQueue.cs", "flyout.FlyoutPresenterStyle = RoundedPresenterStyle(", 16),
        new("scan queue row (rounded-lg)", "AdminLibrariesPage.ScanQueue.cs", "Padding = new(10, 8, 10, 8), CornerRadius =", 12),
        new("card menu (rounded-md)", "AdminLibrariesPage.List.cs", "flyout.FlyoutPresenterStyle = RoundedPresenterStyle(", 10),
        new("card menu item (rounded-sm)", "AdminLibrariesPage.List.cs", "Padding = new(8, 6, 8, 6), CornerRadius =", 8),
    ];

    /// <summary>
    /// Each named component of the page uses the official radius: the named constant on its own line must resolve to the expected pixels, so a component
    /// that reaches for a neighbouring radius (the scan queue popover on the card menu's 10 instead of rounded-xl's 16) fails here.
    /// </summary>
    [Fact]
    public void EveryNamedComponentUsesItsOfficialRadius()
    {
        var constants = RadiusConstants();
        Assert.Equal(new Dictionary<string, double> { ["RadiusSm"] = 8, ["RadiusMd"] = 10, ["RadiusLg"] = 12, ["RadiusXl"] = 16, ["Radius2Xl"] = 20 }, constants);
        var wrong = new List<string>();
        foreach (var component in Components)
        {
            var lines = File.ReadAllLines(Path.Combine(ViewsDirectory(), component.File)).Where(line => line.Contains(component.Anchor, StringComparison.Ordinal)).ToArray();
            Assert.True(lines.Length >= 1, $"{component.Name}: the anchor \"{component.Anchor}\" was not found in {component.File}");
            foreach (var line in lines)
                if (RadiusOnLine(line, constants) != component.Expected) wrong.Add($"{component.Name}: {RadiusOnLine(line, constants)} px, official {component.Expected} px");
        }
        Assert.Empty(wrong);
    }

    /// <summary>The per-component check can tell the radii apart: a popover on the card menu's radius is reported.</summary>
    [Fact]
    public void TheRadiusCheckDetectsAWrongNamedRadius()
    {
        var constants = RadiusConstants();
        Assert.Equal(16, RadiusOnLine("flyout.FlyoutPresenterStyle = RoundedPresenterStyle(RadiusXl);", constants));
        Assert.Equal(10, RadiusOnLine("flyout.FlyoutPresenterStyle = RoundedPresenterStyle(RadiusMd);", constants));
        Assert.NotEqual(16, RadiusOnLine("flyout.FlyoutPresenterStyle = RoundedPresenterStyle(RadiusMd);", constants));
        Assert.NotEqual(20, RadiusOnLine("return Surface(scroller, RadiusXl, 0);", constants));
    }

    /// <summary>No view builds a radius from a bare theme literal: the named constants carry the meaning (rounded-md, -lg, -xl, -2xl).</summary>
    [Fact]
    public void PageRadiiComeFromTheNamedThemeRadiiNotLiterals()
    {
        var files = PageSources();
        Assert.True(files.Length >= 9, "the Libraries view sources were found");
        var offenders = files.SelectMany(path => File.ReadAllLines(path).Select((line, index) => (Path.GetFileName(path), index + 1, line)))
            .Where(entry => LiteralRadius.IsMatch(entry.line)).Select(entry => entry.Item1 + ":" + entry.Item2).ToArray();
        Assert.Empty(offenders);
    }

    // ── Dates ────────────────────────────────────────────────────────────

    private static readonly string[] DateProperties = ["LastScannedAt", "ScanWarningAt", "FirstSeenAt", "LastSeenAt", "StartedAt", "CheckedAt", "CreatedAt", "UpdatedAt", "FinishedAt"];

    /// <summary>
    /// The places in <paramref name="source"/> that show a date without the page's shared formatter (<c>DateTimeText</c>, which goes through <c>DateTimeDisplay</c> and so
    /// the user's date and time format settings): a date API called directly, a date property formatted in place, or the older Core formatter.
    /// </summary>
    internal static List<string> DateBypasses(string fileName, string source)
    {
        var found = new List<string>();
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            string? why = null;
            if (Regex.IsMatch(line, @"\b(ToShortDateString|ToLongDateString|ToShortTimeString|ToLongTimeString|ToLocalTime)\b")) why = "a direct date API";
            else if (Regex.IsMatch(line, @"\bP\.DateTimeText\(|AdminLibraryPresentation\.DateTimeText\(")) why = "the Core formatter that ignores the preferences";
            else if (Regex.IsMatch(line, @"\.ToString\(""(d|D|t|T|g|G|f|F|s|u|U|R|r|y|Y|m|M)""") || Regex.IsMatch(line, @"\.ToString\(""[^""]*(yyyy|yy|MM|dd|HH|hh|mm|ss)[^""]*""")) why = "a date format string";
            else if (Regex.IsMatch(line, @"\b(" + string.Join('|', DateProperties) + @")\b") && !Regex.IsMatch(line, @"DateTimeText\(|\bis \{ \}|\.UtcDateTime\.ToString\(""O""\)|P\.ScanStarted\(|SortBy|OrderBy|\.Where\(|==|\s[<>]=?\s"))
                why = "a date property that is not passed to DateTimeText";
            if (why != null) found.Add($"{fileName}:{index + 1} ({why})");
            // A date bound by `is { } name` must reach DateTimeText(name) within the next few lines.
            foreach (Match bound in Regex.Matches(line, @"\b(?:" + string.Join('|', DateProperties) + @") is \{ \} (?<name>\w+)"))
            {
                var name = bound.Groups["name"].Value;
                var window = string.Join('\n', lines.Skip(index).Take(5));
                if (!Regex.IsMatch(window, @"DateTimeText\(" + name + @"\)")) found.Add($"{fileName}:{index + 1} (the date bound as \"{name}\" is never passed to DateTimeText)");
            }
        }
        return found;
    }

    /// <summary>The official formatDateTime follows the user's date and time format settings: every date the page shows goes through the shared formatter.</summary>
    [Fact]
    public void EveryPageDateGoesThroughThePreferenceFormatter()
    {
        var files = PageSources();
        var bypasses = files.Where(path => Path.GetFileName(path) != "AdminLibrariesUi.cs")
            .SelectMany(path => DateBypasses(Path.GetFileName(path), File.ReadAllText(path))).ToList();
        Assert.Empty(bypasses);
        // The one formatter the page uses is the app's preference formatter, date then time with seconds.
        var ui = File.ReadAllText(Path.Combine(ViewsDirectory(), "AdminLibrariesUi.cs"));
        Assert.Contains("DateTimeDisplay.FormatDate(value)", ui);
        Assert.Contains("DateTimeDisplay.FormatTime(value, seconds: true)", ui);
        Assert.DoesNotContain("AdminLibraryPresentation.DateTimeText", ui);
        // And the formatter is actually reached: the list, the roots and the stale-ID tables all call it.
        var calls = files.Sum(path => Regex.Matches(File.ReadAllText(path), @"\bDateTimeText\(").Count);
        Assert.True(calls >= 8, "the date columns call DateTimeText (" + calls + ")");
    }

    /// <summary>The bypass check is not vacuous: each kind of bypass in a page source is reported.</summary>
    [Theory]
    [InlineData("Text(library.LastScannedAt.ToString(\"d\"), 12)")]
    [InlineData("Text(root.FirstSeenAt.ToLocalTime().ToString(), 12)")]
    [InlineData("Text(P.DateTimeText(root.LastSeenAt), 12)")]
    [InlineData("Text(row.FirstSeenAt.ToString(\"yyyy-MM-dd HH:mm\"), 12)")]
    [InlineData("var when = Text(library.LastScannedAt?.ToString(), 12);")]
    public void TheDateCheckReportsAnyBypass(string line) => Assert.NotEmpty(DateBypasses("Synthetic.cs", line));

    [Theory]
    [InlineData("Text(DateTimeText(root.FirstSeenAt), 12, true)")]
    [InlineData("\"first_seen\" => root => root.FirstSeenAt.UtcDateTime.ToString(\"O\"),")]
    public void TheDateCheckAcceptsTheSharedFormatterAndSortKeys(string line) => Assert.Empty(DateBypasses("Synthetic.cs", line));

    [Fact]
    public void ABoundDateThatIsNeverFormattedIsReported()
    {
        Assert.NotEmpty(DateBypasses("Synthetic.cs", "if (library.ScanWarningAt is { } warned)\n{\n    var warning = Text(warned.ToString(), 11);\n}"));
        Assert.Empty(DateBypasses("Synthetic.cs", "if (library.ScanWarningAt is { } warned)\n{\n    var warning = Text(T(\"k\", (\"time\", DateTimeText(warned))), 11);\n}"));
    }

    // ── Destructive colours ──────────────────────────────────────────────

    /// <summary>The official <c>--destructive-fill</c> is the fixed #dc2626 (not a theme-derived, darkened colour), under white: 4.83:1.</summary>
    [Fact]
    public void TheDestructiveFillIsTheOfficialColourAndHoldsContrastUnderWhite()
    {
        Assert.Equal("#DC2626", AdminLibrariesPalette.DestructiveFill.Hex);
        Assert.Equal("#FFFFFF", AdminLibrariesPalette.DestructiveForeground.Hex);
        var rest = AdminLibrariesPalette.ContrastRatio(AdminLibrariesPalette.DestructiveFill, AdminLibrariesPalette.DestructiveForeground);
        Assert.InRange(rest, 4.82, 4.84); // app.css: "4.83:1"
        Assert.True(rest >= 4.5, "white on the destructive fill is " + rest);
    }

    /// <summary>
    /// <c>hover:bg-destructive-fill/90</c>: hover and pressed stay on the fill, never lighter than it, and white text keeps 4.5:1 over every backdrop
    /// the page puts a destructive button on (black through the lightest panel surface, #28282e). The shared theme's hover (the lighter theme red at 90%) is the
    /// failing case this replaces.
    /// </summary>
    [Fact]
    public void DestructiveHoverAndPressedKeepContrastUnderWhiteOverThePageSurfaces()
    {
        Assert.Equal(0.9, AdminLibrariesPalette.DestructiveHoverOpacity);
        Assert.Equal(230, AdminLibrariesPalette.AlphaByte(AdminLibrariesPalette.DestructiveHoverOpacity));
        foreach (var backdrop in new[] { new AdminLibrariesPalette.Rgb(0, 0, 0), AdminLibrariesPalette.RaisedSurface })
        {
            var hover = AdminLibrariesPalette.Over(AdminLibrariesPalette.DestructiveFill, AdminLibrariesPalette.DestructiveHoverOpacity, backdrop);
            var ratio = AdminLibrariesPalette.ContrastRatio(hover, AdminLibrariesPalette.DestructiveForeground);
            Assert.True(ratio >= 4.5, $"hover over {backdrop.Hex} is {hover.Hex}, {ratio:0.00}:1 under white");
            Assert.True(AdminLibrariesPalette.Luminance(hover) <= AdminLibrariesPalette.Luminance(AdminLibrariesPalette.DestructiveFill) + 1e-9, "hover is not lighter than the fill");
        }
        // The theme's destructive button hover (#EF6B73 at 90% over the same surface) would not hold it: this is what the page no longer uses.
        var theme = AdminLibrariesPalette.Over(new(0xEF, 0x6B, 0x73), 0.9, AdminLibrariesPalette.RaisedSurface);
        Assert.True(AdminLibrariesPalette.ContrastRatio(theme, AdminLibrariesPalette.DestructiveForeground) < 4.5);
    }

    /// <summary>
    /// The fill is a plain computed value: nothing in <c>AdminLibrariesUi</c> reads <c>Application.Current.Resources</c> (or derives a colour from the theme) in a static
    /// initializer, where a throw would poison the type for the whole process.
    /// </summary>
    [Fact]
    public void NoStaticInitializerInTheUiHelpersReadsTheThemeResources()
    {
        var ui = File.ReadAllText(Path.Combine(ViewsDirectory(), "AdminLibrariesUi.cs"));
        Assert.DoesNotContain("ContrastFill", ui);
        var risky = ui.Split('\n').Select((line, index) => (line, number: index + 1))
            .Where(entry => Regex.IsMatch(entry.line, @"^\s*(?:internal|private|public)?\s*static\s+(?:readonly\s+)?[\w<>\.\?\[\], ]+\s+\w+\s*(?:\{\s*get;\s*\}\s*)?=[^>]") && Regex.IsMatch(entry.line, @"Application\.Current|Resources\[|\bBrush\(|AdminMonitorUi\.|\bDestructive\b|XamlReader"))
            .Select(entry => entry.number).ToList();
        Assert.Empty(risky);
        Assert.Contains("DestructiveFill => ToColor(AdminLibrariesPalette.DestructiveFill)", ui);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
