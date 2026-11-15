using System.Text.RegularExpressions;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorIconGeometryTests
{
    // Pinned Lucide 0.576.0 geometry, normalized for concatenation into one XAML path:
    // independent starts are absolute, but implicit lines after upstream 'm' stay relative.
    // Pin the whole glyph so a relative start after an earlier subpath is also rejected.
    [Theory]
    [InlineData("library", "M16 6l4 14 M12 6v14 M8 8v12 M4 4v16")]
    [InlineData("radar", "M19.07 4.93A10 10 0 0 0 6.99 3.34 M4 6h.01 M2.29 9.62A10 10 0 1 0 21.31 8.35 M16.24 7.76A6 6 0 1 0 8.23 16.67 M12 18h.01 M17.99 11.66A6 6 0 0 1 15.77 16.67 M14 12A2 2 0 1 1 10 12A2 2 0 1 1 14 12Z M13.41 10.59l5.66-5.66")]
    [InlineData("send", "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z M21.854 2.147l-10.94 10.939")]
    [InlineData("hard-drive-download", "M12 2v8 M16 6l-4 4-4-4 M4 14H20A2 2 0 0 1 22 16V20A2 2 0 0 1 20 22H4A2 2 0 0 1 2 20V16A2 2 0 0 1 4 14Z M6 18h.01 M10 18h.01")]
    [InlineData("compass", "M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12Z M16.24 7.76l-1.804 5.411a2 2 0 0 1-1.265 1.265L7.76 16.24l1.804-5.411a2 2 0 0 1 1.265-1.265z")]
    [InlineData("download", "M12 15V3 M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5")]
    public void ConcatenatedGlyphPreservesPinnedGeometry(string name, string expected)
    {
        // Read the production switch without taking a WinUI dependency in the unit project.
        var source = File.ReadAllText(Path.Combine(TestRepository.Root,
            "src", "SiloPlayer", "Controls", "AdminMonitorUi.cs"));
        var match = Regex.Match(source, "\"" + Regex.Escape(name) + "\" => \"([^\"]+)\"");
        Assert.True(match.Success, $"Missing icon: {name}");
        Assert.Equal(expected, match.Groups[1].Value);
    }
}
