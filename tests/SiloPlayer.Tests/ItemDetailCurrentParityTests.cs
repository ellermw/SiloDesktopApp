using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Tests;

public sealed class ItemDetailCurrentParityTests
{
    [Fact]
    public void SeasonEpisodeFailureUsesTheCurrentPageLevelErrorContract()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var code = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("x:Name=\"SeasonEpisodesFatalError\"", xaml);
        Assert.Contains("x:Name=\"SeasonEpisodesBackButton\"", xaml);
        Assert.Contains("x:Name=\"SeasonEpisodesFatalErrorText\"", xaml);
        Assert.Contains("ShowSeasonEpisodesFatalError(ViewModel.Item);", code);
        Assert.Contains("ContentScroll.Visibility = Visibility.Collapsed;", code);
        Assert.Contains("Back to {season.SeriesTitle ?? \"Series\"}", code);
        Assert.Contains("navigationToken.IsCancellationRequested", code);
    }

    [Fact]
    public void DetailAndOptionalQueryFailuresMatchTheCurrentWebUiContract()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var code = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("x:Name=\"DetailErrorContent\"", xaml);
        Assert.Contains("Text=\"Item not found.\"", xaml);
        Assert.DoesNotContain("Text=\"Unable to load this item\"", xaml);
        Assert.Contains("ViewModel.ErrorMessage ?? \"Failed to load item\"", code);
        Assert.Contains("SeasonsSection.Visibility = Visibility.Collapsed;", code);
        Assert.Contains("SimilarSection.Visibility = Visibility.Collapsed;", code);
        Assert.Contains("SiblingEpisodesSection.Visibility = Visibility.Collapsed;", code);
    }

    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void DetailMetadataOverviewAndActionsAreComposedInsideArtworkHero()
    {
        var code = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");

        Assert.Contains("ComposeHeroLayout();", code);
        Assert.Contains("MoveIntoHero(HeroMetadataRow)", code);
        Assert.Contains("MoveIntoHero(OverviewText)", code);
        Assert.Contains("MoveIntoHero(HeroActionsRow)", code);
        Assert.Contains("MoveIntoHero(PlaybackOptionsRow)", code);
        Assert.Contains("PlaybackOptionsRow.Children.Add(VersionDropdownButton)", code);
        Assert.Contains("PlaybackOptionsRow.Children.Add(AudioTracksButton)", code);
        Assert.Contains("PlaybackOptionsRow.Children.Add(SubtitlesPopoverButton)", code);
        Assert.Contains("? wide ? 0.42 : 0.35", code);
        Assert.Contains(": wide ? 0.72 : 0.60", code);
        Assert.Contains("x:Name=\"HeroInfoPanel\"", xaml);
        Assert.Contains("x:Name=\"HeroActionsRow\"", xaml);
        Assert.Contains("x:Name=\"PlaybackOptionsRow\"", xaml);
        var titleStart = xaml.IndexOf("x:Name=\"TitleText\"", StringComparison.Ordinal);
        var titleEnd = xaml.IndexOf("/>", titleStart, StringComparison.Ordinal);
        Assert.True(titleStart >= 0 && titleEnd > titleStart);
        Assert.DoesNotContain("MaxLines", xaml[titleStart..titleEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void DetailLoadingAndBelowFoldSectionsFollowCurrentResponsiveLayout()
    {
        var code = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "ItemDetailViewModel.cs");

        Assert.Contains("x:Name=\"DetailSkeletonHero\"", xaml);
        Assert.Contains("x:Name=\"DetailSkeletonPoster\" Width=\"220\" Height=\"330\"", xaml);
        Assert.Contains("x:Name=\"DetailSkeletonSections\"", xaml);
        Assert.Contains("MaxWidth=\"1520\"", xaml);
        Assert.Contains("MaxWidth=\"1400\"", xaml);
        Assert.Contains("Text=\"Crew\"", xaml);
        Assert.Contains("x:Name=\"SimilarPanel\"", xaml);
        Assert.DoesNotContain("x:Name=\"SimilarScrollViewer\"", xaml);
        Assert.Contains("HorizontalScrollMode=\"Enabled\"", xaml);
        Assert.Contains("TitleText.FontSize = isSeason", code);
        Assert.Contains("TitleText.CharacterSpacing = isSeason ? -25 : -50", code);
        Assert.Contains("TitleText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight", code);
        Assert.Contains("TitleText.LineHeight = TitleText.FontSize * (isSeason ? 1.1d : 0.98d)", code);
        Assert.Contains("FontWeight=\"ExtraBold\"", xaml);
        Assert.Contains("width >= 1280 ? 6", code);
        Assert.Contains("card.SetCatalogGridLayout(cardWidth)", code);
        Assert.Contains("response.Items.Take(12)", viewModel);
        Assert.Contains("x:Name=\"SeasonsPrevButton\"", xaml);
        Assert.Contains("x:Name=\"SeasonsNextButton\"", xaml);
        Assert.Contains("Click=\"SeasonsPrev_Click\"", xaml);
        Assert.Contains("Click=\"SeasonsNext_Click\"", xaml);
        Assert.Contains("UpdateSeasonScrollButtons", code);
    }

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
        Assert.Contains("var parent = DetailContentPanel", page);
        Assert.Contains("DetailContentPanel.Spacing = width >= 640 ? 56 : 48", page);
        Assert.DoesNotContain("new Thickness(0, 32, 0, 0)", page);
        Assert.Contains("MediaLocationsSection, TrailersSection, ExtrasSection", page);
        Assert.Contains("SeasonsSection, EpisodesSection, TrailersSection, ExtrasSection", page);
        Assert.True(
            xaml.IndexOf("x:Name=\"MediaLocationsSection\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"TrailersSection\"", StringComparison.Ordinal),
            "Default XAML order should match the current WebUI leaf detail order: media locations before trailers.");
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
        Assert.Contains("item.Type == \"movie\" || item.Type == \"series\"", page);
        Assert.Contains("item.Type.Equals(\"series\"", page);
        Assert.Contains("item.Type.Equals(\"movie\"", page);
        Assert.Contains("ShowRefreshMetadataDialogAsync", page);
        Assert.Contains("new { mode }", api);
        Assert.Contains("/split", api);
        Assert.Contains("GetItemMarkersAsync", playback);
        Assert.Contains("SetItemMarkersAsync", playback);
        Assert.Contains("var hasOverflowActions = false;", page);
        Assert.Contains("if (hasOverflowActions)\n                MoreFlyout.Items.Add(new MenuFlyoutSeparator());", page.Replace("\r\n", "\n"));
    }

    [Fact]
    public void MediaInfoUsesCurrentMultiVersionSpecSheetsAndAudioProfiles()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var models = Read("src", "SiloPlayer.Core", "Models", "Playback", "WatchDetailResponse.cs");
        var web = ReadWeb("web", "src", "pages", "ItemDetail", "components", "mediaSpecSections.ts");

        Assert.Contains("BuildMediaInfoSpecSheet", page);
        Assert.Contains("new Expander", page);
        Assert.Contains("(\"Profile\", track.Profile)", page);
        Assert.Contains("(\"Chroma Subsampling\"", page);
        Assert.Contains("(\"Color Range\", FormatColorRange(track.ColorRange))", page);
        Assert.Contains("\"tv\" => \"Limited (tv)\"", page);
        Assert.Contains("\"pc\" => \"Full (pc)\"", page);
        Assert.Contains("public string? ColorRange { get; set; }", models);
        Assert.Contains("(\"Hearing Impaired\"", page);
        Assert.Contains("DOVIWithHDR10", web);
        Assert.Contains("Dolby Vision (HDR10 compatible)", page);
        Assert.Contains("HDR10 compatible", page);
        Assert.Contains("DolbyVisionCompatibilityLabels", page);
        Assert.Contains("VideoRangeTypeLabels", page);
        Assert.Contains("2 => \"stereo\"", page);
        Assert.Contains("normalized.Contains(\"av1\")", page);
        Assert.Contains("public string? Profile { get; set; }", models);
        Assert.DoesNotContain("AUDIO TRACKS", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailContextAndCuratorMediaLocationsFollowCurrentWebUiPolicy()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var policy = Read("src", "SiloPlayer.Core", "Services", "AuthorizationPolicy.cs");
        var web = ReadWeb("web", "src", "components", "MediaLocations.tsx");

        Assert.Contains("x:Name=\"HeroContextText\"", xaml);
        Assert.Contains("item.Type == \"movie\" ? \"Movie\" : \"Series\"", page);
        Assert.Contains("AuthorizationPolicy.CanCurateMetadata(authService)", page);
        Assert.Contains("_watchDetail.Versions", page);
        Assert.Contains("space-y-3", web);
        Assert.Contains("text-base font-semibold tracking-tight", web);
        Assert.Contains("x:Name=\"MediaLocationsSection\" Visibility=\"Collapsed\" Spacing=\"12\"", xaml);
        Assert.Contains("x:Name=\"MediaLocationsTitle\" Text=\"Media locations\" Style=\"{StaticResource TitleTextStyle}\" FontSize=\"16\"", xaml);
        Assert.Contains("View media info for", page);
        Assert.Contains("ShowMediaInfoDialogAsync(version.FileId)", page);
        Assert.Contains("profile?.IsPrimary == true", policy);
        Assert.Contains("user?.Permissions?.Contains(permission", policy);
        Assert.Contains("VersionRanking.MapAudioLabel(v.CodecAudio)", page);
        Assert.Contains("VersionRanking.MapAudioLabel(version.CodecAudio)", page);
    }

    [Fact]
    public void MovieAndSeriesHeroKickersUseTheExactWebUiSourceFields()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("\"movie\" when item.Studios is { Count: > 0 } => item.Studios[0]", page);
        Assert.Contains("\"series\" when item.Networks is { Count: > 0 } => item.Networks[0]", page);
        Assert.DoesNotContain("item.Type is \"movie\" or \"series\" && item.Networks", page);
    }

    [Fact]
    public void PrePlaySummariesUseCurrentWebUiLanguageAndOffLabels()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var web = ReadWeb("web", "src", "pages", "ItemDetail", "components", "SubtitlesPopover.tsx");

        Assert.Contains("MediaLanguageCatalog.Label(track.Language)", page);
        Assert.Contains("VersionRanking.MapAudioLabel(track.Codec)", page);
        Assert.Contains("return string.Join(\" · \", parts)", page);
        Assert.Contains("2 => \"stereo\"", page);
        Assert.Contains("Auto, Off, optional candidate sections", page);
        Assert.Contains("var autoItem = CreateSubtitleMenuItem(\"Auto\")", page);
        Assert.Contains("var offItem = CreateSubtitleMenuItem(\"Off\")", page);
        Assert.Contains("Text = \"No subtitles available.\"", page);
        Assert.Contains("SubtitlesSummary.Text = \"Auto: Off\"", page);
        Assert.Contains("MediaLanguageCatalog.Label(sub.Language)", page);
        Assert.Contains("\"srt\" or \"subrip\" => \"SRT\"", page);
        Assert.Contains("PreferredTrackSignature: _watchDetail.EffectiveSubtitleTrackSignature", page);
        Assert.Contains("AddTrackGroup(\"Embedded\"", page);
        Assert.Contains("AddTrackGroup(\"External\"", page);
        Assert.Contains("Text = \"Downloaded\"", page);
        Assert.Contains("OrderBy(row => MediaLanguageCatalog.Label(row.Track.Language)", page);
        Assert.Contains("ThenByDescending(row => row.Track.Forced == true)", page);
        Assert.Contains("ThenByDescending(row => row.Track.Default == true)", page);
        Assert.Contains("OrderByDescending(entry => entry.Score)", page);
        Assert.Contains("MinWidth = 300", page);
        Assert.Contains("FormatSubtitleTrackMenuText", page);
        Assert.DoesNotContain("Text = \"Add subtitles...\"", page);
        Assert.DoesNotContain("Add subtitles", web);
    }

    [Fact]
    public void TrailerCardsRevealThePlayOverlayOnlyOnHoverOrFocusLikeTheWebUi()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var web = ReadWeb("web", "src", "pages", "ItemDetail", "components", "TrailersSection.tsx");

        Assert.Contains("group-hover/trailer:opacity-100", web);
        Assert.Contains("var playOverlay = new Border", page);
        Assert.Contains("Opacity = 0", page);
        Assert.Contains("card.PointerEntered += (_, _) => playOverlay.Opacity = 1", page);
        Assert.Contains("card.GotFocus += (_, _) => playOverlay.Opacity = 1", page);
    }

    [Fact]
    public void SplitVersionsAutomaticallyDebouncesItsDryRunPreview()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("await Task.Delay(400, token)", page);
        Assert.Contains("BuildSplitRequest(dryRun: true)", page);
        Assert.Contains("previewText.Text = \"Previewing…\"", page);
        Assert.Contains("dialog.IsPrimaryButtonEnabled = true", page);
        Assert.Contains("BuildSplitRequest(dryRun: false)", page);
        Assert.DoesNotContain("PrimaryButtonText = \"Review Split\"", page);
        Assert.DoesNotContain("Title = \"Confirm Split\"", page);
    }

    [Fact]
    public void VersionControlIsScopedToTheActiveEditionLikeTheWebUi()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var versionFlyout = ReadWeb("web", "src", "pages", "ItemDetail", "components", "VersionFlyout.tsx");

        Assert.Contains("VersionDropdownButton.Visibility = versions.Count > 1", page);
        Assert.Contains("activeVariant?.Parts", page);
        Assert.Contains("EditionButton.Visibility = showEditions", page);
        Assert.Contains("parts.length === 0 && version.container", versionFlyout);
        Assert.Contains("parts.Add(version.Container.ToUpperInvariant())", page);
        Assert.Contains("sortByResolution", versionFlyout);
        Assert.Contains(".OrderByDescending(v => ResolutionRank(v.Resolution))", page);
        Assert.DoesNotContain(".ThenByDescending(v => v.Bitrate)", page);
    }

    [Fact]
    public void SingleVersionResumeUsesTheWebChoiceDialogAndNavigationResetsPlaybackState()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var webLayout = ReadWeb("web", "src", "pages", "ItemDetail", "itemDetailLayout.ts");

        Assert.Contains("ShouldOfferResumeChoice", page);
        Assert.Contains("Title = \"Resume Playback?\"", page);
        Assert.Contains("Content = $\"Resume at {resumeTime}\"", page);
        Assert.Contains("Content = \"Play from Beginning\"", page);
        Assert.Contains("versionCount <= 1", page);
        Assert.Contains("variantCount <= 1", page);
        Assert.Contains("_watchDetail = null", page);
        Assert.Contains("_selectedVersion = null", page);
        Assert.Contains("_selectedSubtitleSignature = null", page);
        Assert.Contains("including a rewatch in flight (played stays true)", webLayout);
        Assert.Contains("any nonzero position remains a", page);
        Assert.DoesNotContain("&& !userData.Played", page);
    }

    [Fact]
    public void VersionChoiceSelectsBeforePlayAndPlayCarriesSelectedFileId()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");

        Assert.Contains("x:Name=\"EditionButton\"", xaml);
        Assert.Contains("ConfigureVersionSelectors", page);
        Assert.Contains("ResolvePlayableContentIdForPlaybackAsync", page);
        Assert.Contains("NavigateToPlayer(contentId, fileId: _selectedVersion?.FileId)", page);
        Assert.DoesNotContain("NavigateToPlayer(ViewModel.Item.ContentId, fileId: _selectedVersion?.FileId)", page);
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
        Assert.Contains("LoadMangaChapterPosterAsync", page);
        Assert.Contains("new Expander", page);
        Assert.Contains("MangaDownload_Click", page);
        Assert.Contains("GetItemVersionsAsync(chapter.ContentId)", page);
        Assert.Contains("Mark chapter unread", page);
        Assert.Contains("MangaJumpButton", xaml);
        Assert.Contains("StartBringIntoView", page);
    }

    [Fact]
    public void DetailAndEpisodeCardsUseSelectedVersionAndCurrentOverlayMetadata()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var episodeModel = Read("src", "SiloPlayer.Core", "Models", "Catalog", "EpisodesResponse.cs");

        Assert.Contains("UpdateSelectedVersionUi", page);
        Assert.Contains("MediaVideoRange.Label", page);
        Assert.Contains(": version.Duration", page);
        Assert.Contains("VersionRanking.PickBestAttributes([selectedVersion]", page);
        Assert.Contains("VersionRanking.PickBestAttributes([best]", page);
        Assert.DoesNotContain("var audioLabel = best.CodecAudio.ToUpperInvariant()", page);
        Assert.Contains("episode.OverlaySummary", page);
        Assert.Contains("AddEpisodeCardOverlays", page);
        Assert.Contains("public SiloPlayer.Core.Models.Home.OverlaySummary? OverlaySummary", episodeModel);
    }

    [Fact]
    public void SeasonDetailTargetsFirstEpisodeThroughCanonicalItemEndpoint()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var api = Read("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");

        Assert.Contains("LoadSeasonEpisodesAsync(", page);
        Assert.Contains("ViewModel.Item.ContentId,", page);
        Assert.Contains("navigationToken", page);
        Assert.Contains("GetItemEpisodesAsync(seasonContentId)", page);
        Assert.Contains("PlayButtonText.Text = \"Play First Episode\"", page);
        Assert.Contains("_playableContentId = firstEpisode?.ContentId", page);
        Assert.Contains("No playable episodes found for this season.", page);
        Assert.DoesNotContain("NavigateToPlayer(ViewModel.Item.ContentId, fromStart: true", page);
        Assert.Contains("BuildSeasonBreadcrumb(item, seasonLabel)", page);
        Assert.Contains("EpisodesHeader.Text = \"Episodes\"", page);
        Assert.Contains("x:Name=\"EpisodesTotalText\"", Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml"));
        Assert.Contains("/api/v1/catalog/items/{Uri.EscapeDataString(seasonContentId)}/episodes", api);
        Assert.DoesNotContain("LoadSeasonEpisodesAsync(ViewModel.Item.SeriesId", page);
    }

    [Fact]
    public void SingleSeasonSeriesLoadsEpisodesThroughTheCanonicalSeasonItemEndpoint()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var webSeries = Read(".codex-tmp", "silo-server-current", "web", "src", "pages", "ItemDetail", "SeriesContent.tsx");

        Assert.Contains("useItemEpisodes(singleSeason?.content_id)", webSeries);
        Assert.Contains("ShowSingleSeasonEpisodesAsync(", page);
        Assert.Contains("ViewModel.Seasons.Count == 1", page);
        Assert.Contains("ViewModel.Seasons[0]", page);
        Assert.Contains("singleSeason.ContentId", page);
        Assert.Contains("singleSeason.SeasonNumber", page);
        Assert.Contains("GetItemEpisodesAsync(seasonContentId)", page);
        Assert.Contains("SeasonNumber == 0", page);
        Assert.Contains("EpisodesHeader.Text = ViewModel.SelectedSeasonNumber == 0", page);
        Assert.DoesNotContain("ViewModel.Seasons.Count <= 1", page);
    }

    [Fact]
    public void DetailNavigationCancelsSupersededAndDetachedLoads()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "ItemDetailViewModel.cs");

        Assert.Contains("ViewModel.CancelPendingLoads();", page);
        Assert.Contains("ViewModel.Item?.ContentId != contentId", page);
        Assert.Contains("AllowConcurrentExecutions = true", viewModel);
        Assert.Contains("GetItemDetailAsync(contentId, ct)", viewModel);
        Assert.Contains("ReferenceEquals(_loadCts, loadCts)", viewModel);
        Assert.Contains("catch (OperationCanceledException)", viewModel);
        Assert.Contains("var contentId = Item?.ContentId;", viewModel);
        Assert.Contains("if (Item?.ContentId != contentId) return;", viewModel);
        Assert.Contains("Publish(MediaSurfaceChangeKind.RatingChanged, contentId, seriesId, nextRating)", viewModel);
        Assert.Contains("Publish(\n                wasWatched ? MediaSurfaceChangeKind.WatchedCleared : MediaSurfaceChangeKind.WatchedMarked,\n                contentId,\n                seriesId)", viewModel.Replace("\r\n", "\n"));
        Assert.Contains("if (Item?.ContentId == contentId)\n                IsWatched = wasWatched;", viewModel.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SeriesAndEpisodeDetailsFollowCurrentNavigationAndActionSemantics()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var landscape = Read("src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs");

        Assert.Contains("EpisodesSection.Visibility = Visibility.Collapsed", page);
        Assert.Contains("Navigate<ItemDetailPage>(season.ContentId)", page);
        Assert.Contains("item.Type is \"season\" or \"episode\"", page);
        Assert.Contains("\"series\" => \"Mark Series Unwatched\"", page);
        Assert.Contains("\"season\" => \"Mark Season Unwatched\"", page);
        Assert.Contains("EpisodeContextText.Text = $\"S{item.SeasonNumber}", page);
        Assert.Contains("ItemSource = \"episode_carousel\"", page);
        Assert.Contains("nav.Navigate<ItemDetailPage>(MediaItem.ContentId)", landscape);
        Assert.Contains("private void OnHeadingTapped", landscape);
        Assert.Contains("headingIsSeries ? MediaItem.SeriesId! : MediaItem.ContentId", landscape);
        Assert.Contains("private void OnMetadataTapped", landscape);
        Assert.Contains("CurrentItemBorder.Visibility = CurrentItemBadge.Visibility", landscape);
        Assert.Contains("EpisodeWatchedBadge.Visibility", landscape);
    }

    [Fact]
    public void DetailHeroUsesThemeGradientsResponsiveStackingAndKeyboardTargets()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");

        Assert.Contains("UpdateThemeGradientColors();", page);
        Assert.Contains("Application.Current.Resources[\"AppBackgroundColor\"]", page);
        Assert.Contains("var stackedHero = width < 1024", page);
        Assert.Contains("Grid.SetRow(HeroInfoPanel, stackedHero && !hidesPoster ? 1 : 0)", page);
        Assert.Contains("HeroContentGrid.ColumnSpacing = hidesPoster ? 0 : 24", page);
        Assert.Contains("HeroContentGrid.RowSpacing = stackedHero && !hidesPoster ? 24 : 0", page);
        Assert.Contains("HeroContentGrid.MaxWidth = Math.Max(0, 1520 - (heroGutter * 2))", page);
        Assert.Contains("DetailSkeletonHeroContent.MaxWidth = Math.Max(0, 1520 - (heroGutter * 2))", page);
        Assert.Equal(2, xaml.Split("<Grid.RowDefinitions>").Length - 1);
        Assert.Contains("KeyDown=\"HorizontalCarousel_KeyDown\"", xaml);
        Assert.Contains("x:Name=\"DetailErrorContent\"", xaml);
        Assert.Contains("Item not found.", xaml);
        Assert.Contains("AutomationProperties.SetName(button", page);
        Assert.Contains("Margin=\"8,24,0,0\"", xaml);
        Assert.Contains("Width=\"32\" Height=\"32\" CornerRadius=\"16\"", xaml);
        Assert.Contains("BackButton.Margin = new Thickness(8, width >= 640 ? 24 : 16", page);
        Assert.Contains("border-b border-border/10", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Opacity=\"0.1\"", xaml);
    }

    [Fact]
    public void EpisodeMoreEpisodesCarouselAlignsTheCurrentEpisodeToTheWebUiStartSnap()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var landscapeCard = Read("src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs");

        Assert.Contains("currentEpisodeIndex", page);
        Assert.Contains("AlignSiblingEpisodeToStart(currentEpisodeIndex)", page);
        Assert.Contains("_pendingSiblingEpisodeIndex = episodeIndex", page);
        Assert.Contains("SiblingEpisodesScrollViewer.UpdateLayout()", page);
        Assert.Contains("TryAlignSiblingEpisodeToStart()", page);
        Assert.Contains("SiblingEpisodesScrollViewer.ChangeView(targetOffset", page);
        Assert.Contains("scrollTo(currentEpisodeIndex)", page);
        Assert.Contains("_pendingSiblingEpisodeIndex * (cardWidth + gap)", page);
        Assert.DoesNotContain("cardCenter - viewport / 2d", page);
        Assert.Contains("x:Name=\"SiblingEpisodesPanel\"", xaml);
        Assert.Contains("Margin=\"16,0,0,0\"", xaml);
        Assert.Contains("Text=\"More Episodes\" Style=\"{StaticResource TitleTextStyle}\" FontSize=\"20\"", xaml);
        Assert.Contains("x:Name=\"SiblingEpisodesPrevButton\"", xaml);
        Assert.Contains("x:Name=\"SiblingEpisodesNextButton\"", xaml);
        Assert.Contains("Click=\"SiblingEpisodesPrev_Click\"", xaml);
        Assert.Contains("Click=\"SiblingEpisodesNext_Click\"", xaml);
        Assert.Contains("PointerPressed=\"SiblingEpisodesScrollViewer_PointerPressed\"", xaml);
        Assert.Contains("PointerMoved=\"SiblingEpisodesScrollViewer_PointerMoved\"", xaml);
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", xaml);
        Assert.Contains("ScrollSiblingEpisodes(-1)", page);
        Assert.Contains("ScrollSiblingEpisodes(1)", page);
        Assert.Contains("SiblingEpisodesScrollViewer.CapturePointer", page);
        Assert.Contains("UpdateSiblingEpisodeScrollButtons", page);
        Assert.Contains("SiblingEpisodesPrevButton.Visibility = canScrollPrev", page);
        Assert.Contains("SiblingEpisodesNextButton.Visibility = canScrollNext", page);
        Assert.Contains("item.ItemSource == \"episode_carousel\"", landscapeCard);
        Assert.Contains("? \"still\"", landscapeCard);
    }

    [Fact]
    public void HeroMetadataAndScoresUseTheCurrentWebPresentation()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");

        Assert.Contains("CornerRadius=\"999\" Padding=\"10,4\"", xaml);
        Assert.Contains("x:Name=\"ScoresPanel\" Orientation=\"Horizontal\" Spacing=\"20\"", xaml);
        Assert.Contains("x:Name=\"ImdbScoreText\" FontSize=\"15\" FontWeight=\"Bold\"", xaml);
        Assert.DoesNotContain("x:Name=\"TmdbScorePanel\"", xaml);
        Assert.Contains("item.Type.Equals(\"episode\"", page);
        Assert.Contains("? item.RatingImdb ?? item.RatingTmdb", page);
        Assert.Contains(": item.RatingImdb", page);
        Assert.Contains("RuntimeText.Text = \"\"", page);
        Assert.Contains("MetaDot2.Visibility = Visibility.Collapsed", page);
    }

    [Fact]
    public void HeroArtworkKeepsAStableTitleFallbackAndUsesCachedLogoBytes()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("TitleText.Visibility = Visibility.Visible", page);
        Assert.Contains("LoadHeroLogoAsync(item.ContentId, item.LogoUrl, imageToken)", page);
        Assert.Contains("\"logo\",", page);
        Assert.Contains("ViewModel.Item?.ContentId != contentId", page);
        Assert.Contains("StudioKickerText.Text = kicker.ToUpperInvariant()", page);
    }

    [Fact]
    public void EpisodeRepaintPreservesWatchControlsAndMatchesMetadataSemantics()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("ArrangeMetadataBadges(item.Type)", page);
        Assert.Contains("MetaPanel.Children.Insert(0, RuntimeBadge)", page);
        Assert.Contains("YearText.Text = YearText.Text.ToUpperInvariant()", page);
        Assert.Contains("$\"{minutes / 60}H {minutes % 60}M\"", page);
        Assert.Contains("DateTimeStyles.AssumeUniversal", page);
        Assert.Contains("DateTimeDisplay.FormatDate(parsed, medium: true)", page);
        Assert.Contains("string.Equals(_watchDetail.ContentId, _playableContentId", page);
        Assert.Contains("UpdatePlayButton();", page);
        Assert.Contains("BuildMediaLocationsSection(canCurateMetadata, _watchDetail.Versions)", page);
        Assert.Contains("UnderlineStyle = UnderlineStyle.None", page);
        Assert.Contains("Navigate<PersonDetailPage>(personId)", page);
        Assert.Contains("nav.Navigate<HomePage>();", page);
        Assert.Contains("showCollectionActions: false", page);
        Assert.DoesNotContain("Resources[\"SurfaceBorderBrush\"]", page);
        Assert.DoesNotContain("Executive Producer\", StringComparison.OrdinalIgnoreCase", page);
        Assert.Contains("var episodeAirDate = FormatDetailDate(episode.AirDate)", page);
        Assert.Contains("DefaultLeafPlayLabel(item.Type)", page);
        Assert.Contains("? \"Play Episode\"", page);
        Assert.Contains("userData.WatchedCount > 0 || userData.InProgressCount > 0", page);
        Assert.Contains("$\"{userData!.WatchedCount} of {season.EpisodeCount} episodes\"", page);
        Assert.Contains("FormatSeasonProgressText(season)", page);
    }

    [Fact]
    public void DetailBackButtonFallsBackToHomeLikeCurrentPageBack()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var web = ReadWeb("web", "src", "components", "PageBack.tsx");
        var webTest = ReadWeb("web", "src", "components", "PageBack.test.tsx");

        Assert.Contains("to = \"/\"", web);
        Assert.Contains("falls back to the default route when there is no router history", webTest);
        Assert.Contains("if (nav.CanGoBack)", page);
        Assert.Contains("nav.GoBack();", page);
        Assert.Contains("nav.Navigate<HomePage>();", page);
    }

    [Fact]
    public void DetailActionsExposeTheSameAccessibleNamesAsTheWebUi()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");

        Assert.Contains("x:Name=\"PrimaryPlayButton\"", xaml);
        Assert.Contains("Padding=\"12,10\" CornerRadius=\"22\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding Text, ElementName=PlayButtonText}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding Text, ElementName=WatchedText}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Favorite\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Rating\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"1 star\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"5 stars\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"More\"", xaml);
        Assert.Contains("AutomationProperties.SetName(FavoriteButton", page);
        Assert.Contains("KeyDown=\"Star_KeyDown\"", xaml);
        Assert.Contains("starButtons[i].IsTabStop = i + 1 == tabbableStar", page);
        Assert.Contains("Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFA, 0xCC, 0x15)", page);
        Assert.DoesNotContain("Application.Current.Resources[\"AccentHoverBrush\"]", page);
        Assert.Contains("Windows.System.VirtualKey.Home", page);
        Assert.Contains("Windows.System.VirtualKey.End", page);
        Assert.Contains("AutomationProperties.SetItemStatus", page);
    }

    [Fact]
    public void SeriesPaintsSeasonResultsBeforeContinueWatchingFinishes()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var loadStart = page.IndexOf("await ViewModel.LoadSeasonsCommand.ExecuteAsync(null);", StringComparison.Ordinal);
        var cardPaint = page.IndexOf("BuildSeasonCards();", loadStart, StringComparison.Ordinal);
        var resumeAwait = page.IndexOf("var resumeEpisode = await resumeEpisodeTask;", loadStart, StringComparison.Ordinal);

        Assert.True(loadStart >= 0);
        Assert.True(cardPaint > loadStart);
        Assert.True(resumeAwait > cardPaint);
        Assert.Contains("SeasonsLoadingSkeleton.Visibility = Visibility.Visible", page);
        Assert.Contains("SeasonsLoadError.Visibility", page);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException();
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }

    private static string ReadWeb(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException();
        return File.ReadAllText(Path.Combine([dir, ".codex-tmp", "silo-server-current", .. parts]));
    }
}
