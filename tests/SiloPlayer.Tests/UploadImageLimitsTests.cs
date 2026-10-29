using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class UploadImageLimitsTests
{
    [Fact]
    public void LimitsMatchTheServerUploadLimits()
    {
        // silo-server: collectionImageMaxBytes and profileAvatarMaxFileSize are both 10 << 20.
        Assert.Equal(10 << 20, CollectionArtworkLimits.MaximumBytes);
        Assert.Equal(10 << 20, ProfileAvatarLimits.MaximumBytes);
    }

    [Fact]
    public void SmartCollectionPostersUseTheCollectionArtworkLimitBeforeReading()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SmartCollectionWizardPage.xaml.cs");
        Assert.DoesNotContain("20 * 1024 * 1024", page, StringComparison.Ordinal);
        var reads = 0;
        for (var read = page.IndexOf("FileIO.ReadBufferAsync(file)", StringComparison.Ordinal); read >= 0;
             read = page.IndexOf("FileIO.ReadBufferAsync(file)", read + 1, StringComparison.Ordinal))
        {
            reads++;
            var check = page.LastIndexOf("> CollectionArtworkLimits.MaximumBytes", read, StringComparison.Ordinal);
            var previousRead = page.LastIndexOf("FileIO.ReadBufferAsync(file)", read - 1, StringComparison.Ordinal);
            Assert.True(check > previousRead, "A poster is read before the collection artwork limit is checked.");
        }
        Assert.Equal(2, reads);
    }

    [Fact]
    public void AvatarPickerChecksTheServerLimitBeforeReadingTheFile()
    {
        var dialog = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "ProfileEditorDialog.cs");
        var pick = dialog[dialog.IndexOf("private async Task PickAvatarAsync()", StringComparison.Ordinal)..];
        var check = pick.IndexOf("> ProfileAvatarLimits.MaximumBytes", StringComparison.Ordinal);
        var read = pick.IndexOf("FileIO.ReadBufferAsync(file)", StringComparison.Ordinal);
        Assert.True(check >= 0 && check < read);
        Assert.Contains("ShowValidation(ProfileAvatarLimits.OversizeMessage);", pick[check..read], StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "SiloPlayer", "App.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine([root!.FullName, .. parts]));
    }
}
