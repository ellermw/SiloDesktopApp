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

        Assert.Contains("MaxWidth=\"1152\"", markup);
        Assert.Contains("MaximumRowsOrColumns=\"7\"", markup);
        Assert.Contains("That's everything popular on this server.", markup);
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
