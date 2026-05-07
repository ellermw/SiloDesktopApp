namespace ContinuumPlayer.Tests;

public sealed class WatchProvidersParitySourceTests
{
    [Fact]
    public void SettingsIncludesWatchProvidersTabAndPanel()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SettingsPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("WatchProvidersTab", xaml);
        Assert.Contains("WatchProvidersPanel", xaml);
        Assert.Contains("WatchProvidersCardsPanel", xaml);
        Assert.Contains("LoadWatchProvidersAsync", code);
    }

    [Fact]
    public void WatchProvidersApiMirrorsWebUiEndpoints()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "WatchProvidersApi.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "SettingsViewModel.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "App.xaml.cs"));

        Assert.Contains("/api/v1/watch-providers", api);
        Assert.Contains("/connection", api);
        Assert.Contains("/auth/device-code", api);
        Assert.Contains("/auth/poll", api);
        Assert.Contains("/auth/api-key", api);
        Assert.Contains("/sync-runs", api);
        Assert.Contains("WatchProviderCards", vm);
        Assert.Contains("WatchProvidersApi", app);
    }

    [Fact]
    public void WatchProvidersSupportApiKeyAuthMethod()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "WatchProvidersApi.cs"));
        var models = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "WatchProviders", "WatchProviderModels.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "SettingsViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("AuthMethod", models);
        Assert.Contains("api_key", models);
        Assert.Contains("ConnectApiKeyAsync", api);
        Assert.Contains("ConnectWatchProviderApiKeyAsync", vm);
        Assert.Contains("ApiKeyPromptVisible", page);
        Assert.Contains("BuildWatchProviderApiKeyPanel", page);
        Assert.Contains("if (vm.UsesApiKey)", page);
        Assert.Contains("vm.ApiKeyPromptVisible = true;", page);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "ContinuumPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
