namespace SiloPlayer.Tests;

public sealed class CurrentHomeParitySourceTests
{
    [Fact]
    public void CachedHomeCardsRestoreArtworkAfterNavigationReload()
    {
        var landscape = ReadRepoFile("src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs");
        var poster = ReadRepoFile("src", "SiloPlayer", "Controls", "PosterCard.xaml.cs");

        Assert.Contains("this.Loaded +=", landscape);
        Assert.Contains("BackdropImage.Source == null", landscape);
        Assert.Contains("LoadImageAsync(item, _loadCts.Token)", landscape);
        Assert.Contains("this.Loaded +=", poster);
        Assert.Contains("PosterImage.Source == null", poster);
        Assert.Contains("!DeferImageLoading", poster);
        Assert.Contains("LoadPosterAsync(item, _loadCts.Token)", poster);
    }

    [Fact]
    public void CustomizeHomeOpensTheActualHomeScreenSettingsSurface()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml.cs");

        Assert.Contains("Frame.Navigate(typeof(SettingsPage), \"HomeScreen\")", source);
        Assert.DoesNotContain("Frame.Navigate(typeof(SettingsPage), \"Home\")", source);
    }

    [Fact]
    public void EveryHomeStaticResourceIsDefinedByThePageOrApplicationTheme()
    {
        var home = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml");
        var theme = ReadRepoFile("src", "SiloPlayer", "Themes", "DarkTheme.xaml");
        var referenced = System.Text.RegularExpressions.Regex.Matches(
                home,
                @"\{StaticResource\s+([^}\s]+)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var defined = System.Text.RegularExpressions.Regex.Matches(
                home + Environment.NewLine + theme,
                "x:Key=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(referenced.Where(key => !defined.Contains(key)));
    }

    [Fact]
    public void HomeUsesCurrentHeroTastePromptAndEmptyState()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml.cs");
        var web = ReadWebUiFile("web", "src", "components", "TasteSeedBanner.tsx");

        Assert.Contains("x:Name=\"HeroLoadingSkeleton\"", xaml);
        Assert.Contains("x:Name=\"HeroErrorPanel\"", xaml);
        Assert.Contains("Personalize your home", xaml);
        Assert.Contains("Pick a few titles you love and we'll tailor your recommendations.", xaml);
        Assert.Contains("text-sm font-semibold sm:text-base", web);
        Assert.Contains("text-xs sm:text-sm", web);
        Assert.Contains("x:Name=\"TasteSeedTitle\"", xaml);
        Assert.Contains("x:Name=\"TasteSeedDescription\"", xaml);
        Assert.Contains("TasteSeedTitle.FontSize = isCompact ? 14 : 16", code);
        Assert.Contains("TasteSeedDescription.FontSize = isCompact ? 12 : 14", code);
        Assert.Contains("Customize Home Screen", xaml);
        Assert.DoesNotContain("Watch Tonight", xaml);
        Assert.Contains("TasteSeedBannerDismissedProfileIds", code);
        Assert.Contains("RetrySectionAsync", code);
        Assert.Contains("else if (!heroSection.LoadCompleted)", code);
        Assert.Contains("var hasRenderableHero", code);
    }

    [Fact]
    public void SectionRowsUseCurrentBrowsePinRetryAndDismissContracts()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml.cs");
        var home = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml.cs");
        var library = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("FontSize=\"20\"", xaml);
        Assert.Contains("x:Name=\"PinSectionBtn\"", xaml);
        Assert.Contains("AutomationProperties.SetName(PinSectionBtn, label)", code);
        Assert.Contains("This section could not be loaded right now.", xaml);
        Assert.DoesNotContain("RefreshSectionBtn", xaml);
        Assert.Contains("\"custom_filter\"", code);
        Assert.Contains("\"random\"", code);
        Assert.DoesNotContain("\"popular\"", code);
        Assert.Contains("OnRetry", code);
        Assert.Contains("Scope: \"home\"", home);
        Assert.Contains("Scope: \"library\"", library);
        Assert.Contains("LibraryId = libraryId", library);
    }

    [Fact]
    public void SectionRowsSupportWebStyleDragKeyboardAndResponsivePageScrolling()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml.cs");

        Assert.Contains("IsTabStop=\"True\"", xaml);
        Assert.Contains("KeyDown=\"CardsScrollViewer_KeyDown\"", xaml);
        Assert.Contains("PointerMoved=\"CardsScrollViewer_PointerMoved\"", xaml);
        Assert.Contains("CardsScrollViewer.CapturePointer", code);
        Assert.Contains("CardsScrollViewer.ViewportWidth * 0.82", code);
        Assert.Contains("case VirtualKey.Left", code);
        Assert.Contains("case VirtualKey.Right", code);
    }

    [Fact]
    public void SectionRowsUseCurrentWebCarouselEdgeFades()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml");
        var web = ReadWebUiFile("web", "src", "components", "MediaCarousel.tsx");

        Assert.Contains("w-10 bg-gradient-to-r", web);
        Assert.Contains("w-11", web);
        Assert.Contains("x:Name=\"LeftFadeGradient\"", xaml);
        Assert.Contains("x:Name=\"RightFadeGradient\"", xaml);
        Assert.Contains("Width=\"40\"", xaml);
        Assert.Contains("Width=\"44\"", xaml);
        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.DoesNotContain("Width=\"64\"", xaml);
    }

    [Fact]
    public void HomeLoadingSkeletonKeepsTheHeroFullBleedAndRowsResponsive()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml.cs");

        Assert.Contains("x:Name=\"LoadingSkeletonPanel\" Padding=\"0,16,0,24\"", xaml);
        Assert.Contains("x:Name=\"LoadingSectionOne\" Padding=\"48,0\"", xaml);
        Assert.Contains("x:Name=\"HeroLoadingSkeleton\" Height=\"380\" Margin=\"0,0,0,40\"", xaml);
        Assert.Contains("x:Name=\"HeroCarouselControl\" Margin=\"0,0,0,40\"", xaml);
        Assert.Contains("LoadingSectionOne.Padding = sectionPadding", code);
        Assert.Contains("e.NewSize.Width >= 1280 ? 48d", code);
        Assert.Contains("e.NewSize.Width >= 1024 ? 40d", code);
        Assert.Contains("SetResponsiveWidth", code);
    }

    [Fact]
    public void HomeHeroKeepsCurrentWebBorderAndStackRhythm()
    {
        var home = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml");
        var hero = ReadRepoFile("src", "SiloPlayer", "Controls", "HeroCarousel.xaml");

        Assert.Contains("x:Name=\"HeroCarouselControl\" Margin=\"0,0,0,40\"", home);
        Assert.Contains("border-b border-border/60", hero, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Height=\"1\"", hero);
        Assert.Contains("Opacity=\"0.6\"", hero);
    }

    [Fact]
    public void SectionCatalogNavigationPreservesStoredRecipeScope()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "CatalogPage.xaml.cs");
        var shell = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("section_id=", api);
        Assert.Contains("&scope=", api);
        Assert.Contains("_source == \"section\"", page);
        Assert.Contains("FilterPanel.Visibility = Visibility.Collapsed", page);
        Assert.Contains("SectionId: pinTag.PinId", shell);
        Assert.Contains("IsSidebarPin", shell);
    }

    [Fact]
    public void FailedHomeSectionsRemainRetryableInsteadOfDisappearing()
    {
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Home", "HomeSectionsResponse.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "HomeViewModel.cs");

        Assert.Contains("LoadFailed", model);
        Assert.Contains("CloneSection(section, loadFailed: true, loadCompleted: true)", viewModel);
        Assert.Contains("public async Task RetrySectionAsync", viewModel);
        Assert.Contains("LoadCompleted", model);
        Assert.Contains("completed.LoadCompleted = true", viewModel);
        Assert.DoesNotContain("RemoveFromBoundCollection(sectionId)", viewModel);
    }

    [Fact]
    public void HomeRetainsItsVisualTreeAndCoalescesLayoutRefreshes()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "HomePage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "HomeViewModel.cs");

        Assert.Contains("NavigationCacheMode = NavigationCacheMode.Required", page);
        Assert.Contains("AttachViewModelEvents();", page);
        Assert.Contains("_layoutChangedWhileRefreshing", page);
        Assert.Contains("if (_isRefreshingLayout)", page);
        Assert.Contains("Math.Clamp(e.NewSize.Height * ratio, 350, 700)", page);
        Assert.Contains("if (_loadInProgress) return;", viewModel);
        Assert.Contains("IsLoading = !hadContent;", viewModel);
    }

    [Fact]
    public void ContinueWatchingCardsUseCurrentWideMenuOverlayAndMetadataLayout()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "LandscapeCard.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs");
        var menu = ReadRepoFile("src", "SiloPlayer", "Controls", "MediaItemMenu.cs");

        Assert.Contains("Width=\"315\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"More actions\"", xaml);
        Assert.Contains("x:Name=\"OverlayTopLeft\"", xaml);
        Assert.Contains("x:Name=\"TimeLeftText\"", xaml);
        Assert.Contains("Tapped=\"OnPlayTapped\"", xaml);
        Assert.Contains("Navigate<ItemDetailPage>(MediaItem.ContentId)", code);
        Assert.Contains("Navigate<EbookReaderPage>", code);
        Assert.Contains("ToggleAudiobookPlayback", code);
        Assert.Contains("item.BackdropUrl", code);
        Assert.Contains("PosterCard.BuildBadge", code);
        Assert.Contains("item.ProgressUpdatedAt", menu);
        Assert.Contains("MediaSurfaceChangeKind.HomeDismissed", menu);
    }

    [Fact]
    public void AdminServerActivityUsesTheCurrentActiveScanAndBoundedFlyoutContract()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "ServerActivityButton.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "ServerActivityButton.xaml.cs");

        Assert.Contains("MaxHeight=\"400\"", xaml);
        Assert.Contains("MaxActivityScanRows = 25", code);
        Assert.Contains("run.Status is \"accepted\" or \"running\"", code);
        Assert.DoesNotContain("run.Status is \"accepted\" or \"queued\"", code);
        Assert.Contains("_lastActiveScans.Take(MaxActivityScanRows)", code);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = FindRepositoryRoot();
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    private static string ReadWebUiFile(params string[] parts)
    {
        var all = new string[parts.Length + 3];
        all[0] = FindRepositoryRoot();
        all[1] = ".codex-tmp";
        all[2] = "silo-server-current";
        Array.Copy(parts, 0, all, 3, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
