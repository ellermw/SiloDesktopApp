namespace SiloPlayer.Tests;

public sealed class HomeHeroCurrentParityTests
{
    [Fact]
    public void HeroPrimaryActionMatchesCurrentMediaNavigationSemantics()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "HeroCarousel.xaml.cs"));

        Assert.Contains("player.PlayAsync(item.ContentId)", source);
        Assert.Contains("Navigate<EbookReaderPage>", source);
        Assert.Contains("item.Type is not (\"movie\" or \"episode\" or \"audiobook\")", source);
        Assert.Contains("MoreInfoButton_Click", source);
        Assert.Contains("ToggleAudiobookPlayback", source);
    }

    [Fact]
    public void HeroIncludesCurrentRuntimeAndSlideshowControls()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "HeroCarousel.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "HeroCarousel.xaml"));

        Assert.Contains("FormatRuntime(item.DurationSeconds", code);
        Assert.Contains("AddHeroMeta($\"IMDb", code);
        Assert.Contains("root.ActualWidth >= 1400 ? 68d", code);
        Assert.Contains("RootGrid_PointerEntered", code);
        Assert.Contains("PauseCarouselButton_Click", code);
        Assert.Contains("private bool _isPaused;", code);
        Assert.DoesNotContain("_manuallyPaused", code);
        Assert.DoesNotContain("_pointerPaused", code);
        Assert.Contains("x:Name=\"PauseCarouselButton\"", xaml);
        Assert.Contains("Click=\"MoreInfoButton_Click\"", xaml);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
