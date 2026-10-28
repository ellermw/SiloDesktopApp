namespace SiloPlayer.Tests;

public sealed class ProfileAvatarSourceTests
{
    [Fact]
    public void SettingsProfileAvatarsLoadThroughTheSvgAwareConverter()
    {
        // new Uri("/profile-avatars/…") throws on Windows and aborted the
        // remaining Settings profile cards; BitmapImage cannot decode SVG.
        var settings = Read("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        Assert.DoesNotContain("new Uri(profile.AvatarUrl)", settings);
        Assert.DoesNotContain("BitmapImage(avatarUri)", settings);
        Assert.Equal(2, CountOf(settings, "AvatarImages.Convert("));

        var converter = Read("src", "SiloPlayer", "Converters", "UrlToImageSourceConverter.cs");
        Assert.Contains("SvgArtwork.IsSvgUrl(uri)", converter);
        Assert.Contains("new SvgImageSource()", converter);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string Read(params string[] parts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SiloPlayer.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine([root.FullName, .. parts]));
    }
}
