namespace SiloPlayer.Tests;

public sealed class DateTimePreferenceSourceTests
{
    private static readonly string RepoRoot = TestRepository.Root;

    [Fact]
    public void UserFacingAppCodeDoesNotBypassTheSharedDateTimePreferenceFormatter()
    {
        var appRoot = Path.Combine(RepoRoot, "src", "SiloPlayer");
        var helper = Path.Combine(appRoot, "Helpers", "DateTimeDisplay.cs");
        var bypasses = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, helper, StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => entry.line.Contains("ToLocalTime().ToString", StringComparison.Ordinal)
                || entry.line.Contains("LocalDateTime.ToString", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(RepoRoot, entry.path)}:{entry.index + 1}")
            .ToArray();

        Assert.True(bypasses.Length == 0,
            "Date/time display bypasses the user's ui.date_format/ui.time_format preference: "
            + string.Join(", ", bypasses));
    }

    [Fact]
    public void MediumDateFallbackMatchesTheWebUiAbbreviatedMonthContract()
    {
        Assert.Equal("Nov 12, 2026", SiloPlayer.Core.Localization.DisplayDateFormatting.Date(
            new DateTime(2026, 11, 12), "auto", System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            localized: false, medium: true));
    }
}
