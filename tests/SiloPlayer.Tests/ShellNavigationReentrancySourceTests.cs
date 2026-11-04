namespace SiloPlayer.Tests;

public sealed class ShellNavigationReentrancySourceTests
{
    [Fact]
    public void NavigatedHandlerDefersSidebarMenuSwap()
    {
        var window = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs").ReplaceLineEndings("\n");
        var handler = MethodBody(window, "private void OnNavigated_SynchronizeShellChrome(");

        // Navigated runs synchronously inside NavView.ItemInvoked; replacing
        // MenuItems there removes the container NavigationView is dispatching.
        Assert.Contains("QueueSettingsSidebarSynchronization();", handler);
        Assert.DoesNotContain("SynchronizeSettingsSidebar();", handler);
        Assert.DoesNotContain("RestoreAppSidebar();", handler);
        Assert.DoesNotContain("RebuildSettingsSidebar();", handler);

        var sidebar = ReadRepoFile("src", "SiloPlayer", "MainWindow.SettingsSidebar.cs").ReplaceLineEndings("\n");
        var queue = MethodBody(sidebar, "private void QueueSettingsSidebarSynchronization(");
        Assert.Contains("DispatcherQueue.TryEnqueue(", queue);
        Assert.Contains("if (_settingsSidebarSynchronizationQueued) return;", queue);
    }

    [Fact]
    public void ReconnectKeepsHealthyDetailsButStillSchedulesDeferredShellHydrationAndRetriesFailedRoutes()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs").ReplaceLineEndings("\n");
        var catchUp = MethodBody(source, "private void OnReadCatchUpRequested(");
        var detailStart = catchUp.IndexOf("if (ContentFrame.Content is ItemDetailPage detail)", StringComparison.Ordinal);
        Assert.True(detailStart >= 0);
        var detailBranch = MethodBody(catchUp[detailStart..], "if (ContentFrame.Content is ItemDetailPage detail)");
        Assert.Contains("_pendingDetailCatchUp = true;", detailBranch);
        Assert.Contains("_pendingDetailCatchUpRequiresRemount |= !detail.CanPreserveOnReconnect;", detailBranch);
        Assert.Contains("ApplyPendingAccessRefresh();", detailBranch);

