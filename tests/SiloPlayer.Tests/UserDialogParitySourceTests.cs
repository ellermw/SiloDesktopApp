namespace SiloPlayer.Tests;

public sealed class UserDialogParitySourceTests
{
    [Fact]
    public void DownloadPickerKeepsTheDialogOpenUntilAFileIsActuallySaved()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var picker = Slice(source, "private async Task ShowDownloadDialogAsync", "private async Task<bool> SaveDirectDownloadAsync");
        var save = Slice(source, "private async Task<bool> SaveDirectDownloadAsync", "// ===== Match (admin)");

        Assert.Contains("foreach (var button in versionButtons) button.IsEnabled = false", picker);
        Assert.Contains("IsActive = true", picker);
        Assert.Contains("var saved = await SaveDirectDownloadAsync", picker);
        Assert.Contains("if (saved)", picker);
        var savedCheck = picker.IndexOf("if (saved)", StringComparison.Ordinal);
        var hideDialog = picker.IndexOf("dialog.Hide();", StringComparison.Ordinal);
        Assert.True(savedCheck >= 0 && hideDialog > savedCheck);
        Assert.DoesNotContain("dialog.Hide();\n                await SaveDirectDownloadAsync", picker);
        Assert.Contains("if (file == null) return false", save);
        Assert.Contains("toast.Success(\"Download saved\")", save);
        Assert.Contains("return true", save);
        Assert.Contains("return false", save);
    }

    [Fact]
    public void AddToCollectionUsesWebStyleSelectableRowsWithoutNativeRadioGlyphs()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Controls", "AddToCollectionDialog.xaml.cs");
        var markup = ReadRepoFile("src", "SiloPlayer", "Controls", "AddToCollectionDialog.xaml");

        Assert.Contains("Text=\"Loading collections…\"", markup);
        Assert.Contains("You don't have any manual collections yet", markup);
        Assert.Contains("var button = new Button", source);
        Assert.DoesNotContain("new RadioButton", source);
        Assert.Contains("_selectedButton.Background", source);
        Assert.Contains("IsPrimaryButtonEnabled = false", source);
        Assert.Contains("args.Cancel = true", source);
        Assert.Contains("ItemAdded?.Invoke()", source);
        Assert.Contains("Collections could not be loaded.", source);
        Assert.Contains("_collectionsApi.GetCollectionsAsync", source);
        Assert.DoesNotContain("AdminApi", source);
    }

    [Fact]
    public void MangaFileInspectorIncludesTheWebUiLoadingFailureAndEmptyStates()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Controls", "MangaFilesDialog.cs");

        Assert.Contains("Local files backing this series.", source);
        Assert.Contains("Couldn't load file details. Try again later.", source);
        Assert.Contains("No files found.", source);
        Assert.Contains("GetMangaSeriesFilesAsync", source);
        Assert.Contains("FolderPaths", source);
        Assert.Contains("FileRowLabel", source);
        Assert.Contains("FormatFileSize", source);
    }

    [Fact]
    public void ProfileEditorUsesTheCurrentWebUiSelectionAndPendingPatterns()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "ProfileEditorDialog.cs");

        Assert.Contains("private readonly Grid _avatarStyleGrid", source);
        Assert.Contains("AvatarStyleSummary", source);
        Assert.Contains("private readonly Dictionary<int, ToggleSwitch> _libraryChecks", source);
        Assert.DoesNotContain("private readonly Dictionary<int, CheckBox> _libraryChecks", source);
        Assert.Contains("PIN will be removed", source);
        Assert.Contains("Keep existing PIN", source);
        Assert.Contains("args.Cancel = _saving", source);
        Assert.Contains("sender.PrimaryButtonText = \"Saving...\"", source);
        Assert.Contains("Choose at least one library.", source);
    }

    [Fact]
    public void ProfilePinPromptsAutoFocusAndUseTheCurrentWebUiErrors()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "ProfileSelectPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "ProfileSelectViewModel.cs");

        Assert.Contains("dialog.Opened += (_, _) => pinBox.Focus(FocusState.Programmatic)", page);
        Assert.Contains("PinErrorMessage = \"Incorrect PIN\"", viewModel);
        Assert.Contains("PinErrorMessage = \"Verification failed\"", viewModel);
    }

    [Fact]
    public void ManualMatchKeepsResultsScrollableAndTheApplyActionPinned()
    {
        var markup = ReadRepoFile("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml");
        var source = ReadRepoFile("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml.cs");

        Assert.Contains("<ScrollViewer Grid.Row=\"0\"", markup);
        Assert.Contains("x:Name=\"ApplyMatchButton\"", markup);
        Assert.Contains("Grid.Row=\"1\"", markup);
        Assert.Contains("No folder paths are available for this item.", source);
        Assert.Contains("Copied root path", source);
        Assert.Contains("Failed to copy path", source);
        Assert.Contains("if (_searching) return", source);
    }

    [Fact]
    public void PlayerSubtitleAiActionIsOnlyShownWhenTheCurrentWebUiWouldShowIt()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs");

        Assert.Contains("private SubtitleAiStatus? _subtitleAiStatus", source);
        Assert.Contains("if (CanShowSubtitleAi())", source);
        Assert.Contains("status.Enabled", source);
        Assert.Contains("IsTranslatableSubtitleSource", source);
        Assert.Contains("status.TranscribeEnabled", source);
        Assert.Contains("RefreshSubtitleAiCapabilityAsync", source);
    }

    [Fact]
    public void PlayerUtilityMenusUseOpaqueStructuredFlyoutsInsteadOfGenericWindowsMenus()
    {
        var markup = ReadRepoFile("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml");
        var source = ReadRepoFile("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs");

        Assert.Contains("x:Name=\"AudioListPanel\"", markup);
        Assert.Contains("x:Name=\"SubtitleListPanel\"", markup);
        Assert.Contains("x:Name=\"QualityListPanel\"", markup);
        Assert.Contains("x:Name=\"SpeedListPanel\"", markup);
        Assert.Contains("x:Name=\"SleepListPanel\"", markup);
        Assert.Contains("Background=\"#F0000000\"", markup);
        Assert.DoesNotContain("<MenuFlyout x:Name=\"SubtitleFlyout\"", markup);
        Assert.Contains("CreatePlayerMenuBadge(codecLabel)", source);
        Assert.Contains("CreatePlayerMenuBadge(channelLabel)", source);
        Assert.Contains("Text = \"DELAY\"", source);
        Assert.Contains("Subtitle delay 100ms earlier", source);
        Assert.Contains("Subtitle delay 100ms later", source);
        Assert.Contains("Reset subtitle delay", source);
        Assert.Contains("activeSubtitleIndex >= 0 && _subtitleDelayMs", source);
        Assert.Contains("captionsAction", source);
        Assert.Contains("SubtitleFlyout.Hide();", source);
        Assert.Contains("SubtitleButton.Focus(FocusState.Programmatic)", source);
    }

    [Fact]
    public void PrePlaySubtitlePopoverShowsTheCurrentWebUiDownloadedLoadingState()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var player = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");

        Assert.Contains("private bool _loadingDownloadedSubtitles", source);
        Assert.Contains("Text = \"Loading downloaded...\"", source);
        Assert.Contains("AddDownloadedSubtitlesLoadingState", source);
        Assert.Contains("_loadingDownloadedSubtitles = false", source);
        Assert.Contains("Text = \"No subtitles available.\"", source);
        Assert.Contains("SelectedProfile?.Language", source);
        Assert.Contains("ProfileLanguage: _authService.SelectedProfile?.Language", player);
    }

    [Fact]
    public void MediaInfoDialogRespondsToNarrowAndSnappedWindows()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var mediaInfo = Slice(source, "private async Task ShowMediaInfoDialogAsync", "private StackPanel BuildMediaInfoSpecSheet");

        Assert.Contains("Math.Clamp(availableWidth, 300, 640)", mediaInfo);
        Assert.Contains("Math.Clamp(availableHeight, 280, 680)", mediaInfo);
        Assert.DoesNotContain("MinWidth = 560", mediaInfo);
    }

    [Fact]
    public void HouseholdProfileSettingsKeepTheCurrentWebUiActiveStreamsLive()
    {
        var settings = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var authApi = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AuthApi.cs");

        Assert.Contains("GetHouseholdSessionsAsync", authApi);
        Assert.Contains("/api/v2/profiles/household/sessions", authApi);
        Assert.Contains("StartHouseholdSessionsPolling", settings);
        Assert.Contains("TimeSpan.FromSeconds(10)", settings);
        Assert.Contains("LoadHouseholdSessionsAsync(showLoading: false)", settings);
        Assert.Contains("No one is streaming right now.", settings);
        Assert.Contains("Active streams", settings);
        Assert.Contains("Playback happening on any profile in this account.", settings);
        Assert.Contains("session.Client", settings);
        Assert.Contains("session.IpAddress", settings);
        Assert.Contains("session.NodeDisplayName", settings);
    }

    [Fact]
    public void OnlineSubtitleSearchFollowsTheServerProviderCapability()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "PlaybackApi.cs");
        var models = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Playback", "SubtitleAiModels.cs");
        var overlay = ReadRepoFile("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs");

        Assert.Contains("SubtitleProviderStatus", models);
        Assert.Contains("GetSubtitleProviderStatusAsync", api);
        Assert.Contains("/api/v2/subtitles/providers/status", api);
        Assert.Contains("private SubtitleProviderStatus _subtitleProviderStatus", overlay);
        Assert.Contains("if (_subtitleProviderStatus.Enabled)", overlay);
        Assert.Contains("GetSubtitleProviderStatusAsync", overlay);
        Assert.Contains("ex.StatusCode == 404", overlay);
    }

    [Fact]
    public void CollectionTemplateImportsCarryTheCurrentDefaultSortChoice()
    {
        var models = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Collections", "CollectionImports.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "CollectionsViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs");

        Assert.Contains("public Dictionary<string, object>? SortConfig", models);
        Assert.Contains("SortConfig = request.SortConfig", viewModel);
        Assert.Contains("SortConfig = draft.SortConfig", viewModel);
        Assert.Contains("\"Default Sort\"", page);
        Assert.Contains("The order viewers get when they open this collection.", page);
        Assert.Contains("BuildCollectionSortConfig", page);
        Assert.Contains("__source_order", page);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
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
