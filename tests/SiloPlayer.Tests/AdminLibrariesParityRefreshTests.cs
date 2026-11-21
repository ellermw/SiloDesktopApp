using System.Text.RegularExpressions;
using Xunit;

namespace SiloPlayer.Tests;

/// <summary>
/// Source guards for the Libraries visual refresh against official Silo Server e130f262d (the WebUI theme radius,
/// the preference-aware date formatter, the destructive fill added in #4700). Rendered evidence lives in the
/// <c>admin-libraries</c> native fixture; these tests only keep the decisions from silently regressing.
/// </summary>
public sealed class AdminLibrariesParityRefreshTests
{
    private static readonly Regex LiteralRadius = new(@"CornerRadius\s*=\s*new\((6|8|10|12|16|20|24)\)", RegexOptions.Compiled);

    private static string[] PageSources()
    {
        var views = Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Views", "Admin");
        return Directory.EnumerateFiles(views, "*.cs")
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("AdminLibrariesPage", StringComparison.Ordinal) || name is "AdminLibrariesUi.cs" or "AdminLibraryEditorView.cs"))
            .ToArray();
    }

    /// <summary>The theme radius is 0.75rem, so rounded-md/-lg/-xl/-2xl are 10/12/16/20 px: the page names them instead of repeating literals.</summary>
    [Fact]
    public void PageRadiiComeFromTheNamedThemeRadiiNotLiterals()
    {
        var files = PageSources();
        Assert.True(files.Length >= 9, "the Libraries view sources were found");
        var offenders = files.SelectMany(path => File.ReadAllLines(path).Select((line, index) => (Path.GetFileName(path), index + 1, line)))
            .Where(entry => LiteralRadius.IsMatch(entry.line)).Select(entry => entry.Item1 + ":" + entry.Item2).ToArray();
        Assert.Empty(offenders);
        var ui = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesUi.cs"));
        Assert.Contains("RadiusSm = 8, RadiusMd = 10, RadiusLg = 12, RadiusXl = 16, Radius2Xl = 20", ui);
    }

    /// <summary>The official formatDateTime follows the user's date and time format settings; the page goes through the shared formatter.</summary>
    [Fact]
    public void PageDatesGoThroughThePreferenceFormatter()
    {
        var root = FindRepositoryRoot();
        var views = PageSources();
        Assert.DoesNotContain(views, path => File.ReadAllText(path).Contains("P.DateTimeText(", StringComparison.Ordinal));
        Assert.DoesNotContain(views, path => Regex.IsMatch(File.ReadAllText(path), @"\.ToString\(""(d|T)"""));
        var ui = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesUi.cs"));
        Assert.Contains("DateTimeDisplay.FormatDate(value)", ui);
        Assert.Contains("DateTimeDisplay.FormatTime(value, seconds: true)", ui);
    }

    /// <summary>#4700: a destructive badge or button is the solid destructive fill under white text, not the text colour at 90%.</summary>
    [Fact]
    public void DestructiveBadgesAndButtonsUseTheContrastSafeFill()
    {
        var ui = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesUi.cs"));
        Assert.Contains("DestructiveFill", ui);
        Assert.Contains("Ratio(result, text) < 4.5", ui);
        Assert.DoesNotContain("Solid(Destructive, 0.9)", ui);
        Assert.Contains("BadgeTone.Destructive => Solid(Colors.White)", ui);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
