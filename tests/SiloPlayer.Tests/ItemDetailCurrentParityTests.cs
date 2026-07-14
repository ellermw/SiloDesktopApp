using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Tests;

public sealed class ItemDetailCurrentParityTests
{
    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void ItemDetailDeserializesRemoteVideosAndLocalExtras()
    {
        const string json = """
        {
          "content_id": "movie-1",
          "type": "movie",
          "title": "Example",
          "videos": [{
            "kind": "trailer",
            "site": "YouTube",
            "site_key": "abc_123-Z",
            "name": "Official Trailer",
            "language": "en",
            "is_official": true
          }],
          "extras": [{
            "content_id": "extra-1",
            "kind": "behind_the_scenes",
            "title": "Making Of",
            "duration_seconds": 121.5,
            "file_id": 42
          }],
          "effective_version_edition_key": "imax",
          "playback_variants": [{
            "variant_id": "imax-variant",
            "edition_raw": "IMAX Enhanced",
            "edition_key": "imax",
            "part_count": 1,
            "default_file_id": 77,
            "parts": [{
              "part_index": 0,
              "default_file_id": 77,
              "versions": [{
                "file_id": 77,
                "resolution": "2160p",
                "codec_video": "hevc",
                "codec_audio": "truehd",
                "hdr": true,
                "container": "mkv",
                "file_size": 1,
                "duration": 1,
                "bitrate": 1,
                "edition_key": "imax"
              }]
            }]
          }]
        }
        """;

        var item = JsonSerializer.Deserialize<MediaItemDetail>(json, ApiJson);

        Assert.NotNull(item);
        var video = Assert.Single(item.Videos);
        Assert.Equal("abc_123-Z", video.SiteKey);
        Assert.True(video.IsOfficial);
        var extra = Assert.Single(item.Extras);
        Assert.Equal("extra-1", extra.ContentId);
        Assert.Equal(121.5, extra.DurationSeconds);
        Assert.Equal(42, extra.FileId);
        Assert.Equal("imax", item.EffectiveVersionEditionKey);
        var variant = Assert.Single(item.PlaybackVariants);
        Assert.Equal("IMAX Enhanced", variant.EditionRaw);
        Assert.Equal(77, Assert.Single(variant.Parts).DefaultFileId);
    }

    [Fact]
    public void DetailPageCarriesCurrentTrailerExtraBehaviorAndSectionOrder()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");

        Assert.Contains("Trailers &amp; More", xaml);
        Assert.Contains("www.youtube-nocookie.com/embed", page);
        Assert.Contains("video.Site.Equals(\"youtube\"", page);
        Assert.Contains("playable.GroupBy(extra => extra.Kind)", page);
        Assert.Contains("await playerService.PlayAsync(contentId)", page);
        Assert.Contains("ArrangeCurrentWebUiContentOrder", page);
        Assert.Contains("MediaLocationsSection, TrailersSection, ExtrasSection", page);
        Assert.Contains("SeasonsSection, EpisodesSection, TrailersSection, ExtrasSection", page);
    }

    [Fact]
    public void DetailActionBarCarriesCurrentOverflowAndCuratorActions()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var api = Read("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var playback = Read("src", "SiloPlayer.Core", "Api", "PlaybackApi.cs");

        Assert.Contains("Add to Collection", page);
        Assert.Contains("Search Subtitles", page);
        Assert.Contains("Media Info", page);
        Assert.Contains("View Play History", page);
        Assert.Contains("Re-detect Intro Markers", page);
        Assert.Contains("Edit Markers", page);
        Assert.Contains("Split Versions", page);
        Assert.Contains("ShowRefreshMetadataDialogAsync", page);
        Assert.Contains("new { mode }", api);
        Assert.Contains("/split", api);
        Assert.Contains("GetItemMarkersAsync", playback);
        Assert.Contains("SetItemMarkersAsync", playback);
    }

    [Fact]
    public void VersionChoiceSelectsBeforePlayAndPlayCarriesSelectedFileId()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");

        Assert.Contains("x:Name=\"EditionButton\"", xaml);
        Assert.Contains("ConfigureVersionSelectors", page);
        Assert.Contains("NavigateToPlayer(ViewModel.Item.ContentId, fileId: _selectedVersion?.FileId)", page);
        Assert.DoesNotContain("_ = playerService.SwitchVersionAsync(fileVersion)", page);
    }

    [Fact]
    public void DetailPageCarriesCurrentOnViewTranslationContract()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var api = Read("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");
        var model = Read("src", "SiloPlayer.Core", "Models", "Catalog", "MediaItemDetail.cs");

        Assert.Contains("PendingTranslationLanguage", model);
        Assert.Contains("/api/v1/metadata/ai/status", api);
        Assert.Contains("/translate-description", api);
        Assert.Contains("ConfigureOnViewTranslationAsync", page);
        Assert.Contains("TimeSpan.FromSeconds(45)", page);
        Assert.Contains("TimeSpan.FromSeconds(2)", page);
    }

    [Fact]
    public void DetailPageRoutesCurrentAudiobookEbookAndMangaSurfaces()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var api = Read("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");
        var model = Read("src", "SiloPlayer.Core", "Models", "Catalog", "MediaItemDetail.cs");

        Assert.Contains("AudiobookChaptersSection", xaml);
        Assert.Contains("MangaChaptersSection", xaml);
        Assert.Contains("BookRelatedSection", xaml);
        Assert.Contains("Resume Reading", page);
        Assert.Contains("Listen from Start", xaml);
        Assert.Contains("new EbookReaderNavigation(target, _readerTargetFileId)", page);
        Assert.Contains("startPositionOverride: seconds", page);
        Assert.Contains("GetMangaSeriesFilesAsync", api);
        Assert.Contains("MangaDetailExtension", model);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException();
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }
}
