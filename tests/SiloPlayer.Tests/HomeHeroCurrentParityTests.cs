namespace SiloPlayer.Tests;

public sealed class HomeHeroCurrentParityTests
{
    [Fact]
    public void HeroPrimaryActionMatchesCurrentMediaNavigationSemantics()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "HeroCarousel.xaml.cs"));

        Assert.Contains("player.PlayAsync(item.ContentId, libraryId: LibraryId)", source);
        Assert.Contains("MediaNavigationContext.Detail(item.ContentId, LibraryId)", source);
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

        Assert.Contains("FormatRuntime(runtimeSeconds)", code);
        Assert.Contains("RatingPresentation.PrimaryCardRating(item.RatingImdb, item.RatingTmdb)", code);
        Assert.Contains("DisplayRatingEntry.Create(primaryRating", code);
        Assert.Contains("width >= 1280 ? 72d", code);
        Assert.Contains("width >= 640 ? 48d", code);
        Assert.Contains("Math.Clamp(root.ActualHeight * heightRatio, 380, 760)", code);
        Assert.Contains("_sizeRoot.SizeChanged -= SizeRoot_SizeChanged", code);
        Assert.Contains("RootGrid_PointerEntered", code);
        Assert.Contains("PauseCarouselButton_Click", code);
        Assert.Contains("private bool _isPaused;", code);
        Assert.Contains("UpdateThemeGradientColors", code);
        Assert.Contains("ThumbhashDecoder.GetAmbientColor", code);
        Assert.Contains("AreSystemAnimationsEnabled", code);
        Assert.Contains("OnHeroGotFocus", code);
        Assert.Contains("HeroOverview.MaxLines = width >= 640 ? 0 : 2", code);
        Assert.Contains("_restingArrowOpacity", code);
        Assert.DoesNotContain("_manuallyPaused", code);
        Assert.DoesNotContain("_pointerPaused", code);
        Assert.Contains("x:Name=\"PauseCarouselButton\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Previous slide\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Next slide\"", xaml);
        Assert.Contains("x:Name=\"ProgressRailContainer\"", xaml);
        Assert.Contains("ProgressRailContainer.Visibility = Visibility.Collapsed", code);
        Assert.Contains("BuildOpacity(incoming, 1, 1000)", code);
        Assert.Contains("Click=\"MoreInfoButton_Click\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Featured content\"", xaml);
        Assert.Contains("x:Name=\"AmbientGlowColor\"", xaml);
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
