using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ContinuumPlayerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void LoadSettings_NoFile_ReturnsDefaults()
    {
        var settings = _service.Load();
        Assert.NotNull(settings);
        Assert.Empty(settings.Servers);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var settings = new AppSettings
        {
            Servers = [new ServerEntry { Url = "https://example.com", Name = "Test", LastUsed = DateTime.UtcNow }],
            LastProfileId = "abc-123"
        };
        _service.Save(settings);
        var loaded = _service.Load();
        Assert.Single(loaded.Servers);
        Assert.Equal("https://example.com", loaded.Servers[0].Url);
        Assert.Equal("abc-123", loaded.LastProfileId);
    }

    [Fact]
    public void AddServer_AddsToList()
    {
        _service.AddServer("https://server1.com", "Server 1");
        _service.AddServer("https://server2.com", "Server 2");
        var settings = _service.Load();
        Assert.Equal(2, settings.Servers.Count);
    }

    [Fact]
    public void RemoveServer_RemovesByUrl()
    {
        _service.AddServer("https://server1.com", "Server 1");
        _service.AddServer("https://server2.com", "Server 2");
        _service.RemoveServer("https://server1.com");
        var settings = _service.Load();
        Assert.Single(settings.Servers);
        Assert.Equal("https://server2.com", settings.Servers[0].Url);
    }
}
