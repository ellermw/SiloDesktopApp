using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SettingsPersistenceSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "silo-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void TruncatedSettingsFileFallsBackToDefaultsAndIsPreserved()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"siloCastEnabled\":false,\"servers\":[{\"url\":\"https://exa");

        var settings = new SettingsService(_directory).Load();

        Assert.True(settings.SiloCastEnabled);
        Assert.False(File.Exists(SettingsPath));
        Assert.StartsWith("{\"siloCastEnabled\":false", File.ReadAllText(SettingsPath + ".corrupt"));
    }

    [Fact]
    public void FailedReplacementPreservesThePreviousFileAndNextSaveCanRecover()
    {
        var service = new SettingsService(_directory);
        var settings = service.Load();
        service.Save(settings);
        var previous = File.ReadAllBytes(SettingsPath);
        using (var held = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            settings.UiTextScale = "large";
            var failure = Record.Exception(() => service.Save(settings));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                "The destination is locked against replacement; the save must fail without truncating it.");
            Assert.Equal(previous, File.ReadAllBytes(SettingsPath));
        }
        service.Save(settings);
        Assert.Equal("large", new SettingsService(_directory).Load().UiTextScale);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void SaveReplacesTheFileWithoutLeavingATemporaryCopy()
    {
        var service = new SettingsService(_directory);
        var settings = service.Load();
        settings.SiloCastEnabled = false;
        service.Save(settings);
        settings.UiTextScale = "large";
        service.Save(settings);

        var reloaded = new SettingsService(_directory).Load();
        Assert.False(reloaded.SiloCastEnabled);
        Assert.Equal("large", reloaded.UiTextScale);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void StaleTemporaryFileFromAnInterruptedSaveDoesNotReplaceSettings()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"siloCastEnabled\":false}");
        File.WriteAllText(SettingsPath + ".tmp", "{\"siloCast");

        Assert.False(new SettingsService(_directory).Load().SiloCastEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
