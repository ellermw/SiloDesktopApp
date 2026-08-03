using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DesktopSidebarPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"silo-sidebar-settings-{Guid.NewGuid():N}");

    [Fact]
    public void SidebarDefaultsOpenAndPersistsAnExplicitClose()
    {
        var service = new SettingsService(_directory);
        var settings = service.Load();
        Assert.True(settings.DesktopSidebarOpen);

        settings.DesktopSidebarOpen = false;
        service.Save(settings);

        Assert.False(new SettingsService(_directory).Load().DesktopSidebarOpen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
