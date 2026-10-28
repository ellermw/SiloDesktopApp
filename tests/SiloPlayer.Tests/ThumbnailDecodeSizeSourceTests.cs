using System.Text.RegularExpressions;

namespace SiloPlayer.Tests;

public sealed class ThumbnailDecodeSizeSourceTests
{
    // Without DecodePixelWidth WinUI decodes every poster at full resolution
    // (a 1000x1500 poster is ~6 MB of pixels) even when it is shown at 44 px.
    [Theory]
    [InlineData("Views", "CollectionsPage.xaml.cs", 1)]
    [InlineData("Views", "SettingsPage.NotInterested.cs", 1)]
    [InlineData("Views", "SettingsPage.xaml.cs", 1)]
    [InlineData("Controls", "MatchItemDialog.xaml.cs", 2)]
    public void RemoteThumbnailsDecodeAtTheirDisplayedSize(string folder, string file, int expected)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SiloPlayer.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "SiloPlayer", folder, file));
        var remote = Regex.Matches(source, @"BitmapImage\((posterUri|imageUri)\)([^;]*)").ToList();
        Assert.Equal(expected, remote.Count);
        foreach (var match in remote)
        {
            Assert.Contains("DecodePixelType = ", match.Groups[2].Value);
            Assert.Matches(@"DecodePixelWidth = [^,}]+", match.Groups[2].Value);
        }
    }
}
