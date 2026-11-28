using System.Text.RegularExpressions;

namespace SiloPlayer.Tests;

public sealed class ThumbnailDecodeSizeSourceTests
{
    // Without DecodePixelWidth WinUI decodes every poster at full resolution
    // (a 1000x1500 poster is ~6 MB of pixels) even when it is shown at 44 px.
    [Theory]
    [InlineData("Views", "CollectionsPage.xaml.cs", 1)]
    [InlineData("Views", "SettingsPage.NotInterested.cs", 1)]
    [InlineData("Controls", "MatchItemDialog.xaml.cs", 2)]
    public void RemoteThumbnailsDecodeAtTheirDisplayedSize(string folder, string file, int expected)
    {
        var source = ReadSource(folder, file);
        var remote = Regex.Matches(source, @"BitmapImage\((posterUri|imageUri)\)([^;]*)").ToList();
        Assert.Equal(expected, remote.Count);
        foreach (var match in remote)
        {
            Assert.Contains("DecodePixelType = ", match.Groups[2].Value);
            Assert.Matches(@"DecodePixelWidth = [^,}]+", match.Groups[2].Value);
        }
    }

    [Fact]
    public void HouseholdStreamPosterDecodesAtItsDisplayedWidth()
    {
        // Settings uses the shared artwork loader instead of a URI-backed BitmapImage.
        var settings = ReadSource("Views", "SettingsPage.xaml.cs");
        var start = settings.IndexOf("private HouseholdStreamRow BuildHouseholdStreamRow(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var row = settings[start..];
        var poster = Regex.Match(row, @"var poster = new Border\s*\{\s*Width = (\d+),");
        Assert.True(poster.Success);
        var width = poster.Groups[1].Value;
        Assert.Contains($"new AdminPosterArtwork(row, \"household-poster:\" + owner.AccountId, Current, lifetime, {width}, source => image.Source = source)", row);
        Assert.Contains("artwork.SetUrl(url);", row);

        var artwork = ReadSource("Helpers", "AdminPosterArtwork.cs");
        Assert.Contains("_width = width;", artwork);
        Assert.Contains("var bitmap = new BitmapImage { DecodePixelWidth = _width, DecodePixelType = DecodePixelType.Logical };", artwork);
        Assert.Contains("await bitmap.SetSourceAsync(stream);", artwork);
    }

    private static string ReadSource(string folder, string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SiloPlayer.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, "src", "SiloPlayer", folder, file));
    }
}
