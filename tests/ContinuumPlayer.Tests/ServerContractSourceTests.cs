using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Tests;

public sealed class ServerContractSourceTests
{
    [Fact]
    public void FileVersionExposesPerVersionMarkers()
    {
        Assert.NotNull(typeof(FileVersion).GetProperty("Intro"));
        Assert.NotNull(typeof(FileVersion).GetProperty("Credits"));
    }

    [Fact]
    public void LibraryExposesIntroDetectionFlag()
    {
        Assert.NotNull(typeof(Library).GetProperty("IntroDetectionEnabled"));
    }

    [Fact]
    public void AdminLibrariesEditorSavesIntroDetectionFlag()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Views",
            "Admin",
            "AdminLibrariesPage.xaml.cs"));

        Assert.Contains("Detect intro markers", source);
        Assert.Contains("intro_detection_enabled", source);
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