        var apply = MethodBody(source, "private void ApplyPendingAccessRefresh(");
        Assert.Contains("!_pendingAccessRefresh && !_pendingDetailCatchUp", apply);
        Assert.Contains("PlayerState.Expanded or PlayerState.Fullscreen", apply);
        Assert.Contains("var preserveDetail = !_pendingAccessRefresh", apply);
        Assert.Contains("!_pendingDetailCatchUpRequiresRemount", apply);
        Assert.Contains("_pendingDetailCatchUpRequiresRemount = false;", apply);
        Assert.Contains("detail.CanPreserveOnReconnect", apply);
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var healthStart = page.IndexOf("public bool CanPreserveOnReconnect =>", StringComparison.Ordinal);
        Assert.True(healthStart >= 0);
        var health = page[healthStart..page.IndexOf(';', healthStart)];
        Assert.Contains("ViewModel.Item != null", health);
        Assert.Contains("ViewModel.Item.Type is \"series\" or \"season\"", health);
        Assert.Contains("!ViewModel.SimilarLoadFailed", health);
        Assert.Contains("ViewModel.FailureKind == DetailFailureKind.None", health);
        Assert.Contains("!ViewModel.IsLoading", health);
        Assert.Contains("!ViewModel.IsSeasonsLoading", health);
        Assert.Contains("!ViewModel.IsEpisodesLoading", health);
        Assert.Contains("_seasonEpisodesLoads == 0", health);
        foreach (var member in new[] { "!_isTranslatingEpisodes", "_episodesTranslationStatusReads == 0", "!_episodesTranslationInterrupted",
            "!_isTranslatingOverview", "_overviewTranslationStatusReads == 0", "!_overviewTranslationInterrupted",
            "_detailArtworkLoads == 0", "!_detailArtworkInterrupted" })
            Assert.Contains(member, health);
        Assert.Contains("!ViewModel.SeasonsLoadFailed", health);
        Assert.Contains("!ViewModel.EpisodesLoadFailed", health);
        Assert.Contains("EpisodesLoadError.Visibility != Visibility.Visible", health);
        Assert.Contains("SeasonEpisodesFatalError.Visibility != Visibility.Visible", health);
        Assert.Contains("if (!preserveDetail) ShowMainNavigation();", apply);
        Assert.Contains("LoadShellNavigationAsync(shellKey, token)", apply);
        Assert.Contains("RefreshUserNavigationCapabilitiesAsync(shellKey, token)", apply);
        // Box sets also reconcile in place; failed non-box-set routes still remount.
        Assert.Contains("if (!preserveDetail && ContentFrame.Content is not BoxSetPage)\n            TryShellAction(\"access_active_route\"", apply);
        Assert.Contains("_navigationService.RefreshCurrentEntry()", apply);
    }

    [Fact]
    public void UnpaintedArtworkAndRetiredOverviewWorkRetainRemountRecovery()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        foreach (var (method, navigation, border, revision) in new[]
        {
            ("private async Task LoadSeasonPosterAsync(", "navigation", "posterBorder", "revision"),
            ("private async Task LoadEpisodeStillAsync(", "navigationOwner", "stillBorder", "stillRevision")
        })
        {
            var load = MethodBody(page, method);
            Assert.Contains("++_detailArtworkLoads", load);
            Assert.Contains("finally", load);
            Assert.Contains("--_detailArtworkLoads", load);
            Assert.Contains("completed = imageService.IsKnownUnavailable", load);
            Assert.Contains("completed = true", load);
            Assert.Contains($"ReferenceEquals(_navigationCts, {navigation}) && Equals({border}.Tag, {revision})", load);
            Assert.Contains($"if (completed) _interruptedCardArtwork.Remove({border})", load);
            Assert.Contains($"else _interruptedCardArtwork.Add({border})", load);
        }
        var configure = MethodBody(page, "private async Task ConfigureOnViewTranslationAsync(");
        Assert.Contains("++_overviewTranslationStatusReads", configure);
        Assert.Contains("--_overviewTranslationStatusReads", configure);
        Assert.Contains("_overviewTranslationInterrupted = true", configure);
        var departed = MethodBody(page, "protected override void OnNavigatedFrom(");
        Assert.Contains("_isTranslatingOverview = false", departed);
        Assert.Contains("_overviewTranslationInterrupted = false", departed);
        Assert.Contains("_detailArtworkInterrupted = false", departed);
    }

    [Fact]
    public void RetainedMenuMutationsKeepTheirBuildTimeIdentityAcrossReadContextChanges()
    {
        var interest = ReadRepoFile("src", "SiloPlayer", "Controls", "NotInterestedActions.cs");
        var create = MethodBody(interest, "public static MenuFlyoutItem CreateMenuItem(");
        Assert.True(create.IndexOf("CaptureIdentityContext()", StringComparison.Ordinal)
            < create.IndexOf("action.Click +=", StringComparison.Ordinal));
        var click = create[create.IndexOf("action.Click +=", StringComparison.Ordinal)..];
        Assert.True(click.IndexOf("!client.IsCurrentIdentityContext(owner)", StringComparison.Ordinal)
            < click.IndexOf("await ToggleAsync(item)", StringComparison.Ordinal));
        var menu = MethodBody(ReadRepoFile("src", "SiloPlayer", "Controls", "MediaItemMenu.cs"), "public static MenuFlyout Build(");
        Assert.Contains("var menuOwner = client.CaptureIdentityContext();", menu);
        var reset = menu[menu.IndexOf("var reset = BuildItem", StringComparison.Ordinal)..];
        Assert.True(reset.IndexOf("!client.IsCurrentIdentityContext(menuOwner)", StringComparison.Ordinal)
            < reset.IndexOf("await MediaItemCardActions.ResetProgressAsync(item)", StringComparison.Ordinal));
        Assert.Contains("reset.IsEnabled = false", reset);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidOperationException("Unbalanced method body: " + signature);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
