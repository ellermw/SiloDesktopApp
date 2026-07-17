using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class UpgradeAuthenticationRegressionTests
{
    [Theory]
    [InlineData(" HTTPS://Example.COM/ ", "https://example.com")]
    [InlineData("https://Example.COM:443/silo/", "https://example.com/silo")]
    [InlineData("http://Example.COM:80/", "http://example.com")]
    public void ServerUrlIdentity_NormalizesCredentialScope(string input, string expected)
    {
        Assert.Equal(expected, ServerUrlIdentity.Normalize(input));
    }

    [Fact]
    public void SavedSessionResolver_MigratesLegacyUrlScopedCredentialsWithoutLosingProfile()
    {
        var credentials = new MemoryCredentialStore();
        const string legacy = "HTTPS://Example.COM/";
        credentials.SaveCredential(legacy, "refresh_token", "refresh-value");
        credentials.SaveCredential(legacy, "profile_id", "profile-id");
        credentials.SaveCredential(legacy, "profile_token", "profile-token");
        credentials.SaveCredential(legacy, "access_token", "legacy-access");

        var resolved = new SavedSessionCredentialResolver(credentials).Resolve(legacy);

        Assert.NotNull(resolved);
        Assert.Equal("https://example.com", resolved.ServerUrl);
        Assert.Equal("refresh-value", resolved.RefreshToken);
        Assert.True(resolved.MigratedLegacyCredential);
        Assert.Equal("refresh-value", credentials.LoadCredential(resolved.ServerUrl, "refresh_token"));
        Assert.Equal("profile-id", credentials.LoadCredential(resolved.ServerUrl, "profile_id"));
        Assert.Equal("profile-token", credentials.LoadCredential(resolved.ServerUrl, "profile_token"));
        Assert.Null(credentials.LoadCredential(legacy, "refresh_token"));
        Assert.Null(credentials.LoadCredential(legacy, "access_token"));
    }

    [Fact]
    public void UnauthenticatedShellCannotRevealStaleServerNavigation()
    {
        var mainWindow = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("private bool CanExposeAuthenticatedNavigation", mainWindow);
        Assert.Contains("_authService.IsLoggedIn", mainWindow);
        Assert.Contains("!string.IsNullOrWhiteSpace(_authService.SelectedProfileId)", mainWindow);
        Assert.Contains("NavView.IsPaneOpen = false;", mainWindow);
        Assert.Contains("RemoveDynamicLibraryNavItems();", mainWindow);
        Assert.Contains("NavView.IsPaneVisible = CanExposeAuthenticatedNavigation;", mainWindow);
        Assert.Contains("if (!CanExposeAuthenticatedNavigation)", mainWindow);
        Assert.Contains("BuildPluginApps([]);", mainWindow);
    }

    [Fact]
    public void UpgradeClosesOldProcessAndApplicationRejectsConcurrentSessionOwner()
    {
        var installer = ReadRepoFile("installer", "SiloInstaller.iss");
        var app = ReadRepoFile("src", "SiloPlayer", "App.xaml.cs");

        Assert.Contains("CloseApplications=force", installer);
        Assert.Contains("RestartApplications=no", installer);
        Assert.Contains("SiloDesktopPlayer-6F4EE0EA-4DA3-49D0-940D-461F977BA343", app);
        Assert.Contains("secondary_instance_blocked", app);
    }

    [Fact]
    public void StartupRestoreSearchesSavedServersAndRetriesTransientFailures()
    {
        var mainWindow = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");
        var hardenedStart = mainWindow.IndexOf("private async Task TryAutoLoginAsync()", StringComparison.Ordinal);
        var hardenedEnd = mainWindow.IndexOf("private bool _navInitialized", StringComparison.Ordinal);
        var hardened = mainWindow[hardenedStart..hardenedEnd];

        Assert.Contains("settings.Servers.OrderByDescending(entry => entry.LastUsed)", hardened);
        Assert.Contains("new SavedSessionCredentialResolver(_credentialStore)", hardened);
        Assert.Contains("attempt <= 2", hardened);
        Assert.Contains("refresh_terminal", hardened);
        Assert.Contains("CompleteAutoLoginAsync", hardened);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private sealed class MemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<(string Server, string Key), string> _values = [];

        public void SaveCredential(string serverUrl, string key, string value) =>
            _values[(serverUrl, key)] = value;

        public string? LoadCredential(string serverUrl, string key) =>
            _values.TryGetValue((serverUrl, key), out var value) ? value : null;

        public void DeleteCredential(string serverUrl, string key) =>
            _values.Remove((serverUrl, key));

        public void DeleteAllForServer(string serverUrl)
        {
            foreach (var key in _values.Keys.Where(item => item.Server == serverUrl).ToList())
                _values.Remove(key);
        }
    }
}
