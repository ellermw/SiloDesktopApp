using System.Text.RegularExpressions;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The Home sections page shows only official or table text: a display string cannot be written into the page's source again.
/// The scan reads the page's own files; its rules are themselves tested on small samples so a reintroduced literal is known to be caught.
/// </summary>
public sealed partial class AdminHomeSectionsLiteralTests
{
    /// <summary>Property names and calls that put their string argument on screen (or into the accessibility tree), and the result of a switch arm.</summary>
    [GeneratedRegex(@"(?:\b(?:Text|Content|Header|Title|PlaceholderText|PrimaryButtonText|SecondaryButtonText|CloseButtonText|Label|Description|ToolTip)\s*=\s*|=>\s*|\b(?:U\.Text|U\.Alert|U\.Input|U\.Action|U\.Title|Action|Field|Menu|MenuAsync|SetName|SetHelpText|SetToolTip|Success|Warning|Error)\(\s*(?:[A-Za-z_][\w.]*\s*,\s*)?)""((?:[^""\\]|\\.)*)""")]
    private static partial Regex DisplaySink();

    [GeneratedRegex(@"\$?@?""((?:[^""\\]|\\.)*)""")]
    private static partial Regex StringLiteral();

    /// <summary>A catalog key, an automation id, a theme resource, an icon name, a glyph escape or a wire value: names, not words.</summary>
    [GeneratedRegex(@"^(?:(?:web|native|common|settingsManifest)\.[A-Za-z0-9_.]+|HomeSections?[A-Za-z0-9_]*|[A-Za-z]+(?:Brush|Color)|[A-Z][a-z]+(?:[A-Z][a-z0-9]+)+|[a-z]+(?:-[a-z0-9]+)+|\\u[0-9A-Fa-f]{4}|[a-z]+(?:[A-Z][a-z0-9]*)+|_?[a-z][a-z0-9_]*(?:[.:][a-z0-9_]*)*)$")]
    private static partial Regex Name();

    /// <summary>What a text sink may be given: a catalog key or an id, never a word.</summary>
    [GeneratedRegex(@"^(?:(?:web|native|common|settingsManifest)\.[A-Za-z0-9_.]+|HomeSections?[A-Za-z0-9_]*)$")]
    private static partial Regex KeyOrId();

    /// <summary>Literals that are data, not display text: request values, a regular expression, a placeholder URL and a XAML binding.</summary>
    private static readonly HashSet<string> Data = new(StringComparer.Ordinal)
    {
        "Row", "Preview", "Label", "N0", @"\s*·\s*", "https://mdblist.com/lists/…", "{TemplateBinding Content}",
    };

