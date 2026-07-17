namespace SiloPlayer.Tests;

public sealed class CurrentItemCardParityTests
{
    [Fact]
    public void PosterCardUsesCurrentItemCardHoverWithoutPlaybackChrome()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "PosterCard.xaml"));
        var code = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "PosterCard.xaml.cs"));

        Assert.Contains("x:Name=\"HoverBrighten\"", xaml);
        Assert.Contains("x:Name=\"CardHoverTransform\"", xaml);
        Assert.DoesNotContain("HoverPlayButton", xaml);
        Assert.DoesNotContain("HoverDim", xaml);
        Assert.DoesNotContain("HoverBorder", xaml);
        Assert.Contains("x:Name=\"MoreButton\"", xaml);
        Assert.Contains("MoreButton_Click", code);
        Assert.Contains("scale: 1.06, translateY: -4.0", code);
    }

    [Fact]
    public void VirtualizedLibraryCardUsesTheSameLiftZoomAndMenuPlacement()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "LibraryGridCard.cs"));

        Assert.Contains("_posterHoverTransform.ScaleX = 1.06", code);
        Assert.Contains("_cardHoverTransform.TranslateY = -4", code);
        Assert.Contains("_hoverBrighten.Opacity = 1", code);
        Assert.Contains("SetLeft(_moreButton, cardWidth - 42)", code);
        Assert.Contains("SetTop(_moreButton, posterHeight - 42)", code);
    }

    [Fact]
    public void EpisodeCardsUseSeriesTitleEpisodeTitleAndEpisodeCode()
    {
        var item = new SiloPlayer.Core.Models.Home.MediaItem
        {
            Type = "episode",
            Title = "The Episode",
            SeriesTitle = "The Series",
            SeasonNumber = 2,
            EpisodeNumber = 7,
        };

        Assert.Equal("The Series", SiloPlayer.Core.Services.MediaItemDisplayText.BuildTitle(item));
        Assert.Equal("The Episode", SiloPlayer.Core.Services.MediaItemDisplayText.BuildEpisodeTitle(item));
        Assert.Equal("S02E07", SiloPlayer.Core.Services.MediaItemDisplayText.BuildSubtitle(item, null));

        var root = FindRepositoryRoot();
        var posterXaml = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "PosterCard.xaml"));
        Assert.Contains("x:Name=\"EpisodeTitleText\"", posterXaml);
    }

    [Fact]
    public void UpcomingCardsExposeScheduleAndPremierePresentation()
    {
        var upcoming = new SiloPlayer.Core.Models.Home.SectionItemUpcomingEvent
        {
            Type = "episode",
            AirDate = "2026-07-18",
            AirTime = "20:30:00",
            SeasonNumber = 3,
            EpisodeNumber = 4,
            EpisodeTitle = "A New Chapter",
            Badges = ["season_premiere"],
        };

        Assert.Equal(
            "S3 \u00b7 E4 - A New Chapter",
            SiloPlayer.Core.Services.MediaItemDisplayText.FormatUpcomingSubtitle(upcoming));
        Assert.Contains("Jul 18", SiloPlayer.Core.Services.MediaItemDisplayText.FormatUpcomingSchedule(upcoming));

        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "PosterCard.xaml.cs"));
        Assert.Contains("Series Premiere", code);
        Assert.Contains("FormatUpcomingSchedule", code);
    }

    [Fact]
    public void ContinueWatchingChoosesCoverCardsForCoverMedia()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(
            root, "src", "SiloPlayer", "Controls", "SectionRow.xaml.cs"));

        Assert.Contains("allCoverMedia", code);
        Assert.Contains("item.Type is \"movie\" or \"audiobook\" or \"ebook\"", code);
        Assert.Contains("allAudiobooks", code);
    }

    [Fact]
    public void NextInSeriesCardsShowBookAndSeriesMetadata()
    {
        var item = new SiloPlayer.Core.Models.Home.MediaItem
        {
            Type = "audiobook",
            Title = "Second Book",
            ItemSource = "next_in_series",
            SeriesTitle = "The Saga",
            Badges = ["Book 2"],
        };

        Assert.Equal(
            "Book 2 \u00b7 The Saga",
            SiloPlayer.Core.Services.MediaItemDisplayText.BuildSubtitle(item, null));
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
