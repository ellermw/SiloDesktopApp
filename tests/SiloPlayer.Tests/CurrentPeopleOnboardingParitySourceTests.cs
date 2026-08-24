namespace SiloPlayer.Tests;

public sealed class CurrentPeopleOnboardingParitySourceTests
{
    [Fact]
    public void PersonAdminMutationsUseCurrentPeopleEndpoints()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "PersonDetailPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "PersonDetailViewModel.cs");

        Assert.Contains("/api/v1/admin/people/{Uri.EscapeDataString(personId)}/refresh", api);
        Assert.Contains("/api/v1/admin/people/{Uri.EscapeDataString(personId)}", api);
        Assert.Contains("adminApi.UpdatePersonAsync", page);
        Assert.DoesNotContain("UpdateItemMetadataAsync(person.Id", page);
        Assert.Contains("_adminApi.RefreshPersonAsync(Person.Id)", viewModel);
        Assert.Contains("Person refresh queued.", viewModel);
    }

    [Fact]
    public void PersonAndTasteSeedPagingCannotApplyAfterAReplacementRequest()
    {
        var person = ReadRepoFile("src", "SiloPlayer", "ViewModels", "PersonDetailViewModel.cs");
        var taste = ReadRepoFile("src", "SiloPlayer", "ViewModels", "TasteSeedViewModel.cs");

        Assert.Contains("Interlocked.Exchange(ref _filmographyCts, cts)", person);
        Assert.Contains("ReferenceEquals(_filmographyCts, cts)", person);
        Assert.Contains("ViewModel.Cancel();", ReadRepoFile("src", "SiloPlayer", "Views", "PersonDetailPage.xaml.cs"));
        Assert.Contains("Interlocked.Exchange(ref _loadMoreCts, ownerCts)", taste);
        Assert.Contains("ReferenceEquals(_loadMoreCts, ownerCts)", taste);
        Assert.Contains("HasReachedEnd", taste);
    }

    [Fact]
    public void PersonDetailUsesCurrentBadgeHeaderAndResponsiveFilmographyGrid()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "PersonDetailPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "PersonDetailPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "PersonDetailViewModel.cs");
        var customization = ReadRepoFile("src", "SiloPlayer", "Services", "UICustomizationService.cs");

        Assert.Contains("x:Name=\"PersonContentShell\"", xaml);
        Assert.Contains("MaxWidth=\"1400\"", xaml);
        Assert.Contains("x:Name=\"PersonMetadataBadges\"", xaml);
        Assert.Contains("x:Name=\"BirthDateBadge\"", xaml);
        Assert.Contains("x:Name=\"DeathDateBadge\"", xaml);
        Assert.Contains("x:Name=\"BirthplaceBadge\"", xaml);
        Assert.Contains("FontSize=\"30\"", xaml);
        Assert.Contains("x:Name=\"FilmographyGridLayout\"", xaml);
        Assert.Contains("FilmographyRepeater_ElementPrepared", page);
        Assert.Contains("_uiCustomizationService.GetPosterColumnCount(innerWidth)", page);
        Assert.Contains("_ => contentWidth >= 1280 ? 8", customization);
        Assert.Contains("nav.Navigate<HomePage>();", page);
        Assert.Contains("public string BirthDateDisplay", viewModel);
        Assert.Contains("public string DeathDateDisplay", viewModel);
    }

    [Fact]
    public void PersonEditorIncludesEveryCurrentWebUiMetadataField()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "PersonDetailPage.xaml.cs");

        Assert.Contains("AddField(\"Homepage\"", page);
        Assert.Contains("AddField(\"TMDB ID\"", page);
        Assert.Contains("AddField(\"IMDb ID\"", page);
        Assert.Contains("AddField(\"TVDB ID\"", page);
        Assert.Contains("AddStringChange(changes, \"homepage\"", page);
        Assert.Contains("AddStringChange(changes, \"tmdb_id\"", page);
        Assert.Contains("AddStringChange(changes, \"imdb_id\"", page);
        Assert.Contains("AddStringChange(changes, \"tvdb_id\"", page);
    }

    [Fact]
    public void InvitationAndHouseholdSetupUseCurrentNativeContract()
    {
        var authApi = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AuthApi.cs");
        var deepLink = ReadRepoFile("src", "SiloPlayer", "Helpers", "InviteDeepLink.cs");
        var invite = ReadRepoFile("src", "SiloPlayer", "Views", "InviteClaimPage.xaml");
        var household = ReadRepoFile("src", "SiloPlayer", "Views", "HouseholdSetupPage.xaml");
        var householdPage = ReadRepoFile("src", "SiloPlayer", "Views", "HouseholdSetupPage.xaml.cs");
        var installer = ReadRepoFile("installer", "SiloInstaller.iss");

        Assert.Contains("/api/v1/invitations/{Uri.EscapeDataString(token)}", authApi);
        Assert.Contains("/accept", authApi);
        Assert.Contains("uri.Scheme, \"silo\"", deepLink);
        Assert.Contains("uri.Host, \"invite\"", deepLink);
        Assert.Contains("x:Name=\"WelcomeTitle\"", invite);
        Assert.Contains("Invitation expired", invite);
        Assert.Contains("Who’s watching?", household);
        Assert.Contains("ProfileEditorDialog.ShowAsync", householdPage);
        Assert.Contains("Software\\Classes\\silo", installer);
    }

    [Fact]
    public void ProfileSelectionEntersHomeBeforeOptionalTasteSeedLookupCompletes()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "ProfileSelectViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "ProfileSelectPage.xaml.cs");

        var selected = viewModel.IndexOf("ProfileSelected?.Invoke();", StringComparison.Ordinal);
        var favorites = viewModel.IndexOf("GetFavoritesAsync(cts.Token)", StringComparison.Ordinal);
        Assert.True(selected >= 0 && favorites > selected);
        Assert.Contains("TasteSeedRequired?.Invoke();", viewModel);
        Assert.Contains("navigation.Frame?.Content is not HomePage", page);
        Assert.Contains("ViewModel.CancelProfileLoad();", page);
    }

    [Fact]
    public void TasteSeedUsesCurrentWidthAndReturnsToPlaybackSettings()
    {
        var markup = ReadRepoFile("src", "SiloPlayer", "Views", "TasteSeedPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "TasteSeedPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "TasteSeedViewModel.cs");

        Assert.Contains("MaxWidth=\"1152\"", markup);
        Assert.Contains("MaximumRowsOrColumns=\"7\"", markup);
        Assert.Contains("That's everything popular on this server.", markup);
        Assert.Contains("ItemsSource=\"{x:Bind SkeletonItems}\"", markup);
        Assert.Contains("Enumerable.Range(0, 24)", page);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind AccessibleName, Mode=OneWay}\"", markup);
        Assert.Contains("Deselect", viewModel);
        Assert.Contains("typeof(SettingsPage), \"Playback\"", page);
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
