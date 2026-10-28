namespace SiloPlayer.Tests;

public sealed class SubtitlePreferenceFallbackSourceTests
{
    // SettingsViewModel is registered transient: resolving it outside the
    // Settings page yields a fresh, never-loaded instance whose defaults
    // (ShowForcedSubtitles = false, empty language/mode) silently replaced the
    // intended fallbacks and hid forced subtitles.
    [Theory]
    [InlineData("Services", "PlayerService.cs")]
    [InlineData("Views", "ItemDetailPage.xaml.cs")]
    public void SubtitleFallbacksReadTheSelectedProfileNotATransientSettingsViewModel(string folder, string file)
    {
        var source = ReadRepoFile("src", "SiloPlayer", folder, file);
        Assert.DoesNotContain("GetService<ViewModels.SettingsViewModel>()", source);
        Assert.DoesNotContain("GetService<SiloPlayer.ViewModels.SettingsViewModel>()", source);
        Assert.DoesNotContain("GetService<SettingsViewModel>()", source);
        Assert.DoesNotContain("GetRequiredService<SettingsViewModel>()", source);
        Assert.Contains("SelectedProfile", source);
        Assert.Contains("showForced ??= profile.ShowForcedSubtitles;", source);
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
