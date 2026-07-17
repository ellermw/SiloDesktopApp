namespace SiloPlayer.Tests;

public sealed class CurrentAuthSetupParitySourceTests
{
    [Fact]
    public void SignupUsesAuthoritativeClosedStateAndBootstrapsTheOnlyUnlockedProfile()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SignupViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SignupPage.xaml");

        Assert.Contains("ShowSignupClosed", viewModel);
        Assert.Contains("IsCheckingSignupStatus", viewModel);
        Assert.Contains("IsSignupEnabled = true;", viewModel);
        Assert.Contains("profiles.Count == 1 && !profiles[0].HasPin", viewModel);
        Assert.Contains("Invite code is required.", viewModel);
        Assert.Contains("Public signups are currently closed.", page);
        Assert.Contains("Passwords do not match", page);
    }

    [Fact]
    public void DeviceApprovalLookupIsLatestWinsAndTokenLinksCannotSwitchCodes()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "ActivateDeviceViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "ActivateDevicePage.xaml");
        var codeBehind = ReadRepoFile("src", "SiloPlayer", "Views", "ActivateDevicePage.xaml.cs");

        Assert.Contains("Interlocked.Exchange(ref _loadCts, cts)", viewModel);
        Assert.Contains("ReferenceEquals(_loadCts, cts)", viewModel);
        Assert.Contains("CanEnterAnotherCode", viewModel);
        Assert.Contains("DeviceDisplayName", viewModel);
        Assert.Contains("ViewModel.CanEnterAnotherCode", page);
        Assert.Contains("ViewModel.CancelLoad();", codeBehind);
    }

    [Fact]
    public void SetupUsesCurrentEightStepOrderAndPersistsCurrentServerFeatures()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SetupWizardViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SetupWizardPage.xaml");
        var codeBehind = ReadRepoFile("src", "SiloPlayer", "Views", "SetupWizardPage.xaml.cs");

        Assert.Contains("account, profile, server, integrations", viewModel);
        Assert.Contains("case 7:", viewModel);
        Assert.Contains("SubmitLibraryStepAsync", viewModel);
        Assert.Contains("CreateNodeAsync", viewModel);
        Assert.Contains("download.server_bandwidth_mbps", viewModel);
        Assert.Contains("recommendations.embedding_base_url", viewModel);
        Assert.Contains("GetSubtitleProvidersAsync", viewModel);
        Assert.Contains("jellyfin_compat.web_install_dir", viewModel);
        Assert.DoesNotContain("SubmitMetadataStepAsync", viewModel);
        Assert.Contains("Nodes &amp; finish", page);
        Assert.Contains("Add a worker node", page);
        Assert.Contains("Start using Silo", page);
        Assert.Contains("Account\", \"Profile\", \"Server\", \"Integrations\", \"Downloads\", \"Recs\", \"Library\", \"Finish", codeBehind);
    }

    [Fact]
    public void SetupLibraryStepUsesCurrentReusableFormContract()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SetupWizardViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SetupWizardPage.xaml");

        Assert.Contains("AddedLibraries", viewModel);
        Assert.Contains("MetadataLanguage = LibraryMetadataLanguage", viewModel);
        Assert.Contains("AutoTranslateMetadata = LibraryAutoTranslateMetadata", viewModel);
        Assert.Contains("ChapterThumbnailsEnabled", viewModel);
        Assert.Contains("IntroDetectionEnabled", viewModel);
        Assert.Contains("TrailerKinds = trailerKinds", viewModel);
        Assert.Contains("GetLibraryProviderDefaultsAsync", viewModel);
        Assert.Contains("UpdateLibraryProvidersAsync", viewModel);
        Assert.Contains("MoveLibraryProvider", viewModel);
        Assert.Contains("Add at least one library or choose Skip.", viewModel);
        Assert.Contains("Content=\"Audiobooks\" Tag=\"audiobooks\"", page);
        Assert.Contains("Content=\"Ebooks\" Tag=\"ebooks\"", page);
        Assert.Contains("Content=\"Manga\" Tag=\"manga\"", page);
        Assert.Contains("Content=\"Podcasts\" Tag=\"podcasts\"", page);
        Assert.Contains("Auto-translate descriptions", page);
        Assert.Contains("Trailer &amp; extras types", page);
        Assert.Contains("Generate chapter thumbnails", page);
        Assert.Contains("Detect intro markers", page);
        Assert.Contains("Provider Priority", page);
        Assert.Contains("BrowseLibraryFolders_Click", page);
        Assert.Contains("Command=\"{x:Bind ViewModel.AddLibraryCommand}\"", page);
        Assert.Contains("Command=\"{x:Bind ViewModel.SkipLibraryCommand}\"", page);
        Assert.DoesNotContain("Tag=\"music\"", page);
        Assert.DoesNotContain("Tag=\"live-tv\"", page);
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
