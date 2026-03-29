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

    [Fact]
    public void Load_ReturnsSameInstance_OnRepeatedCalls()
    {
        var first = _service.Load();
        var second = _service.Load();
        Assert.Same(first, second);
    }

    [Fact]
    public void Save_InvalidatesCache_SoNextLoadReturnsUpdatedData()
    {
        var original = _service.Load();
        Assert.Null(original.LastProfileId);

        var updated = new AppSettings { LastProfileId = "new-profile" };
        _service.Save(updated);

        var afterSave = _service.Load();
        Assert.Equal("new-profile", afterSave.LastProfileId);
    }

    [Fact]
    public void AddServer_PreventseDuplicates()
    {
        _service.AddServer("https://server1.com", "Server 1");
        _service.AddServer("https://server1.com", "Server 1 Again");
        var settings = _service.Load();
        Assert.Single(settings.Servers);
        Assert.Equal("Server 1", settings.Servers[0].Name);
    }

    [Fact]
    public void AddServer_PersistsAndReflectedInLoad()
    {
        _service.AddServer("https://server1.com", "Server 1");
        var settings = _service.Load();
        Assert.Single(settings.Servers);
        Assert.Equal("https://server1.com", settings.Servers[0].Url);
        Assert.Equal("Server 1", settings.Servers[0].Name);
    }

    [Fact]
    public void RemoveServer_PersistsRemoval()
    {
        _service.AddServer("https://server1.com", "Server 1");
        _service.AddServer("https://server2.com", "Server 2");
        _service.RemoveServer("https://server1.com");

        // Create a fresh service to prove it persisted to disk
        var freshService = new SettingsService(_tempDir);
        var settings = freshService.Load();
        Assert.Single(settings.Servers);
        Assert.Equal("https://server2.com", settings.Servers[0].Url);
    }

    [Fact]
    public void UpdateLastUsed_UpdatesTimestamp()
    {
        _service.AddServer("https://server1.com", "Server 1");
        var before = _service.Load().Servers[0].LastUsed;

        // Small delay to ensure timestamp differs
        Thread.Sleep(10);
        _service.UpdateLastUsed("https://server1.com");

        var after = _service.Load().Servers[0].LastUsed;
        Assert.True(after > before);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenNoFileExists()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "ContinuumPlayerTest_Empty_" + Guid.NewGuid().ToString("N"));
        try
        {
            var freshService = new SettingsService(emptyDir);
            var settings = freshService.Load();
            Assert.NotNull(settings);
            Assert.Empty(settings.Servers);
            Assert.Null(settings.LastProfileId);
            Assert.Null(settings.LastTheme);
            Assert.Empty(settings.HiddenLibraryIds);
        }
        finally
        {
            if (Directory.Exists(emptyDir))
                Directory.Delete(emptyDir, true);
        }
    }
}
