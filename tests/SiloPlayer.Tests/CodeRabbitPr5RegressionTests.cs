namespace SiloPlayer.Tests;

public sealed class CodeRabbitPr5RegressionTests
{
    [Fact]
    public void ImageDownloadsPublishTheWinningLazyBeforeStartingIt()
    {
        var source = Read("src", "SiloPlayer.Core", "Services", "ImageService.cs");

        var publish = source.IndexOf("var winningDownload = _inflightDownloads.GetOrAdd", StringComparison.Ordinal);
        var start = source.IndexOf("var task = winningDownload.Value", StringComparison.Ordinal);
        Assert.True(publish >= 0 && start > publish);
        Assert.Contains("RemoveInflightDownload(inflightKey, winningDownload)", source);
    }

    [Fact]
    public void DetailQuickWatchedActionAlwaysRestoresItsButtonAndReportsUnexpectedFailures()
    {
        var source = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("catch (Exception ex)\n            {\n                App.Services.GetRequiredService<Services.ToastService>().Error(ex.Message);", Normalize(source));
        Assert.Contains("finally\n            {\n                quickWatchedButton.IsEnabled = true;", Normalize(source));
    }

    [Fact]
    public void LibraryRecommendationRefreshExcludesOnlyTheSelectedHero()
    {
        var source = Read("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("results.Where(result => !ReferenceEquals(result.Layout, heroLayout))", source);
        Assert.DoesNotContain("results.Where(result => !result.Layout.Featured)", source);
    }

    [Fact]
    public void CardQuickActionsAlwaysReleaseTheirPendingState()
    {
        foreach (var source in new[]
                 {
                     Read("src", "SiloPlayer", "Controls", "LibraryGridCard.cs"),
                     Read("src", "SiloPlayer", "Controls", "PosterCard.xaml.cs"),
                 })
        {
            Assert.True(Count(source, "finally") >= 2);
            Assert.Equal(2, Count(source, "_quickActionPending = false;"));
            Assert.Equal(2, Count(source, "SetQuickActionsEnabled(true);"));
        }
    }

    [Fact]
    public void WatchTonightOnlyAddsImagesWhenConversionProducedASource()
    {
        var source = Read("src", "SiloPlayer", "Controls", "WatchTonightDialog.xaml.cs");

        Assert.Contains("is ImageSource backdropSource", source);
        Assert.Contains("is ImageSource logoSource", source);
        Assert.DoesNotContain("Source = (ImageSource)RemoteImageConverter.Convert", source);
    }

    [Fact]
    public void BrandingLoadsRejectOlderWorkWithinTheSameShellHydration()
    {
        var source = Read("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("private readonly AsyncLoadVersionGate _brandingLoadGate = new();", source);
        Assert.Contains("var brandingLoadVersion = _brandingLoadGate.BeginNextLoad();", source);
        Assert.Contains("_brandingLoadGate.IsCurrent(brandingLoadVersion)", source);
        Assert.Contains("_brandingLoadGate.Cancel();", source);
    }

    [Fact]
    public void MetadataMutationsInvalidateDetailCacheBeforeReloading()
    {
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "ItemDetailViewModel.cs");
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        Assert.Contains("public Task ReloadAsync(string contentId)", viewModel);
        Assert.Contains("_detailPrefetchCache.Invalidate(contentId);", viewModel);
        Assert.Contains("await ViewModel.ReloadAsync(item.ContentId);", page);
    }

    [Fact]
    public void RecommendationRealtimePayloadIsValidatedBeforeReadingProperties()
    {
        var source = Read("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("data.ValueKind == JsonValueKind.Object\n            && data.TryGetProperty", Normalize(source));
    }

    [Fact]
    public void EbookChapterNavigationUsesTheCentralReaderHostPolicy()
    {
        var source = Read("src", "SiloPlayer", "Views", "EbookReaderPage.xaml.cs");

        Assert.Contains("EbookReaderWebPolicy.ReaderHost", source);
        Assert.DoesNotContain("https://silo-reader.local/{path}", source);
    }

    [Fact]
    public void MangaWatchedActionContainsFailuresAndAlwaysRestoresItsButton()
    {
        var source = Normalize(Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        var method = SliceMethod(source, "private async void MangaWatched_Click", "private async void MangaDownload_Click");

        Assert.Contains("Button { Tag: MangaChapter chapter } button", method);
        Assert.Contains("button.IsEnabled = false;", method);
        Assert.Contains("catch (Exception ex)", method);
        Assert.Contains("ToastService>().Error(ex.Message)", method);
        Assert.Contains("finally", method);
        Assert.Contains("button.IsEnabled = true;", method);
    }

    [Fact]
    public void HiddenPosterActionsCannotInterceptCardInput()
    {
        var source = Normalize(Read("src", "SiloPlayer", "Controls", "PosterCard.xaml.cs"));
        var method = SliceMethod(source, "private void RevealCardActions", "private void OnCardGotFocus");

        Assert.Contains("MoreButton.IsHitTestVisible = reveal;", method);
        Assert.Contains("QuickActions.IsHitTestVisible = reveal;", method);
    }

    [Fact]
    public void MangaAndTranslationReloadsCannotRepaintAReusedDetailPage()
    {
        var source = Normalize(Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        var manga = SliceMethod(source, "private async void MangaWatched_Click", "private async void MangaDownload_Click");
        var translation = SliceMethod(source, "private async Task TranslateOverviewAsync", "private void UpdateScoresRow");

        Assert.Contains("var navigationToken = _navigationCts?.Token", manga);
        Assert.True(Count(manga, "IsActiveDetail(parentContentId, navigationToken.Value)") >= 2);
        Assert.Contains("MarkWatchedAsync(chapter.ContentId, navigationToken.Value)", manga);
        Assert.Contains("MarkUnwatchedAsync(chapter.ContentId, navigationToken.Value)", manga);

        Assert.True(Count(translation, "IsActiveDetail(item.ContentId, ct)") >= 2);
        var reload = translation.IndexOf("await ViewModel.ReloadAsync(item.ContentId);", StringComparison.Ordinal);
        var postReloadGuard = translation.IndexOf("IsActiveDetail(item.ContentId, ct)", reload, StringComparison.Ordinal);
        Assert.True(reload >= 0 && postReloadGuard > reload);
        Assert.Contains("IsCurrentDetail(item.ContentId)", translation);
    }

    [Fact]
    public void EpisodeActionLayerHitTestingTracksItsVisibleState()
    {
        var source = Normalize(Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        var method = SliceMethod(source, "private FrameworkElement CreateEpisodeCard", "private void AddEpisodeCardOverlays");

        Assert.Contains("void SetActionLayerVisibility(bool reveal)", method);
        Assert.Contains("actionLayer.Opacity = reveal ? 1 : 0;", method);
        Assert.Contains("actionLayer.IsHitTestVisible = reveal;", method);
        Assert.DoesNotContain("IsHitTestVisible = true", method);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static string Normalize(string source) => source.Replace("\r\n", "\n");

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string Read(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory)
               && !File.Exists(Path.Combine(directory, "SiloPlayer.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("Could not locate repository root.");

        return File.ReadAllText(Path.Combine([directory, .. parts]));
    }
}
