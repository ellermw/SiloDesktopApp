using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SiloCastDefaultTests
{
    [Fact]
    public void FreshAndMissingPreferencesEnableCastButExplicitOffIsPreserved()
    {
        Assert.True(new AppSettings().SiloCastEnabled);
        var dir = Path.Combine(Path.GetTempPath(), "SiloPlayer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");
            Assert.True(new SettingsService(dir).Load().SiloCastEnabled);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"siloCastEnabled\":false}");
            Assert.False(new SettingsService(dir).Load().SiloCastEnabled);
        }
        finally { File.Delete(Path.Combine(dir, "settings.json")); Directory.Delete(dir); }
    }
}
