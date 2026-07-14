namespace SiloPlayer.Tests;

public sealed class MainWindowSourceTests
{
    [Fact]
    public void PlayingNextOverlayShowsDedicatedCountdownBadge()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));

        Assert.Contains("PlayingNextBackdrop", xaml);
        Assert.Contains("PlayingNextCloseButton", xaml);
        Assert.Contains("PlayingNextCountdownPanel", xaml);
        Assert.Contains("PlayingNextAutoplayToggle", xaml);
        Assert.Contains("PlayingNextCountdownText", xaml);
        Assert.Contains("PlayingNextCountdownText.Text = $\"{_playingNextRemaining}s\";", code);
        Assert.Contains("PlayingNextCountdownRing.Value = _playingNextRemaining;", code);
        Assert.Contains("PlayingNextPlayNowText.Text = \"Play Now\";", code);
        Assert.Contains("PlayingNextOnDeckSection", xaml);
        Assert.Contains("PlayingNextOnDeckRepeater", xaml);
        Assert.Contains("PlayingNextOnDeckPrev", xaml);
        Assert.Contains("PlayingNextOnDeckNext", xaml);
        Assert.Contains("PlayingNextFinishedPanel", xaml);
        Assert.Contains("There are no more episodes available", xaml);
        Assert.Contains("LoadPlayingNextOnDeckAsync", code);
        Assert.Contains("section.SectionType, \"continue_watching\"", code);
        Assert.Contains("PlayingNextOnDeck_Click", code);
        Assert.Contains("playback.auto_play_next", code);
        Assert.Contains("GetPlayingNextAutoPlayAsync", code);
        Assert.Contains("PutDeviceSettingAsync", code);
        Assert.Contains("if (hasNextEpisode && _playingNextAutoPlay)", code);
        Assert.Contains("PlayingNextFinishedHeading.Text", code);
        Assert.Contains("PlayingNextOnDeckScroller.ChangeView", code);
    }

    [Fact]
    public void ApiClientSendsSiloDeviceHeadersForDeviceScopedSettings()
    {
        var root = FindRepositoryRoot();
        var client = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "SiloApiClient.cs"));
        var settingsApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "SettingsApi.cs"));
        var appSettings = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "ServerConfig.cs"));

        Assert.Contains("SetDeviceMetadata", client);
        Assert.Contains("X-Silo-Device-Id", client);
        Assert.Contains("X-Silo-Device-Name", client);
        Assert.Contains("X-Silo-Device-Platform", client);
        Assert.DoesNotContain("X-" + "Cont" + "inuum-Device-", client);
        Assert.Contains("GetEffectiveSettingsAsync", settingsApi);
        Assert.Contains("PutDeviceSettingAsync", settingsApi);
        Assert.Contains("DeviceId", appSettings);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
