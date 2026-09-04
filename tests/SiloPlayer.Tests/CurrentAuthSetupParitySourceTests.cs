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