    private static string WithoutComment(string line)
    {
        var inText = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inText = !inText;
            if (!inText && line[i] == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    /// <summary>The string literals of <paramref name="source"/> that read as display text. With <paramref name="sinksOnly"/>, only those handed straight to a text, content, name or label.</summary>
    internal static IReadOnlyList<string> DisplayLiterals(string source, bool sinksOnly = false)
    {
        var found = new List<string>();
        foreach (var raw in source.Split('\n'))
        {
            var line = WithoutComment(raw);
            foreach (Match match in DisplaySink().Matches(line))
            {
                var text = match.Groups[1].Value;
                if (text.Any(char.IsLetter) && !KeyOrId().IsMatch(text) && !Data.Contains(text)) found.Add(text);
            }
            if (sinksOnly) continue;
            foreach (Match match in StringLiteral().Matches(line))
            {
                var text = match.Groups[1].Value;
                if (text.Any(char.IsLetter) && !Name().IsMatch(text) && !Data.Contains(text) && !found.Contains(text)) found.Add(text);
            }
        }
        return found;
    }

    [Fact]
    public void ThePagesSourceHoldsNoDisplayLiteral()
    {
        var folder = Path.Combine(TestRepository.Root, "src/SiloPlayer/Views/Admin");
        var files = Directory.GetFiles(folder, "AdminHomeSections*.cs").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.True(files.Length >= 9, "the scan found the page's files");
        var offenders = new List<string>();
        foreach (var file in files)
        {
            // The helper file is icon data and XAML templates, so only what it hands straight to a text sink is read.
            var sinksOnly = Path.GetFileName(file) == "AdminHomeSectionsUi.cs";
            foreach (var literal in DisplayLiterals(File.ReadAllText(file), sinksOnly)) offenders.Add(Path.GetFileName(file) + ": \"" + literal + "\"");
        }
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("(\"true\", \"Yes\"), (\"false\", \"No\")", "Yes")]
    [InlineData("var sort = new[] { (\"rating_rt_critic\", \"RT critic score\") };", "RT critic score")]
    [InlineData("\"is_not\" => \"is not\", \"contains\" => \"contains\"", "is not")]
    [InlineData("status.Text = $\"{count} choices · page {page}\";", "{count} choices · page {page}")]
    [InlineData("panel.Children.Add(U.Text(\"Nothing here\", 13, true));", "Nothing here")]
    [InlineData("button.Content = \"yes\";", "yes")]
    [InlineData("var tag = U.Text(\"yes\");", "yes")]
    [InlineData("MenuAsync(menu, \"Preview row\", run);", "Preview row")]
    [InlineData("\"is\" => \"is\"", "is")]
    public void AReintroducedDisplayLiteralIsCaught(string source, string expected) => Assert.Contains(expected, DisplayLiterals(source));

    [Theory]
    [InlineData("panel.Children.Add(U.Text(T(\"web.home.rows.page.empty\"), 13, true));")]
    [InlineData("var box = Input(label, \"HomeSectionTitle\", \"\", value => { }); // \"a comment may say anything\"")]
    [InlineData("AutomationProperties.SetAutomationId(list, \"HomeSectionsRows\"); AutomationProperties.SetName(button, T(\"native.homeSections.search\"));")]
    [InlineData("Select(\"HomeSectionRuleScope\", [(\"movie\", T(\"native.homeSections.movies\"))], scope);")]
    [InlineData("Title = \"Row\", Config = config")]
    [InlineData("button.Content = AdminHomeSectionsUi.Icon(\"chevron-left\", 16, brush); toggle.Foreground = U.Brush(\"PrimaryTextBrush\");")]
    [InlineData("field switch { \"title\" => \"web.home.addRow.form.rowName\", _ => \"web.home.addRow.more.heroBanner\" }")]
    public void AnIdOrACatalogKeyIsNotADisplayLiteral(string source) => Assert.Empty(DisplayLiterals(source));

    [Fact]
    public void TheOfficialTextTheRuleBuilderUsesIsInTheCatalog()
    {
        string[] operators = ["Is", "IsNot", "Contains", "NotContains", "BeginsWith", "EndsWith", "AtLeast", "AtMost", "Above", "Below", "Before", "After", "Between", "InLast", "NotInLast"];
        foreach (var op in operators) Assert.True(AdminHomeSectionsText.Has("web.collections.builder.op" + op), op);
        Assert.Equal("is not", AdminHomeSectionsText.T("web.collections.builder.opIsNot"));
        Assert.Equal("does not contain", AdminHomeSectionsText.T("web.collections.builder.opNotContains"));
        Assert.Equal("begins with", AdminHomeSectionsText.T("web.collections.builder.opBeginsWith"));
        Assert.Equal("is at least", AdminHomeSectionsText.T("web.collections.builder.opAtLeast"));
        Assert.Equal("is not in the last", AdminHomeSectionsText.T("web.collections.builder.opNotInLast"));
        Assert.Equal("Yes", AdminHomeSectionsText.T("web.catalog.rules.yes"));
        Assert.Equal("No", AdminHomeSectionsText.T("web.catalog.rules.no"));
        Assert.Equal("RT Critic Rating", AdminHomeSectionsText.T("web.common.sort.rtCriticRating"));
        Assert.Equal("RT Audience Rating", AdminHomeSectionsText.T("web.common.sort.rtAudienceRating"));
        Assert.Equal("Close", AdminHomeSectionsText.T("web.ui.close"));
        Assert.False(AdminHomeSectionsText.NativeOnly.ContainsKey("native.homeSections.close"));
    }

    [Fact]
    public void ThePickerLinesComeFromTheNativeOnlyTable()
    {
        Assert.Equal("7 choices · page 2", AdminHomeSectionsText.T("native.homeSections.choicesPage", new { count = 7L, page = 2L }));
        Assert.Equal("Only the first 3 collections are listed, so a search can miss some. To add one that is not listed, use “Find collection by ID”.",
            AdminHomeSectionsText.T("native.homeSections.collectionsTruncated", new { count = 3L, find = AdminHomeSectionsText.T("native.homeSections.findCollection") }));
    }

    [Theory]
    [InlineData("\n")] [InlineData("\r")] [InlineData("\t")] [InlineData("\0")] [InlineData("\u001B")] [InlineData("\u007F")] [InlineData("\u0085")]
    public void ANameShownInAConfirmationLosesItsControlCharacters(string control) => Assert.Equal("Movies Shows", AdminHomeSectionsText.OneLine("Movies" + control + "Shows"));

    [Theory]
    [InlineData("\u2028")] [InlineData("\u2029")]
    public void ANameShownInAConfirmationLosesItsLineAndParagraphSeparators(string separator) => Assert.Equal("Delete me? Yes", AdminHomeSectionsText.OneLine("Delete me?" + separator + "Yes"));

    [Theory]
    [InlineData("\u200E")] [InlineData("\u200F")] [InlineData("\u061C")]
    [InlineData("\u202A")] [InlineData("\u202B")] [InlineData("\u202C")] [InlineData("\u202D")] [InlineData("\u202E")]
    [InlineData("\u2066")] [InlineData("\u2067")] [InlineData("\u2068")] [InlineData("\u2069")]
    public void ANameShownInAConfirmationLosesItsBidirectionalControls(string control) => Assert.Equal("a b", AdminHomeSectionsText.OneLine("a" + control + "b"));

    [Fact]
    public void AnOrdinaryNameIsKeptAsItIsAndTheEndsAreTrimmed()
    {
        Assert.Equal("Café — 日本語 😀 a\u200Db", AdminHomeSectionsText.OneLine("Café — 日本語 😀 a\u200Db"));
        Assert.Equal("Name", AdminHomeSectionsText.OneLine("\n  Name \u202E\r\n"));
        Assert.Equal("", AdminHomeSectionsText.OneLine(null)); Assert.Equal("", AdminHomeSectionsText.OneLine("\n\u2028"));
    }

    [Fact]
    public void AConfirmationNameIsBoundedAndNeverCutInsideACharacter()
    {
        var bounded = AdminHomeSectionsText.OneLine(new string('a', 500));
        Assert.Equal(AdminHomeSectionsText.NameLimit + 1, bounded.Length); Assert.EndsWith("…", bounded);
        Assert.Equal(new string('a', AdminHomeSectionsText.NameLimit), AdminHomeSectionsText.OneLine(new string('a', AdminHomeSectionsText.NameLimit)));
        Assert.Equal("abcde…", AdminHomeSectionsText.OneLine("abcdefgh", 5));
        Assert.Equal("ab…", AdminHomeSectionsText.OneLine("ab😀c", 3));
        Assert.Equal("ab😀…", AdminHomeSectionsText.OneLine("ab😀cd", 4));
    }
}
